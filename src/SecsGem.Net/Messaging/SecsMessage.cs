using System.Text;
using SecsGem.Net.Hsms;
using SecsGem.Net.Messaging.Sml;

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
        Data = Body.IsEmpty ? Array.Empty<byte>() : Body.Encode()
    };

    /// <summary>从 HSMS 传输帧解析。</summary>
    public static SecsMessage FromHsmsMessage(HsmsMessage message)
    {
        if (!message.Header.IsDataMessage)
            throw new ArgumentException("不是 SECS 数据消息。", nameof(message));

        int offset = 0;
        var body = message.Data.Length > 0
            ? DataItem.Decode(message.Data, ref offset)
            : DataItem.Empty;

        return new SecsMessage
        {
            DeviceId = message.Header.DeviceId,
            StreamNumber = message.Header.Stream,
            FunctionNumber = message.Header.Function,
            WaitBit = message.Header.WaitBit,
            SystemBytes = message.Header.SystemBytes,
            Body = body
        };
    }

    /// <summary>
    /// 输出消息 SML，例如：
    /// S6F11 W
    /// &lt;L ...&gt;
    /// .
    /// </summary>
    public string ToSml()
    {
        var sb = new StringBuilder();
        sb.Append($"S{StreamNumber}F{FunctionNumber}");
        if (WaitBit)
            sb.Append(" W");

        if (!Body.IsEmpty)
            sb.AppendLine().Append(Body.ToSml());

        sb.AppendLine().Append('.');
        return sb.ToString();
    }

    /// <summary>从消息 SML 文本解析（用于测试脚本、模拟器配置）。</summary>
    public static SecsMessage Parse(string sml) => SmlParser.ParseMessage(sml);

    public override string ToString() => $"S{StreamNumber}F{FunctionNumber} W={(WaitBit ? 1 : 0)} Sys={SystemBytes:X8}";
}
