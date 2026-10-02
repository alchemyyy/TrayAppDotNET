using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Xunit;

namespace TrayAppDotNETCommon.AxamlPropertyLinker.Tests;

public sealed class ControlMapGeneratorTests
{
    private const string MapHeader =
        """
        <ControlMap
            x:Class="Samples.UI.ControlMap"
            xmlns="using:TrayAppDotNETCommon.UI.ControlMapping"
            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
        """;

    private const string MapFooter = "</ControlMap>";

    private const string ValidMap =
        MapHeader +
        """
            <Template ID="Shell">
                <Leaf ID="Close" Kind="Button" Keys="Escape" />
                <Scope ID="Host">
                    <Slot />
                </Scope>
            </Template>

            <Surface ID="Flyout" Kind="Popup" Template="Shell">
                <Leaf ID="Hide" Kind="Command" Keys="Ctrl+W" Pointer="Ctrl+Click;Wheel" />
                <Scope ID="Header" Tab="Once" Arrows="Horizontal">
                    <Leaf ID="Settings" Kind="Button" FocusKeys="Return;F2" Description="Opens &lt;settings&gt;" />
                    <Leaf ID="Undock" Kind="Button" Keys="Ctrl+Shift+U" KeyScope="Flyout" />
                </Scope>
                <Scope ID="Table" IsFocusable="True" Entry="Enter" IsRepeated="True">
                    <Leaf ID="Row" Kind="Region" IsTabStop="False" IsRepeated="True" />
                </Scope>
            </Surface>
        """ +
        MapFooter;

    // Minimal stand-ins for the runtime types, plus the hand-written owner every map's x:Class needs
    private const string RuntimeStubs =
        """
        namespace TrayAppDotNETCommon.UI.ControlMapping
        {
            public readonly record struct ControlMapNodeID(string Map, string Path);

            public class ControlMap
            {
                protected ControlMap(string name) => LoadedName = name;

                public string LoadedName { get; }
            }

            public static class ControlMapCatalog
            {
                public static System.Collections.Generic.List<string> Registered { get; } = new();

                public static void Register(string mapName, System.Func<ControlMap> factory) => Registered.Add(mapName);
            }
        }

        namespace Avalonia.Markup.Xaml
        {
            public static class AvaloniaXamlLoader
            {
                public static void Load(object target) { }
            }
        }

        namespace Samples.UI
        {
            public sealed partial class ControlMap : TrayAppDotNETCommon.UI.ControlMapping.ControlMap
            {
                public ControlMap() : base(MapName) => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
            }

            public sealed class UnrelatedControlMap;
        }
        """;

    [Fact]
    public void GeneratesNestedNodeIdsThatCompile()
    {
        ControlMapGeneratorResult result = ControlMapGeneratorHost.Run(
            [new ControlMapTestFile(Path: "UI/ControlMap.axaml", ValidMap)]);

        Assert.Empty(result.Diagnostics);
        Assembly assembly = ControlMapGeneratorHost.CompileAndLoad(result);

        object? map = Activator.CreateInstance(assembly.GetType("Samples.UI.ControlMap", throwOnError: true)!);
        Assert.Equal(expected: "Samples.UI.ControlMap", map!.GetType().GetProperty("LoadedName")!.GetValue(map));
        AssertNodeID(assembly, typeName: "Samples.UI.ControlMap+Shell", member: "Close", "Shell.Close");
        AssertNodeID(assembly, typeName: "Samples.UI.ControlMap+Shell+Host", member: "ID", "Shell.Host");
        AssertNodeID(assembly, typeName: "Samples.UI.ControlMap+Flyout", member: "ID", "Flyout");
        AssertNodeID(assembly, typeName: "Samples.UI.ControlMap+Flyout+Header", member: "Settings",
            "Flyout.Header.Settings");
        AssertNodeID(assembly, typeName: "Samples.UI.ControlMap+Flyout+Table", member: "Row",
            "Flyout.Table.Row");
    }

