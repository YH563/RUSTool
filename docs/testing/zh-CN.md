# 构建、测试与验证（简体中文）

> 状态：反映当前实现。配套：[`../README.md`](../README.md)、[`../architecture/zh-CN.md`](../architecture/zh-CN.md)、[`../core/zh-CN.md`](../core/zh-CN.md)、[`../ui/zh-CN.md`](../ui/zh-CN.md)、[`../visualization/zh-CN.md`](../visualization/zh-CN.md)。

本文回答三件事：**用什么 SDK 构建**、**怎么跑测试**、**怎么验证界面与 3D 真的在工作**。

---

## 1. 前提：构建 SDK 必须是 .NET 10

| 项 | 值 |
|---|---|
| 目标框架 | `net8.0`（四个工程统一，跟随 `RobotSimulation` 0.2.1 的 lib 目录） |
| 构建 SDK | **.NET SDK 10**（本机装在 `~/.dotnet`，未加入 PATH） |
| 为什么 | Avalonia 12.1.0 的源生成器要求 Roslyn 4.14+：用 SDK 8 时源生成器加载不上，`InitializeComponent` 不被生成，于是整片报 `CS0103` |
| `global.json` | 刻意**不放** —— 钉了 SDK 版本反而编译不过 |
| 换机器 | `~/.dotnet` 里的 SDK 或系统安装的 .NET 10 都可以，只要 `dotnet --version` ≥ 10 |
| NuGet 源 | 仓库根 `NuGet.config`：`<clear />` 后**只**登记 nuget.org | 不依赖本机离线目录或私有源 —— Linux / Windows / CI 用同一套配置还原；`RobotSimulation` 0.1.0 / 0.2.0 / 0.2.1 都已发布在 nuget.org 上 |

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$PATH:$DOTNET_ROOT"
dotnet --version        # 期望 10.x
```

`RUSTool.UI/preview.sh` 自己会设好上面两行环境变量，所以从脚本入口跑界面不需要手动导出。

**它不是唯一入口**：脚本只做了三件事（设 `DOTNET_ROOT`、把工作目录固定到仓库根、建好 `RUSTool.UI/preview/`）。
Windows（PowerShell / cmd）或已把 `dotnet` 加进 PATH 的机器直接用等价命令，行为完全一致：

```powershell
New-Item -ItemType Directory -Force RUSTool.UI/preview | Out-Null   # 脚本会替你建；--shot 不创建目录
dotnet run --project RUSTool.UI -- --shot RUSTool.UI/preview/02-engineer-dark.png --dark
dotnet run --project RUSTool.UI -- --clinical                        # 真实窗口 · 临床模式
dotnet run --project RUSTool.UI -- --shot RUSTool.UI/preview/05-menu-MenuFile.png --open MenuFile
dotnet run --project RUSTool.UI -- --shot RUSTool.UI/preview/06-engineer-status.png --status   # 展开状态浮层
```

`--shot` 的路径按**当前工作目录**解析（脚本传的是绝对路径，手敲相对路径就要在仓库根执行）。

---

## 2. 构建与单元测试

```bash
# 整解决方案（Core / UI / Visualization / Tests）
dotnet build RUSTool.sln

# 单元测试：只跑纯逻辑层，不需要网络 / 界面 / 显卡
dotnet test tests/RUSTool.Core.Tests
```

CI 里请**串行**执行 `build` / `test` / `run`：它们共用同一份输出目录，
并发跑会互相占用 bin 下的文件（症状是「文件被占用」的构建错误）。

## 3. 界面与 3D 的验证（不需要后端）

```bash
# 1) 真实窗口 + GL：起界面，stderr 会出现 [3d] 就绪 …（含 GPU 与模型加载报告）
RUSTool.UI/preview.sh window

# 2) 临床模式（真实窗口）
RUSTool.UI/preview.sh clinical
RUSTool.UI/preview.sh window --clinical     # 等价写法：参数透传

# 3) 无 GL 时的降级：离屏截图不崩，3D 区显示设计好的空状态
RUSTool.UI/preview.sh dark                  # 只深色那张
RUSTool.UI/preview.sh all                   # 五张一次拍全（工程师 / 临床 × 浅色 / 深色 + 状态浮层展开）

# 4) 展开 3D 视口右上角的机械臂状态浮层（默认收起，静态截图里拍不到那枚按钮的结果）
RUSTool.UI/preview.sh status                # -> preview/06-engineer-status.png

