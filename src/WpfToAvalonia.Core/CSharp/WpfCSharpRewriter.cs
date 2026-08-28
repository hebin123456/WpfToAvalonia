using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using WpfToAvalonia.Core.Model;
using WpfToAvalonia.Core.Xaml;

namespace WpfToAvalonia.Core.CSharp;

/// <summary>
/// 基于 Roslyn 语法树（AST）的 CSharpSyntaxRewriter：
/// using / 全限定类型前缀 / 类型名 / 成员访问 API 映射 / 事件重命名 /
/// DependencyProperty 注册重写 / DialogResult→Close 等确定性改写，
/// 无法安全自动转换的 API 标记为 TODO 并给出建议。
/// </summary>
internal sealed class WpfCSharpRewriter : CSharpSyntaxRewriter
{
    private readonly string _file;
    private readonly List<ConversionNote> _notes = new();
    private readonly HashSet<string> _dedupe = new();
    private readonly Stack<string> _methodNames = new();
    private int _resourceVarCounter;

    public bool WpfDetected { get; private set; }
    public IReadOnlyList<ConversionNote> Notes => _notes;

    /// <summary>全限定前缀映射（长前缀优先）。</summary>
    private static readonly (string Prefix, string Replacement)[] QualifiedPrefixes =
    {
        ("System.Windows.Media.Imaging", "global::Avalonia.Media.Imaging"),
        // WPF 动画命名空间 → Avalonia.Animation（不是 Avalonia.Media.Animation！）
        ("System.Windows.Media.Animation", "global::Avalonia.Animation"),
        ("System.Windows.Controls.Primitives", "global::Avalonia.Controls.Primitives"),
        ("System.Windows.Controls.Shapes", "global::Avalonia.Controls.Shapes"),
        ("System.Windows.Controls", "global::Avalonia.Controls"),
        ("System.Windows.Threading", "global::Avalonia.Threading"),
        ("System.Windows.Shapes", "global::Avalonia.Controls.Shapes"),
        ("System.Windows.Input", "global::Avalonia.Input"),
        // Avalonia 12：Inline/Run 在 Avalonia.Controls.Documents
        ("System.Windows.Documents", "global::Avalonia.Controls.Documents"),
        ("System.Windows.Data", "global::Avalonia.Data"),
        ("System.Windows.Markup", "global::Avalonia.Markup"),
        ("System.Windows.Media", "global::Avalonia.Media"),
        ("System.Windows", "global::Avalonia"),
    };

    private static readonly string[] ManualNoteOncePatterns =
    {
        "MessageBox.Show", "new OpenFileDialog", "new SaveFileDialog", "new OpenFolderDialog",
        "Mouse.GetPosition", "Mouse.OverrideCursor", "Keyboard.Modifiers", "Clipboard.SetText",
        "Clipboard.GetText", "Clipboard.SetImage", "Clipboard.GetImage",
        "VisualTreeHelper.GetChild", "VisualTreeHelper.GetChildrenCount",
        "LogicalTreeHelper.FindLogicalNode", "DependencyPropertyDescriptor.FromProperty",
        "CompositionTarget.Rendering", "EventManager.RegisterRoutedEvent",
        "FocusManager.GetFocusedElement", "Application.Current.Windows",
        "new System.Windows.MessageBox", "Toolbar",
    };

    /// <summary>后缀模式（成员调用 receiver.Xxx）：Xxx 部分需人工处理。</summary>
    private static readonly (string Suffix, string Rule, string Message)[] ManualNoteSuffixPatterns =
    {
        (".SetResourceReference", "CS-SETRESOURCE-REF",
            "SetResourceReference（动态资源重定向）无 Avalonia 等价；请改用 {DynamicResource} XAML 绑定或代码里重建绑定。"),
        (".CaptureMouse", "CS-CAPTURE",
            "CaptureMouse/ReleaseMouseCapture → control.Pointer.Capture(pointer)/ReleasePointerCapture（基于 Pointer 实例）。"),
        (".DoDragDrop", "CS-DRAGDROP",
            "DoDragDrop → TopLevel.Clipboard 或 DragDrop.DoDragDrop（Avalonia.Input.DragDrop，基于 DataObject 与 pointer）。"),
        (".PrintVisual", "CS-PRINT", "WPF 打印体系无 Avalonia 等价，需平台特定实现。"),
        (".ShowDialog", "CS-SHOWDIALOG2",
            "ShowDialog() 无参形式：Avalonia 需要 owner 参数 await dialog.ShowDialog(owner)。"),
    };

    /// <summary>前缀模式（泛型/静态类调用）。</summary>
    private static readonly (string Prefix, string Rule, string Message)[] ManualNotePrefixPatterns =
    {
        ("WeakEventManager<", "CS-WEAKEVENT",
            "WeakEventManager<TS,TA>.AddHandler(s, \"Event\", h) → Avalonia WeakEvent 系统：事件宿主用 static WeakEvent<TS,TA> 字段 + AddHandler/RemoveHandler；字符串事件名无法保留。"),
        ("VisualTreeHelper.", "CS-VISUALTREE2",
            "VisualTreeHelper.* → VisualTree 扩展（GetVisualChildren/GetVisualParent/FindDescendant 等，Avalonia 命名空间）。"),
    };

    public WpfCSharpRewriter(string file) => _file = file;

    // ------------------------------------------------------------------ using

    private bool _sawSystemWindows;

