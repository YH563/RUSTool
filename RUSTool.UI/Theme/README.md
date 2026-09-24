# RUSTool.UI/Theme —— 设计系统

RUSTool 的设计系统，位于应用项目 `RUSTool.UI` 内（原独立工程 `RUSTool.Theme` 已并入）。
**只包含 XAML 资源，不产出任何 C# 类型**，因此不会给界面层引入任何代码耦合。

## 快速开始

### 1. 位置

主题是应用项目的一部分，**不需要任何 `ProjectReference`** —— Avalonia 默认把项目下的
`*.axaml` 收进程序集，直接按 `avares://RUSTool.UI/Theme/...` 引用即可。

```
RUSTool.UI/Theme/
├── Theme.axaml        唯一入口（必须排在 FluentTheme 之后加载）
├── Tokens/            Palette / Metrics / Typography / Semantic
├── Bridges/Fluent.axaml 把 Fluent 的资源键重定向到语义色
└── Controls/          Base / Buttons / Menus / Overlays
```

### 2. 在 `App.axaml` 里加载

**顺序不能反**：`FluentTheme` 提供控件模板，本主题负责把它们重新上色。

```xml
<Application.Styles>
    <FluentTheme />
    <StyleInclude Source="avares://RUSTool.UI/Theme/Theme.axaml" />
</Application.Styles>
```

本主题【有意】不包含 `FluentTheme` —— 主题不该替使用方决定"底层模板从哪来"。

### 3. 处理旧的皮肤（关键）

Avalonia 里**后加载的样式覆盖先加载的**。所以如果 `App.axaml` 里还留着旧皮肤：

```xml
<StyleInclude Source="Styles/CustomTheme.axaml" />   <!-- 13KB 自绘皮肤 -->
<SukiUI ... />
```

它们必须排在 `<StyleInclude Source="avares://RUSTool.UI/Theme/Theme.axaml" />` **之前**，
否则旧样式会把本主题的颜色盖回去，表现为"改了没效果"。

最干净的做法是直接删掉旧皮肤文件 —— 本主题已经覆盖了按钮、输入框、菜单、列表、
滚动条、滑块、勾选、提示气泡等全部控件，不存在"删了就没样式"的缺口。

### 4. 深浅色

```xml
<Application RequestedThemeVariant="Dark">          <!-- 全局 -->
```
或按窗口切换：
```xml
<Window RequestedThemeVariant="Light">
```

两套配色都在 `Tokens/Semantic.axaml` 里写好了，控件一律用
`{DynamicResource XxxBrush}` 引用，切主题时自动跟随，**不需要任何代码**。

## 怎么用

### 按钮变体

```xml
<Button Content="连接"     Classes="accent" />     <!-- 主操作 -->
<Button Content="急停"     Classes="danger" />     <!-- 危险 / 急停 -->
<Button Content="急停恢复" Classes="success" />    <!-- 确认 / 复位 -->
<Button Content="关闭设备" Classes="ghost" />      <!-- 工具条 / 图标按钮 -->
<Button Content="小主色"   Classes="accent sm" />  <!-- 叠加尺寸类 -->
```

尺寸：`sm`(26) / 默认(32) / `lg`(38)。高度由 `MinHeight` 统一，
不会再出现并排按钮差几像素的情况。

### 文字语义类

```xml
<TextBlock Classes="pageTitle"    Text="扫查工作流" />
<TextBlock Classes="sectionTitle" Text="机械臂指令" />
<TextBlock Classes="caption"      Text="单位: mm" />
<TextBlock Classes="secondary"    Text="未连接" />
<TextBlock Classes="mono"         Text="12.500" />        <!-- 等宽，数字不抖 -->
<TextBlock Classes="metric"       Text="36.5" />          <!-- 大读数 -->
<TextBlock Classes="success|warning|danger|accent" Text="…" />
```

### 状态灯 / 分隔线

```xml
<Ellipse Classes="dot info" />          <!-- info|success|warning|danger|idle -->
<Rectangle Classes="divider" />         <!-- 横线 -->
<Rectangle Classes="dividerVertical" /> <!-- 竖线 -->
```

## 结构

四层，只有第 ① 层允许写 hex 颜色：

| 层 | 文件 | 职责 |
|---|---|---|
| ① 原始色阶 | `Tokens/Palette.axaml` | 唯一允许出现 `#RRGGBB` 的地方 |
| ② 语义色 | `Tokens/Semantic.axaml` | Light/Dark 各一套语义色，控件只引用它 |
| ③ Fluent 桥接 | `Bridges/Fluent.axaml` | 把 Fluent 的资源键重定向到 ② |
| ④ 控件样式 | `Controls/*.axaml` | 补 Fluent 没有的能力（按钮变体、文字类、菜单、浮层） |

