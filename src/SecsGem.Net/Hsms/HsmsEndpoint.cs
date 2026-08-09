namespace SecsGem.Net.Hsms;

/// <summary>HSMS 端点配置。</summary>
public sealed class HsmsEndpoint
{
    public string IpAddress { get; set; } = string.Empty;

    public int Port { get; set; } = 5000;

    /// <summary>设备 ID（0-126）。</summary>
    public ushort DeviceId { get; set; }

    public HsmsConnectionMode Mode { get; set; } = HsmsConnectionMode.Active;

    public HsmsTimeouts Timeouts { get; set; } = new();

    /// <summary>主动模式下断线重连间隔（毫秒）。</summary>
    public int ReconnectIntervalMs { get; set; } = 5000;

    /// <summary>T3 超时后自动重发次数（0 = 不重发）。</summary>
    public int T3RetryCount { get; set; } = 3;

    /// <summary>LinkTest 保活间隔（秒，0 = 禁用）。</summary>
    public int LinkTestIntervalSeconds { get; set; } = 30;
}
