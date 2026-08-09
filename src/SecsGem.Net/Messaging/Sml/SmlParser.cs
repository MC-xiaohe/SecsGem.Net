using System.Globalization;
using System.Text;

namespace SecsGem.Net.Messaging.Sml;

/// <summary>
/// SML（SECS Message Language，SEMI E5 附录）解析器。
/// 支持数据项 SML 与完整消息 SML。
/// 示例：A 字符串、I4/U4 数值、BOOLEAN、List、以及 S1F1 W / S6F11 W 完整消息。
/// 用于：模拟器脚本、测试用例、报文编辑。
/// </summary>
public static class SmlParser
{
    // ---------- 数据项解析 ----------

    /// <summary>解析一个数据项 SML 文本。</summary>
    public static DataItem ParseItem(string sml)
    {
        var reader = new SmlReader(sml);
        var item = reader.ReadItem();
        reader.SkipWhitespace();
        if (!reader.End)
            throw new FormatException($"SML 数据项后存在多余内容：位置 {reader.Position}。");
        return item;
    }

    // ---------- 消息解析 ----------

    /// <summary>
    /// 解析完整消息 SML，例如：
    /// S6F11 W
    /// &lt;L ...&gt;
    /// .
    /// </summary>
    public static SecsMessage ParseMessage(string sml)
    {
        var reader = new SmlReader(sml);

        reader.SkipWhitespace();

        // S{stream}F{function}
        if (reader.Peek() != 'S')
            throw new FormatException("消息必须以 S{stream}F{function} 开头。");

        reader.Read(); // 'S'
        int stream = reader.ReadNumber();
        if (reader.Peek() != 'F')
            throw new FormatException("缺少 'F'。");
        reader.Read();
        int function = reader.ReadNumber();

        // 可选 W
        reader.SkipWhitespace();
        bool waitBit = false;
        if (reader.Peek() == 'W' || reader.Peek() == 'w')
        {
            waitBit = true;
            reader.Read();
        }

        // 可选数据项
        reader.SkipWhitespace();
        DataItem? body = null;
        if (reader.Peek() == '<')
            body = reader.ReadItem();

        // 结束符 .
        reader.SkipWhitespace();
        if (reader.Peek() != '.')
            throw new FormatException("消息必须以 '.' 结束。");
        reader.Read();
        reader.SkipWhitespace();
        if (!reader.End)
            throw new FormatException($"消息结束符后存在多余内容：位置 {reader.Position}。");

        return new SecsMessage
        {
            StreamNumber = (byte)stream,
            FunctionNumber = (byte)function,
            WaitBit = waitBit,
            Body = body ?? DataItem.Empty
        };
    }

    // ---------- 内部读取器 ----------

    private sealed class SmlReader
    {
        private readonly string _text;
        private int _pos;

        public SmlReader(string text) => _text = text;

        public int Position => _pos;
        public bool End => _pos >= _text.Length;

        public char Peek()
        {
            SkipWhitespace();
            if (End)
                throw new FormatException("SML 意外结束。");
            return _text[_pos];
        }

        public char Read()
        {
            if (End)
                throw new FormatException("SML 意外结束。");
            return _text[_pos++];
        }

        public void SkipWhitespace()
        {
            while (!End && char.IsWhiteSpace(_text[_pos]))
                _pos++;
        }

        public int ReadNumber()
        {
            SkipWhitespace();
            int start = _pos;
            while (!End && char.IsDigit(_text[_pos]))
                _pos++;
            if (start == _pos)
                throw new FormatException($"位置 {_pos} 处预期数字。");
            return int.Parse(_text[start.._pos], CultureInfo.InvariantCulture);
        }

