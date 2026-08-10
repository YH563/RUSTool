# RUSTool — 项目架构

## 顶层目录结构

```
RUSTool/
├── Communication/       ← 前端通信客户端（连 bridge 的那一层）
│                          · BridgeClient.cs        — 对外门面（唯一公共入口，供 UI/VM 调用）
│                          · ConnectionManager.cs   — 连接建立 / 收发循环 / 断线重连 / 退避
│                          · BridgeProtocol.cs      — 消息模型 + JSON 编解码（纯函数）
│                          · ProtocolConstants.cs   — Channels / Commands / Events 常量
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
│   ├── avalonia_client_design.md  — 通信客户端设计
│   └── websocket_api.md           — WebSocket 协议
│
├── Assets/              ← 静态资源（图标等）
│
├── App.axaml            ← 应用入口
└── Program.cs
```

## 分层调用关系

```
View  ← 绑定 →  ViewModel
                      ↓
               BridgeClient          (Communication/)
                      ↓
              ConnectionManager      (Communication/)
                      ↓
           后端 bridge (WebSocket：/control /state /sensor)
```

## 各层职责

| 层 | 职责 | 示例 |
|---|---|---|
| **Communication** | 与 bridge 通信：指令下发/回执匹配、状态流接收、断线重连 | `BridgeClient`、`ConnectionManager`、`BridgeProtocol` |
| **Data** | 数据库/持久化 | SQLite、PostgreSQL 仓储实现 |
| **Visualization** | 3D 场景、数据图表 | 机器人仿真场景、实时曲线 |
| **Infrastructure** | 跨切面基础设施 | 日志、配置、IoC 容器、异常处理 |
| **ViewModels** | UI 状态与命令 | 各功能的 ViewModel |
| **Views** | 界面呈现 | AXAML 文件 |

## 扩展指南

```
新功能 → 看属于哪一层，放在对应的目录下：
  · 新增指令        → Communication/ProtocolConstants.cs 补 Commands 常量
  · 新增事件        → Communication/ProtocolConstants.cs 补 Events 常量
  · /sensor 二进制帧 → BridgeProtocol 增加解码方法（不影响现有结构）
  · 新增业务服务（标定）→ 直接复用 BridgeClient 或在 ViewModel 层编排
  · 新增数据库（SQLite）→ Data/ 下
  · 新增 3D 仿真窗        → Visualization/ + ViewModels/ + Views/
```
