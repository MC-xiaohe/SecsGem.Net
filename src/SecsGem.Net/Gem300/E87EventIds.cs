namespace SecsGem.Net.Gem300;

/// <summary>
/// E87 / E90 常用事件 ID（CEID）约定。
/// 实际工厂可能自定义编号，此处采用行业常见取值，便于演示与对接。
/// </summary>
public static class E87EventIds
{
    public const ushort CarrierArrive = 5001;      // 载具到达 LoadPort
    public const ushort CarrierDepart = 5002;      // 载具离开 LoadPort
    public const ushort CarrierDetect = 5003;      // 检测到载具
    public const ushort CarrierIdRead = 5004;      // 载具 ID 读取成功
    public const ushort LoadStart = 5005;          // 开始装载
    public const ushort LoadComplete = 5006;       // 装载完成
    public const ushort UnloadStart = 5007;        // 开始卸载
    public const ushort UnloadComplete = 5008;     // 卸载完成
}

/// <summary>E90 基片追踪事件 ID。</summary>
public static class E90EventIds
{
    public const ushort TrackIn = 6001;            // 基片进入设备
    public const ushort TrackOut = 6002;           // 基片离开设备
}
