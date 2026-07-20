namespace RUSTool.Communication;

/// <summary>
/// 指令请求（客户端 → 服务器）
/// </summary>
public record CommandRequest
{
    public string Cmd { get; init; } = "";
    public double[] Args { get; init; } = [];
    public int Id { get; init; }
}
