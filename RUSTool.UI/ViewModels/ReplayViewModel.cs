using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Services.Logging;
using System;
using System.Collections.Generic;

namespace RUSTool.UI.ViewModels;

/// <summary>
/// 记录 / 回放面板：把一次扫查的过程按时间轴重放。
///
/// <para>
/// 时间轴是真正的 <c>Slider</c>，支持 A/B 循环点与逐帧步进；
/// 播放进度由面板【自己的定时器】推进 —— 只在"正在播放"时走表，暂停即停表，
/// 不像原来那样让主 ViewModel 一直空转。
/// </para>
/// </summary>
public sealed partial class ReplayViewModel : ViewModelBase
{
    private readonly ILogService _log;
    private readonly DispatcherTimer? _timer;

    /// <summary>回放总时长（秒）—— 00:12:45。</summary>
    public double TotalSeconds => 765;

    public IReadOnlyList<string> SpeedOptions { get; } = new[] { "0.5x", "1.0x", "1.5x", "2.0x" };

    public IReadOnlyList<string> Sources { get; } =
        new[] { "最近一次扫查", "已保存记录 01", "已保存记录 02" };

    [ObservableProperty]
    private double _position = 201; // 秒

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private int _speedIndex = 1;

    /// <summary>播放速度倍率，由 <see cref="SpeedIndex"/> 决定。</summary>
    public double Speed => SpeedIndex switch
    {
        0 => 0.5,
        2 => 1.5,
        3 => 2.0,
        _ => 1.0,
    };

    [ObservableProperty]
    private int _sourceIndex;

    [ObservableProperty]
    private bool _loop;

    [ObservableProperty]
    private bool _hasMarkA;

    [ObservableProperty]
    private bool _hasMarkB;

    [ObservableProperty]
    private double _markA;

    [ObservableProperty]
    private double _markB;

    /// <summary>"00:03:21 / 00:12:45" —— 等宽字体下不会左右抖。</summary>
    public string TimeText => $"{Format(Position)} / {Format(TotalSeconds)}";

    public string SpeedText => SpeedOptions[SpeedIndex];

    partial void OnPositionChanged(double value) => OnPropertyChanged(nameof(TimeText));

    partial void OnSpeedIndexChanged(int value)
    {
        OnPropertyChanged(nameof(Speed));
        OnPropertyChanged(nameof(SpeedText));
    }

    public ReplayViewModel(ILogService log)
    {
        _log = log;
        _markA = 138;
        _markB = 566;
        _hasMarkA = true;
        _hasMarkB = true;

        // 33ms ≈ 30 fps：与状态流的刷新率对齐，进度看起来才是连续走动的。
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Advance(1.0 / 30);
    }

    /// <summary>推进播放进度（由本面板的定时器驱动）。</summary>
    public void Advance(double deltaSeconds)
    {
        if (!IsPlaying)
        {
            return;
        }

        var next = Position + deltaSeconds * Speed;

        // A/B 循环：设了循环点就只在 A~B 之间往返，否则从头再来。
        var upper = Loop && HasMarkB ? MarkB : TotalSeconds;
        var lower = Loop && HasMarkA ? MarkA : 0;

        if (next >= upper)
        {
            if (Loop)
            {
                next = lower;
            }
            else
            {
                next = TotalSeconds;
                IsPlaying = false;
                _timer?.Stop();
                _log.Log("回放结束", LogLevel.Info, "replay");
            }
        }

        Position = next;
    }

    [RelayCommand]
    private void TogglePlay()
    {
        IsPlaying = !IsPlaying;

        // 只在这块面板真的在播放时才走表。
        if (IsPlaying)
            _timer?.Start();
        else
            _timer?.Stop();

        _log.Log(IsPlaying ? $"开始回放（{SpeedText}）" : "回放已暂停", LogLevel.Info, "replay");
    }

    [RelayCommand]
    private void JumpStart() => Position = Loop && HasMarkA ? MarkA : 0;

    [RelayCommand]
    private void JumpEnd() => Position = Loop && HasMarkB ? MarkB : TotalSeconds;

    /// <summary>步退 / 步进：一帧 = 1/30 秒。</summary>
    [RelayCommand]
    private void StepBack() => Position = Math.Max(0, Position - 1.0 / 30);

    [RelayCommand]
    private void StepForward() => Position = Math.Min(TotalSeconds, Position + 1.0 / 30);

    /// <summary>把 A 点设到当前位置。
    /// ⚠ 方法名不能叫 MarkA —— 那会和 [ObservableProperty] 生成的 MarkA 属性同名（CS0102）。</summary>
    [RelayCommand]
    private void SetMarkA()
    {
        MarkA = Position;
        HasMarkA = true;
        _log.Log($"A 点已设为 {Format(MarkA)}", LogLevel.Info, "replay");
    }

    /// <summary>把 B 点设到当前位置。</summary>
    [RelayCommand]
    private void SetMarkB()
    {
        MarkB = Position;
        HasMarkB = true;
        _log.Log($"B 点已设为 {Format(MarkB)}", LogLevel.Info, "replay");
    }

    [RelayCommand]
    private void Export() => _log.Log("导出回放数据（CSV）…", LogLevel.Info, "replay");

    [RelayCommand]
    private void Browse() => _log.Log("打开记录文件选择框…", LogLevel.Info, "replay");

    private static string Format(double seconds) =>
        TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss");
}
