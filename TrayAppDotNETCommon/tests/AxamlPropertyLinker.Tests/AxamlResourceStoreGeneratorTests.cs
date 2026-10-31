using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace TrayAppDotNETCommon.AxamlPropertyLinker.Tests;

public sealed class AxamlResourceStoreGeneratorTests
{
    private const string StorePath = @"C:\repo\Samples\src\UI\Panel.axaml";

    private const string PanelAxaml =
        """
        <ResourceDictionary
            x:Class="Samples.PanelResources"
            xmlns="https://github.com/avaloniaui"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:sys="clr-namespace:System;assembly=System.Runtime">
            <sys:Double x:Key="Panel.Width">12</sys:Double>
        </ResourceDictionary>
        """;

    // Keys that do not follow the one-dot schema give the linker nothing to type
    private const string UntypedAxaml =
        """
        <ResourceDictionary
            x:Class="Samples.UntypedResources"
            xmlns="https://github.com/avaloniaui"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:sys="clr-namespace:System;assembly=System.Runtime">
            <sys:Double x:Key="NoDotKey">12</sys:Double>
        </ResourceDictionary>
        """;

    private const string GlyphAxaml =
        """
        <ResourceDictionary
            x:Class="Samples.CatalogResources"
            xmlns="https://github.com/avaloniaui"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
            xmlns:visuals="clr-namespace:TrayAppDotNETCommon.Visuals">
            <visuals:GlyphDefinition x:Key="Catalog.Settings" Text="S" />
            <visuals:ThemeColor x:Key="Catalog.Background" LightHex="#FFFFFF" />
            <sys:Double x:Key="Catalog.class" xmlns:sys="clr-namespace:System;assembly=System.Runtime">3</sys:Double>
        </ResourceDictionary>
        """;

    private static string Store(string mode, string className = "PanelResources", string label = "Panel resources") =>
        AxamlPropertyLinkerGeneratorTests.SampleSource +
        $$"""

          namespace Samples
          {
              [TrayAppDotNETCommon.UI.AxamlResourceStore(TrayAppDotNETCommon.UI.AxamlReloadMode.{{mode}}, "{{label}}")]
              public sealed partial class {{className}} : Avalonia.Controls.ResourceDictionary;
          }
          """;

    [Theory]
    [InlineData("Common")]
    [InlineData("CommonPerOwner")]
    [InlineData("Synchronized")]
    [InlineData("Replaced")]
    [InlineData("GlyphCatalog")]
    [InlineData("AppTheme")]
    public void EveryModeCompilesInDebugAndRelease(string mode)
    {
        foreach (bool isDebug in new[] { true, false })
        {
            AxamlGeneratorResult result = AxamlGeneratorHost.RunGenerator(
                Store(mode),
                [new AxamlTestFile(StorePath, PanelAxaml)],
                isDebug);

            Assert.Empty(result.CompilationDiagnostics);
        }
    }

