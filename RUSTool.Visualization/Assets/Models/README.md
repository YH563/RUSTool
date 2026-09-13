# 测试模型（`Assets/Models`）

`Scene/RobotScene.cs` 按「首选 → 兜底」的顺序加载第一个存在的模型，
所以本目录决定了「clone 下来跑一下能看到什么」。

| 路径 | 来源 | 覆盖内容 |
|---|---|---|
| `fairino3_v6/` | 与 RobotSimulation 仓库同一份（自带，非下载） | 真机 6 轴机械臂：完整关节树 + 7 个 STL mesh，关节名 `j1 … j6`。URDF 里引用的是 `package://rus_sim_driver/meshes/…`，顺带验证了包名前缀剥离与资源解析（包名与目录名刻意不一致） |
| `primitives.urdf` | 与 RobotSimulation 仓库同一份（手写，非下载） | URDF 内置几何体（`box` / `sphere` / `cylinder` / `capsule`），6 KB；真机模型缺失时用它兜底，保证界面里永远有东西可看 |

许可：两份数据都随 RobotSimulation 仓库分发，与那部分的许可一致；本目录不再单独声明。

## 布局规则（唯一会静默出问题的地方）

URDF 必须**直接躺在自己的包目录**里（`fairino3_v6/fairino3_v6.urdf`），mesh 放在同级 `meshes/`。
不要多套一层 `urdf/`：`package://…/meshes/x.STL` 会解析不到，
症状是「模型加载成功但场景里什么都没有」。

## 加载顺序（`RobotScene.LoadRobot`）

1. `Models/fairino3_v6/fairino3_v6.urdf` —— 首选；缺失或解析失败会往 `LoadReport` 写一行原因；
2. `Models/primitives.urdf` —— 兜底；
3. 都没有 —— 场景只剩网格与世界坐标轴，报告里写明原因。

任何一步失败都**不抛异常**：3D 面板不该因为一个数据文件而消失。

## 加模型 / 换模型

把包丢进本目录（保持上面的布局），改 `Scene/RobotScene.cs` 顶部的
`DefaultModelRelativePath` 一个常量即可；工程文件里的 `Assets\**\*` 复制规则不用动。
