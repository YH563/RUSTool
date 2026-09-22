using RUSTool.Communication;
using System;

namespace RUSTool.UI.Services;

/// <summary>
/// 合成一帧 <c>/sensor</c> 点云 —— 只给截图模式用（<c>--demo-cloud</c>）。
///
/// <para>
/// 它按协议的线格式【真的拼一条帧、再用生产的解码器解回来】（<see cref="SensorFrameCodec.EncodeRaw"/> →
/// <see cref="SensorFrameCodec.TryDecode"/>），所以截图里看到的点云走的是与真实链路同一条
/// 「解码 → 投递 → 场景图层」的路，只跳过 WebSocket 传输本身。没有后端可连的环境里，
/// 这是唯一能证明这条链路通着的办法（<c>preview.sh cloud</c>）。
/// </para>
/// <para>
/// 形状是一段「体表」：绕基座的椭圆柱面，<c>z</c> 从 0.12 m 到 0.55 m，颜色按高度渐变 ——
/// 落在整机取景范围内，谁看一眼都知道那不是网格、也不是机械臂。
/// 单元测试不走这条路径（那边用测试自己的帧构造器）。
/// </para>
/// </summary>
internal static class DemoSensorFrame
{
    /// <summary>点数（够把曲面看成一个面，又不至于拖慢截图）。</summary>
    private const int Points = 60000;

    /// <summary>体表范围（米，base_link 坐标系）。</summary>
    private const float HalfWidth = 0.20f;
    private const float HalfDepth = 0.13f;
    private const float LowZ = 0.12f;
    private const float HighZ = 0.55f;

    /// <summary>造一帧并解回解码后的形状（与真实链路同一个出口）。</summary>
    /// <param name="seq">帧序号（与协议一样，只用于诊断）。</param>
    internal static SensorPointCloudFrame Build(uint seq = 1024)
    {
        var xyz = new float[Points * 3];
        var rgb = new uint[Points];
        var random = new Random(20260922);

        for (int i = 0; i < Points; i++)
        {
            double angle = random.NextDouble() * Math.PI * 2.0;
            float height = LowZ + (float)random.NextDouble() * (HighZ - LowZ);

            // 椭圆柱面（腹部轮廓的量级）+ 一点噪声，免得看起来像贴图。
            var jitter = (float)(random.NextDouble() - 0.5) * 0.006f;
            xyz[i * 3] = HalfWidth * (float)Math.Cos(angle) + jitter;
            xyz[i * 3 + 1] = HalfDepth * (float)Math.Sin(angle) + jitter;
            xyz[i * 3 + 2] = height + jitter;

            rgb[i] = Ramp((height - LowZ) / (HighZ - LowZ));
        }

        SensorPointCloudFrame? frame = SensorFrameCodec.TryDecode(
            SensorFrameCodec.EncodeRaw(xyz, rgb, Points, seq: seq, scope: SensorScopes.Frame),
            out string? error);

        // 合成帧解不开就是本文件与协议的实现不一致 —— 这是程序员错误，直接炸掉（截图脚本要立刻发现）。
        return frame ?? throw new InvalidOperationException($"合成点云帧解码失败：{error}");
    }

    /// <summary>高度 0..1 → 颜色渐变（深蓝 → 青 → 暖黄），打包成 0x00RRGGBB。</summary>
    private static uint Ramp(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        byte red = (byte)(40 + 215 * t);
        byte green = (byte)(110 + 130 * t);
        byte blue = (byte)(200 - 150 * t);
        return (uint)((red << 16) | (green << 8) | blue);
    }
}
