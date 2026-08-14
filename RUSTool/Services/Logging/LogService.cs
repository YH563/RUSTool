using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;
using System.IO;

namespace RUSTool.Services.Logging;

/// <summary>
/// <see cref="ILogService"/> 的默认实现。
/// 限制最大条数，防止长时间运行内存膨胀；所有集合修改统一在 UI 线程执行。
/// 同时将每条日志追加写入文件（按天分文件），线程安全。
/// </summary>
public sealed class LogService : ILogService
{
    private const int MaxEntries = 500;
    private readonly string? _logDirectory;
    private readonly object _fileLock = new();

    public ObservableCollection<LogEntry> Entries { get; } = new();

    /// <param name="logDirectory">日志目录（默认 "logs"，传 null 禁用文件写入）。</param>
    public LogService(string? logDirectory = "logs")
    {
        _logDirectory = logDirectory;
        if (_logDirectory is not null)
            Directory.CreateDirectory(_logDirectory);
    }

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        var entry = new LogEntry(level, DateTime.Now.ToString("HH:mm:ss.fff"), message);
        Dispatcher.UIThread.Post(() =>
        {
            Entries.Add(entry);
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
        });

        WriteToFile(message, level);
    }

    public void Clear()
    {
        Dispatcher.UIThread.Post(() => Entries.Clear());
    }

    private void WriteToFile(string message, LogLevel level)
    {
        if (_logDirectory is null)
            return;
        var file = Path.Combine(_logDirectory, $"{DateTime.Now:yyyy-MM-dd}.log");
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
        lock (_fileLock)
        {
            File.AppendAllText(file, line);
        }
    }
}
