using System.Linq;
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
/// 把 WPF XAML 转换为 Avalonia XAML：
/// 1) 命名空间 URI 替换（文本级，零结构破坏）；
/// 2) 基于 XDocument 的结构转换：控件/特性重命名、资产 URI、绑定表达式清理、
///    Style/Trigger → Selector 样式与 ControlTheme、无等价物元素标记 TODO。
/// </summary>
public sealed partial class XamlTransformer
{
    private readonly string _assemblyName;
    private readonly List<ConversionNote> _notes = new();
    private string _file = "";
    private XElement _root = null!;
    private bool _standaloneStylesFile;
    private readonly List<(XElement Theme, XElement Owner)> _relocations = new();

    private static readonly XNamespace XNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly HashSet<string> AssetAttributes = new(StringComparer.OrdinalIgnoreCase)
    { "Source", "Icon", "SelectedSource", "ImageSource", "PlaySource" };

    [GeneratedRegex(@"^\{(?:Static|Dynamic)Resource\s+(?<key>[^{}]+)\}$")]
    private static partial Regex StyleResourceRegex();

    [GeneratedRegex(@"(,\s*)?(UpdateSourceTrigger|ValidatesOnDataErrors|ValidatesOnExceptions|ValidatesOnNotifyDataErrors|NotifyOnValidationError|NotifyOnSourceUpdated|NotifyOnTargetUpdated|IsAsync|BindingGroupName|BindsDirectlyToSource)\s*=\s*[^,}]*(?=[,}])")]
    private static partial Regex BindingOptionRemoval();

    public XamlTransformer(string assemblyName) => _assemblyName = assemblyName;

