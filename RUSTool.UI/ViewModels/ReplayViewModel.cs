using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Communication;
using RUSTool.Replay;
using RUSTool.Services.Logging;
using RUSTool.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RUSTool.UI.ViewModels;

/// <summary>
/// 回放面板的 ViewModel —— <b>薄适配层</b>：把 <see cref="LocalReplayPlayer"/>（纯逻辑，后台线程）
/// 的状态与事件翻译成界面可绑定的属性与命令。
///
/// <para>
/// <b>回放是本地行为</b>（后端协议已把回放移交前端）：前端直接读录音目录
/// <c>&lt;records_dir&gt;/*.rusrec</c>（目录来自 <see cref="SettingsService"/>），
/// 自行解码 / 播放 / 可视化，不再经 ROS 话题。
/// </para>
/// <para>
/// <b>进入回放要"接管"</b>：播放前回调 <see cref="RequestTakeover"/>（由组装层提供）——
/// 先确认没有运动在进行、兜底 `stop`、再断开后端连接，之后才本地播放。引擎事件在后台线程触发，
/// 这里统一 Post 回 UI 线程再更新绑定/抛给下游。
/// </para>
/// </summary>
public sealed partial class ReplayViewModel : ViewModelBase
{
    private readonly ILogService _log;
    private readonly SettingsService _settings;
    private readonly LocalReplayPlayer _player;
    private readonly List<string> _filePaths = [];

    /// <summary>是否已完成一次"接管"（断开后端）。完成后不再重复。</summary>
    private bool _takenOver;

    /// <summary>一帧状态（通道 0）到达：接到 HUD + 曲线（与实时同一下游）。</summary>
    public event Action<BridgeProtocol.StateFrame>? StateFramePlayed;

    /// <summary>一帧点云（通道 1）到达：接到 3D 视口的点云邮箱。</summary>
    public event Action<SensorPointCloudFrame>? PointCloudPlayed;

    /// <summary>
    /// 播放前的"接管"回调（组装层注入）：检查运动 → 兜底 stop → 断开后端。
    /// 返回 false 表示不允许进入回放（如运动进行中 / 停止失败）。
    /// </summary>
    public Func<Task<bool>>? RequestTakeover { get; set; }

    public ReplayViewModel(SettingsService settings, ILogService log)
    {
        _settings = settings;
        _log = log;
        _player = new LocalReplayPlayer(log);

        _player.StateFramePlayed += frame => Dispatcher.UIThread.Post(() => StateFramePlayed?.Invoke(frame));
        _player.PointCloudPlayed += cloud => Dispatcher.UIThread.Post(() => PointCloudPlayed?.Invoke(cloud));
        _player.Changed += () => Dispatcher.UIThread.Post(SyncFromEngine);
        _player.Error += _ => Dispatcher.UIThread.Post(SyncFromEngine);

        // 启动即列出默认录音目录（离线也能看到有哪些录音）。
        RefreshList();
    }

    /// <summary>录音目录下的文件（仅文件名，下拉显示用）。</summary>
    public ObservableCollection<string> Files { get; } = new();

    public IReadOnlyList<string> SpeedOptions { get; } =
        new[] { "0.25x", "0.5x", "1.0x", "2.0x", "4.0x" };

    private static readonly double[] SpeedValues = { 0.25, 0.5, 1.0, 2.0, 4.0 };

    [ObservableProperty] private int _selectedFileIndex = -1;
    [ObservableProperty] private int _state;        // 0 idle / 1 playing / 2 paused / 3 finished
    [ObservableProperty] private double _position;
    [ObservableProperty] private double _duration;
    [ObservableProperty] private double _speed = 1.0;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private string _currentFile = "";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private int _speedIndex = 2;

    public bool HasFiles => Files.Count > 0;
    public bool IsIdle => State == 0;
    public bool IsPlaying => State == 1;
    public bool IsPaused => State == 2;
    public bool IsFinished => State == 3;
    public bool IsDataEmpty => !IsLoaded;

    public string PlayButtonText => IsPlaying ? "⏸" : "▶";
    public string StatusText => State switch
    {
        1 => $"播放中 {SpeedText}",
        2 => "已暂停",
        3 => "已结束",
        _ => IsLoaded ? "已载入" : "未载入",
    };
    public string TimeText => $"{Format(Position)} / {Format(Duration)}";
    public string SpeedText => $"{Speed:0.##}x";
    public double MaxPosition => Duration > 0 ? Duration : 1.0;

    partial void OnSelectedFileIndexChanged(int value)
    {
        if (value >= 0 && value < _filePaths.Count)
            _settings.LastReplayFile = _filePaths[value];
    }

