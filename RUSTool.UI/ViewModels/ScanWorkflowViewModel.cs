using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Communication;
using RUSTool.Services.Logging;
using RUSTool.Services.Robot;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RUSTool.UI.ViewModels;

/// <summary>流程步骤的状态。四步走完 = 一次扫查完成。</summary>
public enum StepState
{
    /// <summary>还没轮到。</summary>
    Pending,

    /// <summary>当前正在做的这一步。</summary>
    Active,

    /// <summary>已完成。</summary>
    Done,
}

/// <summary>
/// 扫查流程中的一步（纯数据 + 展示派生量）。
///
/// <para>
/// 步骤状态建模成枚举，界面用主题的状态灯类表达：
/// 待办 = <c>dot idle</c>、进行中 = <c>dot info</c>、完成 = <c>dot success</c>。
/// 颜色全部来自主题，将来改"完成"用哪个绿只改一处。
/// </para>
/// </summary>
public sealed partial class ScanStep : ObservableObject
{
    public required string Index { get; init; }

    public required string Title { get; init; }

    public required string Hint { get; init; }

    [ObservableProperty]
    private StepState _state = StepState.Pending;

    /// <summary>
    /// 该步的产物摘要（"建图完成" / "起点 + 终点"…）——
    /// 用户既要知道能不能点（门控），也要知道点完有没有生效（回显），这里是后者。
    /// </summary>
    [ObservableProperty]
    private string? _detail;

    /// <summary>有产物摘要时才显示那枚小标签（避免空标签占位）。</summary>
    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    partial void OnDetailChanged(string? value) => OnPropertyChanged(nameof(HasDetail));

    // 三个互斥布尔量，专供 XAML 的 Classes.xxx 绑定（无需转换器）。
    public bool IsPending => State == StepState.Pending;

    public bool IsActive => State == StepState.Active;

    public bool IsDone => State == StepState.Done;

    /// <summary>只有"进行中"或"已完成"的步骤才显示自己的按钮组。</summary>
    public bool IsActionable => State != StepState.Pending;

    partial void OnStateChanged(StepState value)
    {
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsActionable));
    }
}

/// <summary>
/// 扫查工作流：把任务级指令串成 4 步状态机（① 预扫查 → ② 位姿 → ③ 规划 → ④ 执行）。
///
/// <para>
/// 状态推进规则（与 <c>docs/ui/zh-CN.md</c> 第 5 节一致）：
/// 上一步完成才允许点下一步；<c>stop</c> 任意阶段可达；<c>reset</c> 回到已使能态。
/// 所有按钮都有门控，"执行"在规划完成前点不动 —— 这是真机上的安全底线。
/// </para>
/// <para>
/// 步骤的完成标志有两个来源，缺一不可：
/// ① 同步回执 —— set_start_pose 这类短指令，回执 success 即完成；
/// ② 异步事件 —— 建图 / 规划 / 扫查是长任务，由 <c>pre_scan_done</c>、<c>plan_done</c>、
///    <c>motion_done</c> 事件通知，前端不靠计时去猜。
/// </para>
/// </summary>
public sealed partial class ScanWorkflowViewModel : ViewModelBase
{
    private readonly IRobotService _robot;
    private readonly ILogService _log;

    public ScanWorkflowViewModel(IRobotService robot, ILogService log)
    {
        _robot = robot;
        _log = log;

        Steps = new List<ScanStep>
        {
            new() { Index = "①", Title = "预扫查建图", Hint = "点动探头扫过体表" },
            new() { Index = "②", Title = "位姿选点", Hint = "记录起点与终点" },
            new() { Index = "③", Title = "路径规划", Hint = "plan 生成扫查路径" },
            new() { Index = "④", Title = "执行扫查", Hint = "execute 沿路径运动" },
        };

        Steps[0].State = StepState.Active;
        UpdateDerived();

        // 建图 / 规划 / 扫查都是长任务，完成与否由后端事件决定，前端不靠计时猜。
        _robot.EventReceived += OnEvent;
    }

    public IReadOnlyList<ScanStep> Steps { get; }

    /// <summary>当前阶段的文字描述，如"② 位姿选点"。</summary>
    [ObservableProperty]
    private string _currentStage = "① 预扫查建图";

