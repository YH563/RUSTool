using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Linq;
using System.Text;
using RUSTool.Communication;
using Xunit;
using ZstdSharp;

namespace RUSTool.Core.Tests;

/// <summary>
/// SensorFrameCodec（<c>/sensor</c> 点云帧，协议 §3.3）的单元测试。
///
/// 帧【由测试自己按协议拼】：量化、头 JSON、字节序都独立实现一份，不用被测代码造输入 ——
/// 否则「编码器与解码器一起错」会被判成通过。覆盖三类：正常帧（raw / zstd）、
/// 边界值与缺省字段、坏帧（一律丢掉，不抛给调用方）。
///
/// 全文件不需要网络、界面、显卡或机械臂：解压用的是纯托管实现。
/// </summary>
public class SensorFrameCodecTests
{
    /// <summary>一帧的量化包围盒（逐帧变化，两端都取本帧的值）。</summary>
    private static readonly double[] RangeMin = [-0.5, -0.25, 0.0];

    /// <inheritdoc cref="RangeMin"/>
    private static readonly double[] RangeMax = [0.5, 0.75, 1.5];

    // ════════════════════════════════════════════════════════════════
    //  ① 正常帧
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void 点云帧_raw_点数_序号_坐标系_颜色与坐标都能还原()
    {
        float[] xyz = [0f, 0f, 0f, 0.25f, -0.125f, 1.0f, -0.4f, 0.6f, 0.75f];
        uint[] rgb = [0x00FF0000, 0x0000FF00, 0x000000FF];

        var frame = SensorFrameCodec.TryDecode(
            Frame(QuantizedPayload(xyz, rgb, 3), points: 3, seq: 42), out string? error);

        Assert.Null(error);
        Assert.NotNull(frame);
        Assert.Equal(3, frame!.Count);
        Assert.Equal(42u, frame.Seq);
        Assert.Equal(1234.5, frame.Timestamp);
        Assert.Equal("base_link", frame.FrameId);
        Assert.Equal(SensorScopes.Frame, frame.Scope);
        Assert.Equal(SensorEncodings.Raw, frame.Encoding);
        Assert.Equal(rgb, frame.Rgb);

        // 反量化误差 ≤ 该轴量化步长的一半（步长 = (max−min)/65535）
        for (int axis = 0; axis < 3; axis++)
        {
            double step = (RangeMax[axis] - RangeMin[axis]) / 65535.0;
            for (int i = 0; i < 3; i++)
                Assert.True(Math.Abs(frame.Xyz[i * 3 + axis] - xyz[i * 3 + axis]) <= step / 2 + 1e-5,
                    $"轴 {axis} 第 {i} 点偏差超出量化步长一半");
        }
    }

    [Fact]
    public void 点云帧_zstd_与同一份raw解出完全相同的点()
    {
        const int count = 5000;
        float[] xyz = new float[count * 3];
        uint[] rgb = new uint[count];
        var random = new Random(20260922);
        for (int i = 0; i < count; i++)
        {
            xyz[i * 3] = (float)(random.NextDouble() - 0.5);
            xyz[i * 3 + 1] = (float)(random.NextDouble() * 1.0 - 0.25);
            xyz[i * 3 + 2] = (float)(random.NextDouble() * 1.5);
            rgb[i] = (uint)random.Next(0x1000000);
        }

        byte[] raw = QuantizedPayload(xyz, rgb, count);
        byte[] compressed;
        using (var compressor = new Compressor(3))
            compressed = compressor.Wrap(raw).ToArray();

        var byZstd = SensorFrameCodec.TryDecode(Frame(compressed, count, encoding: "zstd"), out string? error);
        var byRaw = SensorFrameCodec.TryDecode(Frame(raw, count), out _);

        Assert.Null(error);
        Assert.NotNull(byZstd);
        Assert.NotNull(byRaw);
        Assert.Equal(SensorEncodings.Zstd, byZstd!.Encoding);
        Assert.Equal(byRaw!.Xyz, byZstd.Xyz);
        Assert.Equal(byRaw.Rgb, byZstd.Rgb);
    }

