using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Services.Logging;
using RUSTool.Services.Robot;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace RUSTool.UI.ViewModels;

/// <summary>
/// 会话状态：连接 / 使能 / 驱动 / 运动模式。
///
/// <para>
/// 这是【连接类动作的唯一入口】—— 其余 VM 只读状态、不发连接指令，
/// 避免"同一状态多处拷贝"导致互相不同步。
/// </para>
/// <para>
/// 真正的状态存放在共享的 <see cref="RobotSession"/>（单例注入）里。本类只做两件事：
/// 把界面动作翻译成 <see cref="IRobotService"/> 调用，再把 <see cref="RobotSession"/>
/// 的状态翻译成界面可绑定的【互斥布尔量】。
/// </para>
/// <para>
/// 状态灯与分段按钮各自绑定一个互斥布尔量（<c>Classes.success="{Binding IsConnected}"</c>），
/// 于是不需要 <c>BoolToBrushConverter</c> 这类转换器 —— "状态 → 颜色"的映射全部交给
/// 主题的语义类，换主题时它们会跟着变。
/// </para>
/// </summary>
public sealed partial class SessionViewModel : ViewModelBase
{
    private readonly IRobotService _robot;
    private readonly RobotSession _session;
    private readonly ILogService _log;

    /// <summary>急停是否已按下（本地界面状态：后端只回执 stop，没有独立的急停通道）。</summary>
    [ObservableProperty]
    private bool _isEmergencyStopped;

    /// <summary>最近一次操作的错误提示（空串 = 无错误），由界面在工具栏附近展示。</summary>
    [ObservableProperty]
    private string _errorMessage = "";

    public SessionViewModel(IRobotService robot, RobotSession session, ILogService log)
    {
        _robot = robot;
        _session = session;
        _log = log;

        // 断线自动重连也会走到这里，界面因此能自动回到"已连接"；连上后顺带回读后端驱动类型。
        _robot.ConnectionChanged += OnConnectionChanged;
        _session.PropertyChanged += OnSessionChanged;
    }

    // ── 转发共享状态（界面直接绑定这些）──

    public bool IsConnected => _session.IsConnected;

    public bool IsEnabled => _session.IsEnabled;

    public string ConnectText => _session.ConnectText;

    /// <summary>运动状态文本；急停按下后覆盖为"急停"。</summary>
    public string ModeText => IsEmergencyStopped ? "急停" : _session.ModeText;

    // ── 状态灯的互斥布尔量 ──
    // 四个灯位互斥（同一时刻只有一个为真），界面把它们叠在同一个 Ellipse 上，
    // 于是"状态 → 颜色"不再需要一个转换器。

    /// <summary>空闲：未连接，或已连接但不在任何操作模式。</summary>
    public bool IsIdle => !IsConnected || _session.Mode == RobotMode.Idle;

    /// <summary>手动模式（MoveJ / MoveL / 点动）。</summary>
    public bool IsManual => IsConnected && !IsHalted && _session.Mode == RobotMode.Manual;

    /// <summary>扫查模式（预扫描 → 位姿 → 规划 → 执行）。</summary>
    public bool IsScanning => IsConnected && !IsHalted && _session.Mode == RobotMode.Scan;

    /// <summary>已暂停（pause 之后、resume 之前）。</summary>
    public bool IsHalted => _session.IsPaused;

    /// <summary>
    /// 驱动类型是否已从后端回读。未连接 / 掉线 / 回读失败都是 false ——
    /// 此时"真实 / 仿真"两个按钮一起变灰（都不选中），因为驱动类型本来就是后端的状态，
    /// 没连上就无从知道，任何"已选中"都是在撒谎。
    /// </summary>
    public bool IsDriverKnown => _session.IsDriverKnown;

    /// <summary>驱动类型：true = 后端当前是仿真。未知时与 <see cref="IsRealDriver"/> 同时为 false。</summary>
    public bool UseSimulator => IsDriverKnown && _session.Driver == RobotDriver.Simulation;

    /// <summary>
    /// "真实"那半个分段按钮的选中态。
    /// 两个按钮各自绑一个互斥布尔量（<c>Classes.segOn</c>），因此不需要"取反"转换器。
    /// </summary>
    public bool IsRealDriver => IsDriverKnown && _session.Driver == RobotDriver.Real;

    // ── 指令（把界面动作翻译成 IRobotService 调用）──

    /// <summary>连接（连 /control，成功后开启状态流与感知流，并自动上使能）。</summary>
    [RelayCommand]
    private async Task Connect()
    {
        ErrorMessage = "";
        try
        {
            await _robot.ConnectAsync();
            _robot.StartStateStream();

            // /sensor 与 /state 一样「按需连」：未连的通道后端不会发数据。
            // 点云是建图 / 选点两步的输入（见 docs/ui/zh-CN.md 的流程图），所以跟连接一起开，
            // 不等到某一帧界面才发现没数据。
            _robot.StartSensorStream();
            await AutoEnableAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"连接失败: {ex.Message}";
            _log.Log($"连接失败: {ex.Message}", LogLevel.Error, "connect");
        }
    }

    /// <summary>断开连接（同时关闭状态流与感知流）。</summary>
    [RelayCommand]
    private void Disconnect()
    {
        _robot.StopStateStream();
        _robot.StopSensorStream();
        _robot.Disconnect();
        _session.IsConnected = false;
        _session.IsEnabled = false;
        _session.IsDriverKnown = false; // 断开后驱动类型就无从得知了（驱动按钮立即变灰）
        _session.ExitToIdle();
        IsEmergencyStopped = false;
    }