    partial void OnPositionChanged(double value) => OnPropertyChanged(nameof(TimeText));
    partial void OnDurationChanged(double value)
    {
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(MaxPosition));
    }
    partial void OnSpeedChanged(double value) => OnPropertyChanged(nameof(SpeedText));
    partial void OnCurrentFileChanged(string value) => OnPropertyChanged(nameof(TimeText));
    partial void OnIsLoadedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDataEmpty));
        OnPropertyChanged(nameof(StatusText));
    }
    partial void OnStateChanged(int value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(PlayButtonText));
        OnPropertyChanged(nameof(StatusText));
    }
    partial void OnSpeedIndexChanged(int value)
    {
        if (value < 0 || value >= SpeedValues.Length)
            return;
        Speed = SpeedValues[value];
        _player.SetSpeed(Speed);
    }

    // ── 目录 / 文件 ──

    /// <summary>重新列出录音目录（目录来自设置）。</summary>
    [RelayCommand]
    private void RefreshList()
    {
        Files.Clear();
        _filePaths.Clear();

        IReadOnlyList<string> files = RecordingsLibrary.List(_settings.RecordsDirectory);
        foreach (string path in files)
        {
            _filePaths.Add(path);
            Files.Add(Path.GetFileName(path));
        }
        OnPropertyChanged(nameof(HasFiles));

        // 尝试回填上次打开的文件。
        int index = _filePaths.FindIndex(p => p == _settings.LastReplayFile);
        SelectedFileIndex = index >= 0 ? index : (Files.Count > 0 ? 0 : -1);

        _log.Log($"录音目录：{_settings.RecordsDirectoryFull}（{Files.Count} 个文件）", LogLevel.Info, "replay");
    }

    /// <summary>载入当前选中的录音（不播放）。</summary>
    [RelayCommand]
    private Task LoadSelected()
    {
        if (SelectedFileIndex < 0 || SelectedFileIndex >= _filePaths.Count)
        {
            _log.Log("未选择录音文件", LogLevel.Warn, "replay");
            return Task.CompletedTask;
        }
        return LoadCoreAsync(_filePaths[SelectedFileIndex]);
    }

    /// <summary>按绝对路径载入（由「打开…」文件对话框调用）。</summary>
    public async Task LoadFileAsync(string path)
    {
        await LoadCoreAsync(path);
        // 打开的文件不在目录清单里也能载入，但下拉不强行加项；记录到设置以便下次。
        _settings.LastReplayFile = path;
    }

    /// <summary>
    /// 同步载入（仅供离屏截图 / 测试把"已载入"这一态渲染出来）：
    /// UI 线程上直接打开文件，避免 <see cref="LoadFileAsync"/> 的 <c>Task.Run</c> 续体回 UI 线程造成阻塞。
    /// </summary>
    public void LoadSynchronously(string path)
    {
        try
        {
            _player.Load(path);
        }
        catch (Exception ex)
        {
            _log.Log($"打开录音失败：{ex.Message}", LogLevel.Error, "replay");
            return;
        }
        _settings.LastReplayFile = path;
        SyncFromEngine();
    }

    private async Task LoadCoreAsync(string path)
    {
        try
        {
            await Task.Run(() => _player.Load(path)); // 打开 + 扫描在后台
            SyncFromEngine();
            _log.Log($"已载入本地录音 {CurrentFile}（{Detail}）", LogLevel.Success, "replay");
        }
        catch (Exception ex)
        {
            _log.Log($"打开录音失败：{ex.Message}", LogLevel.Error, "replay");
        }
    }

    // ── 传输 ──

    [RelayCommand]
    private async Task TogglePlay()
    {
        if (!IsLoaded)
        {
            _log.Log("请先载入回放文件", LogLevel.Warn, "replay");
            return;
        }

        if (IsPlaying)
        {
            _player.Pause();
            return;
        }

        // 首次播放先接管：断后端、确保不再运动，之后才本地播放。
        if (!_takenOver)
        {
            if (RequestTakeover is null)
            {
                _log.Log("未配置回放接管流程，无法进入回放", LogLevel.Error, "replay");
                return;
            }
            if (!await RequestTakeover())
                return; // 拒绝（运动进行中 / 停止失败），保持现状
            _takenOver = true;
        }

        _player.Play();
    }

    [RelayCommand]
    private void Stop() => _player.Stop();

    [RelayCommand]
    private void JumpStart() => _player.SeekTo(0);

    [RelayCommand]
    private void SkipBack() => _player.Skip(-5);

    [RelayCommand]
    private void SkipForward() => _player.Skip(5);

    [RelayCommand]
    private void StepForward() => _player.Step();

    /// <summary>时间轴拖拽 / 方向键定位（由 ReplayModule 的 code-behind 调用）。</summary>
    public void SeekTo(double seconds) => _player.SeekTo(seconds);

    /// <summary>把引擎状态读进绑定属性（在 UI 线程调用）。</summary>
    private void SyncFromEngine()
    {
        State = (int)_player.State;
        Position = _player.Position;
        Duration = _player.Duration;
        IsLoaded = _player.IsLoaded;
        CurrentFile = _player.CurrentFile;
        Speed = _player.Speed;

        Detail = _player.IsLoaded
            ? $"{_player.RecordCount} 条记录 · {(_player.HasIndex ? "有索引" : "无索引(可能崩溃/未封存)")}"
            : "";
    }

    private static string Format(double seconds)
    {
        if (seconds < 0 || double.IsNaN(seconds))
            seconds = 0;
        return TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
    }
}
