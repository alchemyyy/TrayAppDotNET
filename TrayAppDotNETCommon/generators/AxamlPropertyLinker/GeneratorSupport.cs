using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace TrayAppDotNETCommon.AxamlPropertyLinker;

/// <summary>
/// An immutable array that compares by content, so incremental pipeline models built from it stay cached when a
/// rerun produces the same values.
/// </summary>
[CollectionBuilder(typeof(EquatableArray), nameof(EquatableArray.Create))]
internal readonly struct EquatableArray<T>(ImmutableArray<T> items) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _items = items;

    public int Length => _items.IsDefault ? 0 : _items.Length;

    public T this[int index] => _items[index];

    public ImmutableArray<T> AsImmutableArray() => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public bool Equals(EquatableArray<T> other) =>
        AsImmutableArray().AsSpan().SequenceEqual(other.AsImmutableArray().AsSpan());

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (T item in AsImmutableArray())
            hash.Add(item);

        return hash.ToHashCode();
    }

    public ImmutableArray<T>.Enumerator GetEnumerator() => AsImmutableArray().GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)AsImmutableArray()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)AsImmutableArray()).GetEnumerator();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}

internal static class EquatableArray
{
    public static EquatableArray<T> Create<T>(ReadOnlySpan<T> items) where T : IEquatable<T> =>
        new(ImmutableArray.Create(items));
}

/// <summary>A source location that compares by value, so pipeline models can carry it without a syntax tree.</summary>
internal readonly record struct SourceLocation(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public static SourceLocation From(Location location)
    {
        FileLinePositionSpan lineSpan = location.GetLineSpan();
        return new SourceLocation(lineSpan.Path, location.SourceSpan, lineSpan.Span);
    }

    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);
}

/// <summary>The XML error that made an AXAML file unreadable, with the x:Class its text still names, if any.</summary>
internal sealed record AxamlParseError(string Message, int Line, int Column, string? DeclaredClass);

/// <summary>One AXAML additional file parsed once and shared by every AXAML pipeline of the generator.</summary>
internal sealed class AxamlDocument(string path, XDocument? document, AxamlParseError? parseError)
{
    private static readonly Regex DeclaredClassPattern = new(
        "\\bx:Class\\s*=\\s*[\"'](?<name>[^\"']+)[\"']",
        RegexOptions.CultureInvariant);

    public readonly string Path = path;

    /// <summary>The parsed document with line information, or null when the XML is malformed.</summary>
    public readonly XDocument? Document = document;

    /// <summary>Why <see cref="Document"/> is null for malformed XML; null when the file parsed.</summary>
    public readonly AxamlParseError? ParseError = parseError;

    public static IncrementalValuesProvider<AxamlDocument> CreateProvider(IncrementalGeneratorInitializationContext context) =>
        context.AdditionalTextsProvider
            .Where(static text => text.Path.EndsWith(value: ".axaml", StringComparison.OrdinalIgnoreCase))
            .Select(static (text, cancellationToken) => Parse(text, cancellationToken))
            .WithTrackingName(AxamlPropertyLinkerGenerator.DocumentsTrackingName);

    private static AxamlDocument Parse(AdditionalText text, CancellationToken cancellationToken)
    {
        SourceText? sourceText = text.GetText(cancellationToken);
        if (sourceText == null) return new AxamlDocument(text.Path, document: null, parseError: null);

        string content = sourceText.ToString();
        try
        {
            return new AxamlDocument(
                text.Path,
                XDocument.Parse(content, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo),
                parseError: null);
        }
        catch (System.Xml.XmlException exception)
        {
            // The x:Class attribute usually survives an error later in the file, so a store can still be matched to
            // its broken AXAML instead of being reported as having none
            Match declaredClass = DeclaredClassPattern.Match(content);
            return new AxamlDocument(
                text.Path,
                document: null,
                new AxamlParseError(
                    exception.Message,
                    exception.LineNumber,
                    exception.LinePosition,
                    declaredClass.Success ? declaredClass.Groups["name"].Value.Trim() : null));
        }
    }
}

/// <summary>C# naming rules shared by every AXAML generator.</summary>
internal static class CSharpNames
{
    public const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string DefaultRootNamespace = "AxamlPropertyLinkerGenerated";

    /// <summary>Reads the project's root namespace, falling back to the project name and then a fixed name.</summary>
    public static string ReadRootNamespace(AnalyzerConfigOptions options)
    {
        if (options.TryGetValue(key: "build_property.RootNamespace", out string? rootNamespace)
            && IsQualifiedName(rootNamespace))
            return rootNamespace;

        if (options.TryGetValue(key: "build_property.MSBuildProjectName", out string? projectName)
            && IsQualifiedName(projectName))
            return projectName;

        return DefaultRootNamespace;
    }

    /// <summary>Splits a namespace-qualified x:Class value into namespace and class name.</summary>
    public static bool TrySplitClassName(string? fullClassName, out string classNamespace, out string className)
    {
        classNamespace = string.Empty;
        className = string.Empty;
        if (fullClassName == null) return false;

        int separatorIndex = fullClassName.LastIndexOf('.');
        if (separatorIndex <= 0 || separatorIndex == fullClassName.Length - 1) return false;

        string candidateNamespace = fullClassName.Substring(0, separatorIndex);
        string candidateClass = fullClassName.Substring(separatorIndex + 1);
        if (!IsQualifiedName(candidateNamespace) || !IsIdentifier(candidateClass)) return false;

        classNamespace = candidateNamespace;
        className = candidateClass;
        return true;
    }

    /// <summary>Gets whether a value is one or more dot-separated identifiers.</summary>
    public static bool IsQualifiedName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        foreach (string part in value!.Split('.'))
        {
            if (!IsIdentifier(part))
                return false;
        }

        return true;
    }

    public static bool IsIdentifier(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        if (!IsIdentifierStart(value![0])) return false;

        for (int index = 1; index < value.Length; index++)
        {
            if (!IsIdentifierPart(value[index]))
                return false;
        }

        return true;
    }

    public static bool IsIdentifierPart(char character) => character == '_' || char.IsLetterOrDigit(character);

    /// <summary>Prefixes a reserved C# keyword with @ so it can name a member.</summary>
    public static string Escape(string identifier) =>
        SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None ? identifier : "@" + identifier;

    /// <summary>Escapes every segment of a dot-separated name.</summary>
    public static string EscapeQualifiedName(string qualifiedName)
    {
        string[] parts = qualifiedName.Split('.');
        for (int index = 0; index < parts.Length; index++)
            parts[index] = Escape(parts[index]);

        return string.Join(".", parts);
    }

    public static string StringLiteral(string value)
    {
        StringBuilder builder = new(value.Length + 2);
        builder.Append('"');
        foreach (char character in value)
        {
            switch (character)
            {
                case '\\':
                    builder.Append(@"\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                    builder.Append(@"\r");
                    break;
                case '\n':
                    builder.Append(@"\n");
                    break;
                case '\t':
                    builder.Append(@"\t");
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>Builds a generated file hint name from a qualified class name.</summary>
    public static string HintName(string fullName, string suffix)
    {
        StringBuilder builder = new(fullName.Length + suffix.Length);
        foreach (char character in fullName)
            builder.Append(IsIdentifierPart(character) ? character : '_');

        builder.Append(suffix);
        return builder.ToString();
    }

    private static bool IsIdentifierStart(char character) => character == '_' || char.IsLetter(character);
}
