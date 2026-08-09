---
title: SECS/GEM 从零到一（二）：SECS-II 消息与 SML 实战，附开源库
tags: [SECS/GEM, SECS-II, SML, 半导体, EAP, C#, 设备自动化]
categories: [半导体自动化]
---

# SECS/GEM 从零到一（二）：SECS-II 消息与 SML 实战

> 作者：MC-xiaohe（EAP 自动化工程师）
> 原创声明：本文为作者原创，首发于 CSDN。
> 配套开源库：[SecsGem.Net](https://github.com/MC-xiaohe/SecsGem.Net)（C#，支持 HSMS + SECS-II 完整编解码 + SML）

上一篇（[《HSMS 通讯实战》](https://zhuanlan.zhihu.com/p/2069768337253458775)）打通了通讯层——TCP 连接、Select 握手、超时参数。但通讯层只负责"把字节流送过去"，**字节流里装的是什么，是 SECS-II 的事**。本文把它讲透，全部代码可在开源库直接跑。

## 一、SECS-II 消息的三要素

一条 SECS-II 消息长这样：`S1F1 W` 或 `S6F11 W`。拆开看：

| 要素 | 含义 | 例子 |
|---|---|---|
| **Stream** | 消息类别（1=设备状态，2=设备控制，5=告警，6=数据） | S**6**F11 |
| **Function** | 类别内的具体功能 | S6F**11**（事件上报） |
| **W-bit** | 是否需要对方回复 | S6F11 **W** |

再加上事务编号 System Bytes（回复时必须原样带回），就构成完整消息头。

**关键记忆点**：Stream 6 全是"数据类"消息，联机排查看到 S6 开头的报文，先想"这是数据上报"。

## 二、数据项与格式字节

消息 body 由**数据项**组成，SECS-II 定义了 14 种：

| 类型 | 格式码 | 说明 | SML 示例 |
|---|---|---|---|
| List | 0x00 | 容器，嵌套其他数据项 | `<L ...>` |
| Binary | 0x10 | 二进制字节 | `<B {01 02}>` |
| Boolean | 0x20 | 布尔 | `<BOOLEAN T F>` |
| ASCII | 0x30 | 字符串 | `<A "RUNNING">` |
| JIS-8 | 0x40 | 日文编码（少见） | `<J ...>` |
| F8 / F4 | 0x50 / 0x60 | 浮点 | `<F4 1.5>` |
| I8 / I1 / I2 / I4 | 0x70/0x78/0x80/0x88 | 有符号整数 | `<I4 123>` |
| U8 / U1 / U2 / U4 | 0x90/0x98/0xA0/0xA8 | 无符号整数 | `<U4 1001>` |

**格式字节是这系列最容易翻车的点**，我写开源库时就踩了：格式字节 = **格式码 | 长度字节数**。格式码（如 U4=0xA8）**低 2 位本身就是 0**，直接按位或即可——**千万别左移 2 位**，否则 U4 会被错编成 U2，对端解析全乱。

二进制布局（大端序）：

```
[Format Byte] [Length(1-3字节)] [Data...]
    0xA9      [0x04]           [00 00 03 E9]   ← <U4 1001>
    0x31      [0x07]           [52 55 4E 4E 49 4E 47]  ← <A "RUNNING">
```

## 三、SML：人类可读的 SECS 消息

SML（SECS Message Language，SEMI E5 附录）是 SECS 消息的文本表示，**现场抓包、设备商对接文档里到处是它**。看不懂 SML 就没法排障。

完整的 S6F11 事件上报消息：

```
S6F11 W
<L
  <U4 1001>              ← CEID：事件 ID
  <L                     ← 报告列表
    <U4 1>               ← RPTID：报告 ID
    <L                   ← 数据变量列表
      <A "PROCESS_STATE">
      <A "RUNNING">
    >
  >
>
.
```

结构一句话：**消息头 + 数据项树 + 结束符 `.`**。

## 四、实战：构造 → 编码 → 解析（真实运行输出）

以下是开源库 `samples/Sml_Demo` 的真实运行结果。

**① 用代码构造 S6F11：**

```csharp
var s6f11 = new SecsMessage
{
    StreamNumber = 6,
    FunctionNumber = 11,
    WaitBit = true,
    SystemBytes = 0x00001001,
    Body = DataItem.List(
        DataItem.U4(1001),                   // CEID
        DataItem.List(                       // 报告
            DataItem.U4(1),                  // RPTID
            DataItem.List(                   // 数据变量
                DataItem.A("PROCESS_STATE"),
                DataItem.A("RUNNING")
            )
        )
    )
};
```

**② 输出 SML（自动格式化）：**

```
S6F11 W
<L
  <U4 1001>
  <L
    <U4 1>
    <L
      <A "PROCESS_STATE">
      <A "RUNNING">
    >
  >
>
.
```

**③ 编码为线上报文（Hex）：**

```
0000000D 0000 86 0B 00001001
0128 A904 000003E9 0120 A904 00000001 0118 310D 50524F434553535F5354415445 3107 52554E4E494E47
```

对照解读：`0000000D` = Length（13 words），`86` = Stream 6 + W-bit，`0B` = F11，`A9 04 000003E9` = `<U4 1001>`，`31 07 52554E4E494E47` = `<A "RUNNING">`。**能看懂这行 Hex，联机排障就成功了一半。**

**④ 反向解析 SML 脚本（模拟器/测试场景）：**

```csharp
var parsed = SecsMessage.Parse(smlScript);   // 解析 SML 文本
var decoded = SecsMessage.FromHsmsMessage(parsed.ToHsmsMessage());  // 编码再解码
// 往返一致性验证通过
```

开源库已实现全部 14 种数据项 + SML 解析/输出，`SecsMessage.Parse()` 可直接当模拟器脚本引擎用。

## 五、必会的常用消息流

| 消息 | 方向 | 用途 |
|---|---|---|
| S1F1/S1F2 | Host→设备 / 回复 | 设备识别（型号、软件版本） |
| S1F13/S1F14 | Host→设备 / 回复 | 建立通信（GEM 握手） |
| S2F33/S2F34 | Host→设备 / 回复 | 定义数据变量（SVID） |
| S2F41/S2F42 | Host→设备 / 回复 | 远程命令（Remote Command） |
| S5F1 | 设备→Host | 告警上报 |
| S6F11 | 设备→Host | 事件上报（Collection Event） |
| S6F19/S6F20 | 设备→Host | 周期数据上报 |

## 六、总结与预告

本文讲完了 SECS-II 消息层与 SML。下一篇进入 **GEM（SEMI E30）状态机**：Control State / Processing State 怎么转、S1F13 握手全流程、事件/告警/配方管理——从"会收发消息"到"懂设备行为"的关键一跃。

配套开源库 [SecsGem.Net](https://github.com/MC-xiaohe/SecsGem.Net) 持续开发中（含全中文文档与 GEM300 E87/E90 示例规划），欢迎 Star 和 Issue。有 SECS/GEM 联机调试、GEM300 集成需求的同行欢迎交流。

---

*作者：EAP 自动化工程师，深耕 SECS/GEM 与 GEM300（E30/E37/E5/E87/E90/E94）。*
