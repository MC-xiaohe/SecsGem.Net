# SECS/GEM 从零到一（四）：GEM300 实战——E87 载具管理与 E90 基片追踪

> 作者：MC-xiaohe（EAP 自动化工程师）
> 配套开源库：[SecsGem.Net](https://github.com/MC-xiaohe/SecsGem.Net)（C#，HSMS + SECS-II + GEM + GEM300 实现）

系列前篇：① [HSMS 通讯](https://zhuanlan.zhihu.com/p/2069768337253458775) ② [SECS-II 与 SML](https://zhuanlan.zhihu.com/p/2069773859717374424) ③ [GEM 状态机](https://zhuanlan.zhihu.com/p/2069776161538942087)。前三篇是"通用 GEM"，这一篇进入 **GEM300**——300mm 晶圆厂自动化的核心，也是设备过厂验的硬门槛。

## 一、GEM300 是什么

300mm 晶圆厂要求设备支持载具（FOUP）自动交接、基片级追踪，单靠基础 GEM 不够，SEMI 为此定义了一组标准：

| 标准 | 全称 | 管什么 |
|---|---|---|
| **E87** | Carrier Management | 载具（FOUP）在 LoadPort 上的生命周期 |
| **E90** | Substrate Tracking | 每片晶圆（基片）的进/出/位置追踪 |
| E94 | Control Job | 多设备批次作业协调 |
| E39/E40 | 对象服务 / 加工管理 | 对象模型与加工流程 |

**对设备商来说，E87/E90 合规几乎是进 300mm 厂的入场券**——这也是为什么懂 GEM300 的人值钱。

## 二、E87：LoadPort 状态机

E87 的核心是 LoadPort（装载端口）状态机。FOUP 在端口上的生命周期：

```
Empty ──载具到达──> Loaded ──ID读取/就绪──> Ready ──开始装载──> Processing
  ▲                                                              │
  └─────────── 卸载完成/载具离开 ────────────────────────────────┘
                              (Complete ← 加工完成)
```

对应到代码（开源库 `LoadPort` 模型）：

```csharp
public sealed class LoadPort
{
    public int Number { get; }              // 端口编号（1 起）
    public LoadPortState State { get; }     // Empty/Loaded/Ready/Processing/Complete/UnloadPending
    public string? CarrierId { get; }       // 载具 ID（FOUP 上的 RF/条码）
    public bool CarrierDetected { get; }    // 是否检测到载具
    public bool CarrierIdRead { get; }      // ID 是否读取成功
    public bool DoorOpen { get; }           // 门状态
    public SubstrateState[] Slots { get; }  // 25 个槽位（E90 基片追踪）
}
```

## 三、消息流：Host 发命令，设备报事件

E87 的玩法是"**Host 下命令（S2F41），设备报事件（S6F11）**"。以"装载一个 FOUP"为例：

```
Host                                     Device (LoadPort 1)
  │ S2F41 LOAD LP1 CARRIER-ABC123          │
  │ ────────────────────────────────────>  │ 状态机启动
  │ <────────────────────────────────────  │ S6F11 CEID=5003 CarrierDetect
  │ <────────────────────────────────────  │ S6F11 CEID=5004 CarrierIdRead
  │ <────────────────────────────────────  │ S6F11 CEID=5001 CarrierArrive
  │ <────────────────────────────────────  │ S6F11 CEID=5005 LoadStart
  │ <────────────────────────────────────  │ S6F11 CEID=5006 LoadComplete
  │ <────────────────────────────────────  │ S2F42 HCACK=0（命令接受）
```

设备端处理 LOAD 命令的核心逻辑：

```csharp
private async Task HandleLoadAsync(LoadPort port, List<string> args, CancellationToken ct)
{
    // 载具到达 → 检测 → ID 读取 → 装载（Empty → Loaded → Ready → Processing）
    port.CarrierDetected = true;
    await SendLoadPortEventAsync(port, E87EventIds.CarrierDetect, ct);

    port.CarrierId = args.Count > 1 ? args[1] : $"CARRIER-{port.Number:D2}";
    port.CarrierIdRead = true;
    await SendLoadPortEventAsync(port, E87EventIds.CarrierIdRead, ct);

    port.State = LoadPortState.Loaded;
    await SendLoadPortEventAsync(port, E87EventIds.CarrierArrive, ct);

    port.State = LoadPortState.Ready;
    port.DoorOpen = true;
    await SendLoadPortEventAsync(port, E87EventIds.LoadStart, ct);

    // 模拟装载：填充全部槽位（E90 基片追踪）
    for (int i = 0; i < port.SlotCount; i++)
        port.Slots[i] = SubstrateState.Present;

    port.State = LoadPortState.Processing;
    port.DoorOpen = false;
    await SendLoadPortEventAsync(port, E87EventIds.LoadComplete, ct);
}
```

事件上报（S6F11 组包，含端口号与载具 ID）：

```csharp
public async Task SendLoadPortEventAsync(LoadPort port, ushort ceId, CancellationToken ct = default)
{
    var body = DataItem.List(
        DataItem.U4(ceId),
        DataItem.List(
            DataItem.List(
                DataItem.U4((uint)port.Number),
                DataItem.A(port.CarrierId ?? string.Empty)
            )
        )
    );
    var message = new SecsMessage
    {
        StreamNumber = 6, FunctionNumber = 11, WaitBit = true,
        SystemBytes = NextSystemBytes(), Body = body
    };
    await SendAsync(message, ct);
}
```

## 四、实测：完整载具生命周期（开源库 Gem300_Demo 真实输出）

同一台机器：Device 端 `GemConnection`（含 2 个 25 槽 LoadPort）+ Host 端裸 HSMS。

**① LOAD：** 设备自动上报 5 个事件，状态机 Empty→Processing：

```
[Host  ] >>> S2F41 LOAD LP1 CARRIER-ABC123
[Host  ] S6F11 事件 CEID=5003 (CarrierDetect)
[Host  ] S6F11 事件 CEID=5004 (CarrierIdRead)
[Host  ] S6F11 事件 CEID=5001 (CarrierArrive)
[Host  ] S6F11 事件 CEID=5005 (LoadStart)
[Host  ] S6F11 事件 CEID=5006 (LoadComplete)
[Host  ] LP1 状态: LP1: Processing Carrier=CARRIER-ABC123 Detected=True IdRead=True Door=Closed
```

**② PROCESS：** 25 个槽位全部进入加工（E90 基片追踪）：

```
[Host  ] >>> S2F41 PROCESS LP1
[Host  ] LP1 加工完成槽位: 25/25
```

**③ UNLOAD：** 卸载事件流，状态机回 Empty：

```
[Host  ] >>> S2F41 UNLOAD LP1
[Host  ] S6F11 事件 CEID=5007 (UnloadStart)
[Host  ] S6F11 事件 CEID=5008 (UnloadComplete)
[Host  ] S6F11 事件 CEID=5002 (CarrierDepart)   ← 带原载具 ID
[Host  ] LP1 状态: LP1: Empty Carrier=- Detected=False IdRead=False Door=Closed
```

一个 FOUP 从进到出的完整生命周期，就是这套"命令 + 事件"的组合拳。**你设备跑一遍这个流程，Host 端全程无人工干预——这就是 300mm 厂要的自动化。**

## 五、小结

| 篇 | 内容 | 能力 |
|---|---|---|
| 一~三 | HSMS + SECS-II + GEM | 通用设备通讯 |
| **四** | **E87 载具管理 + E90 基片追踪** | 300mm 自动化入场券 |
| 五（规划） | E94 作业协调 / 模拟器 GUI | 进阶 |

开源库 [SecsGem.Net](https://github.com/MC-xiaohe/SecsGem.Net) 已实现全部前四篇代码，欢迎 Star。有 SECS/GEM 联机调试、GEM300（E87/E90）合规集成需求的同行欢迎交流。

*作者：EAP 自动化工程师，深耕 SECS/GEM 与 GEM300（E30/E37/E5/E87/E90/E94），提供定制开发与远程排障服务（晚 7 点后 + 周末）。*
