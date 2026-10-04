using System;
using System.Buffers.Binary;
using System.Text;

namespace RUSTool.Replay;

/// <summary>
/// 极简 CDR（Common Data Representation）读取器 —— 只覆盖记录文件里两种 ROS 消息
/// （<c>RobotState</c> / <c>SensorFrame</c>）用到的字段类型，不追求通用。
///
/// <para>
/// <b>对齐规则（与 FastCDR / <c>rclcpp::Serialization</c> 一致）：</b>payload 前 4 字节是
/// 封装头（本实现按小端处理），其后每个字段按自身尺寸对齐（2/4/8），是相对<b>整个 payload
/// 起始</b>计算偏移的。序列 = <c>uint32 计数</c> + 元素；字符串 = <c>uint32 长度（含结尾 NUL）</c>
/// + UTF-8 字节。
/// </para>
/// </summary>
public ref struct CdrReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _pos;

    public CdrReader(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
            throw new RecPayloadException($"CDR payload 只有 {payload.Length} 字节，连封装头都不够");

        // 封装头：[0..2) 表示标识（0x0001 小端），[2..4) 选项。这里只支持小端。
        if (payload[0] != 0x00 || payload[1] != 0x01)
            throw new RecPayloadException("CDR 不是小端表示（当前只支持 CDR_LE）");

        _data = payload;
        _pos = 4;
    }

    public int Position => _pos;

    /// <summary>
    /// 对齐到 <paramref name="size"/>。
    ///
    /// <para>
    /// <b>基准是 CDR body 起点（= 4 字节封装头之后），不是整个 payload 起点</b> ——
    /// 与 FastCDR 一致（实测：真实录音里 <c>-π/4</c> 的双精度落在绝对偏移 4+8k）。
    /// 按 payload 起点对齐会整体错位，字段解成垃圾。
    /// </para>
    /// </summary>
    private void Align(int size)
    {
        const int bodyStart = 4;
        int relative = _pos - bodyStart;
        int r = relative % size;
        if (r != 0)
            _pos += size - r;
    }

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || _pos + count > _data.Length)
            throw new RecPayloadException($"CDR 越界：需要 {count} 字节，剩余 {_data.Length - _pos}");
        var span = _data.Slice(_pos, count);
        _pos += count;
        return span;
    }

    public byte ReadU8() => _pos < _data.Length
        ? _data[_pos++]
        : throw new RecPayloadException("CDR 越界：读 u8");

    public int ReadI32()
    {
        Align(4);
        return BinaryPrimitives.ReadInt32LittleEndian(Take(4));
    }

    public uint ReadU32()
    {
        Align(4);
        return BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    }

    public double ReadF64()
    {
        Align(8);
        return BinaryPrimitives.ReadDoubleLittleEndian(Take(8));
    }

    /// <summary>读一个字符串（uint32 长度含 NUL + UTF-8）。</summary>
    public string ReadString()
    {
        uint length = ReadU32();
        if (length == 0)
            return "";
        var bytes = Take((int)length);
        int n = bytes.Length;
        if (n > 0 && bytes[n - 1] == 0)
            n--;
        return Encoding.UTF8.GetString(bytes[..n]);
    }

    /// <summary>读 <c>float64[]</c> 序列。</summary>
    public double[] ReadDoubleSequence()
    {
        uint count = ReadU32();
        if (count == 0)
            return [];
        Align(8);
        var values = new double[count];
        for (int i = 0; i < count; i++)
            values[i] = ReadF64();
        return values;
    }

    /// <summary>读 <c>string[]</c> 序列。</summary>
    public string[] ReadStringSequence()
    {
        uint count = ReadU32();
        var values = new string[count];
        for (int i = 0; i < count; i++)
            values[i] = ReadString();
        return values;
    }

    /// <summary>读 <c>uint8[]</c> 序列。</summary>
    public byte[] ReadByteSequence()
    {
        uint count = ReadU32();
        return Take((int)count).ToArray();
    }

    /// <summary>读 <c>builtin_interfaces/Time</c>（int32 sec + uint32 nanosec）并折算成秒。</summary>
    public double ReadTimeSeconds()
    {
        int sec = ReadI32();
        uint nanosec = ReadU32();
        return sec + nanosec / 1e9;
    }
}

/// <summary>payload 反序列化失败（字段越界 / CDR 头非法等）。</summary>
public sealed class RecPayloadException : Exception
{
    public RecPayloadException(string message) : base(message) { }
}
