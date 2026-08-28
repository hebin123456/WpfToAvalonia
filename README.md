# WPF → Avalonia 转换工具（wpf2ava）

面向 .NET 10 + Avalonia 12 的 WPF 工程自动迁移工具。XAML 用 XML DOM 精确改写，C# 用 **Roslyn AST**（`CSharpSyntaxRewriter`）语义级重写，csproj 与启动引导同步生成，并输出一份分级报告（INFO / WARN / TODO）指导人工收尾。

```
src/WpfToAvalonia.Core          转换内核（可独立引用做批处理/VS 扩展）
  ├─ Xaml/XamlTransformer.cs    XAML 结构转换（命名空间/控件/属性/样式/触发器/资产 URI）
  ├─ Xaml/KnownMaps.cs          WPF→Avalonia 映射规则表
  ├─ CSharp/WpfCSharpRewriter.cs  Roslyn AST 重写器（using/类型/事件/依赖属性/调度器…）
  ├─ CSharp/CSharpTransformer.cs  解析→重写→using 补全 流水线
  ├─ MsBuild/ProjectFileTransformer.cs  csproj 去 WPF 化 + 挂 Avalonia 包
  ├─ Bootstrap/BootstrapGenerator.cs    Program.cs / App 启动引导生成
  ├─ ProjectConverter.cs        单工程转换编排（含跨文件协调）
  └─ Model/                     转换选项 / 注记 / 报告
src/WpfToAvalonia.Cli           命令行入口
tests/WpfToAvalonia.Tests       28 个规则级单元测试
samples/WpfShop                 典型 WPF 样例（MVVM + 样式 + DataGrid + 自定义控件）
```

## 使用

### 获取