    /// <summary>下一步该做什么 —— 界面上用主色高亮，是"向导感"的来源。</summary>
    [ObservableProperty]
    private string _nextAction = "开始预扫查";

    /// <summary>起点是否已记录（set_start_pose 的回执）。</summary>
    [ObservableProperty]
    private bool _hasStartPose;

    /// <summary>终点是否已记录（set_end_pose 的回执）。</summary>
    [ObservableProperty]
    private bool _hasEndPose;

    /// <summary>扫查是否正在执行（用于启用暂停 / 恢复 / 停止）。</summary>
    [ObservableProperty]
    private bool _isRunning;

    // ── 门控属性：每一步只有在前置条件满足时才可点 ──

    /// <summary>选起点：预扫查完成后才允许。</summary>
    public bool CanSetStart => Steps[0].State == StepState.Done;

    /// <summary>选终点：起点已记录后才允许。</summary>
    public bool CanSetEnd => CanSetStart && HasStartPose;

    /// <summary>规划：起点与终点都已记录后才允许。</summary>
    public bool CanPlan => CanSetEnd && HasEndPose;

    /// <summary>执行：规划完成后才允许。</summary>
    public bool CanExecute => Steps[2].State == StepState.Done;

    [RelayCommand]
    private async Task StartPreScan()
    {
        Handle("pre_scan_start", await _robot.PreScanStartAsync());
        NextAction = "结束预扫查（建图完成后点击）";
    }

    [RelayCommand]
    private async Task EndPreScan()
    {
        if (Handle("pre_scan_end", await _robot.PreScanEndAsync()))
            CompleteStep(0, 1);
    }

    [RelayCommand]
    private async Task SetStartPose()
    {
        if (!CanSetStart)
            return;

        if (Handle("set_start_pose", await _robot.SetStartPoseAsync()))
        {
            HasStartPose = true;
            UpdateDerived();
        }
    }

    [RelayCommand]
    private async Task SetEndPose()
    {
        if (!CanSetEnd)
            return;

        if (Handle("set_end_pose", await _robot.SetEndPoseAsync()))
        {
            HasEndPose = true;
            UpdateDerived();
        }
    }

    /// <summary>开始规划：步骤推进由 <c>plan_done</c> 事件完成，这里只负责下发。</summary>
    [RelayCommand]
    private async Task Plan()
    {
        if (!CanPlan)
            return;

        if (Handle("plan", await _robot.PlanAsync()))
            NextAction = "等待规划完成（plan_done）";
    }

    /// <summary>执行扫查：步骤推进由 <c>motion_done</c> / <c>scan_done</c> 事件完成。</summary>
    [RelayCommand]
    private async Task Execute()
    {
        if (!CanExecute)
            return;

        if (Handle("execute", await _robot.ExecuteAsync()))
        {
            IsRunning = true;
            UpdateDerived();
        }
    }

    [RelayCommand]
    private async Task Pause()
    {
        if (Handle("pause", await _robot.PauseAsync()))
        {
            IsRunning = false;
            UpdateDerived();
        }
    }

    [RelayCommand]
    private async Task Resume()
    {
        if (Handle("resume", await _robot.ResumeAsync()))
        {
            IsRunning = true;
            UpdateDerived();
        }
    }

    /// <summary>停止：任意阶段可达（安全约束），回到"已使能"态。</summary>
    [RelayCommand]
    private async Task Stop()
    {
        IsRunning = false;
        Handle("stop", await _robot.StopAsync());
        UpdateDerived();
    }

    /// <summary>复位：清空流程，从头再来。</summary>
    [RelayCommand]
    private async Task Reset()
    {
        ResetSteps();
        Handle("reset", await _robot.ResetAsync());
    }

