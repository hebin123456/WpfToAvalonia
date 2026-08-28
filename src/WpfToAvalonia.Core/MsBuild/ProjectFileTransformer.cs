using System.Xml.Linq;
using WpfToAvalonia.Core.Model;

namespace WpfToAvalonia.Core.MsBuild;

public sealed class ProjectFileResult
{
    public required string Xml { get; init; }
    public IReadOnlyList<ConversionNote> Notes { get; init; } = Array.Empty<ConversionNote>();
    public string? RootNamespace { get; init; }
    public string? AssemblyNameValue { get; init; }
    public bool IsExecutable { get; init; }
}

/// <summary>SDK 风格 csproj：去 WPF 化 → net10.0 → 挂 Avalonia 包 → 修正条目类型。</summary>
public sealed class ProjectFileTransformer
{
    /// <summary>
    /// 强制 Windows Desktop 目标（NETSDK1136）或纯 WPF 的包：
    /// 注释隔离，保证转换后工程能以纯 net10.0 编译 XAML，后续人工替换。
    /// Microsoft-WindowsAPICodePack-Shell 经真实构建验证触发 NETSDK1136。
    /// </summary>
    private static readonly (string Id, string Reason, string Replacement)[] QuarantinedPackages =
    {
        ("Microsoft-WindowsAPICodePack-Shell", "依赖 WPF（TaskDialog 等基于 WPF 实现）", "H.NotifyIcon / 平台对话框 API"),
        ("Extended.Wpf.Toolkit", "WPF 控件库", "Avalonia 社区控件或手工实现"),
        ("MahApps.Metro", "WPF 控件库", "FluentTheme + 手工样式"),
        ("MaterialDesignThemes", "WPF 控件库", "FluentTheme + 手工样式"),
        ("MaterialDesignColors", "WPF 调色板", "Avalonia 主题资源"),
        ("Hardcodet.NotifyIcon.Wpf", "WPF 托盘图标", "Avalonia 12 内置 TrayIcon"),
        ("System.Windows.Interactivity", "WPF 交互库", "Avalonia 事件/类行为"),
        ("WPFToolkit", "WPF 控件库", "Avalonia 核心控件"),
        ("FluentWPF", "WPF 控件库", "FluentTheme"),
        ("ControlzEx", "WPF 控件基础设施", "Avalonia 内置"),
        // OxyPlot.Wpf：WPF 绘图控件（ForkPlus 统计图表用；其 PlotView : Control 引发
        // 314 处 CS0012 级联：要求引用 PresentationFramework）。Avalonia 侧无官方移植，
        // 社区可选 ScottPlot.Avalonia / LiveChartsCore.SkiaSharpView.Avalonia。
        ("OxyPlot.Wpf", "WPF 绘图控件库（PlotView 继承 WPF Control，引发 PresentationFramework 级联错误）", "ScottPlot.Avalonia / LiveChartsCore.SkiaSharpView.Avalonia / 手工自绘"),
    };

    /// <summary>
    /// 1:1 官方/社区移植包替换（非隔离）：包名替换 + 版本对齐 Avalonia 12，
    /// 命名空间重排由 C# 重写器同步处理（ICSharpCode.AvalonEdit.* → AvaloniaEdit.*，
    /// 见 KnownMaps.CSharpNamespaces）。
    /// Avalonia.AvaloniaEdit 12.0.0 经 NuGet 元数据验证（官方 avaloniaui 组织，Avalonia 12 对齐）。
    /// </summary>
    private static readonly (string FromId, string ToId, string Version, string Note)[] PackageReplacements =
    {
        ("AvalonEdit", "Avalonia.AvaloniaEdit", "12.0.0",
            "WPF ICSharpCode.AvalonEdit → 官方 avaloniaui 组织的 Avalonia.AvaloniaEdit（Avalonia 12 对齐版本）；" +
            "命名空间 ICSharpCode.AvalonEdit.* → AvaloniaEdit.* 已由 C# 重写器同步改写；" +
            "API 高度兼容（TextEditor/TextArea/Margin 体系同名），DrawingContext API 差异见转换报告提示。"),
    };

