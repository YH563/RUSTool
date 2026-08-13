using Avalonia.Controls;

namespace RUSTool.Views.Debug;

/// <summary>
/// 机械臂状态 HUD：以半透明背景悬浮在 3D 场景上，展示 TCP 位姿 / 关节角度 / 关节力矩。
/// </summary>
public partial class RobotStatusOverlay : UserControl
{
    public RobotStatusOverlay()
    {
        InitializeComponent();
    }
}
