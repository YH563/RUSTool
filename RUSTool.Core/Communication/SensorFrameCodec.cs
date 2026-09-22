using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ZstdSharp;

namespace RUSTool.Communication;

/// <summary>
/// 一帧解码后的点云（<c>/sensor</c> 通道，协议 §3.3）。
///
/// <para>
/// 这是【反量化之后】的纯数据：<see cref="Xyz"/> 是米（<c>base_link</c> 坐标系，后端已算好变换，
/// 客户端不再做任何坐标变换），<see cref="Rgb"/> 是打包颜色 <c>0x00RRGGBB</c>。
/// 里里外外没有一个图形类型 —— 它由 <see cref="SensorFrameCodec"/> 在 WebSocket 线程上产出，
/// 交给界面层的邮箱（见 <c>RobotViewport.SubmitPointCloud</c>）再进场景图。
/// </para>
/// <para>
/// 每帧都是自包含的完整点集：协议里没有 delta / 差分字段，<c>scope</c> 只是「这帧归属哪个视图」
/// 的语义标记（当前帧 / 累积地图快照），两者走的都是同一条「整帧替换」路径。
/// </para>
/// </summary>
/// <param name="Xyz">XYZ 交错数组（长度 = <c>Count × 3</c>），单位米。</param>
/// <param name="Rgb">每点的打包颜色（长度 = <c>Count</c>），<c>0x00RRGGBB</c>。</param>
/// <param name="Count">点数。</param>
/// <param name="Seq">帧序号（<c>map_clear</c> 后会被后端复位，因此只能用来统计丢帧、不能当单调断言）。</param>
/// <param name="Timestamp">采集时间戳（秒，ROS 时基）。</param>
/// <param name="FrameId">数据坐标系（点云为 <c>base_link</c>）。</param>
/// <param name="Encoding">payload 的压缩算法（<see cref="SensorEncodings"/>）。</param>
/// <param name="Scope">数据语义（<see cref="SensorScopes"/>；未知时为 <c>""</c>）。</param>
public sealed record SensorPointCloudFrame(
    float[] Xyz,
    uint[] Rgb,
    int Count,
    uint Seq,
    double Timestamp,
    string FrameId,
    string Encoding,
    string Scope);

/// <summary>
/// <c>/sensor</c> 二进制帧解码器（协议 §3.3；纯函数，无状态）。
///
/// <para>
/// <b>线格式</b>：<c>uint32 LE 头长度 + JSON 头（UTF-8）+ payload</c>；payload 解压后每点 10 字节 ——
/// <c>int16 x | int16 y | int16 z | uint32 rgb</c>，全部小端。坐标必须用【本帧头里的】
/// <c>range_min</c> / <c>range_max</c> 反量化：<c>v = min + (q + 32768) × (max − min) / 65535</c>。
/// </para>
/// <para>
/// <b>为什么解码放在这一层</b>：解压 + 反量化是纯 CPU 计算，跟界面与 GL 无关（因此可以单测，
/// 见 <c>tests/RUSTool.Core.Tests/SensorFrameCodecTests.cs</c>）；而它必须发生在 UI 线程之外 ——
/// 一帧地图快照有几 MB、几十万到几百万点。产出的数组交给渲染侧「覆盖式只保留最新一帧」。
/// </para>
/// <para>
/// <b>坏帧一律丢，不抛给调用方</b>：<see cref="TryDecode(ReadOnlyMemory{byte}, out string?)"/>
/// 返回 <c>null</c> 并给出原因（由调用方决定记不记日志 —— 覆盖式通道下坏帧可能每帧都来，
/// 不能每帧刷屏）。
/// </para>
/// </summary>
public static class SensorFrameCodec
{
    /// <summary>解压后每个点占的字节数：<c>int16 x/y/z</c> + <c>uint32 rgb</c>。</summary>
    public const int PointStride = 10;

    /// <summary>
    /// 点数上限（纯守卫，不是业务限制）。实际单帧 3~30 万点、地图快照数百万点，
    /// 留出足量余量只为挡住「头里谎报点数」的坏帧 —— 那种帧会先让我们分配几百 MB 的数组。
    /// </summary>
    public const int MaxPoints = 4_000_000;

    /// <summary>payload 的分量布局（协议里当前只有这一种组合）。</summary>
    private static readonly string[] ExpectedFields = ["x", "y", "z", "rgb"];

