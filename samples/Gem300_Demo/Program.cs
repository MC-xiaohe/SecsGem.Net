using SecsGem.Net.Gem;
using SecsGem.Net.Gem300;
using SecsGem.Net.Hsms;
using SecsGem.Net.Messaging;

// ============================================
// GEM300 (E87/E90) 演示：LoadPort 载具生命周期
// Device 端：GemConnection + 2 个 LoadPort（25 槽）
// Host  端：裸 HSMS，发送 LOAD/PROCESS/UNLOAD 命令
// 流程：S1F13 握手 → S2F41 LOAD → 设备自动上报
//       CarrierDetect/IDRead/Arrive/LoadStart/LoadComplete
//       → PROCESS（槽位加工）→ UNLOAD（卸载事件流）
// ============================================

// ---------- Device 端 ----------
var deviceEndpoint = new HsmsEndpoint
{
    Port = 5003,
    DeviceId = 0,
    Mode = HsmsConnectionMode.Passive
};

using var device = new GemConnection(deviceEndpoint, new GemConfig
{
    ModelName = "DEMO-ETCH-300",
    SoftwareVersion = "v1.0.0"
});

// 两个 300mm LoadPort（25 槽）
device.LoadPorts.Add(new LoadPort(1));
device.LoadPorts.Add(new LoadPort(2));

device.CommunicationStateChanged += (_, s) => Console.WriteLine($"[Device] 通信状态: {s}");
device.RemoteCommandReceived += (_, e) => Console.WriteLine($"[Device] 远程命令: {e.Command} args=[{string.Join(",", e.Arguments)}]");
await device.ConnectAsync();

// ---------- Host 端 ----------
var hostEndpoint = new HsmsEndpoint
{
    IpAddress = "127.0.0.1",
    Port = 5003,
    DeviceId = 0,
    Mode = HsmsConnectionMode.Active,
    LinkTestIntervalSeconds = 0
};

using var host = new HsmsTcpConnection(hostEndpoint);
host.MessageReceived += (_, m) =>
{
    var secs = SecsMessage.FromHsmsMessage(m);
    // 只打印 S6F11 事件（精简输出）
    if (secs.StreamNumber == 6 && secs.FunctionNumber == 11)
    {
        var ceId = GetFirstU4(secs.Body);
        Console.WriteLine($"[Host  ] S6F11 事件 CEID={ceId}");
        Console.WriteLine(secs.ToSml());
    }
    else
    {
        Console.WriteLine($"[Host  ] 收到 {secs}");
    }
};
host.ErrorOccurred += (_, ex) => Console.WriteLine($"[Host  ] 错误: {ex.Message}");
await host.ConnectAsync();

for (int i = 0; i < 30 && host.State != HsmsConnectionState.Selected; i++)
    await Task.Delay(100);
Console.WriteLine($"[Host  ] HSMS 握手完成: {host.State}");
Console.WriteLine();

uint sys = 0x200;
async Task SendCmd(string command, params string[] args)
{
    sys++;
    var body = DataItem.List(
        DataItem.A(command),
        DataItem.List(args.Select(DataItem.A).ToArray())
    );
    var msg = new SecsMessage
    {
        StreamNumber = 2,
        FunctionNumber = 41,
        WaitBit = true,
        SystemBytes = sys,
        Body = body
    };
    Console.WriteLine($"[Host  ] >>> S2F41 {command} {string.Join(" ", args)}");
    await host.SendAsync(msg.ToHsmsMessage());
    await Task.Delay(400);
}

// 1. 建立通信
sys++;
await host.SendAsync(new SecsMessage { StreamNumber = 1, FunctionNumber = 13, WaitBit = true, SystemBytes = sys, Body = DataItem.Empty }.ToHsmsMessage());
await Task.Delay(400);

// 2. LOAD 载具到 LP1（带载具 ID）
await SendCmd("LOAD", "LP1", "CARRIER-ABC123");

// 3. 查看 LP1 状态
Console.WriteLine($"[Host  ] LP1 状态: {device.LoadPorts[0]}");
Console.WriteLine();

// 4. PROCESS 加工
await SendCmd("PROCESS", "LP1");

// 5. 查看槽位（E90 基片追踪）
var lp1 = device.LoadPorts[0];
int done = lp1.Slots.Count(s => s == SubstrateState.Complete);
Console.WriteLine($"[Host  ] LP1 加工完成槽位: {done}/{lp1.SlotCount}");
Console.WriteLine();

// 6. UNLOAD 卸载
await SendCmd("UNLOAD", "LP1");
Console.WriteLine($"[Host  ] LP1 状态: {device.LoadPorts[0]}");

Console.WriteLine();
Console.WriteLine("GEM300 演示完成。");

static ushort GetFirstU4(DataItem body)
{
    if (body is ListItem list && list.Items.Count > 0 && list.Items[0] is UInt4Item u4)
        return (ushort)u4.Value[0];
    return 0;
}
