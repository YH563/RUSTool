using System;
using System.Buffers.Binary;
using System.Text;
using RUSTool.Communication;
using Xunit;

namespace RUSTool.Core.Tests;

/// <summary>
/// <c>/mesh</c> 增量网格帧解码（协议 §3.4）单测：raw payload + 一个 upsert + 一个 remove。
/// 锁住块元数据解析、三角汤（verts=3·tris）、位置/法线反量化、以及坏帧返回 null。
/// </summary>
public class MeshFrameCodecTests
{
    [Fact]
    public void 解码_一帧含upsert与remove块()
    {
        byte[] message = BuildFrame(encoding: "raw");

        var frame = MeshFrameCodec.TryDecode(message, out string? error);

        Assert.Null(error);
        Assert.NotNull(frame);
        Assert.Equal("delta", frame!.Scope);
        Assert.Equal(7u, frame.Seq);
        Assert.Equal(2, frame.Chunks.Count);

        var upsert = frame.Chunks[0];
        Assert.False(upsert.Remove);
        Assert.Equal(123, upsert.Id);
        Assert.Equal(3, upsert.VertexCount);
        Assert.Equal(1, upsert.TriangleCount);
        Assert.Equal(9, upsert.Positions.Length);
        Assert.NotNull(upsert.Normals);
        Assert.Equal(9, upsert.Normals!.Length);
        Assert.Equal(1f, upsert.OriginX);
        Assert.Equal(42, upsert.Revision);

        var remove = frame.Chunks[1];
        Assert.True(remove.Remove);
        Assert.Equal(456, remove.Id);
        Assert.Empty(remove.Positions);
    }

    [Fact]
    public void 解码_坏JSON返回null()
        => Assert.Null(MeshFrameCodec.TryDecode("garbage"u8.ToArray(), out _));

    [Fact]
    public void 解码_非mesh类型返回null()
    {
        string head = """{"type":"pointcloud","chunks":[]}""";
        byte[] hb = Encoding.UTF8.GetBytes(head);
        var msg = new byte[4 + hb.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(msg, (uint)hb.Length);
        hb.CopyTo(msg, 4);

        Assert.Null(MeshFrameCodec.TryDecode(msg, out string? error));
        Assert.Contains("mesh", error);
    }

    /// <summary>拼一帧 mesh：raw 编码，3 顶点 1 三角面 + 一个 remove。</summary>
    private static byte[] BuildFrame(string encoding)
    {
        const int verts = 3;
        var payload = new byte[verts * 3 * 2 + verts * 3]; // int16 pos + int8 normal
        for (int i = 0; i < verts; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(i * 6 + 0), (short)(i * 1000));
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(i * 6 + 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(i * 6 + 4), 0);
        }
        for (int i = 0; i < verts * 3; i++)
            payload[verts * 6 + i] = 127; // 法线 (1,1,1)/127

        string head =
            $"{{\"type\":\"mesh\",\"timestamp\":1.5,\"seq\":7,\"frame_id\":\"base_link\"," +
            $"\"encoding\":\"{encoding}\",\"scope\":\"delta\",\"pos_dtype\":\"int16\",\"normal_dtype\":\"int8\"," +
            $"\"chunks\":[" +
            $"{{\"id\":123,\"kind\":\"upsert\",\"origin\":[1.0,2.0,3.0],\"revision\":42,\"verts\":3,\"tris\":1," +
            $"\"range_min\":[-1,-1,-1],\"range_max\":[1,1,1],\"has_normals\":true}}," +
            $"{{\"id\":456,\"kind\":\"remove\",\"revision\":7}}" +
            $"]}}";

        byte[] hb = Encoding.UTF8.GetBytes(head);
        var message = new byte[4 + hb.Length + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(message, (uint)hb.Length);
        hb.CopyTo(message, 4);
        payload.CopyTo(message, 4 + hb.Length);
        return message;
    }
}
