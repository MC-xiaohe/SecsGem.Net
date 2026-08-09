namespace SecsGem.Net.Hsms;

/// <summary>
/// HSMS 连接抽象基类。具体实现（TCP / 串口）由子类提供。
/// </summary>
public abstract class HsmsConnection : IDisposable
{
    /// <summary>收到完整消息（数据消息与控制消息都会触发）。</summary>
    public event EventHandler<HsmsMessage>? MessageReceived;

    /// <summary>连接状态变化。</summary>
    public event EventHandler<HsmsConnectionState>? StateChanged;

    /// <summary>通讯层异常（断线、解析错误等）。</summary>
    public event EventHandler<Exception>? ErrorOccurred;

    public HsmsConnectionState State { get; protected set; } = HsmsConnectionState.Disconnected;

    public abstract Task ConnectAsync(CancellationToken cancellationToken = default);

    public abstract Task DisconnectAsync();

    public abstract Task SendAsync(HsmsMessage message, CancellationToken cancellationToken = default);

    protected void SetState(HsmsConnectionState newState)
    {
        if (State == newState)
            return;

        State = newState;
        StateChanged?.Invoke(this, newState);
    }

    protected void RaiseMessageReceived(HsmsMessage message) => MessageReceived?.Invoke(this, message);

    protected void RaiseError(Exception exception) => ErrorOccurred?.Invoke(this, exception);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
