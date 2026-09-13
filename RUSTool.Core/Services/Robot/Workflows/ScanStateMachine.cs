using System;

namespace RUSTool.Services.Robot.Workflows;

/// <summary>
/// 扫查流程的阶段（严格线性，只能前进或复位）。
///
///   Idle ──► PreScanning ──► Posing ──► Planning ──► Ready ──► Executing ──► Completed
///     ▲         ① 预扫描      ② 位姿      ③ 规划               ④ 执行          │
///     │                                                                         │
///     └──────────── Stop / Reset ◄─────────── Faulted ◄──────── Fail ───────────┘
///
/// 说明：Ready 是「规划已完成、等着按执行」的中间阶段，不是一步业务操作，
///       但它必须存在 —— 否则「规划完成」和「开始执行」之间没有可停留的状态。
/// </summary>
public enum ScanStage
{
    /// <summary>待开始。刚进入流程或已复位。</summary>
    Idle,

    /// <summary>① 预扫描进行中（已下发 pre_scan_start，等待结束）。</summary>
    PreScanning,

    /// <summary>② 标定位姿：正在记录起点 / 终点（两者都记好才能进入规划）。</summary>
    Posing,

    /// <summary>③ 已下发 plan，等待 plan_done 事件。</summary>
    Planning,

    /// <summary>③ 规划完成，可以下发 execute。</summary>
    Ready,

    /// <summary>④ 扫查执行中，等待 motion_done / scan_done 事件。</summary>
    Executing,

    /// <summary>扫查正常完成。可复位后重来。</summary>
    Completed,

    /// <summary>出错（后端上报 error 事件）。只能复位回 Idle，不支持重试。</summary>
    Faulted
}

/// <summary>
/// 触发扫查状态转移的「动作」或「事件」。
/// 前半部分是用户点按钮产生的动作，后半部分是后端异步回报的事件。
/// </summary>
public enum ScanTrigger
{
    // ── 用户动作 ──
    /// <summary>点「开始预扫描」。</summary>
    StartPreScan,
    /// <summary>点「结束预扫描」。</summary>
    EndPreScan,
    /// <summary>点「设为起点」。</summary>
    SetStartPose,
    /// <summary>点「设为终点」。</summary>
    SetEndPose,
    /// <summary>点「开始规划」。</summary>
    StartPlan,
    /// <summary>点「执行」。</summary>
    Execute,
    /// <summary>点「停止」：任何阶段都合法，一律回到 Idle 并清空流程进度。</summary>
    Stop,
    /// <summary>点「复位」：回到 Idle 并清空流程进度（与 Stop 结果相同，仅语义不同）。</summary>
    Reset,

    // ── 后端事件 ──
    /// <summary>收到 pre_scan_done 事件。</summary>
    PreScanDone,
    /// <summary>收到 plan_done 事件。</summary>
    PlanDone,
    /// <summary>收到 motion_done / scan_done 事件。</summary>
    ScanDone,
    /// <summary>收到 error 事件。</summary>
    Fail
}

/// <summary>
/// 扫查流程状态机：只负责「状态怎么变」，不负责「命令怎么发」。
///
/// 设计要点：
/// 1. 本类零依赖 —— 不认识 IRobotService、不认识界面，因此可以脱离网络 / UI 单独测试。
/// 2. 所有状态变化都必须经过 <see cref="TryFire"/> 这一个入口；非法转移会被拒绝并返回 false，
///    调用方据此放弃发送命令（「扫查过程中点手动控制直接拒绝」就是靠这个落地的）。
/// 3. 原来的 5 个 bool（PreScanDone / StartPoseSet / EndPoseSet / PlanDone / ScanDone）
///    共有 32 种组合、其中 27 种非法；压缩成 8 个阶段后，非法状态在类型上就无法表示。
/// </summary>
public sealed class ScanStateMachine
{
    /// <summary>当前阶段。</summary>
    public ScanStage Stage { get; private set; } = ScanStage.Idle;

    /// <summary>起点是否已记录（Posing 阶段内允许只记录其中一个）。</summary>
    public bool StartPoseSet { get; private set; }

    /// <summary>终点是否已记录。</summary>
    public bool EndPoseSet { get; private set; }

    /// <summary>
    /// 预扫描是否已完成。由 <see cref="Stage"/> 推导，不单独存字段 ——
    /// 只要离开 Idle / PreScanning 就说明预扫描这步已经过了
    /// （Faulted 也算 true，因为故障可能发生在更后面的阶段）。
    /// </summary>
    public bool PreScanDone => Stage is not (ScanStage.Idle or ScanStage.PreScanning);

    /// <summary>起点和终点都已记录，可以开始规划。</summary>
    public bool PoseReady => StartPoseSet && EndPoseSet;

    /// <summary>阶段发生变化时触发，参数是新阶段（旧阶段由调用方自己记）。</summary>
    public event Action<ScanStage>? StageChanged;

