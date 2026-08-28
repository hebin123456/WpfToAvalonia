using WpfToAvalonia.Core;
using WpfToAvalonia.Core.Model;

namespace WpfToAvalonia.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        // 兼容 "convert <target>" 动词形式
        if (args[0] is "convert" or "run")
        {
            if (args.Length == 1)
            {
                PrintUsage();
                return 1;
            }
            args = args[1..];
        }

        var target = args[0];
        var options = new ConversionOptions();
        string? reportPathOverride = null;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--tfm" when i + 1 < args.Length:
                    options = options with { TargetFramework = args[++i] };
                    break;
                case "--avalonia" when i + 1 < args.Length:
                    options = options with { AvaloniaVersion = args[++i] };
                    break;
                case "--report" when i + 1 < args.Length:
                    reportPathOverride = args[++i];
                    break;
                case "--dry-run":
                    options = options with { DryRun = true };
                    break;
                case "--no-backup":
                    options = options with { Backup = false };
                    break;
                case "--no-bootstrap":
                    options = options with { GenerateBootstrap = false };
                    break;
                case "--no-inter-font":
                    options = options with { AddInterFont = false };
                    break;
                default:
                    Console.Error.WriteLine($"未知参数：{args[i]}（--help 查看用法）");
                    return 1;
            }
        }

        if (!File.Exists(target) && !Directory.Exists(target))
        {
            Console.Error.WriteLine($"路径不存在：{target}");
            return 1;
        }

        Console.WriteLine($"WPF → Avalonia 转换");
        Console.WriteLine($"  目标：{Path.GetFullPath(target)}");
        Console.WriteLine($"  TFM：{options.TargetFramework}    Avalonia：{options.AvaloniaVersion}    模式：{(options.DryRun ? "仅分析（dry-run）" : "写入")}");
        Console.WriteLine();

        var (report, reportPath) = ConversionRunner.Convert(target, options);
        if (reportPathOverride != null) reportPath = reportPathOverride;

        Console.WriteLine("转换完成：");
        Console.WriteLine($"  工程               {report.ProjectsConverted}");
        Console.WriteLine($"  XAML (.xaml→.axaml) {report.XamlFilesConverted}");
        Console.WriteLine($"  C#（Roslyn AST）    {report.CSharpFilesConverted}");
        Console.WriteLine($"  csproj 重写         {report.ProjectFilesRewritten}");
        Console.WriteLine($"  引导文件            {report.BootstrapFilesGenerated}");
        Console.WriteLine($"  自动处理 INFO       {report.InfoCount}");
        Console.WriteLine($"  需复核   WARN       {report.WarningCount}");
        Console.WriteLine($"  需人工   TODO       {report.ManualCount}");
        Console.WriteLine();

        if (!options.DryRun)
        {
            File.WriteAllText(reportPath, report.ToMarkdown());
            Console.WriteLine($"报告已写入：{reportPath}");
        }
        else
        {
            Console.WriteLine("（dry-run 未写入任何文件，报告仅打印摘要）");
        }

        foreach (var todo in report.Notes.Where(n => n.Severity == NoteSeverity.Manual)
                     .GroupBy(n => n.Rule).OrderBy(g => g.Key))
        {
            Console.WriteLine($"  TODO [{todo.Key}] x{todo.Count()}");
        }

        foreach (var err in report.Notes.Where(n => n.Rule is "PROJECT-ERROR" or "NO-PROJECT"))
            Console.WriteLine($"  ⚠ {err.File}: {err.Message}");

        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            用法：wpf2ava <WPF工程.csproj | 解决方案.sln | 目录> [选项]

            选项：
              --tfm <tfm>        目标框架（默认 net10.0）
              --avalonia <ver>   Avalonia 包版本（默认 12.1.1）
              --dry-run          只分析并输出 TODO 清单，不写任何文件
              --no-backup        不生成 {项目名}.wpf-backup 备份
              --no-bootstrap     不生成 Program.cs / App 启动引导
              --no-inter-font    不添加 Avalonia.Fonts.Inter
              --report <path>    报告输出路径（默认转换目录下 wpf2avalonia-report.md）
            """);
    }
}
