using Avalonia.Threading;
using RUSTool.Services.Logging;
using System;
using System.Collections.ObjectModel;
using System.IO;

namespace RUSTool.UI.Services.Logging;

/// <summary>
/// <see cref="ILogService"/> 的界面层实现。
///
/// <para>
/// 接口在 <c>RUSTool.Core</c>（纯逻辑层），实现必须留在这里：集合更新要 marshal 到
/// UI 线程（<see cref="Dispatcher.UIThread"/>），这是 Avalonia 的类型，
/// 不能出现在 Core 里。
/// </para>
/// <para>
/// 两条硬性约束：
/// ① 限长 —— 界面只保留最近 <see cref="MaxEntries"/> 条，长时间运行不会无限膨胀；
/// ② 落盘 —— 同时把每条日志追加写入按天分文件的文本日志，线程安全（后台线程也会调用）。
/// </para>
/// </summary>
public sealed class LogService : ILogService
{
    private const int MaxEntries = 500;

    private readonly string? _logDirectory;
    private readonly object _fileLock = new();

    public ObservableCollection<LogEntry> Entries { get; } = new();

    /// <param name="logDirectory">日志目录（默认 <c>logs</c>，传 <c>null</c> 禁用落盘）。</param>
    public LogService(string? logDirectory = "logs")
    {
        _logDirectory = logDirectory;
        if (_logDirectory is not null)
            Directory.CreateDirectory(_logDirectory);
    }

    public void Log(string message, LogLevel level = LogLevel.Info, string source = "")
    {
        var entry = new LogEntry(DateTime.Now, level, source, message);

        // 后台线程（通信回调）也会走到这里，必须 Post 到 UI 线程再改集合。
        Dispatcher.UIThread.Post(() =>
        {
            Entries.Add(entry);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
        });

        WriteToFile(entry);
    }

    public void Clear() => Dispatcher.UIThread.Post(() => Entries.Clear());

    private void WriteToFile(LogEntry entry)
    {
        if (_logDirectory is null)
            return;

        var file = Path.Combine(_logDirectory, $"{entry.Time:yyyy-MM-dd}.log");
        var line = $"{entry.Formatted}{Environment.NewLine}";
        lock (_fileLock)
        {
            File.AppendAllText(file, line);
        }
    }
}
