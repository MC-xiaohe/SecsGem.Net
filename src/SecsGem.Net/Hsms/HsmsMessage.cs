using System.Buffers.Binary;

namespace SecsGem.Net.Hsms;

/// <summary>
/// HSMS 消息（完整帧 = 4 字节长度 + 10 字节消息头 + 数据）。
/// 长度字段以 4 字节字（word）为单位。
/// </summary>
public sealed class HsmsMessage
{
    public const int LengthFieldSize = 4;

    public HsmsHeader Header { get; init; }

    public byte[] Data { get; init; } = Array.Empty<byte>();

    /// <summary>消息头 + 数据的字节数。</summary>
    public uint TotalLength => (uint)(HsmsHeader.ByteLength + Data.Length);

    /// <summary>以 4 字节字为单位的总长度（帧长度字段的值）。</summary>
    public uint TotalLengthInWords => (TotalLength + 3) / 4;

    // ---- 控制消息工厂方法（E37）----

    public static HsmsMessage CreateSelectRequest(uint systemBytes) => new()
    {
        Header = new HsmsHeader { SessionId = 0xFFFF, SType = HsmsSType.SelectRequest, SystemBytes = systemBytes }
    };

    public static HsmsMessage CreateSelectResponse(uint systemBytes) => new()
    {
        Header = new HsmsHeader { SessionId = 0xFFFF, SType = HsmsSType.SelectResponse, SystemBytes = systemBytes }
    };

    public static HsmsMessage CreateLinkTestRequest(uint systemBytes) => new()
    {
        Header = new HsmsHeader { SessionId = 0xFFFF, SType = HsmsSType.LinkTestRequest, SystemBytes = systemBytes }
    };

    public static HsmsMessage CreateLinkTestResponse(uint systemBytes) => new()
    {
        Header = new HsmsHeader { SessionId = 0xFFFF, SType = HsmsSType.LinkTestResponse, SystemBytes = systemBytes }
    };

    public static HsmsMessage CreateSeparateRequest(uint systemBytes) => new()
    {
        Header = new HsmsHeader { SessionId = 0xFFFF, SType = HsmsSType.SeparateRequest, SystemBytes = systemBytes }
    };

    /// <summary>编码为完整帧（长度 + 头 + 数据）。</summary>
    public byte[] Encode()
    {
        var frame = new byte[LengthFieldSize + HsmsHeader.ByteLength + Data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, TotalLengthInWords);
        Header.Encode(frame.AsSpan(LengthFieldSize));
        Data.CopyTo(frame, LengthFieldSize + HsmsHeader.ByteLength);
        return frame;
    }

    /// <summary>从完整帧解码。</summary>
    public static HsmsMessage Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < LengthFieldSize + HsmsHeader.ByteLength)
            throw new ArgumentException("帧长度不足。", nameof(frame));

        uint lengthInWords = BinaryPrimitives.ReadUInt32BigEndian(frame);
        int lengthInBytes = (int)lengthInWords * 4;

        if (frame.Length < LengthFieldSize + lengthInBytes)
            throw new ArgumentException("帧数据不完整（截断）。", nameof(frame));

        var header = HsmsHeader.Decode(frame[LengthFieldSize..]);
        var data = frame.Slice(LengthFieldSize + HsmsHeader.ByteLength, lengthInBytes - HsmsHeader.ByteLength).ToArray();

        return new HsmsMessage { Header = header, Data = data };
    }

    public override string ToString() =>
        $"{Header} | Data={Data.Length} bytes";
}
