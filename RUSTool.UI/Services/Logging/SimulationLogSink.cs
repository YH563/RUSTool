using RUSTool.Services.Logging;
using RUSTool.Visualization.Logging;
using System;

namespace RUSTool.UI.Services.Logging;

/// <summary>
/// <see cref="ISimulationLogSink"/> 的界面层实现：把图形栈（RUSTool.Visualization）里发生的日志
/// 转手写进项目自己的 <see cref="ILogService"/>。
///
/// <para>
/// 装配在组合根一次完成（<c>App.CreateMainViewModel</c>：日志服务建好后、视口存在前）：
/// 于是 URDF 资产解析、网格导入告警、Assimp 原生库探测这些<b>只发生在库内部</b>的信息，
/// 会与本项目其它日志出现在同一个面板、同一份落盘文件里 —— 排查 3D 场景问题不必再去翻 stderr。
/// </para>
/// <para>
/// 等级映射：库的 Debug / Information / Warning / Error 分别对应日志器的
/// Debug / Info / Warn / Error（库没有 Success 语义，故不产生该等级）。
/// 落盘文件里的来源列是 <c>sim</c>，或 <c>sim·模块名</c>（库用分类 logger 时）。
/// </para>
/// </summary>
public sealed class SimulationLogSink : ISimulationLogSink
{
    /// <summary>日志来源前缀（界面日志面板不显示来源列，落盘文件里看得到）。</summary>
    private const string Source = "sim";

    private readonly ILogService _log;

    public SimulationLogSink(ILogService log) => _log = log;

    /// <inheritdoc/>
    public void Write(SimulationLogLevel level, string category, string message) =>
        _log.Log(message, Map(level), BuildSource(category));

    /// <summary>等级映射：图形栈四档 → 项目日志器五档。</summary>
    private static LogLevel Map(SimulationLogLevel level) => level switch
    {
        SimulationLogLevel.Debug => LogLevel.Debug,
        SimulationLogLevel.Warning => LogLevel.Warn,
        SimulationLogLevel.Error => LogLevel.Error,
        _ => LogLevel.Info,
    };

    /// <summary>
    /// 来源列文本：库的默认类别（Global / 空）只写 <c>sim</c>；
    /// 分模块的 logger 取其【末段】（全名太长，来源列放不下），如 <c>sim·AssimpModelLoader</c>。
    /// </summary>
    private static string BuildSource(string category)
    {
        if (string.IsNullOrEmpty(category) || category == "Global")
            return Source;

        int dot = category.LastIndexOf('.');
        return dot >= 0 && dot < category.Length - 1
            ? $"{Source}·{category[(dot + 1)..]}"
            : $"{Source}·{category}";
    }
}
