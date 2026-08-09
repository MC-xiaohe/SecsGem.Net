using System.Buffers.Binary;

namespace SecsGem.Net.Hsms;

/// <summary>
/// HSMS 消息头（10 字节）：
/// [0-1] Session ID（低 7 位为 Device ID）
/// [2]   Stream（bit7 = W-bit）
/// [3]   Function
/// [4]   PType（数据消息固定 0）
/// [5]   SType（0 = 数据消息，1-9 = 控制消息）
/// [6-9] System Bytes
/// </summary>
public readonly struct HsmsHeader
{
    public const int ByteLength = 10;

    public ushort SessionId { get; init; }
    public byte Stream { get; init; }
    public byte Function { get; init; }
    public bool WaitBit { get; init; }
    public byte PType { get; init; }
    public HsmsSType SType { get; init; }
    public uint SystemBytes { get; init; }

    public ushort DeviceId => (ushort)(SessionId & 0x7F);

    public bool IsDataMessage => SType == HsmsSType.DataMessage;

    public static HsmsHeader Decode(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < ByteLength)
            throw new ArgumentException($"HSMS 消息头需要 {ByteLength} 字节。", nameof(buffer));

        return new HsmsHeader
        {
            SessionId = BinaryPrimitives.ReadUInt16BigEndian(buffer),
            Stream = (byte)(buffer[2] & 0x7F),
            WaitBit = (buffer[2] & 0x80) != 0,
            Function = buffer[3],
            PType = buffer[4],
            SType = (HsmsSType)buffer[5],
            SystemBytes = BinaryPrimitives.ReadUInt32BigEndian(buffer[6..])
        };
    }

    public void Encode(Span<byte> buffer)
    {
        if (buffer.Length < ByteLength)
            throw new ArgumentException($"HSMS 消息头需要 {ByteLength} 字节。", nameof(buffer));

        BinaryPrimitives.WriteUInt16BigEndian(buffer, SessionId);
        buffer[2] = (byte)((Stream & 0x7F) | (WaitBit ? 0x80 : 0x00));
        buffer[3] = Function;
        buffer[4] = PType;
        buffer[5] = (byte)SType;
        BinaryPrimitives.WriteUInt32BigEndian(buffer[6..], SystemBytes);
    }

    public override string ToString() =>
        IsDataMessage
            ? $"S{Stream}F{Function} W={(WaitBit ? 1 : 0)} Sys={SystemBytes:X8}"
            : $"{SType} Sys={SystemBytes:X8}";
}
