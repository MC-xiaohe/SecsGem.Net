using SecsGem.Net.Hsms;

namespace SecsGem.Net.Messaging;

/// <summary>
/// SECS-II 消息（SEMI E5），如 S1F1、S6F11。
/// 与 HsmsMessage 的区别：HsmsMessage 是传输层帧，SecsMessage 是业务层消息。
/// </summary>
public sealed class SecsMessage
{
    public ushort DeviceId { get; init; }

    public byte StreamNumber { get; init; }

    public byte FunctionNumber { get; init; }

    public bool WaitBit { get; init; }

    public uint SystemBytes { get; init; }

    public DataItem Body { get; init; } = DataItem.Empty;

    /// <summary>转换为 HSMS 传输帧。</summary>
    public HsmsMessage ToHsmsMessage() => new()
    {
        Header = new HsmsHeader
        {
            SessionId = DeviceId,
            Stream = StreamNumber,
            Function = FunctionNumber,
            WaitBit = WaitBit,
            PType = 0,
            SType = HsmsSType.DataMessage,
            SystemBytes = SystemBytes
        },
        Data = Body.EncodeBody()
    };

    /// <summary>从 HSMS 传输帧解析。</summary>
    public static SecsMessage FromHsmsMessage(HsmsMessage message)
    {
        if (!message.Header.IsDataMessage)
            throw new ArgumentException("不是 SECS 数据消息。", nameof(message));

        return new SecsMessage
        {
            DeviceId = message.Header.DeviceId,
            StreamNumber = message.Header.Stream,
            FunctionNumber = message.Header.Function,
            WaitBit = message.Header.WaitBit,
            SystemBytes = message.Header.SystemBytes,
            Body = DataItem.Decode(message.Data)
        };
    }

    public override string ToString() => $"S{StreamNumber}F{FunctionNumber} W={(WaitBit ? 1 : 0)} Sys={SystemBytes:X8}";
}