# 5) 弹层（菜单是 Popup，不展开拍不到）
RUSTool.UI/preview.sh popup MenuFile dark
```

截图落在 `RUSTool.UI/preview/`（已在 `.gitignore` 里忽略），
文件名固定为 `01-engineer-light` / `02-engineer-dark` / `03-clinical-light` / `04-clinical-dark` /
`06-engineer-status`（3D 视口右上角状态浮层展开的那一张）。

**离屏截图与真实启动共用同一份组装**（`App.CreateMainViewModel`），
所以预览图里的界面拓扑就是运行时那一份；但离屏渲染**拿不到桌面 GL**，
3D 区必然是空状态 —— 这不是故障，要用真实 GL 看图请用 `preview.sh window`。

### 图形栈日志确实回到了项目日志器

```bash
grep ' sim ' logs/$(date +%F).log
# 例：sim [AssetResolver] 'package://…' → /…/Assets/Models/fairino3_v6/meshes/base_link.STL
```

日志文件写在 `logs/`（相对**进程工作目录**，已在 `.gitignore` 里忽略）。
如果这里一条 `sim` 都没有，检查 `App.CreateMainViewModel` 里 `SimulationLogBridge.Attach` 是否在
建视口**之前**调用 —— 库的日志门面只认第一次初始化。

---

## 4. 单元测试覆盖了什么

当前只有一件事被测：**扫查流程状态机**（`tests/RUSTool.Core.Tests/ScanStateMachineTests.cs`，
11 个测试方法，`[Theory]` 展开后共 **54 个用例**，全部不依赖网络 / 界面 / 图形栈）。

| 用例（方法名就是「什么情况_结果应该是什么」） | 锁住的不变量 |
|---|---|
| `完整流程_从待开始一路走到扫查完成` | 8 个阶段的线性通路与每一步的 `StageChanged` 轨迹 |
| `预扫描结束_回执和异步事件谁先到都能进入位姿阶段` | 回执 / 事件双来源；迟到者是无害自转移 |
| `非法动作被拒绝_阶段保持不变` | 非法转移必须返回 `false` 且**不改状态** |
| `任何阶段收到停止_都回到待开始并清空进度` | `Stop` 通配 + 清空起点 / 终点 |
| `任何阶段收到复位_都回到待开始并清空进度` | `Reset` 通配 |
| `任何阶段收到出错_都进入故障阶段` | `Fail` 通配 → `Faulted` |
| `出错后_只有复位能离开故障阶段_不做重试` | `Faulted` 只能 `Reset` 回 `Idle` |
| `起点和终点都记录好之后才允许开始规划` | 子条件门禁（`PoseReady`） |
| `预扫描完成标志由当前阶段推导` | `PreScanDone` 是派生量，不单独存字段 |
| `只有阶段真的变了才触发事件_自转移不触发` | 自转移不刷界面 |
| `按钮置灰的判断与实际能否触发完全一致` | `CanFire()` 与 `TryFire()` 判定同源（逐阶段 × 逐触发器穷举） |

> 测试方法名刻意用中文写成「什么情况_结果应该是什么」，可以直接当需求文档读。

**还没被覆盖**（属于待补的测试）：

| 对象 | 建议方式 |
|---|---|
| `BridgeProtocol` 的 JSON 编解码 | 用 [`../protocol/zh-CN.md`](../protocol/zh-CN.md) 的样例 JSON：reply / event / state；字段缺省（`null` → `[]`）与坏 JSON（返回 `null`） |
| `BridgeClient.SendAsync` | 注入 fake `ConnectionManager`：id 自增、reply 按 id 匹配、超时返回 `Success=false` |
| 断线行为 | fake 连接模拟断线：未决请求全部失败 + `ConnectionChanged(false)` |

## 5. 手工验证清单（改完代码自检）

| 改动类型 | 至少要做的验证 |
|---|---|
| 任何代码改动 | `dotnet build RUSTool.sln` + `dotnet test tests/RUSTool.Core.Tests` |
| `RUSTool.Core/Communication/` | 起真实后端（或本地 bridge）跑 `preview.sh window`，点「连接」，确认日志里出现 `→ 发送指令` / `← 指令 … 结果` 且状态灯变化 |
| `RUSTool.Core/Services/` | 点动按住 / 松开（`start_jog` / `stop_jog_decel` 成对出现）；急停后确认回到「空闲」 |
| `RUSTool.UI/Theme/` | `./preview.sh all` 看五张截图的配色；`./preview.sh popup MenuFile` 看弹层 |
| `RUSTool.UI/Views/` | `./preview.sh window` 交互一遍受影响的面板；再 `./preview.sh all` 确认布局没塌；动过 3D 视口右上角的覆盖层时再补一张 `./preview.sh status`（浮层展开态） |
| `RUSTool.Visualization/` | `./preview.sh window` 看 stderr 的 `[3d] 就绪 …`（GPU + 模型报告）；再 `./preview.sh dark` 确认无 GL 时降级不崩 |
| 模型资产（`Assets/Models/`） | 检查 `LoadReport` / stderr 里加载的是预期的 URDF（布局规则见该目录 README） |

---

## 6. 常见问题

| 现象 | 原因 / 处理 |
|---|---|
| `dotnet: command not found` | 本机 dotnet 装在 `~/.dotnet` 但未进 PATH：`export PATH="$PATH:$HOME/.dotnet"`，或直接用 `preview.sh` |
| 构建报一大片 `CS0103`（`InitializeComponent` 找不到） | 用了 .NET SDK 8 构建。换 SDK 10；本仓库刻意不放 `global.json`，正是为了避免 SDK 被钉死 |
| 构建报某文件被占用 | 并发的 `build` / `test` / `run` 在写同一份输出目录；串行执行 |
| 截图里 3D 区是空的 | 离屏渲染拿不到桌面 GL，属预期降级；要看 3D 请 `preview.sh window` |
| 截图里菜单没展开 | 菜单是 Popup，必须 `preview.sh popup <菜单名>` |
| 界面读数全是 0、日志空白 | 没连后端。`preview.sh window` → 点「连接」；这是正确行为 |
| `logs/` 里找不到文件 | 日志相对**进程工作目录**写；用 `preview.sh` 时工作目录是仓库根 |
| 状态行的值一直是「空闲 / 已暂停」 | 模式仲裁（`RobotSession.TryEnter*`）尚未接线，见 [`../ui/zh-CN.md`](../ui/zh-CN.md) 第 13.2 节 |
| 点云 / 影像 / 曲线没有数据 | `/sensor` 通道尚未解码、影像与曲线仍是占位，见 [`../ui/zh-CN.md`](../ui/zh-CN.md) 第 13 节 |