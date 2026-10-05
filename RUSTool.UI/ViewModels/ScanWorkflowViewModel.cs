using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Communication;
using RUSTool.Services.Logging;
using RUSTool.Services.Robot;
using RUSTool.Services.Robot.Workflows;
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
/// </para>
/// </summary>
public sealed partial class ScanStep : ObservableObject
{
    public required string Index { get; init; }

    public required string Title { get; init; }

    public required string Hint { get; init; }

    [ObservableProperty]
    private StepState _state = StepState.Pending;

    /// <summary>该步的产物摘要（"建图完成" / "起点 + 终点"…）——门控之外的回显。</summary>
    [ObservableProperty]
    private string? _detail;

    /// <summary>有产物摘要时才显示那枚小标签（避免空标签占位）。</summary>
    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    partial void OnDetailChanged(string? value) => OnPropertyChanged(nameof(HasDetail));

    // 三个互斥布尔量，专供 XAML 的 Classes.xxx 绑定（无需转换器）。
    public bool IsPending => State == StepState.Pending;

    public bool IsActive => State == StepState.Active;

    public bool IsDone => State == StepState.Done;

    partial void OnStateChanged(StepState value)
    {
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsDone));
    }
}

/// <summary>
/// 扫查工作流：把任务级指令串成 4 步 —— ① 预扫查 → ② 位姿 → ③ 规划 → ④ 执行。
///
/// <para>
/// <b>状态由 <see cref="ScanStateMachine"/> 驱动，本类不再自己维护阶段。</b>
/// 用户动作先过 <c>CanFire</c> 门控、再下发命令；后端异步事件（pre_scan_done / plan_done /
/// motion_done / scan_done / error）驱动状态转移。界面只读 <c>Stage</c> 派生出的
/// 当前阶段 / 下一步 / 各步状态灯 —— 「按钮灰不灰」与「点了能不能执行」同源。
/// </para>
/// <para>
/// 长任务（建图 / 规划 / 扫查）完成与否以后端事件为准，前端不靠计时猜；
/// 命令回执与异步事件谁先到都算数（状态机里显式写了无害自转移）。
/// </para>
/// </summary>
public sealed partial class ScanWorkflowViewModel : ViewModelBase
{
    private readonly IRobotService _robot;
    private readonly ILogService _log;
    private readonly ScanStateMachine _fsm = new();

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

        _fsm.StageChanged += _ => UpdateDerived();

        // 建图 / 规划 / 扫查都是长任务，完成与否由后端事件决定。
        _robot.EventReceived += OnEvent;

