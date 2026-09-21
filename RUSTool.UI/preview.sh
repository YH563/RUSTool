#!/usr/bin/env bash
#
# RUSTool.UI —— 一条命令跑应用 / 拍界面截图。
#
#   ./preview.sh                  打开真实窗口（工程师模式，可交互）
#   ./preview.sh clinical         真实窗口 + 临床模式
#   ./preview.sh window [参数…]   真实窗口 + 透传参数（如 window --clinical）
#
#   ./preview.sh light            工程师模式 · 浅色截图  -> preview/01-engineer-light.png
#   ./preview.sh dark             工程师模式 · 深色截图  -> preview/02-engineer-dark.png
#   ./preview.sh clinical-light   临床模式 · 浅色截图    -> preview/03-clinical-light.png
#   ./preview.sh clinical-dark    临床模式 · 深色截图    -> preview/04-clinical-dark.png
#   ./preview.sh status           工程师模式 · 展开机械臂状态浮层
#                                                      -> preview/06-engineer-status.png
#   ./preview.sh all              以上五张一次拍全
#
#   ./preview.sh popup <菜单名> [dark]
#                                 展开菜单后截图。菜单栏下拉本身是 Popup，
#                                 静态截图里默认不存在，不展开就永远拍不到。
#                                 菜单名取自 MainWindow.axaml 的 x:Name：
#                                 MenuFile / MenuConnect / MenuView / MenuHelp
#
# 为什么要有这个脚本：dotnet 装在 ~/.dotnet，而 ~/.bashrc 只导出了 DOTNET_ROOT、
# 没加进 PATH，直接敲 dotnet 会"未找到命令"；另外截图路径是相对当前目录解析的，
# 手敲容易把图落到意料之外的地方。这里统一固定好。
set -euo pipefail

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$PATH:$DOTNET_ROOT"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/RUSTool.UI"
PREVIEW="$PROJECT/preview"

mkdir -p "$PREVIEW"
cd "$ROOT"

run() { dotnet run --project "$PROJECT" -- "$@"; }

case "${1:-window}" in
    window)
        shift || true
        run "$@"
        ;;
    clinical)
        run --clinical
        ;;
    light)
        run --shot "$PREVIEW/01-engineer-light.png"
        ;;
    dark)
        run --shot "$PREVIEW/02-engineer-dark.png" --dark
        ;;
    clinical-light)
        run --shot "$PREVIEW/03-clinical-light.png" --clinical
        ;;
    clinical-dark)
        run --shot "$PREVIEW/04-clinical-dark.png" --clinical --dark
        ;;
    status)
        run --shot "$PREVIEW/06-engineer-status.png" --status
        ;;
    all)
        run --shot "$PREVIEW/01-engineer-light.png"
        run --shot "$PREVIEW/02-engineer-dark.png" --dark
        run --shot "$PREVIEW/03-clinical-light.png" --clinical
        run --shot "$PREVIEW/04-clinical-dark.png" --clinical --dark
        run --shot "$PREVIEW/06-engineer-status.png" --status
        ;;
    popup)
        name="${2:-MenuFile}"
        if [ "${3:-}" = "dark" ]; then
            run --shot "$PREVIEW/05-menu-$name-dark.png" --dark --open "$name"
        else
            run --shot "$PREVIEW/05-menu-$name.png" --open "$name"
        fi
        ;;
    *)
        echo "未知参数：$1" >&2
        echo "可用：window [参数…] | clinical | light | dark | clinical-light | clinical-dark | status | all | popup <菜单名> [dark]" >&2
        exit 2
        ;;
esac
