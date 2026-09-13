using System;
using System.Collections.ObjectModel;

namespace RUSTool.Services.Logging;

/// <summary>日志等级。</summary>
public enum LogLevel
{
    /// <summary>调试信息。</summary>
    Debug,

    /// <summary>常规信息。</summary>
    Info,

    /// <summary>操作成功（后端回执 <c>success=true</c> 时使用）。</summary>
    Success,

    /// <summary>警告：不阻断流程，但值得注意。</summary>
    Warn,

    /// <summary>错误：指令失败或抛出异常。</summary>
    Error
}

/// <summary>
/// 一条日志（只读）。
///
/// <para>
/// 除了数据本身（时间 / 等级 / 来源 / 正文），还提供若干【纯派生文本与布尔量】。
/// 这样界面可以直接绑定，而不必为"级别 → 颜色"再写一个转换器：
/// <c>Classes.success="{Binding IsSuccess}"</c>。
/// </para>
/// <para>
/// 这些派生成员不含任何 UI 框架类型（只有 string / bool），因此放在纯逻辑层
/// 并不违反"Core 不认识 Avalonia"这条约束 —— 与原有的 <see cref="Formatted"/> 同性质。
/// 等级到配色/文案的映射集中在主题里，改配色不需要动这里。
/// </para>
/// </summary>
public sealed record LogEntry(DateTime Time, LogLevel Level, string Source, string Message)
{
    /// <summary>时间列文本（固定宽度，等宽字体下天然对齐）。</summary>
    public string TimeText => Time.ToString("HH:mm:ss.fff");

    /// <summary>级别列的缩写文本。</summary>
    public string LevelText => Level switch
    {
        LogLevel.Debug => "DBG",
        LogLevel.Success => "OK",
        LogLevel.Warn => "WARN",
        LogLevel.Error => "ERR",
        _ => "INFO",
    };

    /// <summary>整行文本（导出 / 复制用）。</summary>
    public string Formatted => $"[{TimeText}] [{LevelText}] {Source} {Message}";

    // 互斥的布尔量，专供 XAML 的 Classes.xxx 绑定（无需转换器）。
    public bool IsDebug => Level == LogLevel.Debug;

    public bool IsSuccess => Level == LogLevel.Success;

    public bool IsWarning => Level == LogLevel.Warn;

    public bool IsError => Level == LogLevel.Error;
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
    /// <param name="message">正文。</param>
    /// <param name="level">等级。</param>
    /// <param name="source">来源：指令名 / 模块名，如 <c>movej</c>、<c>连接</c>。</param>
    void Log(string message, LogLevel level = LogLevel.Info, string source = "");

    /// <summary>清空日志。</summary>
    void Clear();
}
