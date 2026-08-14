using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using RUSTool.ViewModels;

namespace RUSTool.Views.Debug;

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
        _entries = (DataContext as MainViewModel)?.Log.Entries;
        if (_entries is not null)
            _entries.CollectionChanged += OnEntriesChanged;
        ScrollToEnd();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScrollToEnd();
    }

    /// <summary>写入全局日志（供启动信息等界面代码调用）。</summary>
    public void Log(string message)
    {
        if (DataContext is MainViewModel vm)
            vm.Log.Log(message);
    }

    /// <summary>清空全局日志。</summary>
    public void Clear()
    {
        if (DataContext is MainViewModel vm)
            vm.Log.Clear();
    }

    private void ScrollToEnd()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (LogListBox.ItemCount > 0)
                LogListBox.ScrollIntoView(LogListBox.Items[^1]!);
        });
    }
}
