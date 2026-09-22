using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Scene;
using RobotSimulation.Robot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace RUSTool.Visualization.Scene;

/// <summary>
/// 机器人工位场景 —— 3D 视图的「内容」部分，与「怎么画」完全解耦。
///
/// <para>
/// 这里只碰 RobotSimulation 的纯 CPU 类型（<see cref="SceneGraph"/> / <see cref="RobotModel"/>）：
/// 没有 GL、没有控件、没有窗口，因此可以脱离显示器与显卡单独构造和检查 ——
/// 关节驱动这半条链路因此是可测的（<see cref="ApplyJointValues"/>）。
/// </para>
/// <para>
/// 它也是今后加图层的落点：点云、规划路径、实时轨迹、力矢量箭头都往 <see cref="Graph"/> 上挂，
/// 上层界面不需要知道多了一个图层。
/// </para>
/// </summary>
public sealed class RobotScene : IDisposable
{
    /// <summary>资源根目录名 —— 与 csproj 里 <c>Assets\**</c> 的复制规则一致。</summary>
    private const string AssetsFolder = "Assets";

    /// <summary>首选模型：真机 6 轴机械臂（带完整关节树与 STL mesh）。</summary>
    private const string DefaultModelRelativePath = "Models/fairino3_v6/fairino3_v6.urdf";

    /// <summary>首选模型缺失时的兜底：只用 URDF 内置几何体，保证「任何 checkout 都能看到东西」。</summary>
    private const string FallbackModelRelativePath = "Models/primitives.urdf";

    private readonly StringBuilder _log = new();

    private RobotScene(SceneGraph graph)
    {
        Graph = graph;

        // 感知点云图层在装配时就挂上（默认不可见，收到第一帧才显示）。
        // 结构一次性定下来，此后整份生命期只管往里灌数据 —— 见 PointCloudLayer 的注释。
        PointCloud = new PointCloudLayer();
        graph.Add(PointCloud.Node);
    }

    /// <summary>场景图：已含默认网格 / 灯光 / 世界坐标轴与一个可用机位（由库的构造函数给出）。</summary>
    public SceneGraph Graph { get; }

    /// <summary>
    /// 感知点云图层（<c>/sensor</c>）。<see cref="ApplyPointCloudFrame"/> 是它的数据入口 ——
    /// 相机取景刻意不看它：临床场景里点云可能铺得比机械臂大得多，按整机取景才看得清姿态。
    /// </summary>
    public PointCloudLayer PointCloud { get; }

    /// <summary>实际加载的机器人模型；未加载到模型时为 null（场景只剩网格与坐标轴）。</summary>
    public RobotModel? Robot { get; private set; }

    /// <summary>实际加载的 URDF 绝对路径；未加载到模型时为 null。</summary>
    public string? ModelPath { get; private set; }

    /// <summary>整机在场景空间的包围盒（米，零位姿态下算得）；没有网格时为 null。</summary>
    public Bounds? MachineBounds { get; private set; }

    /// <summary>装配过程的中文说明（加载了哪个模型、跳过了哪个、为什么）—— 供界面直接显示。</summary>
    public string LoadReport => _log.ToString().TrimEnd();

    /// <summary>
    /// 装配默认场景：空场景图 + 优先加载真机模型。
    /// 模型文件缺失或解析失败都【不抛异常】—— 界面不该因为一个数据文件而白屏，
    /// 失败原因会写进 <see cref="LoadReport"/> 让用户看见。
    /// </summary>
    public static RobotScene CreateDefault()
    {
        var scene = new RobotScene(new SceneGraph());
        scene.LoadRobot();

        // 选中显示不用在这里开：SceneGraph.ShowSelectionAxes 库默认为 true —— 拾取走 Select/PickAndSelect
        // 时，被选节点会自动挂上一枚自己的局部坐标轴（普通子节点，随节点一起动、不参与拾取），
        // 箭头尺寸恒定屏幕大小，SelectionAxesLength（0.3 m）只是箭头几何的参考长度，
        // 对 0.6 m 量级的整机不必调。本工程唯一要守的是「拾取必须走 PickAndSelect」，
        // 见 RobotViewport.PickAt。
        return scene;
    }

    /// <summary>
    /// 把关节角批量写进模型（单位：弧度；长度必须等于 <see cref="RobotModel.DrivableJointCount"/>）。
    /// </summary>
    /// <returns>true = 已应用；false = 无模型或长度不匹配（调用方可根据 false 提示一次，不要每帧刷屏）。</returns>
    public bool ApplyJointValues(IReadOnlyList<float> radians)
    {
        if (Robot is null || radians.Count != Robot.DrivableJointCount)
            return false;

        Robot.ApplyJointValues(radians);
        return true;
    }

