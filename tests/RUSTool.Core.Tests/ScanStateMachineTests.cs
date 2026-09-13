using System;
using System.Collections.Generic;
using RUSTool.Services.Robot.Workflows;
using Xunit;

namespace RUSTool.Core.Tests;

/// <summary>
/// ScanStateMachine 的单元测试。
///
/// 整个文件不需要网络、界面或机械臂，几毫秒就能跑完 —— 这正是把状态机单独拆成一个文件的价值。
///
/// 测试方法的命名规则：<b>「什么情况」_「结果应该是什么」</b>，看不懂的用例可以直接看方法名。
/// 每个用例都是三步：
///     Arrange  用 <see cref="Arrange"/> 把状态机推到某个阶段
///     Act      触发一个动作（TryFire）
///     Assert   断言阶段 / 返回值
/// </summary>
public class ScanStateMachineTests
{
    // ════════════════════════════════════════════════════════════════
    //  ① 正常流程
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void 完整流程_从待开始一路走到扫查完成()
    {
        var fsm = new ScanStateMachine();
        var trace = Track(fsm);

        Assert.True(fsm.TryFire(ScanTrigger.StartPreScan));   // ① 开始预扫描
        Assert.True(fsm.TryFire(ScanTrigger.EndPreScan));     // ① 结束预扫描
        Assert.True(fsm.TryFire(ScanTrigger.SetStartPose));   // ② 记录起点
        Assert.True(fsm.TryFire(ScanTrigger.SetEndPose));     // ② 记录终点
        Assert.True(fsm.TryFire(ScanTrigger.StartPlan));      // ③ 开始规划
        Assert.True(fsm.TryFire(ScanTrigger.PlanDone));       // ③ 后端回报规划完成
        Assert.True(fsm.TryFire(ScanTrigger.Execute));        // ④ 执行
        Assert.True(fsm.TryFire(ScanTrigger.ScanDone));       // ④ 后端回报扫查完成

        Assert.Equal(ScanStage.Completed, fsm.Stage);

        // 走过的阶段轨迹（起点 Idle 也在里面）
        Assert.Equal(new[]
        {
            ScanStage.Idle,
            ScanStage.PreScanning,
            ScanStage.Posing,      // 记录起点 / 终点都是自转移，所以只出现一次
            ScanStage.Planning,
            ScanStage.Ready,
            ScanStage.Executing,
            ScanStage.Completed,
        }, trace);
    }

    [Fact]
    public void 预扫描结束_回执和异步事件谁先到都能进入位姿阶段()
    {
        // 情况一：命令回执先到
        var byReceipt = new ScanStateMachine();
        byReceipt.TryFire(ScanTrigger.StartPreScan);
        Assert.True(byReceipt.TryFire(ScanTrigger.EndPreScan));
        Assert.Equal(ScanStage.Posing, byReceipt.Stage);

        // 情况二：异步事件先到
        var byEvent = new ScanStateMachine();
        byEvent.TryFire(ScanTrigger.StartPreScan);
        Assert.True(byEvent.TryFire(ScanTrigger.PreScanDone));
        Assert.Equal(ScanStage.Posing, byEvent.Stage);

        // 情况三：两个都到，后来的那个是迟到的，应当无害
        Assert.True(byReceipt.TryFire(ScanTrigger.PreScanDone));
        Assert.Equal(ScanStage.Posing, byReceipt.Stage);
        Assert.True(byEvent.TryFire(ScanTrigger.EndPreScan));
        Assert.Equal(ScanStage.Posing, byEvent.Stage);
    }

    // ════════════════════════════════════════════════════════════════
    //  ② 非法动作被拒绝 —— 阶段保持不变，调用方据此放弃发命令
    // ════════════════════════════════════════════════════════════════

    [Theory]
    // 还没预扫描：不能标位姿、不能规划、不能执行
    [InlineData(ScanStage.Idle, ScanTrigger.SetStartPose)]
    [InlineData(ScanStage.Idle, ScanTrigger.SetEndPose)]
    [InlineData(ScanStage.Idle, ScanTrigger.StartPlan)]
    [InlineData(ScanStage.Idle, ScanTrigger.Execute)]
    [InlineData(ScanStage.Idle, ScanTrigger.ScanDone)]
    // 预扫描中：不能重复开始，也不能跳到后面
    [InlineData(ScanStage.PreScanning, ScanTrigger.StartPreScan)]
    [InlineData(ScanStage.PreScanning, ScanTrigger.Execute)]
    // 位姿阶段：还没规划，不能执行
    [InlineData(ScanStage.Posing, ScanTrigger.Execute)]
    // 规划中：要等 plan_done，不能抢跑
    [InlineData(ScanStage.Planning, ScanTrigger.Execute)]
    [InlineData(ScanStage.Planning, ScanTrigger.StartPlan)]
    [InlineData(ScanStage.Planning, ScanTrigger.StartPreScan)]
    // 扫查执行中：不能从头再来
    [InlineData(ScanStage.Executing, ScanTrigger.StartPreScan)]
    [InlineData(ScanStage.Executing, ScanTrigger.EndPreScan)]
    [InlineData(ScanStage.Executing, ScanTrigger.StartPlan)]
    [InlineData(ScanStage.Executing, ScanTrigger.Execute)]
    // 已完成：要重来得先「停止」或「复位」
    [InlineData(ScanStage.Completed, ScanTrigger.StartPreScan)]
    [InlineData(ScanStage.Completed, ScanTrigger.Execute)]
    public void 非法动作被拒绝_阶段保持不变(ScanStage stage, ScanTrigger trigger)
    {
        var fsm = Arrange(stage);

        Assert.False(fsm.TryFire(trigger));

        Assert.Equal(stage, fsm.Stage);
    }