    /// <summary>
    /// 代码用法驱动的 BCL 包补偿（ProjectConverter 探测传入）：去 WindowsDesktop 框架
    /// （UseWPF 移除）后，桌面共享框架传递提供的程序集断供——
    /// System.Drawing.Common（Icon/Bitmap/Graphics，ForkPlus IconTools.cs CS1069 实测）、
    /// System.CodeDom（TempFileCollection，TempFileManager.cs CS1069 实测）。
    /// 10.0.0 版本均经 NuGet 真实 restore 验证（net10.0）。
    /// </summary>
    private static readonly Dictionary<string, string> FrameworkPackageVersions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["System.Drawing.Common"] = "10.0.0",
            ["System.CodeDom"] = "10.0.0",
        };

    private readonly ConversionOptions _options;
    private readonly List<ConversionNote> _notes = new();
    private string _file = "project.csproj";

    public ProjectFileTransformer(ConversionOptions options) => _options = options;

    public ProjectFileResult Transform(string csprojXml, string fileName, bool usesDataGrid,
        IReadOnlySet<string>? frameworkPackages = null)
    {
        _notes.Clear();
        _file = fileName;

        var doc = XDocument.Parse(csprojXml);
        var root = doc.Root ?? throw new InvalidOperationException("csproj 根缺失");

        if (root.Attribute("xmlns")?.Value is { Length: > 0 })
        {
            _notes.Add(new ConversionNote(_file, 0, NoteSeverity.Manual, "PROJ-OLDSTYLE",
                "非 SDK 风格旧工程：请先升级为 SDK 风格（dotnet migrate / 手工），本工具仅处理 SDK 风格。"));
            return new ProjectFileResult { Xml = csprojXml, Notes = _notes.ToList() };
        }

        var sdk = root.Attribute("Sdk")?.Value ?? "";
        if (sdk.Contains("WindowsDesktop"))
        {
            root.SetAttributeValue("Sdk", "Microsoft.NET.Sdk");
            Note("PROJ-SDK", "Sdk 由 Microsoft.NET.Sdk.WindowsDesktop → Microsoft.NET.Sdk。");
        }

        // —— 属性组 ——
        string? rootNs = null, asmName = null;
        bool isExe = false;

        foreach (var prop in root.Descendants().Where(e => e.Name.LocalName == "PropertyGroup").ToList())
        {
            foreach (var child in prop.Elements().ToList())
            {
                switch (child.Name.LocalName)
                {
                    case "UseWPF":
                    case "UseWindowsForms":
                        child.Remove();
                        Note("PROJ-REMOVE", $"{child.Name.LocalName} 已移除。");
                        break;
                    case "TargetFramework":
                        child.SetValue(_options.TargetFramework);
                        Note("PROJ-TFM", $"TargetFramework → {_options.TargetFramework}。");
                        break;
                    case "TargetFrameworks":
                        child.Remove();
                        var single = new XElement(XName.Get("TargetFramework", child.Name.NamespaceName), _options.TargetFramework);
                        prop.Add(single);
                        Note("PROJ-TFM", $"多目标 TargetFrameworks → 单目标 TargetFramework={_options.TargetFramework}（如需多目标请手工恢复）。");
                        break;
                    case "RootNamespace":
                        rootNs = child.Value.Trim();
                        break;
                    case "AssemblyName":
                        asmName = child.Value.Trim();
                        break;
                    case "OutputType":
                        isExe = child.Value.Trim().Equals("WinExe", StringComparison.OrdinalIgnoreCase) ||
                                child.Value.Trim().Equals("Exe", StringComparison.OrdinalIgnoreCase);
                        break;
                }
            }
        }

        if (root.Descendants().All(e => e.Name.LocalName != "TargetFramework"))
        {
            var pg = root.Elements().First(e => e.Name.LocalName == "PropertyGroup");
            pg.AddFirst(new XElement(XName.Get("TargetFramework", pg.Name.NamespaceName), _options.TargetFramework));
            Note("PROJ-TFM", $"已添加 TargetFramework={_options.TargetFramework}。");
        }

        // —— 条目组：Page/ApplicationDefinition 移除（.axaml 由 Avalonia 自动包含）；Resource → AvaloniaResource ——
        foreach (var item in root.Descendants().Where(e =>
                     e.Name.LocalName is "Page" or "ApplicationDefinition" or "SplashScreen" or "DesignData").ToList())
        {
            var include = item.Attribute("Include")?.Value;
            item.Remove();
            Note("PROJ-ITEM", $"{item.Name.LocalName} {include} 已移除（.axaml 由 Avalonia 构建自动包含）。");
        }

        foreach (var item in root.Descendants().Where(e => e.Name.LocalName == "Resource").ToList())
        {
            item.Name = XName.Get("AvaloniaResource", item.Name.NamespaceName);
            Note("PROJ-RESOURCE", $"Resource → AvaloniaResource（{item.Attribute("Include")?.Value}）。");
        }

        // —— 1:1 包替换（AvalonEdit → Avalonia.AvaloniaEdit 等）：替换优先于隔离 ——
        foreach (var item in root.Descendants()
                     .Where(e => e.Name.LocalName == "PackageReference").ToList())
        {
            var id = item.Attribute("Include")?.Value?.Trim();
            if (id == null) continue;
            var rep = PackageReplacements.FirstOrDefault(r =>
                r.FromId.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (rep.FromId == null) continue;

            item.SetAttributeValue("Include", rep.ToId);
            item.SetAttributeValue("Version", rep.Version);
            _notes.Add(new ConversionNote(_file, 0, NoteSeverity.Info, "PROJ-PACKAGE-REPLACE",
                $"PackageReference {id} → {rep.ToId} {rep.Version}。{rep.Note}"));
        }

        // —— WPF-only 包隔离：避免 NETSDK1136 强制 -windows TFM，转注释待人工替换 ——
        foreach (var item in root.Descendants()
                     .Where(e => e.Name.LocalName == "PackageReference").ToList())
        {
            var id = item.Attribute("Include")?.Value?.Trim();
            if (id == null) continue;
            var hit = QuarantinedPackages.FirstOrDefault(q =>
                q.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith(q.Id + ".", StringComparison.OrdinalIgnoreCase));
            if (hit.Id == null) continue;

            // XML 注释禁止含 "--"，包名仅含单连字符，此处兜底防非法输出
            var commentText = $" {item}  已隔离：{hit.Reason}；替代方案：{hit.Replacement} ".Replace("--", "−−");
            item.ReplaceWith(new XComment(commentText));
            _notes.Add(new ConversionNote(_file, 0, NoteSeverity.Manual, "PROJ-WPF-PACKAGE",
                $"PackageReference {id} 已注释隔离（{hit.Reason}，会强制 -windows TFM 阻断编译）。" +
                $"替代方案：{hit.Replacement}。相关调用代码需人工迁移。"));
        }

        // WPF 反射绑定语义 → 保持反射绑定，避免转换后编译期绑定报错
        var firstPg = root.Elements().First(e => e.Name.LocalName == "PropertyGroup");
        if (root.Descendants().All(e => e.Name.LocalName != "AvaloniaUseCompiledBindingsByDefault"))
        {
            firstPg.Add(new XElement(XName.Get("AvaloniaUseCompiledBindingsByDefault", firstPg.Name.NamespaceName), "false"));
            Note("PROJ-COMPILEDBINDINGS",
                "已设置 AvaloniaUseCompiledBindingsByDefault=false 以匹配 WPF 反射绑定语义；迁移稳定后可改回 true 并补 x:DataType 以启用编译绑定。");
        }
        // 注意：不注入 <Nullable>enable</Nullable> —— 沿用源工程的可空设置，
        // 避免 WPF 旧式签名（object value / object sender）产生大量 CS8767/CS8622 噪音。

        // —— Avalonia 包 ——
        var pkgs = new List<(string Id, string Version)>
        {
            ("Avalonia", _options.AvaloniaVersion),
            ("Avalonia.Desktop", _options.AvaloniaVersion),
            ("Avalonia.Themes.Fluent", _options.AvaloniaVersion),
        };
        if (usesDataGrid) pkgs.Add(("Avalonia.Controls.DataGrid", _options.AvaloniaVersion));
        if (_options.AddInterFont) pkgs.Add(("Avalonia.Fonts.Inter", _options.AvaloniaVersion));

        // —— BCL 包补偿（去 WindowsDesktop 框架断供）：仅在既有引用缺失时注入 ——
        if (frameworkPackages is { Count: > 0 })
        {
            var existingIds = root.Descendants()
                .Where(e => e.Name.LocalName == "PackageReference")
                .Select(e => e.Attribute("Include")?.Value?.Trim())
                .Where(id => id is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var id in frameworkPackages)
            {
                if (!FrameworkPackageVersions.TryGetValue(id, out var ver)) continue;
                if (existingIds.Contains(id)) continue;
                pkgs.Add((id, ver));
                _notes.Add(new ConversionNote(_file, 0, NoteSeverity.Warning, "PROJ-FX-PACKAGE",
                    $"已添加 PackageReference {id} {ver}（代码使用相关类型；原由 WindowsDesktop 桌面框架传递提供，去 WPF 化后断供）。"
                    + (id.Equals("System.Drawing.Common", StringComparison.OrdinalIgnoreCase)
                        ? "注意：System.Drawing.Common 仅 Windows 运行时可用，跨平台部署需换 ImageSharp/SkiaSharp。"
                        : "")));
            }
        }

        var itemGroup = new XElement("ItemGroup");
        foreach (var (id, ver) in pkgs)
            itemGroup.Add(new XElement("PackageReference", new XAttribute("Include", id), new XAttribute("Version", ver)));
        root.Add(itemGroup);
        Note("PROJ-PACKAGES",
            $"已添加 Avalonia {_options.AvaloniaVersion} 包引用" +
            (usesDataGrid ? "（含 DataGrid）" : "") +
            "。DevTools 请注意：Avalonia 12 已移除 Avalonia.Diagnostics，可用 AvaloniaUI.DiagnosticsSupport。");

        var sw = new StringWriter();
        doc.Declaration = null;
        doc.Save(sw, SaveOptions.None);
        var xml = sw.ToString();
        if (xml.StartsWith("<?xml", StringComparison.Ordinal))
            xml = xml[(xml.IndexOf("?>", StringComparison.Ordinal) + 2)..].TrimStart('\r', '\n');
        return new ProjectFileResult
        {
            Xml = xml,
            Notes = _notes.ToList(),
            RootNamespace = rootNs,
            AssemblyNameValue = asmName,
            IsExecutable = isExe,
        };
    }

    private void Note(string rule, string msg) =>
        _notes.Add(new ConversionNote(_file, 0, NoteSeverity.Info, rule, msg));
}
