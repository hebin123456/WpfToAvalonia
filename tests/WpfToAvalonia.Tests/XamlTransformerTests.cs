using WpfToAvalonia.Core.Model;
using WpfToAvalonia.Core.Xaml;

namespace WpfToAvalonia.Tests;

public class XamlTransformerTests
{
    private static XamlTransformResult Transform(string xaml) =>
        new XamlTransformer("TestApp").Transform(xaml, "Test.xaml");

    [Fact]
    public void NamespaceUri_IsReplaced_WithAvaloniaui()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
            </Window>
            """);

        Assert.Contains("https://github.com/avaloniaui", r.Xaml);
        Assert.DoesNotContain("schemas.microsoft.com/winfx/2006/xaml/presentation", r.Xaml);
    }

    [Fact]
    public void Label_IsRenamed_ToTextBlock_WithContentMoved()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Label Content="你好" />
            </Window>
            """);

        Assert.Contains("<TextBlock Text=\"你好\"", r.Xaml);
    }

    [Fact]
    public void MouseEvents_AreRenamed_ToPointerModel()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Button MouseLeftButtonDown="OnLeft" MouseMove="OnMove" />
                <Button MouseRightButtonDown="OnRight" />
            </Window>
            """);

        Assert.Contains("PointerPressed=\"OnLeft\"", r.Xaml);
        Assert.Contains("PointerMoved=\"OnMove\"", r.Xaml);
        // 右键事件合并到 PointerPressed 后需要运行时判断按键 → 警告
        Assert.Contains(r.Notes, n => n.Rule == "XAML-EVENT-RENAME" && n.Severity == NoteSeverity.Warning);
    }

    [Fact]
    public void CollidingMouseEvents_MergeWithWarning()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Button MouseLeftButtonDown="OnLeft" MouseRightButtonDown="OnRight" />
            </Window>
            """);

        // 左键/右键都映射到 PointerPressed，仅保留后者，并提示在处理器内区分按键
        Assert.Contains("PointerPressed=\"OnRight\"", r.Xaml);
        Assert.Contains(r.Notes, n => n.Rule == "XAML-EVENT-MERGE" && n.Severity == NoteSeverity.Warning);
    }

    [Fact]
    public void PropertyTrigger_ConvertsTo_PseudoClassNestedStyle()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Window.Resources>
                    <Style TargetType="Button">
                        <Setter Property="Background" Value="Red" />
                        <Style.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter Property="Background" Value="Blue" />
                            </Trigger>
                        </Style.Triggers>
                    </Style>
                </Window.Resources>
                <Button />
            </Window>
            """);

        Assert.Contains("Selector=\"Button\"", r.Xaml);
        Assert.Contains("Selector=\"^:pointerover\"", r.Xaml);
        Assert.DoesNotContain("Style.Triggers", r.Xaml);
    }

    [Fact]
    public void KeyedStyle_Gets_ClassSelector_AndReferenceBecomesClasses()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Window.Resources>
                    <Style x:Key="PrimaryButton" TargetType="Button">
                        <Setter Property="Background" Value="Red" />
                    </Style>
                </Window.Resources>
                <Button Style="{StaticResource PrimaryButton}" />
            </Window>
            """);

        Assert.Contains("Selector=\"Button.PrimaryButton\"", r.Xaml);
        Assert.Contains("Classes=\"PrimaryButton\"", r.Xaml);
        Assert.DoesNotContain("StaticResource", r.Xaml);
    }

    [Fact]
    public void TemplateStyle_Becomes_ControlTheme_WithTargetType()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Window.Resources>
                    <Style TargetType="Button">
                        <Setter Property="Template">
                            <Setter.Value>
                                <ControlTemplate TargetType="Button">
                                    <ContentPresenter />
                                </ControlTemplate>
                            </Setter.Value>
                        </Setter>
                    </Style>
                </Window.Resources>
                <Button />
            </Window>
            """);

        Assert.Contains("<ControlTheme TargetType=\"Button\"", r.Xaml);
        // ControlTheme 不使用 Selector 定位类型
        Assert.DoesNotContain("ControlTheme Selector", r.Xaml);
        // 默认控件主题迁移到 Window.Styles 才能生效
        Assert.Contains("Window.Styles", r.Xaml);
    }

    [Fact]
    public void StandaloneStylesDictionary_RootBecomes_Styles()
    {
        var r = new XamlTransformer("TestApp").Transform("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Style x:Key="A" TargetType="Button">
                    <Setter Property="Background" Value="Red" />
                </Style>
            </ResourceDictionary>
            """, "Styles/Buttons.xaml");

        Assert.StartsWith("<Styles", r.Xaml.TrimStart());
        Assert.Contains("Selector=\"Button.A\"", r.Xaml);
    }

    [Fact]
    public void DataGrid_StaysInDefaultNamespace_AndSetsFlag()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <DataGrid AutoGenerateColumns="False" />
            </Window>
            """);

        Assert.True(r.UsesDataGrid);
        Assert.Contains("<DataGrid ", r.Xaml);
        // Avalonia 11+ 中 DataGrid 映射到默认命名空间，无需额外 xmlns
        Assert.DoesNotContain("datagrid", r.Xaml);
    }

    [Fact]
    public void AppElement_StartupUriRemoved_AndFluentThemeAdded()
    {
        var r = Transform("""
            <Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         StartupUri="MainWindow.xaml">
            </Application>
            """);

        Assert.Equal("MainWindow.xaml", r.StartupUri);
        Assert.DoesNotContain("StartupUri", r.Xaml);
        Assert.Contains("<FluentTheme />", r.Xaml);
    }

    [Fact]
    public void PackUri_IsRewritten_ToAvares()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Image Source="pack://application:,,,/TestApp;component/Assets/logo.png" />
                <Image Source="/Assets/other.png" />
            </Window>
            """);

        Assert.Contains("avares://TestApp/Assets/logo.png", r.Xaml);
        Assert.Contains("avares://TestApp/Assets/other.png", r.Xaml);
    }

    [Fact]
    public void BindingOptions_WpfSpecific_AreRemoved()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <TextBox Text="{Binding Name, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" />
            </Window>
            """);

        Assert.DoesNotContain("UpdateSourceTrigger", r.Xaml);
        Assert.Contains("Mode=TwoWay", r.Xaml);
    }

    [Fact]
    public void UnsupportedElement_Produces_ManualNote()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <RichTextBox />
            </Window>
            """);

        Assert.Contains(r.Notes, n => n.Rule == "XAML-UNSUPPORTED-ELEMENT" && n.Severity == NoteSeverity.Manual);
    }

    [Fact]
    public void Output_HasNoXmlDeclaration()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
            </Window>
            """);

        Assert.False(r.Xaml.TrimStart().StartsWith("<?xml"));
    }
}