    [Fact]
    public void 地图快照_scope_map_走同一条整帧替换路径()
    {
        // 协议里没有 delta 字段：map 与 frame 的区别只是点数与「归属哪个视图」，
        // 解码路径必须完全一致（不 merge、不抽帧）。
        float[] xyz = [0.1f, 0.1f, 0.1f, 0.2f, 0.2f, 0.2f];
        uint[] rgb = [0x00123456, 0x00654321];

        var frame = SensorFrameCodec.TryDecode(
            Frame(QuantizedPayload(xyz, rgb, 2), points: 2, scope: SensorScopes.Map), out string? error);

        Assert.Null(error);
        Assert.NotNull(frame);
        Assert.Equal(SensorScopes.Map, frame!.Scope);
        Assert.Equal(2, frame.Count);
        Assert.Equal(rgb, frame.Rgb);
    }

    // ════════════════════════════════════════════════════════════════
    //  ② 量化 / 反量化与缺省字段
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void 反量化_两个端点精确落在包围盒边界_中点落在零()
    {
        Assert.Equal(-0.5f, SensorFrameCodec.Dequantize(short.MinValue, -0.5, 0.5), 6);
        Assert.Equal(0.5f, SensorFrameCodec.Dequantize(short.MaxValue, -0.5, 0.5), 6);
        Assert.Equal(0f, SensorFrameCodec.Dequantize(0, -0.5, 0.5), 4);
    }

    [Fact]
    public void 量化_超出包围盒的值被夹在int16范围内()
    {
        Assert.Equal(short.MinValue, SensorFrameCodec.Quantize(-10, -0.5, 0.5));
        Assert.Equal(short.MaxValue, SensorFrameCodec.Quantize(10, -0.5, 0.5));
        Assert.Equal((short)0, SensorFrameCodec.Quantize(0, -0.5, 0.5));
    }

    [Fact]
    public void 量化_退化包围盒不除零()
    {
        Assert.Equal((short)0, SensorFrameCodec.Quantize(1.0, 0.5, 0.5));
        Assert.Equal((short)0, SensorFrameCodec.Quantize(1.0, 0.5, 0.25));
    }

    [Fact]
    public void 合成帧_编码再解码_与原始坐标一致()
    {
        // EncodeRaw 与 TryDecode 必须互逆：截图模式的合成点云走的就是这条路。
        float[] xyz = [0.1f, 0.2f, 0.3f, -0.4f, -0.2f, 0.9f];
        uint[] rgb = [0x00112233, 0x00AABBCC];

        var frame = SensorFrameCodec.TryDecode(
            SensorFrameCodec.EncodeRaw(xyz, rgb, 2, seq: 99, scope: SensorScopes.Map), out string? error);

        Assert.Null(error);
        Assert.NotNull(frame);
        Assert.Equal(99u, frame!.Seq);
        Assert.Equal(SensorScopes.Map, frame.Scope);
        Assert.Equal(SensorEncodings.Raw, frame.Encoding);
        Assert.Equal(rgb, frame.Rgb);
        for (int i = 0; i < xyz.Length; i++)
            Assert.Equal(xyz[i], frame.Xyz[i], 4); // 4 位小数远大于量化误差（1e-5 量级）
    }

    [Fact]
    public void 缺省字段_头里只有最小集合时也能解码()
    {
        float[] xyz = [0f, 0f, 0f];
        uint[] rgb = [0x00010203];
        byte[] payload = QuantizedPayload(xyz, rgb, 1);

        // 只留协议要求必给的字段：没有 frame_id / scope / seq / timestamp / dtype / fields
        const string head = "{\"type\":\"pointcloud\",\"points\":1," +
                            "\"range_min\":[-0.5,-0.25,0.0],\"range_max\":[0.5,0.75,1.5],\"encoding\":\"raw\"}";

        var frame = SensorFrameCodec.TryDecode(FrameWithHead(payload, head), out string? error);

        Assert.Null(error);
        Assert.NotNull(frame);
        Assert.Equal(1, frame!.Count);
        Assert.Equal("", frame.FrameId);
        Assert.Equal("", frame.Scope);
        Assert.Equal(0u, frame.Seq);
        Assert.Equal(0d, frame.Timestamp);
    }

