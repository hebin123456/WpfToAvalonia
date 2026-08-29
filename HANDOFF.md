# HANDOFF — WpfToAvalonia 增强 + ForkPlus 迁移进度交接

> 最后更新：2026-08-29 · 当前 HEAD：`8a31afb` · 测试 92/92 绿
> 写给接手的 agent：先通读本文档，再动手。所有结论都有实测出处，不要凭印象改。

## 1. 任务全景

用户两个诉求：
1. **增强 WpfToAvalonia 转换工具**（仓库 `hebin123456/WpfToAvalonia`，master），使其能转换用户的真实项目 ForkPlus
2. **最终把 ForkPlus 转换后推送到新仓库 ForkPlus-Next**（需用户确认后执行，尚未做）

## 2. 环境（接手必读）

- .NET SDK：`/opt/dotnet`（非 PATH 默认）。每次命令前 `export DOTNET_ROOT=/opt/dotnet && export PATH=$PATH:/opt/dotnet`
- 工作目录：`/data/user/work/`（沙箱可能重置，丢了就重新 clone + 按下面 commit 重建）
  - `WpfToAvalonia/`：工具源码（git 远程已配好凭证，可直接 push）
  - `ForkPlus/`：原始 WPF 源（git@hebin123456/ForkPlus 之类，只读参考）
  - `ForkPlus.conv/`：转换产物副本（验证用，可随时重建）
  - `xaml-smoke/`：XAML 冒烟验证工程（见 §5 方法论）
  - `scratch2/`：Avalonia 12 反射探针（csproj 引 Avalonia 12.1.1 包，直接 dotnet run）
- ForkPlus 原始仓库地址问用户或看 `ForkPlus/.git/config`

## 3. 已完成（按 commit 顺序，全部已推送 master）

| commit | 内容 | 实测效果 |
|---|---|---|
| `2943f01` | 空间重置后重建：Roslyn 重写器主体、SDK、git 凭证 | 恢复基线 |
| `4855351` | 虚方法覆盖单测 + 分隔符 trivia 修复 | 85 绿 |
| `455c6a9` | WPF 独有特性整条删除：ValueConversion/ContentProperty/AssemblyAssociatedContentFile（KnownMaps.RemovedAttributes + VisitAttributeList 末段名匹配，非 WPF 前缀限定同名不误删） | CS0246 特性类 16 处清零，88 绿 |
| `d9c5db6` | **去 override 化**：CS-OVERRIDE-DEOVERRIDE（OverrideMethodManualNotes 方法降级普通方法）+ CS-BASE-REMOVED（基类是 AvaloniaControlBaseNames 时删 base.Xxx 语句，用户中间基类保留调用）+ StartupEventArgs/ExitEventArgs→object + ControlTemplate→IControlTemplate + Application 入基类集合 | **CS0115 94→0，错误行 302→186**，92 绿 |
| `8a31afb` | XAML：TryGetMatcherSegment 类型前缀属性取末段（`CalendarButton.IsInactive`→`[IsInactive=]`，带点名在匹配器语法非法）+ 裸 ControlTemplate.Triggers 注释移除（XAML-TEMPLATE-TRIGGER-ORPHAN） | 冒烟 AVLN2000 Trigger 544 处清零 |

## 4. 关键文件地图（改动热点）

