namespace WpfToAvalonia.Core.Model;

/// <summary>转换选项。</summary>
public sealed record ConversionOptions
{
    /// <summary>目标框架，默认 net10.0。</summary>
    public string TargetFramework { get; init; } = "net10.0";

    /// <summary>Avalonia NuGet 包版本（v12 起推荐 .NET 10）。</summary>
    public string AvaloniaVersion { get; init; } = "12.1.1";

    /// <summary>只分析不改写文件。</summary>
    public bool DryRun { get; init; }

    /// <summary>转换前把项目备份到 {项目名}.wpf-backup。</summary>
    public bool Backup { get; init; } = true;

    /// <summary>是否添加 Avalonia.Fonts.Inter（与官方模板一致）。</summary>
    public bool AddInterFont { get; init; } = true;

    /// <summary>是否生成 Program.cs / App 启动引导。</summary>
    public bool GenerateBootstrap { get; init; } = true;
}
