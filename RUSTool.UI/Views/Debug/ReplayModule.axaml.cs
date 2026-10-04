using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using RUSTool.UI.ViewModels;

namespace RUSTool.UI.Views.Debug;

/// <summary>
/// 回放传输条的 code-behind：只做一件事 —— 时间轴被用户操作（拖拽松手 / 方向键）后，
/// 把当前值当 seek 下发给后端。
///
/// <para>
/// 进度由后端 <c>replay_status</c> 驱动、滑条 Value 是单向绑定，所以正常刷新不会触发 seek；
/// 只有用户松手 / 按键才走这里。这样既避免"轮询更新 ↔ 滑条回写"的死循环，又让定位是一次性的。
/// </para>
/// </summary>
public partial class ReplayModule : UserControl
{
    public ReplayModule()
    {
        InitializeComponent();

        Timeline.AddHandler(PointerReleasedEvent, OnTimelinePointerReleased, RoutingStrategies.Tunnel);
        Timeline.AddHandler(KeyUpEvent, OnTimelineKeyUp, RoutingStrategies.Tunnel);
    }

    private void OnTimelinePointerReleased(object? sender, PointerReleasedEventArgs e) => Seek();

    private void OnTimelineKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown)
            Seek();
    }

    private void Seek()
    {
        if (DataContext is ReplayViewModel vm)
            vm.SeekTo(Timeline.Value);
    }
}
