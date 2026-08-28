using System.Text;

namespace WpfToAvalonia.Core.Model;

/// <summary>整个转换任务的聚合结果与 Markdown 报告。</summary>
public sealed class ConversionReport
{
    private readonly List<ConversionNote> _notes = new();

    public string Root { get; init; } = "";
    public ConversionOptions Options { get; init; } = new();

    public int ProjectsConverted { get; set; }
    public int XamlFilesConverted { get; set; }
    public int XamlFilesRenamed { get; set; }
    public int CSharpFilesConverted { get; set; }
    public int ProjectFilesRewritten { get; set; }
    public int BootstrapFilesGenerated { get; set; }

    public IReadOnlyList<ConversionNote> Notes => _notes;

    public void Add(ConversionNote note) => _notes.Add(note);

    public void AddRange(IEnumerable<ConversionNote> notes) => _notes.AddRange(notes);

    public int ManualCount => _notes.Count(n => n.Severity == NoteSeverity.Manual);
    public int WarningCount => _notes.Count(n => n.Severity == NoteSeverity.Warning);
    public int InfoCount => _notes.Count(n => n.Severity == NoteSeverity.Info);

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# WPF → Avalonia 转换报告");
        sb.AppendLine();
        sb.AppendLine($"- 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- 目标框架：`{Options.TargetFramework}`　Avalonia：`{Options.AvaloniaVersion}`");
        sb.AppendLine($"- 转换根目录：`{Root}`");
        sb.AppendLine();
        sb.AppendLine("## 概览");
        sb.AppendLine();
        sb.AppendLine("| 指标 | 数量 |");
        sb.AppendLine("| --- | ---: |");
        sb.AppendLine($"| 转换项目 | {ProjectsConverted} |");
        sb.AppendLine($"| XAML 文件（→ .axaml） | {XamlFilesConverted} |");
        sb.AppendLine($"| C# 文件（Roslyn AST 改写） | {CSharpFilesConverted} |");
        sb.AppendLine($"| csproj 重写 | {ProjectFilesRewritten} |");
        sb.AppendLine($"| 引导文件生成（Program.cs 等） | {BootstrapFilesGenerated} |");
        sb.AppendLine($"| 自动处理（INFO） | {InfoCount} |");
        sb.AppendLine($"| 需人工确认（WARN） | {WarningCount} |");
        sb.AppendLine($"| **需人工处理（TODO）** | **{ManualCount}** |");
        sb.AppendLine();

        AppendSection(sb, NoteSeverity.Manual, "需人工处理（TODO）");
        AppendSection(sb, NoteSeverity.Warning, "已自动转换、建议复核（WARN）");
        AppendSection(sb, NoteSeverity.Info, "自动处理明细（INFO）");

        sb.AppendLine("## 建议后续步骤");
        sb.AppendLine();
        sb.AppendLine("1. 按 TODO 清单逐条处理（样式/动画/对话框/平台 API 是主要人工点）。");
        sb.AppendLine("2. `dotnet restore && dotnet build` 检查编译错误（Avalonia XAML 编译器会给出精确定位）。");
        sb.AppendLine("3. 运行应用核对视觉与交互差异（无 Trigger 运行时语义、默认字体、对话框行为）。");
        sb.AppendLine("4. 如需 DevTools，Avalonia 12 已移除 Avalonia.Diagnostics，可改用 AvaloniaUI.DiagnosticsSupport。");
        return sb.ToString();
    }

    private void AppendSection(StringBuilder sb, NoteSeverity severity, string title)
    {
        var items = _notes.Where(n => n.Severity == severity).ToList();
        if (items.Count == 0) return;

        sb.AppendLine($"## {title}");
        sb.AppendLine();
        foreach (var group in items.GroupBy(n => n.Rule).OrderBy(g => g.Key))
        {
            sb.AppendLine($"### {group.Key}（{group.Count()}）");
            sb.AppendLine();
            foreach (var n in group.OrderBy(n => n.File).ThenBy(n => n.Line))
            {
                sb.AppendLine($"- `{n.Location}`　{n.Message}");
            }
            sb.AppendLine();
        }
    }
}
