using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Services.Logging;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace RUSTool.UI.ViewModels;

/// <summary>
/// 日志面板。
///
/// <para>
/// 这里【不再自己产生日志】：条目由 <see cref="ILogService"/> 统一维护
/// （契约在 <c>RUSTool.Core</c>，Avalonia 实现在 <c>Services/Logging/LogService</c>），
/// 所有 ViewModel 与启动流程写入的是同一个集合，本类只负责"怎么显示"——
/// 过滤（只看警告）与选中。
/// </para>
/// <para>
/// 服务集合在后台线程变化（通信回调），而绑定集合只能在 UI 线程改，
/// 因此这里做一层【增量镜像】：新增就追加、淘汰就移除，
/// 只有切换过滤条件时才整体重建 —— 代价是每次建表 O(n)，n 上限 500。
/// </para>
/// </summary>
public sealed partial class LogViewModel : ViewModelBase
{
    private readonly ILogService _service;

    /// <summary>界面显示的日志（已按 <see cref="WarningOnly"/> 过滤）。</summary>
    public ObservableCollection<LogEntry> Entries { get; } = new();

    /// <summary>只显示警告以上。</summary>
    [ObservableProperty]
    private bool _warningOnly;

    /// <summary>当前选中的日志行。</summary>
    [ObservableProperty]
    private LogEntry? _selected;

    public LogViewModel(ILogService service)
    {
        _service = service;
        Rebuild();
        _service.Entries.CollectionChanged += OnServiceEntriesChanged;
    }

    partial void OnWarningOnlyChanged(bool value) => Rebuild();

    private bool Match(LogEntry entry) => !WarningOnly || entry.IsWarning || entry.IsError;

    private void OnServiceEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 集合是后台线程改的，镜像动作统一排到 UI 线程。
        Dispatcher.UIThread.Post(() =>
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    foreach (var entry in e.NewItems?.OfType<LogEntry>() ?? [])
                    {
                        if (Match(entry))
                            Entries.Add(entry);
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    foreach (var entry in e.OldItems?.OfType<LogEntry>() ?? [])
                        Entries.Remove(entry);
                    break;

                default:
                    // Reset（清空）：整体重建最省事。
                    Rebuild();
                    break;
            }
        });
    }

    /// <summary>按当前过滤条件从头镜像一遍。</summary>
    private void Rebuild()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Rebuild);
            return;
        }

        Entries.Clear();
        foreach (var entry in _service.Entries)
        {
            if (Match(entry))
                Entries.Add(entry);
        }
    }

    /// <summary>清空日志（内存与界面同时清；落盘的文本日志不受影响）。</summary>
    [RelayCommand]
    private void Clear() => _service.Clear();
}