    /// <summary>查询各阶段完成情况（query_pre_scan_done / query_motion_done），据此校正状态灯。</summary>
    [RelayCommand]
    private async Task RefreshStatus()
    {
        var pre = await _robot.QueryPreScanDoneAsync();
        if (pre.Success && pre.Result.Length > 0 && pre.Result[0] != 0)
            CompleteStep(0, 1);

        var motion = await _robot.QueryMotionDoneAsync();
        if (motion.Success && motion.Result.Length > 0 && motion.Result[0] != 0)
        {
            CompleteStep(2, 3);
            Steps[3].State = StepState.Done;
            IsRunning = false;
        }

        UpdateDerived();
        _log.Log(
            $"pre_scan_done={Steps[0].State == StepState.Done}, " +
            $"plan_done={Steps[2].State == StepState.Done}, " +
            $"scan_done={Steps[3].State == StepState.Done}",
            LogLevel.Info, "query");
    }

    // ── 步骤推进 ──

    /// <summary>把第 <paramref name="done"/> 步标记为完成、第 <paramref name="active"/> 步标记为进行中。</summary>
    private void CompleteStep(int done, int active)
    {
        Steps[done].State = StepState.Done;
        if (active < Steps.Count)
            Steps[active].State = StepState.Active;

        UpdateDerived();
    }

    /// <summary>清空流程回到第一步（只动本地状态；后端复位由 Reset 命令负责）。</summary>
    private void ResetSteps()
    {
        foreach (var step in Steps)
            step.State = StepState.Pending;

        Steps[0].State = StepState.Active;
        HasStartPose = false;
        HasEndPose = false;
        IsRunning = false;
        UpdateDerived();
    }

    /// <summary>
    /// 后端异步事件：长任务的完成 / 失败都在这里落地。
    /// 事件在后台线程到达，但这里只改 VM 属性、不碰控件集合，Avalonia 的属性绑定会自行 marshal。
    /// </summary>
    private void OnEvent(EventNotification evt)
    {
        switch (evt.EventName)
        {
            case "pre_scan_done":
                if (evt.Success)
                    CompleteStep(0, 1);
                break;

            case "plan_done":
                if (evt.Success)
                    CompleteStep(1, 2);
                break;

            case "motion_done":
            case "scan_done":
                if (evt.Success)
                {
                    CompleteStep(2, 3);
                    Steps[3].State = StepState.Done;
                    IsRunning = false;
                }
                break;

            case "error":
                _log.Log($"机器人上报错误: {evt.Message}", LogLevel.Error, "error");
                break;
        }

        UpdateDerived();
    }

    /// <summary>统一处理回执：成功写 Success，失败写 Error；返回是否成功。</summary>
    private bool Handle(string command, CommandResult r)
    {
        if (r.Success)
        {
            _log.Log($"{command} → 成功", LogLevel.Success, command);
            return true;
        }

        _log.Log($"{command} 失败: {r.Message}", LogLevel.Error, command);
        return false;
    }

    /// <summary>把"当前阶段 / 下一步 / 门控 / 各步产物"一次性同步到界面。</summary>
    private void UpdateDerived()
    {
        var active = Steps.FirstOrDefault(s => s.State == StepState.Active);
        CurrentStage = active is null ? "④ 扫查完成" : $"{active.Index} {active.Title}";

        NextAction = active?.Index switch
        {
            "①" => "结束预扫查（建图完成后点击）",
            "②" => HasStartPose ? (HasEndPose ? "开始规划" : "记录当前位姿为终点") : "记录当前位姿为起点",
            "③" => "等待规划完成（plan_done）",
            "④" => IsRunning ? "沿路径执行中（可暂停 / 停止）" : "开始执行",
            _ => "扫查已完成，可复位开始新流程",
        };

        if (Steps[3].State == StepState.Done)
            IsRunning = false;

        // 各步的"产物摘要"——让用户知道这一步确实做出了东西。
        Steps[0].Detail = Steps[0].State == StepState.Done ? "建图完成" : null;
        Steps[1].Detail = HasStartPose && HasEndPose ? "起点 + 终点" : HasStartPose ? "起点" : null;
        Steps[2].Detail = Steps[2].State == StepState.Done ? "路径已生成" : null;
        Steps[3].Detail = Steps[3].State == StepState.Done ? "已完成" : IsRunning ? "进行中" : null;

        OnPropertyChanged(nameof(CanSetStart));
        OnPropertyChanged(nameof(CanSetEnd));
        OnPropertyChanged(nameof(CanPlan));
        OnPropertyChanged(nameof(CanExecute));
    }
}
