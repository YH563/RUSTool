using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RUSTool.Communication;
using RUSTool.Replay;
using Xunit;

namespace RUSTool.Replay.Tests;

/// <summary>
/// 本地回放的数据层单测：合成一个 <c>.rusrec</c>（容器 + CDR payload）再读回来。
///
/// <para>
/// 这里用测试自己写的 <see cref="CdrWriter"/> 按 <c>RecFormat.md</c> / ROS CDR 规则
/// 手工拼字节，用生产的 <see cref="RusRecFile"/> / <see cref="RecPayloadDecoder"/> 读回 ——
/// 覆盖「容器布局 + 记录扫描 + CRC + CDR 对齐 + 点云反量化」整条链路，不依赖后端 / ROS / 界面。
/// </para>
/// </summary>
public class RusRecTests
{
    [Fact]
    public void 读取_有索引的容器_通道与记录数正确()
    {
        string path = WriteSampleFile(withIndex: true);
        try
        {
            using var file = RusRecFile.Open(path);

            Assert.True(file.HasIndex);
            Assert.Equal(2, file.Channels.Count);
            Assert.Equal(2, file.Records.Count);
            Assert.Equal("/driver/state", file.Channels[0].Topic);
            Assert.Equal("/sensor/pointcloud", file.Channels[1].Topic);
            Assert.Equal(RecPayloadDecoder.ChannelRobotState, file.Records[0].ChannelId);
            Assert.Equal(RecPayloadDecoder.ChannelSensorFrame, file.Records[1].ChannelId);
            Assert.True(file.DurationSeconds > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 读取_无索引的容器_仍能顺序扫描()
    {
        string path = WriteSampleFile(withIndex: false);
        try
        {
            using var file = RusRecFile.Open(path);

            Assert.False(file.HasIndex);
            Assert.Equal(2, file.Records.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 解码_RobotState_字段按序还原()
    {
        string path = WriteSampleFile(withIndex: true);
        try
        {
            using var file = RusRecFile.Open(path);
            var payload = file.ReadPayload(file.Records[0]);
            var state = RecPayloadDecoder.DecodeRobotState(payload);

            Assert.Equal(6, state.JointPos.Length);
            Assert.Equal(1.0, state.JointPos[0], 6);
            Assert.Equal(6, state.Effort.Length);
            Assert.Equal(2.5, state.FlangePos[5], 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 解码_SensorFrame_还原点云()
    {
        string path = WriteSampleFile(withIndex: true);
        try
        {
            using var file = RusRecFile.Open(path);
            var payload = file.ReadPayload(file.Records[1]);
            var cloud = RecPayloadDecoder.TryDecodeSensorPointCloud(payload, out string? error);

            Assert.Null(error);
            Assert.NotNull(cloud);
            Assert.Equal(2, cloud!.Count);
            Assert.Equal(6, cloud.Xyz.Length);
            Assert.Equal(2, cloud.Rgb.Length);
            Assert.Equal("raw", cloud.Encoding);
            Assert.Equal("frame", cloud.Scope);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 读取_坏CRC_抛出()
    {
        string path = WriteSampleFile(withIndex: true);
        try
        {
            // 把第一条记录 payload 的首字节改掉，令 CRC 失配。
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                using var file = RusRecFile.Open(path);
                long payloadOffset = file.Records[0].Offset + 40;
                fs.Position = payloadOffset;
                fs.WriteByte(0xFF);
            }

            using var reopened = RusRecFile.Open(path);
            Assert.Throws<RusRecException>(() => reopened.ReadPayload(reopened.Records[0]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ────────────── 合成文件 ──────────────

    internal static string WriteSampleFile(bool withIndex)
    {
        byte[] statePayload = BuildRobotStatePayload();
        byte[] sensorPayload = BuildSensorFramePayload();

        string path = Path.Combine(Path.GetTempPath(), $"rusrec-test-{Guid.NewGuid():N}.rusrec");
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);

        // ── 文件头（64B）──
        Span<byte> header = stackalloc byte[64];
        header.Clear();
        "RUSRECv1"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..], 1);   // version
        BinaryPrimitives.WriteUInt16LittleEndian(header[10..], 64); // header_size
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], 40); // rec_header_size
        BinaryPrimitives.WriteUInt16LittleEndian(header[14..], 32); // index_entry_size
        BinaryPrimitives.WriteInt64LittleEndian(header[20..], 1_700_000_000_000_000_000L);
        BinaryPrimitives.WriteUInt16LittleEndian(header[28..], 2);  // channel_count
        fs.Write(header);

        // ── 通道表 ──
        WriteChannel(fs, 0, "/driver/state", "rus_sim_interfaces/msg/RobotState", "机械臂状态");
        WriteChannel(fs, 1, "/sensor/pointcloud", "rus_sim_interfaces/msg/SensorFrame", "感知帧");

        // ── 记录 ──
        long streamOffset = fs.Position;
        long off0 = fs.Position;
        WriteRecord(fs, channel: 0, seq: 0, stampNs: 1_000, recvNs: 1_500, statePayload);
        long off1 = fs.Position;
        WriteRecord(fs, channel: 1, seq: 0, stampNs: 2_000_000, recvNs: 2_000_500, sensorPayload);

        if (withIndex)
        {
            long indexOffset = fs.Position;
            WriteIndexEntry(fs, off0, 0, 1_000, statePayload);
            WriteIndexEntry(fs, off1, 1, 2_000_000, sensorPayload);

            Span<byte> footer = stackalloc byte[32];
            footer.Clear();
            BinaryPrimitives.WriteInt64LittleEndian(footer[..], indexOffset);
            BinaryPrimitives.WriteInt64LittleEndian(footer[8..], 2);
            BinaryPrimitives.WriteInt64LittleEndian(footer[16..], indexOffset + 32 * 2 + 32);
            "RUSIDX01"u8.CopyTo(footer[24..]);
            fs.Write(footer);
        }

        return path;
    }

    private static void WriteChannel(Stream s, ushort id, string topic, string type, string note)
    {
        Span<byte> u16 = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(u16, id);
        s.Write(u16);
        BinaryPrimitives.WriteUInt16LittleEndian(u16, 0); // kind = ros_msg
        s.Write(u16);
        WriteString(s, topic);
        WriteString(s, type);
        WriteString(s, note);
    }

    private static void WriteRecord(Stream s, ushort channel, uint seq, long stampNs, long recvNs, byte[] payload)
    {
        Span<byte> h = stackalloc byte[40];
        h.Clear();
        "REC1"u8.CopyTo(h);
        BinaryPrimitives.WriteUInt16LittleEndian(h[4..], channel);
        BinaryPrimitives.WriteUInt16LittleEndian(h[6..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(h[8..], seq);
        BinaryPrimitives.WriteInt64LittleEndian(h[12..], stampNs);
        BinaryPrimitives.WriteInt64LittleEndian(h[20..], recvNs);
        BinaryPrimitives.WriteUInt32LittleEndian(h[28..], (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(h[32..], Crc32.Compute(payload));
        s.Write(h);
        s.Write(payload);
    }

    private static void WriteIndexEntry(Stream s, long offset, ushort channel, long stampNs, byte[] payload)
    {
        Span<byte> e = stackalloc byte[32];
        e.Clear();
        BinaryPrimitives.WriteInt64LittleEndian(e[..], offset);
        BinaryPrimitives.WriteInt64LittleEndian(e[8..], stampNs);
        BinaryPrimitives.WriteUInt16LittleEndian(e[16..], channel);
        BinaryPrimitives.WriteUInt16LittleEndian(e[18..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(e[20..], (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(e[24..], Crc32.Compute(payload));
        s.Write(e);
    }

    private static void WriteString(Stream s, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> len = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(len, (ushort)bytes.Length);
        s.Write(len);
        s.Write(bytes);
    }

    private static byte[] BuildRobotStatePayload()
    {
        var w = new CdrWriter();
        w.WriteI32(0);            // Time.sec
        w.WriteU32(0);            // Time.nanosec
        w.WriteString("base");    // frame_id
        w.WriteDoubleSeq([1, 2, 3, 4, 5, 6]);       // joint_pos
        w.WriteDoubleSeq([0, 0, 0, 0, 0, 0]);       // joint_vel
        w.WriteDoubleSeq([0, 0, 0, 0, 0, 0]);       // joint_acc
        w.WriteDoubleSeq([1, 1, 1, 1, 1, 1]);       // effort
        w.WriteDoubleSeq([0, 0, 0, 0, 0, 2.5]);     // flange_pos
        w.WriteI32(0);            // tool_index
        w.WriteDoubleSeq([]);     // tool_pose
        return w.ToArray();
    }

    private static byte[] BuildSensorFramePayload()
    {
        const int points = 2;
        // raw 点数据：int16 x/y/z + uint32 rgb
        var data = new byte[points * SensorFrameCodec.PointStride];
        for (int i = 0; i < points; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 10 + 0), (short)(i * 100));
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 10 + 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(i * 10 + 4), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 10 + 6), 0x00FF0000u);
        }

        var w = new CdrWriter();
        w.WriteU8(0);                 // type = pointcloud
        w.WriteString("raw");         // encoding
        w.WriteI32(0); w.WriteU32(0); // stamp
        w.WriteU32(1);                // seq
        w.WriteString("base_link");   // frame_id
        w.WriteString("frame");       // scope
        w.WriteU32(points);
        w.WriteStringSeq(["x", "y", "z", "rgb"]);
        w.WriteString("int16");
        w.WriteDoubleSeq([-1, -1, -1]); // range_min
        w.WriteDoubleSeq([1, 1, 1]);    // range_max
        w.WriteU32(0);                // width
        w.WriteU32(0);                // height
        w.WriteString("");            // image_encoding
        w.WriteU32(0);                // step
        w.WriteByteSeq(data);
        return w.ToArray();
    }

    /// <summary>按 CDR（小端、字段对齐、字符串含 NUL）手工拼一个 payload。</summary>
    private sealed class CdrWriter
    {
        private readonly List<byte> _bytes = [0x00, 0x01, 0x00, 0x00];

        // 与 CdrReader 同一基准：对齐相对 CDR body（跳过 4 字节封装头）。
        private void Align(int size)
        {
            while ((_bytes.Count - 4) % size != 0)
                _bytes.Add(0);
        }

        public void WriteU8(byte value) => _bytes.Add(value);

        public void WriteI32(int value)
        {
            Align(4);
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(b, value);
            _bytes.AddRange(b.ToArray());
        }

        public void WriteU32(uint value)
        {
            Align(4);
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, value);
            _bytes.AddRange(b.ToArray());
        }

        public void WriteF64(double value)
        {
            Align(8);
            Span<byte> b = stackalloc byte[8];
            BinaryPrimitives.WriteDoubleLittleEndian(b, value);
            _bytes.AddRange(b.ToArray());
        }

        public void WriteString(string value)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(value);
            WriteU32((uint)(utf8.Length + 1)); // 含结尾 NUL
            _bytes.AddRange(utf8);
            _bytes.Add(0);
        }

        public void WriteDoubleSeq(IReadOnlyList<double> values)
        {
            WriteU32((uint)values.Count);
            foreach (double v in values)
                WriteF64(v);
        }

        public void WriteStringSeq(IReadOnlyList<string> values)
        {
            WriteU32((uint)values.Count);
            foreach (string v in values)
                WriteString(v);
        }

        public void WriteByteSeq(ReadOnlySpan<byte> values)
        {
            WriteU32((uint)values.Length);
            _bytes.AddRange(values.ToArray());
        }

        public byte[] ToArray() => [.. _bytes];
    }
}
