using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text.Json;
using ZstdSharp;

namespace RUSTool.Communication;

/// <summary>
/// 一个增量网格块（<c>/mesh</c> 通道，协议 §3.4）——解码 / 反量化后的纯数据。
/// 顶点为<b>块局部</b>坐标（米），世界坐标 = 局部 + (<see cref="OriginX"/>, <see cref="OriginY"/>, <see cref="OriginZ"/>)。
/// </summary>
/// <param name="Id">稳定块身份（同一 id 永远指同一块）。</param>
/// <param name="Remove">true = 删除该块（<see cref="Positions"/> 为空）。</param>
/// <param name="OriginX">块世界原点 X（米）。</param>
/// <param name="OriginY">块世界原点 Y（米）。</param>
/// <param name="OriginZ">块世界原点 Z（米）。</param>
/// <param name="Revision">块单调版本（前端用 <c>&lt;=</c> 已应用则丢弃，乱序守卫）。</param>
/// <param name="VertexCount">顶点数（三角汤：= 3 × 三角面数）。</param>
/// <param name="TriangleCount">三角面数。</param>
/// <param name="Positions">顶点位置（长度 = 顶点数 × 3，块局部，米）。</param>
/// <param name="Normals">顶点法线（长度 = 顶点数 × 3，单位向量）；协议没带时为 null。</param>
public sealed record MeshChunkData(
    long Id,
    bool Remove,
    float OriginX, float OriginY, float OriginZ,
    long Revision,
    int VertexCount, int TriangleCount,
    float[] Positions,
    float[]? Normals);

/// <summary>一帧增量网格（一个批次，含若干 upsert / remove 块）。</summary>
public sealed record MeshFrame(
    double Timestamp, uint Seq, string FrameId, string Encoding, string Scope,
    IReadOnlyList<MeshChunkData> Chunks);

/// <summary>
/// <c>/mesh</c> 增量网格帧解码器（协议 §3.4）。线格式：<c>uint32 LE 头长 + JSON 头 + payload</c>。
///
/// <para>
/// payload（解压后）按 chunks 顺序拼接：每个 upsert 块为
/// <c>int16 pos[verts*3]</c>（块局部）+ <c>int8 normal[verts*3]</c>（<c>has_normals</c> 时，单位向量 ×127）；
/// 反量化公式与点云一致。三角汤 + 隐式顺序索引（前端构建索引 <c>0..verts-1</c>）。
/// </para>
/// <para>
/// 与 <see cref="SensorFrameCodec"/> 同一约定：<b>坏帧一律返回 <c>null</c> + 原因，绝不抛</b>。
/// </para>
/// </summary>
public static class MeshFrameCodec
{
    /// <summary>单块顶点数上限（纯守卫，挡谎报点数的坏帧）。</summary>
    public const int MaxVertices = 8_000_000;

    /// <summary>解码一帧 <c>/mesh</c>（一条 WS 消息 = 一帧）。</summary>
    public static MeshFrame? TryDecode(ReadOnlyMemory<byte> message, out string? error)
    {
        try
        {
            error = null;
            return Decode(message);
        }
        catch (MeshFrameException ex)
        {
            error = ex.Message;
            return null;
        }
        catch (ZstdException ex)
        {
            error = $"zstd 解压失败：{ex.Message}";
            return null;
        }
        catch (JsonException ex)
        {
            error = $"JSON 头解析失败：{ex.Message}";
            return null;
        }
    }