    /// <summary>
    /// 解码一条完整的 <c>/sensor</c> 消息（一条 WS 消息 = 一帧；分片必须由调用方先拼回一条消息）。
    /// </summary>
    /// <param name="message">整帧原始字节。</param>
    /// <param name="error">失败原因（中文，可直接进日志）；成功时为 <c>null</c>。</param>
    /// <returns>解码后的点云；坏帧 / 未实现的帧类型返回 <c>null</c>。</returns>
    public static SensorPointCloudFrame? TryDecode(ReadOnlyMemory<byte> message, out string? error)
    {
        try
        {
            error = null;
            return Decode(message);
        }
        catch (SensorFrameException ex)
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

    /// <summary>同 <see cref="TryDecode(ReadOnlyMemory{byte}, out string?)"/>，不要失败原因。</summary>
    public static SensorPointCloudFrame? TryDecode(ReadOnlyMemory<byte> message)
        => TryDecode(message, out _);

    /// <summary>
    /// 反量化：<c>v = min + (q + 32768) × (max − min) / 65535</c>
    /// （与后端 <c>SensorEncoder::quantize</c> 严格互逆）。
    ///
    /// <para>
    /// 总误差 ≤ 步长/2 + 头里浮点的输出误差（后端按 <c>%.6f</c> 出，步长 ≈ (max−min)/65535，
    /// 典型 1.5e-5 m）。<c>max == min</c> 的退化包围盒在后端被补过 1 mm，这里不必特判。
    /// </para>
    /// </summary>
    public static float Dequantize(short q, double min, double max)
        => (float)(min + (q + 32768.0) * (max - min) / 65535.0);

    /// <summary>
    /// 量化（<see cref="Dequantize"/> 的逆）：<c>q = round((v − min) × 65535 / (max − min)) − 32768</c>，
    /// 结果夹进 <c>int16</c> 范围。
    ///
    /// <para>
    /// 客户端本来只解码、不编码，但留着它让两件事成立：反量化公式可以单测成「量化 → 反量化」闭环；
    /// 截图模式也能造一帧和真实链路同样布局的合成点云（见 <c>Program.cs</c> 的 <c>--demo-cloud</c>）。
    /// 退化包围盒（<c>max ≤ min</c>）返回 0 —— 协议保证不会出现，这里只是不让它除零。
    /// </para>
    /// </summary>
    public static short Quantize(double value, double min, double max)
    {
        if (max <= min)
            return 0;

        double quantized = Math.Round((value - min) * 65535.0 / (max - min) - 32768.0);
        return (short)Math.Clamp(quantized, short.MinValue, short.MaxValue);
    }

    /// <summary>
    /// 组装一条 <c>/sensor</c> 点云帧（<c>encoding = raw</c>，payload 不压缩），与
    /// <see cref="TryDecode(ReadOnlyMemory{byte}, out string?)"/> 严格互逆 —— 供单测与截图模式的合成帧使用（协议 §3.3）。
    /// </summary>
    /// <param name="xyz">XYZ 交错数组（米，长度 = <c>count × 3</c>）。</param>
    /// <param name="rgb">每点的打包颜色（<c>0x00RRGGBB</c>，长度 = <c>count</c>）。</param>
    /// <param name="count">点数。</param>
    /// <param name="seq">帧序号。</param>
    /// <param name="scope">数据语义（<see cref="SensorScopes"/>）。</param>
    /// <param name="frameId">数据坐标系（点云为 <c>base_link</c>）。</param>
    /// <param name="timestamp">时间戳（秒）。</param>
    /// <exception cref="ArgumentException">数组长度与 <paramref name="count"/> 不符。</exception>
    public static byte[] EncodeRaw(ReadOnlySpan<float> xyz, ReadOnlySpan<uint> rgb, int count,
        uint seq = 0, string scope = SensorScopes.Frame, string frameId = "base_link",
        double timestamp = 0)
    {
        if (count <= 0 || xyz.Length != count * 3 || rgb.Length != count)
            throw new ArgumentException($"数组长度与 count 不符（xyz={xyz.Length}, rgb={rgb.Length}, count={count}）");

        // 逐轴包围盒：与后端一样按【本帧实际范围】取值（范围逐帧变化，两侧都不缓存）。
        Span<double> min = [double.MaxValue, double.MaxValue, double.MaxValue];
        Span<double> max = [double.MinValue, double.MinValue, double.MinValue];
        for (int i = 0; i < count; i++)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                double v = xyz[i * 3 + axis];
                min[axis] = Math.Min(min[axis], v);
                max[axis] = Math.Max(max[axis], v);
            }
        }

        // 退化轴补 1 mm，保证 max > min（量化步长才有意义）—— 与后端同一约定。
        for (int axis = 0; axis < 3; axis++)
        {
            if (max[axis] <= min[axis])
                max[axis] = min[axis] + 0.001;
        }

