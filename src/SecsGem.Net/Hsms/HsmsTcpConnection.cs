using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace SecsGem.Net.Hsms;

/// <summary>
/// 基于 TCP 的 HSMS 连接（SEMI E37）完整实现：
/// - Active / Passive 两种模式
/// - Select 握手、LinkTest 保活、Separate 断开
/// - T3 回复超时检测与自动重发
/// - 主动模式断线自动重连（T5 间隔）
/// </summary>
public sealed class HsmsTcpConnection : HsmsConnection
{
    private readonly HsmsEndpoint _endpoint;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<uint, PendingReply> _pendingReplies = new();

    private TcpClient? _client;
    private TcpListener? _listener;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private uint _systemBytes;
    private int _connectingFlag;
    private bool _userDisconnect;
    private bool _disposed;

    public HsmsTcpConnection(HsmsEndpoint endpoint) => _endpoint = endpoint;

    /// <summary>建立连接（Active：连接 + Select 握手；Passive：开始监听等待对端）。</summary>
    public override async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _connectingFlag, 1, 0) != 0)
            throw new InvalidOperationException("连接流程已在执行中。");

        try
        {
            _userDisconnect = false;

            if (_endpoint.Mode == HsmsConnectionMode.Passive)
            {
                StartPassiveListener();
                return;
            }

            SetState(HsmsConnectionState.Connecting);

            _client = new TcpClient { NoDelay = true };
            await _client.ConnectAsync(_endpoint.IpAddress, _endpoint.Port, cancellationToken);
            _stream = _client.GetStream();
            _cts = new CancellationTokenSource();

            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token), CancellationToken.None);

            // 主动模式：发送 Select 请求，收到 SelectResponse 后进入 Selected
            await SendRawAsync(HsmsMessage.CreateSelectRequest(NextSystemBytes()), cancellationToken);
            SetState(HsmsConnectionState.NotSelected);

            StartLinkTestLoop(_cts.Token);
        }
        finally
        {
            Interlocked.Exchange(ref _connectingFlag, 0);
        }
    }

    public override async Task DisconnectAsync()
    {
        _userDisconnect = true;
        _cts?.Cancel();

        if (_stream is not null && _client?.Connected == true)
        {
            try
            {
                await SendRawAsync(HsmsMessage.CreateSeparateRequest(NextSystemBytes()), CancellationToken.None);
            }
            catch
            {
                // 对端已断开时忽略
            }
        }

        _listener?.Stop();
        _client?.Close();
        SetState(HsmsConnectionState.Disconnected);
    }

    /// <summary>发送数据消息；带 W-bit 的消息自动进入 T3 超时监控。</summary>
    public override async Task SendAsync(HsmsMessage message, CancellationToken cancellationToken = default)
    {
        if (State != HsmsConnectionState.Selected)
            throw new InvalidOperationException($"当前状态 {State} 不允许发送数据消息。");

        await SendRawAsync(message, cancellationToken);

        if (message.Header.IsDataMessage && message.Header.WaitBit)
            StartPendingReply(message);
    }

    // ---------- 内部实现 ----------

    private void StartPassiveListener()
    {
        SetState(HsmsConnectionState.Connecting);

        _listener = new TcpListener(IPAddress.Any, _endpoint.Port);
        _listener.Start();
        _cts = new CancellationTokenSource();

        // 注意：.NET 6 的 AcceptTcpClientAsync() 在 fire-and-forget 任务中会偶发
        // SocketException 995（I/O 中止），故使用同步 AcceptTcpClient() 循环，
        // 在后台线程中阻塞等待，行为稳定且兼容所有 .NET 版本。
        _ = Task.Run(AcceptLoop, CancellationToken.None);
        SetState(HsmsConnectionState.NotSelected);
    }

    private void AcceptLoop()
    {
        while (_cts?.IsCancellationRequested != true && _listener is not null)
        {
            TcpClient? client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (SocketException) when (_cts?.IsCancellationRequested == true)
            {
                break; // 主动关闭监听
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                RaiseError(ex);
                break;
            }

            if (client is null)
                break;

            client.NoDelay = true;
            _client = client;
            _stream = client.GetStream();

            // 等待对端发送 Select.Request，收到后由 HandleInboundAsync 回 SelectResponse
            _ = Task.Run(() => ReceiveLoopAsync(_cts!.Token), CancellationToken.None);
        }
    }

    private async Task SendRawAsync(HsmsMessage message, CancellationToken cancellationToken)
    {
        var frame = message.Encode();

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_stream is null)
                throw new InvalidOperationException("连接未建立。");

            await _stream.WriteAsync(frame, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private uint NextSystemBytes() => ++_systemBytes;

    // ---------- T3 超时监控 ----------

    private sealed class PendingReply
    {
        public PendingReply(HsmsMessage message) => Message = message;

        public HsmsMessage Message { get; }
        public int Attempts { get; set; }
        public CancellationTokenSource Cts { get; } = new();
    }

    private void StartPendingReply(HsmsMessage message)
    {
        var pending = new PendingReply(message);
        _pendingReplies[message.Header.SystemBytes] = pending;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_endpoint.Timeouts.T3), pending.Cts.Token);
            }
            catch (OperationCanceledException)
            {
                return; // 已收到回复
            }

            if (!_pendingReplies.TryRemove(message.Header.SystemBytes, out _))
                return;

            if (pending.Attempts < _endpoint.T3RetryCount && State == HsmsConnectionState.Selected)
            {
                pending.Attempts++;
                try
                {
                    await SendRawAsync(message, CancellationToken.None);
                    StartPendingReply(message); // 重新计时
                }
                catch (Exception ex)
                {
                    RaiseError(ex);
                }
            }
            else
            {
                RaiseError(new TimeoutException($"T3 超时：{message.Header}（已重试 {pending.Attempts} 次）"));
            }
        }, CancellationToken.None);
    }

    private void CompletePendingReply(uint systemBytes)
    {
        if (_pendingReplies.TryRemove(systemBytes, out var pending))
            pending.Cts.Cancel();
    }

    // ---------- LinkTest 保活 ----------

    private void StartLinkTestLoop(CancellationToken cancellationToken)
    {
        int interval = _endpoint.LinkTestIntervalSeconds;
        if (interval <= 0)
            return;

        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
                    if (State == HsmsConnectionState.Selected)
                        await SendRawAsync(HsmsMessage.CreateLinkTestRequest(NextSystemBytes()), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    RaiseError(ex);
                }
            }
        }, CancellationToken.None);
    }

    // ---------- 接收循环 ----------

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[HsmsMessage.LengthFieldSize];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream is not null)
            {
                await ReadFullAsync(_stream, lengthBuffer, cancellationToken);

                uint lengthInWords = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(lengthBuffer);
                int payloadLength = (int)lengthInWords * 4;

                var payload = new byte[payloadLength];
                await ReadFullAsync(_stream, payload, cancellationToken);

                var frame = new byte[HsmsMessage.LengthFieldSize + payloadLength];
                lengthBuffer.CopyTo(frame, 0);
                payload.CopyTo(frame, HsmsMessage.LengthFieldSize);

                var message = HsmsMessage.Decode(frame);
                await HandleInboundAsync(message, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            RaiseError(ex);
            CleanupSocket();
            SetState(HsmsConnectionState.Disconnected);

            // 主动模式：断线自动重连
            if (_endpoint.Mode == HsmsConnectionMode.Active && !_userDisconnect)
                _ = Task.Run(ReconnectLoopAsync, CancellationToken.None);
        }
    }

    private async Task HandleInboundAsync(HsmsMessage message, CancellationToken cancellationToken)
    {
        // 任何回复（数据或控制）都尝试匹配并完成 T3 监控
        CompletePendingReply(message.Header.SystemBytes);

        switch (message.Header.SType)
        {
            case HsmsSType.SelectResponse:
                // TODO: 校验 Select 状态码（Data[0]），0x00 为成功
                SetState(HsmsConnectionState.Selected);
                break;

            case HsmsSType.SelectRequest:
                // Passive 端：收到对端 Select 请求，回 SelectResponse 并进入 Selected
                SetState(HsmsConnectionState.Selected);
                await SendRawAsync(HsmsMessage.CreateSelectResponse(message.Header.SystemBytes), cancellationToken);
                break;

            case HsmsSType.LinkTestRequest:
                await SendRawAsync(HsmsMessage.CreateLinkTestResponse(message.Header.SystemBytes), cancellationToken);
                break;

            case HsmsSType.LinkTestResponse:
                break;

            case HsmsSType.SeparateRequest:
                SetState(HsmsConnectionState.NotSelected);
                break;

            case HsmsSType.DataMessage:
                RaiseMessageReceived(message);
                break;
        }
    }

    // ---------- 重连 ----------

    private async Task ReconnectLoopAsync()
    {
        while (!_userDisconnect && !_disposed)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(500, _endpoint.ReconnectIntervalMs)), CancellationToken.None);

            if (_userDisconnect || _disposed)
                return;

            try
            {
                await ConnectAsync();
                return; // 重连成功
            }
            catch (Exception ex)
            {
                RaiseError(ex);
            }
        }
    }

    private void CleanupSocket()
    {
        _cts?.Cancel();
        _stream?.Dispose();
        _stream = null;
        _client?.Dispose();
        _client = null;
    }

    private static async Task ReadFullAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("对端关闭了连接。");

            offset += read;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        _disposed = true;

        if (disposing)
        {
            _userDisconnect = true;
            _cts?.Cancel();

            foreach (var pending in _pendingReplies.Values)
                pending.Cts.Cancel();
            _pendingReplies.Clear();

            _listener?.Stop();
            _stream?.Dispose();
            _client?.Dispose();
            _sendLock.Dispose();
        }

        base.Dispose(disposing);
    }
}
