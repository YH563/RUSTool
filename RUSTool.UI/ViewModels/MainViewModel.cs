using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Communication;
using RUSTool.Services.Logging;
using RUSTool.Services.Robot;
using RUSTool.Settings;
using System;
using System.Collections.Specialized;
using System.Threading.Tasks;

namespace RUSTool.UI.ViewModels;

/// <summary>
/// 主 ViewModel —— 界面的组装点。
///
/// <para>
/// 依赖图（传输 → 业务 → 共享状态 / 日志 → VM → View）在 <c>App.axaml.cs</c> 里组装，
/// 这里只接收已经建好的服务与共享状态，自己不 new 任何具体实现 ——
/// 换后端（真机 / 仿真 / 回放）不用动这一层。
/// </para>
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    private readonly IRobotService _robot;

    public MainViewModel(IRobotService robot, RobotSession session, ILogService log, SettingsService settings)
    {
        _robot = robot;
        LogService = log;
        Settings = settings;

        // /sensor 的点云帧只在这里过一道手：服务层抛帧 → 视图层订阅（3D 视口的数据入口）。
        // 本层不认识 RUSTool.Visualization，也不认识 GL —— 转成视口能懂的形状是视图层的事。
        _robot.SensorFrameReceived += frame => PointCloudFrameReceived?.Invoke(frame);
        _robot.PcMapFrameReceived += frame => PcMapFrameReceived?.Invoke(frame);
        _robot.MeshFrameReceived += frame => MeshFrameReceived?.Invoke(frame);

        // 构造顺序：日志最先，其余 VM 都要往里写。
        Log = new LogViewModel(log);

        // 临床视图的错误条：只跟日志里【最新一条错误】。日志集合本身已在 UI 线程更新，
        // 这里直接赋属性即安全（无需再 Post）。
        log.Entries.CollectionChanged += OnLogEntriesChanged;
        Session = new SessionViewModel(robot, session, log);
        Status = new RobotStatusViewModel(robot);
        Charts = new TorqueChartViewModel(robot);
        Control = new RobotControlViewModel(robot, log);
        Scan = new ScanWorkflowViewModel(robot, log, Control);

        // 规划轨迹（plan_done 事件）从扫查 VM 转发到 3D 视图 —— 与点云同一条「VM → 视图」出口。
        Scan.TrajectoryGenerated += xyz => TrajectoryReceived?.Invoke(xyz);
        Replay = new ReplayViewModel(settings, log);
        Recorder = new RecorderViewModel(robot, log);

        // 本地回放把读出的两路数据喂回与实时完全相同的下游：
        // 状态帧 → HUD + 曲线（PublishStateFrame），点云帧 → 3D 视口（PublishPointCloud）。
        Replay.StateFramePlayed += PublishStateFrame;
        Replay.PointCloudPlayed += PublishPointCloud;

        // 进入回放前的"接管"：查运动 → 兜底 stop → 断开后端。回放本身是本地行为。
        Replay.RequestTakeover = RequestReplayTakeoverAsync;

        // 回放暂停期间若用户又连上后端（连接按钮在暂停时可用），下一次播放要重新接管 ——
        // 否则实时流会和回放同时写 HUD / 曲线 / 3D。
        // ⚠ SessionViewModel.RaiseAll() 每次变化都会重发 IsConnected，
        //   所以不能只看 PropertyName —— 必须用「上一次的连接态」判断真正的翻转，
        //   否则 ApplyRobotMode 改 Mode → 又重发 IsConnected → 无限递归（栈溢出）。
        Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SessionViewModel.IsConnected))
                return;

            bool connected = Session.IsConnected;
            if (connected == _lastConnected)
                return;

            _lastConnected = connected;
            if (!connected)
                return;

            Replay.ResetTakeover();
            // 连上后按当前 tab 显式下发并仲裁：bridge 默认是「自动」，不主动切就会与页签不符。
            _ = ApplyRobotModeAsync(RobotModeIndex);

            // 重建通道按需开：连上后按当前开关补订阅（/sensor 由 SessionViewModel.Connect 负责）。
            if (ShowPcMap) _robot.StartPcMapStream();
            if (ShowMesh) _robot.StartMeshStream();
        };
    }

    /// <summary>上一次观察到的连接态，用于识别「真正的连接翻转」（见构造函数里的说明）。</summary>
    private bool _lastConnected;

    /// <summary>最近一条错误日志（仅错误级）；空串 = 无错误。临床视图用它显示错误条。</summary>
    [ObservableProperty]
    private string _lastError = "";

    /// <summary>错误条是否可见（无错误时整条隐藏）。</summary>
    public bool HasError => !string.IsNullOrEmpty(LastError);

    partial void OnLastErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    /// <summary>日志新增一条：错误级就更新错误条；清空日志（Reset）时一并清掉。</summary>
    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            LastError = "";
            return;
        }

        if (e.NewItems is null)
            return;

        foreach (LogEntry entry in e.NewItems)
        {
            if (entry.IsError)
                LastError = entry.Source.Length == 0 ? entry.Message : $"{entry.Source}: {entry.Message}";
        }
    }

    /// <summary>本地互斥仲裁：进入指定页签对应的操作模式（不发指令）。</summary>
    private void ApplyRobotModeLocal(int mode)
    {
        if (mode == 1)
            Session.EnterScanMode();
        else
            Session.EnterManualMode();
    }

    /// <summary>
    /// 切换操作模式：连上时**总是显式下发 `set_mode`**（即使页签值没变），最后做本地仲裁。
    ///
    /// <para>
    /// <b>为什么"总是发"：</b>后端 bridge 的默认路由模式是「自动」，而前端默认页签是「手动」——
    /// 只按"页签值变化"发指令，两者会从一开始就不同步。
    /// </para>
    /// <para>
    /// <b>切回手动时补发 `reset`（而非 `stop`）：</b>后端 <c>stop</c> 是**急停**语义——它会把
    /// 驱动执行器置为 <c>STOPPED</c>，而 <c>start_jog</c> 只入队、不会把状态拉回 <c>RUNNING</c>
    /// （见后端 <c>TrajectoryExecutor</c> / <c>RobotSimDriver::StartJOG</c>）。所以在连接 / 切模式时
    /// 顺手发 <c>stop</c>，会让点动**只进队列不执行**、机械臂完全不动（必须 `reset` 才恢复）。
    /// 而扫查中止时 planning 会向 driver 发 <c>stop</c>，同样会残留 <c>STOPPED</c>；
    /// 因此这里用 <c>reset</c> 收尾：既清空残余伺服 / 队列，又把执行器恢复到 <c>RUNNING</c>，
    /// 手动点动随即可用。
    /// </para>
    /// </summary>
    private async Task ApplyRobotModeAsync(int mode)
    {
        if (Session.IsConnected)
        {
            await _robot.SetMode(mode);
            if (mode == 0)
                await _robot.ResetAsync(); // 切手动：清扫查残留并把执行器拉回 RUNNING，保证点动可用
        }

        ApplyRobotModeLocal(mode);
    }

    /// <summary>全局参数（录音目录 / bridge 地址…），由组装层注入。</summary>
    public SettingsService Settings { get; }

    /// <summary>
    /// 回放接管：进入本地回放前调用。
    ///
    /// <para>
    /// 顺序：<b>①</b> 有运动 / 扫查在进行 → 拒绝；<b>②</b> 兜底下发 <c>stop</c> 并等回执，
    /// 失败则中止（绝不带着未知运动状态断开）；<b>③</b> 断开后端连接（停 /state、/sensor 流）。
    /// 断开后本地回放才独占 HUD / 曲线 / 3D，不会与实时流打架。
    /// </para>
    /// </summary>
    private async Task<bool> RequestReplayTakeoverAsync()
    {
        if (Control.IsJogging || Scan.IsRunning || Session.IsScanning)
        {
            LogService.Log("运动 / 扫查进行中，无法进入回放：请先停止", LogLevel.Warn, "replay");
            return false;
        }

        if (Session.IsConnected)
        {
            var r = await _robot.StopAsync();
            if (!r.Success)
            {
                LogService.Log($"进入回放前停止失败（{r.Message}），已取消回放", LogLevel.Error, "replay");
                return false;
            }

            Session.DisconnectCommand.Execute(null);
        }

        LogService.Log("已进入本地回放：后端连接已断开", LogLevel.Info, "replay");
        return true;
    }

    /// <summary>
    /// 感知点云帧（<c>/sensor</c> 通道）—— 由 3D 视图订阅，把帧交给视口。
    ///
    /// <para>
    /// <b>后台线程触发</b>（WebSocket 线程），且是覆盖式的：订阅者只该把帧丢进视口的邮箱，
    /// 不要在这里碰界面元素。没接后端时这个事件永远不触发，界面照旧。
    /// </para>
    /// </summary>
    public event Action<SensorPointCloudFrame>? PointCloudFrameReceived;

    /// <summary>面元点云图（<c>/pcmap</c>）——由 3D 视图订阅。后台线程触发。</summary>
    public event Action<SensorPointCloudFrame>? PcMapFrameReceived;

    /// <summary>增量网格（<c>/mesh</c>）——由 3D 视图订阅。后台线程触发。</summary>
    public event Action<MeshFrame>? MeshFrameReceived;

    /// <summary>
    /// 规划轨迹（<c>plan_done</c> 事件的 result）——扁平三维点序列 <c>[x,y,z, …]</c>（m，base_link），
    /// 由 3D 视图订阅后画成一条路径。空数组 = 清空。后台线程触发。
    /// </summary>
    public event Action<float[]>? TrajectoryReceived;

    /// <summary>
    /// 注入一帧点云到自己抛出的那条事件上（与真实流【同一个出口】）。
    ///
    /// <para>
    /// 只给截图模式用（<c>Program.cs</c> 的 <c>--demo-cloud</c>）：本机没有后端可连时，
    /// 这是唯一能把「点云解码 → 投递 → 场景图层 → GL」这条链路画进 PNG 的办法。
    /// 真实运行时不调用它。
    /// </para>
    /// </summary>
    /// <param name="frame">要注入的帧（通常是 <c>DemoSensorFrame.Build()</c> 的产物）。</param>
    public void PublishPointCloud(SensorPointCloudFrame frame) => PointCloudFrameReceived?.Invoke(frame);

    /// <summary>
    /// 注入一条规划轨迹到自己抛出的那条事件上（与真实 <c>plan_done</c> 【同一个出口】）。
    /// 只给截图模式用（<c>Program.cs</c> 的 <c>--demo-trajectory</c>）；真实运行时不调用它。
    /// </summary>
    /// <param name="xyz">扁平三维点序列 <c>[x,y,z, …]</c>（m，base_link）；空数组 = 清空。</param>
    public void PublishTrajectory(float[] xyz) => TrajectoryReceived?.Invoke(xyz);

    /// <summary>
    /// 注入一帧状态帧（<c>/state</c>）到【订阅了状态流的两个消费者】上（HUD 与六路曲线），
    /// 效果与真机收到一帧完全相同。
    ///
    /// <para>
    /// 只给截图模式用（<c>Program.cs</c> 的 <c>--demo-torque</c>）：没有后端可连时，
    /// 这是唯一能把「状态帧解码 → HUD 读数 + 力矩曲线」这条链路画进 PNG 的办法。
    /// 一帧同时喂给两处 —— 与真机上一帧被两处接收的方式一致。
    /// </para>
    /// <para>
    /// <b>必须在 UI 线程调用</b>：曲线那侧直接改绑定源集合。<c>Dispatcher.UIThread.Post</c>
    /// 是异步的，截图模式等的是同步效果，所以这里不走 <c>Post</c>。
    /// </para>
    /// </summary>
    /// <param name="state">要注入的帧（通常是 <c>DemoStateFrame.Build()</c> 的产物）。</param>
    public void PublishStateFrame(BridgeProtocol.StateFrame state)
    {
        Status.PushFrame(state);
        Charts.PushFrame(state);
    }


    /// <summary>
    /// 日志服务本体（<see cref="Log"/> 面板展示的就是它的集合）。
    /// 供视图层写诊断用 —— 3D 视口的就绪 / 初始化失败原因要进同一个面板与同一份落盘文件；
    /// 这是 Core 的接口（不是控件类型），所以把它暴露给视图不违反「VM 不认识图形栈」。
    /// </summary>
    public ILogService LogService { get; }

    // ── 子 ViewModel（界面按这些名字绑定）──

    /// <summary>会话状态：连接 / 使能 / 驱动 / 运动模式。</summary>
    public SessionViewModel Session { get; }

    /// <summary>机械臂实时状态 HUD。</summary>
    public RobotStatusViewModel Status { get; }

    /// <summary>实时曲线（六路关节力矩，窗口滚动）。</summary>
    public TorqueChartViewModel Charts { get; }

    /// <summary>手动 / 点动控制。</summary>
    public RobotControlViewModel Control { get; }

    /// <summary>扫查流程。</summary>
    public ScanWorkflowViewModel Scan { get; }

    /// <summary>全局日志面板。</summary>
    public LogViewModel Log { get; }

    /// <summary>记录 / 回放。</summary>
    public ReplayViewModel Replay { get; }

    /// <summary>录制控制（开始 / 停止 / 状态，全局旁路模块）。</summary>
    public RecorderViewModel Recorder { get; }

    /// <summary>true = 工程师模式（3D + 影像 + 曲线 + 控制台），false = 临床模式。</summary>
    [ObservableProperty]
    private bool _isDebugMode = true;

    /// <summary>3D 视口是否显示感知点云（默认显示）。关掉可排除点云与模型的遮挡/深度冲突。</summary>
    [ObservableProperty]
    private bool _showPointCloud = true;

    /// <summary>是否显示面元点云图（<c>/pcmap</c>，重建融合地图）。默认显示；开关同时启停该流。</summary>
    [ObservableProperty]
    private bool _showPcMap = true;

    /// <summary>是否显示增量网格（<c>/mesh</c>）。默认关；开关同时启停该流。</summary>
    [ObservableProperty]
    private bool _showMesh;

    /// <summary>3D 视口是否显示规划轨迹（plan_done 生成）。默认显示；纯显隐开关，不涉及订阅流。</summary>
    [ObservableProperty]
    private bool _showTrajectory = true;

    partial void OnShowPcMapChanged(bool value)
    {
        if (!Session.IsConnected) return;
        if (value) _robot.StartPcMapStream(); else _robot.StopPcMapStream();
    }

    partial void OnShowMeshChanged(bool value)
    {
        if (!Session.IsConnected) return;
        if (value) _robot.StartMeshStream(); else _robot.StopMeshStream();
    }

    public bool IsClinicalMode => !IsDebugMode;

    /// <summary>工具栏上的模式标签文案。</summary>
    public string ModeName => IsDebugMode ? "工程师模式" : "临床模式";

    /// <summary>"机器人指令"卡片里手动 / 扫查两个面板的切换（0=手动 / 1=扫查，与后端 set_mode 对齐）。</summary>
    [ObservableProperty]
    private int _robotModeIndex;

    /// <summary>手动面板是否为当前页（供分段标签的选中态绑定，避免值转换器）。</summary>
    public bool IsManualMode => RobotModeIndex == 0;

    /// <summary>扫查面板是否为当前页（供分段标签的选中态绑定）。</summary>
    public bool IsScanMode => RobotModeIndex == 1;

    partial void OnIsDebugModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsClinicalMode));
        OnPropertyChanged(nameof(ModeName));
    }

    /// <summary>机器人指令模式切换（含 Carousel 翻页）：同步给后端并做本地模式仲裁。</summary>
    partial void OnRobotModeIndexChanged(int value)
    {
        _ = ApplyRobotModeAsync(value);
        OnPropertyChanged(nameof(IsManualMode));
        OnPropertyChanged(nameof(IsScanMode));
    }

    [RelayCommand]
    private void SwitchToDebug() => IsDebugMode = true;

    [RelayCommand]
    private void SwitchToClinical() => IsDebugMode = false;

    /// <summary>
    /// 「机器人指令」卡片里手动 / 扫查两个面板的切换（参数 "0" = 手动 / "1" = 扫查）。
    /// 与当前页签相同也强制重发一次（见 <see cref="ApplyRobotModeAsync"/>）——
    /// 这样"点手动"总能保证后端真的切到手动、并清掉扫查残留。
    /// </summary>
    [RelayCommand]
    private void SelectRobotMode(object? parameter)
    {
        int mode = parameter?.ToString() == "1" ? 1 : 0;
        if (RobotModeIndex == mode)
            _ = ApplyRobotModeAsync(mode);
        else
            RobotModeIndex = mode; // 变值：由 OnRobotModeIndexChanged 走同一条路
    }

    /// <summary>
    /// 「重置视角」请求。3D 视口的相机是图形栈内部的显示状态（属于视图层），
    /// VM 既不认识也不该碰它 —— 这里只发信号，由 3D 视图自己复位相机。
    /// 于是菜单项可以用普通 Command 绑定，无需把控件类型泄漏进 VM。
    /// </summary>
    public event Action? ViewResetRequested;

    [RelayCommand]
    private void ResetView() => ViewResetRequested?.Invoke();
}
