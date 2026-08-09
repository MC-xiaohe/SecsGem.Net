namespace SecsGem.Net.Hsms;

/// <summary>
/// HSMS 消息类型（E37，Header Byte 3 / SType）。
/// 0 = 数据消息（SECS-II），其余为控制消息。
/// </summary>
public enum HsmsSType : byte
{
    DataMessage = 0,
    SelectRequest = 1,
    SelectResponse = 2,
    DeselectRequest = 3,
    DeselectResponse = 4,
    LinkTestRequest = 5,
    LinkTestResponse = 6,
    RejectRequest = 7,
    SeparateRequest = 9
}
