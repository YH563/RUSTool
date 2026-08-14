using System.Collections.ObjectModel;

namespace RUSTool.Services.Logging;

/// <summary>日志等级。</summary>
public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

/// <summary>一条日志（只读，含预格式化文本供界面直接绑定）。</summary>
public sealed record LogEntry(LogLevel Level, string Timestamp, string Message)
{
    public string Formatted => $"[{Timestamp}] {Message}";
}

/// <summary>
/// 全局日志服务。线程安全：可从后台线程调用，内部会 marshal 到 UI 线程更新集合。
/// 供所有 ViewModel 与启动流程共用，界面上统一绑定展示。
/// </summary>
public interface ILogService
{
    /// <summary>日志集合（绑定到 UI 的 ItemsSource）。</summary>
    ObservableCollection<LogEntry> Entries { get; }

    /// <summary>写入一条日志。</summary>
    void Log(string message, LogLevel level = LogLevel.Info);

    /// <summary>清空日志。</summary>
    void Clear();
}
