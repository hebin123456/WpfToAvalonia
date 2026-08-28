using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using WpfToAvalonia.Core.Bootstrap;
using WpfToAvalonia.Core.CSharp;
using WpfToAvalonia.Core.Model;
using WpfToAvalonia.Core.MsBuild;
using WpfToAvalonia.Core.Xaml;

namespace WpfToAvalonia.Core;

/// <summary>单个 WPF 工程 → Avalonia 工程的转换流水线。</summary>
public sealed class ProjectConverter
{
    private readonly ConversionOptions _options;
    private readonly ConversionReport _report;

    public ProjectConverter(ConversionOptions options, ConversionReport report)
    {
        _options = options;
        _report = report;
    }

    public bool Convert(string csprojPath)
    {
        csprojPath = Path.GetFullPath(csprojPath);
        if (!File.Exists(csprojPath)) return false;

        var dir = Path.GetDirectoryName(csprojPath)!;
        var projName = Path.GetFileNameWithoutExtension(csprojPath);

        var csprojText = File.ReadAllText(csprojPath);
        var assemblyName = Regex.Match(csprojText, "<AssemblyName>([^<]+)</AssemblyName>").Groups[1].Value;
        if (assemblyName.Length == 0) assemblyName = projName;
        var rootNs = Regex.Match(csprojText, "<RootNamespace>([^<]+)</RootNamespace>").Groups[1].Value;
        if (rootNs.Length == 0) rootNs = assemblyName;

        if (!_options.DryRun && _options.Backup)
        {
            var backupDir = Path.Combine(Directory.GetParent(dir)?.FullName ?? dir, projName + ".wpf-backup");
            if (!Directory.Exists(backupDir))
            {
                CopyTree(dir, backupDir);
                _report.Add(new ConversionNote(projName, 0, NoteSeverity.Info, "BACKUP", $"原工程已备份到 {Path.GetFileName(backupDir)}。"));
            }
        }

        // ---------- 1. XAML（两遍式：预扫描收集跨文件资源键，再统一转换） ----------
        bool usesDataGrid = false;
        string? appXamlPath = null;
        string? startupUri = null;

        var xamlFiles = Directory.EnumerateFiles(dir, "*.xaml", SearchOption.AllDirectories)
            .Where(p => !IsUnderObjBin(p))
            .OrderBy(p => p.Length)
            .ToList();

        // 预扫描：ForkPlus 的资源经字典链合并（App → Generic → Styles/*.xaml），
        // 键与主题跨文件可见，转换前收集全局键集合供引用判定（BasedOn 自动补链 / B2V 解包）
        var styleKeys = new HashSet<string>(StringComparer.Ordinal);
        var typeThemeKeys = new HashSet<string>(StringComparer.Ordinal);
        var booleanToVisibilityKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var xaml in xamlFiles)
            PreScanKeys(File.ReadAllText(xaml), styleKeys, typeThemeKeys, booleanToVisibilityKeys);

        var xamlTransformer = new XamlTransformer(assemblyName, styleKeys, typeThemeKeys, booleanToVisibilityKeys);
        foreach (var xaml in xamlFiles)
        {
            var rel = Path.GetRelativePath(dir, xaml);
            var source = File.ReadAllText(xaml);
            XamlTransformResult result;
            try
            {
                result = xamlTransformer.Transform(source, rel);
            }
            catch (Exception ex)
            {
                _report.Add(new ConversionNote(rel, 0, NoteSeverity.Manual, "XAML-PARSE-ERROR", $"XAML 解析失败：{ex.Message}，该文件保留原样，请人工处理。"));
                continue;
            }

            _report.AddRange(result.Notes);
            usesDataGrid |= result.UsesDataGrid;

            var isApp = source.Contains("<Application", StringComparison.Ordinal);
            if (isApp)
            {
                appXamlPath = xaml;
                startupUri = result.StartupUri;
            }

            if (!_options.DryRun)
            {
                var axaml = Path.ChangeExtension(xaml, ".axaml");
                File.WriteAllText(axaml, result.Xaml);
                File.Delete(xaml);
                _report.XamlFilesRenamed++;
            }
            _report.XamlFilesConverted++;
        }

