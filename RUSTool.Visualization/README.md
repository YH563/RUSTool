# RUSTool.Visualization — 图形栈的隔离容器

这个工程只有一个职责：**让 3D 图形栈（Silk.NET + OpenGL + RobotSimulation）只在这里出现**。
`RUSTool.UI` 引用它，但只认识一个类型：`Controls/RobotViewport.cs`。

```
RUSTool.UI  ──绑定──►  RobotViewport.JointValues   （float 列表，单位弧度）
                              │
                              ▼        ← 到此为止，界面的世界结束
                       RobotScene.Graph（场景图：网格 / 灯光 / 轴 / 机器人）
                              │
                              ▼
                       RobotSimulation.*（Core / Robot / OpenGL）
```

## 数据契约（界面层需要知道的全部）

| 入口 | 类型 | 说明 |
|---|---|---|
| `JointValues` | `IReadOnlyList<float>?`（Avalonia 属性，可绑定） | 关节角，**弧度**，顺序 = URDF 可驱动关节顺序；长度不符则该帧被忽略并只提示一次 |
| `ResetCamera()` | 方法 | 相机回默认机位（菜单「视图 → 重置视角」用） |
| `Ready` / `Failed` | 事件（UI 线程） | GL 就绪报告 / 初始化失败原因。失败时控件自己不画，请把它隐藏让占位层露出 |
| `Stats` / `Picked` | 事件（UI 线程） | 每秒性能行 / 单击拾取结果 |

单位约定与库一致：**弧度 / 米 / Z 轴向上**。界面这一层不做换算 ——
HUD 上给人看的度数是另一条投影（见 `RobotStatusViewModel`）。

## 依赖

| 包 | 版本 | 作用 |
|---|---|---|
| `RobotSimulation.Core` / `.Robot` / `.OpenGL` | 0.1.0 | 场景图 / URDF + 正运动学 / Silk.NET 渲染后端 |
| `Avalonia` | 12.1.0 | `OpenGlControlBase`：给我们一个 GL 上下文和一个 framebuffer |
| `Silk.NET.OpenGL` | 2.23.0 | 把 Avalonia 的过程地址包成 GL 门面 |

**要求桌面 OpenGL**：后端只带 `#version 330 core` 着色器，拿到 GLES（部分平台的 ANGLE / EGL 默认）
会抛 `NotSupportedException` —— 控件会捕获它并走 `Failed` 降级，不会让应用崩掉。

离线安装（无外网时）：包同时放在 `/home/hp/nuget-local-feed`，
把它加进 NuGet 源（`dotnet nuget add source /home/hp/nuget-local-feed -n local`）即可还原。

## 资产

`Assets/Models/` 随编译复制到输出目录，并被引用本工程的 `RUSTool.UI` 一并带上，
所以加载路径固定写 `AppContext.BaseDirectory/Assets/...`，与启动时的工作目录无关。见 `Assets/Models/README.md`。

## 怎么验证（不需要后端）

```bash
# 1) 界面 + GL：真窗口跑起来，stderr 会出现 [3d] 就绪 …（含 GPU 与模型加载报告）
RUSTool.UI/preview.sh window

# 2) 无 GL 时的降级：离屏截图不崩、3D 区显示设计好的空状态
RUSTool.UI/preview.sh dark
```
