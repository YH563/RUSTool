# RUSTool — 项目架构

## 顶层目录结构

```
RUSTool/
├── Models/              ← 领域数据模型（纯数据结构，无行为）
│                          · RobotState.cs — 机器人状态（关节角、位姿等）
│
├── Communication/       ← 外部通信层（所有与后端/外设的通信协议）
│                          · IProtocolClient.cs     — 通信层接口
│                          · WebSocketClient.cs      — WebSocket 实现
│                          · CommandRequest.cs       — 请求报文
│                          · CommandResponse.cs      — 响应报文
│
├── Services/            ← 业务服务层（指令编排、业务逻辑）
│                          · ICommandService.cs      — 命令服务接口
│                          · CommandService.cs       — 命令服务实现
│
├── Data/                ← 数据库 / 持久化层（占位）
│
├── Visualization/       ← 3D 仿真 / 图表可视化（占位）
│
├── Infrastructure/      ← 基础设施（日志、配置、DI 容器等，占位）
│
├── ViewModels/          ← 视图模型（Avalonia MVVM）
│   └── Robot/
│       └── RobotViewModel.cs
│
├── Views/               ← 视图（Avalonia AXAML）
│   └── MainWindow.axaml
│
├── Docs/                ← 文档
│   └── websocket_api.md
│
├── Assets/              ← 静态资源（图标等）
│
├── App.axaml            ← 应用入口
└── Program.cs
```

## 分层调用关系

```
View  ← 绑定 →  ViewModel
                     ↓ 依赖接口
               ICommandService        (Services/)
                     ↓
               CommandService         (Services/)
                     ↓ 依赖接口
               IProtocolClient        (Communication/)
                     ↓
               WebSocketClient        (Communication/)
                     ↓
              后端服务器 (WebSocket)
```

## 各层职责

| 层 | 职责 | 示例 |
|---|---|---|
| **Models** | 纯领域数据，无行为 | `RobotState`、后续的工件模型、工艺参数等 |
| **Communication** | 与外部系统通信 | WebSocket 客户端、gRPC 客户端、HTTP 客户端 |
| **Services** | 业务逻辑编排 | 运动指令服务、标定服务、日志服务 |
| **Data** | 数据库/持久化 | SQLite、PostgreSQL 仓储实现 |
| **Visualization** | 3D 场景、数据图表 | 机器人仿真场景、实时曲线 |
| **Infrastructure** | 跨切面基础设施 | 日志、配置、IoC 容器、异常处理 |
| **ViewModels** | UI 状态与命令 | 各功能的 ViewModel |
| **Views** | 界面呈现 | AXAML 文件 |

## 扩展指南

```
新功能 → 看属于哪一层，放在对应的目录下：
  · 新增协议（gRPC）      → Communication/GrpcClient.cs
  · 新增业务服务（标定）    → Services/CalibrationService.cs
  · 新增数据模型（工件）    → Models/Workpiece.cs
  · 新增数据库（SQLite）   → Data/ 下
  · 新增 3D 仿真窗        → Visualization/ + ViewModels/ + Views/
```
