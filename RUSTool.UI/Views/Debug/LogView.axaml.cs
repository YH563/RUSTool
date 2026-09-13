using Avalonia.Controls;
using Avalonia.Threading;
using RUSTool.UI.ViewModels;
using System;
using System.Collections.Specialized;

namespace RUSTool.UI.Views.Debug;

/// <summary>
/// 日志面板的 code-behind：只做一件事 —— 有新日志时把列表滚到末尾。
///
/// <para>
/// 这是纯粹的视图行为（ListBox 的滚动位置），放在 ViewModel 里反而别扭：
/// ViewModel 不该知道控件滚到哪了。日志条目是后台线程写进集合的，
/// 所以滚动统一 Post 到 UI 线程，等布局完成后再执行。
/// </para>
/// </summary>
public partial class LogView : UserControl
{
    private INotifyCollectionChanged? _entries;

    public LogView()
    {
        InitializeComponent();
        LogListBox.AttachedToVisualTree += (_, _) => ScrollToEnd();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_entries is not null)
            _entries.CollectionChanged -= OnEntriesChanged;

        _entries = (DataContext as LogViewModel)?.Entries;
        if (_entries is not null)
            _entries.CollectionChanged += OnEntriesChanged;

        ScrollToEnd();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToEnd();

    /// <summary>滚到最后一行。集合在变化过程中不能直接滚，先 Post 让布局跑完。</summary>
    private void ScrollToEnd()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (LogListBox.ItemCount > 0)
                LogListBox.ScrollIntoView(LogListBox.Items[^1]!);
        });
    }
}
