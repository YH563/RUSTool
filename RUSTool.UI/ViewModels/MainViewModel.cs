using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Communication;
using RUSTool.Services.Logging;
using RUSTool.Services.Robot;
using System;

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

    public MainViewModel(IRobotService robot, RobotSession session, ILogService log)
    {
        _robot = robot;
        LogService = log;

        // /sensor 的点云帧只在这里过一道手：服务层抛帧 → 视图层订阅（3D 视口的数据入口）。
        // 本层不认识 RUSTool.Visualization，也不认识 GL —— 转成视口能懂的形状是视图层的事。
        _robot.SensorFrameReceived += frame => PointCloudFrameReceived?.Invoke(frame);

        // 构造顺序：日志最先，其余 VM 都要往里写。
        Log = new LogViewModel(log);
        Session = new SessionViewModel(robot, session, log);
        Status = new RobotStatusViewModel(robot);
        Charts = new TorqueChartViewModel(robot);
        Control = new RobotControlViewModel(robot, log);
        Scan = new ScanWorkflowViewModel(robot, log);
        Replay = new ReplayViewModel(log);
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

    /// <summary>true = 工程师模式（3D + 影像 + 曲线 + 控制台），false = 临床模式。</summary>
    [ObservableProperty]
    private bool _isDebugMode = true;

    public bool IsClinicalMode => !IsDebugMode;

    /// <summary>工具栏上的模式标签文案。</summary>
    public string ModeName => IsDebugMode ? "工程师模式" : "临床模式";

    /// <summary>"机器人指令"卡片里手动 / 扫查两个面板的切换（0=手动 / 1=扫查，与后端 set_mode 对齐）。</summary>
    [ObservableProperty]
    private int _robotModeIndex;

    partial void OnIsDebugModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsClinicalMode));
        OnPropertyChanged(nameof(ModeName));
    }

    /// <summary>机器人指令模式切换后同步给后端（0=手动 / 1=扫查）。</summary>
    partial void OnRobotModeIndexChanged(int value) => _ = _robot.SetMode(value);

    [RelayCommand]
    private void SwitchToDebug() => IsDebugMode = true;

    [RelayCommand]
    private void SwitchToClinical() => IsDebugMode = false;

    /// <summary>
    /// 「重置视角」请求。3D 视口的相机是图形栈内部的显示状态（属于视图层），
    /// VM 既不认识也不该碰它 —— 这里只发信号，由 3D 视图自己复位相机。
    /// 于是菜单项可以用普通 Command 绑定，无需把控件类型泄漏进 VM。
    /// </summary>
    public event Action? ViewResetRequested;

    [RelayCommand]
    private void ResetView() => ViewResetRequested?.Invoke();
}