    /// <summary>
    /// 判断当前阶段是否允许该动作 —— 供界面按钮置灰用，不改变任何状态。
    /// 与 <see cref="TryFire"/> 走同一套判定，因此「按钮灰不灰」和「点了能不能执行」永远一致。
    /// </summary>
    public bool CanFire(ScanTrigger trigger)
        => GateOpen(trigger) && (IsWildcard(trigger) || NextOf(Stage, trigger) is not null);

    /// <summary>
    /// 尝试触发一次转移。
    /// </summary>
    /// <returns>
    /// true  = 转移成功（含自转移，例如同一个事件重复到达）；
    /// false = 当前阶段不允许该动作，状态保持不变，调用方应放弃后续命令。
    /// </returns>
    public bool TryFire(ScanTrigger trigger)
    {
        // ── 通配动作：任何阶段都合法，不查表 ──
        if (trigger is ScanTrigger.Stop or ScanTrigger.Reset)
        {
            GoIdle();
            return true;
        }

        if (trigger == ScanTrigger.Fail)
        {
            MoveTo(ScanStage.Faulted);
            return true;
        }

        // ── 子条件门禁：光看「当前阶段」还不够的前置条件 ──
        if (!GateOpen(trigger))
            return false;

        // ── 精确转移：查表，查不到就是非法动作 ──
        var next = NextOf(Stage, trigger);
        if (next is null)
            return false;

        // 子条件：起点 / 终点是「可分别记录的两件小事」，没法只靠阶段表达，故单独标记
        if (trigger == ScanTrigger.SetStartPose)
            StartPoseSet = true;
        if (trigger == ScanTrigger.SetEndPose)
            EndPoseSet = true;

        MoveTo(next.Value);
        return true;
    }

    /// <summary>
    /// 转移表。读法：(当前阶段, 动作) =&gt; 新阶段；查不到（返回 null）即非法。
    /// 表里显式写出的「自转移」（如 Posing + SetStartPose =&gt; Posing）表示该信号
    /// 重复到达也无害 —— 因为命令回执和异步事件谁先到并不确定。
    /// </summary>
    private static ScanStage? NextOf(ScanStage stage, ScanTrigger trigger) => (stage, trigger) switch
    {
        // ① 预扫描：EndPreScan（命令回执）与 PreScanDone（异步事件）谁先到都算这步完成
        (ScanStage.Idle,        ScanTrigger.StartPreScan) => ScanStage.PreScanning,
        (ScanStage.PreScanning, ScanTrigger.EndPreScan)   => ScanStage.Posing,
        (ScanStage.PreScanning, ScanTrigger.PreScanDone)  => ScanStage.Posing,
        (ScanStage.Posing,      ScanTrigger.EndPreScan)   => ScanStage.Posing,   // 迟到的回执，无害
        (ScanStage.Posing,      ScanTrigger.PreScanDone)  => ScanStage.Posing,   // 迟到的事件，无害

        // ② 标定位姿：起点 / 终点各自可重复记录，阶段不变
        (ScanStage.Posing,      ScanTrigger.SetStartPose) => ScanStage.Posing,
        (ScanStage.Posing,      ScanTrigger.SetEndPose)   => ScanStage.Posing,

        // ③ 规划：完成后进入 Ready，等用户按「执行」
        (ScanStage.Posing,      ScanTrigger.StartPlan)    => ScanStage.Planning,
        (ScanStage.Planning,    ScanTrigger.PlanDone)     => ScanStage.Ready,
        (ScanStage.Ready,       ScanTrigger.PlanDone)     => ScanStage.Ready,    // 重复事件，无害

        // ④ 执行
        (ScanStage.Ready,       ScanTrigger.Execute)      => ScanStage.Executing,
        (ScanStage.Executing,   ScanTrigger.ScanDone)     => ScanStage.Completed,
        (ScanStage.Completed,   ScanTrigger.ScanDone)     => ScanStage.Completed, // 重复事件，无害

        // 其余组合一律非法（例如「没规划就执行」「扫查中又开始预扫描」）
        _ => null
    };

    /// <summary>Stop / Reset / Fail 三个动作在任何阶段都合法。</summary>
    private static bool IsWildcard(ScanTrigger trigger)
        => trigger is ScanTrigger.Stop or ScanTrigger.Reset or ScanTrigger.Fail;

    /// <summary>
    /// 子条件门禁：除了「当前阶段」，还需要额外检查的前置条件。
    /// 目前只有一条 —— 起点和终点都记录好之后，才允许开始规划。
    /// </summary>
    private bool GateOpen(ScanTrigger trigger)
    {
        if (trigger == ScanTrigger.StartPlan && !PoseReady)
            return false;

        return true;
    }

    /// <summary>回到 Idle 并清空流程进度。Stop 与 Reset 的结果相同。</summary>
    private void GoIdle()
    {
        StartPoseSet = false;
        EndPoseSet = false;
        MoveTo(ScanStage.Idle);
    }

    /// <summary>切换阶段；自转移不重复触发事件（避免界面白刷新）。</summary>
    private void MoveTo(ScanStage next)
    {
        if (next == Stage)
            return;

        Stage = next;
        StageChanged?.Invoke(next);
    }
}