    [Fact]
    public void DebugStoreWatchesTheAdditionalFilePathAndRaisesItsOwnEvent()
    {
        AxamlGeneratedAssembly generated = AxamlGeneratorHost.CompileAndLoad(
            Store("Replaced"),
            [new AxamlTestFile(StorePath, PanelAxaml)],
            isDebug: true);
        Type resourcesType = generated.Assembly.GetRequiredType("Samples.PanelResources");

        object store = resourcesType
            .GetField("ResourceStore", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(obj: null)!;
        Assert.Equal(StorePath, store.GetType().GetProperty("SourcePath")!.GetValue(store));

        object initial = Current(resourcesType);
        int notifications = 0;
        EventInfo reloaded = resourcesType.GetEvent(
            "ResourcesReloaded",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Action handler = () => notifications++;
        reloaded.GetAddMethod(nonPublic: true)!.Invoke(obj: null, [handler]);

        resourcesType.GetMethod("ReloadNow", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(obj: null, parameters: null);

        Assert.Equal(expected: 1, notifications);
        Assert.NotSame(initial, Current(resourcesType));
    }

    [Fact]
    public void ReleaseStoreSharesOneLazyInstanceWithoutReloadMembers()
    {
        AxamlGeneratedAssembly generated = AxamlGeneratorHost.CompileAndLoad(
            Store("Replaced"),
            [new AxamlTestFile(StorePath, PanelAxaml)]);
        Type resourcesType = generated.Assembly.GetRequiredType("Samples.PanelResources");

        Assert.Same(Current(resourcesType), Current(resourcesType));
        Assert.Null(resourcesType.GetEvent("ResourcesReloaded", BindingFlags.NonPublic | BindingFlags.Static));
        Assert.Null(resourcesType.GetMethod("ReloadNow", BindingFlags.NonPublic | BindingFlags.Static));
    }

    [Fact]
    public void PerOwnerStoreKeepsNoSharedInstanceInRelease()
    {
        AxamlGeneratedAssembly generated = AxamlGeneratorHost.CompileAndLoad(
            Store("CommonPerOwner"),
            [new AxamlTestFile(StorePath, PanelAxaml)]);
        Type resourcesType = generated.Assembly.GetRequiredType("Samples.PanelResources");

        Assert.Null(resourcesType.GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static));
        Assert.NotNull(Activator.CreateInstance(resourcesType));
    }

    [Fact]
    public void DictionaryWithoutTypedKeysStillGetsItsStore()
    {
        AxamlGeneratedAssembly generated = AxamlGeneratorHost.CompileAndLoad(
            Store("Common", className: "UntypedResources"),
            [new AxamlTestFile(Path: "Untyped.axaml", UntypedAxaml)]);
        Type resourcesType = generated.Assembly.GetRequiredType("Samples.UntypedResources");

        Assert.Same(Current(resourcesType), Current(resourcesType));
    }

    [Fact]
    public void GlyphThemeColorAndKeywordKeysGetTypedGetters()
    {
        const string source =
            """

            namespace Samples
            {
                public partial class CatalogResources : Avalonia.Controls.ResourceDictionary
                {
                    public CatalogResources()
                    {
                        this["Catalog.Settings"] = new TrayAppDotNETCommon.Visuals.GlyphDefinition { Text = "S" };
                        this["Catalog.Background"] = new TrayAppDotNETCommon.Visuals.ThemeColor { LightHex = "#FFFFFF" };
                        this["Catalog.class"] = 3.0;
                    }
                }
            }
            """;
        AxamlGeneratedAssembly generated = AxamlGeneratorHost.CompileAndLoad(
            AxamlPropertyLinkerGeneratorTests.SampleSource + source,
            [new AxamlTestFile(Path: "Catalog.axaml", GlyphAxaml)]);
        Type resourcesType = generated.Assembly.GetRequiredType("Samples.CatalogResources");
        object resources = Activator.CreateInstance(resourcesType)!;
        object accessors = Get(resources, "AxamlCatalog")!;

        object glyph = Get(accessors, "Settings")!;
        Assert.Equal(expected: "TrayAppDotNETCommon.Visuals.Glyph", glyph.GetType().FullName);
        Assert.Equal(expected: "S", Get(glyph, "Text"));
        object themeColor = Get(accessors, "Background")!;
        Assert.Equal(expected: "TrayAppDotNETCommon.Visuals.ThemeColor", themeColor.GetType().FullName);
        Assert.Equal(expected: 3.0, Get(accessors, "class"));

        object registered = generated.Assembly
            .GetRequiredType("TrayAppDotNETCommon.UI.Debugging.DebugUIProvenance")
            .GetProperty("RegisteredGlyphKeys")!
            .GetValue(obj: null)!;
        Assert.Contains(expected: "Catalog.Settings", (IEnumerable<string>)registered);
    }

    [Fact]
    public void RenamedGlyphKeyBecomesACompileError()
    {
        const string consumer =
            """

            namespace Samples
            {
                public partial class CatalogResources : Avalonia.Controls.ResourceDictionary;

                public static class Consumer
                {
                    public static TrayAppDotNETCommon.Visuals.Glyph Gear(CatalogResources resources) =>
                        resources.AxamlCatalog.Gear;
                }
            }
            """;
        AxamlGeneratorResult result = AxamlGeneratorHost.RunGenerator(
            AxamlPropertyLinkerGeneratorTests.SampleSource + consumer,
            [new AxamlTestFile(Path: "Catalog.axaml", GlyphAxaml)]);

        Diagnostic error = Assert.Single(result.CompilationDiagnostics);
        Assert.Equal(expected: "CS1061", error.Id);
    }

    [Fact]
    public void StoreWithoutAxamlIsReported()
    {
        AxamlGeneratorResult result = AxamlGeneratorHost.RunGenerator(Store("Common"), []);

        Assert.Contains(result.RunResult.Diagnostics, static diagnostic => diagnostic.Id == "TADNAX001");
    }

    [Fact]
    public void StoreWithTwoAxamlFilesIsReported()
    {
        AxamlGeneratorResult result = AxamlGeneratorHost.RunGenerator(
            Store("Common"),
            [new AxamlTestFile(Path: "A.axaml", PanelAxaml), new AxamlTestFile(Path: "B.axaml", PanelAxaml)]);

        Assert.Contains(result.RunResult.Diagnostics, static diagnostic => diagnostic.Id == "TADNAX002");
    }

    [Fact]
    public void NonPartialStoreOwnerIsReportedInsteadOfBreakingGeneratedCode()
    {
        string source = AxamlPropertyLinkerGeneratorTests.SampleSource +
                        """

                        namespace Samples
                        {
                            [TrayAppDotNETCommon.UI.AxamlResourceStore(TrayAppDotNETCommon.UI.AxamlReloadMode.Common, "Panel")]
                            public sealed class OtherResources : Avalonia.Controls.ResourceDictionary;
                        }
                        """;
        AxamlGeneratorResult result = AxamlGeneratorHost.RunGenerator(
            source,
            [new AxamlTestFile(Path: "Other.axaml", PanelAxaml.Replace("PanelResources", "OtherResources"))]);

        Diagnostic diagnostic = Assert.Single(result.RunResult.Diagnostics);
        Assert.Equal(expected: "TADNAX003", diagnostic.Id);
        Assert.Contains(expectedSubstring: "partial", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("public abstract partial class OtherResources : Avalonia.Controls.ResourceDictionary;", "abstract")]
    [InlineData("public sealed partial class OtherResources<T> : Avalonia.Controls.ResourceDictionary;", "generic")]
    public void AbstractOrGenericStoreOwnerIsReportedInsteadOfBreakingGeneratedCode(string declaration, string word)
    {
        string source = AxamlPropertyLinkerGeneratorTests.SampleSource +
                        $$"""

                          namespace Samples
                          {
                              [TrayAppDotNETCommon.UI.AxamlResourceStore(TrayAppDotNETCommon.UI.AxamlReloadMode.Common, "Panel")]
                              {{declaration}}
                          }
                          """;
        AxamlGeneratorResult result = AxamlGeneratorHost.RunGenerator(
            source,
            [new AxamlTestFile(Path: "Other.axaml", PanelAxaml.Replace("PanelResources", "OtherResources"))]);

        Diagnostic diagnostic = Assert.Single(result.RunResult.Diagnostics);
        Assert.Equal(expected: "TADNAX003", diagnostic.Id);
        Assert.Contains(word, diagnostic.GetMessage());
        Assert.DoesNotContain(
            result.CompilationDiagnostics,
            static error => error.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void MalformedStoreAxamlReportsTheParseErrorInsteadOfAMissingFile()
    {
        string malformed = PanelAxaml.Replace("</sys:Double>", "</sys:Doubel>");
        AxamlGeneratorResult result = AxamlGeneratorHost.RunGenerator(
            Store("Common"),
            [new AxamlTestFile(StorePath, malformed)]);

        Diagnostic diagnostic = Assert.Single(result.RunResult.Diagnostics);
        Assert.Equal(expected: "TADNAX004", diagnostic.Id);
        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        Assert.Equal(StorePath, span.Path);
        Assert.Equal(expected: 6, span.StartLinePosition.Line + 1);

        // The store itself still compiles, so the AXAML error is the only error
        Assert.Empty(result.CompilationDiagnostics.Where(static error => error.Id != "TADNAX004"));
    }

    [Fact]
    public void KeywordNamespacesAndClassesAreEscaped()
    {
        const string axaml =
            """
            <ResourceDictionary
                x:Class="Samples.event.checked"
                xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:sys="clr-namespace:System;assembly=System.Runtime">
                <sys:Double x:Key="Flags.default">1</sys:Double>
            </ResourceDictionary>
            """;
        AxamlGeneratorResult result = AxamlGeneratorHost.RunGenerator(
            AxamlPropertyLinkerGeneratorTests.SampleSource,
            [new AxamlTestFile(Path: "Flags.axaml", axaml)]);

        string generated = string.Join(
            Environment.NewLine,
            result.RunResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
        Assert.Contains(expectedSubstring: "namespace Samples.@event", generated);
        Assert.Contains(expectedSubstring: "partial class @checked", generated);
        Assert.Contains(expectedSubstring: "public double @default =>", generated);
    }

    [Fact]
    public void UnchangedKeysStayCachedWhenOnlyCommentsOrUnrelatedCodeChange()
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "AxamlPropertyLinkerCachingTest",
            [CSharpSyntaxTree.ParseText(Store("Common"), parseOptions)],
            PlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        StringAdditionalText original = new(StorePath, PanelAxaml);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new AxamlPropertyLinkerGenerator().AsSourceGenerator()],
            [original],
            parseOptions,
            new TestAnalyzerConfigOptionsProvider(rootNamespace: "Samples", projectDirectory: string.Empty),
            new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);

        // A comment changes the parsed document but not the keys the linker reads
        StringAdditionalText commented = new(StorePath, PanelAxaml.Replace("<sys:Double", "<!-- note --><sys:Double"));
        driver = driver.ReplaceAdditionalText(original, commented);
        compilation = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Samples { public sealed class Unrelated; }", parseOptions));
        driver = driver.RunGenerators(compilation);

        GeneratorRunResult run = Assert.Single(driver.GetRunResult().Results);
        AssertCached(run, stepName: "AxamlClassModels");
        AssertCached(run, stepName: "AxamlResourceStores");
        Assert.Contains(
            run.TrackedSteps["AxamlDocuments"].SelectMany(static step => step.Outputs),
            static output => output.Reason == IncrementalStepRunReason.Modified);
    }

    private static void AssertCached(GeneratorRunResult run, string stepName)
    {
        ImmutableArray<IncrementalGeneratorRunStep> steps = run.TrackedSteps[stepName];
        Assert.NotEmpty(steps);
        foreach ((object _, IncrementalStepRunReason reason) in steps.SelectMany(static step => step.Outputs))
            Assert.True(reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged, $"{stepName}: {reason}");
    }

    private static MetadataReference[] PlatformReferences() =>
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(static path => path.EndsWith(value: ".dll", StringComparison.OrdinalIgnoreCase))
            .Select(static path => MetadataReference.CreateFromFile(path))
    ];

    private static object Current(Type resourcesType) =>
        resourcesType.GetProperty("Current", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(obj: null)!;

    private static object? Get(object target, string property) =>
        target.GetType()
            .GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(target);
}
