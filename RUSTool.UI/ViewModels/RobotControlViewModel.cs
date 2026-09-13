using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Communication;
using RUSTool.Services.Logging;
using RUSTool.Services.Robot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RUSTool.UI.ViewModels;

/// <summary>
/// 一根点动轴（X / Y / Z / 绕X / 绕Y / 绕Z）—— 纯数据。
///
/// <para>
/// 轴是数据、行是模板：<c>ItemsControl</c> 用同一个 DataTemplate 渲染 6 次，
/// 改按钮宽度只改一处；12 个点动按钮共用面板 code-behind 里的一份交互逻辑。
/// </para>
/// <para>
/// 这里改成数据驱动：轴是数据，行是模板，<c>ItemsControl</c> 渲染 6 次。
/// 交互（按下开始 / 松开停止）由面板的 code-behind 统一处理 —— 真机上的点动是
/// "按住走、松手停"，用 Button 的 Click 命令表达不了这个语义。
/// </para>
/// </summary>
public sealed partial class JogAxis : ObservableObject
{
    /// <summary>短名，用于与按钮的 Tag 参数对应（如 "X"、"Rz"）。</summary>
    public required string Key { get; init; }

    /// <summary>轴序号 1~6，与后端 jog 指令的 axis 参数对齐。</summary>
    public required int Axis { get; init; }

    /// <summary>显示名。切到关节空间时由面板改为"关节 N"。</summary>
    [ObservableProperty]
    private string _label = "";
}


/// <summary>
/// 手动 / 点动控制面板：MoveJ / MoveL、点动（按下 / 松开）、暂停 / 恢复、急停、复位。
/// 只依赖 <see cref="IRobotService"/>，不接触协议字符串与传输层。
/// </summary>
public sealed partial class RobotControlViewModel : ViewModelBase
{
    private readonly IRobotService _robot;
    private readonly ILogService _log;

    public RobotControlViewModel(IRobotService robot, ILogService log)
    {
        _robot = robot;
        _log = log;

        Axes = new List<JogAxis>
        {
            new() { Key = "X",  Axis = 1, Label = "X 方向" },
            new() { Key = "Y",  Axis = 2, Label = "Y 方向" },
            new() { Key = "Z",  Axis = 3, Label = "Z 方向" },
            new() { Key = "Rx", Axis = 4, Label = "绕 X 轴旋转" },
            new() { Key = "Ry", Axis = 5, Label = "绕 Y 轴旋转" },
            new() { Key = "Rz", Axis = 6, Label = "绕 Z 轴旋转" },
        };

        // 其余 VM 只读连接状态；连接动作由会话面板独占。
        _robot.ConnectionChanged += connected => IsConnected = connected;
    }

    public IReadOnlyList<JogAxis> Axes { get; }

    /// <summary>点动参考系下拉项：0 = 基坐标系 / 1 = 工具坐标系 / 2 = 关节空间。</summary>
    public IReadOnlyList<string> JogFrames { get; } = new[] { "基坐标系", "工具坐标系", "关节空间" };

    [ObservableProperty]
    private int _jogFrameIndex;

    /// <summary>点动速度百分比（0~100）。</summary>
    [ObservableProperty]
    private double _jogSpeed = 35;

    /// <summary>加速度百分比（0~100）。</summary>
    [ObservableProperty]
    private double _jogAcc = 30;

    [ObservableProperty]
    private string _moveJInput = "10, -45, 60, -8, 34, 4";

    [ObservableProperty]
    private string _moveLInput = "0.420, -0.080, 0.310, 178.5, -3.2, 91.0";

    /// <summary>最近一次操作的描述，显示在面板底部（点动有没有生效，用户看这里）。</summary>
    [ObservableProperty]
    private string _lastAction = "就绪";

    [ObservableProperty]
    private bool _isJogging;

    /// <summary>是否已连上控制通道（只读；连接动作由会话面板负责）。</summary>
    [ObservableProperty]
    private bool _isConnected;

    /// <summary>最近一次失败的原因（空串 = 无错误）。</summary>
    [ObservableProperty]
    private string _errorMessage = "";

    /// <summary>下拉索引 → 后端参考系编码：0=关节空间 / 2=基坐标 / 4=工具坐标。</summary>
    public int JogRefFrame => JogFrameIndex switch { 0 => 2, 1 => 4, _ => 0 };

    /// <summary>切换参考系时把各轴显示名在"方向"与"关节"之间切换（真机上这一步决定按下去走什么）。</summary>
    partial void OnJogFrameIndexChanged(int value)
    {
        var names = value == 2
            ? new[] { "关节 1", "关节 2", "关节 3", "关节 4", "关节 5", "关节 6" }
            : new[] { "X 方向", "Y 方向", "Z 方向", "绕 X 轴旋转", "绕 Y 轴旋转", "绕 Z 轴旋转" };

        for (var i = 0; i < Axes.Count; i++)
            Axes[i].Label = names[i];

        OnPropertyChanged(nameof(JogRefFrame));
    }


