using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RUSTool.Replay;

/// <summary>
/// <c>.rusrec</c> 记录文件格式 v1 的读取实现（纯 std，无 ROS / 无界面依赖）。
///
/// <para>
/// 布局见 RUS_Engine <c>docs/Protocol/RecFormat.md</c>：
/// <c>FileHeader(64B) → ChannelDesc × N → Record × N → IndexEntry × N → FileFooter(32B)</c>。
/// 本类只做「容器」层：读头 / 通道表 / 顺序扫描记录 / 按偏移读 payload + CRC 校验，
/// **不解释 payload 语义**（CDR 反序列化在 <see cref="RecPayloadDecoder"/>）。
/// </para>
/// <para>
/// 记录流一律用<b>顺序扫描</b>建立（每条只读 40B 记录头、payload 用 seek 跳过）：
/// 这样「正常关闭（有尾索引）」和「被强杀（无尾索引 / 截断）」的文件走同一条路径，
/// 尾部写了一半的记录会被停在截断处，前面的记录照常可读 —— 与后端 replayer 口径一致。
/// </para>
/// </summary>
public sealed class RusRecFile : IDisposable
{
    /// <summary>payload 上限（读侧防御，与后端 <c>kMaxPayloadSize</c> 一致）。</summary>
    public const uint MaxPayloadSize = 256u * 1024 * 1024;

    private const int FileHeaderSize = 64;
    private const int FileFooterSize = 32;
    private const int RecordHeaderSize = 40;

    private readonly FileStream _stream;
    private readonly int _recHeaderSize;

    public string Path { get; }
    public long CreatedUnixNs { get; }
    public IReadOnlyList<RusRecChannel> Channels { get; }
    public IReadOnlyList<RusRecRecord> Records { get; }

    /// <summary>文件是否存在可用尾索引（正常关闭）。仅供参考 / 诊断。</summary>
    public bool HasIndex { get; }

    /// <summary>录音时长（秒）= 末条 − 首条（按有效时间戳）。不足两条为 0。</summary>
    public double DurationSeconds { get; }

    private RusRecFile(FileStream stream, string path, int recHeaderSize, long createdUnixNs,
        IReadOnlyList<RusRecChannel> channels, IReadOnlyList<RusRecRecord> records, bool hasIndex)
    {
        _stream = stream;
        _recHeaderSize = recHeaderSize;
        Path = path;
        CreatedUnixNs = createdUnixNs;
        Channels = channels;
        Records = records;
        HasIndex = hasIndex;

        if (records.Count >= 2)
        {
            long first = records[0].EffectiveStampNs;
            long last = records[^1].EffectiveStampNs;
            DurationSeconds = Math.Max(0, (last - first) / 1e9);
        }
    }

