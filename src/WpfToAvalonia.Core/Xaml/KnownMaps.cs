namespace WpfToAvalonia.Core.Xaml;

/// <summary>WPF → Avalonia 的已知映射规则表。</summary>
public static class KnownMaps
{
    public const string AvaloniaNs = "https://github.com/avaloniaui";

    /// <summary>DataGrid 主题（Avalonia.Controls.DataGrid 包内嵌 XAML，须由 App 引入）。</summary>
    public const string DataGridThemeSource = "avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml";

    /// <summary>XAML 命名空间 URI 替换（在解析前做纯文本替换，安全且保留全部格式）。</summary>
    public static readonly IReadOnlyDictionary<string, string> NamespaceUris = new Dictionary<string, string>
    {
        ["http://schemas.microsoft.com/winfx/2006/xaml/presentation"] = AvaloniaNs,
        ["http://schemas.microsoft.com/netfx/2007/xaml/presentation"] = AvaloniaNs,
        ["http://schemas.microsoft.com/netfx/2009/xaml/presentation"] = AvaloniaNs,
    };

    /// <summary>元素重命名（WPF 独有 → Avalonia 对应物）。</summary>
    public static readonly IReadOnlyDictionary<string, string> ElementRenames = new Dictionary<string, string>
    {
        ["Label"] = "TextBlock",
        ["Page"] = "UserControl",
    };

    /// <summary>无核心等价物、需人工处理的元素。</summary>
    public static readonly IReadOnlySet<string> UnsupportedElements = new HashSet<string>
    {
        "RichTextBox", "FlowDocument", "FlowDocumentScrollViewer", "FlowDocumentReader",
        "Frame", "ToolBar", "ToolBarTray", "StatusBar", "WindowsFormsHost", "WebBrowser",
        "InkCanvas", "Viewport3D", "MediaElement", "AdornerDecorator", "Ribbon", "JumpList",
    };

    /// <summary>特性重命名。</summary>
    public static readonly IReadOnlyDictionary<string, string> AttributeRenames = new Dictionary<string, string>
    {
        ["ToolTipService.ToolTip"] = "ToolTip.Tip",
        ["ToolTipService.Placement"] = "",           // 丢弃
        ["ToolTipService.InitialShowDelay"] = "",    // 丢弃
        ["WindowStyle"] = "SystemDecorations",       // 仅 None 值语义等价
    };

    /// <summary>直接丢弃的 WPF 特有特性（Avalonia 无对应或无意义）。</summary>
    public static readonly IReadOnlySet<string> DropAttributes = new HashSet<string>
    {
        "SnapsToDevicePixels", "UseLayoutRounding", "FocusManager.IsFocusScope",
        "TextOptions.TextFormattingMode", "TextOptions.TextRenderingMode",
        "TextOptions.TextHintingMode", "x:Uid", "ShowActivated",
    };

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
    };

    /// <summary>右键事件需要运行时通过 PointerEventArgs 判断按键。</summary>
    public static readonly IReadOnlySet<string> RightButtonEvents = new HashSet<string>
    { "MouseRightButtonDown", "MouseRightButtonUp" };

    /// <summary>属性触发器 → Avalonia 伪类选择器。</summary>
    public static readonly IReadOnlyDictionary<string, string> TriggerPseudoClasses = new Dictionary<string, string>
    {
        ["IsMouseOver"] = "pointerover",
        ["IsPressed"] = "pressed",
        ["IsEnabled"] = "disabled",      // 需 Value=False
        ["IsChecked"] = "checked",
        ["IsSelected"] = "selected",
        ["IsFocused"] = "focus",
        ["IsKeyboardFocused"] = "focus",
    };

    /// <summary>触发的 Value 必须与伪类激活条件匹配（IsEnabled 需要 False）。</summary>
    public static bool TriggerValueMatches(string property, string value)
    {
        if (property == "IsEnabled") return value is "False" or "false";
        return value is "True" or "true";
    }

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
        ["System.Windows.Documents"] = "Avalonia.Documents",
        ["System.Windows.Interactivity"] = "Avalonia.Xaml.Interactivity",
        ["Microsoft.Xaml.Behaviors.Wpf"] = "Avalonia.Xaml.Behaviors",
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
    };

    /// <summary>全限定类型映射（System.Windows.Point 等）。</summary>
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
        ["System.Windows.Media.FontWeights"] = "global::Avalonia.Media.FontWeights",
        ["System.Windows.HorizontalAlignment"] = "global::Avalonia.Layout.HorizontalAlignment",
        ["System.Windows.VerticalAlignment"] = "global::Avalonia.Layout.VerticalAlignment",
        ["System.Windows.Visibility"] = "global::Avalonia.Visibility",
        ["System.Windows.Threading.Dispatcher"] = "global::Avalonia.Threading.Dispatcher",
        ["System.Windows.Threading.DispatcherTimer"] = "global::Avalonia.Threading.DispatcherTimer",
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
    };

    /// <summary>绑定表达式中应移除的 WPF 特有选项（Avalonia 不支持或语义不同）。</summary>
    public static readonly string[] BindingOptionsToRemove =
    {
        "UpdateSourceTrigger", "ValidatesOnDataErrors", "ValidatesOnExceptions",
        "ValidatesOnNotifyDataErrors", "NotifyOnValidationError", "NotifyOnSourceUpdated",
        "NotifyOnTargetUpdated", "IsAsync", "BindingGroupName", "BindsDirectlyToSource",
    };
}
