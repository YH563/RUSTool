namespace RUSTool.Models.Robot;

/// <summary>
/// 指令请求（客户端 → 服务器）
/// </summary>
public record CommandRequest
{
    public string Cmd { get; init; } = "";
    public double[] Args { get; init; } = [];
    public int Id { get; init; }
}

/// <summary>
/// 指令响应（服务器 → 客户端）
/// </summary>
public record CommandResponse
{
    public int Id { get; init; }
    public bool Success { get; init; }
    public double[] Result { get; init; } = [];
}
