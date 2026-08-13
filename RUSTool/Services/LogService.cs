using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;

namespace RUSTool.Services;

/// <summary>
/// <see cref="ILogService"/> 的默认实现。
/// 限制最大条数，防止长时间运行内存膨胀；所有集合修改统一在 UI 线程执行。
/// </summary>
public sealed class LogService : ILogService
{
    private const int MaxEntries = 500;

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        var entry = new LogEntry(level, DateTime.Now.ToString("HH:mm:ss.fff"), message);
        Dispatcher.UIThread.Post(() =>
        {
            Entries.Add(entry);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
        });
    }

    public void Clear()
    {
        Dispatcher.UIThread.Post(() => Entries.Clear());
    }
}
