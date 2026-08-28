using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using WpfToAvalonia.Core.Model;

namespace WpfToAvalonia.Core.Xaml;

public sealed class XamlTransformResult
{
    public required string Xaml { get; init; }
    public IReadOnlyList<ConversionNote> Notes { get; init; } = Array.Empty<ConversionNote>();
    public bool UsesDataGrid { get; init; }
    public string? StartupUri { get; init; }
}

/// <summary>
/// 把 WPF XAML 转换为 Avalonia XAML。
/// 核心架构（与 Avalonia 官方 Fluent 主题结构一致，均经编译验证）：
/// 1) 命名空间 URI 文本替换（零结构破坏）；
/// 2) 反编译式所有者前缀归一化（Setter/Trigger/TemplateBinding）；
/// 3) Style → ControlTheme 体系：keyed 样式保键转 ControlTheme（引用处 Style= → Theme=），
///    keyless 样式补 x:Key="{x:Type X}" 经资源链隐式生效（StyledElement.GetEffectiveTheme
///    以 StyleKey 查资源）；类型键/命名 ControlTheme 均可 BasedOn={StaticResource {x:Type X}}；
/// 4) ControlTemplate.Triggers → "^:pseudo /template/ Type#Part" 嵌套样式；
///    Style.Triggers → "^:pseudo" 嵌套样式；MultiTrigger → 伪类链；
/// 5) Visibility→IsVisible、Geometry→StreamGeometry、DataTemplate 迁移、资产 URI 重写。
/// </summary>
public sealed partial class XamlTransformer
{
    private readonly string _assemblyName;
    private readonly IReadOnlySet<string> _styleKeys;
    private readonly IReadOnlySet<string> _typeThemeKeys;
    private readonly IReadOnlySet<string> _booleanToVisibilityKeys;

    private readonly List<ConversionNote> _notes = new();
    private string _file = "";
    private string _fileDir = "";
    private XElement _root = null!;

    private static readonly XNamespace XNs = KnownMaps.XNs;

    private static readonly HashSet<string> AssetAttributes = new(StringComparer.OrdinalIgnoreCase)
    { "Source", "Icon", "SelectedSource", "ImageSource", "PlaySource" };

    /// <summary>Style/Theme 引用：{StaticResource key} 或 {StaticResource {x:Type X}}。</summary>
    [GeneratedRegex(@"^\{(?<kind>Static|Dynamic)Resource\s+(?<key>(?:\{x:Type\s+[^}]+\})|[^{}]+)\}$")]
    private static partial Regex StyleResourceRegex();

    /// <summary>{TemplateBinding Path} / {TemplateBinding Path=Prop}。</summary>
    [GeneratedRegex(@"\{TemplateBinding\s+(?:Path\s*=\s*)?(?<path>[^,}]+)\}")]
    private static partial Regex TemplateBindingRegex();

    /// <summary>绑定中的 WPF 特有选项。</summary>
    [GeneratedRegex(@"(,\s*)?(UpdateSourceTrigger|ValidatesOnDataErrors|ValidatesOnExceptions|ValidatesOnNotifyDataErrors|NotifyOnValidationError|NotifyOnSourceUpdated|NotifyOnTargetUpdated|IsAsync|BindingGroupName|BindsDirectlyToSource)\s*=\s*[^,}]*(?=[,}])")]
    private static partial Regex BindingOptionRemoval();

    /// <summary>BooleanToVisibility 转换器引用（Visibility 绑定 → IsVisible 直接绑定）。</summary>
    [GeneratedRegex(@",?\s*Converter\s*=\s*\{(?:Static|Dynamic)Resource\s+(?<key>[^}]+)\}")]
    private static partial Regex BindingConverterRegex();

    [GeneratedRegex(@"^\{x:Type\s+(?<type>[^}]+)\}$")]
    private static partial Regex XTypeRegex();

    public XamlTransformer(string assemblyName,
        IReadOnlySet<string>? styleKeys = null,
        IReadOnlySet<string>? typeThemeKeys = null,
        IReadOnlySet<string>? booleanToVisibilityKeys = null)
    {
        _assemblyName = assemblyName;
        _styleKeys = styleKeys ?? new HashSet<string>();
        _typeThemeKeys = typeThemeKeys ?? new HashSet<string>();
        _booleanToVisibilityKeys = booleanToVisibilityKeys ?? new HashSet<string>();
    }

    public XamlTransformResult Transform(string source, string relativePath)
    {
        _notes.Clear();
        _file = relativePath.Replace('\\', '/');
        _fileDir = Path.GetDirectoryName(_file)?.Replace('\\', '/') ?? "";

        var text = source;
        foreach (var (oldUri, newUri) in KnownMaps.NamespaceUris)
            text = text.Replace(oldUri, newUri);

        var doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var root = doc.Root ?? throw new InvalidOperationException("XAML 根元素缺失");
        _root = root;

        bool usesDataGrid = false;
        string? startupUri = null;
        VisitElement(root, ref usesDataGrid, ref startupUri);

        var sw = new StringWriter();
        doc.Declaration = null; // Avalonia axaml 不需要 XML 声明
        doc.Save(sw, SaveOptions.None);
        var output = sw.ToString();
        if (output.StartsWith("<?xml", StringComparison.Ordinal))
            output = output[(output.IndexOf("?>", StringComparison.Ordinal) + 2)..].TrimStart('\r', '\n');
        return new XamlTransformResult
        {
            Xaml = output,
            Notes = _notes.ToList(),
            UsesDataGrid = usesDataGrid,
            StartupUri = startupUri,
        };
    }

    // ————————————————————————————————— 元素遍历 —————————————————————————————————