    // ════════════════════════════════════════════════════════════════
    //  ③ 坏帧：一律丢，不抛
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void 坏帧_消息不足四字节_丢弃并给出原因()
    {
        var frame = SensorFrameCodec.TryDecode(new byte[] { 0x01, 0x02, 0x03 }, out string? error);

        Assert.Null(frame);
        Assert.Contains("连头长度", error);
    }

    [Fact]
    public void 坏帧_头长度超过整帧长度_丢弃()
    {
        byte[] message = Frame(QuantizedPayload([0f, 0f, 0f], [0u], 1), points: 1);
        BinaryPrimitives.WriteUInt32LittleEndian(message, (uint)(message.Length + 100)); // 谎报头长度

        Assert.Null(SensorFrameCodec.TryDecode(message, out string? error));
        Assert.Contains("不自洽", error);
    }

    [Fact]
    public void 坏帧_JSON头不是合法JSON_丢弃()
    {
        var frame = SensorFrameCodec.TryDecode(FrameWithHead(new byte[10], "{不是 JSON"), out string? error);

        Assert.Null(frame);
        Assert.Contains("JSON", error);
    }

    [Fact]
    public void 坏帧_点数超过上限_丢弃而不是先分配数组()
    {
        byte[] message = Frame(QuantizedPayload([0f, 0f, 0f], [0u], 1), points: SensorFrameCodec.MaxPoints + 1);

        Assert.Null(SensorFrameCodec.TryDecode(message, out string? error));
        Assert.Contains("上限", error);
    }

    [Fact]
    public void 坏帧_raw的payload长度与points不符_丢弃()
    {
        // 头里说 2 个点，payload 只给了 1 个点
        byte[] message = Frame(QuantizedPayload([0f, 0f, 0f], [0u], 1), points: 2);

        Assert.Null(SensorFrameCodec.TryDecode(message, out string? error));
        Assert.Contains("payload", error);
    }

    [Fact]
    public void 坏帧_未实现的编码_丢弃()
    {
        byte[] message = Frame(QuantizedPayload([0f, 0f, 0f], [0u], 1), points: 1, encoding: "lz4");

        Assert.Null(SensorFrameCodec.TryDecode(message, out string? error));
        Assert.Contains("lz4", error);
    }

    [Fact]
    public void 坏帧_zstd的payload是垃圾_丢弃()
    {
        byte[] message = Frame([9, 9, 9, 9, 9, 9, 9, 9, 9, 9], points: 1, encoding: "zstd");

        Assert.Null(SensorFrameCodec.TryDecode(message, out string? error));
        Assert.Contains("zstd", error);
    }

    [Fact]
    public void 坏帧_未实现的帧类型_丢弃()
    {
        byte[] message = Frame(QuantizedPayload([0f, 0f, 0f], [0u], 1), points: 1, type: "image");

        Assert.Null(SensorFrameCodec.TryDecode(message, out string? error));
        Assert.Contains("image", error);
    }

    [Fact]
    public void 坏帧_dtype不是int16_丢弃()
    {
        byte[] message = Frame(QuantizedPayload([0f, 0f, 0f], [0u], 1), points: 1, dtype: "float32");

        Assert.Null(SensorFrameCodec.TryDecode(message, out string? error));
        Assert.Contains("float32", error);
    }

    [Fact]
    public void 坏帧_fields不是xyzrgb_丢弃()
    {
        byte[] message = Frame(QuantizedPayload([0f, 0f, 0f], [0u], 1), points: 1,
            fields: "[\"x\",\"y\",\"z\",\"intensity\"]");

        Assert.Null(SensorFrameCodec.TryDecode(message, out string? error));
        Assert.Contains("fields", error);
    }

    [Fact]
    public void 坏帧_包围盒不是三个数_丢弃()
    {
        byte[] message = Frame(QuantizedPayload([0f, 0f, 0f], [0u], 1), points: 1, min: [0.0, 0.0]);

        Assert.Null(SensorFrameCodec.TryDecode(message, out string? error));
        Assert.Contains("range_min", error);
    }

