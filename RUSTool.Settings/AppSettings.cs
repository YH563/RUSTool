namespace RUSTool.Settings;

/// <summary>
/// 前端全局参数的<b>可序列化模型</b>（与 <see cref="SettingsService"/> 的界面绑定属性一一对应）。
///
/// <para>
/// 这是落盘单位：只放「需要持久化」的字段，且都必须有默认值 ——
/// 配置文件缺失 / 损坏 / 新版本多了字段时，反序列化都能回退到默认值而不是崩掉。
/// </para>
/// </summary>
public sealed class AppSettings
{
    /// <summary>录音目录（后端 recorder 的 <c>output_dir</c>，前后端需指向同一绝对目录）。默认相对当前工作目录的 <c>records</c>。</summary>
    public string RecordsDirectory { get; set; } = "records";

    /// <summary>后端 bridge 主机名。</summary>
    public string BridgeHost { get; set; } = "127.0.0.1";

    /// <summary>后端 bridge WebSocket 端口。</summary>
    public int BridgePort { get; set; } = 8765;

    /// <summary>上次回放的文件（绝对路径），用于下次启动自动回填。</summary>
    public string LastReplayFile { get; set; } = "";

    public AppSettings Clone() => new()
    {
        RecordsDirectory = RecordsDirectory,
        BridgeHost = BridgeHost,
        BridgePort = BridgePort,
        LastReplayFile = LastReplayFile,
    };
}