    private void VisitElement(XElement el, ref bool usesDataGrid, ref string? startupUri)
    {
        // —— 反编译冗余 xmlns：非根元素声明上提/去除（Avalonia 编译器仅允许根声明，AXN0003） ——
        if (!ReferenceEquals(el, _root))
            HoistNamespaceDeclarations(el);

        var ns = el.Name.NamespaceName;
        var local = el.Name.LocalName;
        var isAvaloniaNs = ns == KnownMaps.AvaloniaNs;

        if (isAvaloniaNs)
        {
            // —— GridView 列视图体系：整体注释移除（Avalonia 无列视图；DataGrid 是另一套机制） ——
            if (KnownMaps.GridViewFamilyElements.Contains(local))
            {
                ReplaceWithComment(el, "XAML-GRIDVIEW",
                    $"<{local}>（WPF ListView 列视图体系）在 Avalonia 核心无等价物，已移除；" +
                    "多列列表请用 ListBox + DataTemplate（Grid 列布局）重建，列头用 Grid 手工实现（或引入 Avalonia.Controls.DataGrid 包）。");
                return;
            }

            // —— AdornerDecorator：解包（WPF 模板装饰层包装，Avalonia 无装饰器层） ——
            if (local == "AdornerDecorator" && el.Parent != null)
            {
                UnwrapAdornerDecorator(el, ref usesDataGrid, ref startupUri);
                return;
            }

            // —— 无等价物元素：保留 + 人工提示 ——
            if (KnownMaps.UnsupportedElements.Contains(local))
            {
                Note(el, NoteSeverity.Manual, "XAML-UNSUPPORTED-ELEMENT",
                    $"元素 <{local}> 在 Avalonia 核心中无直接等价物，需人工替换（社区库或自定义控件）。");
            }

            // —— 元素重命名 ——
            if (KnownMaps.ElementRenames.TryGetValue(local, out var renamed))
            {
                if (local == "Label")
                {
                    // 有元素级内容 → ContentControl（保留 Content）；纯文本 → TextBlock（Content → Text）
                    var hasElementContent = el.Elements().Any(e => e.Name.LocalName != "Label.Content");
                    var target = hasElementContent ? "ContentControl" : renamed;
                    el.Name = XName.Get(target, ns);
                    Note(el, NoteSeverity.Info, "XAML-ELEMENT-RENAME", $"Label → {target}。");
                    if (target == "TextBlock")
                    {
                        var content = el.Attribute("Content");
                        if (content != null)
                        {
                            content.Remove();
                            el.SetAttributeValue("Text", content.Value);
                        }
                    }
                    local = target;
                }
                else
                {
                    el.Name = XName.Get(renamed, ns);
                    if (local == "Geometry" || local == "PathGeometry")
                    {
                        // PathGeometry Figures（迷你语言字符串）→ StreamGeometry 内容
                        var figures = el.Attribute("Figures");
                        if (figures != null && !el.Elements().Any())
                        {
                            figures.Remove();
                            el.Value = figures.Value;
                        }
                        else if (el.Elements().Any())
                        {
                            Note(el, NoteSeverity.Manual, "XAML-GEOMETRY",
                                "PathGeometry 含 PathFigure 子元素，Avalonia StreamGeometry 仅支持迷你语言，需人工重写。");
                        }
                    }

                    // —— 属性元素所有者前缀随元素重命名同步：<Hyperlink.NavigateUri> → <HyperlinkButton.NavigateUri> ——
                    var ownerPrefix = local + ".";
                    foreach (var propEl in el.Elements()
                                 .Where(c => c.Name.LocalName.StartsWith(ownerPrefix, StringComparison.Ordinal)).ToList())
                    {
                        var newLocal = renamed + "." + propEl.Name.LocalName[(local.Length + 1)..];
                        propEl.Name = XName.Get(newLocal, propEl.Name.NamespaceName);
                    }

                    // —— ListView → ListBox：View 特性/属性元素（GridView 列视图）移除 ——
                    if (local == "ListView")
                    {
                        var viewAttr = el.Attribute("View");
                        if (viewAttr != null)
                        {
                            var viewValue = viewAttr.Value;
                            viewAttr.Remove();
                            Note(el, NoteSeverity.Manual, "XAML-LISTVIEW-VIEW",
                                $"ListView.View=\"{viewValue}\" 已移除；Avalonia ListBox 无列视图，多列行请用 DataTemplate（Grid 列）重建。");
                        }
                        var viewEl = el.Elements().FirstOrDefault(c =>
                            c.Name.LocalName is "ListView.View" or "View" or "ListBox.View");
                        if (viewEl != null)
                        {
                            viewEl.Remove();
                            Note(el, NoteSeverity.Manual, "XAML-LISTVIEW-VIEW",
                                "<ListView.View>（GridView 列布局）已移除；Avalonia ListBox 用 DataTemplate 重建多列行，DisplayMemberBinding → 绑定表达式。");
                        }
                    }

                    if (local == "RichTextBox")
                    {
                        Note(el, NoteSeverity.Manual, "XAML-RICHTEXTBOX",
                            "RichTextBox → TextBox（纯文本降级）；Document/Selection/TextRange/CaretPosition API 无等价，代码侧需人工改写（富文本可考虑 AvaloniaEdit）。");
                    }

                    if (local == "Hyperlink")
                    {
                        Note(el, NoteSeverity.Manual, "XAML-HYPERLINK",
                            "Hyperlink → HyperlinkButton；NavigateUri 属性保留，RequestNavigate 事件已移除——Click 处理器内用 Process.Start 打开链接（Avalonia 不会自动打开）。");
                    }

                    Note(el, NoteSeverity.Info, "XAML-ELEMENT-RENAME", $"{local} → {renamed}。");
                    local = renamed;
                }
            }

            if (local.StartsWith("DataGrid", StringComparison.Ordinal))
            {
                usesDataGrid = true;
                Note(el, NoteSeverity.Info, "XAML-DATAGRID",
                    "DataGrid 使用 Avalonia.Controls.DataGrid 包（保持在默认命名空间；工具已在 App.axaml 注入主题 StyleInclude）。");
            }

            if (local == "Application")
            {
                var sui = el.Attribute("StartupUri");
                if (sui != null)
                {
                    startupUri = sui.Value;
                    sui.Remove();
                    Note(el, NoteSeverity.Info, "XAML-STARTUPURI",
                        "StartupUri 已移除；启动窗口改由 App.OnFrameworkInitializationCompleted 创建（工具已生成引导代码）。");
                }
                var sdm = el.Attribute("ShutdownMode");
                if (sdm != null)
                {
                    sdm.Remove();
                    Note(el, NoteSeverity.Info, "XAML-SHUTDOWNMODE",
                        $"Application.ShutdownMode({sdm.Value}) 已移除；Avalonia 由 IClassicDesktopStyleApplicationLifetime.ShutdownMode 控制。");
                }
                EnsureFluentTheme(el);
            }

            // FrameworkElement.Triggers：直接子元素容器（非 Style/Template 内）
            if (local == "Triggers" && el.Parent != null &&
                el.Parent.Name.LocalName is not ("Style" or "ControlTheme" or "ControlTemplate" or "DataTemplate"))
            {
                Note(el, NoteSeverity.Manual, "XAML-ELEMENT-TRIGGERS",
                    "FrameworkElement.Triggers 不被 Avalonia 支持，需改写为 Style/伪类或代码。");
            }

            if (local is "Storyboard" or "EventTrigger" or "BeginStoryboard")
            {
                Note(el, NoteSeverity.Manual, "XAML-STORYBOARD",
                    "Storyboard/EventTrigger 动画体系不同，需改写为 Avalonia Animation/Transitions（CSS 式关键帧）。");
            }

            if (local == "MultiBinding")
            {
                Note(el, NoteSeverity.Manual, "XAML-MULTIBINDING",
                    "Avalonia 核心无 MultiBinding，需用多绑定转换器合并或改写为计算属性。");
            }

            // 合并字典：带 Source 的 ResourceDictionary → ResourceInclude
            if (local == "ResourceDictionary" && el.Attribute("Source") != null)
            {
                var src = el.Attribute("Source")!;
                el.Name = XName.Get("ResourceInclude", ns);
                src.Value = NormalizeDictionarySource(src.Value);
                Note(el, NoteSeverity.Info, "XAML-MERGED-DICT",
                    $"带 Source 的 ResourceDictionary → ResourceInclude，Source 归一化为 {src.Value}。");
            }

            // 属性元素重命名：<ListBox.ItemContainerStyle> → <ListBox.ItemContainerTheme>
            if (local.Length > 0 && local.Contains('.') &&
                KnownMaps.PropertyElementRenames.TryGetValue(local[(local.LastIndexOf('.') + 1)..],
                    out var propElRenamed))
            {
                var newLocal = local[..(local.LastIndexOf('.') + 1)] + propElRenamed;
                el.Name = XName.Get(newLocal, ns);
                Note(el, NoteSeverity.Info, "XAML-PROPERTY-ELEMENT-RENAME", $"{local} → {newLocal}。");
                local = newLocal;
            }

            // DropShadowEffect 属性差异：ShadowDepth → OffsetY；Direction 删除
            if (local == "DropShadowEffect" || local == "BlurEffect")
            {
                foreach (var ea in el.Attributes().ToList())
                {
                    if (ea.IsNamespaceDeclaration) continue;
                    if (KnownMaps.EffectAttributeRenames.TryGetValue(ea.Name.LocalName, out var effectTarget))
                    {
                        ea.Remove();
                        if (effectTarget.Length > 0) el.SetAttributeValue(effectTarget, ea.Value);
                        Note(el, NoteSeverity.Info, "XAML-EFFECT-ATTR",
                            $"特效属性 {ea.Name.LocalName} → {(effectTarget.Length == 0 ? "（已移除）" : effectTarget)}。");
                    }
                }
            }

            // BooleanToVisibilityConverter 资源定义：Avalonia 直接用 IsVisible 绑定，资源删除
            if (local == "BooleanToVisibilityConverter")
            {
                var key = el.Attribute(XNs.GetName("Key"))?.Value;
                el.Remove();
                Note(el, NoteSeverity.Info, "XAML-B2V-RESOURCE",
                    "WPF 内建 BooleanToVisibilityConverter 已移除；所有 Visibility 绑定已改为 IsVisible 直接绑定 bool。");
                if (key != null) _ = key;
            }
        }

        // —— 递归子元素（先处理子树，再转换自身语义） ——
        foreach (var child in el.Elements().ToList())
            VisitElement(child, ref usesDataGrid, ref startupUri);

        // —— 特性处理（所有命名空间的元素都需要） ——
        VisitAttributes(el, ref startupUri);

        if (!isAvaloniaNs) return;

        // —— Setter 通用归一化（Property 前缀/丢弃/Visibility 值） ——
        if (local == "Setter")
            NormalizeSetter(el);

        // —— DataTemplate：DataType 触发器转换 + keyless 模板迁移 ——
        if (local == "DataTemplate")
            ConvertDataTemplateTriggers(el);

        // —— Resources 内 keyless DataTemplate → 宿主 .DataTemplates ——
        VisitResourcesContainer(el);

        // —— Style → ControlTheme（含 Style.Triggers 与 ControlTemplate.Triggers） ——
        if (local == "Style" || local == "ControlTheme")
            ConvertStyle(el);
    }

    // ————————————————————————————————— 特性处理 —————————————————————————————————

