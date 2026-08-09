using System.Buffers.Binary;
using System.Text;

namespace SecsGem.Net.Messaging;

/// <summary>
/// SECS-II 数据项（SEMI E5）基类。
/// 每个数据项由一个格式字节（Format Code）和正文组成。
/// TODO(v0.2)：完整实现所有数据类型（B / Boolean / U1-U8 / I1-I8 / F4 / F8 / List）与 SML 编解码。
/// </summary>
public abstract class DataItem
{
    public abstract byte FormatCode { get; }

    public abstract byte[] EncodeBody();

    public static DataItem Empty { get; } = new EmptyItem();

    public static DataItem Create(string value) => new AsciiItem(value);

    public static DataItem Create(int value) => new Int4Item(value);

    /// <summary>TODO(v0.2)：从消息数据解码出数据项树。</summary>
    public static DataItem Decode(ReadOnlySpan<byte> body) =>
        throw new NotImplementedException("SML 解码将在 v0.2 实现。");

    private sealed class EmptyItem : DataItem
    {
        public override byte FormatCode => 0x00;
        public override byte[] EncodeBody() => Array.Empty<byte>();
    }
}

/// <summary>A（ASCII 字符串，Format Code 0x40）。</summary>
public sealed class AsciiItem : DataItem
{
    public string Value { get; }

    public AsciiItem(string value) => Value = value;

    public override byte FormatCode => 0x40;

    public override byte[] EncodeBody() => Encoding.ASCII.GetBytes(Value);

    public override string ToString() => $"A: \"{Value}\"";
}

/// <summary>I4（32 位有符号整数，Format Code 0x60）。</summary>
public sealed class Int4Item : DataItem
{
    public int Value { get; }

    public Int4Item(int value) => Value = value;

    public override byte FormatCode => 0x60;

    public override byte[] EncodeBody()
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, Value);
        return buffer;
    }

    public override string ToString() => $"I4: {Value}";
}
