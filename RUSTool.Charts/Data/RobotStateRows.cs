using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RUSTool.Charts.Data;

/// <summary>
/// 按通道目录建一组行（一路一行），交给 <c>RobotStatePanel.Rows</c>。
///
/// <para>
/// <b>为什么要一个工厂：</b>「行」是<b>外部拥有</b>的绑定源（控件只渲染它、不替调用方造），
/// 这行代码于是成了每个调用方都要抄一遍的样板；抄错一处（少建一行、窗口长度不一致、
/// 行和下标错位）就会表现为「某一行永远不动」或「读数对不上曲线」。
/// 收敛成一个入口，行为的唯一来源就在这里。
/// </para>
/// <para>
/// <b>行数 = 通道数：</b>一路一行 —— 第 i 行画第 i 路（<c>ChannelIndex = i</c>），
/// 所以预览一对一的「哪一行是哪一路」不用问任何一层：行的顺序就是目录的顺序。
/// </para>
/// <para>
/// <b>必须在创建后交给 UI 线程使用</b>：返回的是绑定源集合（见 <see cref="RobotStateRow"/> 的线程契约）。
/// </para>
/// </summary>
public static class RobotStateRows
{
    /// <summary>
    /// 按目录建一组行：第 i 行画第 i 路（见 <see cref="RobotStateRow"/> 构造函数）。
    /// </summary>
    /// <param name="channels">通道目录（可能是 <c>RobotArmChannels.Torques()</c>）。</param>
    /// <param name="windowFrames">滚动窗口长度（帧）；默认取 <see cref="RobotStateRow.DefaultWindowFrames"/>。</param>
    public static ObservableCollection<RobotStateRow> Create(
        IReadOnlyList<StateChannel> channels,
        int windowFrames = RobotStateRow.DefaultWindowFrames)
    {
        ArgumentNullException.ThrowIfNull(channels);

        var rows = new ObservableCollection<RobotStateRow>();
        for (var slot = 0; slot < channels.Count; slot++)
            rows.Add(new RobotStateRow(slot, channels[slot], windowFrames));

        return rows;
    }

    /// <summary>
    /// 推一帧给一组行（每行取自己当前那一路的分量）。
    ///
    /// <para>
    /// <b>为什么要一个静态入口：</b>「推出去了」的实际含义是「每一行都取到它该取的那一个分量」——
    /// 这句话只该写一次。面板的 <c>PushFrame</c> 与宿主的 ViewModel 都往这里走，
    /// 于是「哪一层推的帧」不会让曲线出现两种行为。
    /// </para>
    /// <para><b>必须在 UI 线程调用</b>（改的是绑定源集合）。</para>
    /// </summary>
    /// <param name="rows">行集合；<c>null</c> 或空集就是无事发生。</param>
    /// <param name="frame">一帧数据，下标与通道目录对齐（分量不够时缺的那些按 0 计）。</param>
    public static void PushFrame(IEnumerable<RobotStateRow>? rows, IReadOnlyList<double> frame)
    {
        if (rows is null)
            return;

        foreach (var row in rows)
            row.Push(frame);
    }

    /// <summary>清空一组行的历史与读数（见 <see cref="RobotStateRow.Clear"/>）。<b>必须在 UI 线程调用</b>。</summary>
    public static void Clear(IEnumerable<RobotStateRow>? rows)
    {
        if (rows is null)
            return;

        foreach (var row in rows)
            row.Clear();
    }
}