        /// <summary>读取一个数据项：&lt;TYPE ...&gt;。</summary>
        public DataItem ReadItem()
        {
            if (Read() != '<')
                throw new FormatException($"位置 {Position - 1} 处预期 '<'。");

            // 类型名（字母 + 数字，如 L / A / U4 / BOOLEAN）
            SkipWhitespace();
            int start = _pos;
            while (!End && (char.IsLetter(_text[_pos]) || char.IsDigit(_text[_pos])))
                _pos++;
            string typeName = _text[start.._pos].ToUpperInvariant();

            SkipWhitespace();

            switch (typeName)
            {
                case "L":
                    return ReadList();
                case "A":
                {
                    string value = ReadQuotedString();
                    ExpectClose();
                    return new AsciiItem(value);
                }
                case "B":
                {
                    byte[] bytes = ReadHexBytes();
                    ExpectClose();
                    return new BinaryItem(bytes);
                }
                case "BOOLEAN":
                    return new BooleanItem(ReadBooleans());
                case "I1":
                    return new Int1Item(ReadNumericArray((int v) => (sbyte)v));
                case "I2":
                    return new Int2Item(ReadNumericArray((int v) => (short)v));
                case "I4":
                    return new Int4Item(ReadNumericArray(v => v));
                case "I8":
                    return new Int8Item(ReadNumericArray((int v) => (long)v));
                case "U1":
                    return new UInt1Item(ReadNumericArray((int v) => (byte)v));
                case "U2":
                    return new UInt2Item(ReadNumericArray((int v) => (ushort)v));
                case "U4":
                    return new UInt4Item(ReadNumericArray((int v) => (uint)v));
                case "U8":
                    return new UInt8Item(ReadNumericArray((int v) => (ulong)v));
                case "F4":
                    return new Float4Item(ReadFloatArray((double v) => (float)v));
                case "F8":
                    return new Float8Item(ReadFloatArray(v => v));
                default:
                    throw new FormatException($"不支持的 SML 类型：{typeName}。");
            }
        }

        private void ExpectClose()
        {
            SkipWhitespace();
            if (Read() != '>')
                throw new FormatException($"位置 {Position - 1} 处预期 '>'。");
        }

        private DataItem ReadList()
        {
            var items = new List<DataItem>();
            SkipWhitespace();

            if (Peek() == '>')
            {
                Read(); // 空 List
                return new ListItem(items);
            }

            while (true)
            {
                SkipWhitespace();
                if (Peek() == '>')
                {
                    Read();
                    break;
                }
                items.Add(ReadItem());
            }

            return new ListItem(items);
        }

        private string ReadQuotedString()
        {
            SkipWhitespace();
            if (Read() != '"')
                throw new FormatException("A 类型必须带双引号字符串。");

            var sb = new StringBuilder();
            while (!End)
            {
                char c = Read();
                if (c == '"')
                    return sb.ToString();
                sb.Append(c);
            }
            throw new FormatException("字符串未闭合。");
        }

        private byte[] ReadHexBytes()
        {
            // 支持 {AA BB CC} 或 {AABBCC}
            SkipWhitespace();
            if (Read() != '{')
                throw new FormatException("B 类型必须使用花括号。");

            var hex = new StringBuilder();
            while (!End)
            {
                char c = Read();
                if (c == '}')
                    break;
                if (!char.IsWhiteSpace(c))
                    hex.Append(c);
            }

            if (hex.Length % 2 != 0)
                throw new FormatException("B 类型十六进制长度必须为偶数。");

            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = byte.Parse(hex.ToString(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }

        private bool[] ReadBooleans()
        {
            var values = new List<bool>();
            SkipWhitespace();
            while (Peek() != '>')
            {
                char c = Read();
                values.Add(c switch
                {
                    'T' or 't' => true,
                    'F' or 'f' => false,
                    _ => throw new FormatException($"位置 {Position - 1} 处预期 T/F。")
                });
                SkipWhitespace();
            }
            Read(); // '>'
            return values.ToArray();
        }

        private T[] ReadNumericArray<T>(Func<int, T> convert)
        {
            var values = new List<T>();
            SkipWhitespace();
            while (Peek() != '>')
            {
                int sign = 1;
                if (Peek() == '-')
                {
                    sign = -1;
                    Read();
                }
                values.Add(convert(sign * ReadNumber()));
                SkipWhitespace();
            }
            Read(); // '>'
            return values.ToArray();
        }

        private T[] ReadFloatArray<T>(Func<double, T> convert)
        {
            var values = new List<T>();
            SkipWhitespace();
            while (Peek() != '>')
            {
                SkipWhitespace();
                int start = _pos;
                while (!End && _text[_pos] != '>' && !char.IsWhiteSpace(_text[_pos]))
                    _pos++;
                string token = _text[start.._pos];
                values.Add(convert(double.Parse(token, CultureInfo.InvariantCulture)));
                SkipWhitespace();
            }
            Read(); // '>'
            return values.ToArray();
        }
    }
}
