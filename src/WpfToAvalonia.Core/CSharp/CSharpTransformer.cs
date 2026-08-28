using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using WpfToAvalonia.Core.Model;

namespace WpfToAvalonia.Core.CSharp;

public sealed class CSharpTransformResult
{
    public required string Code { get; init; }
    public IReadOnlyList<ConversionNote> Notes { get; init; } = Array.Empty<ConversionNote>();
    public bool WpfDetected { get; init; }
}

/// <summary>用 Roslyn 解析 → AST 重写 → 补全 using → 输出。</summary>
public sealed class CSharpTransformer
{
    public CSharpTransformResult Transform(string source, string filePath)
    {
        var wpfDetectedByText =
            source.Contains("System.Windows", StringComparison.Ordinal) ||
            source.Contains("DependencyProperty", StringComparison.Ordinal) ||
            source.Contains("MessageBox.Show", StringComparison.Ordinal) ||
            source.Contains("Application.Current", StringComparison.Ordinal);

        var tree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var root = (CompilationUnitSyntax)tree.GetCompilationUnitRoot();

        var rewriter = new WpfCSharpRewriter(filePath);
        var rewritten = (CompilationUnitSyntax?)rewriter.Visit(root) ?? root;
        rewritten = UsingsInjector.Inject(rewritten, rewriter.Notes.Count > 0 || wpfDetectedByText);

        return new CSharpTransformResult
        {
            Code = rewritten.ToFullString(),
            Notes = rewriter.Notes,
            WpfDetected = rewriter.WpfDetected || wpfDetectedByText,
        };
    }
}

/// <summary>根据文件中出现但未 using 的 Avalonia 类型补充命名空间。</summary>
internal static class UsingsInjector
{
    private static readonly (string Marker, string Using)[] Markers =
    {
        ("RoutedEventHandler", "using Avalonia.Interactivity;"),
        ("RoutedEventArgs", "using Avalonia.Interactivity;"),
        ("AvaloniaObject", "using Avalonia;"),
        ("AvaloniaProperty", "using Avalonia;"),
        ("StyledProperty", "using Avalonia;"),
        ("StyledPropertyMetadata", "using Avalonia;"),
        ("Thickness", "using Avalonia;"),
        ("CornerRadius", "using Avalonia;"),
        ("GridLength", "using Avalonia;"),
        ("HorizontalAlignment", "using Avalonia.Layout;"),
        ("VerticalAlignment", "using Avalonia.Layout;"),
        ("SizeToContent", "using Avalonia.Layout;"),
        ("IBrush", "using Avalonia.Media;"),
        ("SolidColorBrush", "using Avalonia.Media;"),
        ("FontWeights", "using Avalonia.Media;"),
        ("DrawingContext", "using Avalonia.Media;"),
        ("KeyEventArgs", "using Avalonia.Input;"),
        ("KeyModifiers", "using Avalonia.Input;"),
        ("PointerEventArgs", "using Avalonia.Input;"),
        ("DragEventArgs", "using Avalonia.Input;"),
        ("Dispatcher", "using Avalonia.Threading;"),
        ("DispatcherTimer", "using Avalonia.Threading;"),
        ("IValueConverter", "using Avalonia.Data.Converters;"),
        ("IMultiValueConverter", "using Avalonia.Data.Converters;"),
        ("StorageProvider", "using Avalonia.Platform.Storage;"),
    };

    public static CompilationUnitSyntax Inject(CompilationUnitSyntax root, bool wpfDetected)
    {
        if (!wpfDetected) return root;

        var text = root.ToFullString();
        var existing = root.Usings
            .Select(u => u.Name?.ToString() ?? "")
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var toAdd = Markers
            .Where(m => !existing.Contains(m.Using["using ".Length..^1]) &&
                        text.Contains(m.Marker, StringComparison.Ordinal))
            .Select(m => m.Using)
            .Distinct()
            .ToList();
        if (toAdd.Count == 0) return root;

        var parsed = toAdd.Select(u =>
            SyntaxFactory.ParseCompilationUnit(u + "\n").DescendantNodes()
                .OfType<UsingDirectiveSyntax>().First()).ToList();

        return root.WithUsings(root.Usings.AddRange(parsed));
    }
}
