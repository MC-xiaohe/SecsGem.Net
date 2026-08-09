namespace SecsGem.Net.Hsms;

/// <summary>
/// HSMS 超时参数（SEMI E37），单位为秒：
/// T3 = 等待回复超时；T5 = 连接分离超时；T6 = 数据消息超时；
/// T7 = 未选中超时；T8 = 字符间超时。
/// </summary>
public sealed class HsmsTimeouts
{
    public int T3 { get; set; } = 45;
    public int T5 { get; set; } = 10;
    public int T6 { get; set; } = 5;
    public int T7 { get; set; } = 10;
    public int T8 { get; set; } = 5;
}
