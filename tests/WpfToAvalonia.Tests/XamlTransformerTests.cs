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
                <Frame Source="Page1.xaml" />
            </Window>
            """);

        Assert.Contains(r.Notes, n => n.Rule == "XAML-UNSUPPORTED-ELEMENT" && n.Severity == NoteSeverity.Manual);
        Assert.Contains("<Frame", r.Xaml); // 保留原样 + 提示（可编译语义由人工替换）
    }

    [Fact]
    public void RichTextBox_IsRenamedTo_TextBox_WithManualNote()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <RichTextBox x:Name="OutputTextBox" IsReadOnly="True" IsDocumentEnabled="True" />
            </Window>
            """);

        Assert.Contains("<TextBox", r.Xaml);
        Assert.DoesNotContain("RichTextBox", r.Xaml);
        Assert.DoesNotContain("IsDocumentEnabled", r.Xaml);
        Assert.Contains(r.Notes, n => n.Rule == "XAML-RICHTEXTBOX");
    }

    [Fact]
    public void Hyperlink_IsRenamedTo_HyperlinkButton_RequestNavigateDropped()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Hyperlink NavigateUri="https://example.com" RequestNavigate="H_Click">
                    <TextBlock Text="example" />
                </Hyperlink>
            </Window>
            """);

        Assert.Contains("<HyperlinkButton", r.Xaml);
        Assert.Contains("NavigateUri=\"https://example.com\"", r.Xaml);
        Assert.DoesNotContain("RequestNavigate", r.Xaml);
        Assert.Contains(r.Notes, n => n.Rule == "XAML-HYPERLINK");
    }

    [Fact]
    public void Hyperlink_PropertyElement_OwnerPrefix_FollowsRename()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Hyperlink>
                    <Hyperlink.NavigateUri>https://example.com</Hyperlink.NavigateUri>
                    <TextBlock Text="example" />
                </Hyperlink>
            </Window>
            """);

        Assert.Contains("HyperlinkButton.NavigateUri", r.Xaml);
        Assert.DoesNotContain("Hyperlink.NavigateUri", r.Xaml);
    }

    [Fact]
    public void ListView_IsRenamedTo_ListBox_AndViewRemoved()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ListView x:Name="RevList">
                    <ListView.View>
                        <GridView>
                            <GridViewColumn Header="Index" DisplayMemberBinding="{Binding Index}" />
                        </GridView>
                    </ListView.View>
                </ListView>
            </Window>
            """);

        Assert.Contains("<ListBox", r.Xaml);
        // 元素级断言：解析后无 ListView/GridView 节点（注释里的原文不算）
        var doc = System.Xml.Linq.XDocument.Parse(r.Xaml);
        Assert.DoesNotContain(doc.Descendants(), d => d.Name.LocalName == "ListView");
        Assert.DoesNotContain(doc.Descendants(), d => d.Name.LocalName.Contains("GridView"));
        Assert.Contains(r.Notes, n => n.Rule == "XAML-LISTVIEW-VIEW");
    }

    [Fact]
    public void ListView_ViewAttribute_IsRemoved()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Window.Resources>
                    <GridView x:Key="Cols" />
                </Window.Resources>
                <ListView View="{StaticResource Cols}" />
            </Window>
            """);

        Assert.DoesNotContain("View=", r.Xaml);
        // 资源内 GridView 整体注释移除（解析后无 GridView 元素，仅剩 TODO 注释）
        var doc = System.Xml.Linq.XDocument.Parse(r.Xaml);
        Assert.DoesNotContain(doc.Descendants(), d => d.Name.LocalName == "GridView");
        Assert.Contains("TODO(wpf2avalonia)", r.Xaml);
    }

    [Fact]
    public void ListViewItemStyle_TargetType_Renames_ToListBoxItem()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Window.Resources>
                    <Style x:Key="ItemStyle" TargetType="ListViewItem">
                        <Setter Property="Padding" Value="4" />
                    </Style>
                </Window.Resources>
                <ListView ItemContainerStyle="{StaticResource ItemStyle}" />
            </Window>
            """);

        Assert.Contains("TargetType=\"ListBoxItem\"", r.Xaml);
        Assert.Contains("{x:Type ListBoxItem}", r.Xaml); // BasedOn/类型键链同步
        Assert.Contains("ItemContainerTheme", r.Xaml);
    }

    [Fact]
    public void GridViewColumnHeaderTheme_IsRemovedAsComment()
    {
        var r = Transform("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Style x:Key="HeaderStyle" TargetType="GridViewColumnHeader">
                    <Setter Property="Background" Value="#FFDDDDDD" />
                </Style>
            </ResourceDictionary>
            """);

        // 主题整体注释移除：解析后无 ControlTheme 元素
        var doc = System.Xml.Linq.XDocument.Parse(r.Xaml);
        Assert.DoesNotContain(doc.Descendants(), d => d.Name.LocalName == "ControlTheme");
        Assert.Contains("TODO(wpf2avalonia)", r.Xaml);
        Assert.Contains(r.Notes, n => n.Rule == "XAML-GRIDVIEW-THEME");
    }

    [Fact]
    public void GridViewRowPresenter_Renames_To_ContentPresenter()
    {
        var r = Transform("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Style TargetType="ListViewItem">
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate TargetType="ListViewItem">
                                <GridViewRowPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" />
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>
            </ResourceDictionary>
            """);

        Assert.Contains("<ContentPresenter", r.Xaml);
        Assert.DoesNotContain("GridViewRowPresenter", r.Xaml);
    }

    [Fact]
    public void MouseDoubleClick_Renames_To_DoubleTapped()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <ListBox MouseDoubleClick="List_DoubleClick" SelectionChanged="List_Selection" />
            </Window>
            """);

        Assert.Contains("DoubleTapped=\"List_DoubleClick\"", r.Xaml);
        Assert.DoesNotContain("MouseDoubleClick", r.Xaml);
    }

    [Fact]
    public void Nested_XmlnsDeclarations_AreHoistedOrRemoved()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Border x:Name="B1" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Border x:Name="B2" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
              </Border>
            </Window>
            """);

        // 嵌套 xmlns:x 上提到根（根原先未声明 x），根声明唯一
        var doc = System.Xml.Linq.XDocument.Parse(r.Xaml);
        var root = doc.Root!;
        var rootX = root.Attributes().Count(a => a.IsNamespaceDeclaration && a.Name.LocalName == "x");
        Assert.Equal(1, rootX);
        // 非根元素不再有任何 xmlns 声明
        var nested = root.Descendants().SelectMany(e => e.Attributes())
            .Count(a => a.IsNamespaceDeclaration);
        Assert.Equal(0, nested);
        // 序列化文本里 xmlns:x 只出现在根元素行
        var occurrences = System.Text.RegularExpressions.Regex.Matches(r.Xaml, "xmlns:x").Count;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void AdornerDecorator_IsUnwrapped_AttributesTransferred()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Grid>
                    <Grid.RowDefinitions>
                        <RowDefinition />
                        <RowDefinition />
                    </Grid.RowDefinitions>
                    <AdornerDecorator Grid.Row="1">
                        <Grid>
                            <TextBlock Text="Content" />
                        </Grid>
                    </AdornerDecorator>
                </Grid>
            </Window>
            """);

        Assert.DoesNotContain("AdornerDecorator", r.Xaml);
        // 布局特性转移给首个子元素（Grid.Row=1）
        var doc = System.Xml.Linq.XDocument.Parse(r.Xaml);
        var innerGrid = doc.Descendants().First(d => d.Name.LocalName == "Grid" &&
            d.Elements().Any(c => c.Name.LocalName == "TextBlock"));
        Assert.Equal("1", innerGrid.Attribute("Grid.Row")?.Value);
        Assert.Contains("Text=\"Content\"", r.Xaml);
    }

    [Fact]
    public void AdornerDecorator_Unwrap_PreservesInnerTransformations()
    {
        // 回归：AddBeforeSelf 对带父级节点是克隆而非移动——曾导致解包子树的转换全部丢失
        //（留在树里的是未转换克隆副本：Hyperlink 未改名、xmlns:x 残留 → AXN0003，ForkPlus 实测）
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Grid>
                    <AdornerDecorator>
                        <Hyperlink NavigateUri="https://example.com" Style="{DynamicResource S}" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" RequestNavigate="H" />
                    </AdornerDecorator>
                </Grid>
            </Window>
            """);

        // 解包后子树必须已转换（克隆副本曾丢失全部转换）
        Assert.Contains("HyperlinkButton NavigateUri", r.Xaml);
        Assert.DoesNotContain("AdornerDecorator", r.Xaml);
        // 非根元素不得残留 xmlns 声明（AXN0003；注意 XDocument.Descendants 含根，须从根出发）
        var doc = System.Xml.Linq.XDocument.Parse(r.Xaml);
        Assert.DoesNotContain(doc.Root!.Descendants(),
            e => e.Attributes().Any(a => a.IsNamespaceDeclaration));
    }

    [Fact]
    public void PasswordBox_RenamesToTextBox_WithPasswordChar()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <PasswordBox x:Name="InputPasswordBox" Margin="4" />
            </Window>
            """);

        Assert.Contains("<TextBox", r.Xaml);
        Assert.Contains("PasswordChar=\"●\"", r.Xaml);
        Assert.Contains(r.Notes, n => n.Rule == "XAML-PASSWORDBOX");
        // 显式指定时保留原值
        var r2 = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <PasswordBox PasswordChar="*" />
            </Window>
            """);
        Assert.Contains("PasswordChar=\"*\"", r2.Xaml);
    }

    [Fact]
    public void VsmPropertyElement_CommentedOut_WholeBlock()
    {
        // 反射探针17：Avalonia 12 无 VSM 类型。属性元素 <VisualStateManager.VisualStateGroups>
        //（点在 LocalName 内）原样残留会报 AVLN2000 Unknown type（ForkPlus Calendar 实测）
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid>
                    <VisualStateManager.VisualStateGroups>
                        <VisualStateGroup x:Name="CommonStates">
                            <VisualState x:Name="Normal" />
                            <VisualState x:Name="Disabled">
                                <Storyboard><DoubleAnimation To="0.5" /></Storyboard>
                            </VisualState>
                        </VisualStateGroup>
                    </VisualStateManager.VisualStateGroups>
                    <TextBlock Text="keep" />
                </Grid>
            </Window>
            """);

        // 存活树中无 VSM 族（注释里的原始片段不算）
        var doc = System.Xml.Linq.XDocument.Parse(r.Xaml);
        Assert.DoesNotContain(doc.Root!.Descendants(),
            e => e.Name.LocalName.StartsWith("VisualState", StringComparison.Ordinal));
        Assert.DoesNotContain(doc.Root!.Descendants(), e => e.Name.LocalName == "Storyboard");
        Assert.Contains("keep", r.Xaml);
        Assert.Contains(r.Notes, n => n.Rule == "XAML-VSM-REMOVED");
    }

    [Fact]
    public void BitmapImageResource_BecomesAvaloniaUriString()
    {
        // 反射探针17：Avalonia 12 Bitmap 无属性语法 ctor/UriSource——字典级图片唯一编译期合法
        // 等价物是 x:String 存 avares URI（键不断链，引用处运行时解析）
        var r = Transform("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <BitmapImage x:Key="TagIcon" UriSource="/Assets/Tag.png" />
            </ResourceDictionary>
            """);

        Assert.DoesNotContain("BitmapImage", r.Xaml);
        Assert.Contains("""<x:String x:Key="TagIcon">avares://TestApp/Assets/Tag.png</x:String>""", r.Xaml);
        Assert.Contains(r.Notes, n => n.Rule == "XAML-BITMAPIMAGE-STRING");
    }

    [Fact]
    public void OxyPlotNamespace_ElementsCommentedOut_RootDeclRemoved()
    {
        var r = Transform("""
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:oxy="http://oxyplot.org/wpf">
                <Grid>
                    <oxy:PlotView x:Name="LinePlot" />
                    <TextBlock Text="keep" />
                </Grid>
            </Window>
            """);

        // 包已隔离：oxy 元素注释移除 + 根声明清理，其余内容保留
        Assert.Contains("TODO(wpf2avalonia)", r.Xaml);
        Assert.Contains("keep", r.Xaml);
        // 存活树中无 oxy 命名空间元素（注释里保留的原始片段不算）
        var doc = System.Xml.Linq.XDocument.Parse(r.Xaml);
        Assert.DoesNotContain(doc.Root!.Descendants(), e => e.Name.NamespaceName == "http://oxyplot.org/wpf");
        // 根元素不得再声明 oxy 命名空间。注：注释文本里保留的原始片段（el.ToString()
        // 会内联元素用到的命名空间声明）不属于 XML 语义，不在解析树检查范围内。
        Assert.DoesNotContain(doc.Root!.Attributes(),
            a => a.IsNamespaceDeclaration && a.Value == "http://oxyplot.org/wpf");
        Assert.Contains(r.Notes, n => n.Rule == "XAML-WPF-LIB");
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
