# SecsGem.Net

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6.0%2B-blueviolet)](https://dotnet.microsoft.com)
[![PRs Welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](CONTRIBUTING.md)

轻量级、纯托管的 **SECS/GEM & GEM300** 通讯库（C#），对标商业 SDK（Cimetrix / 台达 DIASECS）的核心能力，面向半导体、光伏、面板、LED 行业的设备厂商与工厂自动化工程师。

A lightweight, fully managed **SECS/GEM & GEM300** communication library in C#, designed as an open-source alternative to commercial SDKs (Cimetrix / Delta DIASECS) for semiconductor, PV, panel and LED equipment vendors and factory automation engineers.

> 🌟 特色：**全中文文档 + GEM300 完整示例**（E87 / E90 / E94）——这是同类开源项目最缺的部分。
>
> 🌟 Highlight: **Full Chinese documentation + complete GEM300 samples** (E87 / E90 / E94) — the most-missing part in existing open-source projects.

---

## ✨ 特性 Features

### 通讯层 Communication
- ✅ **HSMS (SEMI E37)**：主动 / 被动模式（Active / Passive）
- ✅ T3 / T5 / T6 / T7 超时与重试机制
- ✅ Link Test、心跳、断线重连
- ✅ 支持 TCP/IP，预留 SECS-I (E4) 串口扩展点

### 消息层 Messaging
- ✅ **SECS-II (SEMI E5)**：完整数据类型（A / B / Boolean / U1-U8 / I1-I8 / F4 / F8 / List）
- ✅ SML 文本解析与序列化（支持 `S1F1`、`S6F11` 等常见消息流）
- ✅ 事务（Transaction）管理与 Message ID 分配
- ✅ 原始报文日志（Hex + SML 双格式，方便排障）

### GEM 层 (SEMI E30)
- ✅ Control State / Processing State 状态机（S1F13 握手、S1F15/F17/F18 在线离线切换）
- ✅ Collection Events 与 Reports（S2F33/S2F35/S2F37/S6F11）
- ✅ Alarms（S5F1/S5F3）与 Remote Commands（S2F41）
- ⬜ Recipe 管理（S2F15/S2F31，v0.3 规划）
- ✅ SVID 数据变量管理

### GEM300（护城河 Moat）
- ✅ **E87 (SEMI E87)**：LoadPort / Carrier 状态机完整实现（LOAD/UNLOAD/PROCESS 命令流 + 事件上报）
- ✅ **E90 (SEMI E90)**：基片（Substrate）追踪（槽位级状态）
- ⬜ **E94 (SEMI E94)**：Control Job 管理（规划中）
- ⬜ E39 / E40 基础对象模型（规划中）

### 工具 Tools
- ✅ 内置 **Equipment 模拟器**（WinForms / WPF），无需真实设备即可联调
- ✅ Host 端测试台：一键发送 S1F1 / S2F33 / S6F11 等常用消息
- ✅ 报文查看器：实时抓包、过滤、导出

---

## 🚀 快速开始 Quick Start

### 安装 Install

```bash
dotnet add package SecsGem.Net   # NuGet（发布后可用）
```

### 最小示例：建立 HSMS 连接并查询设备状态

```csharp
using SecsGem.Net;
using SecsGem.Net.Messaging;

// 1. 创建并配置通讯端点（设备端示例）
var endpoint = new HsmsEndpoint
{
    IpAddress = "192.168.1.100",
    Port = 5000,
    DeviceId = 1,
    Mode = ConnectionMode.Active,   // 主动连接 Host
    Timeouts = new HsmsTimeouts { T3 = 45, T5 = 10, T6 = 5, T7 = 10 }
};

using var gem = new GemConnection(endpoint);

// 2. 订阅事件（设备状态上报、告警等）
gem.EventReceived += (s, e) => Console.WriteLine($"[Event] {e.CeId}: {e.Report}");
gem.AlarmReceived += (s, e) => Console.WriteLine($"[Alarm] {e.AlarmId} {e.AlarmText}");

// 3. 连接 Host 并完成 GEM 握手
await gem.ConnectAsync();
await gem.CommunicateAsync();               // S1F13/S1F14 建立通信

// 4. 主动上报一个事件（S6F11）
await gem.SendEventAsync(ceId: 1001, new Report
{
    { "PROCESS_STATE", "RUNNING" },
    { "LOT_ID", "LOT-2026-0812" }
});

Console.WriteLine("GEM 联机成功，等待 Host 指令...");
await Task.Delay(Timeout.Infinite);
```

> 完整可运行示例见 [`samples/`](samples/)：`E87_CarrierManagement`、`E90_SubstrateTracking`、`Host_ConsoleTest`。

---

## 📁 项目结构 Structure

```
SecsGem.Net/
├── src/
│   ├── SecsGem.Net/           # 核心库（NuGet 包）
│   │   ├── Hsms/              # HSMS 通讯层（E37）
│   │   ├── Messaging/         # SECS-II 消息模型与 SML（E5）
│   │   ├── Gem/               # GEM 状态机与对象模型（E30）
│   │   ├── Gem300/            # E87 / E90 / E94 实现
│   │   └── Logging/           # 报文日志（Hex + SML）
│   ├── SecsGem.Net.Simulator/ # 设备模拟器（WPF）
│   └── SecsGem.Net.HostTest/  # Host 端测试台
├── samples/
│   ├── E87_CarrierManagement/ # 载具管理完整示例
│   ├── E90_SubstrateTracking/ # 基片追踪完整示例
│   └── Basic_Connect/         # 最小连接示例
├── docs/
│   ├── zh-CN/                 # 中文文档（协议解析、状态机图解）
│   └── en/
└── tests/
    └── SecsGem.Net.Tests/     # xUnit 单元测试
```

---

## 🗺 路线图 Roadmap

| 阶段 | 内容 | 状态 |
|------|------|------|
| v0.1 | HSMS 通讯 + SECS-II 消息模型 + SML | ✅ 完成 |
| v0.2 | GEM (E30) 状态机 + 事件/告警/远程命令 + SML 编解码 | ✅ 完成 |
| v0.3 | GEM300：E87 / E90 实现 + 载具命令流 | ✅ 完成 |
| v0.4 | E94 Control Job + 设备模拟器 GUI | 规划中 |
| v1.0 | NuGet 发布 + 中文文档站 | 目标 |

---

## 🤝 参与贡献 Contributing

欢迎 PR、Issue 与使用反馈！请先阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。

计划参与方向：
- 补充 SECS-I（E4）串口支持
- E94 Control Job / E39 对象服务实现
- 更多设备厂验场景示例（Etch / CVD / 清洗机 / 分选机）
- 英文文档翻译

---

## 📝 系列文章 Articles

- [半导体设备自动化入门：手把手用 C# 实现 HSMS 通讯（SECS/GEM 零基础）](https://zhuanlan.zhihu.com/p/2069768337253458775) — 知乎（2026-08）
- [半导体设备自动化（二）：SECS-II 消息与 SML，看懂报文的另一半](https://zhuanlan.zhihu.com/p/2069773859717374424) — 知乎（2026-08）
- [半导体设备自动化（三）：GEM 状态机实战——从“会收发消息”到“懂设备行为”](https://zhuanlan.zhihu.com/p/2069776161538942087) — 知乎（2026-08）
- [半导体设备自动化（四）：GEM300 实战——E87 载具管理与 E90 基片追踪](https://zhuanlan.zhihu.com/p/2069777496942318647) — 知乎（2026-08）
- E94 Control Job / 设备模拟器（规划中）

---

## 📄 许可证 License

[MIT](LICENSE)

---

## ☎️ 联系作者 Contact

- 作者：MC-xiaohe（EAP 自动化工程师，C# / SECS-GEM / GEM300）
- 服务：SECS/GEM 定制开发、GEM300 集成、远程联机排障（晚 7 点后 + 周末）
- 欢迎同行交流，也欢迎设备商 / 工厂自动化项目合作

> 免责声明：本项目为个人开源项目，与任何商业 SDK 无关联，不包含任何商业产品代码。
