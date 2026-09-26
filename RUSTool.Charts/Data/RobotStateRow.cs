using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RUSTool.Charts.Data;

/// <summary>
/// 一行曲线：<b>一路数据一行</b> —— 这一行画哪一路在构造时就定下来了（见 <see cref="ChannelIndex"/>）。
///
/// <para>
/// <b>为什么自己存历史：</b>曲线要看的是「刚才这一段」，而推帧的人只描述「此刻」。
/// 历史就是 <see cref="Values"/> 这一条队列：每来一帧加尾、超出窗口就删头（见 <see cref="Push"/>）——
/// 长满一屏之后曲线开始往左滚，而不是每次重画都缩放成「刚好填满」。
/// </para>
/// <para>
/// <b>为什么 <see cref="Values"/> 是 ObservableCollection 而且从不整体换掉：</b>
/// 图表订阅的是<b>集合实例</b>的变更通知。换一个新实例等于让它重挂一次监听 ——
/// 滚动更新时每帧换一次既浪费又容易漏通知。所以更新一律「删头 + 加尾」（<see cref="Push"/>），
/// 集合实例始终不变。
/// </para>
/// <para>
/// <b>线程契约：</b>改的是绑定源集合，所有方法都<b>必须在 UI 线程调用</b>。
/// 本层不替调用方 marshal —— 推帧的那一层（<c>/state</c> 的订阅者）本来就知道自己在哪个线程上。
/// </para>
/// </summary>
public sealed partial class RobotStateRow : ObservableObject
{
    /// <summary>
    /// 默认滚动窗口长度（帧）。按 <c>/state</c> 约 20 Hz 折算 → 一屏约 15 秒历史：
    /// 够看出一整段动作的形状（起停 / 换向 / 平台段），又不至于把 20 Hz 的点挤成一条实心带。
    ///
    /// <para>
    /// <b>这里只认帧数，不认秒：</b>一屏等于几秒取决于上游推帧有多快 —— 「15 秒」是界面层按标称帧率
    /// 折算出来给人看的（见 <c>TorqueChartViewModel.WindowCaption</c>），横轴画的一直是帧序号。
    /// </para>
    /// </summary>
    public const int DefaultWindowFrames = 300;

    /// <summary>
    /// 建一行：画第 <paramref name="channelIndex"/> 路数据。
    /// </summary>
    /// <param name="channelIndex">这一行画的是第几路（下标与推帧的数组对齐；同时决定线色与行头色标）。</param>
    /// <param name="channel">这一路的名字与单位（行头标签与读数后缀都取自它）。</param>
    /// <param name="windowFrames">滚动窗口长度（帧）。</param>
    public RobotStateRow(int channelIndex, StateChannel channel, int windowFrames = DefaultWindowFrames)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(channelIndex);
        ArgumentNullException.ThrowIfNull(channel);
        if (windowFrames <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowFrames), windowFrames, "窗口长度必须为正。");

        ChannelIndex = channelIndex;
        Channel = channel;
        WindowFrames = windowFrames;
    }

    /// <summary>这一行画的是第几路数据（下标与 <see cref="Push"/> 收到的数组对齐）。</summary>
    public int ChannelIndex { get; }

    /// <summary>这一路的通道（名字 + 单位）：行头标签与读数后缀都取自它。</summary>
    public StateChannel Channel { get; }

    /// <summary>滚动窗口长度（帧）；也是曲线的 X 轴整段长度（见 <c>StateRowChart</c>）。</summary>
    public int WindowFrames { get; }

    /// <summary>
    /// 曲线上的点（最近 <see cref="WindowFrames"/> 帧，旧 → 新）—— 同时也是这一行的历史。
    /// 视图把它直接交给图表，自己不再复制一份 —— 更新就地做（见类注释）。
    /// </summary>
    public ObservableCollection<double> Values { get; } = [];

    /// <summary>最新一帧的读数（显示在行头）。</summary>
    [ObservableProperty] private double _current;

    /// <summary>行头读数：数值 + 单位。固定两位小数，读数跳动时不会左右晃。</summary>
    public string CurrentText => Channel.Format(Current);

    partial void OnCurrentChanged(double value) => OnPropertyChanged(nameof(CurrentText));

    /// <summary>
    /// 推一帧：取这一路的分量，加一个点到 <see cref="Values"/> 尾部、超出窗口就删掉头上那个。
    /// <b>必须在 UI 线程调用</b>。
    /// </summary>
    /// <param name="frame">
    /// 一帧数据，下标与通道目录对齐；分量不足时按 0 计
    /// （帧字段在协议演进中可能先到一半）。
    /// </param>
    public void Push(IReadOnlyList<double> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var value = ChannelIndex < frame.Count ? frame[ChannelIndex] : 0;

        Current = value;
        Values.Add(value);
        while (Values.Count > WindowFrames)
            Values.RemoveAt(0);
    }

    /// <summary>
    /// 清空这一行（历史与当前读数）：曲线回到「一条都没有」的状态 ——
    /// 例如重新连接后端时，旧的形状不该还挂在那里冒充新数据。
    /// </summary>
    public void Clear()
    {
        Values.Clear();
        Current = 0;
    }
}
