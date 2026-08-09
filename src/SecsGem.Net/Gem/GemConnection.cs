using System.Collections.Concurrent;
using SecsGem.Net.Gem300;
using SecsGem.Net.Hsms;
using SecsGem.Net.Messaging;

namespace SecsGem.Net.Gem;

/// <summary>设备基本信息（S1F2 回复用）。</summary>
public sealed class GemConfig
{
    public string ModelName { get; set; } = "SecsGem.Net Device";
    public string SoftwareVersion { get; set; } = "v0.2.0";
    public string HardwareVersion { get; set; } = "Rev.A";
}

/// <summary>远程命令事件参数（S2F41）。</summary>
public sealed class RemoteCommandEventArgs : EventArgs
{
    public string Command { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
}

/// <summary>事件上报参数（S6F11）。</summary>
public sealed class EventReceivedEventArgs : EventArgs
{
    public ushort CeId { get; init; }
    public Report Report { get; init; } = new();
}

/// <summary>告警参数（S5F1）。</summary>
public sealed class AlarmReceivedEventArgs : EventArgs
{
    public ushort AlarmId { get; init; }
    public bool Set { get; init; }
    public string AlarmText { get; init; } = string.Empty;
}

/// <summary>报告（RPTID → 数据变量集合）。</summary>
public sealed class Report : Dictionary<string, object>
{
    public Report()
    {
    }

    public Report(IDictionary<string, object> values) : base(values)
    {
    }
}

/// <summary>
/// GEM（SEMI E30）连接：在 HSMS 之上实现设备侧 GEM 行为。
/// 已实现：S1F13/F14 通信建立、S1F15/F16 离线、S1F17/F18 在线、
/// S2F33/F34 定义变量、S2F35/F36 定义报告、S2F37/F38 使能事件、
/// S2F41/F42 远程命令、S5F3/F4 使能告警、S6F11 事件上报、S5F1 告警上报。
/// </summary>
public sealed class GemConnection : IDisposable
{
    private readonly HsmsConnection _transport;
    private readonly GemModel _model;
    private readonly GemConfig _config;
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<SecsMessage>> _pendingRequests = new();
    private uint _systemBytes;

    public GemConnection(HsmsEndpoint endpoint, GemConfig? config = null, GemModel? model = null)
    {
        _transport = new HsmsTcpConnection(endpoint);
        _config = config ?? new GemConfig();
        _model = model ?? new GemModel();
        _transport.MessageReceived += OnTransportMessageReceived;
        _transport.StateChanged += (_, state) => StateChanged?.Invoke(this, state);
    }

    // ---------- 状态 ----------

    public CommunicationState CommunicationState { get; private set; } = CommunicationState.NotCommunicating;

    public ControlState ControlState { get; private set; } = ControlState.Offline;

    public ProcessingState ProcessingState { get; private set; } = ProcessingState.Idle;

    /// <summary>LoadPort 集合（SEMI E87 载具管理）。</summary>
    public List<LoadPort> LoadPorts { get; } = new();

    // ---------- 事件 ----------

    public event EventHandler<HsmsConnectionState>? StateChanged;
    public event EventHandler<CommunicationState>? CommunicationStateChanged;
    public event EventHandler<ControlState>? ControlStateChanged;
    public event EventHandler<SecsMessage>? MessageReceived;
    public event EventHandler<EventReceivedEventArgs>? EventReceived;
    public event EventHandler<AlarmReceivedEventArgs>? AlarmReceived;
    public event EventHandler<RemoteCommandEventArgs>? RemoteCommandReceived;

    // ---------- 连接与通信 ----------

    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        _transport.ConnectAsync(cancellationToken);

    /// <summary>S1F13/S1F14 建立通信（GEM 握手）。</summary>
    public async Task<bool> CommunicateAsync(CancellationToken cancellationToken = default)
    {
        var request = new SecsMessage
        {
            StreamNumber = 1,
            FunctionNumber = 13,
            WaitBit = true,
            SystemBytes = NextSystemBytes(),
            Body = DataItem.Empty
        };

        var reply = await RequestAsync(request, cancellationToken);
        // TODO: 校验 COMMACK（reply 第一个 U1 = 0 表示成功）
        CommunicationState = CommunicationState.Communicating;
        CommunicationStateChanged?.Invoke(this, CommunicationState);
        return true;
    }

