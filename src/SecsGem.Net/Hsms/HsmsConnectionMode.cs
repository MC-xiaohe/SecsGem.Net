namespace SecsGem.Net.Hsms;

/// <summary>
/// HSMS 连接模式（SEMI E37）。
/// Active = 本端主动发起 TCP 连接并发送 Select 请求；
/// Passive = 本端监听端口，等待对端发起连接。
/// </summary>
public enum HsmsConnectionMode
{
    Active,
    Passive
}
