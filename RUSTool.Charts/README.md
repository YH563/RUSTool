# RUSTool.Charts — 2D 图表栈的隔离容器

本工程只有一个职责：**让 2D 图表栈（LiveCharts 2 + SkiaSharp）只在这里出现**。
`RUSTool.UI` 引用它，但只认识三个契约：`Controls/RobotStatePanel.axaml`（控件）、
`Data/RobotStateRow.cs`（一行的绑定源）与 `Data/RobotStateRows.cs`（建行 / 推帧 / 清空的静态入口）。

界面侧因此不出现任何 `LiveChartsCore.*` / `SkiaSharp`（曲线配色、坐标轴、主题跟随都跟着曲线走）。

## 文档在哪

| 想知道什么 | 看哪里 |
|---|---|
| 数据契约 / 线程契约 / 配色与主题 / 依赖与版本约束 / 约定的反例 | [`../docs/charts/zh-CN.md`](../docs/charts/zh-CN.md) |
| 界面侧接线（谁推帧、卡片外壳长什么样） | [`../docs/ui/zh-CN.md`](../docs/ui/zh-CN.md) 第 11 节 |
| 怎么拍一张曲线截图（不需要后端） | [`../docs/testing/zh-CN.md`](../docs/testing/zh-CN.md) 第 3 节（`preview.sh torque`） |
| 为什么它必须独立存在（与 3D 隔离同一条规矩） | [`../docs/architecture/zh-CN.md`](../docs/architecture/zh-CN.md) 第 3 节 |
