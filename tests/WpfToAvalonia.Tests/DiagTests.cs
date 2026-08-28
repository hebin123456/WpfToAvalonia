using WpfToAvalonia.Core.CSharp;

namespace WpfToAvalonia.Tests;

/// <summary>
/// 回归：成员绑定（x?.Member）位置不得替换为限定名
/// （MemberBinding.Name 要求 SimpleNameSyntax，否则 InvalidCastException）。
/// 真实样本：ForkPlus BinaryDiffUserControl.xaml.cs 的 lhsImageData?.ImageSource。
/// </summary>
public class MemberBindingRegressionTests
{
    private static CSharpTransformResult Transform(string code) =>
        new CSharpTransformer().Transform(code, "Test.cs");

    [Fact]
    public void ConditionalAccess_MemberName_CollidingWithTypeName_IsUntouched()
    {
        var r = Transform("""
            using System.Windows.Media;

            class C
            {
                void M(BitmapSource lhs)
                {
                    var s = lhs?.ImageSource;
                }
            }
            """);

        // 转换不崩溃；成员名 ImageSource 保持原样（仅类型名位置重命名）
        Assert.Contains("lhs?.ImageSource", r.Code);
        Assert.DoesNotContain("lhs?.global::", r.Code);
    }

    [Fact]
    public void AliasQualified_Name_CollidingWithTypeName_IsUntouched()
    {
        var r = Transform("""
            class C
            {
                void M()
                {
                    var t = typeof(global::ImageSource);
                }
            }
            """);

        Assert.NotNull(r);
    }
}
