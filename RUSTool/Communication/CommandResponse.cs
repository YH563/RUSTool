namespace RUSTool.Communication;

/// <summary>
/// 指令响应（服务器 → 客户端）
/// </summary>
public record CommandResponse
{
    public int Id { get; init; }
    public bool Success { get; init; }
    public double[] Result { get; init; } = [];
}
