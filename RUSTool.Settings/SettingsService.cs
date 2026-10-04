using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace RUSTool.Settings;

/// <summary>
/// 前端全局参数的读写服务（界面直接绑定它的属性，改动即落盘）。
///
/// <para>
/// <b>唯一的全局参数来源：</b>构造时从 <see cref="ISettingsStore"/> 读一次，之后任何属性变化
/// 都写回存储。组装层（<c>App.CreateMainViewModel</c>）持有它，把 bridge 地址 / 录音目录等
/// 注入到需要的地方，而不是散落硬编码。
/// </para>
/// <para>
/// 构造阶段不触发落盘（<c>_loaded</c> 门控），否则首次启动会用默认值覆盖用户已有设置。
/// </para>
/// </summary>
public sealed partial class SettingsService : ObservableObject
{
    private readonly ISettingsStore _store;
    private bool _loaded;

    public SettingsService(ISettingsStore store)
    {
        _store = store;

        AppSettings s = store.Load();
        _recordsDirectory = s.RecordsDirectory;
        _bridgeHost = s.BridgeHost;
        _bridgePort = s.BridgePort;
        _lastReplayFile = s.LastReplayFile;

        _loaded = true;
    }

    /// <summary>设置文件位置（诊断用）。</summary>
    public string Location => _store.Location;

    /// <summary>后端 recorder 的录音目录（前后端需指向同一目录）；相对路径按前端进程工作目录解析。</summary>
    [ObservableProperty]
    private string _recordsDirectory;

    /// <summary>解析成绝对路径的录音目录（目录枚举 / 读取用）；空值按当前目录。</summary>
    public string RecordsDirectoryFull =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(RecordsDirectory) ? "." : RecordsDirectory.Trim());

    /// <summary>后端 bridge 主机。</summary>
    [ObservableProperty]
    private string _bridgeHost;

    /// <summary>后端 bridge 端口。</summary>
    [ObservableProperty]
    private int _bridgePort;

    /// <summary>上次回放的文件（绝对路径）。</summary>
    [ObservableProperty]
    private string _lastReplayFile;

    partial void OnRecordsDirectoryChanged(string value)
    {
        OnPropertyChanged(nameof(RecordsDirectoryFull));
        Persist();
    }

    partial void OnBridgeHostChanged(string value) => Persist();

    partial void OnBridgePortChanged(int value) => Persist();

    partial void OnLastReplayFileChanged(string value) => Persist();

    private void Persist()
    {
        if (!_loaded)
            return;

        _store.Save(new AppSettings
        {
            RecordsDirectory = RecordsDirectory,
            BridgeHost = BridgeHost,
            BridgePort = BridgePort,
            LastReplayFile = LastReplayFile,
        });
    }
}
