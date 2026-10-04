using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using RUSTool.Charts.Data;
using RUSTool.Communication;
using RUSTool.Services.Robot;
using System.Collections.ObjectModel;

namespace RUSTool.UI.ViewModels;

/// <summary>
/// 「数据曲线」面板的 ViewModel：<b>薄适配层</b> —— 把 <c>/state</c> 状态帧接到图表库的行模型上。
///
/// <para>
/// <b>这里还剩什么：</b>三件事，都不属于任何图形栈 ——
/// 从通道目录建出六行（<see cref="RobotArmChannels.Torques"/> + <see cref="RobotStateRows.Create"/>）、
/// 订阅 <see cref="IRobotService.StateUpdated"/> 并把帧推给这些行、把连接状态透给面板
/// （面板要据此说清「没有曲线」是没连上还是还没来帧）。
/// </para>
/// <para>
/// <b>这里不再有什么：</b>滚动历史、点集合的增删、超窗口后的删头 —— 那些是行模型自己的事
/// （<see cref="RobotStateRow"/>），原先和 HUD 抢同一份状态帧的这段逻辑连同
/// LiveCharts / SkiaSharp 一起搬进了 <c>RUSTool.Charts</c>。本层现在不认识图表库，
/// 视图层也不认识（见 <c>Views/Debug/DebugWorkspace.axaml</c> 的「数据曲线」卡片体）。
/// </para>
/// <para>
/// <b>采样率与时间窗口：</b><c>/state</c> 状态流约 125 Hz，但曲线不需要 125 个点/秒 ——
/// 那样 300 帧只够 2.4 秒、曲线会像一条飞速掠过的实心带。所以这里把推给行模型的帧
/// <b>按时间戳节流到 <see cref="DisplayHz"/></b>，窗口因此按「秒」算得准：
/// <see cref="WindowSeconds"/> 秒 × <see cref="DisplayHz"/> = <see cref="WindowFrames"/> 点。
/// 状态帧在后台线程到达，所有赋值统一 <c>Post</c> 到 UI 线程（绑定源集合只能在 UI 线程改）。
/// </para>
/// </summary>
public sealed partial class TorqueChartViewModel : ViewModelBase
{
    /// <summary>曲线展示的时间窗口（秒）：一屏要覆盖多久的历史。</summary>
    public const int WindowSeconds = 30;

    /// <summary>
    /// 曲线的显示采样率（Hz）—— 状态流多以 125 Hz 到达，这里节流到 20 Hz 再入行模型。
    /// 20 Hz 足以看清力矩的形状，又不会把一行几十像素高的曲线挤成实心带。
    /// </summary>
    public const int DisplayHz = 20;

    /// <summary>
    /// 滚动窗口长度（帧）= 秒数 × 显示采样率。
    ///
    /// <para>
    /// 转发给行模型作为 X 轴整段长度（曲线从这个长度从左往右长满后开始滚动）；
    /// 截图脚本（<c>--demo-torque</c>）也按它推满一屏的帧数 —— 两者必须同源，
    /// 否则图上窗口和实际帧数会对不上。
    /// </para>
    /// </summary>
    public const int WindowFrames = WindowSeconds * DisplayHz;

    /// <summary>两个采样点之间的最小时间间隔（秒）：把上游帧率节流到 <see cref="DisplayHz"/>。</summary>
    private const double MinSampleInterval = 1.0 / DisplayHz;

    private readonly IRobotService _robot;

    /// <summary>上一次真正推进行模型的状态帧时间戳（秒）；用于按时间戳节流。</summary>
    private double _lastPushedTimestamp = double.NegativeInfinity;

    public TorqueChartViewModel(IRobotService robot)
    {
        _robot = robot;

        // 六路关节力矩，一行一路（第 i 行画第 i 路，行头写的就是目录里的名字）。
        // 换一组要画的量只改这一行参数 —— 目录在图表库里，本层不认识它的内容。
        Rows = RobotStateRows.Create(RobotArmChannels.Torques(), WindowFrames);

        _robot.ConnectionChanged += connected => Dispatcher.UIThread.Post(() => IsConnected = connected);
        _robot.StateUpdated += OnStateUpdated;
    }

    /// <summary>
    /// 六行（一行一路，第 i 行画目录里的第 i 路）。行模型由图表库定义，本层只负责建它、推帧给它 ——
    /// 面板订阅的是这个集合实例本身（换了实例曲线会重挂一次监听），所以只在构造时建一次。
    /// </summary>
    public ObservableCollection<RobotStateRow> Rows { get; }

    /// <summary>
    /// 控制通道是否在线 —— 面板用它决定空数据那句话的措辞
    /// （「未连接控制通道」而不是「等待状态帧」：一个要查连接、一个要查后端的状态流）。
    /// </summary>
    [ObservableProperty] private bool _isConnected;

    /// <summary>
    /// 窗口长度文案（卡片头右侧显示）：秒数与帧数一起给。
    /// 秒数是主信息（一眼对上刚才那段动作有多长），帧数是实现细节的旁注。
    /// </summary>
    public string WindowCaption => $"窗口 {WindowSeconds} s · {WindowFrames} 帧";

    private void OnStateUpdated(BridgeProtocol.StateFrame state)
    {
        if (!ShouldSample(state.Timestamp))
            return;

        Dispatcher.UIThread.Post(() => PushFrame(state));
    }

    /// <summary>
    /// 按时间戳判断这一帧要不要进曲线：距上一帧不足 <see cref="MinSampleInterval"/> 就丢弃。
    /// 时间戳回退（重连 / 复位 / 换信号源）时强制放行并重设基线，避免从此一帧都进不来。
    /// </summary>
    private bool ShouldSample(double timestamp)
    {
        if (timestamp <= _lastPushedTimestamp || timestamp - _lastPushedTimestamp >= MinSampleInterval)
        {
            _lastPushedTimestamp = timestamp;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 推一帧状态帧：同一帧进所有行，每行取自己那一路的分量。
    /// <b>必须在 UI 线程调用</b>（改的是绑定源集合）。
    /// </summary>
    /// <param name="state">状态帧；<c>effort</c> 分量不足时缺的那些按 0 计（帧初期字段可能不全）。</param>
    public void PushFrame(BridgeProtocol.StateFrame state) => RobotStateRows.PushFrame(Rows, state.Effort);
}