```
src/WpfToAvalonia.Core/
├── Xaml/KnownMaps.cs          # 全部映射表：TypeRenames/ElementRenames/QualifiedTypeRenames/
│                              #   WpfOnlyTypes/OverrideMethodManualNotes/AvaloniaControlBaseNames/
│                              #   RemovedAttributes/TryGetMatcherSegment/TryGetPseudoClass...
├── Xaml/XamlTransformer.cs    # XAML 转换主体：VisitElement 逐元素处理、Style→ControlTheme、
│                              #   触发器→伪类选择器、UnwrapAdornerDecorator、ReplaceWithComment
├── CSharp/WpfCSharpRewriter.cs # Roslyn 重写器：RewriteQualified/VisitMemberAccessExpression/
│                              #   VisitMethodDeclaration(去override化)/VisitExpressionStatement/
│                              #   VisitAttributeList(特性删除)/WithoutOverride/WithAccess
├── MsBuild/ProjectFileTransformer.cs # csproj：quarantine 包隔离注释、frameworkPackages 注入
└── ProjectConverter.cs        # 流水线编排：两遍式 XAML、DetectFrameworkPackages 传入 csproj
tests/WpfToAvalonia.Tests/     # 92 个测试；Core 有 InternalsVisibleTo 给测试
```

**坑**：`Note()` 必须在重赋值前的原 node 上调用（新节点位置丢失 → 去重键撞同行吞提示，d9c5db6 修过）。

## 5. 验证方法论（照此复现）

1. **端到端**：`cp -r ForkPlus ForkPlus.conv && cd ForkPlus.conv && dotnet <CLI>/wpf2ava.dll convert ForkPlus.sln --report conv-report.md`，然后 `dotnet build src/ForkPlus/ForkPlus.csproj` 统计错误
2. **XAML 冒烟**（当前关键路径）：A/B 类 C# 错误阻断 Avalonia XAML 编译器启动。解法：69 个无 x:Class 纯资源 axaml 可在独立工程编译。`xaml-smoke/` 工程（AssemblyName=ForkPlus 使 `avares://ForkPlus/Theme/...` 可解析，引 Avalonia+DataGrid+FluentTheme 包，App.axaml 资源链引入 Theme/Generic.axaml）→ `dotnet build` 看 AVLN 错误
3. **反射探针**：`scratch2/` 加代码跑 `dotnet run -c Release`，验证 Avalonia 12 API 真实存在性（已验证：无 ControlTemplate 类、Application 无 OnStartup/OnExit 虚方法、17 个 WPF 虚方法族 ABSENT）
4. 排噪声：冒烟工程无 C# 代码，`Unable to resolve type ForkPlus.X`（x:Class 用户控件）是预期噪声，`grep -v ForkPlus` 排除后才是真实缺口

## 6. 当前错误清单（冒烟 XAML，最近一次聚类）

排除 ForkPlus 用户类型噪声后的真实缺口（AVLN2000）：
- **BitmapImage**：转换器有映射但部分语境漏（查 `Theme/` 内残留形态）
- **Condition / MultiDataTrigger**：多条件触发器未处理（WPF `<Condition Property=...>`）
- **unknown property Name / ToolTip**：属性级差异（ElementName 绑定？ToolTipService?）需看具体行
- 已清零：Trigger 544（裸模板 Triggers 注释化）、带点属性名 AVLN2201

C# 侧（186 行，全部 A/B 类人工项）：OxyPlot 48 / WinRT 20 / WindowsAPICodePack 4（A 类换库）；RequestNavigateEventArgs 24 / RoutedEventHandler 14 / Adorner 18 等（B 类改写）。

## 7. 剩余任务（优先级序）

1. **继续修冒烟 XAML 缺口**：BitmapImage 漏转语境、MultiDataTrigger/Condition、Name/ToolTip 属性差异——每修一类跑 `dotnet test` + 冒烟 build + git push（小步快推！）
2. XAML 干净后，把 x:Class 页面也纳入验证（需把 ForkPlus.conv 的 C# 排除噪声文件后整体编译）
3. 更新 CLI/README 文档（规则清单）
4. 打 tag `v0.2.0` 推送
5. （用户确认后）正式转换 ForkPlus → push ForkPlus-Next master

## 8. 用户偏好

- 中文交流；小步提交推送（用户积分有限，不要攒大招）
- 结论必须实测驱动（编译/反射验证），映射表注释里都写了出处
- A 类（换库）B 类（架构改写）不自动处理，输出人工指引——这是产品定位，别越界