    // 头里字段「类型」不对（数字写成字符串、字符串写成数字）时必须照样丢 ——
    // System.Text.Json 的 GetString / GetUInt32 对类型不符的元素是抛异常的，
    // 「坏帧一律丢、绝不抛」这条约定要靠 ValueKind 判断守住。
    [Theory]
    [InlineData("{\"type\":\"pointcloud\",\"points\":\"1\",\"range_min\":[0,0,0],\"range_max\":[1,1,1],\"encoding\":\"raw\"}")]
    [InlineData("{\"type\":\"pointcloud\",\"points\":1,\"range_min\":[0,0,0],\"range_max\":[1,1,1],\"encoding\":5}")]
    [InlineData("{\"type\":\"pointcloud\",\"points\":1,\"range_min\":[\"0\",0,0],\"range_max\":[1,1,1],\"encoding\":\"raw\"}")]
    [InlineData("{\"type\":\"pointcloud\",\"points\":1,\"fields\":[1,2,3,4],\"range_min\":[0,0,0],\"range_max\":[1,1,1],\"encoding\":\"raw\"}")]
    [InlineData("{\"type\":\"pointcloud\",\"range_min\":[0,0,0],\"range_max\":[1,1,1],\"encoding\":\"raw\"}")]
    public void 坏帧_头里字段类型不对_丢弃而不向外抛(string head)
    {
        byte[] payload = QuantizedPayload([0f, 0f, 0f], [0u], 1);

        var frame = SensorFrameCodec.TryDecode(FrameWithHead(payload, head), out string? error);

        Assert.Null(frame);
        Assert.False(string.IsNullOrEmpty(error));
    }

    // ════════════════════════════════════════════════════════════════
    //  测试自带的「协议编码端」：量化 + 拼帧（不调用被测代码）
    // ════════════════════════════════════════════════════════════════

    /// <summary>按协议把坐标量化成 int16 小端、颜色原样 uint32 小端，每点 10 字节。</summary>
    private static byte[] QuantizedPayload(ReadOnlySpan<float> xyz, ReadOnlySpan<uint> rgb, int count,
        double[]? min = null, double[]? max = null)
    {
        double[] lo = min ?? RangeMin;
        double[] hi = max ?? RangeMax;
        byte[] payload = new byte[count * SensorFrameCodec.PointStride];
        for (int i = 0; i < count; i++)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                double q = Math.Round((xyz[i * 3 + axis] - lo[axis]) * 65535.0 / (hi[axis] - lo[axis]) - 32768.0);
                BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(i * 10 + axis * 2), (short)q);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(i * 10 + 6), rgb[i] & 0x00FFFFFFu);
        }
        return payload;
    }

    /// <summary>拼一条线格式的帧：<c>uint32 LE 头长度 + JSON 头 + payload</c>。</summary>
    private static byte[] Frame(byte[] payload, int points, string encoding = "raw", string type = "pointcloud",
        string dtype = "int16", string fields = "[\"x\",\"y\",\"z\",\"rgb\"]", string scope = "frame",
        uint seq = 7, string frameId = "base_link", double[]? min = null, double[]? max = null)
    {
        string head =
            $"{{\"type\":\"{type}\",\"points\":{points},\"fields\":{fields},\"dtype\":\"{dtype}\"," +
            $"\"range_min\":{Json(min ?? RangeMin)},\"range_max\":{Json(max ?? RangeMax)}," +
            $"\"frame_id\":\"{frameId}\",\"encoding\":\"{encoding}\",\"scope\":\"{scope}\"," +
            $"\"timestamp\":1234.5,\"seq\":{seq}}}";
        return FrameWithHead(payload, head);
    }

    /// <summary>用给定的头拼帧 —— 坏帧用例直接改头（头长度、字段、类型都在这里造）。</summary>
    private static byte[] FrameWithHead(byte[] payload, string head)
    {
        byte[] headBytes = Encoding.UTF8.GetBytes(head);
        byte[] message = new byte[4 + headBytes.Length + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(message, (uint)headBytes.Length);
        headBytes.CopyTo(message, 4);
        payload.CopyTo(message, 4 + headBytes.Length);
        return message;
    }

    private static string Json(double[] values) => "[" + string.Join(",", values.Select(Inv)) + "]";

    /// <summary>头里的浮点按 <c>%.6f</c> 出（与后端一致）。</summary>
    private static string Inv(double value) => value.ToString("F6", CultureInfo.InvariantCulture);
}
