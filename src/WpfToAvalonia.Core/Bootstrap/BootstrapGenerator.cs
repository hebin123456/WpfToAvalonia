using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using WpfToAvalonia.Core.CSharp;
using WpfToAvalonia.Core.Model;

namespace WpfToAvalonia.Core.Bootstrap;

/// <summary>生成/修补 Avalonia 启动引导：Program.cs、App 代码后置、OnFrameworkInitializationCompleted。</summary>
public sealed class BootstrapGenerator
{
    private readonly ConversionOptions _options;
    private readonly List<ConversionNote> _notes = new();

    public IReadOnlyList<ConversionNote> Notes => _notes;

    public BootstrapGenerator(ConversionOptions options) => _options = options;

    /// <summary>WPF 没有 Program.cs（入口由 MSBuild 生成），Avalonia 需要显式入口点。</summary>
    public string BuildProgramCs(string rootNamespace) =>
        $$"""
          using System;
          using Avalonia;

          namespace {{rootNamespace}};

          internal static class Program
          {
              [STAThread]
              public static void Main(string[] args) => BuildAvaloniaApp()
                  .StartWithClassicDesktopLifetime(args);

              // 如需自定义字体/渲染等，请在此扩展 AppBuilder。
              public static AppBuilder BuildAvaloniaApp()
                  => AppBuilder.Configure<App>()
                      .UsePlatformDetect()
                      {{(_options.AddInterFont ? ".WithInterFont()" : "")}}
                      .LogToTrace();
          }
          """;

    public string BuildAppCodeBehindCs(string rootNamespace, string startupWindowClass) =>
        $$"""
          using Avalonia;
          using Avalonia.Markup.Xaml;

          namespace {{rootNamespace}};

          public partial class App : Application
          {
              public override void Initialize() => AvaloniaXamlLoader.Load(this);

              public override void OnFrameworkInitializationCompleted()
              {
                  if (ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                  {
                      desktop.MainWindow = new {{startupWindowClass}}();
                  }

                  base.OnFrameworkInitializationCompleted();
              }
          }
          """;

    /// <summary>已有 App.xaml.cs 时，注入 OnFrameworkInitializationCompleted（若缺）。</summary>
    public string PatchAppCodeBehind(string source, string appFile, string startupWindowClass)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: appFile);
        var root = (CompilationUnitSyntax)tree.GetCompilationUnitRoot();

        var appClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.ValueText == "App");
        if (appClass == null) return source;

        var hasOverride = appClass.Members.OfType<MethodDeclarationSyntax>()
            .Any(m => m.Identifier.ValueText == "OnFrameworkInitializationCompleted");

        if (!hasOverride && startupWindowClass.Length > 0)
        {
            var method = SyntaxFactory.ParseMemberDeclaration(
                $$"""
                          public override void OnFrameworkInitializationCompleted()
                          {
                              if (ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                              {
                                  desktop.MainWindow = new {{startupWindowClass}}();
                              }

                              base.OnFrameworkInitializationCompleted();
                          }
                  """)!;
            var newClass = appClass.AddMembers(method);
            root = root.ReplaceNode(appClass, newClass);
            _notes.Add(new ConversionNote(appFile, appClass.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                NoteSeverity.Info, "BOOTSTRAP-APP",
                $"已注入 OnFrameworkInitializationCompleted：desktop.MainWindow = new {startupWindowClass}();"));
        }
        else if (!hasOverride)
        {
            _notes.Add(new ConversionNote(appFile, 0, NoteSeverity.Manual, "BOOTSTRAP-APP",
                "未找到启动窗口，无法自动注入 OnFrameworkInitializationCompleted，请手工创建主窗口。"));
        }

        // 确保 using Avalonia;
        var transformer = new CSharpTransformer();
        var result = transformer.Transform(root.ToFullString(), appFile);
        foreach (var n in result.Notes) _notes.Add(n);
        return result.Code;
    }
}
