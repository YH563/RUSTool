using System.Collections.Generic;

namespace RUSTool.Charts.Data;

/// <summary>
/// 机械臂的<b>通道目录</b>：一帧状态里能画的每一路量，写成「名字 + 单位」。
///
/// <para>
/// <b>为什么目录在图表库里、而不是界面层：</b>
/// 它决定曲线上标什么、每一行行头写什么 —— 这是「怎么画」的一部分，跟着控件走。
/// 界面层只负责把一帧里的数组取出来交给控件（见 <c>RobotStatePanel.PushFrame</c>），
/// 本层不认识 <c>BridgeProtocol.StateFrame</c>，只认识 <c>IReadOnlyList&lt;double&gt;</c>。
/// </para>
/// <para>
/// <b>目录顺序 = 数据数组的分量顺序 = 行序</b>（下标对齐）：<c>Torques()[2]</c> 说的是
/// 「第 3 路关节力矩」，调用方就该把状态帧里第 3 个 <c>effort</c> 分量放到同一个下标上；
/// 建出来的第 3 行画的也是它（见 <c>RobotStateRows.Create</c>）。
/// </para>
/// </summary>
public static class RobotArmChannels
{
    /// <summary>关节数（与状态帧 <c>joints</c> / <c>effort</c> 的分量个数对齐）。</summary>
    public const int JointCount = 6;

    /// <summary>
    /// 六路关节力矩（N·m）—— 对应状态帧的 <c>effort</c>。
    /// 这是「数据曲线」面板的默认目录。
    /// </summary>
    public static IReadOnlyList<StateChannel> Torques() => Joints("N·m");

    /// <summary>
    /// 六路关节角（度）—— 对应状态帧的 <c>joints</c>。
    ///
    /// <para>
    /// ⚠ 单位写的是<b>度</b>：协议里弧度，弧度→度的换算由界面层在做（本库不换算，见
    /// <see cref="StateChannel"/> 的注释）。把这份目录交给控件前，先确认交进来的数组已经是度。
    /// </para>
    /// </summary>
    public static IReadOnlyList<StateChannel> JointAngles() => Joints("°");

    /// <summary>
    /// 末端位姿六分量（X / Y / Z 平移 + Rx / Ry / Rz 姿态）—— 对应状态帧的 <c>pose</c>。
    /// 六个分量的单位不统一，所以逐路写清（这也是 <see cref="StateChannel"/> 把单位放在通道上的原因）。
    /// </summary>
    public static IReadOnlyList<StateChannel> FlangePose() =>
    [
        new StateChannel("X", "m"),
        new StateChannel("Y", "m"),
        new StateChannel("Z", "m"),
        new StateChannel("Rx", "°"),
        new StateChannel("Ry", "°"),
        new StateChannel("Rz", "°"),
    ];

    /// <summary>第 <paramref name="joint"/> 号关节的名字（0 基）。名字只有一个来源，见 <see cref="Joints"/>。</summary>
    public static string JointName(int joint) => $"关节 {joint + 1}";

    /// <summary>六路同构的关节通道（名字按 <see cref="JointName"/> 生成，单位由调用方给）。</summary>
    private static StateChannel[] Joints(string unit)
    {
        var channels = new StateChannel[JointCount];
        for (var joint = 0; joint < channels.Length; joint++)
            channels[joint] = new StateChannel(JointName(joint), unit);
        return channels;
    }
}