另外 `Tokens/Metrics.axaml`（间距/圆角/尺寸）、`Tokens/Typography.axaml`（字体/字号）。

换配色 = 改 ① 和 ②，控件样式一行都不用动。

## 默认文字色：靠继承，不写进样式

**这是本主题最容易踩的第二个坑。**

`Controls/Base.axaml` 里**故意不给** `TextBlock` 设 `Foreground`：默认文字色由 `Window` 上的
`Foreground`（`TextPrimaryBrush`）**继承**下来，`TextBlock` 的默认样式只管字号 / 换行 / 对齐。

原因是 Avalonia 的优先级顺序：**应用级 `Style` 的 setter 高于继承值（Inheritance）**。
一旦给所有 `TextBlock` 写死 `Foreground`，按钮里那些由 `ContentPresenter` 现场生成的
`TextBlock` 就再也拿不到按钮自己的 `Foreground`：

| 症状 | 机制 |
|---|---|
| 红底急停按钮上是深色字（不是白字） | `Button` 主题设了 `Foreground=TextOnAccentBrush`，被全局 `TextBlock` 样式盖掉 |
| 分段按钮（真实 / 仿真）选中的那个看不出主色 | 同上：选中色写在按钮上（`Class="segOn"`），被全局样式截走 |
| `Classes="danger|accent"` 的文字类看着没生效 | 同上（这类是 `TextBlock` 自己的样式，不冲突，但容易连带怀疑） |

所以：**要改某块文字的颜色，就在它自己（或最近的祖先 `Window` / `Panel`）上设 `Foreground`**，
不要在全局样式里兜底。自检方法很直接 ——
`Theme/Controls/Base.axaml` 里 `Style Selector="TextBlock"` 的 `Setter` 列表里
**不该出现任何 `*Brush`**。

改完颜色的回归验证：`./preview.sh all` 后逐张看**彩色按钮上的字是不是白的**
（急停 `danger`、主操作 `accent`、分段按钮选中态），深浅两套主题都要看。

## 弹层圆角：为什么默认是 0

**这是本主题最容易踩的坑，值得单独说明。**

弹层（菜单、ComboBox 下拉、Flyout、ToolTip）在桌面上是**独立窗口**，
它四周的圆角和阴影本质上依赖窗口的 alpha 通道被**合成器（compositor）**合成：

- **有合成器**（picom / mutter / kwin）→ 圆角是圆的、阴影是柔的；
- **没有合成器** → alpha 被直接丢弃，透明区渲染成**纯黑** →
  菜单/下拉的四个角出现黑边，看起来像"边框发霉"。

查自己有没有合成器：

```bash
xprop -root _NET_WM_CM_S0        # 有输出 = 有合成器；"not found" = 没有
```

所以本主题默认把两个令牌设成"安全值"：

```xml
<!-- Tokens/Metrics.axaml -->
<CornerRadius x:Key="RadiusOverlay">0</CornerRadius>     <!-- 弹层圆角 -->
<BoxShadows  x:Key="ShadowOverlay">none</BoxShadows>     <!-- 弹层阴影 -->
```

**确认有合成器之后**，改成下面这样，即可拿回完整的 Win11 观感：

```xml
<CornerRadius x:Key="RadiusOverlay">8</CornerRadius>
<BoxShadows  x:Key="ShadowOverlay">0 2 4 0 #140F172A, 0 8 16 0 #260F172A</BoxShadows>
```

只改这两行就够了 —— 菜单、下拉、Flyout 的圆角和阴影都从这里取值。

## 预览

主题样式直接反映在应用界面上，用应用自己的脚本截图即可（见 `RUSTool.UI/preview.sh`）：

```bash
cd RUSTool.UI
./preview.sh                   # 打开真实窗口，可交互
./preview.sh light             # 离屏渲染浅色  -> preview/01-engineer-light.png
./preview.sh dark              # 离屏渲染深色  -> preview/02-engineer-dark.png
./preview.sh clinical-light    # 临床模式 · 浅色 -> preview/03-clinical-light.png
./preview.sh clinical-dark     # 临床模式 · 深色 -> preview/04-clinical-dark.png
./preview.sh all               # 四张一次拍全
./preview.sh popup MenuFile    # 展开弹层后截图（菜单默认拍不到）
```

> 原先的主题对照画廊（`tools/RUSTool.Theme.Gallery`，带 `hover` / `dump` 检视子命令）
> 已随 `tools/` 目录一并删除；改样式后用上面的截图对比即可。

预览图是本地产物，已在 `.gitignore` 里忽略。
