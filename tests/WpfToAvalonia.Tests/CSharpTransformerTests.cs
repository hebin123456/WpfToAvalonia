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
    public void MouseDoubleClickEvent_IsRenamed_ToDoubleTapped()
    {
        var r = Transform("""
            using System.Windows.Controls;

            namespace Demo
            {
                class C
                {
                    void M(System.Windows.Controls.ListBox list)
                    {
                        list.MouseDoubleClick += List_DoubleClick;
                        list.PreviewMouseDoubleClick -= List_DoubleClick;
                    }
                }
            }
            """);

        Assert.Contains("list.DoubleTapped += List_DoubleClick", r.Code);
        Assert.Contains("list.DoubleTapped -= List_DoubleClick", r.Code);
        Assert.DoesNotContain("MouseDoubleClick", r.Code);
    }

    [Fact]
    public void DoubleTapHandler_ParamBecomes_TappedEventArgs()
    {
        var r = Transform("""
            using System.Windows.Input;

            namespace Demo
            {
                class C
                {
                    void List_DoubleClick(object sender, MouseButtonEventArgs e)
                    {
                        var src = e.OriginalSource;
                    }

                    void OnDown(object sender, MouseButtonEventArgs e)
                    {
                        var src = e.OriginalSource;
                    }
                }
            }
            """);

        // 双击处理器（方法名含 DoubleClick）：参数 → TappedEventArgs
        Assert.Contains("List_DoubleClick(object sender, global::Avalonia.Input.TappedEventArgs e)", r.Code);
        // 普通鼠标按下处理器：参数 → PointerPressedEventArgs（默认映射不受影响）
        Assert.Contains("OnDown(object sender, global::Avalonia.Input.PointerPressedEventArgs e)", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-DOUBLETAP-ARGS");
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

    [Fact]
    public void VisibilityAssignment_LhsBecomes_IsVisible_NotBool()
    {
        var r = Transform("""
            using System.Windows;
            using System.Windows.Controls;

            namespace Demo
            {
                class C : Button
                {
                    void M()
                    {
                        Visibility = Visibility.Collapsed;
                    }
                }
            }
            """);

        // 左侧是属性名（this.Visibility）→ IsVisible；右侧枚举成员 → false 字面量
        Assert.Contains("IsVisible = false", r.Code);
        Assert.DoesNotContain("bool = false", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-VISIBILITY-PROP");
    }

    [Fact]
    public void VisibilityObjectInitializer_LhsBecomes_IsVisible()
    {
        var r = Transform("""
            using System.Windows;
            using System.Windows.Controls;

            namespace Demo
            {
                class C
                {
                    void M()
                    {
                        var b = new TextBlock { Text = "x", Visibility = Visibility.Collapsed };
                    }
                }
            }
            """);

        // 对象初始化器里的 A = v 是 SimpleAssignmentExpression（非 NameEquals）：
        // 左侧须映射属性名 IsVisible，按类型映射 bool 会产出 `bool = false` 语法错误
        Assert.Contains("IsVisible = false", r.Code);
        Assert.DoesNotContain("bool = false", r.Code);
    }

    [Fact]
    public void VisibilityTypePosition_StillBecomes_Bool()
    {
        var r = Transform("""
            using System.Windows;

            namespace Demo
            {
                class C
                {
                    void M()
                    {
                        Visibility v = Visibility.Visible;
                        var w = (Visibility)1;
                        var t = typeof(Visibility);
                    }
                }
            }
            """);

        // 类型位置（局部声明 / cast / typeof）→ bool 映射保持不变
        Assert.Contains("bool v = true", r.Code);
        Assert.Contains("(bool)1", r.Code);
        Assert.Contains("typeof(bool)", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-VISIBILITY-TYPE");
    }

    [Fact]
    public void BareVisibilityComparison_Becomes_IsVisible()
    {
        var r = Transform("""
            using System.Windows;
            using System.Windows.Controls;

            namespace Demo
            {
                class C : UserControl
                {
                    bool M() => Visibility == Visibility.Visible;
                }
            }
            """);

        // 裸标识符比较（this.Visibility == …）→ IsVisible == true
        Assert.Contains("IsVisible == true", r.Code);
        Assert.DoesNotContain("bool == true", r.Code);
    }

    // ---------------------------------------------- 虚方法覆盖重写（CS-OVERRIDE-*）

    [Fact]
    public void OnMouseDownOverride_RenamedTo_OnPointerPressed_WithBaseSync()
    {
        var r = Transform("""
            using System.Windows;
            using System.Windows.Controls;
            using System.Windows.Input;

            namespace Demo
            {
                class C : ListBox
                {
                    protected override void OnMouseDown(MouseButtonEventArgs e)
                    {
                        base.OnMouseDown(e);
                        var pos = e.GetPosition(this);
                    }
                }
            }
            """);

        // 方法名 + 参数类型（映射表强制覆盖）
        Assert.Contains("protected override void OnPointerPressed(global::Avalonia.Input.PointerPressedEventArgs e)", r.Code);
        // 方法体内 base 调用同步重命名（否则 CS0115）
        Assert.Contains("base.OnPointerPressed(e)", r.Code);
        Assert.DoesNotContain("OnMouseDown", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-OVERRIDE-RENAME");
    }

    [Fact]
    public void OnRenderOverride_Becomes_PublicRender()
    {
        var r = Transform("""
            using System.Windows.Controls;
            using System.Windows.Media;

            namespace Demo
            {
                class C : Control
                {
                    protected override void OnRender(DrawingContext dc)
                    {
                        base.OnRender(dc);
                    }
                }
            }
            """);

        // WPF protected OnRender → Avalonia public Render（CS0507：protected 覆盖 public 报错）
        Assert.Contains("public override void Render(DrawingContext dc)", r.Code);
        Assert.Contains("base.Render(dc)", r.Code);
        Assert.DoesNotContain("OnRender", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-ONRENDER-BODY");
    }

    [Fact]
    public void OnApplyTemplate_GetsParam_AndProtected_AndBaseArg()
    {
        var r = Transform("""
            using System.Windows.Controls;

            namespace Demo
            {
                class C : TextBox
                {
                    public override void OnApplyTemplate()
                    {
                        base.OnApplyTemplate();
                        var part = this.GetTemplateChild("PART_X");
                    }
                }
            }
            """);

        // WPF 无参 public → Avalonia 12 带参 protected（CS0115：无参覆盖报错）
        Assert.Contains("protected override void OnApplyTemplate(global::Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)", r.Code);
        // base 调用同步补 (e)
        Assert.Contains("base.OnApplyTemplate(e)", r.Code);
        Assert.DoesNotContain("base.OnApplyTemplate()", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-APPLYTEMPLATE");
    }

    [Fact]
    public void OverrideClash_PreviewPlusBubble_BothKeepOriginalNames()
    {
        var r = Transform("""
            using System.Windows.Controls;
            using System.Windows.Input;

            namespace Demo
            {
                class C : TextBox
                {
                    protected override void OnPreviewKeyDown(KeyEventArgs e) { }
                    protected override void OnKeyDown(KeyEventArgs e) { }
                }
            }
            """);

        // OnPreviewKeyDown+OnKeyDown 均得 OnKeyDown → CS0111：保留原名 + 人工合并提示
        Assert.Contains("OnPreviewKeyDown(KeyEventArgs e)", r.Code);
        Assert.Contains("OnKeyDown(KeyEventArgs e)", r.Code);
        Assert.DoesNotContain("CS-OVERRIDE-RENAME", string.Join(",", r.Notes.Select(n => n.Rule)));
        Assert.Contains(r.Notes, n => n.Rule == "CS-OVERRIDE-CLASH");
    }

    [Fact]
    public void NonOverrideMethod_WithVirtualName_IsNotRenamed()
    {
        var r = Transform("""
            using System.Windows.Input;

            namespace Demo
            {
                class C
                {
                    private void OnMouseDown(object sender, MouseButtonEventArgs e) { }
                }
            }
            """);

        // 无 override 修饰的普通方法（事件处理器）不参与虚方法重命名
        Assert.Contains("void OnMouseDown(", r.Code);
        Assert.DoesNotContain("OnPointerPressed", r.Code);
    }

    [Fact]
    public void ManualNoteOverride_WithoutAvaloniaVirtual_StaysUntouched()
    {
        var r = Transform("""
            using System.Windows;

            namespace Demo
            {
                class C : Window
                {
                    protected override void OnActivated(EventArgs e) { }
                }
            }
            """);

        // Window 激活无虚方法：不改签名，仅人工提示
        Assert.Contains("OnActivated(EventArgs e)", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-OVERRIDE-MANUAL");
    }

    [Fact]
    public void OnClosingOverride_ParamForcedTo_WindowClosingEventArgs()
    {
        var r = Transform("""
            using System.Windows;

            namespace Demo
            {
                class C : Window
                {
                    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
                    {
                        base.OnClosing(e);
                        e.Cancel = true;
                    }
                }
            }
            """);

        // 方法名不变，参数类型强制覆盖（e.Cancel 在 WindowClosingEventArgs 上存在）
        Assert.Contains("protected override void OnClosing(global::Avalonia.Controls.WindowClosingEventArgs e)", r.Code);
        Assert.Contains("e.Cancel = true", r.Code);
    }

    // ---------------------------------------------- WPF 独有命名空间（CS-WPFONLY-*）

    [Fact]
    public void WpfOnlyUsing_IsRemoved()
    {
        var r = Transform("""
            using System.Windows;
            using System.Windows.Interop;
            using System.Windows.Navigation;

            namespace Demo
            {
                class C { }
            }
            """);

        // WPF 独有命名空间 using 整条移除（Avalonia 无等价）
        Assert.DoesNotContain("System.Windows.Interop", r.Code);
        Assert.DoesNotContain("System.Windows.Navigation", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-USING-WPFONLY");
    }

    [Fact]
    public void WpfOnlyQualifiedReference_IsPreserved_WithManualNote()
    {
        var r = Transform("""
            namespace Demo
            {
                class C
                {
                    object M(object w)
                    {
                        var h = new System.Windows.Interop.WindowInteropHelper(w);
                        return h;
                    }
                }
            }
            """);

        // 限定引用保留原样（不能错映射为 global::Avalonia.Interop.*）+ 人工提示
        Assert.Contains("new System.Windows.Interop.WindowInteropHelper(w)", r.Code);
        Assert.DoesNotContain("Avalonia.Interop", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-WPFONLY-REF");
    }

    // ============ ForkPlus 端到端 dry-run 错误驱动增强（CS0012/CS0115/CS0246） ============

    [Fact]
    public void UsingAlias_WindowState_MapsToControlsNamespace()
    {
        var r = Transform("""
            using WindowState = System.Windows.WindowState;

            namespace Demo
            {
                class C { WindowState S => WindowState.Maximized; }
            }
            """);

        // using 别名右侧走精确全名映射（前缀替换会错位到不存在的 global::Avalonia.WindowState）
        Assert.Contains("using WindowState = global::Avalonia.Controls.WindowState", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-QUALIFIED-EXACT");
    }

    [Fact]
    public void MarkupExtension_RenamesToMarkupXaml()
    {
        var r = Transform("""
            using System.Windows.Markup;

            namespace Demo
            {
                class C : MarkupExtension
                {
                    public override object ProvideValue(IServiceProvider sp) => this;
                }
            }
            """);

        // System.Windows.Markup.MarkupExtension → Avalonia.Markup.Xaml 程序集（非 Avalonia.Markup！）
        Assert.Contains("class C : global::Avalonia.Markup.Xaml.MarkupExtension", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-TYPE-RENAME");
    }

    [Fact]
    public void OnInitializedOverride_DropsEventArgsParam_AndBaseArgs()
    {
        var r = Transform("""
            using System.Windows;

            namespace Demo
            {
                class C : Window
                {
                    protected override void OnInitialized(EventArgs e)
                    {
                        base.OnInitialized(e);
                        Init();
                    }
                }
            }
            """);

        // WPF OnInitialized(EventArgs) → StyledElement.OnInitialized() 无参（反射验证）
        Assert.Contains("protected override void OnInitialized()", r.Code);
        Assert.Contains("base.OnInitialized()", r.Code);
        Assert.DoesNotContain("base.OnInitialized(e)", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-OVERRIDE-PARAMTRIM");
        Assert.Contains(r.Notes, n => n.Rule == "CS-BASE-PARAMTRIM");
    }

    [Fact]
    public void PrepareContainerForItemOverride_GainsIndexParam()
    {
        var r = Transform("""
            using System.Windows.Controls;

            namespace Demo
            {
                class C : ListBox
                {
                    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
                    {
                        base.PrepareContainerForItemOverride(element, item);
                    }
                }
            }
            """);

        // WPF (DependencyObject, object) → Avalonia 12 (Control, object, int index)
        Assert.Contains("PrepareContainerForItemOverride(global::Avalonia.Controls.Control element, object item, int index)", r.Code);
        Assert.Contains("base.PrepareContainerForItemOverride(element, item, index)", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-OVERRIDE-PARAMAPPEND");
    }

    [Fact]
    public void ClearContainerForItemOverride_LosesSecondParam()
    {
        var r = Transform("""
            using System.Windows.Controls;

            namespace Demo
            {
                class C : ListBox
                {
                    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
                    {
                        base.ClearContainerForItemOverride(element, item);
                    }
                }
            }
            """);

        // WPF (DependencyObject, object) → Avalonia 12 (Control) 单参
        Assert.Contains("ClearContainerForItemOverride(global::Avalonia.Controls.Control element)", r.Code);
        Assert.Contains("base.ClearContainerForItemOverride(element)", r.Code);
        Assert.DoesNotContain("base.ClearContainerForItemOverride(element, item)", r.Code);
    }

    [Fact]
    public void ParamListSeparatorTrivia_PreservedAfterTrimAndAppend()
    {
        // 回归：重建 SeparatedList 曾丢失逗号后空格（element,object / item,intindex）
        var trim = Transform("""
            using System.Windows.Controls;

            namespace Demo
            {
                class C : ListBox
                {
                    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
                    {
                        base.ClearContainerForItemOverride(element, item);
                    }
                }
            }
            """);
        Assert.DoesNotContain("element,item", trim.Code);

        var append = Transform("""
            using System.Windows.Controls;

            namespace Demo
            {
                class C : ListBox
                {
                    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
                    {
                        base.PrepareContainerForItemOverride(element, item);
                    }
                }
            }
            """);
        Assert.DoesNotContain("element,object", append.Code);
        Assert.DoesNotContain("item,intindex", append.Code);
        Assert.DoesNotContain(",item, index", append.Code);
    }

    [Fact]
    public void PasswordBoxMembers_RenamedForPasswordBoxReceivers()
    {
        var r = Transform("""
            using System.Windows;
            using System.Windows.Controls;

            namespace Demo
            {
                class C : Window
                {
                    private PasswordBox InputPasswordBox;
                    private ViewModel Vm;

                    string Read() => InputPasswordBox.Password;
                    string VmPassword() => Vm.Password; // VM 属性不改
                    void Wire()
                    {
                        // 事件名 PasswordChanged 全局改 TextChanged（WPF 独有名，含 VM 同名事件）
                        InputPasswordBox.PasswordChanged += OnPw;
                    }
                    void OnPw(object s, RoutedEventArgs e) { }
                }
            }
            """);

        // 接收者名含 PasswordBox → .Password/.PasswordChanged 改名
        Assert.Contains("InputPasswordBox.Text", r.Code);
        Assert.DoesNotContain("InputPasswordBox.Password", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-PASSWORDBOX");
        // 类型名 PasswordBox → TextBox
        Assert.Contains("global::Avalonia.Controls.TextBox InputPasswordBox", r.Code);
        // VM 属性 .Password：接收者不含 PasswordBox，保留原样
        Assert.Contains("Vm.Password", r.Code);
    }

    [Fact]
    public void PasswordChangedEvent_RenamedToTextChanged()
    {
        var r = Transform("""
            using System.Windows.Controls;

            namespace Demo
            {
                class C
                {
                    void Wire(PasswordBox box)
                    {
                        box.PasswordChanged += OnPw;
                    }
                    void OnPw(object s, RoutedEventArgs e) { }
                }
            }
            """);

        // WPF 独有事件名全局改名（误伤面窄）
        Assert.Contains("box.TextChanged += OnPw", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-EVENT-RENAME");
    }

    [Fact]
    public void OnPreviewMouseRightButtonDown_MapsToPointerPressed()
    {
        var r = Transform("""
            using System.Windows.Controls;
            using System.Windows.Input;

            namespace Demo
            {
                class C : TreeView
                {
                    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
                    {
                        base.OnPreviewMouseRightButtonDown(e);
                    }
                }
            }
            """);

        Assert.Contains("OnPointerPressed(global::Avalonia.Input.PointerPressedEventArgs e)", r.Code);
        Assert.Contains("base.OnPointerPressed(e)", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-OVERRIDE-RENAME");
    }

    [Fact]
    public void OnCheckedOverride_KeepsSignature_WithManualNote()
    {
        var r = Transform("""
            using System.Windows.Controls.Primitives;

            namespace Demo
            {
                class C : ToggleButton
                {
                    protected override void OnChecked(RoutedEventArgs e) { }
                    protected override void OnContentChanged(object oldContent, object newContent) { }
                }
            }
            """);

        // ToggleButton 无 OnChecked/OnChecked 虚方法、ContentControl 无 OnContentChanged：保留签名 + 人工提示
        Assert.Contains("OnChecked(RoutedEventArgs e)", r.Code);
        Assert.Contains("OnContentChanged(object oldContent, object newContent)", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-OVERRIDE-MANUAL" && n.Message.Contains("OnIsCheckedChanged"));
        Assert.Contains(r.Notes, n => n.Rule == "CS-OVERRIDE-MANUAL" && n.Message.Contains("OnContentChanged"));
    }

    [Fact]
    public void IMultiValueConverter_ObjectArrayParam_BecomesIList()
    {
        var r = Transform("""
            using System;
            using System.Globalization;
            using System.Windows.Data;

            namespace Demo
            {
                class C : IMultiValueConverter
                {
                    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => null;
                }
            }
            """);

        // 反射验证 Avalonia 12：IMultiValueConverter.Convert(IList<object>, Type, object, CultureInfo)
        Assert.Contains("global::System.Collections.Generic.IList<object> values", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-IMVC-ARGS");
    }

    [Fact]
    public void AvalonEditNamespaces_RewrittenToAvaloniaEdit()
    {
        var r = Transform("""
            using ICSharpCode.AvalonEdit.Editing;
            using ICSharpCode.AvalonEdit.Rendering;

            namespace Demo
            {
                class C
                {
                    ICSharpCode.AvalonEdit.Document.TextDocument Doc() => null;
                }
            }
            """);

        // Avalonia.AvaloniaEdit 12.0.0 命名空间重排：ICSharpCode.AvalonEdit.* → AvaloniaEdit.*
        Assert.Contains("using AvaloniaEdit.Editing", r.Code);
        Assert.Contains("using AvaloniaEdit.Rendering", r.Code);
        Assert.Contains("AvaloniaEdit.Document.TextDocument", r.Code);
        Assert.DoesNotContain("ICSharpCode", r.Code);
    }

    [Fact]
    public void ThemeInfoAttribute_IsRemoved()
    {
        var r = Transform("""
            using System.Windows;

            [assembly: ThemeInfo(ResourceDictionaryLocation.None, ResourceDictionaryLocation.SourceAssembly)]

            namespace Demo { }
            """);

        Assert.DoesNotContain("ThemeInfo", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-THEMEINFO-REMOVED");
    }

    [Fact]
    public void ValueConversionAttribute_IsRemoved()
    {
        // ForkPlus InverseBooleanConverter 等实测：CS0246（Avalonia 12 无此特性）
        var r = Transform("""
            using System;
            using System.Globalization;
            using System.Windows.Data;

            namespace Demo
            {
                [ValueConversion(typeof(bool), typeof(bool))]
                public class InverseBooleanConverter : IValueConverter
                {
                    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(bool)value;
                }
            }
            """);

        Assert.DoesNotContain("ValueConversion", r.Code);
        Assert.Contains("IValueConverter", r.Code); // 类与实现保留
        Assert.Contains(r.Notes, n => n.Rule == "CS-ATTRIBUTE-REMOVED");
    }

    [Fact]
    public void ContentPropertyAttribute_IsRemoved_NonWpfSameName_Kept()
    {
        // 删除：类级 [ContentProperty("Content")]（ForkPlus CustomWindow.cs 实测 CS0246）
        var r = Transform("""
            using System.Windows;

            namespace Demo
            {
                [ContentProperty("Content")]
                public class CustomWindow : Window
                {
                    public object Content { get; set; }
                }
            }
            """);

        Assert.DoesNotContain("ContentProperty", r.Code);
        Assert.Contains("class CustomWindow", r.Code); // 类型本体保留
        Assert.Contains(r.Notes, n => n.Rule == "CS-ATTRIBUTE-REMOVED");

        // 保留：非 WPF 前缀的限定同名特性（用户自有类型不受误删）
        var r2 = Transform("""
            namespace Demo
            {
                [MyLib.ContentProperty("X")]
                public class C2 { }
            }
            """);

        Assert.Contains("MyLib.ContentProperty", r2.Code);
    }

    [Fact]
    public void AssemblyAssociatedContentFileAttribute_IsRemoved()
    {
        // ForkPlus AssemblyInfo.cs 实测 CS0246（WPF 松散文件关联）
        var r = Transform("""
            using System.Reflection;

            [assembly: AssemblyAssociatedContentFile("webview2loader.dll")]

            namespace Demo { }
            """);

        Assert.DoesNotContain("AssemblyAssociatedContentFile", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-ATTRIBUTE-REMOVED");
    }

    [Fact]
    public void WpfOnlyType_GetsManualNote_WithAlternative()
    {
        var r = Transform("""
            namespace Demo
            {
                class C
                {
                    object M() => new Adorner(null);
                }
            }
            """);

        // WPF 独有类型：保留原名 + 人工提示（替代方案指引）
        Assert.Contains("new Adorner(null)", r.Code);
        Assert.Contains(r.Notes, n => n.Rule == "CS-WPFONLY-TYPE" && n.Message.Contains("Overlay"));
    }
}