    /// <summary>上报事件（S6F11）：按 CEID 定义从模型取报告与 SVID 值。</summary>
    public async Task SendEventAsync(ushort ceId, CancellationToken cancellationToken = default)
    {
        if (!_model.Ceids.TryGetValue(ceId, out var ce))
            throw new ArgumentException($"未定义的 CEID: {ceId}。");

        var reports = new List<DataItem>();
        foreach (var reportId in ce.ReportIds)
        {
            if (!_model.Reports.TryGetValue(reportId, out var report))
                continue;

            var variables = new List<DataItem>();
            foreach (var svid in report.SvidIds)
                variables.Add(DataItem.List(DataItem.U4(svid), _model.ReadSvid(svid)));

            reports.Add(DataItem.List(DataItem.U4(reportId), DataItem.List(variables.ToArray())));
        }

        var body = DataItem.List(DataItem.U4(ceId), DataItem.List(reports.ToArray()));
        var message = new SecsMessage
        {
            StreamNumber = 6,
            FunctionNumber = 11,
            WaitBit = true,
            SystemBytes = NextSystemBytes(),
            Body = body
        };

        await SendAsync(message, cancellationToken);
    }

    /// <summary>上报告警（S5F1）。</summary>
    public async Task SendAlarmAsync(ushort alarmId, bool set, string text, CancellationToken cancellationToken = default)
    {
        var body = DataItem.List(
            DataItem.U4(alarmId),
            DataItem.Boolean(set),
            DataItem.A(text)
        );
        var message = new SecsMessage
        {
            StreamNumber = 5,
            FunctionNumber = 1,
            WaitBit = true,
            SystemBytes = NextSystemBytes(),
            Body = body
        };

        await SendAsync(message, cancellationToken);
    }

    // ---------- 内部：收发 ----------

