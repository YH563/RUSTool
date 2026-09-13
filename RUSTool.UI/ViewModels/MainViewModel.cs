using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        // 构造顺序：日志最先，其余 VM 都要往里写。
        Log = new LogViewModel(log);
        Session = new SessionViewModel(robot, session, log);
        Status = new RobotStatusViewModel(robot);
        Control = new RobotControlViewModel(robot, log);
        Scan = new ScanWorkflowViewModel(robot, log);
        Replay = new ReplayViewModel(log);
    }

    // ── 子 ViewModel（界面按这些名字绑定）──

    /// <summary>会话状态：连接 / 使能 / 驱动 / 运动模式。</summary>
    public SessionViewModel Session { get; }

    /// <summary>机械臂实时状态 HUD。</summary>
    public RobotStatusViewModel Status { get; }

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
