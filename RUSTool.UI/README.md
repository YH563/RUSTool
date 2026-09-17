# RUSTool.UI — 应用主项目

RUSTool 的**唯一应用**：Avalonia + MVVM。`dotnet run` 起来的就是它；
界面皮肤全部来自项目内的设计系统（`Theme/`），业务能力全部来自 `RUSTool.Core`。

## 文档在哪

| 想知道什么 | 看哪里 |
|---|---|
| 怎么跑 / 怎么拍截图 / 目录结构 / 组装根与数据流 / 已知边界 | [`../docs/ui/zh-CN.md`](../docs/ui/zh-CN.md) |
| 设计系统（令牌 / 语义类 / 控件样式 / 弹层圆角） | [`Theme/README.md`](Theme/README.md) |
| 工程边界与依赖方向（为什么界面不许碰图形栈） | [`../docs/architecture/zh-CN.md`](../docs/architecture/zh-CN.md) |
| 业务层（指令 / 状态机 / 日志契约） | [`../docs/core/zh-CN.md`](../docs/core/zh-CN.md) |
| 构建 SDK 要求与验证清单 | [`../docs/testing/zh-CN.md`](../docs/testing/zh-CN.md) |

## 最快上手

```bash
cd RUSTool.UI
./preview.sh            # 真实窗口（工程师模式，可交互）
./preview.sh clinical   # 真实窗口（临床模式）
./preview.sh all        # 四种界面组合各拍一张 PNG 到 preview/
```

> 目标框架是 `net8.0`，但**构建必须用 .NET SDK 10**（Avalonia 12 的源生成器需要 Roslyn 4.14+，
> 用 SDK 8 会整片报 `CS0103`）。本仓库刻意不放 `global.json`。