        // DataGrid 主题注入：Avalonia.Controls.DataGrid 包的默认样式不会自动加载
        if (usesDataGrid && appXamlPath != null && !_options.DryRun)
        {
            var appAxaml = Path.ChangeExtension(appXamlPath, ".axaml");
            if (File.Exists(appAxaml)) EnsureDataGridTheme(appAxaml, dir);
        }

        // ---------- 2. 启动窗口类型 ----------
        var startupWindowClass = ResolveStartupWindowClass(dir, startupUri, rootNs);

        // ---------- 3. C# ----------
        var appCs = appXamlPath != null ? appXamlPath + ".cs" : null; // App.xaml.cs
        var csharp = new CSharpTransformer();
        var csFiles = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsUnderObjBin(p))
            .ToList();

        foreach (var cs in csFiles.Where(p => p != appCs))
        {
            var rel = Path.GetRelativePath(dir, cs);
            CSharpTransformResult result;
            try
            {
                result = csharp.Transform(File.ReadAllText(cs), rel);
            }
            catch (Exception ex)
            {
                _report.Add(new ConversionNote(rel, 0, NoteSeverity.Manual, "CS-PARSE-ERROR",
                    $"C# AST 转换失败：{ex.Message}，该文件保留原样，请人工处理。"));
                continue;
            }
            if (!result.WpfDetected) continue;

            _report.AddRange(result.Notes);
            var csSource = File.ReadAllText(cs);
            if (!_options.DryRun && result.Code != csSource)
            {
                File.WriteAllText(cs, result.Code);
            }
            _report.CSharpFilesConverted++;
        }

        // .xaml.cs → .axaml.cs（纯改名，保持 IDE 嵌套）
        if (!_options.DryRun)
        {
            foreach (var xaml in xamlFiles)
            {
                var oldCs = xaml + ".cs";
                var newCs = Path.ChangeExtension(xaml, ".axaml") + ".cs";
                if (oldCs == appCs) appCs = newCs;
                if (File.Exists(oldCs))
                {
                    File.Move(oldCs, newCs);
                    _report.Add(new ConversionNote(Path.GetRelativePath(dir, oldCs), 0, NoteSeverity.Info,
                        "RENAME-CODEBEHIND", "代码后置重命名为 .axaml.cs（与 .axaml 保持配对）。"));
                }
            }
        }

        // ---------- 4. csproj ----------
        var projTransformer = new ProjectFileTransformer(_options);
        var projResult = projTransformer.Transform(csprojText, Path.GetFileName(csprojPath), usesDataGrid);
        _report.AddRange(projResult.Notes);
        if (!_options.DryRun)
        {
            File.WriteAllText(csprojPath, projResult.Xml);
        }
        _report.ProjectFilesRewritten++;

        // ---------- 5. 启动引导 ----------
        if (_options.GenerateBootstrap)
        {
            var boot = new BootstrapGenerator(_options);

            // Program.cs
            var programPath = Path.Combine(dir, "Program.cs");
            if (projResult.IsExecutable || appXamlPath != null)
            {
                if (!File.Exists(programPath))
                {
                    if (!_options.DryRun) File.WriteAllText(programPath, boot.BuildProgramCs(rootNs));
                    _report.BootstrapFilesGenerated++;
                    _report.Add(new ConversionNote(projName, 0, NoteSeverity.Info, "BOOTSTRAP-PROGRAM",
                        "已生成 Program.cs（[STAThread] Main + AppBuilder.UsePlatformDetect）。"));
                }
                else
                {
                    _report.Add(new ConversionNote(projName, 0, NoteSeverity.Manual, "BOOTSTRAP-PROGRAM-EXISTS",
                        "已存在 Program.cs：请确认入口使用 AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args)。"));
                }
            }

            // App 代码后置
            if (appXamlPath != null)
            {
                var appCsFinal = appCs ?? Path.ChangeExtension(appXamlPath, ".axaml") + ".cs";

                if (File.Exists(appCsFinal))
                {
                    var patched = boot.PatchAppCodeBehind(File.ReadAllText(appCsFinal),
                        Path.GetRelativePath(dir, appCsFinal), startupWindowClass);
                    _report.AddRange(boot.Notes);
                    if (!_options.DryRun) File.WriteAllText(appCsFinal, patched);
                }
                else
                {
                    if (!_options.DryRun)
                        File.WriteAllText(appCsFinal, boot.BuildAppCodeBehindCs(rootNs, startupWindowClass.Length > 0 ? startupWindowClass : "MainWindow"));
                    _report.BootstrapFilesGenerated++;
                    _report.Add(new ConversionNote(projName, 0, NoteSeverity.Info, "BOOTSTRAP-APP",
                        "已生成 App 代码后置（OnFrameworkInitializationCompleted 创建主窗口）。"));
                }
            }
        }

        _report.ProjectsConverted++;
        return true;
    }

    /// <summary>
    /// XAML 预扫描：收集跨文件资源键（文本级正则，解析失败的文件不阻塞扫描）。
    /// styleKeys：命名键（Style x:Key / 直接资源）；typeThemeKeys：类型键
    /// （{x:Type X} 的 x:Key 及全部 TargetType，keyless 样式转换后亦成类型键）；
    /// booleanToVisibilityKeys：BooleanToVisibilityConverter 的键（元素将被删除，绑定解包需键匹配）。
    /// </summary>
    private static void PreScanKeys(string source,
        HashSet<string> styleKeys, HashSet<string> typeThemeKeys, HashSet<string> booleanToVisibilityKeys)
    {
        foreach (Match m in Regex.Matches(source, @"x:Key\s*=\s*""(?<key>[^""]+)"""))
        {
            var key = m.Groups["key"].Value.Trim();
            if (key.StartsWith("{x:Type", StringComparison.Ordinal))
                typeThemeKeys.Add(key);
            else if (key.Length > 0)
                styleKeys.Add(key);
        }

        // 全部 TargetType 均构成类型键（keyless 样式转换后补 {x:Type T} 键）
        foreach (Match m in Regex.Matches(source,
                     @"TargetType\s*=\s*""(?:\{x:Type\s+)?(?<type>[\w:.]+)\}?"""))
            typeThemeKeys.Add($"{{x:Type {m.Groups["type"].Value.Trim()}}}");

        // BooleanToVisibilityConverter：<BooleanToVisibilityConverter x:Key="X" .../>
        foreach (Match m in Regex.Matches(source,
                     @"<BooleanToVisibilityConverter[^>]*?\bx:Key\s*=\s*""(?<key>[^""]+)"""))
            booleanToVisibilityKeys.Add(m.Groups["key"].Value.Trim());
    }

    private void EnsureDataGridTheme(string appAxamlPath, string projectDir)
    {
        var text = File.ReadAllText(appAxamlPath);
        if (text.Contains("Avalonia.Controls.DataGrid", StringComparison.Ordinal))
            return; // 已有 DataGrid 主题/命名空间引用

        try
        {
            var doc = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            var app = doc.Root;
            if (app == null) return;

            var styles = app.Elements().FirstOrDefault(e =>
                e.Name.LocalName is "Styles" or "Application.Styles");
            if (styles == null) return;

            var ns = styles.Name.NamespaceName;
            var include = new XElement(XName.Get("StyleInclude", ns),
                new XAttribute("Source", KnownMaps.DataGridThemeSource));

            // 放到 FluentTheme 之后，保证可覆盖基础主题
            var theme = styles.Elements().LastOrDefault(e => e.Name.LocalName is "FluentTheme" or "SimpleTheme");
            if (theme != null) theme.AddAfterSelf(new XText("\n      "), include);
            else styles.Add(include);

            doc.Declaration = null;
            File.WriteAllText(appAxamlPath, doc.ToString(SaveOptions.None));
            _report.Add(new ConversionNote(Path.GetRelativePath(projectDir, appAxamlPath), 0, NoteSeverity.Info,
                "XAML-DATAGRID-THEME",
                $"已注入 <StyleInclude Source=\"{KnownMaps.DataGridThemeSource}\" />（DataGrid 控件需要该主题才有默认外观）。"));
        }
        catch (Exception ex)
        {
            _report.Add(new ConversionNote(Path.GetRelativePath(projectDir, appAxamlPath), 0, NoteSeverity.Warning,
                "XAML-DATAGRID-THEME", $"DataGrid 主题注入失败：{ex.Message}，请手工添加 StyleInclude。"));
        }
    }

    private static string ResolveStartupWindowClass(string dir, string? startupUri, string rootNs)
    {
        if (string.IsNullOrWhiteSpace(startupUri)) return "MainWindow";
        try
        {
            var axamlPath = Path.GetFullPath(Path.Combine(dir, startupUri.TrimStart('/')));
            axamlPath = File.Exists(axamlPath) ? axamlPath : Path.ChangeExtension(axamlPath, ".axaml");
            if (!File.Exists(axamlPath)) return "MainWindow";

            var cls = XDocument.Load(axamlPath).Root?
                .Attribute(XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value;
            if (!string.IsNullOrEmpty(cls)) return cls!;
        }
        catch
        {
            // 回退：文件名 + 根命名空间
        }
        return rootNs.Length > 0
            ? $"{rootNs}.{Path.GetFileNameWithoutExtension(startupUri)}"
            : Path.GetFileNameWithoutExtension(startupUri);
    }

    private static bool IsUnderObjBin(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains(".wpf-backup", StringComparison.Ordinal);

    private static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
        {
            if (IsUnderObjBin(dir) || dir.Contains(".wpf-backup")) continue;
            Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, dir)));
        }
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            if (IsUnderObjBin(file) || file.Contains(".wpf-backup")) continue;
            var target = Path.Combine(dst, Path.GetRelativePath(src, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}

/// <summary>入口选择：.sln / 目录 / 单个 csproj。</summary>
public static class ConversionRunner
{
    public static (ConversionReport Report, string ReportPath) Convert(string target, ConversionOptions options)
    {
        var report = new ConversionReport { Root = Path.GetFullPath(target), Options = options };

        var projects = new List<string>();
        if (File.Exists(target) && target.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(target),
                         @"Project\([^)]+\)\s*=\s*""[^""]+"",\s*""(?<path>[^""]+\.(?:csproj|vbproj))""", RegexOptions.IgnoreCase))
            {
                var p = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(target))!, m.Groups["path"].Value));
                if (File.Exists(p) && p.EndsWith(".csproj")) projects.Add(p);
            }
        }
        else if (File.Exists(target) && target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            projects.Add(Path.GetFullPath(target));
        }
        else if (Directory.Exists(target))
        {
            projects = Directory.EnumerateFiles(target, "*.csproj", SearchOption.AllDirectories)
                .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                            !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                            !p.Contains(".wpf-backup"))
                .ToList();
        }

        if (projects.Count == 0)
            report.Add(new ConversionNote(target, 0, NoteSeverity.Manual, "NO-PROJECT",
                "未找到可转换的 WPF 工程（仅支持 SDK 风格 .csproj / .sln / 目录）。"));

        foreach (var p in projects)
        {
            try
            {
                new ProjectConverter(options, report).Convert(p);
            }
            catch (Exception ex)
            {
                report.Add(new ConversionNote(Path.GetFileName(p), 0, NoteSeverity.Manual, "PROJECT-ERROR",
                    $"工程转换失败：{ex.Message}"));
                Console.Error.WriteLine(ex);
            }
        }

        var reportPath = Path.Combine(
            Directory.Exists(target) ? target : Path.GetDirectoryName(Path.GetFullPath(target))!,
            "wpf2avalonia-report.md");
        return (report, reportPath);
    }
}