        byte[] payload = new byte[count * PointStride];
        for (int i = 0; i < count; i++)
        {
            Span<byte> point = payload.AsSpan(i * PointStride, PointStride);
            for (int axis = 0; axis < 3; axis++)
                BinaryPrimitives.WriteInt16LittleEndian(point[(axis * 2)..], Quantize(xyz[i * 3 + axis], min[axis], max[axis]));
            BinaryPrimitives.WriteUInt32LittleEndian(point[6..], rgb[i] & 0x00FFFFFFu);
        }

        string head =
            $"{{\"type\":\"{SensorTypes.PointCloud}\",\"points\":{count}," +
            $"\"fields\":[\"x\",\"y\",\"z\",\"rgb\"],\"dtype\":\"int16\"," +
            $"\"range_min\":[{Invariant(min[0])},{Invariant(min[1])},{Invariant(min[2])}]," +
            $"\"range_max\":[{Invariant(max[0])},{Invariant(max[1])},{Invariant(max[2])}]," +
            $"\"frame_id\":\"{frameId}\",\"encoding\":\"{SensorEncodings.Raw}\",\"scope\":\"{scope}\"," +
            $"\"timestamp\":{timestamp.ToString("F3", CultureInfo.InvariantCulture)},\"seq\":{seq}}}";

        byte[] headBytes = Encoding.UTF8.GetBytes(head);
        byte[] message = new byte[4 + headBytes.Length + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(message, (uint)headBytes.Length);
        headBytes.CopyTo(message, 4);
        payload.CopyTo(message, 4 + headBytes.Length);
        return message;
    }

    /// <summary>把三点包围盒写成协议头里的数组（<c>%.6f</c> 与后端一致）。</summary>
    private static string Invariant(double value)
        => value.ToString("F6", CultureInfo.InvariantCulture);

    // ────────────── 内部 ──────────────

    private static SensorPointCloudFrame Decode(ReadOnlyMemory<byte> message)
    {
        if (message.Length < 4)
            throw new SensorFrameException($"帧只有 {message.Length} 字节，连头长度字段都不够");

        int headLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(message.Span);
        if (headLength <= 0 || 4L + headLength > message.Length)
            throw new SensorFrameException($"头长度 {headLength} 不自洽（整帧 {message.Length} 字节）");

        using JsonDocument document = JsonDocument.Parse(message.Slice(4, headLength));
        JsonElement head = document.RootElement;

        string type = OptionalString(head, "type");
        if (type != SensorTypes.PointCloud)
            throw new SensorFrameException($"帧类型 {type} 未实现（本客户端只解码 {SensorTypes.PointCloud}）");

        // dtype / fields 当前是固定组合；不匹配就整帧丢 —— 猜着解比丢掉更坏（坐标会被解成噪声）。
        string dtype = OptionalString(head, "dtype");
        if (dtype.Length > 0 && dtype != "int16")
            throw new SensorFrameException($"dtype={dtype} 未实现（当前只支持 int16）");
        if (!FieldsMatch(head))
            throw new SensorFrameException("fields 不是 [x,y,z,rgb]（布局不认识，整帧丢弃）");

        long points = RequireUInt32(head, "points");
        if (points <= 0)
            throw new SensorFrameException("points 为 0");
        if (points > MaxPoints)
            throw new SensorFrameException($"points={points} 超过上限 {MaxPoints}（疑为坏帧）");

        double[] rangeMin = ReadTriple(head, "range_min");
        double[] rangeMax = ReadTriple(head, "range_max");
        string encoding = RequireString(head, "encoding");

        int headSize = 4 + headLength;
        int payloadLength = message.Length - headSize;
        if (payloadLength <= 0)
            throw new SensorFrameException("payload 为空");

        long expected = points * PointStride;
        ReadOnlySpan<byte> raw;
        switch (encoding)
        {
            case SensorEncodings.Raw:
                // raw：payload 就是点数据本身，直接读（不复制）。
                if (payloadLength != expected)
                    throw new SensorFrameException($"raw payload {payloadLength} 字节 ≠ points×{PointStride}={expected}");
                raw = message.Span.Slice(headSize, payloadLength);
                break;

            case SensorEncodings.Zstd:
                // zstd：解压到「刚好点数据大小」的缓冲；多一字节少一字节都算坏帧。
                // 这是本链路唯一的稳态分配（30 万点 ≈ 3 MB，地图快照数十 MB）——
                // 解出来的数组本来就要交给渲染侧，且是覆盖式的（旧的那份立刻可回收）。
                byte[] decompressed = new byte[expected];
                using (var decompressor = new Decompressor())
                {
                    int written = decompressor.Unwrap(message.Span.Slice(headSize, payloadLength), decompressed);
                    if (written != expected)
                        throw new SensorFrameException($"解压后 {written} 字节 ≠ points×{PointStride}={expected}");
                }
                raw = decompressed;
                break;

            default:
                throw new SensorFrameException($"encoding={encoding} 未实现（协议允许 zstd / raw）");
        }

        // 反量化 + 取色：全部走显式小端读取（绝不用 BitConverter 的「本机端序」）。
        var xyz = new float[points * 3];
        var rgb = new uint[points];
        for (int i = 0; i < points; i++)
        {
            ReadOnlySpan<byte> point = raw.Slice(i * PointStride, PointStride);
            xyz[i * 3] = Dequantize(BinaryPrimitives.ReadInt16LittleEndian(point), rangeMin[0], rangeMax[0]);
            xyz[i * 3 + 1] = Dequantize(BinaryPrimitives.ReadInt16LittleEndian(point[2..]), rangeMin[1], rangeMax[1]);
            xyz[i * 3 + 2] = Dequantize(BinaryPrimitives.ReadInt16LittleEndian(point[4..]), rangeMin[2], rangeMax[2]);
            rgb[i] = BinaryPrimitives.ReadUInt32LittleEndian(point[6..]);
        }

        return new SensorPointCloudFrame(
            Xyz: xyz,
            Rgb: rgb,
            Count: (int)points,
            Seq: OptionalUInt32(head, "seq"),
            Timestamp: OptionalDouble(head, "timestamp"),
            FrameId: OptionalString(head, "frame_id"),
            Encoding: encoding,
            Scope: OptionalString(head, "scope"));
    }

