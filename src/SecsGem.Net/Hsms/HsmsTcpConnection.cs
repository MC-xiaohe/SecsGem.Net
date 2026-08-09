using System.Net.Sockets;

namespace SecsGem.Net.Hsms;

/// <summary>
/// 基于 TCP 的 HSMS 连接（SEMI E37）骨架实现。
/// 已包含：连接建立、Select 握手（主动模式）、接收循环、LinkTest 自动应答。
/// TODO：T3 回复超时管理、断线自动重连、Passive 模式监听。
/// </summary>
public sealed class HsmsTcpConnection : HsmsConnection
{
    private readonly HsmsEndpoint _endpoint;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private uint _systemBytes;

    public HsmsTcpConnection(HsmsEndpoint endpoint) => _endpoint = endpoint;

    public override async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        SetState(HsmsConnectionState.Connecting);

        _client = new TcpClient { NoDelay = true };
        await _client.ConnectAsync(_endpoint.IpAddress, _endpoint.Port, cancellationToken);
        _stream = _client.GetStream();
        _cts = new CancellationTokenSource();

        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token), CancellationToken.None);

        if (_endpoint.Mode == HsmsConnectionMode.Active)
        {
            // 主动模式：发送 Select 请求，等待对端 SelectResponse（由接收循环置为 Selected）
            await SendRawAsync(HsmsMessage.CreateSelectRequest(NextSystemBytes()), cancellationToken);
            SetState(HsmsConnectionState.NotSelected);
        }
        else
        {
            SetState(HsmsConnectionState.NotSelected);
        }
    }

    public override async Task DisconnectAsync()
    {
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

        _client?.Close();
        SetState(HsmsConnectionState.Disconnected);
    }

    public override async Task SendAsync(HsmsMessage message, CancellationToken cancellationToken = default)
    {
        if (State != HsmsConnectionState.Selected)
            throw new InvalidOperationException($"当前状态 {State} 不允许发送数据消息。");

        await SendRawAsync(message, cancellationToken);
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
            SetState(HsmsConnectionState.Disconnected);
        }
    }

    private async Task HandleInboundAsync(HsmsMessage message, CancellationToken cancellationToken)
    {
        switch (message.Header.SType)
        {
            case HsmsSType.SelectResponse:
                // TODO: 校验 Select 状态码（Data[0]），0x00 为成功
                SetState(HsmsConnectionState.Selected);
                break;

            case HsmsSType.LinkTestRequest:
                await SendRawAsync(HsmsMessage.CreateLinkTestResponse(message.Header.SystemBytes), cancellationToken);
                break;

            case HsmsSType.LinkTestResponse:
                // LinkTest 应答，无需处理
                break;

            case HsmsSType.SeparateRequest:
                SetState(HsmsConnectionState.NotSelected);
                break;

            case HsmsSType.DataMessage:
                RaiseMessageReceived(message);
                break;
        }
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
        if (disposing)
        {
            _cts?.Cancel();
            _stream?.Dispose();
            _client?.Dispose();
            _sendLock.Dispose();
        }

        base.Dispose(disposing);
    }
}
