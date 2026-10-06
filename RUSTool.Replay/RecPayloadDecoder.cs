using RUSTool.Communication;
using System;

namespace RUSTool.Replay;

/// <summary>
/// 把 <c>.rusrec</c> 记录里的 payload（ROS 消息 CDR 字节）反序列化成前端认识的纯数据。
///
/// <para>
/// 前端不认识 ROS 类型系统，只映射到它已有的两个契约：
/// 通道 0 <c>RobotState</c> → <see cref="BridgeProtocol.StateFrame"/>；
/// 通道 1 <c>SensorFrame</c> → <see cref="SensorPointCloudFrame"/>（复用
/// <see cref="SensorFrameCodec"/> 的反量化，和实时 <c>/sensor</c> 走同一条解码路径）。
/// </para>
/// </summary>
public static class RecPayloadDecoder
{
    /// <summary>通道号约定（RecFormat.md §6）：0 = <c>/driver/state</c>，1 = <c>/sensor/pointcloud</c>。</summary>
    public const ushort ChannelRobotState = 0;
    public const ushort ChannelSensorFrame = 1;

    /// <summary>解码 <c>RobotState</c>（通道 0）。字段不足时缺的那些按空数组处理。</summary>
    public static BridgeProtocol.StateFrame DecodeRobotState(ReadOnlySpan<byte> payload)
    {
        var cdr = new CdrReader(payload);

        // std_msgs/Header：Time stamp + string frame_id
        double timestamp = cdr.ReadTimeSeconds();
        _ = cdr.ReadString(); // frame_id（前端不消费）

        double[] jointPos = cdr.ReadDoubleSequence();
        double[] jointVel = cdr.ReadDoubleSequence();
        double[] jointAcc = cdr.ReadDoubleSequence();
        double[] effort = cdr.ReadDoubleSequence();
        double[] flangePos = cdr.ReadDoubleSequence();

        // tool_index / tool_pose：必须读以对齐布局（越界会抛，暴露格式不匹配），
        // 且要带进 StateFrame —— 否则回放时 Status.TcpPose 为空，3D 里的工具坐标系会消失。
        int toolIndex = cdr.ReadI32();
        double[] toolPose = cdr.ReadDoubleSequence();

        return new BridgeProtocol.StateFrame(
            Timestamp: timestamp,
            FrameRate: 0,
            JointPos: jointPos,
            JointVel: jointVel,
            JointAcc: jointAcc,
            Effort: effort,
            FlangePos: flangePos,
            ToolIndex: toolIndex,
            ToolPose: toolPose);
    }

    /// <summary>
    /// 解码 <c>SensorFrame</c>（通道 1）并还原点云。非点云帧类型 / 字段布局不认识时返回
    /// <c>null</c> + 原因（整帧丢，不猜着解）。
    /// </summary>
    public static SensorPointCloudFrame? TryDecodeSensorPointCloud(
        ReadOnlySpan<byte> payload, out string? error)
    {
        error = null;
        try
        {
            var cdr = new CdrReader(payload);

            byte type = cdr.ReadU8();
            string encoding = cdr.ReadString();
            double timestamp = cdr.ReadTimeSeconds();
            uint seq = cdr.ReadU32();
            string frameId = cdr.ReadString();
            string scope = cdr.ReadString();

            uint points = cdr.ReadU32();
            string[] fields = cdr.ReadStringSequence();
            string dtype = cdr.ReadString();
            double[] rangeMin = cdr.ReadDoubleSequence();
            double[] rangeMax = cdr.ReadDoubleSequence();

            // image / ultrasound 元数据（预留）：读了但不消费。
            _ = cdr.ReadU32(); // width
            _ = cdr.ReadU32(); // height
            _ = cdr.ReadString(); // image_encoding
            _ = cdr.ReadU32(); // step
            byte[] data = cdr.ReadByteSequence();

            // type = 0 才是点云（见 SensorFrame.msg 的 TYPE_*）。
            if (type != 0)
            {
                error = $"SensorFrame.type={type} 不是点云，回放暂不支持";
                return null;
            }

            return SensorFrameCodec.TryBuildPointCloud(
                dtype, fields, (int)points, rangeMin, rangeMax, encoding, data,
                seq, timestamp, frameId, scope, out error);
        }
        catch (RecPayloadException ex)
        {
            error = $"CDR 解析失败：{ex.Message}";
            return null;
        }
    }
}
