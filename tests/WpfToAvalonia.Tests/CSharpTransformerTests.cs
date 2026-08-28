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
}
