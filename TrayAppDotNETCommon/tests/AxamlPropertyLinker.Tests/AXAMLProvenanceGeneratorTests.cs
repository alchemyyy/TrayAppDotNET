#if DEBUG
using Microsoft.CodeAnalysis;
using System.Text.RegularExpressions;
using Xunit;

namespace TrayAppDotNETCommon.AxamlPropertyLinker.Tests;

public sealed class AXAMLProvenanceGeneratorTests
{
    [Fact]
    public void EmitsModularDebugCatalogForAXAMLConstructs()
    {
        const string ProjectDirectory = @"C:\repo\Samples\src\";
        const string SourcePath = @"C:\repo\Samples\src\Views\SampleWindow.axaml";
        AxamlGeneratorResult result = AxamlGeneratorHost.RunGenerator(
            AxamlPropertyLinkerGeneratorTests.SampleSource,
            [new AxamlTestFile(SourcePath, ProvenanceAXAML)],
            isDebug: true,
            projectDirectory: ProjectDirectory);

        Assert.Empty(result.CompilationDiagnostics);
        GeneratedSourceResult generatedResult = result.RunResult.Results
            .Single()
            .GeneratedSources
            .Single(static source => source.HintName == "AxamlPropertyLinker.AxamlProvenance.g.cs");
        string generatedSource = generatedResult.SourceText.ToString();

        Assert.Contains("#if DEBUG", generatedSource);
        Assert.Contains("internal static class AXAMLProvenanceCatalog", generatedSource);
        Assert.Contains("DebugUIProvenance.RegisterAXAML(", generatedSource);
        Assert.Contains("\"Samples/src/Views/SampleWindow.axaml\"", generatedSource);
        Assert.Contains("AXAMLProvenanceKind.ResourceDefinition", generatedSource);
        Assert.Contains("AXAMLProvenanceKind.PropertyAssignment", generatedSource);
        Assert.Contains("AXAMLProvenanceKind.ResourceReference", generatedSource);
        Assert.Contains("AXAMLProvenanceKind.Style", generatedSource);
        Assert.Contains("AXAMLProvenanceKind.StyleSetter", generatedSource);
        Assert.Contains("AXAMLProvenanceKind.ControlTheme", generatedSource);
        Assert.Contains("AXAMLProvenanceKind.Template", generatedSource);
        Assert.Contains("AXAMLProvenanceKind.Binding", generatedSource);
        Assert.Contains("\"/Window[1]/Window.Resources[1]/Color[1]\"", generatedSource);
        Assert.Contains("\"PART_Border\"", generatedSource);
        Assert.Contains("\"Palette.Card\"", generatedSource);
        Assert.Contains("\"Button:pointerover\"", generatedSource);
        Assert.Matches(
            "\"Border\",\\s*\"/Window\\[1\\]/Grid\\[1\\]/Border\\[1\\]\",\\s*null,\\s*\"Background\"",
            generatedSource);
    }

    [Fact]
    public void EmitsExactXMLLineAndColumnLocations()
    {
        AxamlGeneratorResult result = AxamlGeneratorHost.RunGenerator(
            AxamlPropertyLinkerGeneratorTests.SampleSource,
            [new AxamlTestFile("SampleWindow.axaml", ProvenanceAXAML)],
            isDebug: true);

        GeneratedSourceResult generatedResult = result.RunResult.Results
            .Single()
            .GeneratedSources
            .Single(static source => source.HintName == "AxamlPropertyLinker.AxamlProvenance.g.cs");
        string generatedSource = generatedResult.SourceText.ToString();

        Match resourceLocation = Regex.Match(
            generatedSource,
            "AXAMLProvenanceKind\\.ResourceDefinition,\\s*\"SampleWindow\\.axaml\",\\s*" +
            "(?<line>\\d+),\\s*(?<column>\\d+),.*?\"Palette\\.Card\"",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(resourceLocation.Success);
        Assert.Equal("9", resourceLocation.Groups["line"].Value);
        Assert.Equal("10", resourceLocation.Groups["column"].Value);

        Match styleLocation = Regex.Match(
            generatedSource,
            "AXAMLProvenanceKind\\.Style,\\s*\"SampleWindow\\.axaml\",\\s*" +
            "(?<line>\\d+),\\s*(?<column>\\d+),.*?\"Button:pointerover\"",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(styleLocation.Success);
        Assert.Equal("21", styleLocation.Groups["line"].Value);
        Assert.Equal("16", styleLocation.Groups["column"].Value);
    }

    private const string ProvenanceAXAML =
        """
        <Window
            x:Class="Samples.SampleWindow"
            xmlns="https://github.com/avaloniaui"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            Background="{StaticResource Palette.Card}"
            Tag="{Binding Title, Converter={StaticResource Converter.Title}}">

            <Window.Resources>
                <Color x:Key="Palette.Card">#FF202020</Color>
                <ControlTheme x:Key="ButtonTheme" TargetType="Button">
                    <Setter Property="Background" Value="{DynamicResource Palette.Card}" />
                    <Setter Property="Template">
                        <ControlTemplate>
                            <Border x:Name="PART_Border" Background="{TemplateBinding Background}" />
                        </ControlTemplate>
                    </Setter>
                </ControlTheme>
            </Window.Resources>

            <Window.Styles>
                <Style Selector="Button:pointerover">
                    <Setter Property="Foreground" Value="{StaticResource Palette.Foreground}" />
                </Style>
            </Window.Styles>

            <Grid x:Name="Container">
                <Border Background="#FF303030" />
            </Grid>
        </Window>
        """;
}
#endif