    // ── 点动（按下开始 / 松开减速停止，由面板 code-behind 驱动）──

    /// <summary>点动开始（按下）：按当前参考系下发 start_jog。</summary>
    public async Task BeginJogAsync(int axis, int direction)
    {
        var jog = new JogParameters(JogRefFrame, axis, direction, (int)JogSpeed, (int)JogAcc, 0);
        var r = await _robot.StartJogAsync(jog);

        if (r.Success)
        {
            IsJogging = true;
            LastAction = "点动中…（松开按钮即减速停止）";
            _log.Log($"start_jog(axis={axis}, dir={direction}, speed={(int)JogSpeed})",
                LogLevel.Info, "start_jog");
        }
        else
        {
            ErrorMessage = $"点动失败: {r.Message}";
            LastAction = "点动失败";
            _log.Log($"点动失败: {r.Message}", LogLevel.Error, "start_jog");
        }
    }

    /// <summary>点动结束（松开）：减速停止。</summary>
    public async Task EndJogAsync()
    {
        if (!IsJogging)
            return;

        IsJogging = false;
        LastAction = "点动已停止";

        var r = await _robot.StopJogAsync();
        if (!r.Success)
            _log.Log($"停止点动失败: {r.Message}", LogLevel.Warn, "stop_jog_decel");
    }

    /// <summary>停止点动按钮（等价于松开按钮）。</summary>
    [RelayCommand]
    private Task StopJog() => EndJogAsync();

    // ── 运动指令 ──

    /// <summary>关节空间运动：输入是【度】，下发前转成弧度。</summary>
    [RelayCommand]
    private async Task MoveJ()
    {
        if (!TryParse(MoveJInput, out var joints, out var error))
        {
            ErrorMessage = $"movej 参数格式错误：{error}";
            LastAction = "movej 参数错误";
            _log.Log($"movej 参数格式错误：{MoveJInput}", LogLevel.Error, "movej");
            return;
        }

        for (var i = 0; i < joints.Length; i++)
            joints[i] *= Math.PI / 180.0;

        Finish("movej", await _robot.MoveJAsync(joints));
    }

    /// <summary>笛卡尔直线运动：x,y,z 是米，rx,ry,rz 是度（姿态转弧度后下发）。</summary>
    [RelayCommand]
    private async Task MoveL()
    {
        if (!TryParse(MoveLInput, out var pose, out var error))
        {
            ErrorMessage = $"movel 参数格式错误：{error}";
            LastAction = "movel 参数错误";
            _log.Log($"movel 参数格式错误：{MoveLInput}", LogLevel.Error, "movel");
            return;
        }

        for (var i = 3; i < 6; i++)
            pose[i] *= Math.PI / 180.0;

        Finish("movel", await _robot.MoveLAsync(pose));
    }

    /// <summary>暂停运动。</summary>
    [RelayCommand]
    private async Task Pause() => Finish("pause", await _robot.PauseAsync());

    /// <summary>恢复运动。</summary>
    [RelayCommand]
    private async Task Resume() => Finish("resume", await _robot.ResumeAsync());

    /// <summary>停止所有运动（任务级 stop）。</summary>
    [RelayCommand]
    private async Task Stop() => Finish("stop", await _robot.StopAsync());

    /// <summary>复位：把后端恢复到已使能态（reset），并清掉点动中的界面态。</summary>
    [RelayCommand]
    private async Task ResetJog()
    {
        IsJogging = false;
        Finish("reset", await _robot.ResetAsync());
    }

    /// <summary>统一处理回执：成功写 Success、失败写 Error，两者都更新底部回显与错误条。</summary>
    private void Finish(string command, CommandResult r)
    {
        if (r.Success)
        {
            ErrorMessage = "";
            LastAction = $"{command} 已下发";
            _log.Log($"{command} → 成功", LogLevel.Success, command);
        }
        else
        {
            ErrorMessage = $"{command} 失败: {r.Message}";
            LastAction = $"{command} 失败";
            _log.Log($"{command} 失败: {r.Message}", LogLevel.Error, command);
        }
    }

    /// <summary>解析 6 个逗号分隔的数字。</summary>
    private static bool TryParse(string text, out double[] values, out string error)
    {
        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        values = new double[6];

        if (parts.Length != 6)
        {
            error = $"需要 6 个数字（逗号分隔），实际给了 {parts.Length} 个";
            return false;
        }

        for (var i = 0; i < 6; i++)
        {
            if (!double.TryParse(parts[i], out values[i]))
            {
                error = $"\"{parts[i]}\" 不是数字";
                return false;
            }
        }

        error = "";
        return true;
    }
}

