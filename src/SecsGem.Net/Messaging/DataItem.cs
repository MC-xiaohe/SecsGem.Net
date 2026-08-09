using System.Buffers.Binary;
using System.Text;

namespace SecsGem.Net.Messaging;

/// <summary>SECS-II 格式码（SEMI E5，Format Byte 的高 6 位）。</summary>
public static class FormatCodes
{
    public const byte List = 0x00;
    public const byte Binary = 0x10;
    public const byte Boolean = 0x20;
    public const byte Ascii = 0x30;
    public const byte Jis8 = 0x40;
    public const byte Float8 = 0x50;
    public const byte Float4 = 0x60;
    public const byte Int8 = 0x70;
    public const byte Int1 = 0x78;
    public const byte Int2 = 0x80;
    public const byte Int4 = 0x88;
    public const byte UInt8 = 0x90;
    public const byte UInt1 = 0x98;
    public const byte UInt2 = 0xA0;
    public const byte UInt4 = 0xA8;

    public static string Name(byte formatCode) => formatCode switch
    {
        List => "L",
        Binary => "B",
        Boolean => "BOOLEAN",
        Ascii => "A",
        Jis8 => "J",
        Float8 => "F8",
        Float4 => "F4",
        Int8 => "I8",
        Int1 => "I1",
        Int2 => "I2",
        Int4 => "I4",
        UInt8 => "U8",
        UInt1 => "U1",
        UInt2 => "U2",
        UInt4 => "U4",
        _ => $"0x{formatCode:X2}"
    };
}

/// <summary>
/// SECS-II 数据项（SEMI E5）基类。
/// 二进制格式：[Format Byte(1)] [Length(1-3)] [Data]
/// Format Byte 高 6 位为格式码，低 2 位为长度字节数。
/// </summary>
public abstract class DataItem
{
    public abstract byte FormatCode { get; }

    /// <summary>数据正文（不含格式字节与长度字段）。</summary>
    public abstract byte[] EncodeBody();

    /// <summary>输出 SML 文本（用于日志、调试、配置）。</summary>
    public abstract string ToSml();

    /// <summary>编码为完整 SECS-II 二进制（格式字节 + 长度 + 正文）。</summary>
    public byte[] Encode()
    {
        var body = EncodeBody();

        int lengthBytes = body.Length switch
        {
            0 => 0,
            <= 0xFF => 1,
            <= 0xFFFF => 2,
            _ => 3
        };

        var result = new byte[1 + lengthBytes + body.Length];
        // 格式字节 = 格式码（标准值低 2 位为 0）| 长度字节数
        result[0] = (byte)(FormatCode | (lengthBytes == 0 ? 0 : lengthBytes));

        for (int i = 0; i < lengthBytes; i++)
            result[1 + i] = (byte)(body.Length >> (8 * (lengthBytes - 1 - i)));

        body.CopyTo(result, 1 + lengthBytes);
        return result;
    }

