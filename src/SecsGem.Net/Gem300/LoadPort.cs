namespace SecsGem.Net.Gem300;

/// <summary>LoadPort 状态（SEMI E87 状态模型，简化版）。</summary>
public enum LoadPortState
{
    /// <summary>空，无载具。</summary>
    Empty,

    /// <summary>载具已到达/已放置，未就绪。</summary>
    Loaded,

    /// <summary>载具就绪（ID 已读取、门可开），可开始加工。</summary>
    Ready,

    /// <summary>正在加工。</summary>
    Processing,

    /// <summary>加工完成，等待卸载。</summary>
    Complete,

    /// <summary>正在卸载。</summary>
    UnloadPending
}

/// <summary>基片（Substrate）状态（SEMI E90 简化版）。</summary>
public enum SubstrateState
{
    /// <summary>槽位为空。</summary>
    Empty,

    /// <summary>基片在位（已装载）。</summary>
    Present,

    /// <summary>正在加工。</summary>
    Processing,

    /// <summary>加工完成。</summary>
    Complete
}

/// <summary>
/// LoadPort（装载端口）：管理一个载具（Carrier/FOUP）的生命周期。
/// 状态流转：Empty → Loaded → Ready → Processing → Complete → (卸载) → Empty
/// </summary>
public sealed class LoadPort
{
    public LoadPort(int number, int slotCount = 25)
    {
        Number = number;
        SlotCount = slotCount;
        Slots = new SubstrateState[slotCount];
    }

    /// <summary>端口编号（1 起）。</summary>
    public int Number { get; }

    /// <summary>槽位数量（300mm FOUP 为 25，200mm 为 13）。</summary>
    public int SlotCount { get; }

    /// <summary>当前状态。</summary>
    public LoadPortState State { get; internal set; } = LoadPortState.Empty;

    /// <summary>载具 ID（Carrier ID，读取后有效）。</summary>
    public string? CarrierId { get; internal set; }

    /// <summary>是否检测到载具。</summary>
    public bool CarrierDetected { get; internal set; }

    /// <summary>载具 ID 是否已读取成功。</summary>
    public bool CarrierIdRead { get; internal set; }

    /// <summary>门是否打开。</summary>
    public bool DoorOpen { get; internal set; }

    /// <summary>各槽位基片状态（E90 基片追踪）。</summary>
    public SubstrateState[] Slots { get; }

    public override string ToString() =>
        $"LP{Number}: {State} Carrier={CarrierId ?? "-"} Detected={CarrierDetected} IdRead={CarrierIdRead} Door={(DoorOpen ? "Open" : "Closed")}";
}
