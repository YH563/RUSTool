using System.Text.Json;

namespace RUSTool.Communication;

/// <summary>
/// 消息模型 + JSON 编解码（纯函数，无状态）。
/// reply 与 event 同构，共用一个类；状态帧单独一类。
/// </summary>
public static class BridgeProtocol
{
    // ---- 请求（前端 → 后端）----
    public sealed record Command(uint Id, string Cmd, double[] Args);

    // ---- 回执 / 事件（后端 → 前端，同构）----
    public sealed record ReplyOrEvent(
        string Type,        // "reply" / "event"
        uint Id,            // reply: 对应 command id；event: 恒 0
        uint AckId,         // 仅 event：触发它的 command id
        string Event,       // 仅 event：事件名
        bool Success,
        string Message,
        double[] Result);

    // ---- 状态帧（/state 通道）----
    public sealed record StateFrame(
        double Timestamp, double FrameRate,
        double[] JointPos, double[] JointVel, double[] JointAcc,
        double[] Effort, double[] FlangePos);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>序列化 command（{"id", "cmd", "args"}）</summary>
    public static string Encode(Command cmd)
        => JsonSerializer.Serialize(cmd, JsonOptions);

    /// <summary>
    /// 序列化状态帧（字段名与 <see cref="TryParseState"/> 对称）。
    ///
    /// <para>
    /// 正常运行时状态帧只有「后端 → 前端」一个方向，这里是那条路的反向编码，
    /// 用途是「手上没有后端，也要能验证前端这段链路」：截图模式的合成帧
    /// （<c>--demo-torque</c>）先按线格式拼帧、再由 <see cref="TryParseState"/> 解回来，
    /// 于是曲线的数据源与真机走的是同一份字段定义 —— 字段名对不上会立刻暴露，
    /// 而不是安静地解出一堆空数组、让曲线停在 0。
    /// </para>
    /// </summary>
    public static string Encode(StateFrame frame)
        => JsonSerializer.Serialize(frame, JsonOptions);

    /// <summary>解析 reply / event；解析失败返回 null（丢弃 + 记日志）</summary>
    public static ReplyOrEvent? TryParseReply(string json)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<ReplyOrEvent>(json, JsonOptions);
            if (msg is null)
                return null;
            return msg with { Message = msg.Message ?? "", Result = msg.Result ?? [] };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>解析状态帧；解析失败返回 null</summary>
    public static StateFrame? TryParseState(string json)
    {
        try
        {
            var frame = JsonSerializer.Deserialize<StateFrame>(json, JsonOptions);
            if (frame is null)
                return null;
            return frame with
            {
                JointPos = frame.JointPos ?? [],
                JointVel = frame.JointVel ?? [],
                JointAcc = frame.JointAcc ?? [],
                Effort = frame.Effort ?? [],
                FlangePos = frame.FlangePos ?? []
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