    /// <summary>
    /// 从二进制流解码一个数据项，并推进 offset。
    /// </summary>
    public static DataItem Decode(ReadOnlySpan<byte> buffer, ref int offset)
    {
        if (offset >= buffer.Length)
            throw new FormatException("数据不足，无法读取格式字节。");

        byte formatByte = buffer[offset++];
        // 格式码 = 格式字节高 6 位（标准值如 A=0x30, I4=0x88, U4=0xA8）
        byte formatCode = (byte)(formatByte & 0xFC);
        int lengthBytes = formatByte & 0x03;

        int length = 0;
        for (int i = 0; i < lengthBytes; i++)
        {
            if (offset >= buffer.Length)
                throw new FormatException("数据不足，无法读取长度字段。");
            length = (length << 8) | buffer[offset++];
        }

        if (offset + length > buffer.Length)
            throw new FormatException($"数据不足：需要 {length} 字节，剩余 {buffer.Length - offset}。");

        var data = buffer.Slice(offset, length);
        offset += length;

        return formatCode switch
        {
            FormatCodes.List => ListItem.DecodeChildren(data),
            FormatCodes.Binary => new BinaryItem(data.ToArray()),
            FormatCodes.Boolean => new BooleanItem(data.ToArray().Select(b => b != 0).ToArray()),
            FormatCodes.Ascii => new AsciiItem(Encoding.ASCII.GetString(data)),
            FormatCodes.Float8 => new Float8Item(DecodeValues(data, 8, static (s) => BinaryPrimitives.ReadDoubleBigEndian(s))),
            FormatCodes.Float4 => new Float4Item(DecodeValues(data, 4, static (s) => BinaryPrimitives.ReadSingleBigEndian(s))),
            FormatCodes.Int8 => new Int8Item(DecodeValues(data, 8, static (s) => BinaryPrimitives.ReadInt64BigEndian(s))),
            FormatCodes.Int1 => new Int1Item(DecodeValues(data, 1, static (s) => (sbyte)s[0])),
            FormatCodes.Int2 => new Int2Item(DecodeValues(data, 2, static (s) => BinaryPrimitives.ReadInt16BigEndian(s))),
            FormatCodes.Int4 => new Int4Item(DecodeValues(data, 4, static (s) => BinaryPrimitives.ReadInt32BigEndian(s))),
            FormatCodes.UInt8 => new UInt8Item(DecodeValues(data, 8, static (s) => BinaryPrimitives.ReadUInt64BigEndian(s))),
            FormatCodes.UInt1 => new UInt1Item(DecodeValues(data, 1, static (s) => s[0])),
            FormatCodes.UInt2 => new UInt2Item(DecodeValues(data, 2, static (s) => BinaryPrimitives.ReadUInt16BigEndian(s))),
            FormatCodes.UInt4 => new UInt4Item(DecodeValues(data, 4, static (s) => BinaryPrimitives.ReadUInt32BigEndian(s))),
            _ => throw new FormatException($"不支持的格式码 0x{formatCode:X2}。")
        };
    }

    private delegate T SpanReader<T>(ReadOnlySpan<byte> data);

    private static T[] DecodeValues<T>(ReadOnlySpan<byte> data, int size, SpanReader<T> read)
    {
        if (data.Length % size != 0)
            throw new FormatException($"数据长度 {data.Length} 不是 {size} 的整数倍。");

        var values = new T[data.Length / size];
        for (int i = 0; i < values.Length; i++)
            values[i] = read(data.Slice(i * size, size));
        return values;
    }

    // ---------- 工厂方法（A / I4 / U4 等常用类型） ----------

    public static DataItem A(string value) => new AsciiItem(value);
    public static DataItem I1(sbyte value) => new Int1Item(value);
    public static DataItem I2(short value) => new Int2Item(value);
    public static DataItem I4(int value) => new Int4Item(value);
    public static DataItem I8(long value) => new Int8Item(value);
    public static DataItem U1(byte value) => new UInt1Item(value);
    public static DataItem U2(ushort value) => new UInt2Item(value);
    public static DataItem U4(uint value) => new UInt4Item(value);
    public static DataItem U8(ulong value) => new UInt8Item(value);
    public static DataItem F4(float value) => new Float4Item(value);
    public static DataItem F8(double value) => new Float8Item(value);
    public static DataItem B(byte[] value) => new BinaryItem(value);
    public static DataItem Boolean(bool value) => new BooleanItem(value);

    /// <summary>创建 List，元素按给定顺序排列。</summary>
    public static DataItem List(params DataItem[] items) => new ListItem(items);

    /// <summary>空数据项（空 List），表示消息无 body。</summary>
    public static DataItem Empty { get; } = new ListItem();

    /// <summary>是否为无 body 的空数据项。</summary>
    public bool IsEmpty => this is ListItem { Items.Count: 0 };
}

// ================= 具体类型 =================

public sealed class ListItem : DataItem
{
    public IReadOnlyList<DataItem> Items { get; }

