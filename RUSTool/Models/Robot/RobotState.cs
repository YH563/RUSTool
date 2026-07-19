namespace RUSTool.Models.Robot;

/// <summary>
/// 机器人状态推送（服务器 125Hz 广播）
/// </summary>
public record RobotState
{
    public double Timestamp { get; init; }
    public double FrameRate { get; init; }
    public double[] JointPos { get; init; } = [];
    public double[] JointVel { get; init; } = [];
    public double[] JointAcc { get; init; } = [];
    public double[] Effort { get; init; } = [];
    public double[] FlangePos { get; init; } = [];
}
