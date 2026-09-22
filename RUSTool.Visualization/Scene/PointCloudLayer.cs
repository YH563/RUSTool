using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Scene;
using System;
using System.Numerics;

namespace RUSTool.Visualization.Scene;

/// <summary>
/// 感知点云图层（<c>/sensor</c> 通道的点云）—— 场景图里那一个点云节点，加上「怎么把一帧灌进去」。
///
/// <para>
/// <b>为什么是「整帧替换」而不是增量</b>：协议的每一帧都是自包含的完整点集
/// （<c>scope=frame</c> 是当前帧、<c>scope=map</c> 是累积地图快照，两者都没有 delta 字段），
/// 所以这里 <c>Clear()</c> + <c>AddPoints()</c> 覆盖写同一份 <see cref="PointCloud2Data"/>。
/// </para>
/// <para>
/// <b>这样省下的是「重建」而不是「上传」</b>：复用同一份数据、容量不变时，渲染器不重建 GPU 缓冲
/// （<c>PointMesh.Sync</c> 只在 <c>Capacity</c> 变了才 <c>AllocateStore</c>），CPU 侧也不重新分配
/// 几个 MB。但上传量是实打实的整帧 —— 整帧替换会让 <c>Dirty</c> 窗口正好覆盖全部点
/// （<c>Clear</c> 把窗口归零、<c>AddPoints</c> 又把它拉到 <c>count</c>），于是每帧上传
/// <c>count</c> 个点的位置 + 颜色（每点 28 字节；30 万点 ≈ 8.4 MB/帧）。
/// 协议不推 delta，这个上传量降不下去；真要滑窗得先有增量帧。
/// </para>
/// <para>
/// <b>线程规矩（RobotSimulation 0.3.0 起）</b>：<see cref="Apply"/> 只能在场景图属主线程上调用 ——
/// 也就是渲染回调那条线程。WS 线程送来的帧必须先过 <c>RobotViewport</c> 的邮箱。
/// </para>
/// <para>
/// <b>MaxPoints 保持 0</b>：它的语义是「超出上限就丢最老的」（滑窗），只作用于
/// <c>Append</c> / <c>AppendRange</c>；整帧替换不需要它，设了反而会悄悄砍掉地图快照的点。
/// </para>
/// <para>
/// 节点在场景装配时就挂上、默认 <c>Visible = false</c>，收到第一帧才显示 ——
/// 于是「场景图结构」在整份生命期里不变（0.3.0 的线程契约允许属主线程改结构，
/// 但结构不动最省事，也不会在渲染中途动到节点列表）。
/// </para>
/// </summary>
public sealed class PointCloudLayer
{
    /// <summary>节点名（拾取提示里会显示它）。</summary>
    private const string NodeName = "SensorPointCloud";

    /// <summary>数据坐标系 —— 后端已算好 <c>base_link</c> 下的坐标，客户端不做任何变换。</summary>
    private const string FrameId = "base_link";

    private readonly PointCloud2Data _data;

    /// <summary>转换缓冲：只在点数变大时扩容，稳态下每帧零分配。</summary>
    private Vector3[] _positions = [];
    private Vector4[] _colors = [];

    public PointCloudLayer(float pointSize = 2.5f)
    {
        _data = PointCloud2Data.CreateMutable(initialCapacity: 0, withColor: true, frameId: FrameId);
        Node = new PointCloud(pointSize, color: null, name: NodeName, data: _data) { Visible = false };
    }

    /// <summary>场景图里的那个节点（由 <see cref="RobotScene"/> 挂到图上并负责释放）。</summary>
    public PointCloud Node { get; }

    /// <summary>当前帧的点数（0 = 还没收到数据 / 已清空）。</summary>
    public int Count => _data.Count;

    /// <summary>已成功落地的帧数（诊断用）。</summary>
    public long AppliedFrames { get; private set; }

    /// <summary>已丢弃的帧数（点数与数组长度不符 —— 正常链路上应当恒为 0）。</summary>
    public long RejectedFrames { get; private set; }

    /// <summary>最后一帧的序号（诊断用）。</summary>
    public uint LastSeq { get; private set; }

    /// <summary>最后一帧的语义（<c>frame</c> / <c>map</c>）。</summary>
    public string LastScope { get; private set; } = "";

    /// <summary>
    /// 用一帧整帧替换点云。<b>只能在场景图属主线程（渲染回调）里调用。</b>
    /// </summary>
    /// <returns>true = 已落地；false = 帧不自洽（被丢弃，不抛异常 —— 不能让一帧坏数据毁掉渲染循环）。</returns>
    public bool Apply(PointCloudFrame frame)
    {
        if (frame.Count <= 0 || frame.Xyz.Length < frame.Count * 3 || frame.Rgb.Length < frame.Count)
        {
            RejectedFrames++;
            return false;
        }

        if (_positions.Length < frame.Count)
        {
            _positions = new Vector3[frame.Count];
            _colors = new Vector4[frame.Count];
        }

        for (int i = 0; i < frame.Count; i++)
        {
            _positions[i] = new Vector3(frame.Xyz[i * 3], frame.Xyz[i * 3 + 1], frame.Xyz[i * 3 + 2]);
            _colors[i] = Unpack(frame.Rgb[i]);
        }

        // 整帧替换：清空 → 预留 → 灌满。先 EnsureCapacity 再 AddPoints，
        // 让扩容一步到位（否则会按 16 → 32 → … 翻倍着长）。
        _data.Clear();
        _data.EnsureCapacity(frame.Count);
        _data.AddPoints(_positions.AsSpan(0, frame.Count), _colors.AsSpan(0, frame.Count));

        Node.Visible = true;
        AppliedFrames++;
        LastSeq = frame.Seq;
        LastScope = frame.Scope;
        return true;
    }

    /// <summary>清空并隐藏（断流 / 换场景时用；容量保留，下一帧不必重新扩容）。</summary>
    public void Clear()
    {
        _data.Clear();
        Node.Visible = false;
        LastScope = "";
    }

    /// <summary>
    /// <c>0x00RRGGBB</c> → 0..1 的 RGBA。
    /// 协议里的颜色是打包整数（<c>(byte)(c &gt;&gt; 16)</c> 这样取），不是 PCL 的 float 位模式。
    /// </summary>
    private static Vector4 Unpack(uint rgb) => new(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8) & 0xFF) / 255f,
        (rgb & 0xFF) / 255f,
        1f);
}