    public ListItem(params DataItem[] items) => Items = items;

    public ListItem(IEnumerable<DataItem> items) => Items = items.ToArray();

    public override byte FormatCode => FormatCodes.List;

    public override byte[] EncodeBody()
    {
        var parts = Items.Select(i => i.Encode()).ToArray();
        var buffer = new byte[parts.Sum(p => p.Length)];
        int offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(buffer, offset);
            offset += part.Length;
        }
        return buffer;
    }

    internal static DataItem DecodeChildren(ReadOnlySpan<byte> data)
    {
        var items = new List<DataItem>();
        int offset = 0;
        while (offset < data.Length)
            items.Add(Decode(data, ref offset));
        return new ListItem(items);
    }

    public override string ToSml()
    {
        if (Items.Count == 0)
            return "<L>";

        var sb = new StringBuilder();
        sb.AppendLine("<L");
        foreach (var item in Items)
            sb.AppendLine(item.ToSml().Replace("\n", "\n  ").PrependIndent("  "));
        sb.Append('>');
        return sb.ToString();
    }

    public override string ToString() => $"L[{Items.Count}]";
}

internal static class StringExtensions
{
    public static string PrependIndent(this string s, string indent) => indent + s;
}

public sealed class AsciiItem : DataItem
{
    public string Value { get; }

    public AsciiItem(string value) => Value = value;

    public override byte FormatCode => FormatCodes.Ascii;

    public override byte[] EncodeBody() => Encoding.ASCII.GetBytes(Value);

    public override string ToSml() => $"<A \"{Value}\">";

    public override string ToString() => $"A: \"{Value}\"";
}

public sealed class BinaryItem : DataItem
{
    public byte[] Value { get; }

    public BinaryItem(byte[] value) => Value = value;

    public override byte FormatCode => FormatCodes.Binary;

    public override byte[] EncodeBody() => Value;

    public override string ToSml() => $"<B {{{string.Join(" ", Value.Select(b => b.ToString("X2")))}}}>";

    public override string ToString() => $"B: {Value.Length} bytes";
}

public sealed class BooleanItem : DataItem
{
    public bool[] Value { get; }

    public BooleanItem(bool[] value) => Value = value;

    public BooleanItem(bool value) => Value = new[] { value };

    public override byte FormatCode => FormatCodes.Boolean;

    public override byte[] EncodeBody() => Value.Select(b => (byte)(b ? 1 : 0)).ToArray();

    public override string ToSml() => $"<BOOLEAN {string.Join(" ", Value.Select(b => b ? "T" : "F"))}>";

    public override string ToString() => $"BOOLEAN: [{string.Join(",", Value)}]";
}

// ---------- 数值类型（泛型基类，大端序） ----------

public abstract class NumericItem<T> : DataItem where T : unmanaged
{
    public T[] Value { get; }

    protected NumericItem(T value) => Value = new[] { value };

    protected NumericItem(T[] value) => Value = value;

    protected abstract int SizeOf { get; }

    protected abstract void WriteValue(Span<byte> buffer, int index, T value);

    public override byte[] EncodeBody()
    {
        var buffer = new byte[Value.Length * SizeOf];
        for (int i = 0; i < Value.Length; i++)
            WriteValue(buffer.AsSpan(i * SizeOf, SizeOf), i, Value[i]);
        return buffer;
    }

    public override string ToSml() => $"<{FormatCodes.Name(FormatCode)} {string.Join(" ", Value.Select(FormatValue))}>";

    protected abstract string FormatValue(T value);
}

public sealed class Int1Item : NumericItem<sbyte>
{
    public Int1Item(sbyte value) : base(value) { }
    public Int1Item(sbyte[] value) : base(value) { }
    public override byte FormatCode => FormatCodes.Int1;
    protected override int SizeOf => 1;
    protected override void WriteValue(Span<byte> buffer, int index, sbyte value) => buffer[0] = (byte)value;
    protected override string FormatValue(sbyte value) => value.ToString();
}

