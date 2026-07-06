using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using TrayAppDotNETCommon.AxamlPropertyLinker;
using Xunit;

namespace TrayAppDotNETCommon.AxamlPropertyLinker.Tests;

public sealed class AxamlPropertyLinkerGeneratorTests
{
    [Fact]
    public void GeneratesTypedAccessorsForSupportedAxamlResources()
    {
        AxamlGeneratorResult result = AxamlGeneratorHost.RunGenerator(
            SampleSource,
            [
                new AxamlTestFile("FlyoutButton.axaml", ResourceDictionaryAxaml),
                new AxamlTestFile("SampleWindow.axaml", ControlAxaml)
            ]);

        string generatedSource = string.Join(
            Environment.NewLine,
            result.RunResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));

        Assert.Contains("internal FlyoutButtonAxamlProperties AxamlFlyoutButton", generatedSource);
        Assert.Contains("public double Width =>", generatedSource);
        Assert.Contains("public int ZIndex =>", generatedSource);
        Assert.Contains("public global::Avalonia.Thickness Margin =>", generatedSource);
        Assert.Contains("public global::Avalonia.CornerRadius Radius =>", generatedSource);
        Assert.Contains("public global::Avalonia.Media.TranslateTransform Offset =>", generatedSource);
        Assert.Contains("public global::Avalonia.Media.Color BorderColor =>", generatedSource);
        Assert.Contains("internal FlyoutAxamlProperties AxamlFlyout", generatedSource);
        Assert.Contains("public double WindowWidth =>", generatedSource);
        Assert.Contains("internal static class AxamlFlyoutButton", generatedSource);
        Assert.Contains("public static double Width(object owner) =>", generatedSource);
        Assert.Contains("internal static class AxamlFlyout", generatedSource);
        Assert.Contains("public static double WindowWidth(object owner) =>", generatedSource);
    }

    [Fact]
    public void GeneratedAccessorsReadResourceDictionaryAndControlOwners()
    {
        AxamlGeneratedAssembly generated = AxamlGeneratorHost.CompileAndLoad(
            SampleSource,
            [
                new AxamlTestFile("FlyoutButton.axaml", ResourceDictionaryAxaml),
                new AxamlTestFile("SampleWindow.axaml", ControlAxaml)
            ]);

        Type resourcesType = generated.Assembly.GetRequiredType("Samples.FlyoutButtonResources");
        object resources = Activator.CreateInstance(resourcesType)!;
        object resourceAccessors = Get(resources, "AxamlFlyoutButton")!;
        Assert.Equal(42.5, Get(resourceAccessors, "Width"));
        Assert.Equal(7, Get(resourceAccessors, "ZIndex"));
        Assert.Equal("Avalonia.Thickness", Get(resourceAccessors, "Margin")!.GetType().FullName);
        Assert.Equal("Avalonia.CornerRadius", Get(resourceAccessors, "Radius")!.GetType().FullName);
        Assert.Equal("Avalonia.Media.Color", Get(resourceAccessors, "BorderColor")!.GetType().FullName);

        object originalOffset = Get(resourceAccessors, "Offset")!;
        object clonedOffset = Get(resourceAccessors, "Offset")!;
        Assert.NotSame(originalOffset, clonedOffset);

        Type windowType = generated.Assembly.GetRequiredType("Samples.SampleWindow");
        object window = Activator.CreateInstance(windowType)!;
        object windowAccessors = Get(window, "AxamlFlyout")!;
        Assert.Equal(350.0, Get(windowAccessors, "WindowWidth"));
        Assert.Equal(8, Get(windowAccessors, "EdgePadding"));
    }

    private static object? Get(object target, string property) =>
        target.GetType()
            .GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(target);

    private const string ResourceDictionaryAxaml =
        """
        <ResourceDictionary
            x:Class="Samples.FlyoutButtonResources"
            xmlns="https://github.com/avaloniaui"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:sys="clr-namespace:System;assembly=System.Runtime">

            <sys:Double x:Key="FlyoutButton.Width">42.5</sys:Double>
            <sys:Int32 x:Key="FlyoutButton.ZIndex">7</sys:Int32>
            <Thickness x:Key="FlyoutButton.Margin">1</Thickness>
            <CornerRadius x:Key="FlyoutButton.Radius">4</CornerRadius>
            <TranslateTransform x:Key="FlyoutButton.Offset" X="1" Y="2" />
            <sys:String x:Key="FlyoutButton.BorderColor">#FFFFFFFF</sys:String>
            <sys:String x:Key="Ignored.Title">Ignored</sys:String>
        </ResourceDictionary>
        """;

    private const string ControlAxaml =
        """
        <Window
            x:Class="Samples.SampleWindow"
            xmlns="https://github.com/avaloniaui"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:sys="clr-namespace:System;assembly=System.Runtime">

            <Window.Resources>
                <sys:Double x:Key="Flyout.WindowWidth">350</sys:Double>
                <sys:Int32 x:Key="Flyout.EdgePadding">8</sys:Int32>
            </Window.Resources>
        </Window>
        """;

    private const string SampleSource =
        """
        using System.Collections.Generic;

        namespace Avalonia
        {
            public readonly struct Thickness
            {
                public readonly double Left;

                public Thickness(double uniform)
                {
                    Left = uniform;
                }
            }

            public readonly struct CornerRadius
            {
                public readonly double TopLeft;

                public CornerRadius(double uniform)
                {
                    TopLeft = uniform;
                }
            }
        }

        namespace Avalonia.Media
        {
            public readonly struct Color
            {
                public readonly string Value;

                private Color(string value)
                {
                    Value = value;
                }

                public static Color Parse(string value) => new Color(value);
            }

            public sealed class TranslateTransform
            {
                public double X { get; }
                public double Y { get; }

                public TranslateTransform(double x, double y)
                {
                    X = x;
                    Y = y;
                }
            }
        }

        namespace Avalonia.Controls
        {
            public interface IResourceNode
            {
                bool TryGetResource(string key, object? theme, out object? value);
            }

            public class ResourceDictionary : Dictionary<string, object?>
            {
                public new object? this[string key]
                {
                    get
                    {
                        TryGetValue(key, out object? value);
                        return value;
                    }
                    set
                    {
                        base[key] = value;
                    }
                }
            }

            public class Control : IResourceNode
            {
                private readonly Dictionary<string, object?> _resources = new Dictionary<string, object?>();

                public void SetResource(string key, object? value)
                {
                    _resources[key] = value;
                }

                public bool TryFindResource(string key, out object? value) =>
                    _resources.TryGetValue(key, out value);

                public bool TryGetResource(string key, object? theme, out object? value) =>
                    _resources.TryGetValue(key, out value);
            }
        }

        namespace Samples
        {
            public partial class FlyoutButtonResources : Avalonia.Controls.ResourceDictionary
            {
                public FlyoutButtonResources()
                {
                    this["FlyoutButton.Width"] = 42.5;
                    this["FlyoutButton.ZIndex"] = 7;
                    this["FlyoutButton.Margin"] = new Avalonia.Thickness(1);
                    this["FlyoutButton.Radius"] = new Avalonia.CornerRadius(4);
                    this["FlyoutButton.Offset"] = new Avalonia.Media.TranslateTransform(1, 2);
                    this["FlyoutButton.BorderColor"] = "#FFFFFFFF";
                }
            }

            public partial class SampleWindow : Avalonia.Controls.Control
            {
                public SampleWindow()
                {
                    SetResource("Flyout.WindowWidth", 350.0);
                    SetResource("Flyout.EdgePadding", 8);
                }
            }
        }
        """;
}

