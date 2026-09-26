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
/// 状态帧在后台线程到达，所有赋值统一 <c>Post</c> 到 UI 线程（绑定源集合只能在 UI 线程改）。
/// </para>
/// </summary>
public sealed partial class TorqueChartViewModel : ViewModelBase
{
    /// <summary>
    /// 滚动窗口长度（帧）。<c>/state</c> 约 20 Hz → 一屏约 15 秒历史：
    /// 够看出一整段动作的形状（起停 / 换向 / 平台段），又不至于把 20 Hz 的点挤成一条实心带。
    ///
    /// <para>
    /// 转发 <see cref="RobotStateRow.DefaultWindowFrames"/>：窗口长度是行模型的属性
    /// （曲线的 X 轴整段长度就是它），本层<b>不另定一个数</b> —— 否则截图脚本推满一屏的帧数
    /// 和图上实际的窗口会对不上。
    /// </para>
    /// </summary>
    public const int WindowFrames = RobotStateRow.DefaultWindowFrames;

    private readonly IRobotService _robot;

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
    /// <c>/state</c> 的标称帧率（Hz）。只用来把窗口长度折成「几秒」写到卡片头上 ——
    /// 横轴画的仍然是帧序号，曲线的形状不依赖这个数（见 <see cref="WindowCaption"/>）。
    /// 与 <c>DemoStateFrame.Hz</c>（合成流）以及协议里的 <c>frame_rate</c> 是同一个量级。
    /// </summary>
    private const double NominalStateHz = 20.0;

    /// <summary>
    /// 窗口长度文案（卡片头右侧显示）：帧数与秒数一起给。
    ///
    /// <para>
    /// 帧数是唯一来源（<see cref="WindowFrames"/>）；秒数只按标称帧率折出来当旁注 ——
    /// 「300 帧」对人没有直觉，而「15 秒」一眼就能对上刚才那段动作有多长。
    /// </para>
    /// </summary>
    public string WindowCaption => $"窗口 {WindowFrames / NominalStateHz:0.#} s · {WindowFrames} 帧";

    private void OnStateUpdated(BridgeProtocol.StateFrame state)
        => Dispatcher.UIThread.Post(() => PushFrame(state));

    /// <summary>
    /// 推一帧状态帧：同一帧进所有行，每行取自己那一路的分量。
    /// <b>必须在 UI 线程调用</b>（改的是绑定源集合）。
    /// </summary>
    /// <param name="state">状态帧；<c>effort</c> 分量不足时缺的那些按 0 计（帧初期字段可能不全）。</param>
    public void PushFrame(BridgeProtocol.StateFrame state) => RobotStateRows.PushFrame(Rows, state.Effort);
}