public sealed class Int2Item : NumericItem<short>
{
    public Int2Item(short value) : base(value) { }
    public Int2Item(short[] value) : base(value) { }
    public override byte FormatCode => FormatCodes.Int2;
    protected override int SizeOf => 2;
    protected override void WriteValue(Span<byte> buffer, int index, short value) => BinaryPrimitives.WriteInt16BigEndian(buffer, value);
    protected override string FormatValue(short value) => value.ToString();
}

public sealed class Int4Item : NumericItem<int>
{
    public Int4Item(int value) : base(value) { }
    public Int4Item(int[] value) : base(value) { }
    public override byte FormatCode => FormatCodes.Int4;
    protected override int SizeOf => 4;
    protected override void WriteValue(Span<byte> buffer, int index, int value) => BinaryPrimitives.WriteInt32BigEndian(buffer, value);
    protected override string FormatValue(int value) => value.ToString();
}

public sealed class Int8Item : NumericItem<long>
{
    public Int8Item(long value) : base(value) { }
    public Int8Item(long[] value) : base(value) { }
    public override byte FormatCode => FormatCodes.Int8;
    protected override int SizeOf => 8;
    protected override void WriteValue(Span<byte> buffer, int index, long value) => BinaryPrimitives.WriteInt64BigEndian(buffer, value);
    protected override string FormatValue(long value) => value.ToString();
}

public sealed class UInt1Item : NumericItem<byte>
{
    public UInt1Item(byte value) : base(value) { }
    public UInt1Item(byte[] value) : base(value) { }
    public override byte FormatCode => FormatCodes.UInt1;
    protected override int SizeOf => 1;
    protected override void WriteValue(Span<byte> buffer, int index, byte value) => buffer[0] = value;
    protected override string FormatValue(byte value) => value.ToString();
}

public sealed class UInt2Item : NumericItem<ushort>
{
    public UInt2Item(ushort value) : base(value) { }
    public UInt2Item(ushort[] value) : base(value) { }
    public override byte FormatCode => FormatCodes.UInt2;
    protected override int SizeOf => 2;
    protected override void WriteValue(Span<byte> buffer, int index, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
    protected override string FormatValue(ushort value) => value.ToString();
}

public sealed class UInt4Item : NumericItem<uint>
{
    public UInt4Item(uint value) : base(value) { }
    public UInt4Item(uint[] value) : base(value) { }
    public override byte FormatCode => FormatCodes.UInt4;
    protected override int SizeOf => 4;
    protected override void WriteValue(Span<byte> buffer, int index, uint value) => BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
    protected override string FormatValue(uint value) => value.ToString();
}

public sealed class UInt8Item : NumericItem<ulong>
{
    public UInt8Item(ulong value) : base(value) { }
    public UInt8Item(ulong[] value) : base(value) { }
    public override byte FormatCode => FormatCodes.UInt8;
    protected override int SizeOf => 8;
    protected override void WriteValue(Span<byte> buffer, int index, ulong value) => BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
    protected override string FormatValue(ulong value) => value.ToString();
}

public sealed class Float4Item : NumericItem<float>
{
    public Float4Item(float value) : base(value) { }
    public Float4Item(float[] value) : base(value) { }
    public override byte FormatCode => FormatCodes.Float4;
    protected override int SizeOf => 4;
    protected override void WriteValue(Span<byte> buffer, int index, float value) => BinaryPrimitives.WriteSingleBigEndian(buffer, value);
    protected override string FormatValue(float value) => value.ToString("R");
}

public sealed class Float8Item : NumericItem<double>
{
    public Float8Item(double value) : base(value) { }
    public Float8Item(double[] value) : base(value) { }
    public override byte FormatCode => FormatCodes.Float8;
    protected override int SizeOf => 8;
    protected override void WriteValue(Span<byte> buffer, int index, double value) => BinaryPrimitives.WriteDoubleBigEndian(buffer, value);
    protected override string FormatValue(double value) => value.ToString("R");
}
