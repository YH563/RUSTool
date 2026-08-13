using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Services;

namespace RUSTool.ViewModels;

public enum WorkspaceMode
{
    Debug,
    Clinical
}

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private WorkspaceMode _currentMode = WorkspaceMode.Debug;

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

    /// <summary>仿真控制 VM。</summary>
    public SimulationViewModel Sim { get; }

    /// <summary>机械臂状态 HUD VM。</summary>
    public RobotStatusViewModel Status { get; }

    public MainViewModel(IRobotService robot, RobotSession session, ILogService log)
    {
        Session = session;
        Log = log;
        Connection = new ConnectionViewModel(robot, session);
        Control = new RobotControlViewModel(robot);
        Scan = new ScanWorkflowViewModel(robot);
        Sim = new SimulationViewModel(robot);
        Status = new RobotStatusViewModel(robot);
    }

    public bool IsDebugMode => CurrentMode == WorkspaceMode.Debug;
    public bool IsClinicalMode => CurrentMode == WorkspaceMode.Clinical;

    partial void OnCurrentModeChanged(WorkspaceMode value)
    {
        OnPropertyChanged(nameof(IsDebugMode));
        OnPropertyChanged(nameof(IsClinicalMode));
    }

    [RelayCommand]
    private void SwitchToDebug() => CurrentMode = WorkspaceMode.Debug;

    [RelayCommand]
    private void SwitchToClinical() => CurrentMode = WorkspaceMode.Clinical;
}