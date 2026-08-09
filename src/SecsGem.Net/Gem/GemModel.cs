using SecsGem.Net.Messaging;

namespace SecsGem.Net.Gem;

/// <summary>数据变量定义（SVID，S2F33 定义）。</summary>
public sealed class SvidDefinition
{
    public ushort Svid { get; init; }
    public string Name { get; init; } = string.Empty;
}

/// <summary>报告定义（RPTID，S2F35 定义）：一组数据变量。</summary>
public sealed class ReportDefinition
{
    public ushort ReportId { get; init; }
    public List<ushort> SvidIds { get; } = new();
}

/// <summary>事件定义（CEID，S2F37 使能）：一组报告。</summary>
public sealed class CeidDefinition
{
    public ushort Ceid { get; init; }
    public string Name { get; init; } = string.Empty;
    public List<ushort> ReportIds { get; } = new();

    /// <summary>是否已使能（S2F37 设置）。</summary>
    public bool Enabled { get; set; }
}

/// <summary>告警定义（ALID，S5F3 使能）。</summary>
public sealed class AlarmDefinition
{
    public ushort AlarmId { get; init; }
    public string Text { get; init; } = string.Empty;
    public bool Enabled { get; set; }
}

/// <summary>
/// GEM 模型：SVID / 报告 / 事件 / 告警注册表。
/// 设备侧在启动时预定义，Host 可通过 S2F33/S2F35/S2F37 动态修改。
/// </summary>
public sealed class GemModel
{
    public Dictionary<ushort, SvidDefinition> Svids { get; } = new();
    public Dictionary<ushort, ReportDefinition> Reports { get; } = new();
    public Dictionary<ushort, CeidDefinition> Ceids { get; } = new();
    public Dictionary<ushort, AlarmDefinition> Alarms { get; } = new();

    /// <summary>
    /// 设备侧取值回调：根据 SVID 返回当前值。
    /// 未设置时返回空字符串，用于演示/测试。
    /// </summary>
    public Func<ushort, DataItem>? SvidValueProvider { get; set; }

    public SvidDefinition DefineSvid(ushort svid, string name)
    {
        var def = new SvidDefinition { Svid = svid, Name = name };
        Svids[svid] = def;
        return def;
    }

    public ReportDefinition DefineReport(ushort reportId, params ushort[] svidIds)
    {
        var def = new ReportDefinition { ReportId = reportId };
        def.SvidIds.AddRange(svidIds);
        Reports[reportId] = def;
        return def;
    }

    public CeidDefinition DefineCeid(ushort ceId, string name, params ushort[] reportIds)
    {
        var def = new CeidDefinition { Ceid = ceId, Name = name };
        def.ReportIds.AddRange(reportIds);
        Ceids[ceId] = def;
        return def;
    }

    public AlarmDefinition DefineAlarm(ushort alarmId, string text)
    {
        var def = new AlarmDefinition { AlarmId = alarmId, Text = text };
        Alarms[alarmId] = def;
        return def;
    }

    /// <summary>读取 SVID 当前值（经回调）。</summary>
    public DataItem ReadSvid(ushort svid) =>
        SvidValueProvider?.Invoke(svid) ?? DataItem.A(string.Empty);
}
