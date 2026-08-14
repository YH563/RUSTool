using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Services.Logging;
using RUSTool.Services.Robot;
using RUSTool.ViewModels.Connection;
using RUSTool.ViewModels.Robot;
using RUSTool.ViewModels.Scan;

namespace RUSTool.ViewModels;

public enum WorkspaceMode
{
    Debug,
    Clinical
}

public partial class MainViewModel : ViewModelBase
{
    private readonly IRobotService _robot;

    [ObservableProperty]
    private WorkspaceMode _currentMode = WorkspaceMode.Debug;

    /// <summary>机器人指令区域当前选中的模式（0=手动 / 1=扫查，与后端 set_mode 对齐）。</summary>
    [ObservableProperty]
    private int _robotModeIndex;

    /// <summary>全局共享状态（工具栏等绑定）。</summary>
    public RobotSession Session { get; }

    /// <summary>全局日志服务。</summary>
    public ILogService Log { get; }

    /// <summary>连接 / 驱动 VM。</summary>
    public ConnectionViewModel Connection { get; }

    /// <summary>机械臂手动控制 VM。</summary>
    public RobotControlViewModel Control { get; }

    /// <summary>扫查流程 VM。</summary>
    public ScanWorkflowViewModel Scan { get; }

    /// <summary>机械臂状态 HUD VM。</summary>
    public RobotStatusViewModel Status { get; }

    public MainViewModel(IRobotService robot, RobotSession session, ILogService log)
    {
        _robot = robot;
        Session = session;
        Log = log;
        Connection = new ConnectionViewModel(robot, session, log);
        Control = new RobotControlViewModel(robot, log);
        Scan = new ScanWorkflowViewModel(robot, log);
        Status = new RobotStatusViewModel(robot);
        // 连接成功（含断线自动重连）后，把当前模式同步给后端
        _robot.ConnectionChanged += SyncModeOnConnect;
    }

    /// <summary>连接成功时向后端同步当前模式（0=手动 / 1=扫查）。</summary>
    private void SyncModeOnConnect(bool connected)
    {
        if (connected)
            _ = _robot.SetMode(RobotModeIndex);
    }

    public bool IsDebugMode => CurrentMode == WorkspaceMode.Debug;
    public bool IsClinicalMode => CurrentMode == WorkspaceMode.Clinical;

    partial void OnCurrentModeChanged(WorkspaceMode value)
    {
        OnPropertyChanged(nameof(IsDebugMode));
        OnPropertyChanged(nameof(IsClinicalMode));
    }

    /// <summary>机器人指令模式切换时，向后端同步 set_mode（0=手动 / 1=扫查）。</summary>
    partial void OnRobotModeIndexChanged(int value)
    {
        _ = _robot.SetMode(value);
    }

    [RelayCommand]
    private void SwitchToDebug() => CurrentMode = WorkspaceMode.Debug;

    [RelayCommand]
    private void SwitchToClinical() => CurrentMode = WorkspaceMode.Clinical;
}