    private async Task<SecsMessage> RequestAsync(SecsMessage request, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<SecsMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[request.SystemBytes] = tcs;

        try
        {
            await SendAsync(request, cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            return await tcs.Task.WaitAsync(timeoutCts.Token);
        }
        finally
        {
            _pendingRequests.TryRemove(request.SystemBytes, out _);
        }
    }

    private Task SendAsync(SecsMessage message, CancellationToken cancellationToken) =>
        _transport.SendAsync(message.ToHsmsMessage(), cancellationToken);

    private async Task ReplyAsync(SecsMessage request, byte function, DataItem body)
    {
        var reply = new SecsMessage
        {
            DeviceId = request.DeviceId,
            StreamNumber = request.StreamNumber,
            FunctionNumber = function,
            WaitBit = false,
            SystemBytes = request.SystemBytes, // 回复必须原样带回事务编号
            Body = body
        };

        await SendAsync(reply, CancellationToken.None);
    }

    private uint NextSystemBytes() => ++_systemBytes;

    // ---------- 接收与分发 ----------

    private void OnTransportMessageReceived(object? sender, HsmsMessage hsmsMessage)
    {
        if (!hsmsMessage.Header.IsDataMessage)
            return;

        var secs = SecsMessage.FromHsmsMessage(hsmsMessage);
        MessageReceived?.Invoke(this, secs);

        // 回复匹配：W-bit=0 且事务编号命中等待中的请求
        if (!secs.WaitBit && _pendingRequests.TryRemove(secs.SystemBytes, out var tcs))
        {
            tcs.TrySetResult(secs);
            return;
        }

        _ = Task.Run(() => HandleIncomingAsync(secs), CancellationToken.None);
    }

    private async Task HandleIncomingAsync(SecsMessage request)
    {
        try
        {
            switch ((request.StreamNumber, request.FunctionNumber))
            {
                case (1, 1): // 设备识别
                    await ReplyAsync(request, 2, BuildS1F2());
                    break;

                case (1, 13): // 建立通信
                    CommunicationState = CommunicationState.Communicating;
                    CommunicationStateChanged?.Invoke(this, CommunicationState);
                    await ReplyAsync(request, 14, DataItem.List(DataItem.U1(0))); // COMMACK=0
                    break;

                case (1, 15): // 离线
                    ControlState = ControlState.Offline;
                    ControlStateChanged?.Invoke(this, ControlState);
                    await ReplyAsync(request, 16, DataItem.List(DataItem.U1(0))); // OFLAACK=0
                    break;

                case (1, 17): // 在线-本地
                    ControlState = ControlState.OnlineLocal;
                    ControlStateChanged?.Invoke(this, ControlState);
                    await ReplyAsync(request, 18, DataItem.List(DataItem.U1(0))); // ONLAACK=0
                    break;

                case (2, 17): // 在线-远程
                    ControlState = ControlState.OnlineRemote;
                    ControlStateChanged?.Invoke(this, ControlState);
                    await ReplyAsync(request, 18, DataItem.List(DataItem.U1(0)));
                    break;

                case (2, 33): // 定义 SVID
                    HandleDefineSvid(request);
                    await ReplyAsync(request, 34, DataItem.List(DataItem.U1(0))); // ACKC6=0
                    break;

                case (2, 35): // 定义报告
                    HandleDefineReport(request);
                    await ReplyAsync(request, 36, DataItem.List(DataItem.U1(0)));
                    break;

                case (2, 37): // 使能/禁用事件
                    HandleEnableCeid(request);
                    await ReplyAsync(request, 38, DataItem.List(DataItem.U1(0)));
                    break;

                case (2, 41): // 远程命令
                    await HandleRemoteCommandAsync(request);
                    break;

                case (5, 3): // 使能/禁用告警
                    HandleEnableAlarm(request);
                    await ReplyAsync(request, 4, DataItem.List(DataItem.U1(0)));
                    break;

                case (5, 1): // 对端告警上报（作为 Host 时）
                    AlarmReceived?.Invoke(this, ParseAlarm(request));
                    break;

                case (6, 11): // 对端事件上报（作为 Host 时）
                    EventReceived?.Invoke(this, ParseEvent(request));
                    break;
            }
        }
        catch (Exception ex)
        {
            // 单条消息处理失败不影响连接
            System.Diagnostics.Debug.WriteLine($"[Gem] 处理 S{request.StreamNumber}F{request.FunctionNumber} 失败: {ex.Message}");
        }
    }

    // ---------- 消息构造与解析 ----------

    private DataItem BuildS1F2() => DataItem.List(
        DataItem.A(_config.ModelName),
        DataItem.A(_config.SoftwareVersion),
        DataItem.A(_config.HardwareVersion)
    );

    private void HandleDefineSvid(SecsMessage request)
    {
        foreach (var entry in GetList(request.Body))
        {
            var parts = GetList(entry);
            if (parts.Count < 2)
                continue;

            ushort svid = AsU4(parts[0]);
            string name = (parts[1] as AsciiItem)?.Value ?? string.Empty;
            _model.Svids[svid] = new SvidDefinition { Svid = svid, Name = name };
        }
    }

    private void HandleDefineReport(SecsMessage request)
    {
        foreach (var entry in GetList(request.Body))
        {
            var parts = GetList(entry);
            if (parts.Count < 2)
                continue;

            ushort reportId = AsU4(parts[0]);
            var definition = new ReportDefinition { ReportId = reportId };
            foreach (var svidItem in GetList(parts[1]))
                definition.SvidIds.Add(AsU4(svidItem));

            _model.Reports[reportId] = definition;
        }
    }

    private void HandleEnableCeid(SecsMessage request)
    {
        var items = GetList(request.Body);
        if (items.Count == 0)
            return;

        // 单事件：L[U4, BOOLEAN]
        if (items[0] is UInt4Item)
        {
            ApplyCeidEnable(items);
            return;
        }

        // 多事件：L[L[U4, BOOLEAN], ...]
        foreach (var entry in items)
            ApplyCeidEnable(GetList(entry));
    }

    private void ApplyCeidEnable(List<DataItem> parts)
    {
        if (parts.Count < 2)
            return;

        ushort ceId = AsU4(parts[0]);
        bool enabled = parts[1] is BooleanItem b && b.Value.Length > 0 && b.Value[0];

        if (_model.Ceids.TryGetValue(ceId, out var ce))
            ce.Enabled = enabled;
    }

    private void HandleEnableAlarm(SecsMessage request)
    {
        var items = GetList(request.Body);
        if (items.Count < 2)
            return;

        ushort alarmId = AsU4(items[0]);
        bool enabled = items[1] is BooleanItem b && b.Value.Length > 0 && b.Value[0];

        if (_model.Alarms.TryGetValue(alarmId, out var alarm))
            alarm.Enabled = enabled;
    }

    private async Task HandleRemoteCommandAsync(SecsMessage request)
    {
        var items = GetList(request.Body);
        string command = items.Count > 0 && items[0] is AsciiItem a ? a.Value : string.Empty;

        var args = new List<string>();
        if (items.Count > 1 && items[1] is ListItem argList)
        {
            foreach (var arg in argList.Items)
            {
                if (arg is AsciiItem argText)
                    args.Add(argText.Value);
            }
        }

        RemoteCommandReceived?.Invoke(this, new RemoteCommandEventArgs { Command = command, Arguments = args });

        // E87 远程命令：LOAD / UNLOAD / PROCESS（第一参数为目标 LoadPort，如 "LP1" 或 "1"）
        if (args.Count > 0 && TryGetLoadPort(args[0], out var port))
        {
            switch (command.ToUpperInvariant())
            {
                case "LOAD":
                    await HandleLoadAsync(port, args, CancellationToken.None);
                    break;
                case "UNLOAD":
                    await HandleUnloadAsync(port, CancellationToken.None);
                    break;
                case "PROCESS":
                    await HandleProcessAsync(port, CancellationToken.None);
                    break;
            }
        }

        await ReplyAsync(request, 42, DataItem.List(DataItem.U1(0))); // HCACK=0 接受
    }

    // ---------- E87 载具流程 ----------

    /// <summary>上报 LoadPort 事件（S6F11，含端口号与载具 ID）。</summary>
    public async Task SendLoadPortEventAsync(LoadPort port, ushort ceId, CancellationToken cancellationToken = default)
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
            StreamNumber = 6,
            FunctionNumber = 11,
            WaitBit = true,
            SystemBytes = NextSystemBytes(),
            Body = body
        };

