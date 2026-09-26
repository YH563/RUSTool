using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Communication;
using RUSTool.Services.Robot;
using System;

namespace RUSTool.UI.ViewModels;

/// <summary>
/// 机械臂实时状态（3D 视口右上角、按需展开的 HUD）。
///
/// <para>
/// 读数来自 <see cref="IRobotService.StateUpdated"/>（<c>/state</c> 状态流）。
/// 状态帧在后台线程到达，因此统一 <c>Post</c> 到 UI 线程再赋值。
/// </para>
/// <para>
/// 读数之外还管着一件事：这块浮层的展开 / 收起（<see cref="IsPanelVisible"/> +
/// <see cref="TogglePanelCommand"/>）。默认收起 —— 3D 里机械臂按视口居中绘制，
/// 常驻的读数块会压住它；显隐跟着读数的所有者走，界面层不需要再放一个标志位。
/// </para>
/// <para>
/// 单位换算只在这一层做：后端用弧度，界面显示度 —— 界面代码不需要知道这件事。
/// </para>
/// </summary>
public sealed partial class RobotStatusViewModel : ViewModelBase
{
    private readonly IRobotService _robot;

    // ── TCP / 法兰位姿（位置 m / 姿态 deg）──
    [ObservableProperty] private double _flangeX;
    [ObservableProperty] private double _flangeY;
    [ObservableProperty] private double _flangeZ;
    [ObservableProperty] private double _flangeRx;
    [ObservableProperty] private double _flangeRy;
    [ObservableProperty] private double _flangeRz;

    // ── 关节角（deg）──
    [ObservableProperty] private double _joint1;
    [ObservableProperty] private double _joint2;
    [ObservableProperty] private double _joint3;
    [ObservableProperty] private double _joint4;
    [ObservableProperty] private double _joint5;
    [ObservableProperty] private double _joint6;

    // ── 关节力矩（Nm）──
    [ObservableProperty] private double _torque1;
    [ObservableProperty] private double _torque2;
    [ObservableProperty] private double _torque3;
    [ObservableProperty] private double _torque4;
    [ObservableProperty] private double _torque5;
    [ObservableProperty] private double _torque6;

    /// <summary>接触力（N）—— 示教器上比关节力矩更常看，界面里单独给一行。</summary>
    [ObservableProperty] private double _contactForce;

    /// <summary>状态流帧率（Hz），用于判断链路是否健康。</summary>
    [ObservableProperty] private double _frameRate;

    /// <summary>
    /// 关节角（弧度），直接喂给 3D 视口。
    ///
    /// <para>
    /// 与上面 6 个「显示用」度数是同一份数据的两种投影：3D 图形库内部单位就是弧度 / 米，
    /// 再换算一次只会引入无谓误差，所以这里原样透传（换算只发生在需要给人看的那一侧）。
    /// </para>
    /// <para>
    /// 每帧换一个新数组：Avalonia 按【引用变化】触发绑定更新，原地改内容不会通知。
    /// 6 个 float 的分配量可以忽略。
    /// </para>
    /// </summary>
    [ObservableProperty] private float[] _jointsRadians = new float[6];

    /// <summary>是否已连上控制通道（未连接时 HUD 各读数保持为 0）。</summary>
    [ObservableProperty] private bool _isConnected;

    /// <summary>
    /// 读数浮层是否展开（3D 视口右上角那枚小按钮切换）。<b>默认收起</b>。
    ///
    /// <para>
    /// 默认收起的理由：3D 视口里机械臂是<b>按视口居中</b>绘制的，任何常驻的读数块
    /// （浮在右上角，或占视口旁一列）都会压掉/挤掉一根胳膊。收起时视口右上角只剩
    /// 一枚半透明小按钮，3D 的画面是完整的；要读数再按开。
    /// </para>
    /// </summary>
    [ObservableProperty] private bool _isPanelVisible;

    /// <summary>按钮文案：展开时提示可以收起，收起时说明按下去看什么。</summary>
    public string PanelToggleText => IsPanelVisible ? "隐藏状态" : "机械臂状态";

