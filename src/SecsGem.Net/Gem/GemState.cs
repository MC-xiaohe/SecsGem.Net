namespace SecsGem.Net.Gem;

/// <summary>通信状态（SEMI E30，Communication State）。</summary>
public enum CommunicationState
{
    /// <summary>未建立通信（S1F13 之前）。</summary>
    NotCommunicating,

    /// <summary>已建立通信（S1F13/S1F14 握手完成）。</summary>
    Communicating,

    /// <summary>需要人工干预（Host 需要向设备请求说明）。</summary>
    Attention
}

/// <summary>控制状态（SEMI E30，Control State）。</summary>
public enum ControlState
{
    /// <summary>离线：设备本地操作，不响应远程控制。</summary>
    Offline,

    /// <summary>在线-本地：可远程监控，但控制权在本地操作员。</summary>
    OnlineLocal,

    /// <summary>在线-远程：远程控制已使能（S2F37 后）。</summary>
    OnlineRemote
}

/// <summary>处理状态（SEMI E30，Processing State）。</summary>
public enum ProcessingState
{
    Idle,
    Ready,
    Processing,
    Paused,
    Complete
}