    private static MeshFrame Decode(ReadOnlyMemory<byte> message)
    {
        if (message.Length < 4)
            throw new MeshFrameException($"帧只有 {message.Length} 字节，连头长度字段都不够");

        int headLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(message.Span);
        if (headLength <= 0 || 4L + headLength > message.Length)
            throw new MeshFrameException($"头长度 {headLength} 不自洽（整帧 {message.Length} 字节）");

        using JsonDocument document = JsonDocument.Parse(message.Slice(4, headLength));
        JsonElement head = document.RootElement;

        if (Str(head, "type") != SensorTypes.Mesh)
            throw new MeshFrameException($"帧类型 {Str(head, "type")} 不是 mesh（整帧丢弃）");

        double timestamp = Num(head, "timestamp");
        uint seq = (uint)Num(head, "seq");
        string frameId = Str(head, "frame_id");
        string encoding = Str(head, "encoding");
        string scope = Str(head, "scope");

        if (!head.TryGetProperty("chunks", out JsonElement chunksJson) || chunksJson.ValueKind != JsonValueKind.Array)
            throw new MeshFrameException("头缺 chunks 数组");

        // ① 先过一遍元数据：校验 + 算出解压后总长（zstd 要先知道大小）。
        var metas = new List<ChunkMeta>(chunksJson.GetArrayLength());
        long totalBytes = 0;
        foreach (JsonElement chunk in chunksJson.EnumerateArray())
        {
            long id = Int64(chunk, "id");
            bool remove = !string.Equals(Str(chunk, "kind"), "upsert", StringComparison.Ordinal);
            long revision = Int64(chunk, "revision");

            if (remove)
            {
                metas.Add(new ChunkMeta(id, revision, true, 0, 0, 0, 0, default, default));
                continue;
            }

            int verts = (int)Num(chunk, "verts");
            int tris = (int)Num(chunk, "tris");
            if (verts <= 0 || verts > MaxVertices)
                throw new MeshFrameException($"块 {id} 顶点数 {verts} 非法");
            if (verts != tris * 3)
                throw new MeshFrameException($"块 {id} verts={verts} ≠ 3×tris={tris * 3}（三角汤约定）");

            bool hasNormals = chunk.TryGetProperty("has_normals", out JsonElement hn) && hn.ValueKind == JsonValueKind.True;
            double[] origin = Triple(chunk, "origin");
            double[] rangeMin = Triple(chunk, "range_min");
            double[] rangeMax = Triple(chunk, "range_max");

            int posBytes = verts * 3 * sizeof(short);
            int normalBytes = hasNormals ? verts * 3 : 0;
            totalBytes += posBytes + normalBytes;

            metas.Add(new ChunkMeta(id, revision, false, verts, tris, posBytes, normalBytes,
                new Vec3((float)origin[0], (float)origin[1], (float)origin[2]), (rangeMin, rangeMax)));
        }

        // ② 解压 / 校验整段 payload。
        ReadOnlySpan<byte> payload = message.Span[(4 + headLength)..];
        ReadOnlySpan<byte> data = encoding switch
        {
            SensorEncodings.Raw => payload.Length == totalBytes
                ? payload
                : throw new MeshFrameException($"raw payload {payload.Length} 字节 ≠ 期望 {totalBytes}"),
            SensorEncodings.Zstd => Decompress(payload, totalBytes),
            _ => throw new MeshFrameException($"encoding={encoding} 未实现（协议允许 zstd / raw）"),
        };

        // ③ 按块顺序切片 + 反量化。
        var chunks = new List<MeshChunkData>(metas.Count);
        int offset = 0;
        foreach (ChunkMeta meta in metas)
        {
            if (meta.Remove)
            {
                chunks.Add(new MeshChunkData(meta.Id, true, 0, 0, 0, meta.Revision, 0, 0, [], null));
                continue;
            }

            ReadOnlySpan<byte> posSpan = data.Slice(offset, meta.PosBytes);
            ReadOnlySpan<byte> normalSpan = meta.NormalBytes > 0 ? data.Slice(offset + meta.PosBytes, meta.NormalBytes) : default;
            offset += meta.PosBytes + meta.NormalBytes;

            var positions = new float[meta.Verts * 3];
            for (int i = 0; i < positions.Length; i++)
            {
                short q = BinaryPrimitives.ReadInt16LittleEndian(posSpan[(i * 2)..]);
                int axis = i % 3;
                double min = axis == 0 ? meta.Range.Item1[0] : axis == 1 ? meta.Range.Item1[1] : meta.Range.Item1[2];
                double max = axis == 0 ? meta.Range.Item2[0] : axis == 1 ? meta.Range.Item2[1] : meta.Range.Item2[2];
                positions[i] = SensorFrameCodec.Dequantize(q, min, max);
            }

            float[]? normals = null;
            if (meta.NormalBytes > 0)
            {
                normals = new float[meta.Verts * 3];
                for (int i = 0; i < normals.Length; i++)
                    normals[i] = normalSpan[i] / 127f;
            }

            chunks.Add(new MeshChunkData(meta.Id, false, meta.Origin.X, meta.Origin.Y, meta.Origin.Z,
                meta.Revision, meta.Verts, meta.Tris, positions, normals));
        }

        return new MeshFrame(timestamp, seq, frameId, encoding, scope, chunks);
    }

    /// <summary>zstd 解压到「刚好 totalBytes」的缓冲；多一字节少一字节都算坏帧。</summary>
    private static byte[] Decompress(ReadOnlySpan<byte> payload, long totalBytes)
    {
        if (totalBytes <= 0 || totalBytes > int.MaxValue)
            throw new MeshFrameException($"解压目标长度 {totalBytes} 非法");

        var buffer = new byte[totalBytes];
        using var decompressor = new Decompressor();
        int written = decompressor.Unwrap(payload, buffer);
        if (written != totalBytes)
            throw new MeshFrameException($"解压后 {written} 字节 ≠ 期望 {totalBytes}");
        return buffer;
    }

    // ── JSON 小工具（缺字段 / 类型不符一律当坏帧） ──

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double Num(JsonElement e, string name)
        => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static long Int64(JsonElement e, string name)
        => e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long n) ? n : 0;

    private static double[] Triple(JsonElement e, string name)
    {
        var result = new double[3];
        if (!e.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.Array || v.GetArrayLength() != 3)
            return result;
        int i = 0;
        foreach (JsonElement el in v.EnumerateArray())
            result[i++] = el.ValueKind == JsonValueKind.Number ? el.GetDouble() : 0;
        return result;
    }

    private readonly record struct Vec3(float X, float Y, float Z);

    private readonly record struct ChunkMeta(
        long Id, long Revision, bool Remove, int Verts, int Tris, int PosBytes, int NormalBytes,
        Vec3 Origin, (double[] Min, double[] Max) Range);

    private sealed class MeshFrameException(string message) : Exception(message);
}
