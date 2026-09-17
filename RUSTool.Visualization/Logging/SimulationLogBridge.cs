using Microsoft.Extensions.Logging;
using RobotSimulation.Core.Utils;
using System;

namespace RUSTool.Visualization.Logging;

/// <summary>
/// 把图形栈内部的日志接到宿主日志器上 —— 组合根调用一次 <see cref="Attach(ISimulationLogSink, SimulationLogLevel)"/> 即可。
///
/// <para>
/// 为什么需要它：仿真库自己有日志（URDF 资产解析、mesh 导入、Assimp 原生库探测、模型装配），
/// 走的是库内的静态门面 <see cref="Logger"/>（底层 Microsoft.Extensions.Logging）。
/// 该门面在没人调用 <c>Initialize</c> 时只建一个<b>没有任何 provider</b> 的工厂 ——
/// 于是库里的日志默认写到哪里都不去，界面上的日志面板与落盘文件里一条都看不到。
/// 本类就是补上这一步：自己实现一个 provider，把每条库日志转交给宿主的 <see cref="ISimulationLogSink"/>。
/// </para>
/// <para>
/// 三条契约：
/// ① <b>越早越好</b> —— 库的 <c>Initialize</c> 只生效一次（第一个调用者决定 provider 集合），
///    所以必须在任何库日志产生之前 Attach（本项目的组合根在 <c>App.CreateMainViewModel</c> 里、建视口之前）；
/// ② <b>可重复 Attach</b> —— provider 每次读的是当前 sink，因此换宿主只是换目标，不会重复注册 provider；
/// ③ <b>等级</b> —— 库自己的主开关（<c>Logger.MinimumLevel</c>）由本方法统一设置，
///    <see cref="SimulationLogLevel.Debug"/> 即库的默认行为（Debug 及以上）。
/// </para>
/// </summary>
public static class SimulationLogBridge
{
    // Initialize 的「只生效一次」与 sink 的更换都在同一把锁下，避免出现「provider 建好了但 sink 还是 null」的窗口。
    private static readonly object SyncRoot = new();

    private static volatile ISimulationLogSink? _sink;
    private static bool _initialized;

    /// <summary>已接上宿主日志器（且尚未解绑）。</summary>
    public static bool IsAttached => _sink is not null;

    /// <summary>
    /// 把库日志接到 <paramref name="sink"/>。第一次调用会顺带初始化库的静态工厂；
    /// 之后再调用只是换目标（幂等，线程安全）。
    /// </summary>
    /// <param name="sink">宿主日志出口（界面层实现的适配器）。</param>
    /// <param name="minimumLevel">库日志的最低等级，默认 <see cref="SimulationLogLevel.Debug"/>（库的默认行为）。</param>
    public static void Attach(ISimulationLogSink sink, SimulationLogLevel minimumLevel = SimulationLogLevel.Debug)
    {
        ArgumentNullException.ThrowIfNull(sink);

        lock (SyncRoot)
        {
            _sink = sink;
            Logger.MinimumLevel = ToLogLevel(minimumLevel);

            if (_initialized)
                return;

            // 只挂自己的 provider：库文档写明「传了 configure 就不再加控制台 provider」，配置权归调用方。
            Logger.Initialize(builder => builder.AddProvider(new SinkLoggerProvider()));
            _initialized = true;
        }
    }

    // ── 等级映射：只在桥里做一次，两侧都只需要认识自己的枚举 ──

    private static LogLevel ToLogLevel(SimulationLogLevel level) => level switch
    {
        SimulationLogLevel.Debug => LogLevel.Debug,
        SimulationLogLevel.Info => LogLevel.Information,
        SimulationLogLevel.Warning => LogLevel.Warning,
        _ => LogLevel.Error,
    };

    private static SimulationLogLevel ToSimulationLevel(LogLevel level) => level switch
    {
        <= LogLevel.Debug => SimulationLogLevel.Debug,
        LogLevel.Information => SimulationLogLevel.Info,
        LogLevel.Warning => SimulationLogLevel.Warning,
        _ => SimulationLogLevel.Error, // Error / Critical
    };

    /// <summary>正文加工：异常拼进正文（sink 只有一个字符串参数），非默认类别前缀成模块名。</summary>
    private static string Describe(string category, string message, Exception? exception)
    {
        string text = exception is null
            ? message
            : $"{message} —— {exception.GetType().Name}: {exception.Message}";

        return string.IsNullOrEmpty(category) || category == Logger.DefaultCategory
            ? text
            : $"[{category}] {text}";
    }

    /// <summary>库日志 provider：只为转交而存在，不做缓冲、不做格式化（库已把消息格式化成字符串）。</summary>
    private sealed class SinkLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new SinkLogger(categoryName);

        // 没有需要释放的资源：sink 由宿主持有并负责其生命周期。
        public void Dispose()
        {
        }
    }

    /// <summary>库日志的接收端：等级判一次，然后交给当前 sink。</summary>
    private sealed class SinkLogger : ILogger
    {
        private readonly string _category;

        public SinkLogger(string category) => _category = category;

        /// <summary>库不使用日志作用域，无需实现（返回 null 是 M.E.L 允许的「无作用域」语义）。</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => _sink is not null && logLevel >= Logger.MinimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            // 取一次快照：Attach 可能在别的线程上换 sink。
            if (_sink is not { } sink)
                return;

            sink.Write(ToSimulationLevel(logLevel), _category, Describe(_category, formatter(state, exception), exception));
        }
    }
}
