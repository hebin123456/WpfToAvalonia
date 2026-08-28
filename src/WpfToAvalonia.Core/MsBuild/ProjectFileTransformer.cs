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
    private readonly ConversionOptions _options;
    private readonly List<ConversionNote> _notes = new();
    private string _file = "project.csproj";

    public ProjectFileTransformer(ConversionOptions options) => _options = options;

    public ProjectFileResult Transform(string csprojXml, string fileName, bool usesDataGrid)
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
