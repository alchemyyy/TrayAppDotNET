using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using TrayAppDotNETCommon.Serialization;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class TrayXmlSourceGeneratorTests
{
    [Fact]
    public void RoundTripsRichModel()
    {
        GeneratedAssembly generated = GeneratorHost.CompileAndLoad(RichModelSource);
        Type settingsType = generated.Assembly.GetRequiredType("GeneratorSamples.Settings");
        Type childType = generated.Assembly.GetRequiredType("GeneratorSamples.Child");
        Type pointType = generated.Assembly.GetRequiredType("GeneratorSamples.Point");
        Type entryType = generated.Assembly.GetRequiredType("GeneratorSamples.Entry");
        Type modeType = generated.Assembly.GetRequiredType("GeneratorSamples.Mode");

        object settings = Activator.CreateInstance(settingsType)!;
        DateTime when = new(year: 2026, month: 6, day: 20, hour: 12, minute: 34, second: 56, DateTimeKind.Utc);

        Set(settings, property: "Name", value: "Main");
        Set(settings, property: "Title", value: "Loaded title");
        Set(settings, property: "Enabled", value: true);
        Set(settings, property: "Count", value: 123);
        Set(settings, property: "Optional", value: 45);
        Set(settings, property: "Mode", Enum.Parse(modeType, value: "Manual"));
        Set(settings, property: "When", when);
        Set(settings, property: "Ignored", value: "do not persist");

        object child = Get(settings, property: "Child")!;
        Set(child, property: "Id", value: "child-1");
        Set(child, property: "Weight", value: 2.5);

        IList tags = (IList)Get(settings, property: "Tags")!;
        tags.Add("alpha");
        tags.Add("beta");

        IList points = (IList)Get(settings, property: "Points")!;
        object point = Activator.CreateInstance(pointType)!;
        Set(point, property: "X", value: 10);
        Set(point, property: "Y", value: 20);
        points.Add(point);

        IList entries = (IList)Get(settings, property: "Entries")!;
        object entry = Activator.CreateInstance(entryType)!;
        Set(entry, property: "Key", value: "one");
        Set(entry, property: "Value", value: 1);
        entries.Add(entry);

        string xml = GeneratorHost.WriteXml(settings);
        Assert.Contains(expectedSubstring: "<Settings", xml);
        Assert.Contains(expectedSubstring: "Name=\"Main\"", xml);
        Assert.Contains(expectedSubstring: "<Title>Loaded title</Title>", xml);
        Assert.Contains(expectedSubstring: "Enabled=\"true\"", xml);
        Assert.Contains(expectedSubstring: "<Tags>", xml);
        Assert.Contains(expectedSubstring: "<Tag>alpha</Tag>", xml);
        Assert.Contains(expectedSubstring: "<Points>", xml);
        Assert.Contains(expectedSubstring: "<P X=\"10\" Y=\"20\"", xml);
        Assert.Contains(expectedSubstring: "<Entry Key=\"one\" Value=\"1\"", xml);
        Assert.DoesNotContain(expectedSubstring: "do not persist", xml);
        Assert.Equal(expected: 1, GetStaticInt(settingsType, field: "Serializing"));

        object copy = GeneratorHost.ReadXml(settingsType, xml);
        Assert.Equal(expected: "Main", Get(copy, property: "Name"));
        Assert.Equal(expected: "Loaded title", Get(copy, property: "Title"));
        Assert.Equal(expected: true, Get(copy, property: "Enabled"));
        Assert.Equal(expected: 123, Get(copy, property: "Count"));
        Assert.Equal(expected: 45, Get(copy, property: "Optional"));
        Assert.Equal(Enum.Parse(modeType, value: "Manual"), Get(copy, property: "Mode"));
        Assert.Equal(when, Get(copy, property: "When"));
        Assert.Equal(expected: "ignored-default", Get(copy, property: "Ignored"));

        object copyChild = Get(copy, property: "Child")!;
        Assert.Equal(expected: "child-1", Get(copyChild, property: "Id"));
        Assert.Equal(expected: 2.5, Get(copyChild, property: "Weight"));

        IList copyTags = (IList)Get(copy, property: "Tags")!;
        Assert.Equal(expected: 2, copyTags.Count);
        Assert.Equal(expected: "alpha", copyTags[0]);
        Assert.Equal(expected: "beta", copyTags[1]);

        IList copyPoints = (IList)Get(copy, property: "Points")!;
        object? copyPoint = Assert.Single(copyPoints.Cast<object?>());
        Assert.Equal(expected: 10, Get(copyPoint!, property: "X"));
        Assert.Equal(expected: 20, Get(copyPoint!, property: "Y"));

        IList copyEntries = (IList)Get(copy, property: "Entries")!;
        object? copyEntry = Assert.Single(copyEntries.Cast<object?>());
        Assert.Equal(expected: "one", Get(copyEntry!, property: "Key"));
        Assert.Equal(expected: 1, Get(copyEntry!, property: "Value"));

        Assert.Equal(expected: 1, GetStaticInt(settingsType, field: "Deserializing"));
        Assert.Equal(expected: 1, GetStaticInt(settingsType, field: "Deserialized"));
    }

    [Fact]
    public void KeepsDefaultsForInvalidXml()
    {
        GeneratedAssembly generated = GeneratorHost.CompileAndLoad(RichModelSource);
        Type settingsType = generated.Assembly.GetRequiredType("GeneratorSamples.Settings");
        Type modeType = generated.Assembly.GetRequiredType("GeneratorSamples.Mode");

        const string xml =
            """
            <Settings Count="not-an-int" Mode="UnknownValue">
              <Title>Loaded</Title>
              <Unknown>skip me</Unknown>
              <Ignored>not applied</Ignored>
              <Tags>
                <Tag>kept</Tag>
                <Wrong>skipped</Wrong>
              </Tags>
            </Settings>
            """;

        object copy = GeneratorHost.ReadXml(settingsType, xml);
        Assert.Equal(expected: "Default", Get(copy, property: "Name"));
        Assert.Equal(expected: "Loaded", Get(copy, property: "Title"));
        Assert.Equal(expected: 7, Get(copy, property: "Count"));
        Assert.Equal(Enum.Parse(modeType, value: "Auto"), Get(copy, property: "Mode"));
        Assert.Equal(expected: "ignored-default", Get(copy, property: "Ignored"));

        IList tags = (IList)Get(copy, property: "Tags")!;
        Assert.Equal(expected: "kept", Assert.Single(tags.Cast<object?>()));
    }

    [Fact]
    public void ReportsUnsupportedMembers()
    {
        GeneratorDriverRunResult result = GeneratorHost.RunGenerator(UnsupportedSource).RunResult;
        ImmutableArray<Diagnostic> diagnostics = result.Diagnostics;
        int unsupportedCount =
            diagnostics.Count(static d => d is { Id: "TAXML001", Severity: DiagnosticSeverity.Error });
        Assert.Equal(expected: 2, unsupportedCount);
    }

    private static object? Get(object target, string property) =>
        target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.GetValue(target);

    private static void Set(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)!.SetValue(target, value);

    private static int GetStaticInt(Type type, string field) =>
        (int)type.GetField(field, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    private const string RichModelSource =
        """
        using System;
        using System.Collections.Generic;
        using System.Xml.Serialization;
        using TrayAppDotNETCommon.Serialization;

        namespace GeneratorSamples;

        public enum Mode
        {
            Auto,
            Manual,
        }

        [XmlRoot("Settings")]
        public sealed partial class Settings : ITrayXmlSerializationCallbacks
        {
            public static int Serializing;
            public static int Deserializing;
            public static int Deserialized;

            [XmlAttribute]
            public string Name { get; set; } = "Default";

            public string Title { get; set; } = "Untitled";

            [XmlAttribute]
            public bool Enabled { get; set; }

            [XmlAttribute]
            public int Count { get; set; } = 7;

            [XmlAttribute]
            public int? Optional { get; set; }

            [XmlAttribute]
            public Mode Mode { get; set; } = Mode.Auto;

            public DateTime When { get; set; } = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

            public Child Child { get; set; } = new();

            [XmlArray("Tags")]
            [XmlArrayItem("Tag")]
            public List<string> Tags { get; set; } = new();

            [XmlArray("Points")]
            [XmlArrayItem("P")]
            public List<Point> Points { get; set; } = new();

            [XmlElement("Entry")]
            public List<Entry> Entries { get; set; } = new();

            [XmlIgnore]
            public string Ignored { get; set; } = "ignored-default";

            public void OnTrayXmlSerializing() => Serializing++;
            public void OnTrayXmlDeserializing() => Deserializing++;
            public void OnTrayXmlDeserialized() => Deserialized++;
        }

        public sealed class Child
        {
            [XmlAttribute]
            public string Id { get; set; } = "";

            [XmlAttribute]
            public double Weight { get; set; }
        }

        public struct Point
        {
            [XmlAttribute]
            public int X { get; set; }

            [XmlAttribute]
            public int Y { get; set; }
        }

        public sealed class Entry
        {
            [XmlAttribute]
            public string Key { get; set; } = "";

            [XmlAttribute]
            public int Value { get; set; }
        }
        """;

    private const string UnsupportedSource =
        """
        using System;
        using System.Collections.Generic;
        using System.Xml.Serialization;

        namespace GeneratorSamples;

        [XmlRoot("Bad")]
        public sealed partial class Bad
        {
            [XmlAttribute]
            public List<int> Values { get; set; } = new();

            public Uri Link { get; set; } = new("https://example.com");

            [XmlIgnore]
            public Uri IgnoredLink { get; set; } = new("https://example.com");
        }
        """;
}

internal static class GeneratorHost
{
    private static readonly MetadataReference[] References = CreateReferences();

    public static GeneratedAssembly CompileAndLoad(string source)
    {
        GeneratorResult result = RunGenerator(source);
        ThrowIfErrors(result.CompilationDiagnostics);

        using MemoryStream pe = new();
        EmitResult emit = result.Compilation.Emit(pe);
        if (!emit.Success)
            ThrowIfErrors(emit.Diagnostics);

        pe.Position = 0;
        Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(pe);
        return new GeneratedAssembly(assembly, result.RunResult);
    }

    public static GeneratorResult RunGenerator(string source)
    {
        string assemblyName = $"TrayXmlGeneratorTest_{Guid.NewGuid():N}";
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new TrayXmlSourceGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        ImmutableArray<Diagnostic> compilationDiagnostics =
        [
            ..outputCompilation
                .GetDiagnostics()
                .AddRange(generatorDiagnostics)
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
        ];

        return new GeneratorResult(outputCompilation, runResult, compilationDiagnostics);
    }

    public static string WriteXml(object value)
    {
        using MemoryStream stream = new();
        TrayXmlSerializer.Write(stream, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static object ReadXml(Type type, string xml)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(xml), writable: false);
        MethodInfo read = typeof(TrayXmlSerializer)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(static method => method is
                { Name: nameof(TrayXmlSerializer.Read), IsGenericMethodDefinition: true });
        return read.MakeGenericMethod(type).Invoke(obj: null, [stream])!;
    }

    private static MetadataReference[] CreateReferences()
    {
        string? tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrEmpty(tpa))
            throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is not available.");

        SortedSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in tpa.Split(Path.PathSeparator))
        {
            if (path.EndsWith(value: ".dll", StringComparison.OrdinalIgnoreCase))
                paths.Add(path);
        }

        paths.Add(typeof(TrayXmlSerializer).Assembly.Location);
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

internal sealed record GeneratorResult(
    Compilation Compilation,
    GeneratorDriverRunResult RunResult,
    ImmutableArray<Diagnostic> CompilationDiagnostics);

internal sealed record GeneratedAssembly(Assembly Assembly, GeneratorDriverRunResult RunResult);

internal static class AssemblyExtensions
{
    public static Type GetRequiredType(this Assembly assembly, string name) =>
        assembly.GetType(name, throwOnError: true)!;
}
