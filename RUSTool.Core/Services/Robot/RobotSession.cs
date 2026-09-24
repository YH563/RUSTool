using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace RUSTool.Services.Robot;

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
/// 驱动类型。取值即后端编码（<c>get_driver_type</c> 的返回值 / <c>switch_driver</c> 的 type 参数
/// 都是 <c>0 = 仿真（sim）</c> / <c>1 = 真实（real）</c>），前后端只有一个编码，不做二次映射。
///
/// <para>
/// 为什么不用 bool：驱动不是「是/否」而是「哪一种」，而且它可能**未知** ——
/// 未连接 / 掉线 / 回读失败时界面不该假装知道当前是哪个驱动（见 <see cref="RobotSession.IsDriverKnown"/>）。
/// </para>
/// </summary>
public enum RobotDriver
{
    /// <summary>仿真驱动（sim）—— 后端编码 0。</summary>
    Simulation = 0,

    /// <summary>真实驱动（real，连真机）—— 后端编码 1。</summary>
    Real = 1
}

/// <summary>
/// 驱动类型 ↔ 协议编码的转换。驱动类型在协议里是 double（JSON 只有数值数组），
/// 转换只在这里写一次，界面拿到的一律是 <see cref="RobotDriver"/>。
/// </summary>
public static class RobotDriverCodec
{
    /// <summary>协议编码 → 业务类型。只有 <c>1</c> 算真实，其余（含异常值）一律按仿真处理。</summary>
    public static RobotDriver FromProtocol(double value)
        => value == (double)RobotDriver.Real ? RobotDriver.Real : RobotDriver.Simulation;

    /// <summary>业务类型 → 协议编码（<c>switch_driver</c> 的 args[0]）。</summary>
    public static double ToProtocol(RobotDriver driver) => (double)driver;
}

/// <summary>
/// 全局唯一的共享状态对象（单例注入）。
/// 集中存放会被多个 VM 同时关心 / 竞争的状态，消除「同一状态多处拷贝」导致的不同步。
///
/// 写者约定（唯一 owner）：
///   IsConnected / IsEnabled / Driver / IsDriverKnown —— SessionViewModel
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

    /// <summary>
    /// 当前驱动。**只有后端说了算**：连接后由 <c>get_driver_type</c> 回读填充，
    /// 本地点按钮只是「请求切换」，不代表后端真的切过去了。
    /// </summary>
    [ObservableProperty] private RobotDriver _driver = RobotDriver.Simulation;

    /// <summary>
    /// 驱动类型是否已知 —— 只有「连上且回读成功」之后才算已知。
    /// 未连接 / 掉线 / 回读失败一律 false，界面据此把驱动分段按钮整体变灰：
    /// 不知道是哪个驱动时，任何「已选中」都是在撒谎。
    /// </summary>
    [ObservableProperty] private bool _isDriverKnown;

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

    /// <summary>驱动显示文本（工具栏绑定）。未回读到后端状态时显示「未知」，不猜。</summary>
    public string DriverText => !IsDriverKnown ? "未知"
        : Driver == RobotDriver.Real ? "真实" : "仿真";

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

    partial void OnDriverChanged(RobotDriver value) => OnPropertyChanged(nameof(DriverText));
    partial void OnIsDriverKnownChanged(bool value) => OnPropertyChanged(nameof(DriverText));
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