    /// <summary>下使能（robot_enable 0）。</summary>
    [RelayCommand]
    private async Task DisableRobot()
    {
        var r = await _robot.RobotEnableAsync(0);
        if (r.Success)
        {
            _session.IsEnabled = false;
            _session.ExitToIdle();
        }
        else
        {
            ErrorMessage = $"下使能失败: {r.Message}";
            _log.Log($"下使能失败: {r.Message}", LogLevel.Error, "robot_enable");
        }
    }

    /// <summary>
    /// 切换真实 / 仿真驱动（switch_driver）。参数用后端的编码：<c>"1"</c> = 真实，<c>"0"</c> = 仿真。
    ///
    /// <para>
    /// 点按钮只是"请求切换"，回执成功也不等于界面上就该亮它 —— 所以无论回执如何，
    /// 发完都再回读一次 <c>get_driver_type</c>：亮的永远是后端的实际状态。
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task SwitchDriver(object? parameter)
    {
        // 未连接时按钮已是灰的，这里再挡一层（键盘快捷键 / 程序化调用也会走到这里）。
        if (!IsConnected)
            return;

        var driver = parameter?.ToString() == "1" ? RobotDriver.Real : RobotDriver.Simulation;

        var r = await _robot.SwitchDriverAsync(driver);
        if (!r.Success)
        {
            ErrorMessage = $"切换驱动失败: {r.Message}";
            _log.Log($"切换驱动失败: {r.Message}", LogLevel.Error, "switch_driver");
        }

        await RefreshDriverTypeAsync();
    }

    /// <summary>急停：停止所有运动并回到空闲（安全态）。</summary>
    [RelayCommand]
    private async Task EmergencyStop()
    {
        IsEmergencyStopped = true;

        var r = await _robot.StopAsync();
        if (!r.Success)
        {
            ErrorMessage = $"急停回执异常: {r.Message}";
            _log.Log($"急停回执异常: {r.Message}", LogLevel.Error, "stop");
        }

        _session.ExitToIdle();
    }

    /// <summary>急停恢复（reset）。</summary>
    [RelayCommand]
    private async Task ResetEmergency()
    {
        var r = await _robot.ResetAsync();
        if (!r.Success)
        {
            ErrorMessage = $"复位失败: {r.Message}";
            _log.Log($"复位失败: {r.Message}", LogLevel.Error, "reset");
            return;
        }

        IsEmergencyStopped = false;
        _session.IsEnabled = true;
        _session.ExitToIdle();
    }

    /// <summary>连接成功后自动上使能（失败不阻断连接，只在界面提示）。</summary>
    private async Task AutoEnableAsync()
    {
        try
        {
            var r = await _robot.RobotEnableAsync(1);
            _session.IsEnabled = r.Success;
            if (!r.Success)
            {
                ErrorMessage = $"上使能失败: {r.Message}";
                _log.Log($"上使能失败: {r.Message}", LogLevel.Error, "robot_enable");
            }
        }
        catch (Exception ex)
        {
            _session.IsEnabled = false;
            ErrorMessage = $"上使能失败: {ex.Message}";
            _log.Log($"上使能失败: {ex.Message}", LogLevel.Error, "robot_enable");
        }
    }

    // ── 驱动类型回读 ──

    /// <summary>
    /// 连接状态变化。回调发生在 WebSocket 线程上（含断线重连），所以先 <c>Post</c> 回 UI 线程再碰界面状态。
    ///
    /// <para>
    /// 连上后（含自动重连成功）向【后端】回读驱动类型，而不是沿用本地点过的那一个：
    /// 后端可能被外部切过驱动，重连之后也不一定还是原驱动。
    /// </para>
    /// </summary>
    private void OnConnectionChanged(bool connected) => Dispatcher.UIThread.Post(() =>
    {
        _session.IsConnected = connected;

        if (!connected)
        {
            _session.IsDriverKnown = false; // 掉线即未知：驱动按钮变灰，不用旧值继续"显示真相"
            return;
        }

        _ = RefreshDriverTypeAsync();
    });

    /// <summary>
    /// 回读后端当前驱动类型（<c>get_driver_type</c>）并刷新界面。
    /// 连接完成 / 断线重连成功 / 切换驱动之后都会走这里；读不到就退回"未知"（按钮变灰），
    /// 绝不用旧值冒充当前状态。
    /// </summary>
    private async Task RefreshDriverTypeAsync()
    {
        try
        {
            var r = await _robot.QueryDriverTypeAsync();
            if (!r.Success || r.Result.Length == 0)
            {
                _session.IsDriverKnown = false;
                ErrorMessage = $"查询驱动类型失败: {r.Message}";
                _log.Log($"查询驱动类型失败: {r.Message}", LogLevel.Error, "get_driver_type");
                return;
            }

            _session.Driver = RobotDriverCodec.FromProtocol(r.Result[0]);
            _session.IsDriverKnown = true;
        }
        catch (Exception ex)
        {
            _session.IsDriverKnown = false;
            ErrorMessage = $"查询驱动类型失败: {ex.Message}";
            _log.Log($"查询驱动类型失败: {ex.Message}", LogLevel.Error, "get_driver_type");
        }
    }

    // ── 状态同步 ──

    /// <summary>共享状态有任何变化就刷新全部派生属性（状态灯 / 文本 / 按钮态）。</summary>
    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e) => RaiseAll();

    partial void OnIsEmergencyStoppedChanged(bool value) => RaiseAll();

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(ConnectText));
        OnPropertyChanged(nameof(ModeText));
        OnPropertyChanged(nameof(IsDriverKnown));
        OnPropertyChanged(nameof(UseSimulator));
        OnPropertyChanged(nameof(IsRealDriver));
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsManual));
        OnPropertyChanged(nameof(IsScanning));
        OnPropertyChanged(nameof(IsHalted));
    }
}

