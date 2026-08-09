using SecsGem.Net.Hsms;

// ============================================
// SecsGem.Net 最小联调示例
// 本机同时模拟 Device（Passive）与 Host（Active），
// 验证：连接建立 → Select 握手 → 数据消息收发
// ============================================

// --- 1. Device 端（Passive，监听 5001 端口）---
var deviceEndpoint = new HsmsEndpoint
{
    Port = 5001,
    DeviceId = 0,
    Mode = HsmsConnectionMode.Passive
};

using var device = new HsmsTcpConnection(deviceEndpoint);
device.StateChanged += (_, s) => Console.WriteLine($"[Device] 状态: {s}");
device.ErrorOccurred += (_, ex) => Console.WriteLine($"[Device] 错误: {ex}");
device.MessageReceived += (_, m) => Console.WriteLine($"[Device] 收到: {m}");
await device.ConnectAsync();

// --- 2. Host 端（Active，主动连接）---
var hostEndpoint = new HsmsEndpoint
{
    IpAddress = "127.0.0.1",
    Port = 5001,
    DeviceId = 0,
    Mode = HsmsConnectionMode.Active,
    LinkTestIntervalSeconds = 0 // 示例关闭保活，方便观察
};

using var host = new HsmsTcpConnection(hostEndpoint);
host.StateChanged += (_, s) => Console.WriteLine($"[Host  ] 状态: {s}");
host.ErrorOccurred += (_, ex) => Console.WriteLine($"[Host  ] 错误: {ex}");
host.MessageReceived += (_, m) => Console.WriteLine($"[Host  ] 收到: {m}");
await host.ConnectAsync();

// 等 Select 握手完成（最多 3 秒）
for (int i = 0; i < 30 && host.State != HsmsConnectionState.Selected; i++)
    await Task.Delay(100);

if (host.State != HsmsConnectionState.Selected)
{
    Console.WriteLine("[Host  ] 握手失败，退出。");
    return;
}

Console.WriteLine("[Host  ] 握手成功（Selected），发送 S1F1 ...");

// --- 3. Host 发送 S1F1（设备识别请求，W-bit=1，无数据）---
var s1f1 = new HsmsMessage
{
    Header = new HsmsHeader
    {
        SessionId = 0,
        Stream = 1,
        Function = 1,
        WaitBit = true,
        PType = 0,
        SType = HsmsSType.DataMessage,
        SystemBytes = 0x00000001
    },
    Data = Array.Empty<byte>()
};

Console.WriteLine("[Host  ] 发送 S1F1 ...");await host.SendAsync(s1f1);

await Task.Delay(500);
Console.WriteLine();
Console.WriteLine("联调验证完成。若上方两端状态均为 Selected 且 Device 端打印了 S1F1，说明 HSMS 层工作正常。");
