namespace RUSTool.Charts.Data;

/// <summary>
/// 一路可画的状态量：名字（行头标签）+ 单位（读数后缀）。
///
/// <para>
/// <b>为什么单位是字符串、不是枚举、也不在这里换算：</b>
/// 单位只出现在两处 —— 行头读数（<see cref="Format"/>）与行头标签（见
/// <see cref="RobotArmChannels"/> 把单位拼进名字的写法）。本层<b>不做任何换算</b>：
/// 弧度→度、m→mm 都发生在把数据交进来之前的那一层，这样「目录」永远只描述<b>已经是什么</b>，
/// 不会出现「图上画的是度、读数写的是弧度」这种对不上的情况。
/// </para>
/// </summary>
/// <param name="Name">通道名（例：<c>关节 1</c> / <c>X</c>）。同一份目录里的名字必须互不相同 —— 它们是行头的标签。</param>
/// <param name="Unit">单位后缀（例：<c>N·m</c> / <c>°</c> / <c>m</c>）；纯计数量可传空串。</param>
public sealed record StateChannel(string Name, string Unit)
{
    /// <summary>
    /// 读数文案：固定两位小数 + 单位（右侧对齐的等宽字里，位数固定才不会左右晃）。
    /// 单位为空时不留下尾巴空格。
    /// </summary>
    public string Format(double value) => $"{value:F2} {Unit}".TrimEnd();
}
