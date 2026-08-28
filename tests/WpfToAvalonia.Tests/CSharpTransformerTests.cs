using WpfToAvalonia.Core.CSharp;
using WpfToAvalonia.Core.Model;

namespace WpfToAvalonia.Tests;

public class CSharpTransformerTests
{
    private static CSharpTransformResult Transform(string code) =>
        new CSharpTransformer().Transform(code, "Test.cs");

    [Fact]
    public void UsingDirectives_AreMapped_ToAvalonia()
    {
        var r = Transform("""
            using System.Windows;
            using System.Windows.Controls;
            using System.Windows.Media;

            namespace Demo;
            """);

        Assert.Contains("using Avalonia;", r.Code);
        Assert.Contains("using Avalonia.Controls;", r.Code);
        Assert.Contains("using Avalonia.Media;", r.Code);
        Assert.DoesNotContain("using System.Windows", r.Code);
    }

    [Fact]
    public void TypeNames_AreRenamed()
    {
        var r = Transform("""
            using System.Windows;

            namespace Demo
            {
                class C
                {
                    void M()
                    {
                        var dp = DependencyProperty.Register(nameof(C), typeof(string), typeof(C));
                        var b = new BitmapImage();
                    }
                }
            }
            """);

        Assert.Contains("AvaloniaProperty.Register", r.Code);
        Assert.Contains("Avalonia.Media.Imaging.Bitmap", r.Code);
    }

    [Fact]
    public void QualifiedTypes_AreRenamed()
    {
        var r = Transform("""
            namespace Demo
            {
                class C
                {
                    System.Windows.Point P() => new System.Windows.Point(1, 2);
                    System.Windows.Media.Brush B() => System.Windows.Media.Brushes.Red;
                }
            }
            """);

        Assert.Contains("Avalonia.Point", r.Code);
        Assert.Contains("Avalonia.Media.Brushes.Red", r.Code);
        Assert.DoesNotContain("System.Windows", r.Code);
    }

    [Fact]
    public void EventSubscription_IsRenamed_ToPointerModel()
    {
        var r = Transform("""
            using System.Windows.Input;

            namespace Demo
            {
                class C
                {
                    void M(System.Windows.Controls.Button btn)
                    {
                        btn.MouseLeftButtonDown += OnDown;
                        btn.MouseMove -= OnMove;
                    }
                }
            }
            """);

        Assert.Contains("btn.PointerPressed += OnDown", r.Code);
        Assert.Contains("btn.PointerMoved -= OnMove", r.Code);
    }

    [Fact]
    public void MissingAvaloniaUsings_AreInjected()
    {
        var r = Transform("""
            namespace Demo
            {
                class C : System.Windows.Data.IValueConverter
                {
                    public object Convert(object v) => v;
                }
            }
            """);

        Assert.Contains("Avalonia.Data.Converters", r.Code);
    }

    [Fact]
    public void NonWpfCode_IsNotDetected()
    {
        var r = Transform("""
            namespace Pure
            {
                class C { int X => 1; }
            }
            """);

        Assert.False(r.WpfDetected);
    }

    [Fact]
    public void MessageBox_GetsManualNote()
    {
        var r = Transform("""
            using System.Windows;

            namespace Demo
            {
                class C
                {
                    void M() => MessageBox.Show("hi");
                }
            }
            """);

        Assert.Contains(r.Notes, n => n.Severity == NoteSeverity.Manual);
    }
}