    /// <summary>
    /// 用一帧感知点云整帧替换点云图层。
    ///
    /// <para>
    /// <b>只能在场景图属主线程上调用</b>（本工程里就是 <c>RobotViewport</c> 的渲染回调）——
    /// WS 线程递进来的帧要先过那里的邮箱，这是 RobotSimulation 0.3.0 立下的线程契约。
    /// </para>
    /// </summary>
    /// <returns>true = 已落地；false = 帧不自洽（已丢弃并计数）。</returns>
    public bool ApplyPointCloudFrame(PointCloudFrame frame) => PointCloud.Apply(frame);

    /// <summary>
    /// 相机复位：库的默认机位 + 对准整机。
    ///
    /// <para>
    /// 库的默认机位（目标原点、距离 5 m）是给「几米见方」的场景设计的，
    /// 而一台 6 轴机械臂只有 0.8 m 左右 —— 不重新取景的话，整机在面板里只有指甲盖大。
    /// 取景按包围球算，所以换成别的机型（更大或更小）也自动合适。
    /// </para>
    /// </summary>
    public void ResetView()
    {
        Camera camera = Graph.Camera;
        camera.Reset();

        if (MachineBounds is not { } bounds)
            return;

        camera.Target = bounds.Center;

        // 包围球恰好占满视野竖直方向所需距离 = r / sin(FOV/2)，再留 25% 边距。
        // r 取包围盒对角线的一半（Bound 0.1.0 只给 Min/Max/Center/Size）。
        float radius = MathF.Max(bounds.Size.Length() * 0.5f, 0.05f);
        camera.Distance = radius / MathF.Sin(camera.Fov * MathF.PI / 360f) * 1.25f;
    }

    public void Dispose() => Graph.Dispose();

    /// <summary>按「首选 → 兜底」的顺序加载第一个存在的模型，并记录每一步的结果。</summary>
    private void LoadRobot()
    {
        foreach (string relativePath in new[] { DefaultModelRelativePath, FallbackModelRelativePath })
        {
            if (ResolveAssetFile(relativePath) is not { } fullPath)
            {
                _log.AppendLine($"未找到 {relativePath}");
                continue;
            }

            try
            {
                RobotModel robot = RobotModel.ParseFile(fullPath);
                Graph.Add(robot); // 与其它 GameObject 平等入场景；库的场景是 Z-up，基座就在 z=0
                Robot = robot;
                ModelPath = fullPath;
                MachineBounds = ComputeBounds(robot);
                _log.Append($"已加载 {Path.GetFileName(fullPath)}（{robot.DrivableJointCount} 个可驱动关节）");
                return;
            }
            catch (Exception ex)
            {
                // 解析失败要能落到下一个候选，而不是让整个 3D 面板消失。
                _log.AppendLine($"加载失败，改用下一个模型：{relativePath} —— {ex.Message}");
            }
        }

        _log.Append("无可用模型，仅显示网格与坐标轴");
    }

    /// <summary>
    /// 资源文件的绝对路径，不存在则返回 null。
    /// 以 <see cref="AppContext.BaseDirectory"/> 为根，因此程序从哪个工作目录启动都不影响加载。
    /// </summary>
    private static string? ResolveAssetFile(string relativePath)
    {
        string fullPath = Path.GetFullPath(relativePath, Path.Combine(AppContext.BaseDirectory, AssetsFolder));
        return File.Exists(fullPath) ? fullPath : null;
    }

    /// <summary>
    /// 整机包围盒：把每个带网格节点的包围盒 8 个角变换到场景空间后合并。
    /// 只取包围盒角点、不遍历顶点 —— 十万级顶点的网格也不会拖慢启动。
    /// </summary>
    private static Bounds? ComputeBounds(GameObject root)
    {
        var corners = new List<Vector3>(64);
        Collect(root, corners);
        return corners.Count > 0 ? Bounds.FromPoints(corners) : null;

        static void Collect(GameObject node, List<Vector3> into)
        {
            if (node.MeshData is { } mesh)
            {
                Bounds local = mesh.ComputeBounds();
                for (int i = 0; i < 8; i++)
                {
                    into.Add(node.Transform.LocalToWorld(new Vector3(
                        (i & 1) == 0 ? local.Min.X : local.Max.X,
                        (i & 2) == 0 ? local.Min.Y : local.Max.Y,
                        (i & 4) == 0 ? local.Min.Z : local.Max.Z)));
                }
            }

            foreach (Transform child in node.Transform.Children)
                Collect(child.Owner, into);
        }
    }
}