        await SendAsync(message, cancellationToken);
    }

    private async Task HandleLoadAsync(LoadPort port, List<string> args, CancellationToken ct)
    {
        // 载具到达 → 检测 → ID 读取 → 装载（E87 状态机：Empty → Loaded → Ready → Processing）
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

    private async Task HandleProcessAsync(LoadPort port, CancellationToken ct)
    {
        // 模拟加工：Present → Processing → Complete
        for (int i = 0; i < port.SlotCount; i++)
        {
            if (port.Slots[i] == SubstrateState.Present)
                port.Slots[i] = SubstrateState.Processing;
        }

        await Task.Delay(300, ct);

        for (int i = 0; i < port.SlotCount; i++)
        {
            if (port.Slots[i] == SubstrateState.Processing)
                port.Slots[i] = SubstrateState.Complete;
        }

        port.State = LoadPortState.Complete;
    }

    private async Task HandleUnloadAsync(LoadPort port, CancellationToken ct)
    {
        // 卸载：Complete → UnloadPending → Empty
        string departingCarrierId = port.CarrierId ?? string.Empty;

        port.State = LoadPortState.UnloadPending;
        await SendLoadPortEventAsync(port, E87EventIds.UnloadStart, ct);

        port.State = LoadPortState.Empty;
        port.CarrierId = null;
        port.CarrierDetected = false;
        port.CarrierIdRead = false;
        Array.Fill(port.Slots, SubstrateState.Empty);

        await SendLoadPortEventAsync(port, E87EventIds.UnloadComplete, ct);

        // 载具离开事件需要带上原载具 ID
        port.CarrierId = departingCarrierId;
        await SendLoadPortEventAsync(port, E87EventIds.CarrierDepart, ct);
        port.CarrierId = null;
    }

    private bool TryGetLoadPort(string arg, out LoadPort port)
    {
        port = null!;
        string number = arg.TrimStart('L', 'P', 'l', 'p');
        if (!int.TryParse(number, out int n))
            return false;

        var found = LoadPorts.FirstOrDefault(p => p.Number == n);
        if (found is null)
            return false;

        port = found;
        return true;
    }

    private static AlarmReceivedEventArgs ParseAlarm(SecsMessage message)
    {
        var items = GetList(message.Body);
        return new AlarmReceivedEventArgs
        {
            AlarmId = items.Count > 0 ? (ushort)AsU4(items[0]) : (ushort)0,
            Set = items.Count > 1 && items[1] is BooleanItem b && b.Value.Length > 0 && b.Value[0],
            AlarmText = items.Count > 2 && items[2] is AsciiItem text ? text.Value : string.Empty
        };
    }

    private static EventReceivedEventArgs ParseEvent(SecsMessage message)
    {
        var items = GetList(message.Body);
        var report = new Report();

        // 简化：S6F11 结构为 L[U4 CEID, L[...]]，这里仅提取 CEID
        ushort ceId = items.Count > 0 ? AsU4(items[0]) : (ushort)0;
        if (items.Count > 1)
        {
            foreach (var rpt in GetList(items[1]))
            {
                var rptParts = GetList(rpt);
                if (rptParts.Count >= 2)
                    report[$"RPT{AsU4(rptParts[0])}"] = rptParts[1].ToSml();
            }
        }

        return new EventReceivedEventArgs { CeId = ceId, Report = report };
    }

    // ---------- 工具 ----------

    private static List<DataItem> GetList(DataItem item) =>
        item is ListItem list ? list.Items.ToList() : new List<DataItem>();

    private static ushort AsU4(DataItem item) => item switch
    {
        UInt4Item u4 => (ushort)u4.Value[0],
        _ => 0
    };

    public void Dispose() => _transport.Dispose();
}
