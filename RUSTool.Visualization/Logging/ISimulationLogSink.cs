namespace RUSTool.Visualization.Logging;

/// <summary>
/// 图形栈日志的等级 —— 只映射宿主日志器需要区分的四档。
///
/// <para>
/// 故意不直接用 <c>Microsoft.Extensions.Logging.LogLevel</c>：那是图形栈内部的类型，
/// 界面层不该为了收一条日志去认识它 —— 理由与「界面只认识 <c>RobotViewport</c>」同一条。
/// 映射只做一次，在 <see cref="SimulationLogBridge"/> 里。
/// </para>
/// </summary>
public enum SimulationLogLevel
{
    /// <summary>调试信息（资产解析试探、网格导入细节）。</summary>
    Debug,

    /// <summary>常规信息。</summary>
    Info,

    /// <summary>警告：不阻断流程，但值得注意（缺纹理、未定义材质…）。</summary>
    Warning,

    /// <summary>错误：加载 / 解析失败。</summary>
    Error
}

/// <summary>
/// 图形栈的日志出口 —— 界面层要实现的唯一接口（只有一个方法）。
///
/// <para>
/// 图形栈内部（RobotSimulation 的 <c>Logger</c> 门面，底层是 Microsoft.Extensions.Logging）
/// 不认识本项目的 <c>ILogService</c>，本项目也不该引用图形栈的日志类型，
/// 于是双方在「一个方法 + 一个四档枚举」上会合：界面侧实现本接口转手写进 <c>ILogService</c>，
/// 图形栈侧由 <see cref="SimulationLogBridge"/> 把库日志灌进来。
/// </para>
/// <para>
/// <b>可能从任意线程调用</b>（渲染线程、加载线程都会产生日志），实现方自己负责 marshal ——
/// 例如界面侧的实现只是转调 <c>ILogService.Log</c>，而它内部本就 Post 到 UI 线程。
/// </para>
/// </summary>
public interface ISimulationLogSink
{
    /// <summary>写入一条图形栈日志。</summary>
    /// <param name="level">等级（已由桥映射好）。</param>
    /// <param name="category">库里的日志类别；<c>Global</c> 表示库的默认类别（没分模块）。</param>
    /// <param name="message">正文（异常信息已拼进正文，实现方不必再处理异常）。</param>
    void Write(SimulationLogLevel level, string category, string message);
}