    partial void OnIsPanelVisibleChanged(bool value) => OnPropertyChanged(nameof(PanelToggleText));

    /// <summary>右上角小按钮：展开 / 收起读数浮层。</summary>
    [RelayCommand]
    private void TogglePanel() => IsPanelVisible = !IsPanelVisible;

    public RobotStatusViewModel(IRobotService robot)
    {
        _robot = robot;
        _robot.ConnectionChanged += connected => Dispatcher.UIThread.Post(() => IsConnected = connected);
        _robot.StateUpdated += OnStateUpdated;
    }

    private void OnStateUpdated(BridgeProtocol.StateFrame state)
        => Dispatcher.UIThread.Post(() => PushFrame(state));

    /// <summary>
    /// 用一帧状态帧刷新全部读数。<b>必须在 UI 线程调用</b>（改的是绑定属性）。
    ///
    /// <para>
    /// 单独开一道门，而不是把整段换算留在事件回调里：截图模式的合成帧
    /// （<c>--demo-torque</c>）也要从<b>同一段代码</b>进 HUD ——
    /// 与 <c>--demo-cloud</c> 往 <c>MainViewModel.PublishPointCloud</c> 里灌帧同一个道理，
    /// 免得出现「截图走一套换算、真机走另一套」。
    /// </para>
    /// </summary>
    /// <param name="state">状态帧（后端单位：弧度 / 米；换算只在这一层做）。</param>
    public void PushFrame(BridgeProtocol.StateFrame state)
    {
        // 关节角：后端弧度 → 界面度。
        Joint1 = Deg(state.JointPos, 0);
        Joint2 = Deg(state.JointPos, 1);
        Joint3 = Deg(state.JointPos, 2);
        Joint4 = Deg(state.JointPos, 3);
        Joint5 = Deg(state.JointPos, 4);
        Joint6 = Deg(state.JointPos, 5);

        // 3D 视口的同一份关节角（弧度，不换算）—— 姿态与上面几行来自同一帧，不会各说各话。
        JointsRadians = ToFloats(state.JointPos, 6);

        // 法兰位姿：位置本身就是米，姿态是弧度 → 度。
        FlangeX = At(state.FlangePos, 0);
        FlangeY = At(state.FlangePos, 1);
        FlangeZ = At(state.FlangePos, 2);
        FlangeRx = Deg(state.FlangePos, 3);
        FlangeRy = Deg(state.FlangePos, 4);
        FlangeRz = Deg(state.FlangePos, 5);

        Torque1 = At(state.Effort, 0);
        Torque2 = At(state.Effort, 1);
        Torque3 = At(state.Effort, 2);
        Torque4 = At(state.Effort, 3);
        Torque5 = At(state.Effort, 4);
        Torque6 = At(state.Effort, 5);

        FrameRate = state.FrameRate;

        // 状态帧里【没有独立的接触力通道】，这里用各关节力矩的模和作近似：
        // 探头压在体表上时各关节力矩同时升高，趋势与接触力一致。
        // 后端一旦提供 contact_force 字段，只改这一行即可。
        var sum = 0.0;
        foreach (var effort in state.Effort)
            sum += Math.Abs(effort);
        ContactForce = sum;
    }

    /// <summary>取数组第 i 个元素，越界返回 0（状态帧初期字段可能不全）。</summary>
    private static double At(double[] values, int index)
        => index < values.Length ? values[index] : 0;

    /// <summary>
    /// 前 count 个元素转成 float 数组（图形库用 float）。
    /// 字段不全时补 0：宁可让机械臂停在零位，也不要因为少了两个关节就整帧不动。
    /// </summary>
    private static float[] ToFloats(double[] values, int count)
    {
        var result = new float[count];
        for (var i = 0; i < count && i < values.Length; i++)
            result[i] = (float)values[i];
        return result;
    }

    /// <summary>弧度 → 度。</summary>
    private static double Deg(double[] values, int index) => At(values, index) * 180 / Math.PI;
}

