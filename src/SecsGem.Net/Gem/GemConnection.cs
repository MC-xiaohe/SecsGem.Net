using SecsGem.Net.Hsms;
using SecsGem.Net.Messaging;

namespace SecsGem.Net.Gem;

/// <summary>
/// GEM（SEMI E30）连接门面。
/// 在 HSMS 之上提供：Collection Events、Alarms、Remote Commands、Recipe 管理、状态机。
/// TODO(v0.2)：完整 GEM 状态机与 S1F13/S1F14 通信建立流程。
/// </summary>
public sealed class GemConnection : IDisposable
{
    private readonly HsmsConnection _transport;
    private uint _systemBytes;

    public GemConnection(HsmsEndpoint endpoint)
    {
        _transport = new HsmsTcpConnection(endpoint);
        _transport.MessageReceived += OnTransportMessageReceived;
        _transport.StateChanged += (_, state) => StateChanged?.Invoke(this, state);
    }

    public event EventHandler<HsmsConnectionState>? StateChanged;
    public event EventHandler<SecsMessage>? MessageReceived;
    public event EventHandler<EventReceivedEventArgs>? EventReceived;
    public event EventHandler<AlarmReceivedEventArgs>? AlarmReceived;

    public async Task ConnectAsync(CancellationToken cancellationToken = default) =>
        await _transport.ConnectAsync(cancellationToken);

    /// <summary>S1F13/S1F14 建立通信（v0.2 实现）。</summary>
    public Task CommunicateAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("GEM Communicate (S1F13/S1F14) 将在 v0.2 实现。");

    /// <summary>
    /// 上报 Collection Event（S6F11）。
    /// TODO(v0.2)：按 GEM 规范构造完整 S6F11（CEID + RPTID + 数据变量）。
    /// </summary>
    public async Task SendEventAsync(ushort ceId, Report report, CancellationToken cancellationToken = default)
    {
        // 占位实现：先按 S6F11 发送 CEID，v0.2 替换为完整 SML 构造
        var secs = new SecsMessage
        {
            StreamNumber = 6,
            FunctionNumber = 11,
            WaitBit = true,
            SystemBytes = NextSystemBytes(),
            Body = DataItem.Create($"CE{ceId}")
        };

        await _transport.SendAsync(secs.ToHsmsMessage(), cancellationToken);
    }

    private void OnTransportMessageReceived(object? sender, HsmsMessage hsmsMessage)
    {
        if (!hsmsMessage.Header.IsDataMessage)
            return;

        var secs = SecsMessage.FromHsmsMessage(hsmsMessage);
        MessageReceived?.Invoke(this, secs);
    }

    private uint NextSystemBytes() => ++_systemBytes;

    public void Dispose() => _transport.Dispose();
}

public sealed class EventReceivedEventArgs : EventArgs
{
    public ushort CeId { get; init; }
    public Report Report { get; init; } = new();
}

public sealed class AlarmReceivedEventArgs : EventArgs
{
    public ushort AlarmId { get; init; }
    public bool Set { get; init; }
    public string AlarmText { get; init; } = string.Empty;
}

/// <summary>报告（RPTID → 数据变量集合）。</summary>
public sealed class Report : Dictionary<string, object>
{
    public Report()
    {
    }

    public Report(IDictionary<string, object> values) : base(values)
    {
    }
}
