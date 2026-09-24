using System;
using System.Collections.Generic;
using System.ComponentModel;
using RUSTool.Communication;
using RUSTool.Services.Robot;
using Xunit;

namespace RUSTool.Core.Tests;

/// <summary>
/// 驱动类型（<c>switch_driver</c> / <c>get_driver_type</c>）的单元测试。
///
/// 锁住三件事：
///   ① **编码只有一份** —— 0 = 仿真、1 = 真实，与后端和协议文档逐字一致。
///      枚举值就取后端编码，不做二次映射；前后端各写一套映射迟早会对不上。
///   ② **异常回读值不许猜成真实驱动** —— 只有 1 算真实，其余（含越界值 / NaN）一律按仿真处理：
///      读到坏值就退回"未知"，绝不能因为一个 NaN 就把界面点亮成"真实驱动"。
///   ③ **"未知"是一等状态** —— 未连接 / 掉线时 <see cref="RobotSession.DriverText"/> 必须是
///      "未知"，并且驱动或已知性一变就发出变更通知（否则界面拿着旧值继续显示，
///      这就是这次要修的 bug：界面显示的必须是后端当前状态，不是本地记忆）。
///
/// 整个文件不需要网络 / 界面 / 机械臂：全是纯逻辑，几毫秒跑完。
/// 测试方法与既有测试同一命名规则：<b>「什么情况」_「结果应该是什么」</b>。
/// </summary>
public class DriverTypeTests
{
    // ════════════════════════════════════════════════════════════════
    //  ① 编码：与后端 / 协议文档一致
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void 协议编码_仿真0真实1_与后端一致()
    {
        // 枚举值本身就是后端编码，改这里等于改协议，必须是有意为之
        Assert.Equal(0, (int)RobotDriver.Simulation);
        Assert.Equal(1, (int)RobotDriver.Real);

        // switch_driver 的 args[0]
        Assert.Equal(0d, RobotDriverCodec.ToProtocol(RobotDriver.Simulation));
        Assert.Equal(1d, RobotDriverCodec.ToProtocol(RobotDriver.Real));
    }

    [Theory]
    [InlineData(0d, RobotDriver.Simulation)]
    [InlineData(1d, RobotDriver.Real)]
    [InlineData(2d, RobotDriver.Simulation)]              // 后端回了个没见过的编码
    [InlineData(-1d, RobotDriver.Simulation)]
    [InlineData(0.5d, RobotDriver.Simulation)]
    [InlineData(double.NaN, RobotDriver.Simulation)]
    [InlineData(double.PositiveInfinity, RobotDriver.Simulation)]
    public void 回读编码_只有1算真实_其余一律按仿真(double raw, RobotDriver expected)
        => Assert.Equal(expected, RobotDriverCodec.FromProtocol(raw));

    [Fact]
    public void 编码往返_两种驱动都原样还原()
    {
        foreach (RobotDriver driver in Enum.GetValues<RobotDriver>())
            Assert.Equal(driver, RobotDriverCodec.FromProtocol(RobotDriverCodec.ToProtocol(driver)));
    }

    [Fact]
    public void 指令名_与后端逐字一致()
    {
        // 名字写错不会被编译期发现，只会表现为"命令发出去没人应答"，所以钉在这里
        Assert.Equal("switch_driver", Commands.SwitchDriver);
        Assert.Equal("get_driver_type", Commands.GetDriverType);
    }

    // ════════════════════════════════════════════════════════════════
    //  ② "未知"是一等状态（未连接 / 掉线不显示旧值）
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void 刚构造_驱动类型未知_显示未知()
    {
        var session = new RobotSession();

        Assert.False(session.IsDriverKnown);
        Assert.Equal("未知", session.DriverText);
    }

    [Fact]
    public void 回读成功_显示后端实际驱动()
    {
        var session = new RobotSession { IsDriverKnown = true, Driver = RobotDriver.Real };
        Assert.Equal("真实", session.DriverText);

        session.Driver = RobotDriver.Simulation;
        Assert.Equal("仿真", session.DriverText);
    }

    [Fact]
    public void 掉线_驱动类型回到未知_不再显示旧值()
    {
        var session = new RobotSession { IsDriverKnown = true, Driver = RobotDriver.Real };

        session.IsDriverKnown = false; // 掉线

        Assert.Equal("未知", session.DriverText); // 不能继续说"真实"
    }

    [Fact]
    public void 驱动变化_发出DriverText变更通知()
    {
        var session = new RobotSession { IsDriverKnown = true };
        var notified = Track(session);

        session.Driver = RobotDriver.Real;

        Assert.Contains(nameof(RobotSession.DriverText), notified);
    }

    [Fact]
    public void 已知性变化_发出DriverText变更通知()
    {
        var session = new RobotSession();
        var notified = Track(session);

        session.IsDriverKnown = true; // 回读成功 / 掉线都靠这个开关，必须刷界面

        Assert.Contains(nameof(RobotSession.DriverText), notified);
    }

    /// <summary>挂上监听，返回收到的属性名清单（用来断言通知发没发）。</summary>
    private static List<string?> Track(RobotSession session)
    {
        var notified = new List<string?>();
        ((INotifyPropertyChanged)session).PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        return notified;
    }
}
