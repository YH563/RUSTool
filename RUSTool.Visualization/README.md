# RUSTool.Visualization — 图形栈的隔离容器

本工程只有一个职责：**让 3D 图形栈（Silk.NET + OpenGL + RobotSimulation）只在这里出现**。
`RUSTool.UI` 引用它，但只认识两个契约：`Controls/RobotViewport.cs`（控件，数据入口）与
`Logging/ISimulationLogSink.cs`（日志出口）。

## 文档在哪

| 想知道什么 | 看哪里 |
|---|---|
| 数据契约 / 日志出口 / 依赖 / 资产 / 约定的反例 | [`../docs/visualization/zh-CN.md`](../docs/visualization/zh-CN.md) |
| 3D 模型资产的布局规则与加载顺序 | [`Assets/Models/README.md`](Assets/Models/README.md) |
| 3D 是怎么嵌进 Avalonia 的（跨线程邮箱、降级策略） | [`../docs/architecture/zh-CN.md`](../docs/architecture/zh-CN.md) 第 6 节 |
| 为什么它必须独立存在 | [`../docs/architecture/zh-CN.md`](../docs/architecture/zh-CN.md) 第 3 节 |
