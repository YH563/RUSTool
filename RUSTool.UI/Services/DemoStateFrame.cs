using RUSTool.Communication;
using System;

namespace RUSTool.UI.Services;

/// <summary>
/// 合成一帧 <c>/state</c> 状态帧 —— 只给截图模式用（<c>--demo-torque</c>）。
///
/// <para>
/// 与 <see cref="DemoSensorFrame"/> 同一个套路：按协议的线格式【真的拼一条帧、再用生产的
/// 解码器解回来】（<see cref="BridgeProtocol.Encode(BridgeProtocol.StateFrame)"/> →
/// <see cref="BridgeProtocol.TryParseState"/>），所以截图里的曲线走的是与真实链路同一条
/// 「解码 → 状态帧 → 滚动窗口 → 曲线」的路，只跳过 WebSocket 传输本身。
/// 反过来做（直接 <c>new StateFrame</c>）看着更简单，但那样就成了「自己造数据给自己看」，
/// 字段名对不上也不会有人告诉你。
/// </para>
/// <para>
/// 形状：每个关节一段「幅值、频率都不同」的正弦 + 一点确定性抖动。
/// 用正弦是因为随机游走看起来像信号坏了；六条曲线各有各的样子，
/// 六格并排时不会被误读成「同一张图复制了六份」。
/// </para>
/// <para>单元测试不走这条路径（那边自己造帧）。</para>
/// </summary>
internal static class DemoStateFrame
{
    /// <summary>状态流标称帧率（Hz），与 <c>/state</c> 的量级一致。</summary>
    private const double Hz = 20.0;

    /// <summary>造第 <paramref name="frame"/> 帧（帧号 → 时间戳，与真机按帧率推进一样）。</summary>
    /// <param name="frame">帧号（0 基）。</param>
    internal static BridgeProtocol.StateFrame Build(int frame)
    {
        var time = frame / Hz;

        // ── 关节力矩（N·m）：曲线画的就是它 ──
        var effort = new double[6];
        for (var joint = 0; joint < effort.Length; joint++)
        {
            // 幅值随关节号递减（12 → 约 3.4 N·m）：越靠腕部力矩越小，与真实机械臂一致；
            // 频率错开一点，六条曲线才不会整齐划一。
            var amplitude = 12.0 / (1 + joint * 0.55);
            var phase = joint * 0.7;

            // 确定性抖动：由帧号算出来，不用 Random —— 同一张截图可复现
            //（与 DemoSensorFrame 固定种子是同一个理由）。
            var jitter = Math.Sin(frame * 12.9898 + joint * 78.233) * 0.35;

            effort[joint] = amplitude * Math.Sin(time * (0.9 + joint * 0.22) + phase) + jitter;
        }

        // ── 关节角（rad）：HUD 读数与 3D 姿态用得到，顺手给一段慢摆 ──
        var jointPos = new double[6];
        for (var joint = 0; joint < jointPos.Length; joint++)
            jointPos[joint] = 0.35 * Math.Sin(time * (0.4 + joint * 0.1) + joint * 0.5);

        // ── 法兰位姿（位置 m / 姿态 rad）：抬到工作高度、探头朝下 ──
        var flangePos = new double[6];
        flangePos[0] = 0.10;
        flangePos[2] = 0.30 + 0.02 * Math.Sin(time * 0.8);
        flangePos[4] = -1.20 + 0.05 * Math.Sin(time * 0.6);

        var state = new BridgeProtocol.StateFrame(
            Timestamp: time,
            FrameRate: Hz,
            JointPos: jointPos,
            JointVel: new double[6],
            JointAcc: new double[6],
            Effort: effort,
            FlangePos: flangePos);

        // 合成帧解不开就是本文件与协议的定义不一致 —— 这是程序员错误，直接炸掉（截图脚本要立刻发现）。
        var decoded = BridgeProtocol.TryParseState(BridgeProtocol.Encode(state));
        return decoded ?? throw new InvalidOperationException("合成状态帧解码失败（线格式与 BridgeProtocol 不一致）");
    }
}