    /// <summary>打开并扫描一个 <c>.rusrec</c> 文件。</summary>
    public static RusRecFile Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return Parse(stream, path);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static RusRecFile Parse(FileStream stream, string path)
    {
        long size = stream.Length;
        if (size < FileHeaderSize)
            throw new RusRecException($"文件只有 {size} 字节，连文件头都不够");

        Span<byte> header = stackalloc byte[FileHeaderSize];
        stream.ReadExactly(header);

        if (!header[..8].SequenceEqual("RUSRECv1"u8))
            throw new RusRecException("magic 不是 RUSRECv1（不是本格式 / 版本不符）");

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(header[8..]);
        if (version != 1)
            throw new RusRecException($"记录格式版本 {version} 不被支持（只支持 1）");

        int headerSize = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);
        int recHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(header[12..]);
        long createdUnixNs = BinaryPrimitives.ReadInt64LittleEndian(header[20..]);
        int channelCount = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);

        if (headerSize < FileHeaderSize)
            headerSize = FileHeaderSize;
        if (recHeaderSize < RecordHeaderSize)
            recHeaderSize = RecordHeaderSize;

        // ── 通道表 ──
        stream.Position = headerSize;
        var channels = new List<RusRecChannel>(channelCount);
        for (int i = 0; i < channelCount; i++)
        {
            ushort id = ReadU16(stream);
            ushort kind = ReadU16(stream);
            string topic = ReadString(stream);
            string typeName = ReadString(stream);
            string note = ReadString(stream);
            channels.Add(new RusRecChannel(id, kind, topic, typeName, note));
        }

        long streamOffset = stream.Position;

        // ── 尾索引（可选）──
        long indexOffset = size;
        bool hasIndex = false;
        if (size >= FileFooterSize)
        {
            stream.Position = size - FileFooterSize;
            Span<byte> footer = stackalloc byte[FileFooterSize];
            stream.ReadExactly(footer);
            if (footer[24..].SequenceEqual("RUSIDX01"u8))
            {
                long idxOffset = BinaryPrimitives.ReadInt64LittleEndian(footer[..]);
                long fileSize = BinaryPrimitives.ReadInt64LittleEndian(footer[16..]);
                if (fileSize == size && idxOffset >= streamOffset && idxOffset <= size)
                {
                    indexOffset = idxOffset;
                    hasIndex = true;
                }
            }
        }

        // ── 顺序扫描记录（不依赖索引；截断处自然收住）──
        long scanEnd = hasIndex ? indexOffset : size;
        var records = new List<RusRecRecord>();
        long pos = streamOffset;
        Span<byte> recHeader = stackalloc byte[RecordHeaderSize];
        while (pos + recHeaderSize <= scanEnd)
        {
            stream.Position = pos;
            stream.ReadExactly(recHeader);

            if (!recHeader[..4].SequenceEqual("REC1"u8))
                break; // 损坏点 / 尾部残留：停止

            ushort channelId = BinaryPrimitives.ReadUInt16LittleEndian(recHeader[4..]);
            ushort kind = BinaryPrimitives.ReadUInt16LittleEndian(recHeader[6..]);
            uint seq = BinaryPrimitives.ReadUInt32LittleEndian(recHeader[8..]);
            long stampNs = BinaryPrimitives.ReadInt64LittleEndian(recHeader[12..]);
            long recvNs = BinaryPrimitives.ReadInt64LittleEndian(recHeader[20..]);
            uint payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(recHeader[28..]);
            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(recHeader[32..]);

            if (payloadSize > MaxPayloadSize || pos + recHeaderSize + payloadSize > scanEnd)
                break; // 头部损坏 / 末条写了一半

            records.Add(new RusRecRecord(records.Count, pos, channelId, kind, seq, stampNs, recvNs, payloadSize, crc));
            pos += recHeaderSize + payloadSize;
        }

        return new RusRecFile(stream, path, recHeaderSize, createdUnixNs, channels, records, hasIndex);
    }

    /// <summary>
    /// 读一条记录的 payload。<paramref name="verifyCrc"/> = true 时校验 CRC32，
    /// 不匹配即抛（截断 / 坏块当场暴露，而不是解出一堆垃圾）。
    /// </summary>
    public byte[] ReadPayload(RusRecRecord record, bool verifyCrc = true)
    {
        var buffer = new byte[record.PayloadSize];
        _stream.Position = record.Offset + _recHeaderSize;
        _stream.ReadExactly(buffer);

        if (verifyCrc && record.Crc32 != 0)
        {
            uint actual = Crc32.Compute(buffer);
            if (actual != record.Crc32)
                throw new RusRecException($"记录 #{record.Index} CRC 校验失败（文件 {actual:X8} ≠ 头 {record.Crc32:X8}）");
        }

        return buffer;
    }

    public void Dispose() => _stream.Dispose();

    // ── 小工具 ──

    private static ushort ReadU16(Stream stream)
    {
        Span<byte> b = stackalloc byte[2];
        stream.ReadExactly(b);
        return BinaryPrimitives.ReadUInt16LittleEndian(b);
    }

    private static string ReadString(Stream stream)
    {
        ushort length = ReadU16(stream);
        if (length == 0)
            return "";
        var buffer = new byte[length];
        stream.ReadExactly(buffer);
        return Encoding.UTF8.GetString(buffer);
    }
}

/// <summary>通道描述（来自文件通道表）。</summary>
public sealed record RusRecChannel(ushort Id, ushort Kind, string Topic, string TypeName, string Note);

/// <summary>一条记录的元数据（从记录头扫出来；payload 按需再读）。</summary>
public readonly record struct RusRecRecord(
    int Index, long Offset, ushort ChannelId, ushort Kind,
    uint Seq, long StampNs, long RecvNs, uint PayloadSize, uint Crc32)
{
    /// <summary>有效时间戳：优先消息时间戳，为 0 时退回入队时刻（与后端回放口径一致）。</summary>
    public long EffectiveStampNs => StampNs != 0 ? StampNs : RecvNs;
}

/// <summary>容器层读取失败（打开 / 头部非法 / CRC 不符等）。</summary>
public sealed class RusRecException : Exception
{
    public RusRecException(string message) : base(message) { }
}

/// <summary>CRC32（IEEE 802.3，zlib 同算法）：与后端 <c>crc32</c> 一致。</summary>
public static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
            crc = (crc >> 8) ^ Table[(crc ^ b) & 0xFF];
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }
}