    private void VisitAttributes(XElement el, ref string? startupUri)
    {
        foreach (var attr in el.Attributes().ToList())
        {
            if (attr.IsNamespaceDeclaration) continue;
            var name = attr.Name.LocalName;
            var value = attr.Value;

            // x:Uid
            if (attr.Name.Namespace == XNs && name == "Uid")
            {
                attr.Remove();
                Note(el, NoteSeverity.Info, "XAML-DROP-ATTR", "x:Uid 已移除（Avalonia 本地化机制不同）。");
                continue;
            }

            if (KnownMaps.DropAttributes.Contains(name))
            {
                attr.Remove();
                Note(el, NoteSeverity.Info, "XAML-DROP-ATTR", $"特性 {name} 在 Avalonia 无意义，已移除。");
                continue;
            }

            if (KnownMaps.ManualDropAttributes.TryGetValue(name, out var manualHint))
            {
                attr.Remove();
                Note(el, NoteSeverity.Manual, "XAML-DROP-ATTR-MANUAL", manualHint);
                continue;
            }

            // Visibility → IsVisible（含 BooleanToVisibility 转换器绑定）
            if (name == "Visibility")
            {
                var rewritten = ConvertVisibilityValue(value, out var severity, out var message);
                if (rewritten != null)
                {
                    attr.Remove();
                    el.SetAttributeValue("IsVisible", rewritten);
                    Note(el, severity, "XAML-VISIBILITY", message);
                }
                continue;
            }

            // LayoutTransform：需换 LayoutTransformControl 包装控件
            if (name == "LayoutTransform")
            {
                var v = value;
                attr.Remove();
                Note(el, NoteSeverity.Manual, "XAML-LAYOUTTRANSFORM",
                    $"LayoutTransform=\"{v}\" 已移除；Avalonia 需改用 LayoutTransformControl 包装该元素。");
                continue;
            }

            if (name == "WindowStyle" && el.Name.LocalName is "Window" or "WindowBase")
            {
                var v = value.Trim();
                if (v == "None")
                {
                    el.SetAttributeValue("SystemDecorations", "None");
                    attr.Remove();
                    Note(el, NoteSeverity.Info, "XAML-WINDOWSTYLE", "WindowStyle=None → SystemDecorations=None。");
                }
                else
                {
                    Note(el, NoteSeverity.Warning, "XAML-WINDOWSTYLE",
                        $"WindowStyle={v} 无精确等价（SystemDecorations=Full/BorderOnly），请人工确认。");
                }
                continue;
            }

            if (name == "AllowsTransparency" && value.Trim() == "True")
            {
                attr.Remove();
                Note(el, NoteSeverity.Manual, "XAML-TRANSPARENCY",
                    "AllowsTransparency 已移除；需设置 TransparencyLevelHint 并确认平台支持。");
                continue;
            }

            // Style={StaticResource key} → Theme={StaticResource key}（keyed 样式已统一转为 ControlTheme）
            if (name == "Style")
            {
                var m = StyleResourceRegex().Match(value);
                if (m.Success)
                {
                    var key = m.Groups["key"].Value.Trim();
                    // 类型键引用随元素重命名同步（{x:Type Label} → {x:Type TextBlock}）
                    if (key.StartsWith("{x:Type", StringComparison.Ordinal))
                        key = RemapTypeKey(key, out var remapped);
                    var kind = m.Groups["kind"].Value;
                    var newValue = $"{{{kind}Resource {key}}}";
                    attr.Remove();
                    el.SetAttributeValue("Theme", newValue);
                    var known = _styleKeys.Contains(key) ||
                                (key.StartsWith("{x:Type", StringComparison.Ordinal) && _typeThemeKeys.Contains(key));
                    Note(el, known ? NoteSeverity.Info : NoteSeverity.Warning, "XAML-STYLE-REFERENCE",
                        $"Style=\"{value}\" → Theme=\"{newValue}\"（keyed 样式已转为 ControlTheme，经 Theme 属性引用）。" +
                        (known ? "" : "引用键未在扫描中发现，请确认其定义已转为 ControlTheme。"));
                }
                else
                {
                    attr.Remove();
                    Note(el, NoteSeverity.Manual, "XAML-STYLE-REFERENCE",
                        $"Style=\"{value}\" 无法转换（非资源引用）；Avalonia 控件没有 Style 属性，请人工改用 Theme/Classes。");
                }
                continue;
            }

            // Theme={StaticResource {x:Type Label}} 引用键同步重命名
            if (name == "Theme")
            {
                var m = StyleResourceRegex().Match(value);
                if (m.Success)
                {
                    var key = m.Groups["key"].Value.Trim();
                    var remapped = false;
                    if (key.StartsWith("{x:Type", StringComparison.Ordinal))
                        key = RemapTypeKey(key, out remapped);
                    if (remapped)
                    {
                        var kind = m.Groups["kind"].Value;
                        attr.Value = $"{{{kind}Resource {key}}}";
                    }
                }
            }

            // BasedOn：x:Type 直接形式 → StaticResource 形式
            if (name == "BasedOn" && XTypeRegex().IsMatch(value.Trim()))
            {
                var t = XTypeRegex().Match(value.Trim()).Groups["type"].Value.Trim();
                attr.Value = $"{{StaticResource {{x:Type {t}}}}}";
                Note(el, NoteSeverity.Info, "XAML-BASEDON",
                    $"BasedOn={{x:Type {t}}} → BasedOn={{StaticResource {{x:Type {t}}}}}。");
                continue;
            }

            // DataType（DataTemplate）：{x:Type X} → X
            if (name == "DataType" && el.Name.LocalName == "DataTemplate")
            {
                var m = XTypeRegex().Match(value.Trim());
                if (m.Success)
                {
                    attr.Value = m.Groups["type"].Value.Trim();
                    Note(el, NoteSeverity.Info, "XAML-DATATYPE",
                        $"DataTemplate.DataType {{x:Type {attr.Value}}} → {attr.Value}（Avalonia 同名属性，类型字符串直写）。");
                }
                continue;
            }

            // TargetType（ControlTemplate）：{x:Type X} → X
            if (name == "TargetType" && el.Name.LocalName == "ControlTemplate")
            {
                var m = XTypeRegex().Match(value.Trim());
                if (m.Success)
                {
                    attr.Value = m.Groups["type"].Value.Trim();
                    Note(el, NoteSeverity.Info, "XAML-TARGETTYPE",
                        $"ControlTemplate.TargetType {{x:Type {attr.Value}}} → {attr.Value}。");
                }
                continue;
            }

            // ResizeMode → CanResize（值同步转换；WPF 枚举 → Avalonia bool）
            //（反射验证 Avalonia 12 无 ResizeMode 枚举；ForkPlus 24 个 XAML 实测）
            if (name == "ResizeMode")
            {
                var (val, lossy) = KnownMaps.ResizeModeToCanResize(value);
                if (val != null)
                {
                    attr.Remove();
                    if (el.Attribute("CanResize") == null)
                        el.SetAttributeValue("CanResize", val);
                    Note(el, lossy ? NoteSeverity.Warning : NoteSeverity.Info, "XAML-RESIZEMODE",
                        $"ResizeMode=\"{value.Trim()}\" → CanResize=\"{val}\"" +
                        (lossy
                            ? "（语义有损：CanMinimize 的最小化保留靠系统菜单、CanResizeWithGrip 的 grip 无等价）。"
                            : "（WPF ResizeMode 枚举 → Avalonia CanResize bool）。"));
                }
                continue;
            }

            // 事件重命名（无命名空间属性不继承默认 ns）
            if (KnownMaps.XamlEventRenames.TryGetValue(name, out var ev))
            {
                var right = KnownMaps.RightButtonEvents.Contains(name);
                var handler = attr.Value;
                attr.Remove();
                if (el.Attribute(ev) != null)
                {
                    el.SetAttributeValue(ev, handler);
                    Note(el, NoteSeverity.Warning, "XAML-EVENT-MERGE",
                        $"{name} 与其他鼠标事件均映射到 {ev}，仅保留 {handler}；右键判断需在处理器内用 e.GetCurrentPoint(...).Properties.IsRightButton 区分。");
                }
                else
                {
                    el.SetAttributeValue(ev, handler);
                    Note(el, right ? NoteSeverity.Warning : NoteSeverity.Info, "XAML-EVENT-RENAME",
                        $"事件 {name} → {ev}。" + (right ? "右键判断需改为 e.GetCurrentPoint(...).Properties.IsRightButton。" : string.Empty));
                }
                continue;
            }

            // x:Static 转换（命令→PART 部件 / RelativeSource.Self / 系统颜色键）
            if (value.Contains("{x:Static", StringComparison.Ordinal) &&
                ConvertXStatic(el, attr, name, value))
            {
                continue;
            }

            // 值重写：资产 URI / 字体 / TemplateBinding 归一化
            if (ShouldRewriteAsset(name, value))
            {
                var rewritten = RewriteAsset(value);
                if (rewritten.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                    rewritten = Regex.Replace(rewritten, @"\.xaml$", ".axaml", RegexOptions.IgnoreCase);
                if (rewritten != value)
                {
                    attr.Value = rewritten;
                    Note(el, NoteSeverity.Info, "XAML-ASSET-URI", $"资产/资源 URI → {rewritten}");
                    continue;
                }
            }

            if ((name == "Source" || name == "Source+") &&
                (el.Name.LocalName is "ResourceInclude" or "ResourceDictionary" or "StyleInclude") &&
                !value.StartsWith("avares://", StringComparison.OrdinalIgnoreCase) &&
                !value.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
            {
                var rewritten = NormalizeDictionarySource(value);
                if (rewritten != value)
                {
                    attr.Value = rewritten;
                    Note(el, NoteSeverity.Info, "XAML-RESOURCE-SOURCE", $"字典 Source 归一化为 {rewritten}。");
                    continue;
                }
            }

            if (name == "FontFamily" && value.StartsWith('/'))
            {
                attr.Value = RewriteAsset(value);
                Note(el, NoteSeverity.Info, "XAML-FONT-URI", "自定义字体路径 → avares://。");
                continue;
            }

            // TemplateBinding 路径归一化：{TemplateBinding Border.BorderBrush} → {TemplateBinding BorderBrush}
            if (value.Contains("{TemplateBinding", StringComparison.Ordinal))
            {
                var rewritten = TemplateBindingRegex().Replace(value, m =>
                {
                    var path = m.Groups["path"].Value.Trim();
                    var normalized = KnownMaps.NormalizeTemplateBindingPath(path);
                    return normalized == path ? m.Value : m.Value.Replace(path, normalized);
                });
                if (rewritten != value)
                {
                    attr.Value = rewritten;
                    Note(el, NoteSeverity.Info, "XAML-TEMPLATEBINDING",
                        "TemplateBinding 属性路径已剥除所有者前缀（Avalonia 按目标类型解析）。");
                }
                // TemplateBinding 走完后无需再清理绑定选项
                continue;
            }

            // 绑定表达式清理（移除 WPF 特有选项）
            if (value.Contains("{Binding", StringComparison.Ordinal))
            {
                var cleaned = BindingOptionRemoval().Replace(value, "");
                if (cleaned != value)
                {
                    attr.Value = cleaned;
                    Note(el, NoteSeverity.Warning, "XAML-BINDING-OPTIONS",
                        "绑定中的 WPF 特有选项（UpdateSourceTrigger/ValidatesOn*/IsAsync 等）已移除；Avalonia 绑定更新时机与验证机制不同，请复核。");
                }
            }
        }

        // 常规重命名表（最后应用）
        foreach (var attr in el.Attributes().ToList())
        {
            if (attr.IsNamespaceDeclaration) continue;
            if (KnownMaps.AttributeRenames.TryGetValue(attr.Name.LocalName, out var target))
            {
                attr.Remove();
                if (target.Length > 0)
                    el.SetAttributeValue(target, attr.Value);
                Note(el, NoteSeverity.Info, "XAML-ATTR-RENAME",
                    $"特性 {attr.Name.LocalName} → {(target.Length == 0 ? "（已移除）" : target)}。");
            }
        }
    }

    /// <summary>x:Static 标记扩展转换。返回 true 表示特性已处理完毕。</summary>
    private bool ConvertXStatic(XElement el, XAttribute attr, string name, string value)
    {
        // 1) RepeatButton/模板按钮 Command="{x:Static ScrollBar.XxxCommand}" → x:Name="PART_*" 部件约定
        var m = Regex.Match(value, @"^\{x:Static\s+(?<member>[\w:.]+)\}$");
        if (m.Success)
        {
            var member = m.Groups["member"].Value;
            if (name == "Command" &&
                KnownMaps.XStaticCommandToPart.TryGetValue(member, out var part))
            {
                attr.Remove();
                if (el.Attribute(XNs.GetName("Name")) == null)
                    el.SetAttributeValue(XNs.GetName("Name"), part);
                Note(el, NoteSeverity.Info, "XAML-XSTATIC-COMMAND",
                    $"Command={{x:Static {member}}} → x:Name=\"{part}\"（Avalonia 官方模板经 PART_* 部件名驱动滚动/滑块行为，无命令绑定）。");
                return true;
            }

            if (member == "RelativeSource.Self")
            {
                attr.Value = "{RelativeSource Self}";
                Note(el, NoteSeverity.Info, "XAML-XSTATIC-RELATIVESOURCE",
                    "{x:Static RelativeSource.Self} → {RelativeSource Self}。");
                return true;
            }
            return false; // 带命名空间前缀的用户静态成员（commands:Enum.X）→ Avalonia 支持，保留
        }

        // 2) {DynamicResource {x:Static SystemColors.XxxKey}} → 近似固定色
        var sc = Regex.Match(value, @"\{(?:Static|Dynamic)Resource\s+\{x:Static\s+SystemColors\.(?<key>\w+)\}\}");
        if (sc.Success && KnownMaps.WpfSystemColorFallbacks.TryGetValue(
                "SystemColors." + sc.Groups["key"].Value, out var fallback))
        {
            attr.Value = fallback;
            Note(el, NoteSeverity.Warning, "XAML-SYSTEMCOLOR",
                $"{value} → {fallback}（Avalonia 无系统颜色主题键，已用 Light 主题近似色替换，请按目标平台观感复核）。");
            return true;
        }

        // 3) PopupAnimation="{DynamicResource {x:Static SystemParameters.XxxPopupAnimationKey}}" → 删除
        if (name == "PopupAnimation" &&
            Regex.IsMatch(value, @"\{x:Static\s+SystemParameters\.\w*PopupAnimationKey\}"))
        {
            attr.Remove();
            Note(el, NoteSeverity.Info, "XAML-POPUPANIMATION",
                "PopupAnimation={x:Static SystemParameters.XxxPopupAnimationKey} 已移除（Avalonia Popup 无系统动画模式设置）。");
            return true;
        }

        return false;
    }

    /// <summary>Visibility 特性值 → IsVisible 值（含 BooleanToVisibilityConverter 绑定解包）。</summary>
    private string? ConvertVisibilityValue(string value, out NoteSeverity severity, out string message)
    {
        severity = NoteSeverity.Info;
        message = "";

        // {Binding X, Converter={StaticResource BooleanToVisibility}} → {Binding X}
        var b2v = BindingConverterRegex().Match(value);
        if (b2v.Success && value.Contains("{Binding", StringComparison.Ordinal) &&
            _booleanToVisibilityKeys.Contains(b2v.Groups["key"].Value.Trim()))
        {
            var cleaned = BindingConverterRegex().Replace(value, "").Replace(", }", " }");
            severity = NoteSeverity.Info;
            message = $"Visibility=\"{value}\" → IsVisible=\"{cleaned}\"（bool 直接绑定，转换器移除）。";
            return cleaned;
        }

        var v = KnownMaps.VisibilityToIsVisible(value);
        if (v == null) return null;
        if (value.Trim() == "Hidden")
        {
            severity = NoteSeverity.Warning;
            message = $"Visibility=Hidden → IsVisible=False（Avalonia 无布局占位的 Hidden，行为差异需复核）。";
        }
        else
        {
            message = $"Visibility={value.Trim()} → IsVisible={v}。";
        }
        return v;
    }

    // ————————————————————————————————— Setter 归一化 —————————————————————————————————

    private void NormalizeSetter(XElement setter)
    {
        var propAttr = setter.Attribute("Property");
        if (propAttr == null) return;
        var original = propAttr.Value.Trim();
        var normalized = KnownMaps.NormalizePropertyPath(original);

        if (KnownMaps.DropSetterProperties.Contains(original) ||
            KnownMaps.DropSetterProperties.Contains(normalized))
        {
            setter.Remove();
            Note(setter, NoteSeverity.Info, "XAML-SETTER-DROP", $"Setter {original} 在 Avalonia 无对应机制，已移除。");
            return;
        }

        if (normalized != original)
        {
            propAttr.Value = normalized;
            Note(setter, NoteSeverity.Info, "XAML-SETTER-PATH", $"Setter Property {original} → {normalized}。");
        }

        // Visibility → IsVisible（值同步转换）
        if (KnownMaps.SetterPropertyRenames.TryGetValue(normalized, out var renamed))
        {
            var valueAttr = setter.Attribute("Value");
            var converted = valueAttr != null ? KnownMaps.VisibilityToIsVisible(valueAttr.Value) : null;
            propAttr.Value = renamed;
            if (converted != null && valueAttr != null)
            {
                var originalValue = valueAttr.Value;
                valueAttr.Value = converted;
                Note(setter, NoteSeverity.Info, "XAML-SETTER-VISIBILITY",
                    $"Setter {original}={originalValue} → {renamed}={converted}。");
            }
            else
            {
                Note(setter, NoteSeverity.Warning, "XAML-SETTER-VISIBILITY",
                    $"Setter Property={original} → {renamed}，但 Value 非三态 Visibility 常量，请人工复核。");
            }
        }
    }

    // ————————————————————————————————— Style → ControlTheme —————————————————————————————————

    private void ConvertStyle(XElement style)
    {
        var ns = style.Name.NamespaceName;
        var targetTypeRaw = style.Attribute("TargetType")?.Value?.Trim();
        if (targetTypeRaw == null)
        {
            // 反编译主题的 FocusVisual/OptionMarkFocusVisual 等：仅含模板、无 TargetType。
            // WPF 中仅经 FocusVisualStyle 引用；Avalonia 无 FocusVisualStyle 机制（引用点已随属性删除），
            // 该样式成死代码且 Control 无 Template 属性无法转 ControlTheme → 注释移除。
            var focusKey = style.Attribute(XNs.GetName("Key"))?.Value;
            if (focusKey != null && focusKey.Contains("FocusVisual", StringComparison.Ordinal))
            {
                ReplaceWithComment(style, "XAML-FOCUSVISUAL-STYLE",
                    $"焦点视觉样式 {focusKey} 已移除（Avalonia 无 FocusVisualStyle 机制，焦点视觉由主题内部处理；所有引用点已随 FocusVisualStyle 属性删除）。");
            }
            else
            {
                Note(style, NoteSeverity.Manual, "XAML-STYLE-NO-TARGETTYPE",
                    "无 TargetType 的 Style 无法转为 ControlTheme，需人工补写 TargetType。");
            }
            return;
        }

        var targetType = StripXType(targetTypeRaw);

        // —— TargetType 属 GridView 列视图体系（无 Avalonia 等价物）：整个主题注释移除 ——
        if (KnownMaps.GridViewFamilyElements.Contains(targetType))
        {
            ReplaceWithComment(style, "XAML-GRIDVIEW-THEME",
                $"TargetType={targetType} 的主题已随 GridView 列视图体系移除（Avalonia 无该控件）；" +
                "列头样式请以 Grid/列布局手工重建。");
            return;
        }

        // 目标类型同步元素重命名（Label → TextBlock）
        if (KnownMaps.ElementRenames.TryGetValue(targetType, out var renamedType))
        {
            var rt = renamedType == "ContentControl" ? "TextBlock" : renamedType; // 样式目标统一按 TextBlock
            if (rt != targetType)
            {
                targetType = rt;
                Note(style, NoteSeverity.Info, "XAML-TARGETTYPE-RENAME", $"TargetType 已随元素重命名 → {targetType}。");
            }
        }

        var keyAttr = style.Attribute(XNs.GetName("Key"));
        var key = keyAttr?.Value?.Trim();
        var isTypeKey = key != null && key.StartsWith("{x:Type", StringComparison.Ordinal);

        style.Name = XName.Get("ControlTheme", ns);
        style.SetAttributeValue("TargetType", targetType);
        style.Attribute("Selector")?.Remove();

        if (string.IsNullOrEmpty(key))
        {
            // keyless 隐式样式 → 补类型键（StyledElement 以 StyleKey 查资源得到默认主题）
            style.SetAttributeValue(XNs.GetName("Key"), $"{{x:Type {targetType}}}");
            Note(style, NoteSeverity.Info, "XAML-CONTROLTHEME",
                $"隐式样式 TargetType=\"{targetType}\" → ControlTheme x:Key=\"{{x:Type {targetType}}}\"（类型键资源链隐式生效）。");
        }
        else
        {
            // 类型键随 TargetType 重命名同步（{x:Type Label} → {x:Type TextBlock}，否则键与目标类型不一致）
            if (isTypeKey)
            {
                var newTypeKey = $"{{x:Type {targetType}}}";
                if (!string.Equals(key, newTypeKey, StringComparison.Ordinal))
                {
                    keyAttr!.Value = newTypeKey;
                    Note(style, NoteSeverity.Info, "XAML-TYPEKEY-RENAME",
                        $"类型键 {key} → {newTypeKey}（随 TargetType 元素重命名同步，引用处资源键已同步更新）。");
                    key = newTypeKey;
                }
            }
            Note(style, NoteSeverity.Info, "XAML-CONTROLTHEME",
                $"Style x:Key=\"{key}\" TargetType=\"{targetType}\" → ControlTheme（TargetType 定位，键与引用处 Theme= 保持不变）。");
        }

        // setter-only 命名主题：WPF 语义为主题样式叠加；自动补 BasedOn 引用同类型键主题
        var basedOn = style.Attribute("BasedOn");
        var hasTemplate = HasTemplateSetter(style);
        if (basedOn == null && !isTypeKey && !hasTemplate)
        {
            var typeKey = $"{{x:Type {targetType}}}";
            if (_typeThemeKeys.Contains(typeKey) || KnownMaps.DefaultThemeTypes.Contains(targetType))
            {
                style.SetAttributeValue("BasedOn", $"{{StaticResource {typeKey}}}");
                Note(style, NoteSeverity.Info, "XAML-BASEDON-AUTO",
                    $"setter-only 主题自动补 BasedOn={{StaticResource {{x:Type {targetType}}}}}（保留基础主题外观，对应 WPF 主题样式叠加语义）。");
            }
            else
            {
                Note(style, NoteSeverity.Warning, "XAML-BASEDON-MISSING",
                    "setter-only 主题未找到可基于的基础主题；如控件需模板，请补 BasedOn 或确认模板来源。");
            }
        }

        // Style.Triggers → ^:pseudo 嵌套样式
        ConvertStyleTriggers(style);

        // ControlTemplate.Triggers → ^:pseudo /template/ Type#Part 嵌套样式
        var templateEl = FindControlTemplate(style);
        if (templateEl != null)
            ConvertTemplateTriggers(style, templateEl);
    }

    private static bool HasTemplateSetter(XElement style) =>
        style.Elements().Any(e => e.Name.LocalName == "Setter" &&
                                  string.Equals(e.Attribute("Property")?.Value?.Trim(), "Template", StringComparison.Ordinal));

    private static XElement? FindControlTemplate(XElement style)
    {
        foreach (var setter in style.Elements().Where(e => e.Name.LocalName == "Setter"))
        {
            if (!string.Equals(setter.Attribute("Property")?.Value?.Trim(), "Template", StringComparison.Ordinal))
                continue;
            var direct = setter.Elements().FirstOrDefault(e => e.Name.LocalName == "ControlTemplate");
            if (direct != null) return direct;
            var valueEl = setter.Element(XName.Get("Setter.Value", setter.Name.NamespaceName));
            var inner = valueEl?.Elements().FirstOrDefault(e => e.Name.LocalName == "ControlTemplate");
            if (inner != null) return inner;
        }
        return null;
    }

    private static string StripXType(string value)
    {
        var m = XTypeRegex().Match(value.Trim());
        return m.Success ? m.Groups["type"].Value.Trim() : value.Trim();
    }

    // ————————————————————————————————— Style.Triggers —————————————————————————————————

    private void ConvertStyleTriggers(XElement controlTheme)
    {
        var ns = controlTheme.Name.NamespaceName;
        var triggersEl = controlTheme.Element(XName.Get("Style.Triggers", ns));
        if (triggersEl == null) return;

        foreach (var trigger in triggersEl.Elements().ToList())
        {
            var t = trigger.Name.LocalName;
            switch (t)
            {
                case "Trigger":
                {
                    var prop = trigger.Attribute("Property")?.Value ?? "";
                    var val = trigger.Attribute("Value")?.Value;
                    var setters = trigger.Elements().Where(e => e.Name.LocalName == "Setter").ToList();

                    if (TryConditionSegments(prop, val, out var seg) && setters.Count > 0)
                    {
                        // 嵌套样式必须直接放在 ControlTheme 下（而非 Style.Triggers 容器内）
                        var nested = new XElement(XName.Get("Style", ns),
                            new XAttribute("Selector", "^" + seg));
                        foreach (var s in setters)
                            nested.Add(new XText("\n        "), CloneSetterWithoutTargetName(s));
                        nested.Add(new XText("\n    "));
                        controlTheme.Add(new XText("\n    "), nested);
                        trigger.Remove();
                        Note(controlTheme, NoteSeverity.Info, "XAML-TRIGGER-CONVERT",
                            $"Trigger({KnownMaps.NormalizePropertyPath(prop)}={val}) → 嵌套 Style Selector=\"^{seg}\"。");
                    }
                    else
                    {
                        ReplaceWithComment(trigger, "XAML-TRIGGER-UNSUPPORTED",
                            $"Trigger({prop}={val}) 无伪类/属性匹配器等价物（如 IsDefault/IsHighlighted 或空值条件），需人工改写。");
                    }
                    break;
                }
                case "MultiTrigger":
                {
                    var conditions = trigger
                        .Element(XName.Get("MultiTrigger.Conditions", ns))?
                        .Elements(XName.Get("Condition", ns)).ToList() ?? new List<XElement>();
                    var setters = trigger.Elements().Where(e => e.Name.LocalName == "Setter").ToList();
                    var segments = new List<string>();
                    var ok = conditions.Count > 0 && setters.Count > 0;
                    foreach (var c in conditions)
                    {
                        if (TryConditionSegments(c.Attribute("Property")?.Value ?? "",
                                c.Attribute("Value")?.Value, out var seg))
                            segments.Add(seg);
                        else { ok = false; break; }
                    }

                    if (ok)
                    {
                        var selector = "^" + string.Concat(segments);
                        var nested = new XElement(XName.Get("Style", ns), new XAttribute("Selector", selector));
                        foreach (var s in setters)
                            nested.Add(new XText("\n        "), CloneSetterWithoutTargetName(s));
                        nested.Add(new XText("\n    "));
                        controlTheme.Add(new XText("\n    "), nested);
                        trigger.Remove();
                        Note(controlTheme, NoteSeverity.Info, "XAML-MULTITRIGGER-CONVERT",
                            $"MultiTrigger({string.Join(" & ", conditions.Select(c => $"{c.Attribute("Property")?.Value}={c.Attribute("Value")?.Value}"))}) → 嵌套 Style Selector=\"{selector}\"（段链=AND）。");
                    }
                    else
                    {
                        ReplaceWithComment(trigger, "XAML-MULTITRIGGER",
                            "MultiTrigger 含无等价物的条件，需人工改写（伪类/属性匹配器链）。");
                    }
                    break;
                }
                case "DataTrigger":
                {
                    // 反编译形态：Binding Path=控件属性（UIElement.IsMouseOver 等被反编译为数据绑定）
                    // → 视作属性触发器（伪类/匹配器）；真 VM 属性绑定保留人工
                    var setters = trigger.Elements().Where(e => e.Name.LocalName == "Setter").ToList();
                    var path = ExtractDataTriggerPath(trigger);
                    var val = trigger.Attribute("Value")?.Value ?? "True"; // 无 Value 的反编译 hover 形态按 true
                    if (path != null && KnownMaps.ControlPropertyNames.Contains(KnownMaps.NormalizePropertyPath(path)) &&
                        setters.Count > 0 && TryConditionSegments(path, val, out var seg))
                    {
                        var nested = new XElement(XName.Get("Style", ns),
                            new XAttribute("Selector", "^" + seg));
                        foreach (var s in setters)
                            nested.Add(new XText("\n        "), CloneSetterWithoutTargetName(s));
                        nested.Add(new XText("\n    "));
                        controlTheme.Add(new XText("\n    "), nested);
                        trigger.Remove();
                        Note(controlTheme, NoteSeverity.Info, "XAML-DATATRIGGER-CONVERT",
                            $"DataTrigger(Binding Path={path}, Value={val}) 为控件属性条件 → 嵌套 Style Selector=\"^{seg}\"。");
                    }
                    else
                    {
                        ReplaceWithComment(trigger, "XAML-DATATRIGGER",
                            "Style 内 DataTrigger（DataContext 属性条件）无选择器等价物：建议 VM 计算布尔属性 + Classes，或属性匹配器 [Prop=值]。");
                    }
                    break;
                }
                case "MultiDataTrigger":
                {
                    // 全部条件均为控件属性 → 伪类/匹配器链；否则人工
                    var conditions = trigger
                        .Element(XName.Get("MultiDataTrigger.Conditions", ns))?
                        .Elements(XName.Get("Condition", ns)).ToList() ?? new List<XElement>();
                    var setters = trigger.Elements().Where(e => e.Name.LocalName == "Setter").ToList();
                    var segments = new List<string>();
                    var ok = conditions.Count > 0 && setters.Count > 0;
                    foreach (var c in conditions)
                    {
                        var path = ExtractConditionPath(c, ns);
                        var val = c.Attribute("Value")?.Value ?? "True";
                        if (path != null &&
                            KnownMaps.ControlPropertyNames.Contains(KnownMaps.NormalizePropertyPath(path)) &&
                            TryConditionSegments(path, val, out var seg))
                            segments.Add(seg);
                        else { ok = false; break; }
                    }

                    if (ok)
                    {
                        var selector = "^" + string.Concat(segments);
                        var nested = new XElement(XName.Get("Style", ns), new XAttribute("Selector", selector));
                        foreach (var s in setters)
                            nested.Add(new XText("\n        "), CloneSetterWithoutTargetName(s));
                        nested.Add(new XText("\n    "));
                        controlTheme.Add(new XText("\n    "), nested);
                        trigger.Remove();
                        Note(controlTheme, NoteSeverity.Info, "XAML-MULTIDATATRIGGER-CONVERT",
                            $"MultiDataTrigger（控件属性条件）→ 嵌套 Style Selector=\"{selector}\"。");
                    }
                    else
                    {
                        ReplaceWithComment(trigger, "XAML-MULTIDATATRIGGER",
                            "MultiDataTrigger 需人工改写：VM 计算布尔属性 + Classes 类选择器。");
                    }
                    break;
                }
                case "EventTrigger":
                {
                    ReplaceWithComment(trigger, "XAML-EVENTTRIGGER",
                        "样式内 EventTrigger/动画需改写为 Avalonia Animation（Style 内 Animate）或代码。");
                    break;
                }
            }
        }

        // 容器内仅剩注释（TODO）时：注释移到 ControlTheme 末尾，删除容器
        if (!triggersEl.Elements().Any())
        {
            foreach (var comment in triggersEl.Nodes().OfType<XComment>().ToList())
                controlTheme.Add(new XText("\n    "), comment);
            triggersEl.Remove();
        }
    }

    // ————————————————————————————————— ControlTemplate.Triggers —————————————————————————————————

    private void ConvertTemplateTriggers(XElement controlTheme, XElement controlTemplate)
    {
        var ns = controlTemplate.Name.NamespaceName;
        var triggersEl = controlTemplate.Element(XName.Get("ControlTemplate.Triggers", ns));
        if (triggersEl == null) return;

        var partMap = BuildTemplatePartMap(controlTemplate);

        foreach (var trigger in triggersEl.Elements().ToList())
        {
            var t = trigger.Name.LocalName;
            switch (t)
            {
                case "Trigger":
                    ConvertTemplateTrigger(controlTheme, trigger, partMap);
                    break;
                case "MultiTrigger":
                    ConvertTemplateMultiTrigger(controlTheme, trigger, partMap);
                    break;
                case "DataTrigger":
                {
                    // 反编译形态：Binding Path=控件属性 → 与 Trigger 同路径转换；VM 属性 → 人工
                    var path = ExtractDataTriggerPath(trigger);
                    var val = trigger.Attribute("Value")?.Value;
                    if (path != null &&
                        KnownMaps.ControlPropertyNames.Contains(KnownMaps.NormalizePropertyPath(path)))
                    {
                        var sourceName = trigger.Attribute("SourceName")?.Value;
                        ConvertTemplateTriggerCore(controlTheme, trigger, partMap, path, val, sourceName,
                            "XAML-DATATRIGGER-TEMPLATE-CONVERT", "XAML-DATATRIGGER-TEMPLATE");
                    }
                    else
                    {
                        ReplaceWithComment(trigger, "XAML-DATATRIGGER-TEMPLATE",
                            "ControlTemplate 内 DataTrigger（DataContext/VM 属性条件）需人工改写（模板部件条件外观建议 Tag/Classes + 属性匹配器）。");
                    }
                    break;
                }
                case "MultiDataTrigger":
                {
                    // 全部条件为控件属性 → 匹配器/伪类链；否则人工
                    var mdConditions = trigger
                        .Element(XName.Get("MultiDataTrigger.Conditions", ns))?
                        .Elements(XName.Get("Condition", ns)).ToList() ?? new List<XElement>();
                    var ok = mdConditions.Count > 0 && mdConditions.All(c =>
                    {
                        var p = ExtractConditionPath(c, ns);
                        return p != null && KnownMaps.ControlPropertyNames.Contains(KnownMaps.NormalizePropertyPath(p));
                    });

                    if (ok)
                    {
                        // 逐条件转换：全部段可拼 → 交给单触发器管线（段链）
                        var first = mdConditions[0];
                        var firstPath = ExtractConditionPath(first, ns)!;
                        var firstVal = first.Attribute("Value")?.Value;
                        if (mdConditions.Count == 1)
                        {
                            ConvertTemplateTriggerCore(controlTheme, trigger, partMap, firstPath, firstVal,
                                trigger.Attribute("SourceName")?.Value,
                                "XAML-MULTIDATATRIGGER-TEMPLATE-CONVERT", "XAML-DATATRIGGER-TEMPLATE");
                        }
                        else
                        {
                            ReplaceWithComment(trigger, "XAML-DATATRIGGER-TEMPLATE",
                                "多条件 MultiDataTrigger（控件属性）需人工改写为段链选择器（^:a[Prop=v] /template/ …）。");
                        }
                    }
                    else
                    {
                        ReplaceWithComment(trigger, "XAML-DATATRIGGER-TEMPLATE",
                            "ControlTemplate 内 MultiDataTrigger（VM 属性条件）需人工改写。");
                    }
                    break;
                }
                case "EventTrigger":
                    ReplaceWithComment(trigger, "XAML-EVENTTRIGGER-TEMPLATE",
                        "ControlTemplate 内 EventTrigger/动画需改写为 Avalonia Animation/Transitions。");
                    break;
            }
        }

        // 容器内仅剩注释（TODO）时：注释移到 ControlTheme 末尾，删除容器
        if (!triggersEl.Elements().Any())
        {
            foreach (var comment in triggersEl.Nodes().OfType<XComment>().ToList())
                controlTheme.Add(new XText("\n    "), comment);
            triggersEl.Remove();
        }
    }

    private void ConvertTemplateTrigger(XElement controlTheme, XElement trigger, Dictionary<string, XElement> partMap)
    {
        ConvertTemplateTriggerCore(controlTheme, trigger, partMap,
            trigger.Attribute("Property")?.Value ?? "",
            trigger.Attribute("Value")?.Value,
            trigger.Attribute("SourceName")?.Value,
            "XAML-TEMPLATE-TRIGGER-CONVERT", "XAML-TEMPLATE-TRIGGER-UNSUPPORTED");
    }

    /// <summary>模板触发器统一转换：条件段（伪类或匹配器）+ TargetName 分组 → /template/ 嵌套样式。</summary>
    private void ConvertTemplateTriggerCore(XElement controlTheme, XElement trigger,
        Dictionary<string, XElement> partMap, string prop, string? val, string? sourceName,
        string convertRule, string unsupportedRule)
    {
        var ns = controlTheme.Name.NamespaceName;
        var setters = trigger.Elements().Where(e => e.Name.LocalName == "Setter").ToList();
        var hasActions = trigger.Elements().Any(e => e.Name.LocalName is "EnterActions" or "ExitActions");

        var condSeg = TryConditionSegments(prop, val, out var seg) ? seg : null;

        // 按 TargetName 分组生成嵌套样式
        var groups = setters.GroupBy(s => s.Attribute("TargetName")?.Value).ToList();
        var converted = 0;
        var leftovers = new List<XElement>();

        foreach (var group in groups)
        {
            var targetName = group.Key;
            string selector;

            if (targetName == null)
            {
                // Setter 目标 = 控件本身
                if (condSeg == null) { leftovers.AddRange(group); continue; }
                selector = "^" + condSeg;
            }
            else if (sourceName == null)
            {
                // 条件在控件上，Setter 打模板部件：^{seg} /template/ Type#Part
                if (condSeg == null || !partMap.TryGetValue(targetName, out var part))
                { leftovers.AddRange(group); continue; }
                selector = $"^{condSeg} /template/ {PartSelector(part)}";
            }
            else if (sourceName == targetName)
            {
                // 条件与 Setter 同部件：^ /template/ Type#Part{seg}（伪类/匹配器均可后缀）
                if (condSeg == null || !partMap.TryGetValue(targetName, out var part))
                { leftovers.AddRange(group); continue; }
                selector = $"^ /template/ {PartSelector(part)}{condSeg}";
            }
            else
            {
                leftovers.AddRange(group);
                continue;
            }

            controlTheme.Add(new XText("\n    "),
                BuildNestedStyle(ns, selector, group));
            converted++;
        }

        if (converted > 0)
        {
            Note(controlTheme, NoteSeverity.Info, convertRule,
                $"ControlTemplate.Triggers: 条件({KnownMaps.NormalizePropertyPath(prop)}={val})" +
                (sourceName != null ? $" SourceName={sourceName}" : "") +
                $" → {converted} 个 \"/template/\" 嵌套样式。");
        }

        if (hasActions)
            Note(trigger, NoteSeverity.Manual, "XAML-TEMPLATE-TRIGGER-ACTIONS",
                "触发器含 EnterActions/ExitActions 动画，需改写为 Avalonia Animation/Transitions。");

        if (leftovers.Count > 0 || condSeg == null)
        {
            ReplaceWithComment(trigger, unsupportedRule,
                $"条件({prop}={val}) 的部分条件/Setter 无选择器等价物（无伪类/匹配器、SourceName≠TargetName 或部件未找到），已注释保留待人工处理。");
        }
        else
        {
            trigger.Remove();
        }
    }

    private void ConvertTemplateMultiTrigger(XElement controlTheme, XElement trigger, Dictionary<string, XElement> partMap)
    {
        var ns = controlTheme.Name.NamespaceName;
        var conditions = trigger
            .Element(XName.Get("MultiTrigger.Conditions", ns))?
            .Elements(XName.Get("Condition", ns)).ToList() ?? new List<XElement>();
        var setters = trigger.Elements().Where(e => e.Name.LocalName == "Setter").ToList();

        var segments = new List<string>();
        foreach (var c in conditions)
        {
            if (TryConditionSegments(c.Attribute("Property")?.Value ?? "",
                    c.Attribute("Value")?.Value, out var seg))
                segments.Add(seg);
            else
            {
                ReplaceWithComment(trigger, "XAML-MULTITRIGGER-TEMPLATE",
                    "ControlTemplate 内 MultiTrigger 含无等价物（伪类/匹配器）的条件，需人工改写。");
                return;
            }
        }

        if (conditions.Count == 0 || setters.Count == 0)
        {
            ReplaceWithComment(trigger, "XAML-MULTITRIGGER-TEMPLATE",
                "MultiTrigger 条件或 Setter 为空，已注释保留。");
            return;
        }

        var chained = string.Concat(segments);
        var groups = setters.GroupBy(s => s.Attribute("TargetName")?.Value).ToList();
        var converted = 0;
        foreach (var group in groups)
        {
            var targetName = group.Key;
            string selector;
            if (targetName == null)
                selector = "^" + chained;
            else if (!partMap.TryGetValue(targetName, out var part))
                continue;
            else
                selector = "^" + chained + " /template/ " + PartSelector(part);

            controlTheme.Add(new XText("\n    "), BuildNestedStyle(ns, selector, group));
            converted++;
        }

        if (converted > 0)
        {
            Note(controlTheme, NoteSeverity.Info, "XAML-TEMPLATE-MULTITRIGGER-CONVERT",
                $"ControlTemplate.MultiTrigger({string.Join(" & ", conditions.Select(c => $"{c.Attribute("Property")?.Value}={c.Attribute("Value")?.Value}"))}) → {converted} 个嵌套样式 Selector=\"{ "^" + chained }…\"。");
        }
        trigger.Remove();
    }

    /// <summary>克隆 Setter 并移除 TargetName（目标由选择器限定）。</summary>
    private static XElement CloneSetterWithoutTargetName(XElement setter)
    {
        var clone = new XElement(setter);
        clone.Attribute("TargetName")?.Remove();
        return clone;
    }

    private static XElement BuildNestedStyle(string ns, string selector, IEnumerable<XElement> setters)
    {
        var nested = new XElement(XName.Get("Style", ns), new XAttribute("Selector", selector));
        foreach (var s in setters)
            nested.Add(new XText("\n        "), CloneSetterWithoutTargetName(s));
        nested.Add(new XText("\n    "));
        return nested;
    }

    /// <summary>模板部件表：x:Name → 元素（用于生成 "Type#Name" 选择器段）。</summary>
    private static Dictionary<string, XElement> BuildTemplatePartMap(XElement controlTemplate)
    {
        var result = new Dictionary<string, XElement>(StringComparer.Ordinal);
        CollectTemplatePartElements(controlTemplate, result);
        return result;
    }

    private static void CollectTemplatePartElements(XElement el, Dictionary<string, XElement> map)
    {
        foreach (var child in el.Elements())
        {
            if (child.Name.LocalName == "ControlTemplate.Triggers") continue;
            var name = child.Attribute(XNs.GetName("Name"))?.Value;
            if (name != null && !map.ContainsKey(name))
                map[name] = child;
            CollectTemplatePartElements(child, map);
        }
    }

    /// <summary>部件选择器段："Border#Bd"；自定义命名空间类型用 "prefix|Type#Name"。</summary>
    private string PartSelector(XElement part)
    {
        var name = part.Attribute(XNs.GetName("Name"))!.Value;
        if (part.Name.NamespaceName == KnownMaps.AvaloniaNs)
            return $"{part.Name.LocalName}#{name}";

        // 找 xmlns 前缀
        foreach (var el in _root.DescendantsAndSelf())
            foreach (var decl in el.Attributes())
                if (decl.IsNamespaceDeclaration && decl.Value == part.Name.NamespaceName &&
                    decl.Name.LocalName != "xmlns")
                    return $"{decl.Name.LocalName}|{part.Name.LocalName}#{name}";
        return $"{part.Name.LocalName}#{name}";
    }

    // ————————————————————————————————— DataTemplate.Triggers —————————————————————————————————

    /// <summary>
    /// DataTemplate.Triggers 的 DataTrigger：
    /// Visibility Setter → 目标元素 IsVisible="{Binding Path}"（或 !Path）；
    /// 其余 Setter 注释保留 + 人工提示。
    /// </summary>
    private void ConvertDataTemplateTriggers(XElement dataTemplate)
    {
        var ns = dataTemplate.Name.NamespaceName;
        var triggersEl = dataTemplate.Element(XName.Get("DataTemplate.Triggers", ns));
        if (triggersEl == null) return;

        foreach (var trigger in triggersEl.Elements().ToList())
        {
            if (trigger.Name.LocalName != "DataTrigger")
            {
                ReplaceWithComment(trigger, "XAML-DATATEMPLATE-TRIGGER",
                    "DataTemplate.Triggers 内非 DataTrigger 触发器需人工改写。");
                continue;
            }

            var path = ExtractDataTriggerPath(trigger);
            var value = trigger.Attribute("Value")?.Value?.Trim() ?? "True";
            var setters = trigger.Elements().Where(e => e.Name.LocalName == "Setter").ToList();
            var converted = 0;

            foreach (var setter in setters.ToList())
            {
                var prop = setter.Attribute("Property")?.Value?.Trim();
                var normalized = prop != null ? KnownMaps.NormalizePropertyPath(prop) : "";
                var setterValue = setter.Attribute("Value")?.Value?.Trim();

                if (normalized == "IsVisible" && path != null &&
                    (setterValue == "True" || setterValue == "False"))
                {
                    var targetName = setter.Attribute("TargetName")?.Value;
                    var target = FindNamedElement(dataTemplate, targetName);
                    if (target != null)
                    {
                        // DataTrigger 命中(path==value)时该 Setter 生效
                        var valueIsTrue = string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
                        var visibleOnHit = setterValue == "True";
                        var visible = valueIsTrue == visibleOnHit;
                        target.SetAttributeValue("IsVisible",
                            visible ? $"{{Binding {path}}}" : $"{{Binding !{path}}}");
                        setter.Remove();
                        converted++;
                    }
                }
            }

            if (converted > 0)
                Note(dataTemplate, NoteSeverity.Info, "XAML-DATATEMPLATE-TRIGGER-CONVERT",
                    $"DataTemplate.Triggers: DataTrigger(Path={path}, Value={value}) 的 Visibility Setter → 目标元素 IsVisible 绑定（{converted} 处）。");

            if (trigger.Elements().Any(e => e.Name.LocalName == "Setter"))
            {
                ReplaceWithComment(trigger, "XAML-DATATEMPLATE-TRIGGER-UNSUPPORTED",
                    "DataTrigger 的非 Visibility Setter（外观属性）无模板级等价物：建议在 VM 增加计算属性 + Classes 类选择器。");
            }
            else
            {
                var bindingEl = trigger.Element(XName.Get("DataTrigger.Binding", ns));
                bindingEl?.Remove();
                trigger.Remove();
            }
        }

        // 容器内仅剩注释（TODO）时：注释移到 DataTemplate 末尾，删除容器
        if (!triggersEl.Elements().Any())
        {
            foreach (var comment in triggersEl.Nodes().OfType<XComment>().ToList())
                dataTemplate.Add(new XText("\n    "), comment);
            triggersEl.Remove();
        }
    }

    private static string? ExtractDataTriggerPath(XElement dataTrigger)
    {
        var ns = dataTrigger.Name.NamespaceName;
        var bindingAttr = dataTrigger.Attribute("Binding")?.Value;
        if (bindingAttr != null)
        {
            var m = Regex.Match(bindingAttr, @"\{Binding\s+(?:Path\s*=\s*)?(?<path>[A-Za-z_][A-Za-z0-9_.]*)\s*\}");
            return m.Success ? m.Groups["path"].Value : null;
        }
        var bindingEl = dataTrigger.Element(XName.Get("DataTrigger.Binding", ns));
        var binding = bindingEl?.Elements().FirstOrDefault(e => e.Name.LocalName == "Binding");
        if (binding == null) return null;
        var path = binding.Attribute("Path")?.Value;
        // 复杂绑定（转换器/ElementName 等）不支持机械转换
        if (binding.Attributes().Any(a => a.Name.LocalName is not ("Path" or "Mode"))) return null;
        return path;
    }

    /// <summary>MultiDataTrigger 的 Condition：Binding（属性元素或内联）取简单 Path。</summary>
    private static string? ExtractConditionPath(XElement condition, string ns)
    {
        var bindingAttr = condition.Attribute("Binding")?.Value;
        if (bindingAttr != null)
        {
            var m = Regex.Match(bindingAttr, @"\{Binding\s+(?:Path\s*=\s*)?(?<path>[A-Za-z_][A-Za-z0-9_.]*)\s*\}");
            return m.Success ? m.Groups["path"].Value : null;
        }

        var bindingEl = condition.Element(XName.Get("Condition.Binding", ns));
        var binding = bindingEl?.Elements().FirstOrDefault(e => e.Name.LocalName == "Binding");
        if (binding == null) return null;
        if (binding.Attributes().Any(a => a.Name.LocalName is not ("Path" or "Mode"))) return null;
        return binding.Attribute("Path")?.Value;
    }

    /// <summary>
    /// 触发器条件 → Avalonia 选择器段：优先伪类 ":xxx"（含无 Value 的 null 语义），
    /// 其次属性匹配器 "[Prop=Val]"（字面可解析）。均不可 → false（人工）。
    /// </summary>
    private static bool TryConditionSegments(string property, string? value, out string segments)
    {
        segments = "";

        if (KnownMaps.TryGetTriggerPseudoClass(property, value, out var pseudo))
        {
            segments = ":" + pseudo;
            return true;
        }

        // 无 Value（WPF 默认 null）→ 非 null 命中语义的伪类（如 MenuItem.Icon → :icon）
        var rawVal = value?.Trim() ?? "";
        if (rawVal.Length == 0 && KnownMaps.TryGetNonNullPseudoClass(property, out var nonNullPseudo))
        {
            segments = ":" + nonNullPseudo;
            return true;
        }

        if (KnownMaps.TryGetMatcherSegment(property, value, out var matcher))
        {
            segments = matcher;
            return true;
        }

        return false;
    }

    private static XElement? FindNamedElement(XElement root, string? name)
    {
        if (name == null) return null;
        foreach (var el in root.Descendants())
        {
            if (el.Attribute(XNs.GetName("Name"))?.Value == name) return el;
        }
        return null;
    }

    // ————————————————————————————————— 资源容器处理 —————————————————————————————————

    /// <summary>在元素遍历中调用：Resources 内 keyless DataTemplate → 宿主 DataTemplates。</summary>
    private void VisitResourcesContainer(XElement el)
    {
        var isResources = el.Name.LocalName == "Resources" ||
                          el.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal);
        if (!isResources || el.Parent == null) return;

        var keyless = el.Elements()
            .Where(e => e.Name.LocalName == "DataTemplate" && e.Attribute(XNs.GetName("Key")) == null)
            .ToList();
        if (keyless.Count == 0) return;

        var host = el.Parent;
        var hostLocal = host.Name.LocalName;
        var ns = host.Name.NamespaceName;

        var dataTemplates = host.Elements().FirstOrDefault(e =>
            e.Name.LocalName == "DataTemplates" || e.Name.LocalName == hostLocal + ".DataTemplates");
        if (dataTemplates == null)
        {
            dataTemplates = new XElement(XName.Get($"{hostLocal}.DataTemplates", ns));
            host.Add(new XText("\n    "), dataTemplates);
        }

        foreach (var dt in keyless)
        {
            dt.Remove();
            dataTemplates.Add(new XText("\n      "), dt);
        }
        Note(host, NoteSeverity.Info, "XAML-DATATEMPLATE-RELOCATE",
            $"{keyless.Count} 个 keyless DataTemplate 已从 {el.Name.LocalName} 迁移到 {hostLocal}.DataTemplates（Avalonia 隐式数据模板的生效位置）。");
    }

    // ————————————————————————————————— 资产 URI —————————————————————————————————

    private static bool ShouldRewriteAsset(string attrName, string value) =>
        value.StartsWith("pack://application", StringComparison.OrdinalIgnoreCase) ||
        (value.StartsWith('/') && AssetAttributes.Contains(attrName));

    private string RewriteAsset(string value)
    {
        var m = Regex.Match(value, @"^pack://application:,,,/(?<asm>[^;/]+);component/(?<rest>.+)$", RegexOptions.IgnoreCase);
        if (m.Success) return $"avares://{m.Groups["asm"].Value}/{m.Groups["rest"].Value}";
        m = Regex.Match(value, @"^pack://application:,,,/(?<rest>.+)$", RegexOptions.IgnoreCase);
        if (m.Success) return $"avares://{_assemblyName}/{m.Groups["rest"].Value}";
        if (value.StartsWith('/')) return $"avares://{_assemblyName}{value}";
        return value;
    }

    /// <summary>
    /// 字典 Source 归一化：pack URI → avares；相对路径 → 以程序集根为基准的 / 绝对路径；
    /// .xaml → .axaml。
    /// </summary>
    private string NormalizeDictionarySource(string source)
    {
        var src = source.Trim();
        if (src.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
            src = RewriteAsset(src);
        if (src.StartsWith("avares://", StringComparison.OrdinalIgnoreCase) ||
            src.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
            src.StartsWith("resm:", StringComparison.OrdinalIgnoreCase))
        {
            return Regex.Replace(src, @"\.xaml$", ".axaml", RegexOptions.IgnoreCase);
        }
        if (!src.StartsWith('/'))
        {
            // 相对当前文件目录解析 → 根路径
            var combined = _fileDir.Length == 0 ? src : $"{_fileDir}/{src}";
            src = "/" + NormalizeRelative(combined);
        }
        return Regex.Replace(src, @"\.xaml$", ".axaml", RegexOptions.IgnoreCase);
    }

    private static string NormalizeRelative(string path)
    {
        var stack = new List<string>();
        foreach (var seg in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg == ".") continue;
            if (seg == "..")
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(seg);
        }
        return string.Join("/", stack);
    }

    // ————————————————————————————————— App 主题注入 —————————————————————————————————

    /// <summary>保证 App.axaml 含 FluentTheme（否则控件无默认外观）。</summary>
    private void EnsureFluentTheme(XElement app)
    {
        if (app.Descendants().Any(d => d.Name.LocalName is "FluentTheme" or "SimpleTheme" or "FluentControlTheme"))
            return;

        var ns = app.Name.NamespaceName;
        var styles = GetOrCreateStylesCollection(app);
        var theme = new XElement(XName.Get("FluentTheme", ns));
        var firstChild = styles.Elements().FirstOrDefault();
        if (firstChild != null) firstChild.AddBeforeSelf(theme, new XText("\n      "));
        else styles.Add(theme);
        Note(app, NoteSeverity.Info, "XAML-FLUENTTHEME",
            "已添加 <FluentTheme />（Avalonia 控件需要主题才有默认外观；如需 WPF 经典观感可换 SimpleTheme）。");
    }

    private static XElement GetOrCreateStylesCollection(XElement host)
    {
        var hostLocal = host.Name.LocalName;
        var found = host.Elements().FirstOrDefault(e =>
            e.Name.LocalName == "Styles" || e.Name.LocalName == hostLocal + ".Styles");
        if (found != null) return found;
        var created = new XElement(
            XName.Get($"{host.Name.LocalName}.Styles", host.Name.NamespaceName), new XText("\n    "));
        host.Add(new XText("\n    "), created);
        return created;
    }

    // ————————————————————————————————— 工具 —————————————————————————————————

    /// <summary>
    /// 反编译风格 XAML 把 xmlns 声明重复散布在嵌套元素上；Avalonia XAML 编译器要求
    /// xmlns 只在根元素声明（否则 AXN0003 警告 + 运行时加载失败）。处理策略：
    /// 同前缀同 URI → 冗余去除；根未声明该前缀 → 上提到根；同前缀异 URI（罕见冲突）→ 保留 + Manual 提示。
    /// </summary>
    private void HoistNamespaceDeclarations(XElement el)
    {
        foreach (var attr in el.Attributes().ToList())
        {
            if (!attr.IsNamespaceDeclaration) continue;
            var isDefault = attr.Name.Namespace == XNamespace.None; // xmlns="..."（否则 xmlns:prefix）
            var prefix = isDefault ? "" : attr.Name.LocalName;
            var uri = attr.Value;

            var rootDecl = _root.Attributes().FirstOrDefault(a => a.IsNamespaceDeclaration &&
                (a.Name.Namespace == XNamespace.None ? "" : a.Name.LocalName) == prefix);

            if (rootDecl == null)
            {
                if (isDefault)
                {
                    // 根元素带前缀（Name.Namespace 非空）时上提安全：根名不受默认 ns 影响，
                    // 后代沿用根默认绑定；根若为无命名空间的裸元素则上提会改变根语义 → 保守保留
                    if (_root.Name.Namespace != XNamespace.None)
                    {
                        attr.Remove();
                        _root.SetAttributeValue("xmlns", uri);
                    }
                    else
                    {
                        Note(el, NoteSeverity.Manual, "XAML-NS-HOIST",
                            $"嵌套默认 xmlns 声明（{uri}）保留原位：根元素无前缀且无默认命名空间，上提会改变根语义，请人工归一到根。");
                    }
                }
                else
                {
                    attr.Remove();
                    _root.SetAttributeValue(XNamespace.Xmlns + prefix, uri);
                }
            }
            else if (rootDecl.Value == uri)
            {
                attr.Remove(); // 与根声明一致：冗余，直接去除
            }
            else
            {
                // 同前缀异 URI（反编译器在不同作用域用了同名前缀）：
                // 子树元素已按展开名（完整 URI）保存，序列化时自动套用根级绑定。
                // 根已有某前缀绑定该 URI → 幂等去除；否则重命名为根级全新前缀
                attr.Remove();
                var existing = _root.Attributes().FirstOrDefault(a => a.IsNamespaceDeclaration && a.Value == uri);
                if (existing != null)
                {
                    Note(el, NoteSeverity.Info, "XAML-NS-CONFLICT",
                        $"嵌套 xmlns（{(prefix.Length == 0 ? "默认" : prefix + ":")} → {uri}）与根声明（{rootDecl.Value}）冲突：" +
                        $"根已有 {existing.Name.LocalName}: 绑定同 URI，已去除嵌套声明，子树自动改用该前缀。");
                }
                else
                {
                    var fresh = MakeFreshPrefix(prefix);
                    _root.SetAttributeValue(XNamespace.Xmlns + fresh, uri);
                    Note(el, NoteSeverity.Warning, "XAML-NS-CONFLICT",
                        $"嵌套 xmlns（{(prefix.Length == 0 ? "默认" : prefix + ":")} → {uri}）与根声明（{rootDecl.Value}）冲突：" +
                        $"已改为根级前缀 {fresh}:（子树元素引用随序列化自动更新，双方语义保留）。");
                }
            }
        }
    }

    /// <summary>生成未与根声明冲突的全新前缀（原前缀 + 数字后缀，避免 xml/xmlns 保留字）。</summary>
    private string MakeFreshPrefix(string basePrefix)
    {
        var taken = new HashSet<string>(_root.Attributes().Where(a => a.IsNamespaceDeclaration)
            .Select(a => a.Name.Namespace == XNamespace.None ? "xmlns" : a.Name.LocalName));
        var b = basePrefix.Length == 0 ? "ns" : basePrefix;
        if (b is "xml" or "xmlns") b += "0";
        var candidate = b;
        var i = 1;
        while (taken.Contains(candidate))
            candidate = b + i++;
        return candidate;
    }

    /// <summary>
    /// AdornerDecorator 解包：WPF ControlTemplate 中的装饰层包装器，Avalonia 无装饰器体系。
    /// 子元素上提至父级原位置，布局特性（Grid.Row 等）转移给首个子元素；
    /// 上提的子元素需手动递归访问（父级遍历快照在解包前已生成，不会覆盖到它们）。
    /// </summary>
    private void UnwrapAdornerDecorator(XElement el, ref bool usesDataGrid, ref string? startupUri)
    {
        var children = el.Elements().ToList();
        var attrs = el.Attributes().Where(a => !a.IsNamespaceDeclaration).ToList();
        var first = children.FirstOrDefault();

        foreach (var a in attrs)
        {
            a.Remove();
            // 布局附加属性转移给首个子元素（Grid.Row/Grid.Column 等）；子元素已有时跳过
            if (first != null && first.Attribute(a.Name) == null)
                first.SetAttributeValue(a.Name, a.Value);
        }

        foreach (var c in children)
            el.AddBeforeSelf(c); // 依原顺序插入到包装器之前（移动语义）
        el.Remove();

        Note(first ?? el, NoteSeverity.Info, "XAML-ADORNERDECORATOR",
            "AdornerDecorator 已解包（Avalonia 无装饰器层；WPF 模板中的包装器不迁移），布局特性已转移给首个子元素。");

        // 上提的子元素补访问（父级 Elements().ToList() 快照未包含它们）
        foreach (var c in children)
            VisitElement(c, ref usesDataGrid, ref startupUri);
    }

    private void ReplaceWithComment(XElement el, string rule, string message)
    {
        var comment = new XComment("\n      TODO(wpf2avalonia): " + message + "\n      原始片段：\n      " +
                                   el.ToString().Trim().Replace("\n", "\n      ") + "\n    ");
        el.ReplaceWith(comment);
        Note(el, NoteSeverity.Manual, rule, message);
    }

    /// <summary>
    /// 类型键重映射：{x:Type Label} → {x:Type TextBlock}（元素重命名链同步）。
    /// 未发生重命名时返回原键，remapped=false。
    /// </summary>
    private static string RemapTypeKey(string typeKey, out bool remapped)
    {
        remapped = false;
        var m = XTypeRegex().Match(typeKey);
        if (!m.Success) return typeKey;
        var type = m.Groups["type"].Value.Trim();
        if (!KnownMaps.ElementRenames.TryGetValue(type, out var renamed)) return typeKey;
        var rt = renamed == "ContentControl" ? "TextBlock" : renamed;
        remapped = true;
        return $"{{x:Type {rt}}}";
    }

    private void Note(XObject node, NoteSeverity severity, string rule, string message)
    {
        var line = node is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;
        _notes.Add(new ConversionNote(_file, line, severity, rule, message));
    }
}