    [Fact]
    public void ModuleInitializerRegistersTheMapWithTheCatalog()
    {
        ControlMapGeneratorResult result = ControlMapGeneratorHost.Run(
            [new ControlMapTestFile(Path: "UI/ControlMap.axaml", ValidMap)]);

        Assembly assembly = ControlMapGeneratorHost.CompileAndLoad(result);
        Type catalog = assembly.GetType("TrayAppDotNETCommon.UI.ControlMapping.ControlMapCatalog", throwOnError: true)!;
        object registered = catalog.GetProperty("Registered")!.GetValue(obj: null)!;

        Assert.Contains(expected: "Samples.UI.ControlMap", (IEnumerable<string>)registered);
    }

    [Fact]
    public void VariantsGetTypedNamesAndNoNodeIDs()
    {
        ControlMapGeneratorResult result = ControlMapGeneratorHost.Run(
        [
            new ControlMapTestFile(Path: "UI/ControlMap.axaml", Map(
                """
                <Surface ID="Flyout" Kind="Popup">
                    <Variant ID="HeaderOnTop" Order="Header Body" />
                    <Leaf ID="Body" Kind="Slider" />
                    <Scope ID="Header" Tab="Once" Arrows="Horizontal">
                        <Variant ID="HeaderOnTop" Order="Undock Settings" />
                        <Leaf ID="Settings" Kind="Button" />
                        <Leaf ID="Undock" Kind="Button" />
                    </Scope>
                </Surface>
                """))
        ]);

        Assert.Empty(result.Diagnostics);
        string generated = result.GeneratedSource();
        Assert.Contains(expectedSubstring: "public const string HeaderOnTop = \"HeaderOnTop\";", generated);
        Assert.DoesNotContain(expectedSubstring: "\"Flyout.HeaderOnTop\"", generated);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(generated, "public const string HeaderOnTop"));
    }

    [Theory]
    [InlineData("""<Surface ID="Flyout"><Variant ID="Wide" Order="Missing" /><Leaf ID="Close" Kind="Command" /></Surface>""")]
    [InlineData("""<Surface ID="Flyout"><Variant ID="Wide" Order="Close Close" /><Leaf ID="Close" Kind="Command" /></Surface>""")]
    [InlineData("""<Surface ID="Flyout"><Variant ID="Wide" Order=" " /><Leaf ID="Close" Kind="Command" /></Surface>""")]
    [InlineData("""<Template ID="Shell"><Variant ID="Wide" Order="Close" /><Leaf ID="Close" Kind="Command" /><Slot /></Template>""")]
    [InlineData("""<Template ID="Shell"><Leaf ID="Close" Kind="Command" /></Template><Surface ID="Flyout" Template="Shell"><Variant ID="Wide" Order="Close" /></Surface>""")]
    public void ReportsInvalidVariants(string body)
    {
        AssertSingleDiagnostic(Map(body), expectedID: "TADNCM017");
    }

    [Fact]
    public void ReportsVariantWithoutOrderAndVariantAtTheRoot()
    {
        AssertSingleDiagnostic(
            Map("""<Surface ID="Flyout"><Variant ID="Wide" /><Leaf ID="Close" Kind="Command" /></Surface>"""),
            expectedID: "TADNCM011");
        AssertSingleDiagnostic(Map("""<Variant ID="Wide" Order="Flyout" />"""), expectedID: "TADNCM002");
    }

    [Fact]
    public void AnalyzerReportsRequiredNodesThatNoCodeTags()
    {
        const string map =
            MapHeader +
            """
                <Template ID="Card">
                    <Leaf ID="Pick" Kind="Button" />
                </Template>
                <Surface ID="Flyout" Kind="Popup">
                    <Leaf ID="Close" Kind="Command" Keys="Escape" />
                    <Leaf ID="Drag" Kind="Handle" />
                    <Leaf ID="Zoom" Kind="Command" Pointer="Ctrl+Wheel" />
                    <Scope ID="Header" Tab="Once" Arrows="Horizontal">
                        <Leaf ID="Settings" Kind="Button" />
                        <Leaf ID="Undock" Kind="Button" IsTabStop="False" />
                    </Scope>
                    <Scope ID="Body">
                        <Leaf ID="Volume" Kind="Slider" />
                    </Scope>
                    <Scope ID="Single" Template="Card" />
                </Surface>
                <Surface ID="Theme">
                    <Scope ID="TextColor" Template="ColorCard" />
                    <Scope ID="BackgroundColor" Template="ColorCard" />
                </Surface>
                <Template ID="ColorCard">
                    <Leaf ID="Reset" Kind="Button" />
                </Template>
                <Surface ID="TrayIcon" Kind="Tray">
                    <Leaf ID="OpenMenu" Kind="Command" Keys="Shift+F10;Apps" Pointer="RightClick" />
                </Surface>
                <Surface ID="Hotkeys" Kind="Global">
                    <Leaf ID="ShowWindow" Kind="Command" Keys="Ctrl+Shift+Escape" />
                </Surface>
            """ +
            MapFooter;
        const string tagging =
            """
            namespace Samples.UI
            {
                public static class Tagging
                {
                    public static object[] Tags() =>
                    [
                        ControlMap.Flyout.ID,
                        ControlMap.Flyout.Header.Settings,
                        ControlMap.Theme.TextColor.ID,
                        ControlMap.ColorCard.Reset
                    ];
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = ControlMapGeneratorHost.RunAnalyzer(
            [new ControlMapTestFile(Path: "UI/ControlMap.axaml", map)],
            tagging);

        List<string> untagged = diagnostics
            .Where(static diagnostic => diagnostic.Id == "TADNCM100")
            .Select(static diagnostic => diagnostic.GetMessage())
            .ToList();
        Assert.Equal(expected: 6, untagged.Count);
        Assert.Contains(untagged, static message => message.Contains("'Flyout.Close'"));
        Assert.Contains(untagged, static message => message.Contains("'Flyout.Header'"));
        Assert.Contains(untagged, static message => message.Contains("'Flyout.Body.Volume'"));
        Assert.Contains(untagged, static message => message.Contains("'Card.Pick'"));
        Assert.Contains(untagged, static message => message.Contains("'Theme'"));
        Assert.Contains(untagged, static message => message.Contains("'Theme.BackgroundColor'"));
    }

    [Fact]
    public void AnalyzerKeepsSameNamedMapsInOtherNamespacesApart()
    {
        const string flyout = """<Surface ID="Flyout" Kind="Popup"><Leaf ID="Close" Kind="Button" /></Surface>""";
        string otherMap = Map(flyout).Replace("Samples.UI.ControlMap", "Other.UI.ControlMap");
        const string tagging =
            """
            namespace Other.UI
            {
                public sealed partial class ControlMap : TrayAppDotNETCommon.UI.ControlMapping.ControlMap
                {
                    public ControlMap() : base(MapName) { }
                }

                public static class Tagging
                {
                    public static object[] Tags() => [ControlMap.Flyout.ID, ControlMap.Flyout.Close];
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = ControlMapGeneratorHost.RunAnalyzer(
        [
            new ControlMapTestFile(Path: "UI/ControlMap.axaml", Map(flyout)),
            new ControlMapTestFile(Path: "Other/UI/ControlMap.axaml", otherMap)
        ], tagging);

        // Tagging Other.UI.ControlMap leaves the same paths in Samples.UI.ControlMap untagged
        List<Diagnostic> untagged = diagnostics.Where(static diagnostic => diagnostic.Id == "TADNCM100").ToList();
        Assert.Equal(expected: 2, untagged.Count);
        Assert.All(untagged, static diagnostic =>
            Assert.Equal(expected: "UI/ControlMap.axaml", diagnostic.Location.GetLineSpan().Path));
    }

    [Fact]
    public void SummariesCarryKindAndEscapedDescription()
    {
        ControlMapGeneratorResult result = ControlMapGeneratorHost.Run(
            [new ControlMapTestFile(Path: "UI/ControlMap.axaml", ValidMap)]);

        string generated = result.GeneratedSource();
        Assert.Contains(expectedSubstring: "/// <summary>Button leaf: Opens &lt;settings&gt;</summary>", generated);
        Assert.Contains(expectedSubstring: "/// <summary>Popup surface</summary>", generated);

        // The owner declares accessibility, base type, and constructor
        Assert.Contains(expectedSubstring: "    partial class ControlMap" + Environment.NewLine, generated);
        Assert.DoesNotContain(expectedSubstring: "AvaloniaXamlLoader", generated);
    }

    [Fact]
    public void IgnoresAXAMLThatIsNotAControlMap()
    {
        const string resources =
            """
            <ResourceDictionary x:Class="Samples.SampleResources" xmlns="https://github.com/avaloniaui"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
            """;

        ControlMapGeneratorResult result = ControlMapGeneratorHost.Run(
            [new ControlMapTestFile(Path: "SampleResources.axaml", resources)]);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.RunResult.GeneratedTrees);
    }

    [Fact]
    public void ResolvesTemplatesFromReferenceMapsWithoutGeneratingThem()
    {
        const string commonMap =
            """
            <ControlMap x:Class="Common.UI.ControlMap" xmlns="using:TrayAppDotNETCommon.UI.ControlMapping"
                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Template ID="SettingsShell">
                    <Leaf ID="Search" Kind="Text" />
                    <Slot />
                </Template>
            </ControlMap>
            """;
        string appMap = Map(
            """
            <Surface ID="Settings" Template="SettingsShell">
                <Scope ID="GeneralPage">
                    <Leaf ID="RunOnStartup" Kind="Toggle" />
                </Scope>
            </Surface>
            """);

        ControlMapGeneratorResult result = ControlMapGeneratorHost.Run(
        [
            new ControlMapTestFile(Path: "Common/UI/ControlMap.axaml", commonMap, IsReference: true),
            new ControlMapTestFile(Path: "UI/ControlMap.axaml", appMap)
        ]);

        Assert.Empty(result.Diagnostics);
        string generated = result.GeneratedSource();
        Assert.Contains(expectedSubstring: "\"Settings.GeneralPage.RunOnStartup\"", generated);
        Assert.Single(result.RunResult.GeneratedTrees);
        Assert.DoesNotContain(expectedSubstring: "namespace Common", generated);
    }

    [Fact]
    public void ReportsUnknownTemplateWhenNoReferenceMapIsVisible()
    {
        AssertSingleDiagnostic(
            Map("""<Surface ID="Settings" Template="SettingsShell" />"""),
            expectedID: "TADNCM009");
    }

    [Fact]
    public void ReportsMissingClass()
    {
        const string map =
            """
            <ControlMap xmlns="using:TrayAppDotNETCommon.UI.ControlMapping">
                <Surface ID="Flyout"><Leaf ID="Close" Kind="Command" /></Surface>
            </ControlMap>
            """;

        AssertSingleDiagnostic(map, expectedID: "TADNCM011");
    }

    [Theory]
    [InlineData("Samples.UI.OrphanControlMap")]
    [InlineData("Samples.UI.UnrelatedControlMap")]
    public void ReportsOwnerThatIsMissingOrNotAControlMap(string className)
    {
        string map = Map("""<Surface ID="Flyout"><Leaf ID="Close" Kind="Command" /></Surface>""")
            .Replace("Samples.UI.ControlMap", className);

        AssertSingleDiagnostic(map, expectedID: "TADNCM016");
    }

    [Fact]
    public void ReportsMalformedXML()
    {
        AssertSingleDiagnostic(Map("""<Surface ID="Flyout">"""), expectedID: "TADNCM001");
    }

    [Theory]
    [InlineData("""<Leaf ID="Stray" Kind="Button" />""")]
    [InlineData("""<Surface ID="Outer"><Surface ID="Inner"><Leaf ID="Close" Kind="Command" /></Surface></Surface>""")]
    [InlineData("""<Surface ID="Flyout"><Slot /><Leaf ID="Close" Kind="Command" /></Surface>""")]
    [InlineData("""<Surface ID="Flyout"><Button ID="Close" /><Leaf ID="Hide" Kind="Command" /></Surface>""")]
    public void ReportsMisplacedOrUnknownElements(string body)
    {
        AssertSingleDiagnostic(Map(body), expectedID: "TADNCM002");
    }

    [Fact]
    public void ReportsUnknownAttribute()
    {
        AssertSingleDiagnostic(
            Map("""<Surface ID="Flyout"><Leaf ID="Close" Kind="Command" Arrows="Vertical" /></Surface>"""),
            expectedID: "TADNCM003");
    }

    [Theory]
    [InlineData("close")]
    [InlineData("ID")]
    [InlineData("Close_Button")]
    [InlineData("Flyout")]
    public void ReportsInvalidIDs(string id)
    {
        AssertSingleDiagnostic(
            Map($"""<Surface ID="Flyout"><Leaf ID="{id}" Kind="Command" /></Surface>"""),
            expectedID: "TADNCM004");
    }

    [Theory]
    [InlineData("MapName")]
    [InlineData("ControlMap")]
    public void ReportsRootIDThatCollidesWithGeneratedMembers(string id)
    {
        AssertSingleDiagnostic(
            Map($"""<Surface ID="{id}"><Leaf ID="Close" Kind="Command" /></Surface>"""),
            expectedID: "TADNCM004");
    }

    [Fact]
    public void ReportsDuplicateSiblingIDs()
    {
        AssertSingleDiagnostic(
            Map("""<Surface ID="Flyout"><Leaf ID="Close" Kind="Command" /><Leaf ID="Close" Kind="Button" /></Surface>"""),
            expectedID: "TADNCM005");
    }

    [Theory]
    [InlineData("""<Surface ID="Flyout" Kind="Dialog"><Leaf ID="Close" Kind="Command" /></Surface>""")]
    [InlineData("""<Surface ID="Flyout"><Leaf ID="Close" Kind="Link" /></Surface>""")]
    [InlineData("""<Surface ID="Flyout"><Scope ID="Row" Arrows="Diagonal"><Leaf ID="Close" Kind="Command" /></Scope></Surface>""")]
    [InlineData("""<Surface ID="Flyout"><Leaf ID="Close" Kind="Command" IsTabStop="Maybe" /></Surface>""")]
    [InlineData("""<Surface ID="Flyout" Tab="cycle"><Leaf ID="Close" Kind="Command" /></Surface>""")]
    public void ReportsInvalidValues(string body)
    {
        AssertSingleDiagnostic(Map(body), expectedID: "TADNCM006");
    }

    [Theory]
    [InlineData("Ctrl+1")]
    [InlineData("Esc")]
    [InlineData("Shift+Ctrl+F")]
    [InlineData("Ctrl++")]
    [InlineData("Ctrl + F")]
    [InlineData("Control+F")]
    [InlineData(";")]
    public void ReportsInvalidKeyGestures(string keys)
    {
        AssertSingleDiagnostic(
            Map($"""<Surface ID="Flyout"><Leaf ID="Close" Kind="Command" Keys="{keys}" /></Surface>"""),
            expectedID: "TADNCM007");
    }

    [Theory]
    [InlineData("Ctrl+Scroll")]
    [InlineData("Win+Click")]
    [InlineData("Click;Tap")]
    public void ReportsInvalidPointerGestures(string pointer)
    {
        AssertSingleDiagnostic(
            Map($"""<Surface ID="Flyout"><Leaf ID="Close" Kind="Command" Pointer="{pointer}" /></Surface>"""),
            expectedID: "TADNCM007");
    }

    [Theory]
    [InlineData("""<Surface ID="Flyout"><Scope ID="Header"><Leaf ID="Close" Kind="Command" Keys="Escape" KeyScope="Footer" /></Scope></Surface>""")]
    [InlineData("""<Surface ID="Flyout"><Leaf ID="Close" Kind="Command" KeyScope="Flyout" /></Surface>""")]
    public void ReportsInvalidKeyScopes(string body)
    {
        AssertSingleDiagnostic(Map(body), expectedID: "TADNCM008");
    }

    [Theory]
    [InlineData("""<Template ID="Shell"><Leaf ID="Close" Kind="Command" /></Template><Surface ID="Flyout" Template="Shell"><Leaf ID="Hide" Kind="Command" /></Surface>""")]
    [InlineData("""<Template ID="Shell"><Slot /><Scope ID="Host"><Slot /></Scope></Template>""")]
    [InlineData("""<Template ID="Shell"><Slot ID="Actions" /><Scope ID="Host"><Slot ID="Actions" /></Scope></Template>""")]
    [InlineData("""<Template ID="Shell"><Slot /></Template><Surface ID="Flyout" Template="Shell"><Leaf ID="Hide" Kind="Command" Slot="Actions" /></Surface>""")]
    [InlineData("""<Surface ID="Flyout"><Leaf ID="Hide" Kind="Command" Slot="Actions" /></Surface>""")]
    public void ReportsSlotMisuse(string body)
    {
        AssertSingleDiagnostic(Map(body), expectedID: "TADNCM010");
    }

    [Fact]
    public void RoutesChildrenToNamedAndUnnamedSlots()
    {
        ControlMapGeneratorResult result = ControlMapGeneratorHost.Run(
        [
            new ControlMapTestFile(Path: "UI/ControlMap.axaml", Map(
                """
                <Template ID="Page">
                    <Scope ID="Actions"><Leaf ID="Run" Kind="Button" /><Slot ID="Actions" /></Scope>
                    <Slot />
                </Template>
                <Surface ID="Main">
                    <Scope ID="Services" Template="Page">
                        <Leaf ID="Start" Kind="Button" Slot="Actions" />
                        <Leaf ID="Notes" Kind="Text" />
                    </Scope>
                </Surface>
                """))
        ]);

        Assert.Empty(result.Diagnostics);
        string generated = result.GeneratedSource();
        Assert.Contains(expectedSubstring: "\"Main.Services.Start\"", generated);
        Assert.Contains(expectedSubstring: "\"Page.Actions.Run\"", generated);
    }

    [Fact]
    public void ReportsInvalidSlotName()
    {
        AssertSingleDiagnostic(
            Map("""<Template ID="Shell"><Leaf ID="Close" Kind="Command" /><Slot ID="actions" /></Template>"""),
            expectedID: "TADNCM004");
    }

    [Fact]
    public void ReportsLeafWithoutKind()
    {
        AssertSingleDiagnostic(
            Map("""<Surface ID="Flyout"><Leaf ID="Close" /></Surface>"""),
            expectedID: "TADNCM011");
    }

    [Fact]
    public void ReportsEnterEntryOnUnfocusableScope()
    {
        AssertSingleDiagnostic(
            Map("""<Surface ID="Flyout"><Scope ID="Card" Entry="Enter"><Leaf ID="Close" Kind="Button" /></Scope></Surface>"""),
            expectedID: "TADNCM012");
    }

    [Theory]
    [InlineData("Escape", "Escape")]
    [InlineData("Return", "Enter")]
    [InlineData("Ctrl+PageUp;F5", "Ctrl+Prior")]
    public void ReportsAcceleratorConflictsInOneKeyScope(string firstKeys, string secondKeys)
    {
        AssertSingleDiagnostic(
            Map($"""
                 <Surface ID="Flyout">
                     <Leaf ID="Close" Kind="Command" Keys="{firstKeys}" />
                     <Scope ID="Header">
                         <Leaf ID="Hide" Kind="Button" Keys="{secondKeys}" KeyScope="Flyout" />
                     </Scope>
                 </Surface>
                 """),
            expectedID: "TADNCM013");
    }

    [Fact]
    public void AllowsOneGestureInDifferentKeyScopesAndInFocusKeys()
    {
        ControlMapGeneratorResult result = ControlMapGeneratorHost.Run(
        [
            new ControlMapTestFile(Path: "UI/ControlMap.axaml", Map(
                """
                <Surface ID="Flyout">
                    <Leaf ID="Close" Kind="Command" Keys="Escape" FocusKeys="Escape" />
                    <Scope ID="Search">
                        <Leaf ID="Clear" Kind="Command" Keys="Escape" />
                        <Leaf ID="Query" Kind="Text" FocusKeys="Escape" />
                    </Scope>
                </Surface>
                """))
        ]);

        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("""<Surface ID="Flyout"><Scope ID="Header" /></Surface>""")]
    [InlineData("""<Template ID="Shell" />""")]
    [InlineData("""<Surface ID="Flyout" />""")]
    public void ReportsEmptyContainers(string body)
    {
        AssertSingleDiagnostic(Map(body), expectedID: "TADNCM014");
    }

    [Fact]
    public void ReportsRecursiveTemplates()
    {
        ControlMapGeneratorResult result = ControlMapGeneratorHost.Run(
        [
            new ControlMapTestFile(Path: "UI/ControlMap.axaml", Map(
                """
                <Template ID="Outer"><Scope ID="Child" Template="Inner" /></Template>
                <Template ID="Inner"><Scope ID="Child" Template="Outer" /></Template>
                """))
        ]);

        Assert.Equal(expected: 2, result.Diagnostics.Count(static diagnostic => diagnostic.Id == "TADNCM015"));
        Assert.Empty(result.RunResult.GeneratedTrees);
    }

    [Fact]
    public void DiagnosticsPointAtTheOffendingLine()
    {
        ControlMapGeneratorResult result = ControlMapGeneratorHost.Run(
        [
            new ControlMapTestFile(Path: "UI/ControlMap.axaml", Map(
                """
                <Surface ID="Flyout">
                    <Leaf ID="Close" Kind="Command" Keys="Ctrl+1" />
                </Surface>
                """))
        ]);

        Diagnostic diagnostic = Assert.Single(result.Diagnostics);
        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        Assert.Equal(expected: "UI/ControlMap.axaml", span.Path);
        Assert.Equal(expected: 6, span.StartLinePosition.Line + 1);
    }

    private static string Map(string body) => MapHeader + Environment.NewLine + body + Environment.NewLine + MapFooter;

    private static void AssertSingleDiagnostic(string map, string expectedID)
    {
        ControlMapGeneratorResult result = ControlMapGeneratorHost.Run(
            [new ControlMapTestFile(Path: "UI/ControlMap.axaml", map)]);

        Diagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(expectedID, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.RunResult.GeneratedTrees);
    }

    private static void AssertNodeID(Assembly assembly, string typeName, string member, string expectedPath)
    {
        Type type = assembly.GetType(typeName, throwOnError: true)!;
        object? value = type.GetField(member, BindingFlags.Public | BindingFlags.Static)!.GetValue(obj: null);
        Assert.NotNull(value);
        Assert.Equal(expected: "Samples.UI.ControlMap", value!.GetType().GetProperty("Map")!.GetValue(value));
        Assert.Equal(expectedPath, value.GetType().GetProperty("Path")!.GetValue(value));
    }

    private static class ControlMapGeneratorHost
    {
        private static readonly MetadataReference[] References = CreateReferences();

        public static ControlMapGeneratorResult Run(IReadOnlyList<ControlMapTestFile> files, string extraSource = "")
        {
            CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
            CSharpCompilation compilation = CSharpCompilation.Create(
                $"ControlMapGeneratorTest_{Guid.NewGuid():N}",
                [
                    CSharpSyntaxTree.ParseText(RuntimeStubs, parseOptions),
                    CSharpSyntaxTree.ParseText(extraSource, parseOptions)
                ],
                References,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));

            (List<AdditionalText> additionalTexts, HashSet<string> referencePaths) = AdditionalFilesOf(files);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                [new ControlMapGenerator().AsSourceGenerator()],
                additionalTexts,
                parseOptions,
                new ReferenceMetadataOptionsProvider(referencePaths));
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> generatorDiagnostics);

            return new ControlMapGeneratorResult(outputCompilation, driver.GetRunResult(), generatorDiagnostics);
        }

        // Runs the tag analyzer over the generator's output plus hand-written tagging code
        public static ImmutableArray<Diagnostic> RunAnalyzer(IReadOnlyList<ControlMapTestFile> files, string tagging)
        {
            ControlMapGeneratorResult result = Run(files, tagging);
            Assert.Empty(result.Diagnostics);

            (List<AdditionalText> additionalTexts, HashSet<string> referencePaths) = AdditionalFilesOf(files);
            AnalyzerOptions options = new(
                [..additionalTexts],
                new ReferenceMetadataOptionsProvider(referencePaths));
            CompilationWithAnalyzers analyzed = result.Compilation.WithAnalyzers(
                [new ControlMapTagAnalyzer()],
                options);
            return analyzed.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
        }

        private static (List<AdditionalText> Texts, HashSet<string> ReferencePaths) AdditionalFilesOf(
            IReadOnlyList<ControlMapTestFile> files)
        {
            List<AdditionalText> additionalTexts = [];
            HashSet<string> referencePaths = new(StringComparer.Ordinal);
            foreach (ControlMapTestFile file in files)
            {
                additionalTexts.Add(new StringAdditionalText(file.Path, file.Text));
                if (file.IsReference)
                    referencePaths.Add(file.Path);
            }

            return (additionalTexts, referencePaths);
        }

        public static Assembly CompileAndLoad(ControlMapGeneratorResult result)
        {
            using MemoryStream image = new();
            EmitResult emit = result.Compilation.Emit(image);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));

            image.Position = 0;
            return AssemblyLoadContext.Default.LoadFromStream(image);
        }

        private static MetadataReference[] CreateReferences()
        {
            string trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
                                       ?? throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is not available.");
            List<MetadataReference> references = [];
            foreach (string path in trustedAssemblies.Split(Path.PathSeparator))
            {
                if (path.EndsWith(value: ".dll", StringComparison.OrdinalIgnoreCase))
                    references.Add(MetadataReference.CreateFromFile(path));
            }

            return [..references];
        }
    }

    // Hands the reference metadata to the generator the way MSBuild does for the common map
    private sealed class ReferenceMetadataOptionsProvider(HashSet<string> referencePaths) : AnalyzerConfigOptionsProvider
    {
        private static readonly TestAnalyzerConfigOptions ReferenceOptions = new(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.TrayAppDotNETControlMapReference"] = "true"
        });

        public override AnalyzerConfigOptions GlobalOptions => TestAnalyzerConfigOptions.Empty;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => TestAnalyzerConfigOptions.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
            referencePaths.Contains(textFile.Path) ? ReferenceOptions : TestAnalyzerConfigOptions.Empty;
    }
}

internal sealed record ControlMapTestFile(string Path, string Text, bool IsReference = false);

internal sealed record ControlMapGeneratorResult(
    Compilation Compilation,
    GeneratorDriverRunResult RunResult,
    ImmutableArray<Diagnostic> Diagnostics)
{
    public string GeneratedSource() =>
        string.Join(Environment.NewLine, RunResult.GeneratedTrees.Select(static tree => tree.GetText().ToString()));
}
