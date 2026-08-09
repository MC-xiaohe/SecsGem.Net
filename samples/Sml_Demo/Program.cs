using SecsGem.Net.Messaging;

// ============================================
// SECS-II / SML 编解码演示
// 演示：S6F11 事件上报消息的
//   构造 → SML 输出 → 二进制编码 → 解码回读
// ============================================

Console.WriteLine("=== 1. 用代码构造 S6F11 事件上报消息 ===");

var s6f11 = new SecsMessage
{
    StreamNumber = 6,
    FunctionNumber = 11,
    WaitBit = true,
    SystemBytes = 0x00001001,
    Body = DataItem.List(
        DataItem.U4(1001),                      // CEID：事件 ID
        DataItem.List(                          // 报告集合（可多个）
            DataItem.U4(1),                     // RPTID：报告 ID
            DataItem.List(                      // 数据变量列表
                DataItem.A("PROCESS_STATE"),    // SVID 名称
                DataItem.A("RUNNING")           // 值
            )
        )
    )
};

Console.WriteLine(s6f11.ToSml());

Console.WriteLine();
Console.WriteLine("=== 2. 编码为二进制报文 ===");

var hsms = s6f11.ToHsmsMessage();
Console.WriteLine($"帧长度: {hsms.TotalLength} 字节 (Length字段={hsms.TotalLengthInWords} words)");
Console.WriteLine($"Hex   : {Convert.ToHexString(hsms.Encode())}");

Console.WriteLine();
Console.WriteLine("=== 3. 从 SML 文本解析消息（模拟器脚本场景） ===");

const string smlScript = @"S6F11 W
<L
  <U4 1001>
  <L
    <U4 1>
    <L
      <A ""PROCESS_STATE"">
      <A ""RUNNING"">
    >
  >
>
.
";

var parsed = SecsMessage.Parse(smlScript);
Console.WriteLine(parsed.ToSml());

Console.WriteLine();
Console.WriteLine("=== 4. 往返一致性验证（解析→编码→解码→SML 对比） ===");

var parsedHsms = parsed.ToHsmsMessage();
var decoded = SecsMessage.FromHsmsMessage(parsedHsms);
bool equal = decoded.ToSml() == parsed.ToSml();
Console.WriteLine($"原始 SML == 往返 SML : {(equal ? "✓ 一致" : "✗ 不一致")}");

if (!equal)
{
    Console.WriteLine("--- 原始 ---");
    Console.WriteLine(parsed.ToSml());
    Console.WriteLine("--- 往返 ---");
    Console.WriteLine(decoded.ToSml());
}

Console.WriteLine();
Console.WriteLine("=== 5. 常见消息速览 ===");

var s1f2 = SecsMessage.Parse(@"S1F2 W
<L
  <A ""SECSGEM-DEMO"">
  <A ""SECS-GEM"">
  <A ""v0.1.0"">
>
.
");
Console.WriteLine(s1f2.ToSml());

Console.WriteLine();
Console.WriteLine("演示完成。");
