using System.Collections.Generic;

namespace RUSTool.Visualization.Scene;

/// <summary>
/// 一个增量网格块（<b>图形栈侧的纯数据</b>）——顶点为块局部坐标（米）。
///
/// <para>
/// 这是 <c>RUSTool.Visualization</c> 自己的形状：它<b>不引用 RUSTool.Core</b>，
/// 所以不认识协议里的 <c>MeshFrame</c>；由界面层（同时认识两侧）把
/// <c>RUSTool.Communication.MeshFrame</c> 适配成它，与点云
/// （<c>SensorPointCloudFrame</c> → <see cref="PointCloudFrame"/>）同一种做法。
/// </para>
/// </summary>
/// <param name="Id">稳定块身份。</param>
/// <param name="Remove">true = 删除该块。</param>
/// <param name="Origin">块世界原点（米）。</param>
/// <param name="Revision">块版本（乱序守卫）。</param>
/// <param name="TriangleCount">三角面数（= 顶点数 / 3）。</param>
/// <param name="Positions">顶点位置（长度 = 顶点数 × 3，块局部，米）。</param>
/// <param name="Normals">顶点法线（长度 = 顶点数 × 3）；可空。</param>
public sealed record MeshChunkData(
    long Id,
    bool Remove,
    float OriginX, float OriginY, float OriginZ,
    long Revision,
    int TriangleCount,
    float[] Positions,
    float[]? Normals);

/// <summary>一帧增量网格（一个批次）。</summary>
public sealed record MeshFrameData(IReadOnlyList<MeshChunkData> Chunks);
