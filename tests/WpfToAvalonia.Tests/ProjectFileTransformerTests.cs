using System.Xml.Linq;
using WpfToAvalonia.Core.Model;
using WpfToAvalonia.Core.MsBuild;

namespace WpfToAvalonia.Tests;

public class ProjectFileTransformerTests
{
    private static ProjectFileResult Transform(string xml, bool usesDataGrid = false) =>
        new ProjectFileTransformer(new ConversionOptions()).Transform(xml, "Test.csproj", usesDataGrid);

    private const string SdkStyleWpf = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net8.0-windows</TargetFramework>
            <UseWPF>true</UseWPF>
            <RootNamespace>Demo</RootNamespace>
          </PropertyGroup>
          <ItemGroup>
            <Resource Include="Assets\logo.png" />
          </ItemGroup>
        </Project>
        """;

    [Fact]
    public void WpfMarkers_AreRemoved_AndTfmUpdated()
    {
        var r = Transform(SdkStyleWpf);

        Assert.DoesNotContain("UseWPF", r.Xml);
        Assert.DoesNotContain("net8.0-windows", r.Xml);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", r.Xml);
        Assert.True(r.IsExecutable);
    }

    [Fact]
    public void AvaloniaPackages_AreAdded()
    {
        var r = Transform(SdkStyleWpf);
        var doc = XDocument.Parse(r.Xml);

        var refs = doc.Descendants("PackageReference")
            .Where(p => p.Attribute("Include")?.Value.StartsWith("Avalonia", StringComparison.Ordinal) == true)
            .ToDictionary(p => p.Attribute("Include")!.Value, p => p.Attribute("Version")!.Value);

        Assert.Contains("Avalonia", refs.Keys);
        Assert.Contains("Avalonia.Desktop", refs.Keys);
        Assert.Contains("Avalonia.Themes.Fluent", refs.Keys);
        Assert.All(refs.Values, v => Assert.Equal("12.1.1", v));
    }

    [Fact]
    public void DataGridPackage_IsAddedOnlyWhenUsed()
    {
        var withGrid = Transform(SdkStyleWpf, usesDataGrid: true);
        var withoutGrid = Transform(SdkStyleWpf);

        Assert.Contains("Avalonia.Controls.DataGrid", withGrid.Xml);
        Assert.DoesNotContain("Avalonia.Controls.DataGrid", withoutGrid.Xml);
    }

    [Fact]
    public void ResourceItems_BecomeAvaloniaResource()
    {
        var r = Transform(SdkStyleWpf);

        Assert.Contains("<AvaloniaResource Include=\"Assets\\logo.png\"", r.Xml);
        Assert.DoesNotContain("<Resource ", r.Xml);
    }

    [Fact]
    public void PageItems_AreRemoved()
    {
        var xml = SdkStyleWpf.Replace("</Project>", """
              <ItemGroup>
                <Page Include="MainWindow.xaml" />
                <ApplicationDefinition Include="App.xaml" />
              </ItemGroup>
            </Project>
            """);

        var r = Transform(xml);

        Assert.DoesNotContain("<Page ", r.Xml);
        Assert.DoesNotContain("ApplicationDefinition", r.Xml);
    }

    [Fact]
    public void ReflectionBindingSemantics_IsPreserved()
    {
        var r = Transform(SdkStyleWpf);

        Assert.Contains("<AvaloniaUseCompiledBindingsByDefault>false", r.Xml);
    }

    [Fact]
    public void NullableIsNotInjected()
    {
        var r = Transform(SdkStyleWpf);

        // 沿用源工程设置，避免 WPF 旧式签名产生可空警告噪音
        Assert.DoesNotContain("<Nullable>", r.Xml);
    }
}