        UpdateDerived();
    }

    public IReadOnlyList<ScanStep> Steps { get; }

    /// <summary>当前阶段的文字描述（卡片头 / 临床标签）。</summary>
    [ObservableProperty]
    private string _currentStage = "未开始";

    /// <summary>下一步该做什么 —— 界面上用主色高亮，是"向导感"的来源。</summary>
    [ObservableProperty]
    private string _nextAction = "开始预扫查";

    /// <summary>扫查是否处于暂停（本地界面态；后端只回执 pause）。</summary>
    [ObservableProperty]
    private bool _isPaused;

    // ── 状态机派生量 ──

    /// <summary>起点是否已记录。</summary>
    public bool HasStartPose => _fsm.StartPoseSet;

    /// <summary>终点是否已记录。</summary>
    public bool HasEndPose => _fsm.EndPoseSet;

    /// <summary>扫查是否正在执行（且未暂停）。</summary>
    public bool IsRunning => _fsm.Stage == ScanStage.Executing && !IsPaused;

    // ── 门控属性：每一步只有在前置条件满足时才可点（与状态机同源）──

    public bool CanStartPreScan => _fsm.CanFire(ScanTrigger.StartPreScan);
    public bool CanEndPreScan => _fsm.CanFire(ScanTrigger.EndPreScan);
    public bool CanSetStart => _fsm.CanFire(ScanTrigger.SetStartPose);
    public bool CanSetEnd => _fsm.CanFire(ScanTrigger.SetEndPose);
    public bool CanPlan => _fsm.CanFire(ScanTrigger.StartPlan);
    public bool CanExecute => _fsm.CanFire(ScanTrigger.Execute);
    public bool CanPause => IsRunning;
    public bool CanResume => IsPaused && _fsm.Stage == ScanStage.Executing;
    public bool CanStop => _fsm.Stage != ScanStage.Idle;
    public bool CanReset => _fsm.Stage != ScanStage.Idle;

    /// <summary>
    /// 「下一步」是否可用（临床主 CTA）：按当前阶段路由到该做的那件事；
    /// 规划中 / 执行中没有"下一步"，此时按钮不可点（用暂停 / 停止）。
    /// </summary>
    public bool CanNextStep => _fsm.Stage switch
    {
        ScanStage.Idle or ScanStage.PreScanning or ScanStage.Posing
            or ScanStage.Ready or ScanStage.Completed or ScanStage.Faulted => true,
        _ => false,
    };

    partial void OnIsPausedChanged(bool value) => RaiseGates();

    // ── 用户动作：先过门控 → 出发状态机 → 下发命令；命令失败则判故障 ──

    [RelayCommand]
    private Task StartPreScan() => FireThenSend(ScanTrigger.StartPreScan, "pre_scan_start", _robot.PreScanStartAsync);

    [RelayCommand]
    private Task EndPreScan() => FireThenSend(ScanTrigger.EndPreScan, "pre_scan_end", _robot.PreScanEndAsync);

    /// <summary>开始规划：状态先到 Planning，步骤推进由 <c>plan_done</c> 事件完成。</summary>
    [RelayCommand]
    private Task Plan() => FireThenSend(ScanTrigger.StartPlan, "plan", _robot.PlanAsync);

    /// <summary>执行扫查：状态先到 Executing，完成由 <c>motion_done</c> / <c>scan_done</c> 事件决定。</summary>
    [RelayCommand]
    private Task Execute() => FireThenSend(ScanTrigger.Execute, "execute", _robot.ExecuteAsync);

    /// <summary>
    /// 「下一步」（临床主 CTA）：按当前阶段路由到该做的那件事，文案与动作同源。
    /// 规划中 / 执行中不路由（没有可点的"下一步"）。
    /// </summary>
    [RelayCommand]
    private async Task NextStep()
    {
        switch (_fsm.Stage)
        {
            case ScanStage.Idle:
                await StartPreScan();
                break;
            case ScanStage.PreScanning:
                await EndPreScan();
                break;
            case ScanStage.Posing:
                await (HasStartPose
                    ? (HasEndPose ? Plan() : SetEndPose())
                    : SetStartPose());
                break;
            case ScanStage.Ready:
                await Execute();
                break;
            case ScanStage.Completed:
            case ScanStage.Faulted:
                await Reset();
                break;
        }
    }

    /// <summary>选起点：短指令，以回执为准 —— 成功才标记。</summary>
    [RelayCommand]
    private Task SetStartPose() => SendThenFire(ScanTrigger.SetStartPose, "set_start_pose", _robot.SetStartPoseAsync);

    /// <summary>选终点：同起点。</summary>
    [RelayCommand]
    private Task SetEndPose() => SendThenFire(ScanTrigger.SetEndPose, "set_end_pose", _robot.SetEndPoseAsync);

    [RelayCommand]
    private async Task Pause()
    {
        if (Handle("pause", await _robot.PauseAsync()))
        {
            IsPaused = true;
            RaiseGates();
        }
    }

    [RelayCommand]
    private async Task Resume()
    {
        if (Handle("resume", await _robot.ResumeAsync()))
        {
            IsPaused = false;
            RaiseGates();
        }
    }

    /// <summary>停止：任意阶段可达（安全约束），回到 Idle 并清空流程进度。</summary>
    [RelayCommand]
    private async Task Stop()
    {
        IsPaused = false;
        _fsm.TryFire(ScanTrigger.Stop);
        Handle("stop", await _robot.StopAsync());
        UpdateDerived();
    }

    /// <summary>复位：清空流程，从头再来。</summary>
    [RelayCommand]
    private async Task Reset()
    {
        IsPaused = false;
        _fsm.TryFire(ScanTrigger.Reset);
        Handle("reset", await _robot.ResetAsync());
        UpdateDerived();
    }

    /// <summary>查询各阶段完成情况，据此补发对应事件（校正状态灯）。</summary>
    [RelayCommand]
    private async Task RefreshStatus()
    {
        var pre = await _robot.QueryPreScanDoneAsync();
        if (pre.Success && pre.Result.Length > 0 && pre.Result[0] != 0)
            _fsm.TryFire(ScanTrigger.PreScanDone);

        var motion = await _robot.QueryMotionDoneAsync();
        if (motion.Success && motion.Result.Length > 0 && motion.Result[0] != 0)
            _fsm.TryFire(ScanTrigger.ScanDone);

        UpdateDerived();
        _log.Log($"当前阶段 = {_fsm.Stage}（pre_scan_done={_fsm.PreScanDone}, plan_done={_fsm.Stage is ScanStage.Ready or ScanStage.Executing or ScanStage.Completed}）",
            LogLevel.Info, "query");
    }

    // ── 内部 ──

    /// <summary>先出发状态机（乐观），再下发命令；命令失败 → 判故障。</summary>
    private async Task FireThenSend(ScanTrigger trigger, string command, System.Func<Task<CommandResult>> send)
    {
        if (!_fsm.TryFire(trigger))
        {
            _log.Log($"{command} 被状态机拒绝（当前 {_fsm.Stage}）", LogLevel.Warn, command);
            return;
        }

        if (!Handle(command, await send()))
            _fsm.TryFire(ScanTrigger.Fail);

        UpdateDerived();
    }

    /// <summary>短指令：先下发命令，回执成功才出发状态机（失败则不记录）。</summary>
    private async Task SendThenFire(ScanTrigger trigger, string command, System.Func<Task<CommandResult>> send)
    {
        if (!Handle(command, await send()))
            return;

        _fsm.TryFire(trigger);
        UpdateDerived();
    }

    /// <summary>
    /// 后端异步事件：长任务的完成 / 失败都在这里落地。
    /// 事件在后台线程到达，但这里只改 VM 属性（Avalonia 属性绑定会自行 marshal）。
    /// </summary>
    private void OnEvent(EventNotification evt)
    {
        switch (evt.EventName)
        {
            case "pre_scan_done":
                if (evt.Success) _fsm.TryFire(ScanTrigger.PreScanDone);
                break;

            case "plan_done":
                if (evt.Success) _fsm.TryFire(ScanTrigger.PlanDone);
                break;

            case "motion_done":
            case "scan_done":
                if (evt.Success)
                {
                    IsPaused = false;
                    _fsm.TryFire(ScanTrigger.ScanDone);
                }
                break;

            case "error":
                _log.Log($"机器人上报错误: {evt.Message}", LogLevel.Error, "error");
                _fsm.TryFire(ScanTrigger.Fail);
                break;
        }

        UpdateDerived();
    }

    /// <summary>统一处理回执：成功写日志、返回是否成功。</summary>
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

    /// <summary>把状态机阶段 / 门控同步到界面（当前阶段 / 下一步 / 四步状态灯 / 产物摘要）。</summary>
    private void UpdateDerived()
    {
        SyncSteps();

        var active = Steps.FirstOrDefault(s => s.State == StepState.Active);
        CurrentStage = _fsm.Stage switch
        {
            ScanStage.Completed => "④ 扫查完成",
            ScanStage.Faulted => "流程出错",
            ScanStage.Idle => "未开始",
            _ => active is null ? "进行中" : $"{active.Index} {active.Title}",
        };

        NextAction = _fsm.Stage switch
        {
            ScanStage.Idle => "开始预扫查",
            ScanStage.PreScanning => "结束预扫查（建图完成后点击）",
            ScanStage.Posing => HasStartPose
                ? (HasEndPose ? "开始规划" : "记录当前位姿为终点")
                : "记录当前位姿为起点",
            ScanStage.Planning => "等待规划完成（plan_done）",
            ScanStage.Ready => "开始执行",
            ScanStage.Executing => IsPaused ? "已暂停（可恢复）" : "沿路径执行中（可暂停 / 停止）",
            ScanStage.Completed => "扫查已完成，可复位开始新流程",
            _ => "流程出错，请复位",
        };

        // 各步"产物摘要"。
        Steps[0].Detail = _fsm.PreScanDone ? "建图完成" : null;
        Steps[1].Detail = HasStartPose && HasEndPose ? "起点 + 终点" : HasStartPose ? "起点" : null;
        Steps[2].Detail = _fsm.Stage is ScanStage.Ready or ScanStage.Executing or ScanStage.Completed ? "路径已生成" : null;
        Steps[3].Detail = _fsm.Stage == ScanStage.Completed ? "已完成" : IsRunning ? "进行中" : null;

        RaiseGates();
    }

    /// <summary>由状态机阶段推导四步状态灯。</summary>
    private void SyncSteps()
    {
        ScanStage s = _fsm.Stage;

        bool pastPre = s is not (ScanStage.Idle or ScanStage.PreScanning);
        bool pastPose = s is ScanStage.Planning or ScanStage.Ready or ScanStage.Executing or ScanStage.Completed;
        bool pastPlan = s is ScanStage.Ready or ScanStage.Executing or ScanStage.Completed;
        bool pastExec = s == ScanStage.Completed;

        Steps[0].State = s == ScanStage.PreScanning ? StepState.Active
            : pastPre || s == ScanStage.Faulted ? StepState.Done : StepState.Pending;

        Steps[1].State = s == ScanStage.Posing ? StepState.Active
            : pastPose ? StepState.Done : StepState.Pending;

        Steps[2].State = s == ScanStage.Planning ? StepState.Active
            : pastPlan ? StepState.Done : StepState.Pending;

        Steps[3].State = s == ScanStage.Executing ? StepState.Active
            : pastExec ? StepState.Done : StepState.Pending;
    }

    private void RaiseGates()
    {
        OnPropertyChanged(nameof(HasStartPose));
        OnPropertyChanged(nameof(HasEndPose));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(CanStartPreScan));
        OnPropertyChanged(nameof(CanEndPreScan));
        OnPropertyChanged(nameof(CanSetStart));
        OnPropertyChanged(nameof(CanSetEnd));
        OnPropertyChanged(nameof(CanPlan));
        OnPropertyChanged(nameof(CanExecute));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanReset));
        OnPropertyChanged(nameof(CanNextStep));
    }
}
