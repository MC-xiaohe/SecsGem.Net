namespace SecsGem.Net.Hsms;

/// <summary>连接状态（对应 E37 中的 Not Selected / Selected 等状态）。</summary>
public enum HsmsConnectionState
{
    Disconnected,
    Connecting,
    NotSelected,
    Selected,
    Error
}
