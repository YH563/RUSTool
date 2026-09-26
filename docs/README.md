# RUSTool 文档

> 本仓库文档**只提供中文版本**，与仓库根的 [`../README.md`](../README.md) 共用同一套章节约定（编号章节 + 表格 + 反例）。

文档按模块拆分：每个模块一个目录，正文文件统一叫 `zh-CN.md`，本索引也遵循同一条规则 ——
本页只是索引，模块正文在各自目录里。

| 模块 | 文档 |
|---|---|
| **架构设计** | [`architecture/zh-CN.md`](architecture/zh-CN.md) |
| **`RUSTool.Core`**（纯逻辑层） | [`core/zh-CN.md`](core/zh-CN.md) |
| **bridge 协议** | [`protocol/zh-CN.md`](protocol/zh-CN.md) |
| **`RUSTool.UI`**（界面层） | [`ui/zh-CN.md`](ui/zh-CN.md) |
| **`RUSTool.Visualization`**（3D 图形栈隔离容器） | [`visualization/zh-CN.md`](visualization/zh-CN.md) |
| **`RUSTool.Charts`**（2D 图表栈隔离容器） | [`charts/zh-CN.md`](charts/zh-CN.md) |
| **构建、测试与验证** | [`testing/zh-CN.md`](testing/zh-CN.md) |

## 工程内文档（就近放）

| 文档 | 内容 |
|---|---|
| [`../RUSTool.UI/README.md`](../RUSTool.UI/README.md) | `RUSTool.UI` 工程级说明（导航到本目录） |
| [`../RUSTool.UI/Theme/README.md`](../RUSTool.UI/Theme/README.md) | 设计系统用法：令牌 / 语义类 / 控件样式 / 弹层圆角 |
| [`../RUSTool.Visualization/README.md`](../RUSTool.Visualization/README.md) | `RUSTool.Visualization` 工程级说明（导航到本目录） |
| [`../RUSTool.Charts/README.md`](../RUSTool.Charts/README.md) | `RUSTool.Charts` 工程级说明（导航到本目录） |
| [`../RUSTool.Visualization/Assets/Models/README.md`](../RUSTool.Visualization/Assets/Models/README.md) | 3D 模型资产的布局规则与加载顺序 |

---

## 阅读顺序

1. [`architecture/zh-CN.md`](architecture/zh-CN.md) — 先看工程边界、依赖方向、分层职责与关键设计决策（ADR）。
2. [`core/zh-CN.md`](core/zh-CN.md) — 纯逻辑层的公共面：通信客户端、业务服务、流程状态机、日志契约。
3. [`protocol/zh-CN.md`](protocol/zh-CN.md) — 三条通道、状态帧、请求 / 回执、完整指令表、`/sensor` 点云帧的解码契约（后端联调的契约基准）。
4. [`ui/zh-CN.md`](ui/zh-CN.md) — 界面层：两类使用者的心智模型、指令分层、主题、组装根与数据流、已知边界。
5. [`visualization/zh-CN.md`](visualization/zh-CN.md) — 3D 图形栈隔离容器：数据契约（`RobotViewport`）与日志出口。
6. [`charts/zh-CN.md`](charts/zh-CN.md) — 2D 图表栈隔离容器：曲线行模型、推帧线程契约、空数据态、配色与主题。
7. [`testing/zh-CN.md`](testing/zh-CN.md) — 构建 SDK 要求、单元测试、界面截图与手工验证清单。

> 仓库根入口见 [`../README.md`](../README.md)。
