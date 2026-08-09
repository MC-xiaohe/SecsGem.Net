# 半导体设备自动化入门：手把手用 C# 实现 HSMS 通讯（SECS/GEM 零基础）

> 作者：王贺（EAP 自动化工程师）｜ 配套开源库：[SecsGem.Net](https://github.com/MC-xiaohe/SecsGem.Net)

做半导体、光伏、面板设备自动化的朋友应该都有体会：**SECS/GEM 的资料少、门槛高、中文内容更是稀缺**。新人拿到 SEMI 规范文档（E5/E30/E37），几百页英文直接劝退；网上能搜到的中文教程，要么是翻译腔，要么只讲概念不给能跑的代码。

这个系列我打算用**大白话 + 可运行代码**，把 SECS/GEM 从通讯层到 GEM300 完整讲一遍。今天开第一篇：**HSMS 通讯层**——把这层搞懂，你就打通了设备联机的第一道关。

## 一、先搞清楚：SECS/GEM 到底分几层

很多人一上来就懵，是因为把一堆标准混在一起。其实按职责分就三层：

| 层 | 标准 | 干什么的 |
|---|---|---|
| 传输层 | SECS-I (E4) / HSMS (E37) | 怎么把字节流发过去（串口 / TCP/IP） |
| 消息层 | SECS-II (E5) | 消息长什么样（S1F1、S6F11 这种） |
| 模型层 | GEM (E30) | 设备该有哪些行为（事件、告警、配方） |

现在工厂基本都用 **HSMS**（TCP/IP），SECS-I 串口已经很少见了。所以从 HSMS 入手最实际。

## 二、HSMS 消息长什么样

HSMS 的消息由三部分组成：

```
┌─────────────────────────────────────────────┐
│  Length (4字节)  │  Header (10字节)  │  Data  │
│  消息总长度(字)   │   消息头          │  数据   │
└─────────────────────────────────────────────┘
```

**注意：Length 字段的单位是"4 字节字"（word），不是字节。** 这是新人最容易踩的坑——发出去的数据对端解析出来全是乱的，先查这里。

Header 10 个字节的布局：

```
字节 0-1  Session ID（低7位 = Device ID）
字节 2    Stream（bit7 = W-bit，表示是否需要回复）
字节 3    Function
字节 4    PType（数据消息固定 0）
字节 5    SType（0=数据消息，1=Select请求，5=LinkTest...）
字节 6-9  System Bytes（事务编号，回复消息必须原样带回）
```

对应到代码（这是我开源库里的实现）：

```csharp
public readonly struct HsmsHeader
{
    public ushort SessionId { get; init; }   // 低7位 Device ID
    public byte Stream { get; init; }        // bit7 是 W-bit
    public byte Function { get; init; }
    public bool WaitBit { get; init; }       // W-bit
    public byte PType { get; init; }
    public HsmsSType SType { get; init; }
    public uint SystemBytes { get; init; }   // 回复时原样返回

    public static HsmsHeader Decode(ReadOnlySpan<byte> buffer)
    {
        return new HsmsHeader
        {
            SessionId = BinaryPrimitives.ReadUInt16BigEndian(buffer),
            Stream = (byte)(buffer[2] & 0x7F),          // 去掉 W-bit
            WaitBit = (buffer[2] & 0x80) != 0,
            Function = buffer[3],
            PType = buffer[4],
            SType = (HsmsSType)buffer[5],
            SystemBytes = BinaryPrimitives.ReadUInt32BigEndian(buffer[6..])
        };
    }
}
```

## 三、连接建立：Select 握手

HSMS 和 TCP 是两层：TCP 连上之后，还要做 **Select 握手**，连接状态才会从 Not Selected 变成 Selected，之后才能传数据消息。

流程（主动端视角）：

```
1. TCP 连接建立
2. 发送 Select.Request（SType=1）
3. 收到 Select.Response（SType=2），状态码 0 = 成功
4. 状态 → Selected，开始传数据
```

断开时发送 **Separate.Request（SType=9）** 优雅退出；平时用 **LinkTest.Request（SType=5）** 保活，对端必须回 LinkTest.Response。

接收循环的处理逻辑：

```csharp
switch (message.Header.SType)
{
    case HsmsSType.SelectResponse:
        // 校验 Data[0] == 0 则成功
        SetState(HsmsConnectionState.Selected);
        break;

    case HsmsSType.LinkTestRequest:
        // 收到保活请求，立即回复
        await SendRawAsync(HsmsMessage.CreateLinkTestResponse(message.Header.SystemBytes), ct);
        break;

    case HsmsSType.SeparateRequest:
        SetState(HsmsConnectionState.NotSelected);
        break;

    case HsmsSType.DataMessage:
        RaiseMessageReceived(message);  // 上抛给 GEM 层处理
        break;
}
```

## 四、超时参数（E37 必考）

联机老是不稳定，90% 是超时参数没配对：

| 参数 | 默认 | 含义 |
|---|---|---|
| T3 | 45s | 发了带 W-bit 的消息，等回复的最长时间 |
| T5 | 10s | 主动连接分离后重连的间隔 |
| T6 | 5s | 控制消息（Select/LinkTest）的回复超时 |
| T7 | 10s | 未进入 Selected 状态的时间限制 |
| T8 | 5s | 字符间超时（基本不用管） |

**经验**：现场联机慢，先把 T3 调大试试；T6 太短会导致 Select 握手"假失败"。

## 五、下一步

这篇把 HSMS 通讯层讲完了。下一篇写 **SECS-II 消息与 SML**：S1F1/S1F2 设备识别、S2F33 数据变量定义、S6F11 事件上报，附完整 C# 实现。

配套开源库 [SecsGem.Net](https://github.com/MC-xiaohe/SecsGem.Net) 正在开发中（C#，对标商业 SDK，含全中文文档），欢迎 Star 和提 Issue。有 SECS/GEM 联机调试、GEM300 集成需求的同行也欢迎交流。

---

如果这篇文章对你有帮助，点个 **赞同 + 收藏**，系列更新不迷路。评论区聊聊你踩过哪些联机的坑，下一篇我会挑高频问题重点讲。

*作者：EAP 自动化工程师，深耕 SECS/GEM 与 GEM300（E30/E37/E5/E87/E90/E94），提供定制开发与远程排障服务（晚 7 点后 + 周末）。*
