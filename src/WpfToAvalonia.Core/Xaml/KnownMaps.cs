namespace WpfToAvalonia.Core.Xaml;

/// <summary>
/// WPF → Avalonia 的已知映射规则表。
/// 规则值均经过 Avalonia 12（.NET 10）真实编译验证：
/// 类型键 ControlTheme 资源链、^ 嵌套、/template/ Type#Name 选择器、
/// 伪类集合来自 Avalonia.Themes.Fluent 与控件源码的 PseudoClasses 定义。
/// </summary>
public static class KnownMaps
{
    public const string AvaloniaNs = "https://github.com/avaloniaui";

    public const string XNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>DataGrid 主题（Avalonia.Controls.DataGrid 包内嵌 XAML，须由 App 引入）。</summary>
    public const string DataGridThemeSource = "avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml";

    /// <summary>XAML 命名空间 URI 替换（在解析前做纯文本替换，安全且保留全部格式）。</summary>
    public static readonly IReadOnlyDictionary<string, string> NamespaceUris = new Dictionary<string, string>
    {
        ["http://schemas.microsoft.com/winfx/2006/xaml/presentation"] = AvaloniaNs,
        ["http://schemas.microsoft.com/netfx/2007/xaml/presentation"] = AvaloniaNs,
        ["http://schemas.microsoft.com/netfx/2009/xaml/presentation"] = AvaloniaNs,
    };

    /// <summary>元素重命名（WPF 独有 → Avalonia 对应物）。均经 Avalonia 12 反射验证。</summary>
    public static readonly IReadOnlyDictionary<string, string> ElementRenames = new Dictionary<string, string>
    {
        ["Label"] = "TextBlock",
        ["Page"] = "UserControl",
        // Geometry 体系：WPF Geometry 基类/PathGeometry 字符串资源 → StreamGeometry
        ["Geometry"] = "StreamGeometry",
        ["PathGeometry"] = "StreamGeometry",
        // WPF ListView/ListViewItem → Avalonia ListBox 体系（核心无 ListView；GridView 列视图另行移除）
        ["ListView"] = "ListBox",
        ["ListViewItem"] = "ListBoxItem",
        // WPF Hyperlink 内联元素 → HyperlinkButton（NavigateUri 同名属性保留；RequestNavigate 事件移除）
        ["Hyperlink"] = "HyperlinkButton",
        // WPF RichTextBox → TextBox 纯文本降级（Document/Selection API 需人工改写）
        ["RichTextBox"] = "TextBox",
        // WPF PasswordBox → TextBox + PasswordChar 掩码（反射验证：Avalonia 12 核心无 PasswordBox，
        // TextBox.PasswordChar(char) 承担掩码；.Password/.PasswordChanged 成员由 C# 重写器同步改名）
        ["PasswordBox"] = "TextBox",
        // WPF ListView 模板行呈现器 → ContentPresenter（Avalonia.Controls.Presenters.ContentPresenter）
        ["GridViewRowPresenter"] = "ContentPresenter",
    };

    /// <summary>
    /// WPF 独有控件库的 XAML 命名空间（csproj 侧包已隔离）：子树整体注释移除 + Manual 提示。
    /// OxyPlot.Wpf 的 http://oxyplot.org/wpf（PlotView/TrackerControl 等）对应包已隔离，
    /// XAML 侧引用无法解析（AXN0004），注释移除保持文件可编译。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> WpfOnlyXamlNamespaces =
        new Dictionary<string, string>
        {
            ["http://oxyplot.org/wpf"] =
                "OxyPlot.Wpf（包已隔离）：图表改用 ScottPlot.Avalonia / LiveChartsCore.SkiaSharpView.Avalonia，或用 Canvas/DrawingContext 手工自绘（StatisticsUserControl 统计图表实测）",
        };

    /// <summary>
    /// WPF GridView 列视图体系元素——Avalonia 核心无对应（DataGrid 是另一套机制）。
    /// 转换时整体注释移除 + Manual 提示；TargetType 指向这些类型的主题同样移除。
    /// GridViewRowPresenter 例外：重命名为 ContentPresenter（模板行内容呈现仍有用）。
    /// </summary>
    public static readonly IReadOnlySet<string> GridViewFamilyElements = new HashSet<string>
    {
        "GridView", "GridViewColumn", "GridViewColumnHeader", "GridViewHeaderRowPresenter",
    };

    /// <summary>无核心等价物、需人工处理的元素（保留原样 + Manual 提示）。</summary>
    public static readonly IReadOnlySet<string> UnsupportedElements = new HashSet<string>
    {
        "FlowDocument", "FlowDocumentScrollViewer", "FlowDocumentReader",
        "Frame", "ToolBar", "ToolBarTray", "StatusBar", "WindowsFormsHost", "WebBrowser",
        "InkCanvas", "Viewport3D", "MediaElement", "Ribbon", "JumpList",
        "GeometryGroup", "CombinedGeometry",
    };

    /// <summary>特性重命名（值均经 Avalonia 12 编译验证）。</summary>
    public static readonly IReadOnlyDictionary<string, string> AttributeRenames = new Dictionary<string, string>
    {
        // ToolTip 附加属性宿主从 ToolTipService → ToolTip
        ["ToolTipService.ToolTip"] = "ToolTip.Tip",
        // WPF 容器样式 → Avalonia 容器主题（keyed 样式已统一转为 ControlTheme）
        ["ItemContainerStyle"] = "ItemContainerTheme",
        // ZIndex：WPF 附加（Panel/Canvas.ZIndex）→ Avalonia Visual 直接属性
        ["Panel.ZIndex"] = "ZIndex",
        ["Canvas.ZIndex"] = "ZIndex",
        // TextBlock.X 附加（WPF 反编译产物）→ Avalonia TextElement.X 附加属性
        ["TextBlock.FontSize"] = "TextElement.FontSize",
        ["TextBlock.FontWeight"] = "TextElement.FontWeight",
        ["TextBlock.FontStyle"] = "TextElement.FontStyle",
        ["TextBlock.FontFamily"] = "TextElement.FontFamily",
        ["TextBlock.Foreground"] = "TextElement.Foreground",
        // TabIndex：附加形式 → InputElement 直接属性
        ["KeyboardNavigation.TabIndex"] = "TabIndex",
    };

    /// <summary>属性元素（&lt;X.Prop&gt;）后缀重命名。</summary>
    public static readonly IReadOnlyDictionary<string, string> PropertyElementRenames = new Dictionary<string, string>
    {
        ["ItemContainerStyle"] = "ItemContainerTheme",
        ["ItemContainerStyleSelector"] = "ItemContainerThemeSelector",
    };

