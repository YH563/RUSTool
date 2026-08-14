using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Communication;
using RUSTool.Services.Logging;
using RUSTool.Services.Robot;
using System;
using System.Threading.Tasks;

namespace RUSTool.ViewModels.Scan;

/// <summary>
/// 扫查流程 VM：把任务级指令串成 4 步状态机，用状态灯 + 状态文本驱动界面。
/// 步骤：① 预扫描 → ② 位姿 → ③ 规划 → ④ 执行。
/// 完成标志由查询指令（query_prescan_done / query_motion_done）与异步事件（plan_done 等）共同更新。
/// </summary>
public partial class ScanWorkflowViewModel : ViewModelBase
{
    private readonly IRobotService _robot;
    private readonly ILogService _log;

    // ── 步骤完成标志 ──
    [ObservableProperty] private bool _preScanDone;
    [ObservableProperty] private bool _startPoseSet;
    [ObservableProperty] private bool _endPoseSet;
    [ObservableProperty] private bool _planDone;
    [ObservableProperty] private bool _scanDone;

    /// <summary>工作流状态描述（显示给用户）。</summary>
    [ObservableProperty] private string _workflowStatus = "待开始";

    /// <summary>建议的下一步操作。</summary>
    [ObservableProperty] private string _nextStep = "开始预扫描";

    public ScanWorkflowViewModel(IRobotService robot, ILogService log)
    {
        _robot = robot;
        _log = log;
        _robot.EventReceived += OnEvent;
    }

    /// <summary>指令失败时写入日志（同时 UI 已通过 WorkflowStatus 展示）。</summary>
    private void LogIfFailed(string action, CommandResult r)
    {
        if (!r.Success)
            _log.Log($"{action} 失败: {r.Message}", LogLevel.Error);
    }

    // ── ① 预扫描 ──

    [RelayCommand]
    private async Task StartPreScan()
    {
        var r = await _robot.PreScanStartAsync();
        LogIfFailed("预扫描", r);
        WorkflowStatus = r.Success ? "预扫描进行中" : $"预扫描失败: {r.Message}";
        NextStep = "结束预扫描";
    }

    [RelayCommand]
    private async Task EndPreScan()
    {
        var r = await _robot.PreScanEndAsync();
        LogIfFailed("结束预扫描", r);
        WorkflowStatus = r.Success ? "预扫描结束" : $"预扫描失败: {r.Message}";
        NextStep = "设置扫查位姿";
    }

    // ── ② 位姿 ──

    [RelayCommand]
    private async Task SetStartPose()
    {
        var r = await _robot.SetStartPoseAsync();
        LogIfFailed("记录起点", r);
        StartPoseSet = r.Success;
        WorkflowStatus = r.Success ? "起点已记录" : $"记录起点失败: {r.Message}";
        RefreshNextStep();
    }

    [RelayCommand]
    private async Task SetEndPose()
    {
        var r = await _robot.SetEndPoseAsync();
        LogIfFailed("记录终点", r);
        EndPoseSet = r.Success;
        WorkflowStatus = r.Success ? "终点已记录" : $"记录终点失败: {r.Message}";
        RefreshNextStep();
    }

    // ── ③ 规划 ──

    [RelayCommand]
    private async Task StartPlan()
    {
        var r = await _robot.PlanAsync();
        LogIfFailed("规划", r);
        WorkflowStatus = r.Success ? "规划中…（等待 plan_done）" : $"规划失败: {r.Message}";
        NextStep = "等待规划完成";
    }

    // ── ④ 执行 ──

    [RelayCommand]
    private async Task Execute()
    {
        var r = await _robot.ExecuteAsync();
        LogIfFailed("执行", r);
        WorkflowStatus = r.Success ? "扫查中…" : $"执行失败: {r.Message}";
        NextStep = "等待扫查完成";
    }

    [RelayCommand]
    private async Task Pause()
    {
        var r = await _robot.PauseAsync();
        LogIfFailed("暂停", r);
        WorkflowStatus = r.Success ? "已暂停" : $"暂停失败: {r.Message}";
    }

    [RelayCommand]
    private async Task Resume()
    {
        var r = await _robot.ResumeAsync();
        LogIfFailed("恢复", r);
        WorkflowStatus = r.Success ? "已恢复" : $"恢复失败: {r.Message}";
    }

    [RelayCommand]
    private async Task StopScan()
    {
        var r = await _robot.StopAsync();
        LogIfFailed("停止", r);
        WorkflowStatus = r.Success ? "已停止" : $"停止失败: {r.Message}";
    }

    [RelayCommand]
    private void Reset()
    {
        PreScanDone = false;
        StartPoseSet = false;
        EndPoseSet = false;
        PlanDone = false;
        ScanDone = false;
        WorkflowStatus = "待开始";
        NextStep = "开始预扫描";
        _ = _robot.ResetAsync();
    }

    /// <summary>查询后端状态，刷新各步骤完成标志。</summary>
    [RelayCommand]
    private async Task QueryStatus()
    {
        var pre = await _robot.QueryPreScanDoneAsync();
        if (pre.Success && pre.Result.Length > 0)
            PreScanDone = pre.Result[0] != 0;

        var motion = await _robot.QueryMotionDoneAsync();
        if (motion.Success && motion.Result.Length > 0)
            ScanDone = motion.Result[0] != 0;

        RefreshNextStep();
    }

    // ── 异步事件 ──

    private void OnEvent(EventNotification evt)
    {
        switch (evt.EventName)
        {
            case "pre_scan_done":
                PreScanDone = evt.Success;
                WorkflowStatus = "预扫描完成";
                if (!evt.Success)
                    _log.Log($"预扫描事件失败: {evt.Message}", LogLevel.Error);
                break;
            case "plan_done":
                PlanDone = evt.Success;
                WorkflowStatus = evt.Success ? "规划完成" : "规划失败";
                if (!evt.Success)
                    _log.Log($"规划事件失败: {evt.Message}", LogLevel.Error);
                break;
            case "motion_done":
            case "scan_done":
                ScanDone = evt.Success;
                WorkflowStatus = evt.Success ? "扫查完成" : "扫查失败";
                if (!evt.Success)
                    _log.Log($"扫查事件失败: {evt.Message}", LogLevel.Error);
                break;
            case "error":
                WorkflowStatus = $"错误: {evt.Message}";
                _log.Log($"机器人上报错误: {evt.Message}", LogLevel.Error);
                break;
        }
        RefreshNextStep();
    }

    private void RefreshNextStep()
    {
        NextStep = (!PreScanDone) ? "开始预扫描"
            : (!StartPoseSet || !EndPoseSet) ? "设置扫查位姿"
            : (!PlanDone) ? "开始规划"
            : (!ScanDone) ? "执行扫查"
            : "扫查已完成，可复位后重来";
    }
}