从 [Releases](https://github.com/hebin123456/WpfToAvalonia/releases) 下载对应平台包（win-x64 / win-arm64 / linux-x64 / linux-arm64 / osx-x64 / osx-arm64，自包含单文件，解压即用），或源码构建：

```bash
dotnet build src/WpfToAvalonia.Cli -c Release

# 单工程 / 解决方案 / 目录（自动发现 csproj）
dotnet run --project src/WpfToAvalonia.Cli -- convert <路径> [选项]
```

打包为 dotnet 全局工具（命令名 `wpf2ava`）：

```bash
dotnet pack src/WpfToAvalonia.Cli -c Release
dotnet tool install -g wpf2ava --add-source src/WpfToAvalonia.Cli/nupkg
wpf2ava <路径> [选项]
```

| 选项 | 说明 |
|---|---|
| `--tfm <tfm>` | 目标框架，默认 `net10.0` |
| `--avalonia <ver>` | Avalonia 包版本，默认 `12.1.1` |
| `--dry-run` | 只分析输出 TODO 清单，不写任何文件 |
| `--no-backup` | 不生成 `{项目名}.wpf-backup` 备份 |
| `--no-bootstrap` | 不生成 Program.cs / App 引导 |
| `--no-inter-font` | 不添加 Avalonia.Fonts.Inter |
| `--report <path>` | 报告路径，默认转换目录下 `wpf2avalonia-report.md` |

转换后：`dotnet build` 修掉报告中的 WARN/TODO 即可运行。

## 转换能力总览

### XAML（XML DOM 结构级转换）

- 命名空间 URI 整体替换（presentation → avaloniaui），`.xaml` → `.axaml`
- 控件/属性映射：`Label`→`TextBlock`（Content→Text）、`Page`→`UserControl`、`ToolTipService.ToolTip`→`ToolTip.Tip`、`WindowStyle=None`→`SystemDecorations=None` 等
- 鼠标事件 → 指针事件（`MouseDown`→`PointerPressed`…），右键合并会提示用 `e.GetCurrentPoint(...).Properties.IsRightButton` 区分
- 样式体系：`Style TargetType` → `Selector`；**keyed 样式 → `Selector="Type.key"` 类选择器，引用处 `Style="{StaticResource key}"` → `Classes="key"`**；属性触发器 → `^:pointerover` / `^:pressed` / `^:disabled` 嵌套伪类样式；含 `Template` 的样式 → `ControlTheme`（TargetType 定位）
- 样式位置修正：Resources 内的样式迁移到宿主 `.Styles`；纯样式字典文件根元素 → `Styles`，引用处 `ResourceInclude` → `StyleInclude`
- `ResourceDictionary Source` → `ResourceInclude`；`pack://application,,,/Asm;component/...` → `avares://Asm/...`
- `Application.StartupUri` 移除并生成启动引导；自动补 `<FluentTheme />`；使用 DataGrid 时注入 `Avalonia.Controls.DataGrid` 主题（控件在 Avalonia 11+ 中保持默认命名空间）
- 绑定清理：移除 `UpdateSourceTrigger` / `ValidatesOn*` / `IsAsync` 等 WPF 特有选项

### C#（Roslyn AST 重写）

- `using` 映射（`System.Windows.*` → `Avalonia.*`），并按使用到的类型自动补 `using`
- 类型重写：`DependencyProperty`→`AvaloniaProperty`、`BitmapImage`→`Bitmap`、`MouseEventArgs`→`PointerEventArgs`、`Point/Thickness/Brushes` 全限定形式等
- 依赖属性声明整段重写：`DependencyProperty.Register(name, typeof(T), typeof(O))` → `AvaloniaProperty.Register<O, T>(name)`（含默认值与附加属性；回调/Coerce 提示 TODO）
- 事件订阅重命名（`btn.MouseLeftButtonDown +=` → `btn.PointerPressed +=`）
- 常用 API：`Application.Current.Dispatcher`→`Dispatcher.UIThread`、`Dispatcher.BeginInvoke`→`Post`、`Keyboard.Focus(x)`→`x.Focus()`、`Window.GetWindow`→`TopLevel.GetTopLevel`、`DialogResult = v`→`Close(v)`、`Application.Current.Shutdown()`→lifetime 版本、`new BitmapImage(uri)`→`new Bitmap(AssetLoader.Open(uri))`
- 无法等价转换的 API（MessageBox、文件对话框、Clipboard、VisualTreeHelper、Mouse.GetPosition 等）逐处记录 TODO 并附 Avalonia 替代写法

### 工程与引导

- csproj：去 `UseWPF`/`UseWindowsForms`、TFM→net10.0、`Resource`→`AvaloniaResource`、移除 `Page`/`ApplicationDefinition`、挂 Avalonia 包（按需加 DataGrid/Inter）、`AvaloniaUseCompiledBindingsByDefault=false` 保持 WPF 反射绑定语义
- 生成 `Program.cs`（`[STAThread] Main` + `AppBuilder.UsePlatformDetect`）；改造 `App.axaml.cs`（`OnFrameworkInitializationCompleted` 创建 StartupUri 对应主窗口）
- `.xaml.cs` → `.axaml.cs` 保持 IDE 配对；转换前自动备份

## 覆盖率评估（回答"能否覆盖绝大部分场景"）

| 场景 | 覆盖度 |
|---|---|
| 常规控件 UI（布局、文本、输入、列表、命令绑定） | 自动 ✅ |
| 样式/触发器/控件模板（WPF 最常见的自定义外观手段） | 大部分自动，个别 BasedOn/MultiTrigger 需人工 |
| MVVM（ViewModel、IValueConverter、DataContext 绑定） | 自动 ✅ |
| 依赖属性/附加属性、自定义控件 | 声明自动重写，元数据回调需人工 |
| 事件处理、Dispatcher、常用静态 API | 自动 + WARN 提示 |
| DataGrid（含主题与包引用） | 自动 ✅ |
| 动画 Storyboard/EventTrigger | 不转换，TODO 标记（需改 Animation/Transitions） |
| RichTextBox/FlowDocument、Frame 导航、WebBrowser/WinForms 宿主、3D、InkCanvas | 无核心等价物，TODO 标记（社区库或自绘） |
| MessageBox / 文件对话框 / 剪贴板 / 鼠标坐标静态 API | TODO 标记 + 替代写法建议 |
| 非 SDK 风格旧 csproj | 需先 `dotnet migrate` 升级 |

结论：**典型业务型 WPF 工程（数据展示 + MVVM + 样式）约 80–90% 的代码量可自动转换且编译通过；剩余部分集中在动画、文档类控件、平台 API 三类，工具不做破坏性猜测，而是逐条输出带行号和替代方案的 TODO 清单**。转换报告即人工收尾的工作清单。

## 验证状态

- `dotnet test`：28/28 通过（XAML / C# / csproj 三条管线规则级测试）
- 端到端：`samples/WpfShop`（MVVM + keyed 样式 + 触发器 + ControlTemplate + DataGrid + 自定义控件 + 转换器）转换后用 **Avalonia 12.1.1 + net10.0 实际编译，0 错误 0 警告**，TODO 0 条、WARN 1 条
