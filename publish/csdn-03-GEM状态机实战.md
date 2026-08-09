# SECS/GEM 从零到一（三）：GEM 状态机实战——从"会收发消息"到"懂设备行为"

> 作者：MC-xiaohe（EAP 自动化工程师）
> 配套开源库：[SecsGem.Net](https://github.com/MC-xiaohe/SecsGem.Net)（C#，HSMS + SECS-II + GEM 完整实现）

系列前两篇：① [HSMS 通讯实战](https://zhuanlan.zhihu.com/p/2069768337253458775) ② [SECS-II 消息与 SML](https://zhuanlan.zhihu.com/p/2069773859717374424)。到这一步，你已经能收发消息、看懂报文了——但 GEM 协议的核心不在"消息怎么传"，而在**"设备在什么状态下该做什么"**。这一篇把 GEM 状态机讲透，并附完整可运行代码。

## 一、GEM 的三组状态

GEM（SEMI E30）定义了设备的三组状态，联机排障时最先确认的就是它们：

| 状态组 | 取值 | 怎么切换 |
|---|---|---|
| **Communication** | NotCommunicating / Communicating | S1F13/S1F14 握手 |
| **Control** | Offline / Online-Local / Online-Remote | S1F15（离线）/ S1F17（在线）/ S2F17（远程） |
| **Processing** | Idle / Ready / Processing / Paused | 设备自身工艺逻辑 |

**联机排障第一步永远是问这三个问题**：通信建了吗？设备在线吗？在跑吗？——对应的就是这三组状态。

## 二、S1F13 握手：通信状态机

Host 连接设备后，第一件事就是发 S1F13 建立通信（注意：这跟 HSMS 的 Select 握手是两层——Select 是"链路层"，S1F13 是"应用层"）：

```
Host                          Device
  │  S1F13 W（请求建立通信）      │
  │ ──────────────────────────>  │
  │                              │ 通信状态: NotCommunicating → Communicating
  │  S1F14（COMMACK=0 成功）      │
  │ <──────────────────────────  │
```

开源库里的实现（`GemConnection`）：

```csharp
public async Task<bool> CommunicateAsync(CancellationToken ct = default)
{
    var request = new SecsMessage
    {
        StreamNumber = 1,
        FunctionNumber = 13,
        WaitBit = true,
        SystemBytes = NextSystemBytes(),
        Body = DataItem.Empty
    };

    var reply = await RequestAsync(request, ct);   // 发请求，等回复（10s 超时）
    CommunicationState = CommunicationState.Communicating;
    return true;
}
```

关键机制是**请求/回复匹配**：发请求时把 SystemBytes 登记到一个等待表，收到 W-bit=0 的回复时按 SystemBytes 匹配并唤醒——这就是 SECS 事务（Transaction）的核心。

```csharp
private void OnTransportMessageReceived(object? sender, HsmsMessage hsms)
{
    var secs = SecsMessage.FromHsmsMessage(hsms);

    // 回复匹配：W-bit=0 且事务编号命中等待中的请求
    if (!secs.WaitBit && _pendingRequests.TryRemove(secs.SystemBytes, out var tcs))
    {
        tcs.TrySetResult(secs);
        return;
    }

    // 否则是对方发来的请求，走消息分发
    _ = Task.Run(() => HandleIncomingAsync(secs), CancellationToken.None);
}
```

## 三、在线/离线切换：控制状态机

控制状态决定了设备**听谁的**：

| 状态 | 含义 | 切换消息 |
|---|---|---|
| Offline | 设备本地操作，不理 Host | S1F15 → 回 S1F16 |
| Online-Local | 可远程监控，控制权在本地 | S1F17 → 回 S1F18 |
| Online-Remote | 远程控制已使能 | S2F17 → 回 S2F18（由 Online-Local 进入） |

设备端收到 S1F15 的自动处理：

```csharp
case (1, 15): // 离线
    ControlState = ControlState.Offline;
    await ReplyAsync(request, 16, DataItem.List(DataItem.U1(0))); // OFLAACK=0
    break;
```

注意：**回复消息的 SystemBytes 必须原样带回请求的**——这是 SECS 协议的硬性规定，很多联机问题的根源就在这。

## 四、远程命令、事件与告警

控制状态到位后，GEM 的核心玩法：

- **远程命令（S2F41）**：Host 让设备干活，如 `START`、`ABORT`
- **事件上报（S6F11）**：设备主动通知 Host"发生了什么"
- **告警（S5F1）**：设备报告异常

完整流程（Host 配置 → 设备上报）：

```
Host                                   Device
  │ S2F33 定义 SVID（变量 1001/1002）     │
  │ S2F35 定义报告（报告1 = 变量1+2）      │
  │ S2F37 使能事件（CEID 1001）           │
  │ ──────────────────────────────────>  │
  │                              (设备发生工艺事件)
  │ <──────────────────────────────────  │
  │ S6F11 W                             │
  │ <L                                  │
  │   <U4 1001>            ← CEID       │
  │   <L                                 │
  │     <L                               │
  │       <U4 1>           ← RPTID      │
  │       <L                             │
  │         <U4 1001> <A "RUNNING">     │
  │         <U4 1002> <A "LOT-2026-0812">│
  │       >                              │
  │     >                                │
  │   >                                  │
  │ >                                    │
  │ .                                    │
```

设备端上报实现（按 CEID 定义自动组包）：

```csharp
public async Task SendEventAsync(ushort ceId, CancellationToken ct = default)
{
    var ce = _model.Ceids[ceId];
    var reports = new List<DataItem>();

    foreach (var reportId in ce.ReportIds)
    {
        var report = _model.Reports[reportId];
        var variables = report.SvidIds
            .Select(svid => DataItem.List(DataItem.U4(svid), _model.ReadSvid(svid)))
            .ToList();
        reports.Add(DataItem.List(DataItem.U4(reportId), DataItem.List(variables.ToArray())));
    }

    var body = DataItem.List(DataItem.U4(ceId), DataItem.List(reports.ToArray()));
    var message = new SecsMessage { StreamNumber = 6, FunctionNumber = 11, WaitBit = true,
                                    SystemBytes = NextSystemBytes(), Body = body };
    await SendAsync(message, ct);
}
```

## 五、全流程实测（开源库 Gem_Demo 真实输出）

同一台机器上：Device 端跑 `GemConnection`（Passive），Host 端用裸 HSMS 连接，完整走一遍：

```
[Host  ] >>> S1F13 建立通信
[Device] 通信状态: Communicating
[Host  ] 收到 S1F14: <L <U1 0>> .

[Host  ] >>> S2F33 定义 SVID
[Host  ] 收到 S2F34: <L <U1 0>> .

[Host  ] >>> S2F35 定义报告
[Host  ] 收到 S2F36: <L <U1 0>> .

[Host  ] >>> S2F37 使能 CEID 1001
[Host  ] 收到 S2F38: <L <U1 0>> .

[Host  ] >>> S2F41 远程命令 START
[Device] 远程命令: START args=[RECIPE_A]
[Host  ] 收到 S2F42: <L <U1 0>> .

[Host  ] 触发 Device 上报 S6F11 ...
[Host  ] 收到 S6F11 W:
<L <U4 1001> <L <L <U4 1> <L <U4 1001> <A "RUNNING"> <U4 1002> <A "LOT-2026-0812">>>>
```

设备从"会收发"到"懂规矩"：握手、状态切换、配置、命令、上报，一整套 GEM 行为跑通。

## 六、小结与预告

| 掌握程度 | 能力 |
|---|---|
| 前两篇 | 会收发消息、看懂 SML |
| 本篇 | 懂设备状态机、能实现 GEM 握手/事件/告警/命令 |
| 下一篇 | **GEM300（E87/E90/E94）**：300mm 晶圆厂的载具管理、基片追踪 |

配套开源库 [SecsGem.Net](https://github.com/MC-xiaohe/SecsGem.Net) 已实现 HSMS + SECS-II 完整编解码 + GEM 状态机，欢迎 Star。有 SECS/GEM 联机调试、GEM300 集成需求的同行欢迎交流。

*作者：EAP 自动化工程师，深耕 SECS/GEM 与 GEM300（E30/E37/E5/E87/E90/E94），提供定制开发与远程排障服务（晚 7 点后 + 周末）。*
