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
    public void KeylessStyle_Becomes_TypeKeyed_ControlTheme()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
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

        // keyless 隐式样式 → 类型键 ControlTheme（资源链隐式生效）
        Assert.Contains("<ControlTheme", r.Xaml);
        Assert.Contains("x:Key=\"{x:Type Button}\"", r.Xaml);
        Assert.Contains("TargetType=\"Button\"", r.Xaml);
        // Style.Triggers → ^:pointerover 嵌套样式（直接在 ControlTheme 下，不在容器内）
        Assert.Contains("Selector=\"^:pointerover\"", r.Xaml);
        Assert.DoesNotContain("Style.Triggers", r.Xaml);
    }

    [Fact]
    public void KeyedStyle_Becomes_ControlTheme_AndReferenceBecomesTheme()
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

        // keyed 样式保键转 ControlTheme；引用处 Style= → Theme=
        Assert.Contains("<ControlTheme", r.Xaml);
        Assert.Contains("x:Key=\"PrimaryButton\"", r.Xaml);
        Assert.Contains("Theme=\"{StaticResource PrimaryButton}\"", r.Xaml);
        Assert.DoesNotContain("Style=\"{StaticResource", r.Xaml);
    }

    [Fact]
    public void SetterOnlyNamedTheme_GetsAutoBasedOn()
    {
        var r = new XamlTransformer("TestApp").Transform("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Style x:Key="{x:Type Button}" TargetType="{x:Type Button}">
                    <Setter Property="Background" Value="Red" />
                </Style>
                <Style x:Key="AccentButton" TargetType="{x:Type Button}">
                    <Setter Property="Background" Value="Blue" />
                </Style>
            </ResourceDictionary>
            """, "Styles/Buttons.xaml");

        // setter-only 命名主题自动补 BasedOn={StaticResource {x:Type X}}（同文件已有类型键主题）
        Assert.Contains("BasedOn=\"{StaticResource {x:Type Button}}\"", r.Xaml);
        Assert.Contains(r.Notes, n => n.Rule == "XAML-BASEDON-AUTO");
    }

    [Fact]
    public void TemplateStyle_Becomes_ControlTheme_StayingInResources()
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

        Assert.Contains("<ControlTheme", r.Xaml);
        Assert.Contains("TargetType=\"Button\"", r.Xaml);
        // 类型键 ControlTheme 经资源链生效，留在 Resources 中
        Assert.Contains("Window.Resources", r.Xaml);
    }

    [Fact]
    public void StylesDictionary_KeepsResourceDictionaryRoot()
    {
        var r = new XamlTransformer("TestApp").Transform("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Style x:Key="A" TargetType="Button">
                    <Setter Property="Background" Value="Red" />
                </Style>
            </ResourceDictionary>
            """, "Styles/Buttons.xaml");

        // 字典链方案：根保持 ResourceDictionary（经 ResourceInclude 合并）
        Assert.StartsWith("<ResourceDictionary", r.Xaml.TrimStart());
        Assert.Contains("<ControlTheme", r.Xaml);
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

    // ———————————————— ForkPlus 反编译 XAML 模式 ————————————————

    [Fact]
    public void Decompiler_OwnerPrefixes_AreStripped()
    {
        var r = new XamlTransformer("TestApp").Transform("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Style x:Key="S" TargetType="{x:Type TextBlock}">
                    <Setter Property="TextElement.FontSize" Value="14" />
                    <Setter Property="FrameworkElement.Margin" Value="0,12,0,6" />
                    <Setter Property="(Panel.Background)" Value="Red" />
                    <Setter Property="Grid.Row" Value="1" />
                </Style>
            </ResourceDictionary>
            """, "Styles/T.xaml");

        // 基类所有者前缀剥除；附加属性前缀保留
        Assert.Contains("Property=\"FontSize\"", r.Xaml);
        Assert.Contains("Property=\"Margin\"", r.Xaml);
        Assert.Contains("Property=\"Background\"", r.Xaml);
        Assert.Contains("Property=\"Grid.Row\"", r.Xaml);
    }

    [Fact]
    public void TemplateTriggers_ConvertTo_TemplateSelectorStyles()
    {
        var r = new XamlTransformer("TestApp").Transform("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Style x:Key="{x:Type Button}" TargetType="{x:Type Button}">
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate TargetType="{x:Type Button}">
                                <Border x:Name="Bd" Background="{TemplateBinding Background}">
                                    <ContentPresenter />
                                </Border>
                                <ControlTemplate.Triggers>
                                    <Trigger Property="IsMouseOver" Value="True">
                                        <Setter TargetName="Bd" Property="Background" Value="Blue" />
                                    </Trigger>
                                    <Trigger Property="IsPressed" Value="True">
                                        <Setter Property="Background" Value="Gray" />
                                    </Trigger>
                                </ControlTemplate.Triggers>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>
            </ResourceDictionary>
            """, "Styles/Button.xaml");

        // Trigger(IsMouseOver) + Setter TargetName="Bd" → ^:pointerover /template/ Border#Bd
        Assert.Contains("Selector=\"^:pointerover /template/ Border#Bd\"", r.Xaml);
        // Setter 无 TargetName（打控件本身）→ ^:pressed
        Assert.Contains("Selector=\"^:pressed\"", r.Xaml);
        Assert.DoesNotContain("ControlTemplate.Triggers", r.Xaml);
        Assert.Contains(r.Notes, n => n.Rule == "XAML-TEMPLATE-TRIGGER-CONVERT");
    }

    [Fact]
    public void Visibility_AndBooleanToVisibility_AreConvertedToIsVisible()
    {
        var r = new XamlTransformer("TestApp", booleanToVisibilityKeys: new HashSet<string> { "B2V" })
            .Transform("""
                <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                    <Window.Resources>
                        <BooleanToVisibilityConverter x:Key="B2V" />
                    </Window.Resources>
                    <StackPanel Visibility="Collapsed" />
                    <TextBlock Visibility="{Binding Show, Converter={StaticResource B2V}}" />
                </Window>
                """, "W.xaml");

        Assert.Contains("IsVisible=\"False\"", r.Xaml);
        // BooleanToVisibilityConverter 绑定 → IsVisible 直接绑定 bool
        Assert.Contains("IsVisible=\"{Binding Show}\"", r.Xaml);
        Assert.DoesNotContain("BooleanToVisibilityConverter", r.Xaml);
    }

    [Fact]
    public void KeylessDataTemplate_MovesToHostDataTemplates()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Window.Resources>
                    <DataTemplate DataType="{x:Type local:Item}">
                        <TextBlock Text="{Binding Name}" />
                    </DataTemplate>
                    <DataTemplate x:Key="Named">
                        <TextBlock Text="X" />
                    </DataTemplate>
                </Window.Resources>
                <ContentControl />
            </Window>
            """);

        // keyless DataTemplate → Window.DataTemplates（Avalonia 隐式模板位置）；keyed 模板留在 Resources
        Assert.Contains("Window.DataTemplates", r.Xaml);
        Assert.Contains("DataType=\"local:Item\"", r.Xaml);
        // keyed 模板仍留在 Resources
        var resourcesPart = r.Xaml[r.Xaml.IndexOf("Window.Resources")..r.Xaml.IndexOf("Window.DataTemplates")];
        Assert.Contains("x:Key=\"Named\"", resourcesPart);
    }

    [Fact]
    public void MergedDictionary_Source_BecomesResourceInclude_WithAvares()
    {
        var r = new XamlTransformer("TestApp").Transform("""
            <Application xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Application.Resources>
                    <ResourceDictionary>
                        <ResourceDictionary.MergedDictionaries>
                            <ResourceDictionary Source="pack://application:,,,/TestApp;component/Theme/Generic.Light.xaml" />
                            <ResourceDictionary Source="Styles/Colors.xaml" />
                        </ResourceDictionary.MergedDictionaries>
                    </ResourceDictionary>
                </Application.Resources>
            </Application>
            """, "App.xaml");

        // 带 Source 的字典 → ResourceInclude；pack URI 与相对路径统一归一化为 avares 绝对 URI
        Assert.Contains("<ResourceInclude", r.Xaml);
        Assert.Contains("Source=\"avares://TestApp/Theme/Generic.Light.axaml\"", r.Xaml);
        Assert.Contains("Source=\"avares://TestApp/Styles/Colors.axaml\"", r.Xaml);
    }

    [Fact]
    public void PathGeometry_StringResource_BecomesStreamGeometry()
    {
        var r = new XamlTransformer("TestApp").Transform("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <PathGeometry x:Key="Arrow" Figures="M 0 0 L 8 4 L 0 8 Z" />
            </ResourceDictionary>
            """, "Styles/G.xaml");

        Assert.Contains("<StreamGeometry", r.Xaml);
        Assert.Contains(">M 0 0 L 8 4 L 0 8 Z<", r.Xaml);
        Assert.DoesNotContain("Figures", r.Xaml);
    }

    [Fact]
    public void DataTemplateTrigger_VisibilitySetter_BecomesIsVisibleBinding()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Window.Resources>
                    <DataTemplate>
                        <StackPanel x:Name="Panel" />
                        <DataTemplate.Triggers>
                            <DataTrigger Binding="{Binding IsEmpty}" Value="True">
                                <Setter TargetName="Panel" Property="Visibility" Value="Collapsed" />
                            </DataTrigger>
                        </DataTemplate.Triggers>
                    </DataTemplate>
                </Window.Resources>
            </Window>
            """);

        // DataTrigger(IsEmpty=True)+Visibility=Collapsed → IsVisible=!IsEmpty
        Assert.Contains("IsVisible=\"{Binding !IsEmpty}\"", r.Xaml);
    }

    [Fact]
    public void ItemContainerStyle_RenamedToItemContainerTheme()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ListBox>
                    <ListBox.ItemContainerStyle>
                        <Style TargetType="ListBoxItem">
                            <Setter Property="Margin" Value="4" />
                        </Style>
                    </ListBox.ItemContainerStyle>
                </ListBox>
            </Window>
            """);

        Assert.Contains("ListBox.ItemContainerTheme", r.Xaml);
        // 容器样式内的 Style 同步转 ControlTheme
        Assert.Contains("<ControlTheme", r.Xaml);
    }

    [Fact]
    public void LabelTypeKey_RenamesTogetherWithTargetType()
    {
        var r = new XamlTransformer("TestApp").Transform("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Style x:Key="{x:Type Label}" TargetType="{x:Type Label}">
                    <Setter Property="FontSize" Value="12" />
                </Style>
            </ResourceDictionary>
            """, "Styles/L.xaml");

        // Label → TextBlock：类型键与 TargetType 同步重命名（否则键与目标类型不一致）
        Assert.Contains("x:Key=\"{x:Type TextBlock}\"", r.Xaml);
        Assert.Contains("TargetType=\"TextBlock\"", r.Xaml);
        Assert.DoesNotContain("{x:Type Label}", r.Xaml);
    }

    [Fact]
    public void StyleReferenceToRenamedTypeKey_IsSynced()
    {
        var r = new XamlTransformer("TestApp",
            typeThemeKeys: new HashSet<string> { "{x:Type Label}", "{x:Type TextBlock}" })
            .Transform("""
                <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                    <ContentControl Style="{StaticResource {x:Type Label}}" />
                </Window>
                """, "W.xaml");

        // 引用处的类型键也随 Label → TextBlock 重命名
        Assert.Contains("Theme=\"{StaticResource {x:Type TextBlock}}\"", r.Xaml);
    }
}
