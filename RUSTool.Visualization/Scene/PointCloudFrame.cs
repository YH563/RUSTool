namespace RUSTool.Visualization.Scene;

/// <summary>
/// 一帧感知点云 —— <b>渲染侧能看懂的最小形状</b>：一组已反量化的坐标 + 一组打包颜色。
///
/// <para>
/// 它与 <c>RUSTool.Core</c> 的 <c>SensorPointCloudFrame</c> 是同一件事的两种说法，
/// 刻意不复用同一个类型：本工程（RUSTool.Visualization）不认识 RUSTool.Core，
/// Core 也不认识图形栈 —— 适配发生在两边的上层（界面层），一处、几行。
/// 这与日志那条链路同一套做法（本工程声明 <c>ISimulationLogSink</c>，界面层实现它）。
/// </para>
/// <para>
/// 数组按【所有权交接】传递：解码方新建、投递后不再改动，视口只读、且只持有到被渲染线程取走为止
/// （覆盖式邮箱，见 <c>RobotViewport.SubmitPointCloud</c>）。
/// </para>
/// </summary>
/// <param name="Xyz">XYZ 交错数组（米，长度 = <c>Count × 3</c>）。</param>
/// <param name="Rgb">每点的打包颜色 <c>0x00RRGGBB</c>（长度 = <c>Count</c>）。</param>
/// <param name="Count">点数。</param>
/// <param name="Seq">帧序号（只用于诊断：后端 <c>map_clear</c> 后会同退，不能当单调断言）。</param>
/// <param name="Scope">数据语义：<c>frame</c>（当前帧）/ <c>map</c>（累积地图快照）—— 一律整帧替换。</param>
/// <param name="Timestamp">采集时间戳（秒，ROS 时基）。</param>
public sealed record PointCloudFrame(
    float[] Xyz,
    uint[] Rgb,
    int Count,
    uint Seq,
    string Scope,
    double Timestamp);
