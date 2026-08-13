using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace RUSTool.Services;

/// <summary>
/// 机械臂操作模式。手动控制与扫查流程互斥：
/// 二者都只能从 Idle 进入，回到 Idle 后才能切换另一模式。
/// </summary>
public enum RobotMode
{
    /// <summary>空闲：可进入手动或扫查。</summary>
    Idle,

    /// <summary>手动控制：MoveJ / MoveL / 点动。</summary>
    Manual,

    /// <summary>扫查流程：预扫描 → 位姿 → 规划 → 执行。</summary>
    Scan
}

/// <summary>
/// 全局唯一的共享状态对象（单例注入）。
/// 集中存放会被多个 VM 同时关心 / 竞争的状态，消除「同一状态多处拷贝」导致的不同步。
///
/// 写者约定（唯一 owner）：
///   IsConnected / IsEnabled / Driver —— ConnectionViewModel
///   Mode —— 本类的 TryEnter* / ExitToIdle（互斥仲裁）
///   IsPaused —— 暂停 / 恢复命令
/// 其余 VM 只读。
/// </summary>
public partial class RobotSession : ObservableObject
{
    /// <summary>控制通道是否在线。</summary>
    [ObservableProperty] private bool _isConnected;

    /// <summary>机械臂是否已上使能。</summary>
    [ObservableProperty] private bool _isEnabled;

    /// <summary>当前驱动：0=真实，1=仿真。</summary>
    [ObservableProperty] private int _driver = 1;

    /// <summary>是否处于暂停。</summary>
    [ObservableProperty] private bool _isPaused;

    private RobotMode _mode = RobotMode.Idle;

    /// <summary>当前操作模式（互斥仲裁，外部只能通过 TryEnter* / ExitToIdle 改变）。</summary>
    public RobotMode Mode
    {
        get => _mode;
        private set
        {
            if (_mode == value)
                return;
            _mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ModeText));
            ModeChanged?.Invoke(value);
        }
    }

    /// <summary>模式变化通知（供各 VM 刷新命令 CanExecute）。</summary>
    public event Action<RobotMode>? ModeChanged;

    /// <summary>驱动显示文本（工具栏绑定）。</summary>
    public string DriverText => Driver == 0 ? "真实" : "仿真";

    /// <summary>连接状态显示文本（工具栏绑定）。</summary>
    public string ConnectText => IsConnected ? "已连接" : "未连接";

    /// <summary>使能状态显示文本（工具栏绑定）。</summary>
    public string EnableText => IsEnabled ? "已使能" : "未使能";

    /// <summary>运动状态显示文本（含暂停，工具栏绑定）。</summary>
    public string ModeText => IsPaused ? "已暂停"
        : Mode switch
        {
            RobotMode.Manual => "手动",
            RobotMode.Scan => "扫查中",
            _ => "空闲"
        };

    partial void OnDriverChanged(int value) => OnPropertyChanged(nameof(DriverText));
    partial void OnIsPausedChanged(bool value) => OnPropertyChanged(nameof(ModeText));
    partial void OnIsConnectedChanged(bool value) => OnPropertyChanged(nameof(ConnectText));
    partial void OnIsEnabledChanged(bool value) => OnPropertyChanged(nameof(EnableText));

    /// <summary>尝试进入手动模式。仅在 Idle 时成功，返回是否成功。</summary>
    public bool TryEnterManual()
    {
        if (Mode != RobotMode.Idle)
            return false;
        Mode = RobotMode.Manual;
        return true;
    }

    /// <summary>尝试进入扫查模式。仅在 Idle 时成功，返回是否成功。</summary>
    public bool TryEnterScan()
    {
        if (Mode != RobotMode.Idle)
            return false;
        Mode = RobotMode.Scan;
        return true;
    }

    /// <summary>回到空闲（急停 / 复位 / 流程完成时调用），并清除暂停标记。</summary>
    public void ExitToIdle()
    {
        IsPaused = false;
        Mode = RobotMode.Idle;
    }
}