    /// <summary>应整体丢弃的元素特性（Avalonia 无对应或无意义；均经验证）。</summary>
    public static readonly IReadOnlySet<string> DropAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SnapsToDevicePixels", "UseLayoutRounding", "FocusManager.IsFocusScope",
        "TextOptions.TextFormattingMode", "TextOptions.TextRenderingMode",
        "TextOptions.TextHintingMode", "ShowActivated",
        "OverridesDefaultStyle", "FocusVisualStyle", "RecognizesAccessKey",
        "ScrollViewer.CanContentScroll", "RenderOptions.ClearTypeHint",
        // WPF 独有：Avalonia 无 TextSearch / VirtualizingPanel 附加属性（默认虚拟化，无回收模式）
        "TextSearch.TextPath",
        "VirtualizingPanel.IsVirtualizing", "VirtualizingPanel.ScrollUnit",
        "VirtualizingPanel.VirtualizationMode", "VirtualizingPanel.CacheLength",
        "VirtualizingPanel.CacheLengthUnit",
        // KeyboardNavigation.DirectionalNavigation / ToolTipService.ShowDuration：Avalonia 无对应附加属性
        "KeyboardNavigation.DirectionalNavigation", "ToolTipService.ShowDuration",
        // Storyboard 附加定位（Avalonia 动画用 Selector 定位，动画本身已标 Manual）
        "Storyboard.TargetName", "Storyboard.TargetProperty",
        "Grid.IsSharedSizeScope",
        // WPF RichTextBox 文档交互开关（已随 RichTextBox → TextBox 降级）
        "IsDocumentEnabled",
    };

    /// <summary>删除 + Manual 提示的特性（行为缺失需人工补）。</summary>
    public static readonly IReadOnlyDictionary<string, string> ManualDropAttributes = new Dictionary<string, string>
    {
        ["WindowChrome.IsHitTestVisibleInChrome"] =
            "Avalonia 无 WindowChrome；自定义标题栏需用 ExtendClientAreaToDecorationsHint + 通过 tag/IsHitTestVisible 自行处理命中测试。",
        ["WindowChrome.ResizeGripDirection"] =
            "Avalonia 无 WindowChrome.ResizeGripDirection；调整大小手柄由系统装饰或自定义实现提供。",
        ["WindowChrome.GlassFrameThickness"] =
            "Avalonia 无 WindowChrome.GlassFrameThickness；玻璃效果需 TransparencyLevelHint。",
        ["WindowChrome.CornerRadius"] = "Avalonia 无 WindowChrome.CornerRadius。",
        // WPF Hyperlink.RequestNavigate：HyperlinkButton 无该事件（NavigateUri 属性保留）
        ["RequestNavigate"] =
            "Avalonia 无 RequestNavigate 事件；HyperlinkButton 保留 NavigateUri，请在 Click 处理器内用 Process.Start(NavigateUri?.ToString()) 打开链接。",
    };

    /// <summary>应整体丢弃的 Setter/触发器属性（Avalonia 无对应机制）。</summary>
    public static readonly IReadOnlySet<string> DropSetterProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "OverridesDefaultStyle", "FocusVisualStyle", "SnapsToDevicePixels", "UseLayoutRounding",
        "RenderOptions.ClearTypeHint", "FrameworkContentElement.FocusVisualStyle",
    };

    /// <summary>
    /// WPF 反编译 XAML 中属性名的"可剥除所有者前缀"——这些所有者是目标类型的基类，
    /// Avalonia 的 Setter/TemplateBinding 按目标类型解析属性，前缀应剥除：
    /// Property="Border.BorderBrush" → Property="BorderBrush"。
    /// </summary>
    public static readonly IReadOnlySet<string> StripOwnerPrefixes = new HashSet<string>
    {
        // 布局/基础
        "UIElement", "FrameworkElement", "Control", "ContentControl", "ContentElement",
        "FrameworkContentElement", "ItemsControl", "HeaderedContentControl",
        "HeaderedItemsControl", "Decorator", "Panel", "Border", "Viewbox", "GridSplitter",
        "Window", "WindowBase", "NavigationWindow", "Page", "UserControl",
        // 按钮/选择
        "ButtonBase", "Button", "ToggleButton", "RepeatButton", "Selector", "ListBox",
        "ListBoxItem", "ComboBox", "ComboBoxItem", "MenuItem", "Menu", "TabItem", "TabControl",
        "Expander", "GroupBox", "Calendar", "DataGrid", "DataGridCell", "DataGridRow",
        // 输入
        "TextBoxBase", "TextBox", "PasswordBox", "RichTextBox",
        // 文本
        "TextElement", "TextBlock", "AccessText", "Run", "Inline", "Span", "Block",
        // 形状/媒体
        "Shape", "Path", "Rectangle", "Ellipse", "Line", "Polygon", "Polyline", "Image",
        // 范围/滚动/原语
        "RangeBase", "Slider", "ScrollBar", "Thumb", "Track", "ProgressBar",
        "Popup", "ToolTip", "ScrollViewer", "ContentPresenter", "UniformGrid",
        "VirtualizingPanel", "MultiSelector", "TreeView", "TreeViewItem",
    };

    /// <summary>
    /// 纯附加属性所有者——属性名一律保留完整前缀（Avalonia 仍以 Owner.Prop 形式使用）。
    /// </summary>
    public static readonly IReadOnlySet<string> PureAttachedOwners = new HashSet<string>
    {
        "ToolTipService", "KeyboardNavigation", "AutomationProperties", "Validation",
        "VirtualizingStackPanel", "TextOptions", "RenderOptions", "Localization",
        "Stylus", "InkCanvas", "VisualBrush", "BitmapScaling", "DefinitionBase",
        "GridSplitter", "SharedSizeGroup", "Canvas", "DockPanel",
    };

    /// <summary>
    /// 附加属性后缀名——用于 Grid/ScrollViewer 这类"既是控件类型又是附加属性宿主"的所有者：
    /// 前缀 + 附加属性名 → 保留前缀（Grid.Row）；前缀 + 普通属性 → 剥除（Grid.ShowGridLines）。
    /// </summary>
    public static readonly IReadOnlySet<string> AttachedPropertySuffixes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Row", "Column", "RowSpan", "ColumnSpan", "IsSharedSizeScope",
        "Left", "Top", "Right", "Bottom", "ZIndex", "Dock",
        "CanContentScroll", "HorizontalScrollBarVisibility", "VerticalScrollBarVisibility",
        "IsDeferredScrollingEnabled", "VirtualizationMode", "IsScrollInertiaEnabled",
    };

    /// <summary>
    /// 属性触发器 → Avalonia 伪类（均验证自 Avalonia 控件源码 PseudoClasses 定义与 Fluent 主题）。
    /// 返回 false 表示该属性/值组合无伪类等价物（如 IsDefault）。
    /// </summary>
    public static bool TryGetTriggerPseudoClass(string property, string? value, out string pseudoClass)
    {
        pseudoClass = "";
        var prop = NormalizePropertyPath(property);
        var val = value?.Trim().Trim('"') ?? "";
        var isTrue = string.Equals(val, "True", StringComparison.OrdinalIgnoreCase);
        var isFalse = string.Equals(val, "False", StringComparison.OrdinalIgnoreCase);
        var noValue = val.Length == 0; // 反编译丢值：WPF 默认 null

        switch (prop)
        {
            case "IsMouseOver" when isTrue: pseudoClass = "pointerover"; return true;
            case "IsPressed" when isTrue: pseudoClass = "pressed"; return true;
            case "IsEnabled" when isFalse: pseudoClass = "disabled"; return true;
            case "IsChecked" when isTrue: pseudoClass = "checked"; return true;
            case "IsChecked" when isFalse: pseudoClass = "unchecked"; return true;
            // 无 Value：WPF 默认 null → ToggleButton.IsChecked 三态 indeterminate（伪类已验证）
            case "IsChecked" when noValue: pseudoClass = "indeterminate"; return true;
            case "IsSelected" when isTrue: pseudoClass = "selected"; return true;
            case "IsFocused" when isTrue:
            case "IsKeyboardFocused" when isTrue: pseudoClass = "focus"; return true;
            case "IsExpanded" when isTrue: pseudoClass = "expanded"; return true;
            // ComboBox :dropdownopen（MenuItem IsSubmenuOpen → :open，已按控件伪类表核实）
            case "IsDropDownOpen" when isTrue: pseudoClass = "dropdownopen"; return true;
            case "IsSubmenuOpen" when isTrue: pseudoClass = "open"; return true;
            // ItemsControl :empty / TextBox :empty（伪类表已验证）
            case "HasItems" when isFalse: pseudoClass = "empty"; return true;
            case "Text" when val.Length == 0 || val == "\"\"": pseudoClass = "empty"; return true;
            // Thumb :pressed（IsDragging 语义=拖拽按住）
            case "IsDragging" when isTrue: pseudoClass = "pressed"; return true;
            // ScrollBar :horizontal / :vertical
            case "Orientation" when string.Equals(val, "Horizontal", StringComparison.OrdinalIgnoreCase):
                pseudoClass = "horizontal"; return true;
            case "Orientation" when string.Equals(val, "Vertical", StringComparison.OrdinalIgnoreCase):
                pseudoClass = "vertical"; return true;
            default: return false;
        }
    }

    /// <summary>无 Value 触发器（WPF 默认 null）→ "非 null 即命中"语义的属性，Avalonia 伪类等价。</summary>
    public static bool TryGetNonNullPseudoClass(string property, out string pseudoClass)
    {
        pseudoClass = "";
        var prop = NormalizePropertyPath(property);
        switch (prop)
        {
            case "Icon" when prop == "Icon": pseudoClass = "icon"; return true; // MenuItem :icon
            default: return false;
        }
    }

    /// <summary>
    /// 触发器条件 → Avalonia 选择器段：伪类 ":xxx" 或属性匹配器 "[Prop=Value]"。
    /// 属性匹配器仅接受无命名空间前缀属性 + 可解析字面值（布尔解析失败会 AVLN1000）。
    /// </summary>
    public static bool TryGetMatcherSegment(string property, string? value, out string segment)
    {
        segment = "";
        var prop = NormalizePropertyPath(property);
        var val = value?.Trim() ?? "";

        // 属性名带命名空间前缀（附加属性/自定义控件属性）：匹配器语法不支持 → 人工
        if (prop.Contains(':') || prop.Contains('(')) return false;
        // 类型前缀属性（CalendarButton.IsInactive / UIElement.IsMouseOver）：WPF 语义为
        // "定义类上的属性"，WPF 已校验其存在于触发器目标类型继承链上 → 取末段。
        // 带点属性名在 Avalonia 匹配器语法非法（AVLN2201 Expected '='，ForkPlus Calendar 实测）。
        if (prop.Contains('.'))
            prop = prop[(prop.LastIndexOf('.') + 1)..];
        // 空值/标记扩展/绑定值：无法字面解析（"" 对 Boolean 报 AVLN1000）→ 伪类或人工
        if (val.Length == 0 || val.StartsWith('{') || val.Contains(' ') || val.Contains(',')) return false;
        // 布尔属性以外的枚举/字符串值仅限安全字符（避免解析歧义）
        if (!val.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or '#')) return false;

        segment = $"[{prop}={val}]";
        return true;
    }

    /// <summary>WPF Visibility 三态 → Avalonia IsVisible(bool)。Hidden 无布局占位等价物。</summary>
    public static string? VisibilityToIsVisible(string? value)
    {
        return value?.Trim() switch
        {
            "Visible" => "True",
            "Collapsed" => "False",
            "Hidden" => "False", // 警告由调用方记录
            _ => null,
        };
    }

    /// <summary>
    /// WPF Window.ResizeMode 枚举值 → Avalonia Window.CanResize bool 值。
    ///（反射验证：Avalonia 12 无 ResizeMode 枚举，仅 CanResize bool。）
    /// 返回 (canResize, isLossy)：isLossy=true 表示语义有损（CanMinimize/grip）。
    /// </summary>
    public static (string? Value, bool Lossy) ResizeModeToCanResize(string? value)
    {
        return value?.Trim() switch
        {
            "NoResize" => ("False", false),
            // CanMinimize：允许最小化但禁缩放；Avalonia CanResize=false 禁缩放，最小化仍可用（近似）
            "CanMinimize" => ("False", true),
            "CanResize" => ("True", false),
            // CanResizeWithGrip：Avalonia 无 grip，缩放能力等价
            "CanResizeWithGrip" => ("True", true),
            _ => (null, false),
        };
    }

    /// <summary>
    /// 属性路径归一化：剥除可剥除的所有者前缀，保留附加属性前缀。
    /// 兼容反编译 XAML 的 "(Owner.Prop)" 括号写法。
    /// </summary>
    public static string NormalizePropertyPath(string property)
    {
        var p = property.Trim();
        if (p.StartsWith('(') && p.EndsWith(')'))
            p = p[1..^1].Trim();
        var dot = p.IndexOf('.');
        if (dot <= 0 || dot == p.Length - 1) return p;
        var owner = p[..dot];
        var rest = p[(dot + 1)..].Trim();
        // rest 自身还带命名空间前缀（controls:PlaceholderTextBox.Icon）→ 保留原样
        if (rest.Contains(':')) return p;
        if (PureAttachedOwners.Contains(owner)) return p;
        if (AttachedPropertySuffixes.Contains(rest)) return p;
        if (StripOwnerPrefixes.Contains(owner)) return rest;
        return p;
    }

    /// <summary>TemplateBinding 里的属性路径归一化（同 Setter 规则）。</summary>
    public static string NormalizeTemplateBindingPath(string path) => NormalizePropertyPath(path);

    /// <summary>XAML 事件名重命名（鼠标 → 指针模型）。</summary>
    public static readonly IReadOnlyDictionary<string, string> XamlEventRenames = new Dictionary<string, string>
    {
        ["MouseDown"] = "PointerPressed",
        ["MouseUp"] = "PointerReleased",
        ["PreviewMouseDown"] = "PointerPressed",
        ["PreviewMouseUp"] = "PointerReleased",
        ["MouseLeftButtonDown"] = "PointerPressed",
        ["MouseLeftButtonUp"] = "PointerReleased",
        ["MouseRightButtonDown"] = "PointerPressed",
        ["MouseRightButtonUp"] = "PointerReleased",
        ["MouseMove"] = "PointerMoved",
        ["PreviewMouseMove"] = "PointerMoved",
        ["MouseEnter"] = "PointerEntered",
        ["MouseLeave"] = "PointerExited",
        ["MouseWheel"] = "PointerWheelChanged",
        ["PreviewMouseWheel"] = "PointerWheelChanged",
        // WPF 双击：Avalonia InputElement.DoubleTapped（EventHandler<TappedEventArgs>，已反射验证）
        ["MouseDoubleClick"] = "DoubleTapped",
        ["PreviewMouseDoubleClick"] = "DoubleTapped",
        // PasswordBox → TextBox 降级配套：掩码框文本变更事件
        ["PasswordChanged"] = "TextChanged",
    };

    /// <summary>右键事件需要运行时通过 PointerEventArgs 判断按键。</summary>
    public static readonly IReadOnlySet<string> RightButtonEvents = new HashSet<string>
    { "MouseRightButtonDown", "MouseRightButtonUp" };

    /// <summary>C# using 命名空间映射。</summary>
    public static readonly IReadOnlyDictionary<string, string> CSharpNamespaces = new Dictionary<string, string>
    {
        ["System.Windows"] = "Avalonia",
        ["System.Windows.Controls"] = "Avalonia.Controls",
        ["System.Windows.Controls.Primitives"] = "Avalonia.Controls.Primitives",
        ["System.Windows.Media"] = "Avalonia.Media",
        ["System.Windows.Media.Imaging"] = "Avalonia.Media.Imaging",
        ["System.Windows.Shapes"] = "Avalonia.Controls.Shapes",
        ["System.Windows.Input"] = "Avalonia.Input",
        ["System.Windows.Threading"] = "Avalonia.Threading",
        ["System.Windows.Data"] = "Avalonia.Data",
        ["System.Windows.Markup"] = "Avalonia.Markup",
        // Avalonia 12：Inline/Run 位于 Avalonia.Controls.Documents（无 Avalonia.Documents）
        ["System.Windows.Documents"] = "Avalonia.Controls.Documents",
        ["System.Windows.Interactivity"] = "Avalonia.Xaml.Interactivity",
        ["Microsoft.Xaml.Behaviors.Wpf"] = "Avalonia.Xaml.Behaviors",
        // —— Avalonia.AvaloniaEdit 12.0.0（官方 avaloniaui 组织）：WPF ICSharpCode.AvalonEdit
        // 的包替换映射，命名空间整体从 ICSharpCode.AvalonEdit.* 重排为 AvaloniaEdit.*
        //（14 个子命名空间经程序集反射全量枚举验证；AbstractMargin 移入 .Editing）。
        // 配套 csproj 包替换：AvalonEdit → Avalonia.AvaloniaEdit 12.0.0。
        ["ICSharpCode.AvalonEdit"] = "AvaloniaEdit",
        ["ICSharpCode.AvalonEdit.CodeCompletion"] = "AvaloniaEdit.CodeCompletion",
        ["ICSharpCode.AvalonEdit.Document"] = "AvaloniaEdit.Document",
        ["ICSharpCode.AvalonEdit.Editing"] = "AvaloniaEdit.Editing",
        ["ICSharpCode.AvalonEdit.Folding"] = "AvaloniaEdit.Folding",
        ["ICSharpCode.AvalonEdit.Highlighting"] = "AvaloniaEdit.Highlighting",
        ["ICSharpCode.AvalonEdit.Highlighting.Xshd"] = "AvaloniaEdit.Highlighting.Xshd",
        ["ICSharpCode.AvalonEdit.Indentation"] = "AvaloniaEdit.Indentation",
        ["ICSharpCode.AvalonEdit.Indentation.CSharp"] = "AvaloniaEdit.Indentation.CSharp",
        ["ICSharpCode.AvalonEdit.Rendering"] = "AvaloniaEdit.Rendering",
        ["ICSharpCode.AvalonEdit.Search"] = "AvaloniaEdit.Search",
        ["ICSharpCode.AvalonEdit.Snippets"] = "AvaloniaEdit.Snippets",
        ["ICSharpCode.AvalonEdit.Utils"] = "AvaloniaEdit.Utils",
    };

    /// <summary>
    /// using System.Windows 需要额外补充的命名空间：
    /// WPF System.Windows 是大杂烩（Window/Style/Layoutable 分属不同 Avalonia 命名空间），
    /// 单映射到 Avalonia 会丢失 Window（Avalonia.Controls）、Style（Avalonia.Styling）、
    /// Layoutable（Avalonia.Layout）等高频类型。均经 Avalonia 12 反射验证。
    /// </summary>
    public static readonly IReadOnlyList<string> SystemWindowsExtraUsings = new[]
    {
        "Avalonia.Controls",
        "Avalonia.Layout",
        "Avalonia.Styling",
    };

    /// <summary>类型名映射（标识符形式；全限定形式单独处理）。</summary>
    public static readonly IReadOnlyDictionary<string, string> TypeRenames = new Dictionary<string, string>
    {
        ["DependencyObject"] = "global::Avalonia.AvaloniaObject",
        ["DependencyProperty"] = "global::Avalonia.AvaloniaProperty",
        ["ImageSource"] = "global::Avalonia.Media.IImage",
        ["BitmapImage"] = "global::Avalonia.Media.Imaging.Bitmap",
        ["BitmapSource"] = "global::Avalonia.Media.Imaging.Bitmap",
        ["MouseEventArgs"] = "global::Avalonia.Input.PointerEventArgs",
        ["MouseButtonEventArgs"] = "global::Avalonia.Input.PointerPressedEventArgs",
        ["MouseWheelEventArgs"] = "global::Avalonia.Input.PointerWheelEventArgs",
        ["ModifierKeys"] = "global::Avalonia.Input.KeyModifiers",
        ["FrameworkPropertyMetadata"] = "global::Avalonia.StyledPropertyMetadata",
        ["PropertyMetadata"] = "global::Avalonia.StyledPropertyMetadata",
        ["UIElement"] = "global::Avalonia.Input.InputElement",
        ["VisualBrush"] = "global::Avalonia.Media.ImmutableBrush", // 近似，标记 WARN
        // —— Avalonia 12 实测补充（程序集反射验证）——
        // WPF FrameworkElement → Control（ForkPlus Treemap CS0115 驱动：Layoutable 不在
        // InputElement 继承链上，无 OnPointerXxx/OnSizeChanged 虚方法；Control 继承链
        // Control→InputElement→Interactive→Layoutable 全覆盖：布局+输入+渲染。反射验证）。
        // ActualWidth→Bounds.Width；Tag/ToolTip/ContextMenu 差异标 WARN。
        ["FrameworkElement"] = "global::Avalonia.Controls.Control",
        // WPF DP 属性变更参数 → Avalonia 12 非泛型基类（OldValue/NewValue/Property 均在）
        ["DependencyPropertyChangedEventArgs"] = "global::Avalonia.AvaloniaPropertyChangedEventArgs",
        // TemplatePart 特性：Avalonia.Controls.Metadata.TemplatePartAttribute
        ["TemplatePart"] = "global::Avalonia.Controls.Metadata.TemplatePartAttribute",
        ["TemplatePartAttribute"] = "global::Avalonia.Controls.Metadata.TemplatePartAttribute",
        // 滚动条可见性枚举位置
        ["ScrollBarVisibility"] = "global::Avalonia.Controls.Primitives.ScrollBarVisibility",
        // —— 反射验证（Avalonia 12）——
        // ICommand：Avalonia 无自有接口，Button.Command 属性类型就是 BCL 的
        // System.Windows.Input.ICommand（System.ObjectModel 程序集，全平台可用）。
        // 全限定映射保证 using Avalonia.Input（无 ICommand）下仍可解析。
        ["ICommand"] = "global::System.Windows.Input.ICommand",
        // IDataObject（拖拽数据）：Avalonia 12 无接口，等价是 Avalonia.Input.IDataTransfer
        //（DragEventArgs.DataTransfer / DragDrop.DoDragDropAsync 参数；DataObject 已过时）。
        // 方法体 SetData/GetData 成员差异由 CS-DRAGDROP 人工提示覆盖。
        ["IDataObject"] = "global::Avalonia.Input.IDataTransfer",
        // —— 反射验证（Avalonia 12 探针16）——
        // ControlTemplate：Avalonia 12 无此类（XAML 的 <ControlTemplate> 由 XamlIl 编译器
        // 特殊处理成 FuncControlTemplate，C# 侧无类型可引用）→ 代码声明用 IControlTemplate；
        // 成员差异（FindName 等）由 CS-WPFONLY-TYPE 人工提示覆盖（ControlTemplateExtensions 实测）。
        ["ControlTemplate"] = "global::Avalonia.Controls.Templates.IControlTemplate",
        // StartupEventArgs/ExitEventArgs：WPF 仅存在于 Application.OnStartup/OnExit 参数
        //（反射验证 Avalonia.Application 无此二虚方法）；类型映射为 object 使去 override 化
        // 后的方法可编译（方法体仅 base 转发不引用 e；e.Args 引用由映射警告提示改
        // Environment.GetCommandLineArgs()）。
        ["StartupEventArgs"] = "global::System.Object",
        ["ExitEventArgs"] = "global::System.Object",
        // WPF ListView → Avalonia ListBox 体系
        ["ListViewItem"] = "global::Avalonia.Controls.ListBoxItem",
        // ListView 本体（代码侧 new ListView()/类型声明）
        ["ListView"] = "global::Avalonia.Controls.ListBox",
        // WPF Hyperlink（代码侧创建链接元素）→ HyperlinkButton
        ["Hyperlink"] = "global::Avalonia.Controls.HyperlinkButton",
        // 窗口状态枚举位置（成员访问名不受影响）
        ["WindowState"] = "global::Avalonia.Controls.WindowState",
        // 窗口启动位置枚举（反射验证 Avalonia.Controls.WindowStartupLocation）
        ["WindowStartupLocation"] = "global::Avalonia.Controls.WindowStartupLocation",
        // 内容自适应枚举（反射验证 Avalonia.Controls.SizeToContent）
        ["SizeToContent"] = "global::Avalonia.Controls.SizeToContent",
        // WPF System.Windows.Markup.MarkupExtension → Avalonia.Markup.Xaml 程序集
        //（前缀替换会得到不存在的 Avalonia.Markup.MarkupExtension，32 处 CS0246 实测）
        ["MarkupExtension"] = "global::Avalonia.Markup.Xaml.MarkupExtension",
        ["IComponentConnector"] = "global::Avalonia.Markup.Xaml.IComponentConnector",
        // WPF ContextMenuEventArgs → ContextRequested 事件参数（Handled 语义保留，
        // 但无 CursorLeft/Source 属性，标记 WARN；反射验证 Avalonia.Input）
        ["ContextMenuEventArgs"] = "global::Avalonia.Input.ContextRequestedEventArgs",
        // Avalonia 12 已移除 Visibility 枚举 → bool（IsVisible）；Hidden/Collapsed 语义合并
        ["Visibility"] = "bool",
        // WPF PasswordBox → TextBox（掩码经 PasswordChar 特性补齐；ForkPlus 2 窗口实测）
        ["PasswordBox"] = "global::Avalonia.Controls.TextBox",
    };

    /// <summary>
    /// 虚方法覆盖重写映射（WPF 覆盖虚方法 → Avalonia 12 虚方法）。
    /// Param0Type/Param1Type 为 null 表示保留原参数类型（已由 TypeRenames 处理）；
    /// Access 非 null 时强制调整可访问性（Render 须 public / OnApplyTemplate 须 protected）；
    /// TargetParamCount 非 null 时截断/校准参数个数（OnInitialized 去参 / ClearContainerForItemOverride 删参）；
    /// AppendParams 为追加参数的类型+名字（PrepareContainerForItemOverride 补 int index）。
    /// base.Xxx(...) 调用的实参列表会同步增删。
    /// </summary>
    public sealed record OverrideMethodMap(
        string NewName, string? Param0Type, string? Param1Type = null, string? Access = null,
        int? TargetParamCount = null, string[]? AppendParams = null);

    /// <summary>
    /// WPF 覆盖虚方法 → Avalonia 12 虚方法（方法名/参数类型/可访问性强制覆盖）。
    /// 全部经 Avalonia 12.1.1 反射 + 编译探针双重验证：InputElement（指针/键盘）、
    /// Visual（Render public，protected 覆盖报 CS0507）、Control（OnSizeChanged）、
    /// TemplatedControl（OnApplyTemplate 带参，无参覆盖报 CS0115）、Window（OnClosing）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, OverrideMethodMap> OverrideMethodRenames =
        new Dictionary<string, OverrideMethodMap>
        {
            // 键盘（InputElement）
            ["OnPreviewKeyDown"] = new("OnKeyDown", null),
            ["OnPreviewKeyUp"] = new("OnKeyUp", null),
            // 指针（InputElement）：WPF 鼠标虚方法族 → OnPointerXxx
            ["OnMouseDown"] = new("OnPointerPressed", "global::Avalonia.Input.PointerPressedEventArgs"),
            ["OnPreviewMouseDown"] = new("OnPointerPressed", "global::Avalonia.Input.PointerPressedEventArgs"),
            ["OnMouseUp"] = new("OnPointerReleased", "global::Avalonia.Input.PointerReleasedEventArgs"),
            ["OnPreviewMouseUp"] = new("OnPointerReleased", "global::Avalonia.Input.PointerReleasedEventArgs"),
            ["OnMouseLeftButtonDown"] = new("OnPointerPressed", "global::Avalonia.Input.PointerPressedEventArgs"),
            ["OnPreviewMouseLeftButtonDown"] = new("OnPointerPressed", "global::Avalonia.Input.PointerPressedEventArgs"),
            ["OnMouseLeftButtonUp"] = new("OnPointerReleased", "global::Avalonia.Input.PointerReleasedEventArgs"),
            ["OnPreviewMouseLeftButtonUp"] = new("OnPointerReleased", "global::Avalonia.Input.PointerReleasedEventArgs"),
            ["OnMouseRightButtonDown"] = new("OnPointerPressed", "global::Avalonia.Input.PointerPressedEventArgs"),
            ["OnMouseRightButtonUp"] = new("OnPointerReleased", "global::Avalonia.Input.PointerReleasedEventArgs"),
            // 右键 Preview 变体（MultiselectionTreeView.OnPreviewMouseRightButtonDown 实测 CS0115）
            ["OnPreviewMouseRightButtonDown"] = new("OnPointerPressed", "global::Avalonia.Input.PointerPressedEventArgs"),
            ["OnPreviewMouseRightButtonUp"] = new("OnPointerReleased", "global::Avalonia.Input.PointerReleasedEventArgs"),
            ["OnMouseMove"] = new("OnPointerMoved", "global::Avalonia.Input.PointerEventArgs"),
            ["OnPreviewMouseMove"] = new("OnPointerMoved", "global::Avalonia.Input.PointerEventArgs"),
            ["OnMouseEnter"] = new("OnPointerEntered", "global::Avalonia.Input.PointerEventArgs"),
            ["OnMouseLeave"] = new("OnPointerExited", "global::Avalonia.Input.PointerEventArgs"),
            ["OnMouseWheel"] = new("OnPointerWheelChanged", "global::Avalonia.Input.PointerWheelEventArgs"),
            ["OnPreviewMouseWheel"] = new("OnPointerWheelChanged", "global::Avalonia.Input.PointerWheelEventArgs"),
            ["OnMouseDoubleClick"] = new("OnDoubleTapped", "global::Avalonia.Input.TappedEventArgs"),
            ["OnPreviewMouseDoubleClick"] = new("OnDoubleTapped", "global::Avalonia.Input.TappedEventArgs"),
            // 自绘：WPF protected OnRender(DrawingContext) → Avalonia public Render(DrawingContext)
            //（protected 覆盖 public 虚方法 → CS0507，编译探针 T3/T4 验证）
            ["OnRender"] = new("Render", null, Access: "public"),
            // 尺寸：WPF OnRenderSizeChanged(SizeChangedInfo) → Control.OnSizeChanged(SizeChangedEventArgs)
            ["OnRenderSizeChanged"] = new("OnSizeChanged", "global::Avalonia.Controls.SizeChangedEventArgs"),
            // Window 关闭：WPF OnClosing(CancelEventArgs) → Window.OnClosing(WindowClosingEventArgs)
            ["OnClosing"] = new("OnClosing", "global::Avalonia.Controls.WindowClosingEventArgs"),
            // 模板应用：WPF 无参 OnApplyTemplate() → TemplatedControl.OnApplyTemplate(TemplateAppliedEventArgs)
            //（Avalonia 12 带参 + protected；WPF public 无参覆盖 → CS0115，探针 T1/T2 验证）
            ["OnApplyTemplate"] = new("OnApplyTemplate",
                "global::Avalonia.Controls.Primitives.TemplateAppliedEventArgs", Access: "protected"),
            // —— 以下为 ForkPlus 端到端 dry-run CS0115 错误驱动的补充（反射探针验证）——
            // WPF FrameworkElement.OnInitialized(EventArgs) → StyledElement.OnInitialized() 无参
            //（反射验证：protected virtual ()，WPF 带参覆盖 → CS0115）
            ["OnInitialized"] = new("OnInitialized", null, TargetParamCount: 0),
            // WPF ItemsControl.PrepareContainerForItemOverride(DependencyObject, object)
            // → Avalonia 12 (Control, object, int index)（反射验证 protected virtual 三参）
            ["PrepareContainerForItemOverride"] = new("PrepareContainerForItemOverride",
                "global::Avalonia.Controls.Control", AppendParams: new[] { "int index" }),
            // WPF ClearContainerForItemOverride(DependencyObject, object) → Avalonia 12 (Control)
            //（反射验证 protected virtual 单参；WPF 双参覆盖 → CS0115）
            ["ClearContainerForItemOverride"] = new("ClearContainerForItemOverride",
                "global::Avalonia.Controls.Control", TargetParamCount: 1),
        };

    /// <summary>
    /// WPF 覆盖虚方法在 Avalonia 12 中无对应覆盖点（或覆盖点已 sealed）：
    /// 不改签名，输出人工提示（反射 + 编译探针验证）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> OverrideMethodManualNotes =
        new Dictionary<string, string>
        {
            ["OnDrop"] = "拖拽虚方法无覆盖点：AddHandler(DragDrop.DropEvent, H, RoutingStrategies.Bubble) 订阅。",
            ["OnDragEnter"] = "拖拽虚方法无覆盖点：AddHandler(DragDrop.DragEnterEvent, H, RoutingStrategies.Bubble) 订阅。",
            ["OnDragLeave"] = "拖拽虚方法无覆盖点：AddHandler(DragDrop.DragLeaveEvent, H, RoutingStrategies.Bubble) 订阅。",
            ["OnDragOver"] = "拖拽虚方法无覆盖点：AddHandler(DragDrop.DragOverEvent, H, RoutingStrategies.Bubble) 订阅。",
            ["OnGiveFeedback"] = "拖拽反馈无等价（Avalonia 拖拽光标自管理）。",
            ["OnQueryContinueDrag"] = "拖拽续行查询无等价。",
            ["OnLocationChanged"] = "Window 位置变化无虚方法：构造函数订阅 PositionChanged 事件。",
            ["OnActivated"] = "Window 激活无虚方法：订阅 Activated 事件。",
            ["OnDeactivated"] = "Window 失活无虚方法：订阅 Deactivated 事件。",
            ["OnSourceInitialized"] = "无等价（WPF HWND 初始化回调）：改用 OnOpened 或 TopLevel 平台句柄 API（TryGetPlatformHandle）。",
            ["OnContentRendered"] = "无等价：改用 OnOpened / LayoutUpdated。",
            ["OnSelectionChanged"] = "SelectingItemsControl 无此虚方法：订阅 SelectionChanged 事件（或 SelectionModel 变更回调）。",
            ["OnTextChanged"] = "TextBox 无 OnTextChanged 虚方法：订阅 TextChanged 事件。",
            ["GetContainerForItemOverride"] = "ItemsControl 容器机制不同：CreateContainerForItemOverride(object item, int index, object recycleKey)。",
            ["IsItemItsOwnContainerOverride"] = "ItemsControl 容器机制不同：NeedsContainerOverride(object item, int index, out object recycleKey)。",
            ["OnStartup"] = "Application 无 OnStartup：Avalonia 用 OnFrameworkInitializationCompleted（AppBuilder 生命周期）。",
            ["OnExit"] = "Application 无 OnExit：订阅 lifetime.Exit 事件（IClassicDesktopStyleApplicationLifetime）。",
            ["OnSessionEnding"] = "无等价：订阅 lifetime.Exit / 系统会话事件自行处理。",
            // Layoutable.OnVisualParentChanged(Visual?, Visual?) 已 sealed：编译探针 CS0239 验证
            ["OnVisualParentChanged"] = "Layoutable.OnVisualParentChanged 已 sealed 不可覆盖：改订阅 DetachedFromVisualTree / AttachedToVisualTree 事件。",
            // —— 以下为 ForkPlus 端到端 dry-run CS0115 错误驱动（反射验证全链 ABSENT）——
            ["OnContentChanged"] = "ContentControl 无 OnContentChanged 虚方法：监听内容变更用 ContentProperty.GetObservable(this) / OnPropertyChanged(AvaloniaPropertyChangedEventArgs)。",
            ["OnChecked"] = "ToggleButton 无 OnChecked/OnUnchecked 虚方法：覆盖 OnIsCheckedChanged(RoutedEventArgs) 或 IsCheckedProperty.GetObservable。",
            ["OnUnchecked"] = "ToggleButton 无 OnChecked/OnUnchecked 虚方法：覆盖 OnIsCheckedChanged(RoutedEventArgs) 或 IsCheckedProperty.GetObservable。",
            ["OnStateChanged"] = "Window 无 OnStateChanged 虚方法：订阅 WindowStateProperty 变更（this.GetObservable(Window.WindowStateProperty)）。",
            ["OnIsKeyboardFocusWithinChanged"] = "无此虚方法：订阅 GotFocus/LostFocus 事件或 KeyboardNavigation 相关事件聚合实现。",
        };

    /// <summary>WPF 独有命名空间（Avalonia 无等价）：using 移除 / 限定引用保留并提示人工。</summary>
    public static readonly IReadOnlySet<string> WpfOnlyNamespaces = new HashSet<string>
    {
        "System.Windows.Navigation",
        "System.Windows.Interop",
        "System.Windows.Media.Media3D",
        "System.Windows.Shell",
        "System.Windows.Resources",
    };

    /// <summary>
    /// WPF 独有类型（Avalonia 无等价、且非简单改名可解）：保留原名 + 每类型一次人工提示
    /// （ForkPlus 端到端 CS0246 错误驱动；逐一给出替代方案，避免裸报错无指引）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> WpfOnlyTypes = new Dictionary<string, string>
    {
        ["RequestNavigateEventArgs"] = "Hyperlink 导航事件参数无等价：HyperlinkButton.NavigateUri + Click 事件。",
        ["Adorner"] = "WPF Adorner 体系无等价：用 Overlay/Popup 或装饰层 Canvas 叠加自绘控件实现。",
        ["AdornerLayer"] = "WPF AdornerLayer 无等价：自建装饰管理层（Canvas + ZIndex）或 Popup。",
        ["RoutedEventHandler"] = "WPF 路由事件委托无统一等价：按具体事件换用对应 EventHandler<TArgs>（如 EventHandler<RoutedEventArgs>）。",
        ["ContextMenuEventHandler"] = "ContextMenuOpening/Closing 事件无等价：Avalonia 用 ContextMenu.Opening/Closing 或 Control.ContextRequested（ContextRequestedEventArgs）。",
        ["GiveFeedbackEventArgs"] = "拖拽反馈事件无等价（Avalonia 拖拽光标自管理）。",
        ["IWeakEventListener"] = "WPF 弱事件接口无等价：用 Avalonia 弱订阅或普通事件（Avalonia 编译 XAML 无泄漏内存路径）。",
        ["DataObjectPastingEventArgs"] = "粘贴拦截事件无等价：Avalonia TextBox 无 DataObject.Pasting，需在 TextChanged 里校验。",
        ["ValueConversionAttribute"] = "值转换元数据特性无等价：直接实现 IValueConverter 即可，删除特性。",
        ["ContentPropertyAttribute"] = "类级内容属性注解无等价：Avalonia 用 [Content] 标注属性（Avalonia.Metadata）或约定 Content 属性。",
        ["ThemeInfoAttribute"] = "WPF 主题资源位置特性无等价：整条特性已删除，Avalonia 主题经 App.Styles 引入。",
        ["ToolTipEventArgs"] = "ToolTipOpening/Closing 事件无等价：用 ToolTip.Tip 内容或 TipClicked 自绘近似。",
        ["SpellingError"] = "WPF 拼写检查 API 无等价：Avalonia TextBox 无内建拼写检查。",
        ["RoutedCommand"] = "WPF RoutedCommand 体系无等价：改用 ReactiveCommand / 自定义 ICommand + 快捷键绑定（KeyBindings）。",
        ["CommandBinding"] = "WPF CommandBinding 体系无等价：ICommand.CanExecute/Execute 直接绑定。",
        ["ExecutedRoutedEventHandler"] = "WPF 路由命令处理器无等价：ICommand.Execute 实现。",
        ["MouseWheelEventHandler"] = "WPF 滚轮委托无等价：Avalonia PointerWheelChanged 是 EventHandler<PointerWheelEventArgs>（泛型）。",
        ["HwndSource"] = "HWND 互操作无等价：TopLevel.TryGetPlatformHandle() 获取平台句柄。",
        ["GuidelineSet"] = "DrawingContext 网格对齐无等价：Avalonia DrawingContext 无 PushGuidelines，按需重算坐标。",
        ["FrameworkElementAutomationPeer"] = "WPF UI 自动化对等类无公开等价：Avalonia 自动化内建，删除该类。",
        ["ExitEventArgs"] = "Application 退出参数无等价：lifetime.Exit 事件（ControlledApplicationLifetimeExitEventArgs）。",
        ["StartupEventArgs"] = "Application 启动参数无等价：OnFrameworkInitializationCompleted（无参数）。",
        ["ControlTemplate"] = "代码创建 ControlTemplate 无等价：Avalonia 用 FuncControlTemplate<T>(x => Build) 或 XAML 内联模板。",
        ["ResizeMode"] = "WPF ResizeMode 枚举无等价：Window.CanResize bool（NoResize/CanMinimize→false，CanResize*→true；grip 无等价）。",
        ["RoutedPropertyChangedEventArgs"] = "WPF 值变更路由参数无等价：ValueChanged/PropertyChange 事件参数（T 类型的旧/新值属性各异）。",
        ["CommonFileDialog"] = "WindowsAPICodePack 对话框已随包隔离：改用 Avalonia StorageProvider（IStorageProvider.OpenFilePickerAsync 等）。",
        // —— WPF 集合视图族（ForkPlus StatisticsUserControl CS0234 实测：前缀替换曾产出
        //    不存在的 Avalonia.Data.ListCollectionView；Avalonia 无 CollectionView 概念）——
        ["ListCollectionView"] = "WPF 集合视图无等价：Avalonia 直接绑定 IEnumerable/DataView 不存在；排序/过滤/分组在 ViewModel 层实现（ListCollectionView.SortDescriptions/Filter → 预处理集合或自建包装）。",
        ["CollectionView"] = "WPF 集合视图无等价：Avalonia 直接绑定 IEnumerable；排序/过滤在 ViewModel 层实现。",
        ["ICollectionView"] = "WPF 集合视图接口无等价：Avalonia 直接绑定 IEnumerable；排序/过滤在 ViewModel 层实现。",
        ["BindingListCollectionView"] = "WPF 集合视图无等价：Avalonia 直接绑定 IEnumerable；分组/过滤在 ViewModel 层实现。",
    };

    /// <summary>
    /// 常见 Avalonia 控件基类名（去 override 化的 base 调用有效性判定）：
    /// 类的基类列表命中本集合 → 基类是 Avalonia 类型（无 ManualNotes 方法成员），
    /// 方法体内 base.Xxx(...) 语句可安全删除；未命中（如 CustomWindow 等用户基类）
    /// → base 调用指向用户类降级后的普通方法，保留。
    /// 名称经 Avalonia 12 Controls 反射核对（转换后基类名与 WPF 同名）。
    /// </summary>
    public static readonly IReadOnlySet<string> AvaloniaControlBaseNames = new HashSet<string>
    {
        // 窗口/页面/用户控件 + 应用宿主（App : Application 的 base.OnStartup/OnExit 删除判定）
        "Application", "Window", "UserControl", "ContentControl", "Control", "TemplatedControl",
        "HeaderedContentControl", "HeaderedItemsControl",
        // 项集合
        "ItemsControl", "ListBox", "ListBoxItem", "TabControl", "TabItem",
        "TreeView", "TreeViewItem", "ComboBox", "ComboBoxItem", "Menu", "MenuItem",
        // 输入
        "TextBox", "Button", "ToggleButton", "CheckBox", "RadioButton",
        "AutoCompleteBox", "NumericUpDown", "SplitButton",
        // 布局/呈现
        "Panel", "StackPanel", "Grid", "Canvas", "DockPanel", "WrapPanel", "UniformGrid",
        "Border", "ScrollViewer", "Separator", "TextBlock", "Image", "Expander",
        "Slider", "ProgressBar", "ToolTip", "Popup", "Calendar", "DatePicker",
        // 高级（包内）
        "DataGrid", "AvaloniaEdit.TextEditor",
    };

    /// <summary>
    /// 无等价、直接整条删除的 WPF 特性（注解级语义，删除即可编译）：
    /// WpfOnlyTypes 中指引明确为"删除特性"的子集，由 VisitAttributeList 执行删除。
    /// 键为特性名末段（"ValueConversion" 同时匹配 "ValueConversionAttribute"）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> RemovedAttributes =
        new Dictionary<string, string>
        {
            ["ValueConversion"] = "WPF 值转换器设计时元数据，Avalonia 无对应：直接实现 IValueConverter 即可（ForkPlus 8 处实测 CS0246）。",
            ["ContentProperty"] = "类级内容属性注解：Avalonia 在属性上标 [Content]（Avalonia.Metadata）；Window 等基类 Content 属性已内建该标注（反射验证 12.1.1 无 ContentPropertyAttribute）。",
            ["AssemblyAssociatedContentFile"] = "WPF 松散文件关联声明：Avalonia 资源体系（avares://）不需要（ForkPlus AssemblyInfo.cs 实测 CS0246）。",
        };

    /// <summary>全限定类型映射（System.Windows.Point 等）。
    /// 精确全名匹配优先于 QualifiedPrefixes 前缀替换——using 别名
    /// （using WindowState = System.Windows.WindowState）与跨命名空间移动的类型
    /// （WindowState/MarkupExtension）靠本表才能落在正确命名空间。</summary>
    public static readonly IReadOnlyDictionary<string, string> QualifiedTypeRenames = new Dictionary<string, string>
    {
        ["System.Windows.Point"] = "global::Avalonia.Point",
        ["System.Windows.Vector"] = "global::Avalonia.Vector",
        ["System.Windows.Size"] = "global::Avalonia.Size",
        ["System.Windows.Rect"] = "global::Avalonia.Rect",
        ["System.Windows.Thickness"] = "global::Avalonia.Thickness",
        ["System.Windows.CornerRadius"] = "global::Avalonia.CornerRadius",
        ["System.Windows.GridLength"] = "global::Avalonia.GridLength",
        ["System.Windows.Media.Color"] = "global::Avalonia.Media.Color",
        ["System.Windows.Media.Colors"] = "global::Avalonia.Media.Colors",
        ["System.Windows.Media.Brush"] = "global::Avalonia.Media.IBrush",
        ["System.Windows.Media.Brushes"] = "global::Avalonia.Media.Brushes",
        ["System.Windows.Media.SolidColorBrush"] = "global::Avalonia.Media.SolidColorBrush",
        ["System.Windows.Threading.Dispatcher"] = "global::Avalonia.Threading.Dispatcher",
        ["System.Windows.Threading.DispatcherTimer"] = "global::Avalonia.Threading.DispatcherTimer",
        // —— Window 枚举族（反射验证：Avalonia.Controls 命名空间，前缀替换会错位到 Avalonia.*）——
        ["System.Windows.WindowState"] = "global::Avalonia.Controls.WindowState",
        ["System.Windows.WindowStartupLocation"] = "global::Avalonia.Controls.WindowStartupLocation",
        ["System.Windows.SizeToContent"] = "global::Avalonia.Controls.SizeToContent",
        // —— 跨命名空间移动的基类/扩展点（前缀替换会得到不存在的类型）——
        ["System.Windows.Markup.MarkupExtension"] = "global::Avalonia.Markup.Xaml.MarkupExtension",
        ["System.Windows.Markup.IComponentConnector"] = "global::Avalonia.Markup.Xaml.IComponentConnector",
        // WPF FrameworkElement 基类（前缀替换 → Avalonia.FrameworkElement 不存在；与裸名
        // TypeRenames["FrameworkElement"] 对齐：Control 继承链覆盖布局+输入+渲染）
        ["System.Windows.FrameworkElement"] = "global::Avalonia.Controls.Control",
        // WPF UIElement 基类（前缀替换 → Avalonia.UIElement 不存在；InputElement 是输入层）
        ["System.Windows.UIElement"] = "global::Avalonia.Input.InputElement",
        ["System.Windows.Media.Imaging.BitmapImage"] = "global::Avalonia.Media.Imaging.Bitmap",
        ["System.Windows.Media.Imaging.BitmapSource"] = "global::Avalonia.Media.Imaging.Bitmap",
        ["System.Windows.Controls.ContextMenuEventArgs"] = "global::Avalonia.Input.ContextRequestedEventArgs",
        ["System.Windows.Media.ImageSource"] = "global::Avalonia.Media.IImage",
    };

    /// <summary>C# 事件成员重命名（+= / -= 右侧）。</summary>
    public static readonly IReadOnlyDictionary<string, string> CSharpEventRenames = new Dictionary<string, string>
    {
        ["MouseDown"] = "PointerPressed",
        ["MouseUp"] = "PointerReleased",
        ["PreviewMouseDown"] = "PointerPressed",
        ["PreviewMouseUp"] = "PointerReleased",
        ["MouseLeftButtonDown"] = "PointerPressed",
        ["MouseLeftButtonUp"] = "PointerReleased",
        ["MouseRightButtonDown"] = "PointerPressed",
        ["MouseRightButtonUp"] = "PointerReleased",
        ["MouseMove"] = "PointerMoved",
        ["PreviewMouseMove"] = "PointerMoved",
        ["MouseEnter"] = "PointerEntered",
        ["MouseLeave"] = "PointerExited",
        ["MouseWheel"] = "PointerWheelChanged",
        // 双击事件（XAML 侧同步重命名；处理器参数类型见 DoubleTappedArgMethods）
        ["MouseDoubleClick"] = "DoubleTapped",
        ["PreviewMouseDoubleClick"] = "DoubleTapped",
        // PasswordBox.PasswordChanged → TextBox.TextChanged（PasswordBox → TextBox 降级配套；
        // WPF 独有事件名，误伤面窄；ForkPlus SshPassphraseWindow 实测）
        ["PasswordChanged"] = "TextChanged",
    };

    /// <summary>
    /// 双击处理器方法名的包含式判据（MouseDoubleClick → DoubleTapped 时）：
    /// 处理器参数 MouseButtonEventArgs 应改写为 TappedEventArgs
    /// （Avalonia InputElement.DoubleTapped：EventHandler&lt;TappedEventArgs&gt;，已反射验证），
    /// 而非默认映射的 PointerPressedEventArgs。
    /// </summary>
    public static readonly string[] DoubleTappedArgMethodHints = { "DoubleClick", "Doubleclick", "doubleClick" };

    /// <summary>绑定表达式中应移除的 WPF 特有选项（Avalonia 不支持或语义不同）。</summary>
    public static readonly string[] BindingOptionsToRemove =
    {
        "UpdateSourceTrigger", "ValidatesOnDataErrors", "ValidatesOnExceptions",
        "ValidatesOnNotifyDataErrors", "NotifyOnValidationError", "NotifyOnSourceUpdated",
        "NotifyOnTargetUpdated", "IsAsync", "BindingGroupName", "BindsDirectlyToSource",
    };

    /// <summary>
    /// {x:Static 命令} → RepeatButton 模板部件名。
    /// Avalonia 官方 ScrollBar/Slider 模板经 x:Name="PART_*" 约定驱动（无 Command 特性，
    /// 对照 Avalonia.Themes.Fluent ScrollBar.xaml 源码验证）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> XStaticCommandToPart = new Dictionary<string, string>
    {
        ["ScrollBar.LineUpCommand"] = "PART_LineUpButton",
        ["ScrollBar.LineDownCommand"] = "PART_LineDownButton",
        ["ScrollBar.PageUpCommand"] = "PART_PageUpButton",
        ["ScrollBar.PageDownCommand"] = "PART_PageDownButton",
        // 水平方向：Avalonia Fluent 水平模板同样使用 LineUp/LineDown 部件名（Column left/right）
        ["ScrollBar.LineLeftCommand"] = "PART_LineUpButton",
        ["ScrollBar.LineRightCommand"] = "PART_LineDownButton",
        ["ScrollBar.PageLeftCommand"] = "PART_PageUpButton",
        ["ScrollBar.PageRightCommand"] = "PART_PageDownButton",
        ["Slider.IncreaseLarge"] = "PART_IncreaseLarge",
        ["Slider.DecreaseLarge"] = "PART_DecreaseLarge",
    };

    /// <summary>WPF 系统颜色键 → 近似固定色（Avalonia 无系统主题键；Light 主题近似值）。</summary>
    public static readonly IReadOnlyDictionary<string, string> WpfSystemColorFallbacks = new Dictionary<string, string>
    {
        ["SystemColors.ControlTextBrushKey"] = "#FF000000",
        ["SystemColors.GrayTextBrushKey"] = "#FF6D6D6D",
        ["SystemColors.InactiveSelectionHighlightBrushKey"] = "#FF9A9A9A",
        ["SystemColors.ControlDarkBrushKey"] = "#FFACA899",
        ["SystemColors.HighlightBrushKey"] = "#FF0078D4",
        ["SystemColors.WindowBrushKey"] = "#FFFFFFFF",
        ["SystemColors.WindowTextBrushKey"] = "#FF000000",
        ["SystemColors.ControlBrushKey"] = "#FFF0F0F0",
        ["SystemColors.ActiveBorderBrushKey"] = "#FFB4B4B4",
        ["SystemColors.InactiveBorderBrushKey"] = "#FFB4B4B4",
    };

    /// <summary>WPF 特效属性 → Avalonia 属性（DropShadowEffect 无 ShadowDepth，用 OffsetY）。</summary>
    public static readonly IReadOnlyDictionary<string, string> EffectAttributeRenames = new Dictionary<string, string>
    {
        ["ShadowDepth"] = "OffsetY",
        ["Direction"] = "", // WPF 光照角度方向，Avalonia 无对应 → 删除
    };

    /// <summary>
    /// Setter/触发器属性名重命名（先经 <see cref="NormalizePropertyPath"/> 归一化，
    /// 值由调用方按语义转换）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> SetterPropertyRenames = new Dictionary<string, string>
    {
        // WPF Visibility → Avalonia IsVisible（值 Visible→True / Collapsed|Hidden→False）
        ["Visibility"] = "IsVisible",
    };

    /// <summary>
    /// Avalonia Fluent 主题必定提供类型键 ControlTheme 的核心控件
    /// （逐一核对 Avalonia.Themes.Fluent 的控件主题文件）。
    /// setter-only 命名主题引用这些类型时可安全自动补 BasedOn。
    /// </summary>
    public static readonly IReadOnlySet<string> DefaultThemeTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        // 按钮/选择
        "Button", "RepeatButton", "ToggleButton", "CheckBox", "RadioButton",
        "ComboBox", "ComboBoxItem", "ListBox", "ListBoxItem", "DropDownButton", "SplitButton",
        "Expander", "TabControl", "TabItem", "TreeView", "TreeViewItem",
        "Menu", "MenuFlyout", "ContextMenu", "MenuItem", "Separator",
        // 输入
        "TextBox", "PasswordBox", "AutoCompleteBox", "NumericUpDown",
        "TextBlock", "SelectableTextBlock", "Label",
        // 范围/滚动
        "Slider", "ProgressBar", "ScrollBar", "ScrollViewer", "Spinner",
        "DatePicker", "TimePicker", "Calendar", "CalendarButton", "CalendarDayButton",
        "CalendarItem", "CalendarDatePicker",
        // 容器/原语
        "ContentControl", "HeaderedContentControl", "ItemsControl", "HeaderedItemsControl",
        "Border", "Grid", "StackPanel", "WrapPanel", "DockPanel", "UniformGrid",
        "Canvas", "Panel", "Decorator", "Viewbox", "VirtualizingStackPanel",
        "SplitView", "SplitViewPane", "GridSplitter", "BusyArea",
        // 反馈
        "ToolTip", "FlyoutPresenter", "PopupRoot", "OverlayPopupHost",
        "DataGrid", "DataGridCell", "DataGridRow", "DataGridRowHeader", "DataGridColumnHeader",
        // Window/WindowBase 不在 Fluent 主题内（由 WindowBase 主题提供），未列入
        "Window", "WindowBase",
    };

    /// <summary>
    /// 反编译 DataTrigger/Condition 的 Binding Path 中可安全视作「控件属性」的名字
    /// （WPF UIElement/Control/常见控件状态属性全集，用于伪类/匹配器判定；
    /// 未列入的名字按 DataContext 属性处理 → 人工）。
    /// </summary>
    public static readonly IReadOnlySet<string> ControlPropertyNames = new HashSet<string>(StringComparer.Ordinal)
    {
        // UIElement 状态
        "IsMouseOver", "IsMouseDirectlyOver", "IsMouseCaptured", "IsStylusOver", "IsHitTestVisible",
        "IsEnabled", "IsFocused", "IsKeyboardFocused", "IsKeyboardFocusWithin", "IsKeyboardLocked",
        "IsVisible", "IsManipulationEnabled", "Focusable", "IsLoaded", "IsInitialized",
        // ButtonBase/ToggleButton/Button
        "IsPressed", "IsChecked", "IsIndeterminate", "IsDefault", "IsDefaulted", "IsCancel",
        "IsHighlighted", "ClickMode",
        // 选择/展开/弹出
        "IsSelected", "IsSelectionActive", "IsExpanded", "IsDropDownOpen", "IsSubmenuOpen",
        "IsEditable", "IsReadOnly", "StaysOpen", "IsActive", "IsOpen", "IsDragging",
        "IsEditableItem", "IsActiveItem", "IsSingleSelectionFollowsFocus",
        // 内容/项
        "HasItems", "HasContent", "HasHeader", "HasError", "Text", "Icon", "Hint", "Header",
        "WordWrap", "Orientation", "FlowDirection", "SortDirection", "Grouping",
        // ScrollBar/Slider/RangeBase
        "Value", "Minimum", "Maximum", "LargeChange", "SmallChange",
    };
}
