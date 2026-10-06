using Avalonia.Controls;

namespace RUSTool.UI.Views.Debug;

/// <summary>
/// 手动 / 点动控制面板。点动的「按住走、松手停」交互已抽到可复用的 <see cref="JogAxes"/>，
/// 本控件只负责参考系、运动指令与操作条的布局。
/// </summary>
public partial class ArmControlPanel : UserControl
{
    public ArmControlPanel() => InitializeComponent();
}
