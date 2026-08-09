using SecsGem.Net.Gem;
using SecsGem.Net.Hsms;
using SecsGem.Net.Messaging;

// ============================================
// GEM (SEMI E30) 全流程演示
// Device 端：GemConnection（Passive，预定义模型）
// Host  端：裸 HSMS 连接（Active）
// 流程：S1F13 握手 → S2F33 定义变量 → S2F35 定义报告
//       → S2F37 使能事件 → S2F41 远程命令 → S6F11 事件上报
// ============================================

// ---------- Device 端 ----------
var deviceModel = new GemModel();
deviceModel.DefineSvid(1001, "PROCESS_STATE");
deviceModel.DefineSvid(1002, "LOT_ID");
deviceModel.DefineReport(1, 1001, 1002);
deviceModel.DefineCeid(1001, "PROCESS_START", 1);
deviceModel.SvidValueProvider = svid => svid switch
{
    1001 => DataItem.A("RUNNING"),
    1002 => DataItem.A("LOT-2026-0812"),
    _ => DataItem.A("?")
};

var deviceEndpoint = new HsmsEndpoint
{
    Port = 5002,
    DeviceId = 0,
    Mode = HsmsConnectionMode.Passive
};

using var device = new GemConnection(
    deviceEndpoint,
    new GemConfig { ModelName = "DEMO-ETCH", SoftwareVersion = "v1.0.0" },
    deviceModel);

device.CommunicationStateChanged += (_, s) => Console.WriteLine($"[Device] 通信状态: {s}");
device.ControlStateChanged += (_, s) => Console.WriteLine($"[Device] 控制状态: {s}");
device.RemoteCommandReceived += (_, e) => Console.WriteLine($"[Device] 远程命令: {e.Command} args=[{string.Join(",", e.Arguments)}]");
device.MessageReceived += (_, m) => Console.WriteLine($"[Device] 收到: {m}");
await device.ConnectAsync();

// ---------- Host 端 ----------
var hostEndpoint = new HsmsEndpoint
{
    IpAddress = "127.0.0.1",
    Port = 5002,
    DeviceId = 0,
    Mode = HsmsConnectionMode.Active,
    LinkTestIntervalSeconds = 0
};

using var host = new HsmsTcpConnection(hostEndpoint);
host.MessageReceived += (_, m) =>
{
    var secs = SecsMessage.FromHsmsMessage(m);
    Console.WriteLine($"[Host  ] 收到 {secs}:\n{secs.ToSml()}");
};
host.ErrorOccurred += (_, ex) => Console.WriteLine($"[Host  ] 错误: {ex.Message}");
await host.ConnectAsync();

for (int i = 0; i < 30 && host.State != HsmsConnectionState.Selected; i++)
    await Task.Delay(100);
Console.WriteLine($"[Host  ] HSMS 握手完成: {host.State}");
Console.WriteLine();

uint sys = 0x100;
async Task SendRaw(byte stream, byte function, DataItem? body, string desc)
{
    sys++;
    var msg = new SecsMessage
    {
        StreamNumber = stream,
        FunctionNumber = function,
        WaitBit = true,
        SystemBytes = sys,
        Body = body ?? DataItem.Empty
    };
    Console.WriteLine($"[Host  ] >>> {desc}");
    await host.SendAsync(msg.ToHsmsMessage());
    await Task.Delay(300);
}

// 1. S1F13 建立通信
await SendRaw(1, 13, null, "S1F13 建立通信");

// 2. S2F33 定义数据变量
await SendRaw(2, 33, DataItem.List(
    DataItem.List(DataItem.U4(1001), DataItem.A("PROCESS_STATE")),
    DataItem.List(DataItem.U4(1002), DataItem.A("LOT_ID"))
), "S2F33 定义 SVID");

// 3. S2F35 定义报告
await SendRaw(2, 35, DataItem.List(
    DataItem.List(DataItem.U4(1), DataItem.List(DataItem.U4(1001), DataItem.U4(1002)))
), "S2F35 定义报告");

// 4. S2F37 使能事件
await SendRaw(2, 37, DataItem.List(DataItem.U4(1001), DataItem.Boolean(true)), "S2F37 使能 CEID 1001");

// 5. S2F41 远程命令
await SendRaw(2, 41, DataItem.List(DataItem.A("START"), DataItem.List(DataItem.A("RECIPE_A"))), "S2F41 远程命令 START");

// 6. Device 上报事件（S6F11）
Console.WriteLine("[Host  ] 触发 Device 上报 S6F11 ...");
await device.SendEventAsync(1001);
await Task.Delay(500);

Console.WriteLine();
Console.WriteLine("GEM 演示完成。若上方出现 S1F14 / S2F34 / S2F36 / S2F38 / S2F42 回复与 S6F11 上报，说明 GEM 层工作正常。");