    /// <summary>必填字段，缺了就是坏帧。</summary>
    private static JsonElement Require(JsonElement head, string name)
        => head.TryGetProperty(name, out JsonElement value)
            ? value
            : throw new SensorFrameException($"头缺字段 {name}");

    /// <summary>
    /// 必填字符串字段。**先用 <c>ValueKind</c> 判类型再取值** ——
    /// <c>JsonElement.GetString()</c> 在元素是数字 / 对象时是抛 <c>InvalidOperationException</c> 的，
    /// 那会让「坏帧一律丢、绝不抛」这条约定漏气。
    /// </summary>
    private static string RequireString(JsonElement head, string name)
    {
        JsonElement value = Require(head, name);
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : throw new SensorFrameException($"{name} 不是字符串（实际是 {value.ValueKind}）");
    }

    /// <summary>必填非负整数字段（同样按 <c>ValueKind</c> 判，不让 <c>GetUInt32</c> 抛出去）。</summary>
    private static uint RequireUInt32(JsonElement head, string name)
    {
        JsonElement value = Require(head, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out uint parsed)
            ? parsed
            : throw new SensorFrameException($"{name} 不是非负整数");
    }

    private static string OptionalString(JsonElement head, string name)
        => head.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static uint OptionalUInt32(JsonElement head, string name)
        => head.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
           && value.TryGetUInt32(out uint parsed)
            ? parsed
            : 0u;

    private static double OptionalDouble(JsonElement head, string name)
        => head.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
           && value.TryGetDouble(out double parsed)
            ? parsed
            : 0d;

    /// <summary>读 <c>range_min</c> / <c>range_max</c>：必须是 3 个数字。</summary>
    private static double[] ReadTriple(JsonElement head, string name)
    {
        JsonElement array = Require(head, name);
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != 3)
            throw new SensorFrameException($"{name} 不是 3 元素数组");

        var values = new double[3];
        int index = 0;
        foreach (JsonElement element in array.EnumerateArray())
        {
            // 同样先判 ValueKind：TryGetDouble 对字符串元素也是抛的（与 TryGetUInt32 不一致）。
            if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out values[index]))
                throw new SensorFrameException($"{name}[{index}] 不是数字");
            index++;
        }
        return values;
    }

    /// <summary>分量顺序必须是 <c>[x,y,z,rgb]</c>（缺字段时按协议默认布局处理）。</summary>
    private static bool FieldsMatch(JsonElement head)
    {
        if (!head.TryGetProperty("fields", out JsonElement fields))
            return true;

        if (fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() != ExpectedFields.Length)
            return false;

        int index = 0;
        foreach (JsonElement element in fields.EnumerateArray())
        {
            // 元素不是字符串也当布局不认识 —— GetString() 对数字元素会抛。
            if (element.ValueKind != JsonValueKind.String
                || !string.Equals(element.GetString(), ExpectedFields[index], StringComparison.OrdinalIgnoreCase))
                return false;
            index++;
        }
        return true;
    }

    /// <summary>帧不自洽时抛它 —— 由 <see cref="TryDecode(ReadOnlyMemory{byte}, out string?)"/> 转成 <c>null</c> + 原因。</summary>
    private sealed class SensorFrameException : Exception
    {
        public SensorFrameException(string message) : base(message) { }
    }
}