internal sealed class AxamlGeneratorHost
{
    private static readonly MetadataReference[] References = CreateReferences();

    public static AxamlGeneratedAssembly CompileAndLoad(string source, IReadOnlyList<AxamlTestFile> axamlFiles)
    {
        AxamlGeneratorResult result = RunGenerator(source, axamlFiles);
        ThrowIfErrors(result.CompilationDiagnostics);

        using MemoryStream pe = new();
        EmitResult emit = result.Compilation.Emit(pe);
        if (!emit.Success)
            ThrowIfErrors(emit.Diagnostics);

        pe.Position = 0;
        Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(pe);
        return new AxamlGeneratedAssembly(assembly, result.RunResult);
    }

    public static AxamlGeneratorResult RunGenerator(string source, IReadOnlyList<AxamlTestFile> axamlFiles)
    {
        string assemblyName = $"AxamlPropertyLinkerTest_{Guid.NewGuid():N}";
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));

        List<AdditionalText> additionalTexts = [];
        foreach (AxamlTestFile file in axamlFiles)
            additionalTexts.Add(new StringAdditionalText(file.Path, file.Text));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new AxamlPropertyLinkerGenerator().AsSourceGenerator()],
            additionalTexts,
            parseOptions,
            new TestAnalyzerConfigOptionsProvider("Samples"));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        ImmutableArray<Diagnostic> compilationDiagnostics = [
            ..outputCompilation
                .GetDiagnostics()
                .AddRange(generatorDiagnostics)
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        ];

        return new AxamlGeneratorResult(outputCompilation, runResult, compilationDiagnostics);
    }

    private static MetadataReference[] CreateReferences()
    {
        string? tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrEmpty(tpa))
            throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is not available.");

        SortedSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in tpa.Split(Path.PathSeparator))
        {
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                paths.Add(path);
        }

        return [.. paths.Select(static path => MetadataReference.CreateFromFile(path))];
    }

    private static void ThrowIfErrors(IEnumerable<Diagnostic> diagnostics)
    {
        Diagnostic[] errors = diagnostics
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        if (errors.Length == 0) return;

        string message = string.Join(
            Environment.NewLine,
            errors.Select(static diagnostic => diagnostic.ToString()));
        throw new InvalidOperationException(message);
    }
}

internal sealed class StringAdditionalText(string path, string text) : AdditionalText
{
    private readonly SourceText _text = SourceText.From(text, Encoding.UTF8);

    public override string Path { get; } = path;

    public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
}

internal sealed class TestAnalyzerConfigOptionsProvider(string rootNamespace) : AnalyzerConfigOptionsProvider
{
    private readonly AnalyzerConfigOptions _globalOptions = new TestAnalyzerConfigOptions(new Dictionary<string, string>
    {
        ["build_property.RootNamespace"] = rootNamespace,
        ["build_property.MSBuildProjectName"] = rootNamespace
    });

    public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => TestAnalyzerConfigOptions.Empty;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => TestAnalyzerConfigOptions.Empty;
}

internal sealed class TestAnalyzerConfigOptions(Dictionary<string, string> values) : AnalyzerConfigOptions
{
    public static readonly TestAnalyzerConfigOptions Empty = new(new Dictionary<string, string>());

    public override bool TryGetValue(string key, out string value)
    {
        if (values.TryGetValue(key, out string? found))
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }
}

internal sealed record AxamlTestFile(string Path, string Text);

internal sealed record AxamlGeneratorResult(
    Compilation Compilation,
    GeneratorDriverRunResult RunResult,
    ImmutableArray<Diagnostic> CompilationDiagnostics);

internal sealed record AxamlGeneratedAssembly(Assembly Assembly, GeneratorDriverRunResult RunResult);

internal static class AxamlAssemblyExtensions
{
    public static Type GetRequiredType(this Assembly assembly, string name) =>
        assembly.GetType(name, throwOnError: true)!;
}