    public XamlTransformResult Transform(string source, string relativePath)
    {
        _notes.Clear();
        _relocations.Clear();
        _file = relativePath;

        var text = source;
        foreach (var (oldUri, newUri) in KnownMaps.NamespaceUris)
            text = text.Replace(oldUri, newUri);

        var doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var root = doc.Root ?? throw new InvalidOperationException("XAML 根元素缺失");
        _root = root;

        // 预扫描：独立样式字典文件（根 ResourceDictionary 只含 Style），
        // 后续会把根改为 Styles，因此无需对其中样式报"位置不生效"的 TODO。
        _standaloneStylesFile = root.Name.LocalName == "ResourceDictionary"
            && root.Elements().Any()
            && root.Elements().All(e => e.Name.LocalName == "Style");

        bool usesDataGrid = false;
        string? startupUri = null;
        VisitElement(root, ref usesDataGrid, ref startupUri);

        // 第二阶段：把无 key 的 ControlTheme / Style 从 .Resources 迁移到宿主的 .Styles
        foreach (var (theme, owner) in _relocations)
        {
            var host = theme.Parent?.Parent;
            if (host == null) continue;
            var styles = GetOrCreateStylesCollection(host);
            theme.Remove();
            styles.Add(new XText("\n    "), theme, new XText("\n  "));
        }

        // 独立样式字典文件：根 ResourceDictionary 只含样式 → 根改为 Styles，
        // 这样宿主可用 <StyleInclude> 引入且选择器全部生效（WPF 的 ResourceDictionary 无此语义）。
        bool stylesOnly = root.Name.LocalName == "ResourceDictionary"
            && root.Elements().Any(e => e.Name.LocalName is "Style" or "ControlTheme")
            && root.Elements().All(e => e.Name.LocalName is "Style" or "ControlTheme" or "StyleInclude" or "ResourceInclude");
        if (stylesOnly)
        {
            foreach (var s in root.Elements().Where(e => e.Name.LocalName == "Style"))
                s.Attribute(XNs.GetName("Key"))?.Remove(); // 类选择器已自包含
            root.Name = XName.Get("Styles", root.Name.NamespaceName);
            Note(root, NoteSeverity.Info, "XAML-STYLES-FILE",
                "仅含样式的 ResourceDictionary 根元素 → Styles；引用处应使用 StyleInclude（工具会同步迁移）。");
        }

        var sw = new StringWriter();
        doc.Declaration = null; // Avalonia axaml 不需要 XML 声明（utf-16 声明反而会误导解析器）
        doc.Save(sw, SaveOptions.None);
        var output = sw.ToString();
        // 兜底：剥离可能残留的 XML 声明
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

    private void VisitElement(XElement el, ref bool usesDataGrid, ref string? startupUri)
    {
        var ns = el.Name.NamespaceName;
        var local = el.Name.LocalName;

        if (ns == KnownMaps.AvaloniaNs || ns == KnownMaps.NamespaceUris.Values.First())
        {
            // —— 元素级处理 ——
            if (KnownMaps.UnsupportedElements.Contains(local))
            {
                Note(el, NoteSeverity.Manual, "XAML-UNSUPPORTED-ELEMENT",
                    $"元素 <{local}> 在 Avalonia 核心中无直接等价物，需人工替换（社区库或自定义控件）。");
            }

            if (KnownMaps.ElementRenames.TryGetValue(local, out var renamed))
            {
                el.Name = XName.Get(renamed, el.Name.NamespaceName);
                Note(el, NoteSeverity.Info, "XAML-ELEMENT-RENAME", $"元素 {local} → {renamed}。");
                if (local == "Label" && renamed == "TextBlock")
                {
                    var content = el.Attribute("Content");
                    if (content != null)
                    {
                        content.Remove();
                        el.SetAttributeValue("Text", content.Value);
                        Note(el, NoteSeverity.Info, "XAML-ELEMENT-RENAME", "Label.Content → TextBlock.Text。");
                    }
                }
                local = renamed;
            }

            if (local.StartsWith("DataGrid", StringComparison.Ordinal))
            {
                // Avalonia 11+ 中 Avalonia.Controls.DataGrid 包把 DataGrid 类型
                // XmlnsDefinition 到默认命名空间（https://github.com/avaloniaui），
                // 元素无需改命名空间；仅需 csproj 引包 + App 引入 DataGrid 主题。
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
                EnsureFluentTheme(el);
            }

            if (local == "Triggers" && el.Parent != null &&
                el.Parent.Name.LocalName is not ("Style" or "ControlTheme"))
            {
                Note(el, NoteSeverity.Manual, "XAML-ELEMENT-TRIGGERS",
                    "FrameworkElement.Triggers 不被 Avalonia 支持，需改写为 Style/伪类或代码。");
            }

            if (local == "Storyboard" || local == "EventTrigger" || local == "BeginStoryboard")
            {
                Note(el, NoteSeverity.Manual, "XAML-STORYBOARD",
                    "Storyboard/EventTrigger 动画体系不同，需改写为 Avalonia Animation/Transitions（CSS 式关键帧）。");
            }

            if (local == "MultiBinding")
            {
                Note(el, NoteSeverity.Manual, "XAML-MULTIBINDING",
                    "Avalonia 核心无 MultiBinding，需用多绑定转换器合并或改写为计算属性。");
            }

            // 合并字典：ResourceDictionary Source → ResourceInclude（Avalonia 语法）
            if (local == "ResourceDictionary" && el.Attribute("Source") != null)
            {
                var src = el.Attribute("Source")!;
                el.Name = XName.Get("ResourceInclude", el.Name.NamespaceName);
                if (!src.Value.StartsWith('/') && !src.Value.Contains("avares", StringComparison.Ordinal) &&
                    !src.Value.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
                {
                    src.Value = "/" + src.Value.TrimStart('/');
                }
                Note(el, NoteSeverity.Info, "XAML-MERGED-DICT",
                    "带 Source 的 ResourceDictionary → ResourceInclude（Avalonia 合并字典语法）。");
            }

            // —— 递归子元素（先处理子树，再转换自身样式语义） ——
            foreach (var child in el.Elements().ToList())
                VisitElement(child, ref usesDataGrid, ref startupUri);

            // —— 特性处理 ——
            VisitAttributes(el, ref startupUri);

            // —— Style → Selector 样式 / ControlTheme ——
            if (local == "Style" || local == "ControlTheme")
                ConvertStyle(el);

            return;
        }

        foreach (var child in el.Elements().ToList())
            VisitElement(child, ref usesDataGrid, ref startupUri);

        // 本地/其他命名空间元素上的事件与绑定同样需要清理
        VisitAttributes(el, ref startupUri);
    }

    private void VisitAttributes(XElement el, ref string? startupUri)
    {
        foreach (var attr in el.Attributes().ToList())
        {
            if (attr.IsNamespaceDeclaration) continue;
            var name = attr.Name.LocalName;

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

            if (name == "WindowStyle" && el.Name.LocalName is "Window" or "WindowBase")
            {
                var v = attr.Value.Trim();
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

            if (name == "AllowsTransparency" && attr.Value.Trim() == "True")
            {
                attr.Remove();
                Note(el, NoteSeverity.Manual, "XAML-TRANSPARENCY",
                    "AllowsTransparency 已移除；需设置 TransparencyLevelHint 并确认平台支持。");
                continue;
            }

            // 元素级样式引用：Style={StaticResource key} → Classes="key"（与定义侧 Selector="Type.key" 配对）
            if (name == "Style")
            {
                var styleValue = attr.Value;
                var m = StyleResourceRegex().Match(styleValue);
                if (m.Success)
                {
                    var cls = SanitizeClassName(m.Groups["key"].Value.Trim());
                    attr.Remove();
                    var classes = el.Attribute("Classes")?.Value;
                    el.SetAttributeValue("Classes", string.IsNullOrWhiteSpace(classes) ? cls : $"{classes} {cls}");
                    Note(el, NoteSeverity.Info, "XAML-STYLE-REFERENCE",
                        $"Style=\"{{StaticResource {m.Groups["key"].Value}}}\" → Classes=\"{cls}\"（对应定义侧已改为 Selector=\"….{cls}\"）。");
                    continue;
                }
                Note(el, NoteSeverity.Warning, "XAML-STYLE-REFERENCE",
                    "Avalonia 控件没有可赋值的 Style 属性；请改用 Classes + 类选择器样式（或把控式样式放入控件 .Styles）。");
                continue;
            }

            // 事件重命名（无命名空间属性：XML 规范中无前缀属性不继承默认 ns）
            if (KnownMaps.XamlEventRenames.TryGetValue(name, out var ev))
            {
                var right = KnownMaps.RightButtonEvents.Contains(name);
                attr.Remove();
                if (el.Attribute(ev) != null)
                {
                    // 两个鼠标事件（如左键+右键）映射到同一指针事件，后者覆盖前者
                    el.SetAttributeValue(ev, attr.Value);
                    Note(el, NoteSeverity.Warning, "XAML-EVENT-MERGE",
                        $"{name} 与其他鼠标事件均映射到 {ev}，仅保留 {attr.Value}；右键判断需在处理器内用 e.GetCurrentPoint(...).Properties.IsRightButton 区分。");
                }
                else
                {
                    el.SetAttributeValue(ev, attr.Value);
                    Note(el, right ? NoteSeverity.Warning : NoteSeverity.Info, "XAML-EVENT-RENAME",
                        $"事件 {name} → {ev}。" + (right ? "右键判断需改为 e.GetCurrentPoint(...).Properties.IsRightButton。" : string.Empty));
                }
                continue;
            }

            if (el.Name.LocalName == "DataTemplate" && name == "DataType")
            {
                attr.Remove();
                Note(el, NoteSeverity.Warning, "XAML-DATATEMPLATE",
                    "DataTemplate.DataType 已移除；Avalonia 用 x:DataType 做编译绑定匹配，隐式类型模板需人工调整。");
                continue;
            }

            // 值重写：资产 URI / 字体 / 资源字典 Source
            var value = attr.Value;
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

            if (name == "Source" && value.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                attr.Value = Regex.Replace(value, @"\.xaml$", ".axaml", RegexOptions.IgnoreCase);
                Note(el, NoteSeverity.Info, "XAML-RESOURCE-SOURCE", "合并字典 Source 扩展名 → .axaml。");
                continue;
            }

            if (name == "FontFamily" && value.StartsWith('/'))
            {
                attr.Value = RewriteAsset(value);
                Note(el, NoteSeverity.Info, "XAML-FONT-URI", "自定义字体路径 → avares://。");
                continue;
            }

            // 绑定表达式清理
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

        // 常规重命名表（最后应用，避免与上面的特判冲突）
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

    /// <summary>Style TargetType → Selector；Trigger → 伪类样式；模板样式 → ControlTheme。</summary>
    private void ConvertStyle(XElement style)
    {
        var ns = style.Name.NamespaceName;
        var targetType = style.Attribute("TargetType")?.Value?.Trim();

        if (targetType == null)
        {
            if (style.Attribute("Selector") == null)
                Note(style, NoteSeverity.Manual, "XAML-STYLE-NO-TARGETTYPE",
                    "无 TargetType 的 Style 无法推断 Selector，需人工补写 Selector。");
            return;
        }

        // WPF 中 keyed 样式仅通过 Style={StaticResource key} 应用；Avalonia 等价物是
        // 类选择器：定义侧 Selector="Type.key"，使用侧 Classes="key"。
        var keyAttr = style.Attribute(XNs.GetName("Key"));
        var key = keyAttr?.Value?.Trim();
        var baseType = targetType.Replace(':', '|');
        var selector = string.IsNullOrEmpty(key) ? baseType : $"{baseType}.{SanitizeClassName(key!)}";
        style.SetAttributeValue("Selector", selector);
        style.Attribute("TargetType")?.Remove();
        Note(style, NoteSeverity.Info, "XAML-STYLE-SELECTOR",
            key is { Length: > 0 }
                ? $"keyed 样式 TargetType=\"{targetType}\" x:Key=\"{key}\" → Selector=\"{selector}\"（引用处改为 Classes）。"
                : $"Style TargetType=\"{targetType}\" → Selector=\"{selector}\"。");

        var basedOn = style.Attribute("BasedOn");
        if (basedOn != null && basedOn.Value.Contains("x:Type"))
            Note(style, NoteSeverity.Warning, "XAML-BASEDON",
                "BasedOn 基于 x:Type 的默认样式查找不被支持；Avalonia 用 ControlTheme + BasedOn=\"^...\" 或 StaticResource，请复核。");

        // Trigger → 嵌套伪类样式（Selector="^:pseudo" 引用父选择器，keyed 样式同样生效）
        var triggersEl = style.Element(XName.Get("Style.Triggers", ns)) ?? style.Element(XName.Get("ControlTheme.Triggers", ns));
        if (triggersEl != null)
        {
            foreach (var trigger in triggersEl.Elements().ToList())
            {
                var t = trigger.Name.LocalName;
                if (t == "Trigger")
                {
                    var prop = trigger.Attribute("Property")?.Value?.Trim();
                    var val = trigger.Attribute("Value")?.Value?.Trim();
                    if (prop != null && KnownMaps.TriggerPseudoClasses.TryGetValue(prop, out var pseudo) &&
                        KnownMaps.TriggerValueMatches(prop, val ?? "True"))
                    {
                        var nested = new XElement(XName.Get("Style", ns),
                            new XAttribute("Selector", $"^:{pseudo}"));
                        foreach (var n in trigger.Nodes())
                            if (n is XElement e && e.Name.LocalName is "Setter" or "Setter.Value")
                                nested.Add(new XText("\n        "), new XElement(e));
                        nested.Add(new XText("\n      "));
                        CleanBindingsDeep(nested);
                        // 插到最后一个 Setter/嵌套样式之后
                        var last = style.Elements().LastOrDefault(e => e.Name.LocalName is "Setter" or "Style") ?? triggersEl;
                        last.AddAfterSelf(new XText("\n      "), nested);
                        trigger.Remove();
                        Note(style, NoteSeverity.Info, "XAML-TRIGGER-CONVERT",
                            $"Trigger({prop}={val}) → 嵌套 Style Selector=\"^:{pseudo}\"。");
                    }
                    else
                    {
                        Note(trigger, NoteSeverity.Manual, "XAML-TRIGGER-UNSUPPORTED",
                            $"Trigger({prop}={val}) 无法映射为伪类选择器，需人工改写（可用 Classes + 选择器）。");
                    }
                }
                else if (t == "DataTrigger")
                {
                    Note(trigger, NoteSeverity.Manual, "XAML-DATATRIGGER",
                        "DataTrigger 需人工改写：数据条件样式建议在 VM 计算布尔属性 + Classes，或用 Classes 选择器。");
                }
                else if (t == "MultiTrigger" || t == "MultiDataTrigger")
                {
                    Note(trigger, NoteSeverity.Manual, "XAML-MULTITRIGGER",
                        "MultiTrigger 无等价物，需拆分为多个伪类样式或人工改写。");
                }
                else if (t == "EventTrigger")
                {
                    Note(trigger, NoteSeverity.Manual, "XAML-EVENTTRIGGER",
                        "样式内 EventTrigger/动画需改写为 Avalonia Animation（Style 内 Animate）或代码。");
                }
                else if (t == "Setter")
                {
                    continue; // 触发器容器中的 Setter 不常见，忽略
                }
            }

            if (!triggersEl.Elements().Any() && !triggersEl.Attributes().Any())
                triggersEl.Remove();
        }

        // 模板样式 → ControlTheme，并在需要时迁移到 .Styles
        var hasTemplate = style.Elements().Any(e =>
            e.Name.LocalName == "Setter" && string.Equals(e.Attribute("Property")?.Value?.Trim(), "Template", StringComparison.Ordinal));

        if (hasTemplate)
        {
            // ControlTheme 在 Avalonia 中用 TargetType 而非 Selector 定位控件类型
            style.Name = XName.Get("ControlTheme", ns);
            style.SetAttributeValue("TargetType", targetType);
            style.Attribute("Selector")?.Remove();
            // ControlTheme 不参与类选择器，也不需要 x:Key 之外的类映射
            Note(style, NoteSeverity.Info, "XAML-CONTROLTHEME",
                $"含 Template 的 Style → ControlTheme TargetType=\"{targetType}\"（Avalonia 11+ 控件主题机制）。");

            // ControlTheme 必须位于 Styles 集合中；迁移到宿主 .Styles
            var parentName = style.Parent?.Name.LocalName;
            if (IsResourcesContainer(parentName))
            {
                var owner = style.Parent!;
                if (owner.Parent != null)
                {
                    _relocations.Add((style, owner));
                    Note(style, NoteSeverity.Info, "XAML-THEME-RELOCATE",
                        "无 key 的默认控件样式已从 Resources 迁移到宿主 .Styles 集合。");
                }
                else if (!_standaloneStylesFile)
                {
                    Note(style, NoteSeverity.Manual, "XAML-THEME-LOCATION",
                        "独立资源字典文件中的无 key 样式在 Avalonia 中不会自动生效；请移入 App.axaml 的 Application.Styles（可用 StyleInclude 引入）。");
                }
            }
        }
        else
        {
            // 无模板的样式（含 keyed 类选择器样式）也需在 Styles 集合中才能生效
            var parentName = style.Parent?.Name.LocalName;
            if (IsResourcesContainer(parentName))
            {
                if (style.Parent!.Parent != null)
                {
                    keyAttr?.Remove(); // 类选择器已自包含，key 不再需要
                    _relocations.Add((style, style.Parent!));
                    Note(style, NoteSeverity.Info, "XAML-STYLE-RELOCATE",
                        "样式已迁移到宿主 .Styles 集合（Avalonia 样式必须放在 Styles 中才会生效；keyed 样式通过 Classes 匹配）。");
                }
                else if (!_standaloneStylesFile)
                {
                    Note(style, NoteSeverity.Manual, "XAML-STYLE-LOCATION",
                        "独立资源字典文件中的样式在 Avalonia 中不会自动生效；请移入 Application.Styles 或用 StyleInclude 引入。");
                }
            }
        }
    }

    /// <summary>资源容器判定：兼容 "Resources"、"ResourceDictionary" 与 "Window.Resources" 点式属性元素。</summary>
    private static bool IsResourcesContainer(string? parentName) =>
        parentName == "ResourceDictionary" ||
        parentName == "Resources" ||
        (parentName?.EndsWith(".Resources", StringComparison.Ordinal) ?? false);

    /// <summary>CSS 类名合法字符（Avalonia 选择器类名限制）：非 [A-Za-z0-9_-] 一律替换为 '-'。</summary>
    private static string SanitizeClassName(string name) =>
        Regex.Replace(name, @"[^A-Za-z0-9_\-]", "-");

    /// <summary>对新增节点的后代特性统一做绑定表达式清理与资产 URI 重写。</summary>
    private void CleanBindingsDeep(XElement root)
    {
        foreach (var el in root.DescendantsAndSelf())
        {
            foreach (var attr in el.Attributes())
            {
                if (attr.IsNamespaceDeclaration) continue;
                var value = attr.Value;
                if (value.Contains("{Binding", StringComparison.Ordinal))
                {
                    var cleaned = BindingOptionRemoval().Replace(value, "");
                    if (cleaned != value) attr.Value = cleaned;
                }
                if (ShouldRewriteAsset(attr.Name.LocalName, value))
                    attr.Value = RewriteAsset(value);
            }
        }
    }

    /// <summary>查找宿主的 .Styles 集合（兼容 "Styles" 与 "Window.Styles" 两种属性元素写法）。</summary>
    private static XElement? FindStylesCollection(XElement host)
    {
        var hostLocal = host.Name.LocalName;
        return host.Elements().FirstOrDefault(e =>
            e.Name.LocalName == "Styles" || e.Name.LocalName == hostLocal + ".Styles");
    }

    private static XElement GetOrCreateStylesCollection(XElement host)
    {
        var found = FindStylesCollection(host);
        if (found != null) return found;
        var created = new XElement(
            XName.Get($"{host.Name.LocalName}.Styles", host.Name.NamespaceName), new XText("\n    "));
        host.Add(new XText("\n    "), created);
        return created;
    }

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

    private void Note(XObject node, NoteSeverity severity, string rule, string message)
    {
        var line = node is IXmlLineInfo li && li.HasLineInfo() ? li.LineNumber : 0;
        _notes.Add(new ConversionNote(_file, line, severity, rule, message));
    }
}