    // ════════════════════════════════════════════════════════════════
    //  ③ 停止 / 复位 / 出错：在任何阶段都合法
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(ScanStage.Idle)]
    [InlineData(ScanStage.PreScanning)]
    [InlineData(ScanStage.Posing)]
    [InlineData(ScanStage.Planning)]
    [InlineData(ScanStage.Ready)]
    [InlineData(ScanStage.Executing)]
    [InlineData(ScanStage.Completed)]
    [InlineData(ScanStage.Faulted)]
    public void 任何阶段收到停止_都回到待开始并清空进度(ScanStage stage)
    {
        var fsm = Arrange(stage);

        Assert.True(fsm.TryFire(ScanTrigger.Stop));

        Assert.Equal(ScanStage.Idle, fsm.Stage);
        Assert.False(fsm.StartPoseSet);
        Assert.False(fsm.EndPoseSet);
    }

    [Theory]
    [InlineData(ScanStage.Idle)]
    [InlineData(ScanStage.PreScanning)]
    [InlineData(ScanStage.Posing)]
    [InlineData(ScanStage.Planning)]
    [InlineData(ScanStage.Ready)]
    [InlineData(ScanStage.Executing)]
    [InlineData(ScanStage.Completed)]
    [InlineData(ScanStage.Faulted)]
    public void 任何阶段收到复位_都回到待开始并清空进度(ScanStage stage)
    {
        var fsm = Arrange(stage);

        Assert.True(fsm.TryFire(ScanTrigger.Reset));

        Assert.Equal(ScanStage.Idle, fsm.Stage);
        Assert.False(fsm.StartPoseSet);
        Assert.False(fsm.EndPoseSet);
    }

    [Theory]
    [InlineData(ScanStage.Idle)]
    [InlineData(ScanStage.PreScanning)]
    [InlineData(ScanStage.Posing)]
    [InlineData(ScanStage.Planning)]
    [InlineData(ScanStage.Ready)]
    [InlineData(ScanStage.Executing)]
    [InlineData(ScanStage.Faulted)]
    public void 任何阶段收到出错_都进入故障阶段(ScanStage stage)
    {
        var fsm = Arrange(stage);

        Assert.True(fsm.TryFire(ScanTrigger.Fail));

        Assert.Equal(ScanStage.Faulted, fsm.Stage);
    }

    [Fact]
    public void 出错后_只有复位能离开故障阶段_不做重试()
    {
        var fsm = Arrange(ScanStage.Faulted);

        // 用户明确要求：故障后不支持「重试」，必须先复位回待开始
        Assert.False(fsm.TryFire(ScanTrigger.StartPreScan));
        Assert.False(fsm.TryFire(ScanTrigger.EndPreScan));
        Assert.False(fsm.TryFire(ScanTrigger.SetStartPose));
        Assert.False(fsm.TryFire(ScanTrigger.SetEndPose));
        Assert.False(fsm.TryFire(ScanTrigger.StartPlan));
        Assert.False(fsm.TryFire(ScanTrigger.PlanDone));
        Assert.False(fsm.TryFire(ScanTrigger.Execute));
        Assert.False(fsm.TryFire(ScanTrigger.ScanDone));
        Assert.Equal(ScanStage.Faulted, fsm.Stage);

        // 只有复位能出去
        Assert.True(fsm.TryFire(ScanTrigger.Reset));
        Assert.Equal(ScanStage.Idle, fsm.Stage);
    }

    // ════════════════════════════════════════════════════════════════
    //  ④ 位姿子条件：起点 / 终点能分别记录，所以要单独守一道门
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void 起点和终点都记录好之后才允许开始规划()
    {
        var fsm = Arrange(ScanStage.Posing);

        Assert.False(fsm.PoseReady);
        Assert.False(fsm.TryFire(ScanTrigger.StartPlan));   // 一个都没记 → 拒绝
        Assert.Equal(ScanStage.Posing, fsm.Stage);

        fsm.TryFire(ScanTrigger.SetStartPose);
        Assert.False(fsm.PoseReady);
        Assert.False(fsm.TryFire(ScanTrigger.StartPlan));   // 只记了起点 → 仍然拒绝

        fsm.TryFire(ScanTrigger.SetEndPose);
        Assert.True(fsm.PoseReady);
        Assert.True(fsm.TryFire(ScanTrigger.StartPlan));    // 都记好了 → 放行
        Assert.Equal(ScanStage.Planning, fsm.Stage);
    }

    [Fact]
    public void 预扫描完成标志由当前阶段推导()
    {
        Assert.False(Arrange(ScanStage.Idle).PreScanDone);
        Assert.False(Arrange(ScanStage.PreScanning).PreScanDone);
        Assert.True(Arrange(ScanStage.Posing).PreScanDone);
        Assert.True(Arrange(ScanStage.Planning).PreScanDone);
        Assert.True(Arrange(ScanStage.Completed).PreScanDone);
    }

    // ════════════════════════════════════════════════════════════════
    //  ⑤ 阶段变化事件
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void 只有阶段真的变了才触发事件_自转移不触发()
    {
        var fsm = new ScanStateMachine();

        var changes = new List<ScanStage>();
        fsm.StageChanged += changes.Add;

        fsm.TryFire(ScanTrigger.StartPreScan);   // Idle → PreScanning ：应触发
        fsm.TryFire(ScanTrigger.EndPreScan);     // PreScanning → Posing：应触发
        fsm.TryFire(ScanTrigger.PreScanDone);    // Posing → Posing：自转移，不触发
        fsm.TryFire(ScanTrigger.SetStartPose);   // Posing → Posing：自转移，不触发
        fsm.TryFire(ScanTrigger.Stop);           // Posing → Idle：应触发

        Assert.Equal(new[] { ScanStage.PreScanning, ScanStage.Posing, ScanStage.Idle }, changes);
    }

    // ════════════════════════════════════════════════════════════════
    //  ⑥ 界面按钮置灰用 CanFire，必须和实际能不能触发完全一致
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(ScanStage.Idle)]
    [InlineData(ScanStage.PreScanning)]
    [InlineData(ScanStage.Posing)]
    [InlineData(ScanStage.Planning)]
    [InlineData(ScanStage.Ready)]
    [InlineData(ScanStage.Executing)]
    [InlineData(ScanStage.Completed)]
    [InlineData(ScanStage.Faulted)]
    public void 按钮置灰的判断与实际能否触发完全一致(ScanStage stage)
    {
        foreach (var trigger in Enum.GetValues<ScanTrigger>())
        {
            // 每次都重新准备一个干净的状态机，避免上一次 TryFire 改变状态影响下一次
            var canFire = Arrange(stage).CanFire(trigger);
            var fired = Arrange(stage).TryFire(trigger);

            Assert.Equal(canFire, fired);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  测试辅助
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// 把状态机推到指定阶段，供其它用例做前置准备。
    /// 结尾的断言保证「准备动作」本身是对的，否则后续断言就失去意义了。
    /// </summary>
    private static ScanStateMachine Arrange(ScanStage target)
    {
        var fsm = new ScanStateMachine();

        // ① 预扫描：除了 Idle 和 Faulted，其它阶段都已经过了这一步
        if (target is ScanStage.PreScanning or ScanStage.Posing or ScanStage.Planning
                    or ScanStage.Ready or ScanStage.Executing or ScanStage.Completed)
            fsm.TryFire(ScanTrigger.StartPreScan);

        // ② 位姿
        if (target is ScanStage.Posing or ScanStage.Planning
                    or ScanStage.Ready or ScanStage.Executing or ScanStage.Completed)
            fsm.TryFire(ScanTrigger.EndPreScan);

        // ③ 规划（要先记录起点和终点，否则会被门禁拦下）
        if (target is ScanStage.Planning or ScanStage.Ready
                    or ScanStage.Executing or ScanStage.Completed)
        {
            fsm.TryFire(ScanTrigger.SetStartPose);
            fsm.TryFire(ScanTrigger.SetEndPose);
            fsm.TryFire(ScanTrigger.StartPlan);
        }

        if (target is ScanStage.Ready or ScanStage.Executing or ScanStage.Completed)
            fsm.TryFire(ScanTrigger.PlanDone);

        // ④ 执行
        if (target is ScanStage.Executing or ScanStage.Completed)
            fsm.TryFire(ScanTrigger.Execute);

        if (target == ScanStage.Completed)
            fsm.TryFire(ScanTrigger.ScanDone);

        if (target == ScanStage.Faulted)
            fsm.TryFire(ScanTrigger.Fail);

        Assert.Equal(target, fsm.Stage);
        return fsm;
    }

    /// <summary>
    /// 记录状态机走过的每一个阶段（列表第一个元素是起始阶段）。
    /// </summary>
    private static List<ScanStage> Track(ScanStateMachine fsm)
    {
        var trace = new List<ScanStage> { fsm.Stage };
        fsm.StageChanged += trace.Add;
        return trace;
    }
}