    public override SyntaxNode? VisitCompilationUnit(CompilationUnitSyntax node)
    {
        var visited = (CompilationUnitSyntax?)base.VisitCompilationUnit(node);
        if (visited == null || !_sawSystemWindows || KnownMaps.SystemWindowsExtraUsings.Count == 0)
            return visited;

        // using System.Windows → using Avalonia 只覆盖基础类型；
        // Window/Style/Layoutable 等高频类型分属其它命名空间，统一补齐（去重）
        var existing = visited.Usings
            .Select(u => u.Name?.ToString().Replace("global::", ""))
            .Where(n => n != null)
            .ToHashSet(StringComparer.Ordinal);

        var additions = KnownMaps.SystemWindowsExtraUsings
            .Where(ns => !existing.Contains(ns))
            .Select(ns => SyntaxFactory.UsingDirective(
                SyntaxFactory.ParseName(ns).WithLeadingTrivia(SyntaxFactory.Space))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed))
            .ToList();
        if (additions.Count == 0) return visited;

        Note(visited, NoteSeverity.Info, "CS-USING-EXTRA",
            "已补充 " + string.Join("/", KnownMaps.SystemWindowsExtraUsings) +
            "（WPF System.Windows 大杂烩命名空间中的 Window/Style/Layoutable 等分属这些 Avalonia 命名空间）。");
        return visited.WithUsings(visited.Usings.AddRange(additions));
    }

    public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
    {
        var name = node.Name?.ToString().Trim() ?? "";
        if (name == "System.Windows") _sawSystemWindows = true;
        if (name.Length > 0 && KnownMaps.CSharpNamespaces.TryGetValue(name, out var mapped))
        {
            WpfDetected = true;
            Note(node, NoteSeverity.Info, "CS-USING", $"using {name} → using {mapped}");
            var newName = SyntaxFactory.ParseName(mapped).WithTriviaFrom(node.Name!);
            return node.WithName(newName);
        }
        return base.VisitUsingDirective(node);
    }

    // ------------------------------------------------- 全限定类型（声明上下文）

    public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
    {
        var rewritten = RewriteQualified(node, out var changed);
        if (changed) return rewritten!.WithTriviaFrom(node);
        return base.VisitQualifiedName(node);
    }

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var text = node.ToString();

        // —— Visibility 枚举已从 Avalonia 12 移除（只剩 Visual.IsVisible bool）——
        // 必须在限定前缀重写之前处理，否则 System.Windows.Visibility.Collapsed 会先被
        // 前缀逻辑替换成不存在的 global::Avalonia.Visibility.Collapsed
        if (text is "Visibility.Collapsed" or "Visibility.Hidden" or
            "System.Windows.Visibility.Collapsed" or "System.Windows.Visibility.Hidden")
        {
            WpfDetected = true;
            Note(node, NoteSeverity.Warning, "CS-VISIBILITY",
                "Visibility.Collapsed/Hidden → false（Avalonia 12 移除 Visibility 枚举，IsVisible 布尔；Hidden 的占位语义丢失）。");
            return SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression).WithTriviaFrom(node);
        }
        if (text is "Visibility.Visible" or "System.Windows.Visibility.Visible")
        {
            WpfDetected = true;
            Note(node, NoteSeverity.Info, "CS-VISIBILITY", "Visibility.Visible → true（Avalonia 12：IsVisible 布尔）。");
            return SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression).WithTriviaFrom(node);
        }

        // 全限定前缀重写（System.Windows.Media.Brushes.Red 等）
        foreach (var (prefix, replacement) in QualifiedPrefixes)
        {
            if (text.StartsWith(prefix + ".", StringComparison.Ordinal))
            {
                WpfDetected = true;
                var replaced = replacement + text[prefix.Length..];
                Note(node, NoteSeverity.Info, "CS-QUALIFIED", $"{prefix}.* → {replacement}.*");
                return SyntaxFactory.ParseExpression(replaced).WithTriviaFrom(node);
            }
        }

        // 精确成员模式
        switch (text)
        {
            case "Application.Current.Dispatcher":
            case "Application.Current?.Dispatcher":
            case "global::Avalonia.Application.Current.Dispatcher":
            case "global::Avalonia.Application.Current?.Dispatcher":
                WpfDetected = true;
                Note(node, NoteSeverity.Info, "CS-DISPATCHER", "Application.Current.Dispatcher → Avalonia.Threading.Dispatcher.UIThread");
                return Expr(node, "global::Avalonia.Threading.Dispatcher.UIThread");

            case "Application.Current.MainWindow":
            case "Application.Current?.MainWindow":
            case "global::Avalonia.Application.Current.MainWindow":
            case "global::Avalonia.Application.Current?.MainWindow":
                WpfDetected = true;
                Note(node, NoteSeverity.Info, "CS-MAINWINDOW",
                    "Application.Current.MainWindow → (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow");
                return Expr(node,
                    "(global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow");

            case "Application.Current.Windows":
            case "global::Avalonia.Application.Current.Windows":
                WpfDetected = true;
                Note(node, NoteSeverity.Info, "CS-WINDOWS-COLLECTION",
                    "Application.Current.Windows → lifetime.Windows（IClassicDesktopStyleApplicationLifetime.Windows 集合）");
                return Expr(node,
                    "(global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Windows");

            case "Dispatcher.CurrentDispatcher":
                WpfDetected = true;
                Note(node, NoteSeverity.Info, "CS-DISPATCHER", "Dispatcher.CurrentDispatcher → Dispatcher.UIThread");
                return Expr(node, "global::Avalonia.Threading.Dispatcher.UIThread");

            case "DependencyProperty.UnsetValue":
                WpfDetected = true;
                Note(node, NoteSeverity.Info, "CS-UNSETVALUE", "DependencyProperty.UnsetValue → AvaloniaProperty.UnsetValue");
                return Expr(node, "global::Avalonia.AvaloniaProperty.UnsetValue");
        }

        // 事件重命名（+= / -= 右侧）
        if (node.Parent is AssignmentExpressionSyntax { RawKind: not (int)SyntaxKind.SimpleAssignmentExpression } &&
            KnownMaps.CSharpEventRenames.TryGetValue(node.Name.Identifier.ValueText, out var ev))
        {
            WpfDetected = true;
            Note(node, NoteSeverity.Info, "CS-EVENT-RENAME", $"事件 {node.Name.Identifier.ValueText} → {ev}");
            // 保留原标识符 trivia，避免丢失 "+=" 前的空格
            var newName = SyntaxFactory.IdentifierName(ev).WithTriviaFrom(node.Name);
            return node.WithName(newName);
        }

        // x.Visibility 成员访问 → x.IsVisible（bool；枚举字面量已在上方统一转 true/false）
        if (node.Name.Identifier.ValueText == "Visibility")
        {
            WpfDetected = true;
            Note(node, NoteSeverity.Warning, "CS-VISIBILITY",
                "xxx.Visibility → xxx.IsVisible（bool）。比较/赋值右侧的 Visibility.* 已转布尔字面量。");
            return node.WithName(SyntaxFactory.IdentifierName("IsVisible").WithTriviaFrom(node.Name));
        }

        ManualNote(node, text);
        return base.VisitMemberAccessExpression(node);
    }

    // ------------------------------------------------------------------ 类型名

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        // 排除成员访问右侧（x.MouseDown）与限定名右侧（已由前缀逻辑处理）
        if (node.Parent is MemberAccessExpressionSyntax mae && ReferenceEquals(mae.Name, node))
            return base.VisitIdentifierName(node);
        if (node.Parent is QualifiedNameSyntax q && ReferenceEquals(q.Right, node))
            return base.VisitIdentifierName(node);
        if (node.Parent is NameColonSyntax or NameEqualsSyntax)
            return base.VisitIdentifierName(node);
        // x?.Member（MemberBinding.Name 位置必须是 SimpleNameSyntax）：
        // 成员名与类型名同名时（如 lhs?.ImageSource）不能替换为限定名，否则 InvalidCastException
        if (node.Parent is MemberBindingExpressionSyntax mbe && ReferenceEquals(mbe.Name, node))
            return base.VisitIdentifierName(node);
        // global::X（AliasQualifiedName.Name 同样要求 SimpleNameSyntax）
        if (node.Parent is AliasQualifiedNameSyntax aqn && ReferenceEquals(aqn.Name, node))
            return base.VisitIdentifierName(node);

        var name = node.Identifier.ValueText;

        // 双击处理器（MouseDoubleClick → DoubleTapped）的参数类型特判：
        // Avalonia DoubleTapped 是 EventHandler<TappedEventArgs>，参数须为 TappedEventArgs
        // 而非默认映射的 PointerPressedEventArgs（方法名含 DoubleClick 判据）
        if (name == "MouseButtonEventArgs" && IsInDoubleTapHandler())
        {
            WpfDetected = true;
            Note(node, NoteSeverity.Info, "CS-DOUBLETAP-ARGS",
                "双击处理器参数 MouseButtonEventArgs → TappedEventArgs（DoubleTapped 事件签名，已随 MouseDoubleClick 重命名）。");
            return SyntaxFactory.ParseName("global::Avalonia.Input.TappedEventArgs").WithTriviaFrom(node);
        }

        // —— Visibility 表达式位置 = 属性引用（this.Visibility → IsVisible）——
        // 对象初始化器/简单赋值 `Visibility = v` 在 Roslyn 中是 SimpleAssignmentExpression
        // （不是 NameEquals！NameEquals 仅用于 attribute/using 别名/属性模式），
        // 左侧标识符是属性名 → IsVisible；裸标识符比较/传参/nameof 同理。
        // 若按类型映射成 bool 会产出 `bool = false` 语法错误（CS1525）。
        // 仅类型位置（声明/cast/typeof/泛型实参）才走下方 TypeRenames → bool。
        if (name == "Visibility" && !IsTypePosition(node))
        {
            WpfDetected = true;
            Note(node, NoteSeverity.Info, "CS-VISIBILITY-PROP",
                "属性引用 Visibility → IsVisible（WPF Visibility 属性 → Avalonia Visual.IsVisible bool；"
                + "右侧枚举值 Visibility.* 已由成员访问规则转 true/false）。");
            return SyntaxFactory.IdentifierName("IsVisible").WithTriviaFrom(node);
        }

        if (KnownMaps.TypeRenames.TryGetValue(name, out var mapped))
        {
            WpfDetected = true;
            // 关键字类型（Visibility → bool）不能经 ParseName（非标识符）
            if (mapped == "bool")
            {
                Note(node, NoteSeverity.Warning, "CS-VISIBILITY-TYPE",
                    "Visibility 类型 → bool（Avalonia 12 移除枚举；值域 0/1/2 合并为 true/false）。");
                return SyntaxFactory.PredefinedType(
                    SyntaxFactory.Token(SyntaxKind.BoolKeyword)).WithTriviaFrom(node);
            }
            var severity = name is "VisualBrush" or "BitmapSource" or "FrameworkElement"
                ? NoteSeverity.Warning : NoteSeverity.Info;
            Note(node, severity, "CS-TYPE-RENAME", $"类型 {name} → {mapped}");
            return SyntaxFactory.ParseName(mapped).WithTriviaFrom(node);
        }
        return base.VisitIdentifierName(node);
    }

    // ------------------------------------------------------------------ 语句删除

    /// <summary>
    /// 无参 Freeze()/BeginInit()/EndInit() 语句整句删除：
    /// Avalonia 无冻结概念（Brush/Bitmap 均为不可变值语义），
    /// 亦无 BitmapImage 的 BeginInit/EndInit 初始化协议。
    /// </summary>
    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (node.Expression is InvocationExpressionSyntax inv &&
            inv.ArgumentList.Arguments.Count == 0 &&
            inv.Expression is MemberAccessExpressionSyntax ma &&
            ma.Name.Identifier.ValueText is "Freeze" or "BeginInit" or "EndInit")
        {
            WpfDetected = true;
            Note(node, NoteSeverity.Info, "CS-FREEZE-REMOVED",
                $"{node.Expression} 语句已删除（Avalonia 无冻结/初始化协议，资源对象不可变）。");

            // 语句列表成员（Block/SwitchSection/顶层）可整句移除；
            // 控制流单语句体（if(x) obj.Freeze();）不可为 null —— Roslyn VisitIfStatement
            // 会 ArgumentNullException(statement)，退化为空块 { }
            if (node.Parent is BlockSyntax or SwitchSectionSyntax or GlobalStatementSyntax)
                return null;
            return SyntaxFactory.Block().WithTriviaFrom(node);
        }
        return base.VisitExpressionStatement(node);
    }

    // ------------------------------------------------------------------ 调用点

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var callee = node.Expression.ToString();

        switch (callee)
        {
            case "Keyboard.Focus":
            {
                WpfDetected = true;
                var arg = node.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString() ?? "this";
                Note(node, NoteSeverity.Info, "CS-KEYBOARD-FOCUS", $"Keyboard.Focus({arg}) → {arg}.Focus()");
                return Expr(node, $"({arg}).Focus()");
            }
            case "Window.GetWindow":
            {
                WpfDetected = true;
                var arg = node.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString() ?? "this";
                Note(node, NoteSeverity.Info, "CS-GETWINDOW",
                    $"Window.GetWindow({arg}) → TopLevel.GetTopLevel({arg})（如需 Window 请加 as Window）");
                return Expr(node, $"global::Avalonia.Controls.TopLevel.GetTopLevel({arg})");
            }
            case "Application.Current.Shutdown":
            {
                WpfDetected = true;
                if (node.ArgumentList.Arguments.Count > 0)
                {
                    Note(node, NoteSeverity.Manual, "CS-SHUTDOWN", "Application.Current.Shutdown(参数) 需人工改写为 lifetime.Shutdown()。");
                    return base.VisitInvocationExpression(node);
                }
                Note(node, NoteSeverity.Info, "CS-SHUTDOWN",
                    "Application.Current.Shutdown() → (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown()");
                return Expr(node,
                    "(global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown()");
            }
            case "VisualTreeHelper.GetParent":
            {
                WpfDetected = true;
                var arg = node.ArgumentList.Arguments.FirstOrDefault()?.Expression.ToString() ?? "this";
                Note(node, NoteSeverity.Info, "CS-VISUALTREE",
                    "VisualTreeHelper.GetParent → VisualTreeExtensions.GetVisualParent（Avalonia 命名空间）");
                return Expr(node, $"global::Avalonia.VisualTreeExtensions.GetVisualParent({arg})");
            }
        }

        // Dispatcher.Yield（WPF 静态）→ Dispatcher.UIThread.Yield（Avalonia 实例方法，Yield(DispatcherPriority) 存在）
        if (node.Expression is MemberAccessExpressionSyntax yma &&
            yma.Name.Identifier.ValueText == "Yield" &&
            ((yma.Expression is IdentifierNameSyntax yid && yid.Identifier.ValueText == "Dispatcher") ||
             yma.Expression.ToString().EndsWith(".Dispatcher", StringComparison.Ordinal)))
        {
            WpfDetected = true;
            var args = node.ArgumentList.Arguments.Count > 0
                ? node.ArgumentList.Arguments.Select(a => a.ToString()).Aggregate((l, r) => $"{l}, {r}")
                : "";
            Note(node, NoteSeverity.Info, "CS-DISPATCHER-YIELD",
                "Dispatcher.Yield(...)（静态）→ Dispatcher.UIThread.Yield(...)（实例方法，返回 DispatcherPriorityAwaitable）。");
            return Expr(node,
                $"global::Avalonia.Threading.Dispatcher.UIThread.Yield({args})");
        }

        // ForkPlus 自定义 Dispatcher 扩展：Dispatcher.Async(action)（= BeginInvoke）→ Post(action)
        if (node.Expression is MemberAccessExpressionSyntax ama &&
            ama.Name.Identifier.ValueText == "Async" &&
            ama.Expression.ToString().Contains("Dispatcher", StringComparison.Ordinal))
        {
            var receiver = ama.Expression.ToString();
            // Application.Current?.Dispatcher 在 Avalonia Application 上不存在 → Dispatcher.UIThread
            if (receiver.Contains("Application.Current", StringComparison.Ordinal))
            {
                WpfDetected = true;
                var args = node.ArgumentList.Arguments.Count > 0
                    ? node.ArgumentList.Arguments.Select(a => a.ToString()).Aggregate((l, r) => $"{l}, {r}")
                    : "";
                Note(node, NoteSeverity.Info, "CS-DISPATCHER-ASYNC",
                    "Application.Current?.Dispatcher.Async(action) → Dispatcher.UIThread.Post(action)（Avalonia Application 无 Dispatcher 属性，统一走 UI 线程调度器）。");
                return Expr(node, $"global::Avalonia.Threading.Dispatcher.UIThread.Post({args})");
            }
            WpfDetected = true;
            Note(node, NoteSeverity.Info, "CS-DISPATCHER-ASYNC",
                $"{receiver}.Async(action)（BeginInvoke 封装）→ {receiver}.Post(action)（fire-and-forget，返回 void）。");
            return node.WithExpression(
                ama.WithName(SyntaxFactory.IdentifierName("Post").WithTriviaFrom(ama.Name)));
        }

        // Dispatcher.BeginInvoke → Post
        if (node.Expression is MemberAccessExpressionSyntax ma &&
            ma.Name.Identifier.ValueText is "BeginInvoke" or "InvokeShutdown" &&
            ma.Expression.ToString().Contains("Dispatcher", StringComparison.Ordinal))
        {
            if (ma.Name.Identifier.ValueText == "BeginInvoke")
            {
                WpfDetected = true;
                Note(node, NoteSeverity.Warning, "CS-DISPATCHER-BEGININVOKE",
                    "Dispatcher.BeginInvoke → Post；Avalonia 无 DispatcherPriority 首参重载，参数需为 Action。");
                return node.WithExpression(
                    ma.WithName(SyntaxFactory.IdentifierName("Post")).WithTriviaFrom(ma));
            }
        }

        // 全限定调用重写
        foreach (var (prefix, replacement) in QualifiedPrefixes)
        {
            if (callee.StartsWith(prefix + ".", StringComparison.Ordinal))
            {
                WpfDetected = true;
                var replaced = replacement + callee[prefix.Length..];
                Note(node, NoteSeverity.Info, "CS-QUALIFIED", $"{prefix}.* → {replacement}.*");
                return Expr(node, replaced + node.ArgumentList);
            }
        }

        ManualNote(node, callee);
        return base.VisitInvocationExpression(node);
    }

    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        var type = node.Type.ToString();

        if (type.EndsWith("BitmapImage", StringComparison.Ordinal))
        {
            WpfDetected = true;
            if (node.ArgumentList?.Arguments.Count == 1)
            {
                var arg = node.ArgumentList.Arguments[0].Expression;
                Note(node, NoteSeverity.Info, "CS-BITMAPIMAGE",
                    "new BitmapImage(uri) → new Bitmap(AssetLoader.Open(uri))");
                return Expr(node,
                    $"new global::Avalonia.Media.Imaging.Bitmap(global::Avalonia.Platform.AssetLoader.Open({arg}))");
            }
            Note(node, NoteSeverity.Manual, "CS-BITMAPIMAGE",
                "new BitmapImage()（属性初始化方式）需人工改写：Avalonia 用 AssetLoader.Open(uri) 构造 Bitmap。");
        }

        ManualNote(node, type.StartsWith("new ", StringComparison.Ordinal) ? type[4..] : type);
        return base.VisitObjectCreationExpression(node);
    }

    // ------------------------------------------------------------------ 赋值

    public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        // DialogResult = v → Close(v)
        var left = node.Left.ToString();
        if (left.EndsWith(".DialogResult", StringComparison.Ordinal) || left == "DialogResult")
        {
            WpfDetected = true;
            var receiver = left.EndsWith(".DialogResult", StringComparison.Ordinal)
                ? left[..^".DialogResult".Length]
                : null;
            var target = receiver is null ? "Close" : $"({receiver}).Close";
            Note(node, NoteSeverity.Info, "CS-DIALOGRESULT",
                "Window.DialogResult 赋值 → Close(result)；返回值经 ShowDialog(owner) 的 Task<object?> 获取。");
            return Expr(node, $"{target}({node.Right})");
        }

        if (left.EndsWith(".Owner", StringComparison.Ordinal))
        {
            WpfDetected = true;
            Note(node, NoteSeverity.Manual, "CS-WINDOW-OWNER",
                "Window.Owner 不存在；请在 ShowDialog(owner) 中传入属主窗口。");
        }

        if (left.EndsWith(".Effect", StringComparison.Ordinal))
        {
            WpfDetected = true;
            Note(node, NoteSeverity.Manual, "CS-EFFECT",
                "WPF Effect（DropShadow/Blur）与 Avalonia Effects API 不同，需人工改写。");
        }

        if (left.EndsWith(".Icon", StringComparison.Ordinal))
        {
            WpfDetected = true;
            Note(node, NoteSeverity.Warning, "CS-WINDOW-ICON",
                "Window.Icon 在 Avalonia 中为 WindowIcon 类型：XAML 可用 avares 字符串；C# 用 new WindowIcon(AssetLoader.Open(uri))。");
        }

        return base.VisitAssignmentExpression(node);
    }

    public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        if (node.Right is LiteralExpressionSyntax lit &&
            lit.Token.IsKind(SyntaxKind.TrueKeyword) &&
            node.Left.ToString().EndsWith(".ShowDialog()", StringComparison.Ordinal))
        {
            WpfDetected = true;
            Note(node, NoteSeverity.Manual, "CS-SHOWDIALOG",
                "ShowDialog() 返回 bool? 的比较需改为 await ShowDialog(owner)（Task<object?>），方法需标记 async。");
        }

        // as 模式：GetTemplateChild("PART_X") as T → this.FindControl<T>("PART_X")
        if (node.IsKind(SyntaxKind.AsExpression) &&
            node.Left is InvocationExpressionSyntax inv &&
            inv.Expression is MemberAccessExpressionSyntax invMa &&
            invMa.Name.Identifier.ValueText == "GetTemplateChild")
        {
            var name = inv.ArgumentList.Arguments.FirstOrDefault()?.ToString() ?? "\"\"";
            var type = node.Right.ToString();
            WpfDetected = true;
            Note(node, NoteSeverity.Info, "CS-TEMPLATECHILD",
                $"GetTemplateChild({name}) as {type} → this.FindControl<{type}>({name})（模板部件在 NameScope 注册；OnApplyTemplate 虚方法在 Avalonia TemplatedControl 上存在）。");
            return Expr(node, $"this.FindControl<{type}>({name})");
        }

        // as 模式：FindResource(key) as T → TryGetResource 三元（唯一变量名避免作用域冲突）
        if (node.IsKind(SyntaxKind.AsExpression) &&
            node.Left is InvocationExpressionSyntax resInv &&
            resInv.Expression is IdentifierNameSyntax resId &&
            resId.Identifier.ValueText == "FindResource")
        {
            var key = resInv.ArgumentList.Arguments.FirstOrDefault()?.ToString() ?? "null";
            var type = node.Right.ToString();
            var varName = $"__res{_resourceVarCounter++}";
            WpfDetected = true;
            Note(node, NoteSeverity.Warning, "CS-FINDRESOURCE",
                $"FindResource({key}) as {type} → TryGetResource({key}, ActualThemeVariant, out var {varName}) 三元式（沿逻辑树查找语义保留；找不到返回 null 而非抛异常）。");
            return Expr(node,
                $"(this.TryGetResource({key}, ActualThemeVariant, out var {varName}) ? {varName} as {type} : null)");
        }

        return base.VisitBinaryExpression(node);
    }

    // ------------------------------------------------------------------ 字符串字面量（pack URI）

    /// <summary>pack://application:,,,/Asm;component/Path → avares://Asm/Path（C# 侧资产 URI 统一）。</summary>
    public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.StringLiteralExpression))
        {
            var text = node.Token.Text;
            var inner = text.StartsWith("@\"", StringComparison.Ordinal)
                ? text[2..^1]
                : text.StartsWith("\"", StringComparison.Ordinal) ? text[1..^1] : text;
            if (inner.StartsWith("pack://application:,,,/", StringComparison.Ordinal))
            {
                var m = System.Text.RegularExpressions.Regex.Match(inner,
                    @"^pack://application:,,,/(?<asm>[^;/,]+);component/(?<rest>.+)$");
                if (m.Success)
                {
                    WpfDetected = true;
                    var avares = $"avares://{m.Groups["asm"].Value}/{m.Groups["rest"].Value}";
                    Note(node, NoteSeverity.Info, "CS-PACK-URI", $"\"{inner}\" → \"{avares}\"（Avalonia 资产 URI）。");
                    return SyntaxFactory.ParseExpression($"\"{avares}\"").WithTriviaFrom(node);
                }
            }
        }
        return base.VisitLiteralExpression(node);
    }

    // ------------------------------------------------------- 依赖属性字段重写

    public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        var declType = node.Declaration.Type.ToString();
        if (declType is not ("DependencyProperty" or "System.Windows.DependencyProperty"))
            return base.VisitFieldDeclaration(node);

        if (node.Declaration.Variables.Count != 1 ||
            node.Declaration.Variables[0].Initializer?.Value is not InvocationExpressionSyntax inv)
        {
            Note(node, NoteSeverity.Manual, "CS-DEPPROP", "非标准 DependencyProperty 声明，需人工转换。");
            return base.VisitFieldDeclaration(node);
        }

        var method = inv.Expression is MemberAccessExpressionSyntax m ? m.Name.Identifier.ValueText : "";
        if (method is not ("Register" or "RegisterAttached" or "RegisterReadOnly"))
            return base.VisitFieldDeclaration(node);

        WpfDetected = true;
        var fieldName = node.Declaration.Variables[0].Identifier.ValueText;
        var owner = node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "Owner";

        var args = inv.ArgumentList.Arguments;
        if (args.Count < 3)
        {
            Note(node, NoteSeverity.Manual, "CS-DEPPROP", "DependencyProperty.Register 参数不足，需人工转换。");
            return base.VisitFieldDeclaration(node);
        }

        var name = (args[0].Expression as LiteralExpressionSyntax)?.Token.Value?.ToString() ?? fieldName;
        if (args[1].Expression is not TypeOfExpressionSyntax typeofValue)
        {
            Note(node, NoteSeverity.Manual, "CS-DEPPROP", "属性类型参数非 typeof(...)，需人工转换。");
            return base.VisitFieldDeclaration(node);
        }
        var valueType = typeofValue.Type.ToString();

        // 默认值（仅当元数据是 new *PropertyMetadata(字面量) 形式）
        string defaultArg = "";
        if (args.Count >= 4 && args[3].Expression is ObjectCreationExpressionSyntax meta &&
            meta.ArgumentList?.Arguments.Count == 1 &&
            meta.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax lit)
        {
            defaultArg = $", {lit.Token.Text}"; // Token.Text 保留源文本：0.0 仍是 0.0，字符串带引号
        }
        var hasAdvancedMetadata = args.Count >= 4 &&
            args[3].Expression is ObjectCreationExpressionSyntax m2 &&
            (m2.ArgumentList?.Arguments.Count ?? 0) > 1;

        string register;
        if (method == "RegisterAttached")
        {
            register = $"global::Avalonia.AvaloniaProperty.RegisterAttached<{owner}, global::Avalonia.AvaloniaObject, {valueType}>(\"{name}\"{defaultArg})";
            Note(node, NoteSeverity.Warning, "CS-DEPPROP-ATTACHED",
                $"附加属性已转换；宿主类型当前为 AvaloniaObject，建议收紧为实际控件类型（RegisterAttached<{owner}, TTarget, {valueType}>）。");
        }
        else
        {
            register = $"global::Avalonia.AvaloniaProperty.Register<{owner}, {valueType}>(\"{name}\"{defaultArg})";
        }

        if (method == "RegisterReadOnly")
            Note(node, NoteSeverity.Warning, "CS-DEPPROP-READONLY",
                "RegisterReadOnly 的只读语义未保留；Avalonia 建议改用 DirectProperty 或私有 SetValue 包装。");

        if (hasAdvancedMetadata)
            Note(node, NoteSeverity.Manual, "CS-DEPPROP-METADATA",
                "PropertyMetadata 的回调/Coerce/选项参数未迁移；Avalonia 对应：property.Changed / GetObservable / StyledPropertyMetadata。");

        var modifiers = string.Join(" ", node.Modifiers.Select(t => t.Text));
        var attributes = node.AttributeLists.Count > 0
            ? string.Join(" ", node.AttributeLists.Select(a => a.ToString())) + " "
            : "";

        Note(node, NoteSeverity.Info, "CS-DEPPROP",
            $"DependencyProperty.{method} → AvaloniaProperty.{(method == "RegisterAttached" ? "RegisterAttached" : "Register")}<{owner}, {valueType}>");

        var code = $"{attributes}{modifiers} global::Avalonia.StyledProperty<{valueType}> {fieldName} =\n    {register};";
        var parsed = SyntaxFactory.ParseMemberDeclaration(code);
        if (parsed == null)
        {
            Note(node, NoteSeverity.Manual, "CS-DEPPROP", "字段重写失败，需人工转换。");
            return base.VisitFieldDeclaration(node);
        }
        return parsed.WithTriviaFrom(node);
    }

    // ------------------------------------------------------------------ 其它

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        _methodNames.Push(node.Identifier.ValueText);
        try
        {
            if (node.Identifier.ValueText == "OnPropertyChanged" && node.Modifiers.Any(SyntaxKind.OverrideKeyword))
            {
                WpfDetected = true;
                Note(node, NoteSeverity.Manual, "CS-ONPROPERTYCHANGED",
                    "OnPropertyChanged(DependencyPropertyChangedEventArgs) 签名不同；Avalonia 请重写 OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T>)。");
            }

            // OnRender(DrawingContext)：custom drawing 体系差异
            if (node.Identifier.ValueText == "OnRender" &&
                node.ParameterList.Parameters.Count == 1 &&
                node.ParameterList.Parameters[0].Type?.ToString().Contains("DrawingContext") == true)
            {
                WpfDetected = true;
                Note(node, NoteSeverity.Manual, "CS-ONRENDER",
                    "OnRender(DrawingContext) → Avalonia 自绘需继承 CustomDrawingControl / Control + Render(DrawingContext)（context.Context），DrawingContext API（DrawGeometry/DrawRectangle 参数）与 WPF 不同，需逐调用改写。");
            }

            // WPF OnApplyTemplate 在 Avalonia TemplatedControl 上存在（protected virtual），签名相同 → 保留
            return base.VisitMethodDeclaration(node);
        }
        finally
        {
            _methodNames.Pop();
        }
    }

    // ------------------------------------------------------------------ 辅助

    /// <summary>当前遍历位置是否处于名称含 DoubleClick 的方法内（双击处理器特判）。</summary>
    private bool IsInDoubleTapHandler() =>
        _methodNames.Count > 0 &&
        KnownMaps.DoubleTappedArgMethodHints.Any(h =>
            _methodNames.Peek().Contains(h, StringComparison.Ordinal));

    /// <summary>
    /// 判断 IdentifierName 是否处于类型位置（声明类型 / cast / typeof / as / is /
    /// 泛型实参 / 返回类型等）。仅类型位置的 Visibility 才映射 bool；
    /// 表达式位置（赋值左侧、比较、传参）是属性引用，须映射 IsVisible。
    /// </summary>
    private static bool IsTypePosition(SyntaxNode node) => node.Parent switch
    {
        VariableDeclarationSyntax v => ReferenceEquals(v.Type, node),
        ParameterSyntax p => ReferenceEquals(p.Type, node),
        CastExpressionSyntax c => ReferenceEquals(c.Type, node),
        TypeOfExpressionSyntax t => ReferenceEquals(t.Type, node),
        ObjectCreationExpressionSyntax o => ReferenceEquals(o.Type, node),
        DefaultExpressionSyntax d => ReferenceEquals(d.Type, node),
        ArrayTypeSyntax a => ReferenceEquals(a.ElementType, node),
        NullableTypeSyntax n => ReferenceEquals(n.ElementType, node),
        MethodDeclarationSyntax m => ReferenceEquals(m.ReturnType, node),
        PropertyDeclarationSyntax p => ReferenceEquals(p.Type, node),
        IndexerDeclarationSyntax i => ReferenceEquals(i.Type, node),
        SimpleBaseTypeSyntax b => ReferenceEquals(b.Type, node),
        TypeArgumentListSyntax => true,
        BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AsExpression or (int)SyntaxKind.IsExpression } bin
            => !ReferenceEquals(bin.Left, node),
        _ => false,
    };

    private static ExpressionSyntax Expr(SyntaxNode from, string code) =>
        SyntaxFactory.ParseExpression(code).WithTriviaFrom(from);

    private QualifiedNameSyntax? RewriteQualified(QualifiedNameSyntax node, out bool changed)
    {
        var text = node.ToString();
        foreach (var (prefix, replacement) in QualifiedPrefixes)
        {
            if (text.StartsWith(prefix + ".", StringComparison.Ordinal) || text == prefix)
            {
                WpfDetected = true;
                changed = true;
                Note(node, NoteSeverity.Info, "CS-QUALIFIED", $"{prefix}.* → {replacement}.*");
                return (QualifiedNameSyntax)SyntaxFactory.ParseName(replacement + text[prefix.Length..]);
            }
        }
        changed = false;
        return null;
    }

    /// <summary>对无法自动转换的 API 记录一次 TODO（每文件每模式去重）。</summary>
    private void ManualNote(SyntaxNode node, string pattern)
    {
        // 后缀模式：receiver.SetResourceReference(...) 等
        foreach (var (suffix, ruleId, message) in ManualNoteSuffixPatterns)
        {
            if (!pattern.EndsWith(suffix, StringComparison.Ordinal)) continue;
            if (!_dedupe.Add(suffix)) return;
            WpfDetected = true;
            Note(node, NoteSeverity.Manual, ruleId, message);
            return;
        }

        // 前缀模式：WeakEventManager<...> / VisualTreeHelper.* 等
        foreach (var (prefix, ruleId, message) in ManualNotePrefixPatterns)
        {
            if (!pattern.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!_dedupe.Add(prefix)) return;
            WpfDetected = true;
            Note(node, NoteSeverity.Manual, ruleId, message);
            return;
        }

        var matched = ManualNoteOncePatterns.FirstOrDefault(p =>
            pattern.Equals(p, StringComparison.Ordinal) ||
            (p.StartsWith("new ", StringComparison.Ordinal) && pattern.EndsWith(p[4..], StringComparison.Ordinal)));
        if (matched == null) return;

        var key = matched;
        if (!_dedupe.Add(key)) return;
        WpfDetected = true;

        var (rule, msg) = matched switch
        {
            "MessageBox.Show" => ("CS-MESSAGEBOX", "MessageBox.Show 无跨平台等价；建议自绘对话框 / CommunityToolkit，或引入 MessageBox.Avalonia 包。"),
            "new OpenFileDialog" or "new SaveFileDialog" or "new OpenFolderDialog" or "new System.Windows.MessageBox" =>
                ("CS-FILEDIALOG", "Microsoft.Win32 文件对话框 → StorageProvider API：await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions())。"),
            "Mouse.GetPosition" => ("CS-MOUSE", "Mouse.GetPosition(el) → e.GetPosition(el)（PointerEventArgs）；Mouse.OverrideCursor → TopLevel.Cursor。"),
            "Mouse.OverrideCursor" => ("CS-MOUSE", "Mouse.OverrideCursor → TopLevel.Cursor / Cursor.Default。"),
            "Keyboard.Modifiers" => ("CS-KEYBOARD", "Keyboard.Modifiers → e.KeyModifiers（KeyEventArgs）或 KeyboardDevice。"),
            "Clipboard.SetText" or "Clipboard.GetText" or "Clipboard.SetImage" or "Clipboard.GetImage" =>
                ("CS-CLIPBOARD", "WPF Clipboard 静态类 → TopLevel.Clipboard（await Clipboard.SetTextAsync / TryGetTextAsync）。"),
            "VisualTreeHelper.GetChild" or "VisualTreeHelper.GetChildrenCount" =>
                ("CS-VISUALTREE", "VisualTreeHelper.GetChild/Count → visual.GetVisualChildren()（Avalonia 命名空间扩展方法）。"),
            "LogicalTreeHelper.FindLogicalNode" => ("CS-LOGICALTREE", "LogicalTreeHelper → 控件自身 FindControl/NameScope 或 VisualTree/LogicalTree 扩展。"),
            "DependencyPropertyDescriptor.FromProperty" => ("CS-DPPD", "DependencyPropertyDescriptor.FromProperty → property.Changed 全局订阅或 GetObservable。"),
            "CompositionTarget.Rendering" => ("CS-COMPOSITION", "CompositionTarget.Rendering → TopLevel.RequestAnimationFrame 或 Animation。"),
            "EventManager.RegisterRoutedEvent" => ("CS-ROUTED", "RoutedEvent 定义方式不同：Avalonia 用 RoutedEvent<T> + AddHandler/RemoveHandler。"),
            "FocusManager.GetFocusedElement" => ("CS-FOCUSMANAGER", "FocusManager → TopLevel.FocusManager / KeyboardDevice.FocusedElement。"),
            "Application.Current.Windows" => ("CS-WINDOWS-COLLECTION", "Application.Current.Windows 无等价；请自行维护窗口列表或遍历 TopLevel。"),
            _ => ("CS-MANUAL", "该 API 需人工确认 Avalonia 等价物。"),
        };

        Note(node, NoteSeverity.Manual, rule, msg);
    }

    private void Note(SyntaxNode node, NoteSeverity severity, string rule, string message)
    {
        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        if (!_dedupe.Add($"{rule}:{line}")) return;
        _notes.Add(new ConversionNote(_file, line, severity, rule, message));
    }
}
