using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace TrayAppDotNETCommon.AxamlPropertyLinker;

/// <summary>One attribute of a control map element with its source position.</summary>
internal sealed class ControlMapAttribute(string name, string value, int line, int column)
{
    public readonly string Name = name;
    public readonly string Value = value;
    public readonly int Line = line;
    public readonly int Column = column;
}

/// <summary>One element of a control map document with its attributes, children, and source position.</summary>
internal sealed class ControlMapElement(string name, int line, int column, ControlMapElement? parent)
{
    public const string IDAttribute = "ID";

    public readonly string Name = name;
    public readonly int Line = line;
    public readonly int Column = column;
    public readonly ControlMapElement? Parent = parent;
    public readonly List<ControlMapAttribute> Attributes = [];
    public readonly List<ControlMapElement> Children = [];

    /// <summary>Gets the ID attribute value, or an empty string when the element has none.</summary>
    public string ID => GetValue(IDAttribute) ?? string.Empty;

    /// <summary>Returns the attribute with the given name, or null.</summary>
    public ControlMapAttribute? GetAttribute(string attributeName)
    {
        foreach (ControlMapAttribute attribute in Attributes)
        {
            if (string.Equals(attribute.Name, attributeName, StringComparison.Ordinal))
                return attribute;
        }

        return null;
    }

    /// <summary>Returns the value of the attribute with the given name, or null.</summary>
    public string? GetValue(string attributeName) => GetAttribute(attributeName)?.Value;
}

/// <summary>A parsed control map AXAML file.</summary>
internal sealed class ControlMapDocument(string path, bool isReference)
{
    public readonly string Path = path;

    // Reference documents only supply templates to the maps compiled by this project
    public readonly bool IsReference = isReference;

    public readonly List<ControlMapElement> RootElements = [];
    public readonly List<Diagnostic> Diagnostics = [];
    public string ClassNamespace = string.Empty;
    public string ClassName = string.Empty;
    public int RootLine;
    public int RootColumn;

    /// <summary>Gets whether the root carried a usable x:Class.</summary>
    public bool HasClass => ClassName.Length > 0;

    /// <summary>
    /// Gets the namespace-qualified x:Class, which is also the map name node ids carry. Every map is named ControlMap
    /// by convention, so only the namespace tells two maps apart.
    /// </summary>
    public string FullClassName => ClassNamespace + "." + ClassName;
}

/// <summary>Reads control map AXAML into <see cref="ControlMapDocument"/> trees.</summary>
internal static class ControlMapParser
{
    public const string ControlMapNamespace = "using:TrayAppDotNETCommon.UI.ControlMapping";
    public const string RootElementName = "ControlMap";
    private const string XAMLNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string ClassAttribute = "Class";

    /// <summary>Parses a control map file, or returns null when the file is not a control map.</summary>
    public static ControlMapDocument? Parse(AdditionalText text, bool isReference, CancellationToken cancellationToken)
    {
        SourceText? sourceText = text.GetText(cancellationToken);
        if (sourceText == null) return null;

        // Every control map declares the schema namespace, so other AXAML is skipped without an XML parse
        string content = sourceText.ToString();
        if (content.IndexOf(ControlMapNamespace, StringComparison.Ordinal) < 0) return null;

        ControlMapDocument document = new(text.Path, isReference);
        XDocument xml;
        try
        {
            xml = XDocument.Parse(content, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            document.Diagnostics.Add(ControlMapDiagnostics.At(
                ControlMapDiagnostics.InvalidDocument,
                text.Path,
                exception.LineNumber,
                exception.LinePosition,
                exception.Message));
            return document;
        }

        XElement? root = xml.Root;
        if (root == null
            || !string.Equals(root.Name.LocalName, RootElementName, StringComparison.Ordinal)
            || !string.Equals(root.Name.NamespaceName, ControlMapNamespace, StringComparison.Ordinal))
            return null;

        (document.RootLine, document.RootColumn) = Position(root);
        ReadRoot(document, root);
        ReadElements(document, root);
        return document;
    }

    private static void ReadRoot(ControlMapDocument document, XElement root)
    {
        bool hasClassAttribute = false;
        foreach (XAttribute attribute in root.Attributes())
        {
            if (attribute.IsNamespaceDeclaration) continue;

            (int line, int column) = Position(attribute);
            bool isClass = string.Equals(attribute.Name.NamespaceName, XAMLNamespace, StringComparison.Ordinal)
                           && string.Equals(attribute.Name.LocalName, ClassAttribute, StringComparison.Ordinal);
            if (!isClass)
            {
                document.Diagnostics.Add(ControlMapDiagnostics.At(
                    ControlMapDiagnostics.UnknownAttribute,
                    document.Path,
                    line,
                    column,
                    attribute.Name.LocalName,
                    RootElementName,
                    "x:Class"));
                continue;
            }

            hasClassAttribute = true;
            string fullClassName = attribute.Value.Trim();
            int separatorIndex = fullClassName.LastIndexOf('.');
            string classNamespace = separatorIndex > 0 ? fullClassName.Substring(0, separatorIndex) : string.Empty;
            string className = separatorIndex > 0 ? fullClassName.Substring(separatorIndex + 1) : string.Empty;
            if (!IsQualifiedName(classNamespace) || !IsIdentifier(className))
            {
                document.Diagnostics.Add(ControlMapDiagnostics.At(
                    ControlMapDiagnostics.InvalidDocument,
                    document.Path,
                    line,
                    column,
                    $"x:Class '{fullClassName}' must be a namespace-qualified class name"));
                continue;
            }

            document.ClassNamespace = classNamespace;
            document.ClassName = className;
        }

        if (hasClassAttribute) return;

        document.Diagnostics.Add(ControlMapDiagnostics.At(
            ControlMapDiagnostics.MissingAttribute,
            document.Path,
            document.RootLine,
            document.RootColumn,
            RootElementName,
            "x:Class"));
    }

    // Converts the XML tree iteratively, keeping each element's parent and source position
    private static void ReadElements(ControlMapDocument document, XElement root)
    {
        Stack<(XElement Source, ControlMapElement? Parent)> pending = new();
        PushChildren(pending, root, parent: null);

        while (pending.Count > 0)
        {
            (XElement source, ControlMapElement? parent) = pending.Pop();
            (int line, int column) = Position(source);
            if (!string.Equals(source.Name.NamespaceName, ControlMapNamespace, StringComparison.Ordinal))
            {
                document.Diagnostics.Add(ControlMapDiagnostics.At(
                    ControlMapDiagnostics.UnexpectedElement,
                    document.Path,
                    line,
                    column,
                    source.Name.ToString(),
                    "outside the control map namespace"));
                continue;
            }

            ControlMapElement element = new(source.Name.LocalName, line, column, parent);
            foreach (XAttribute attribute in source.Attributes())
            {
                if (attribute.IsNamespaceDeclaration) continue;

                (int attributeLine, int attributeColumn) = Position(attribute);
                string attributeName = attribute.Name.NamespaceName.Length == 0
                    ? attribute.Name.LocalName
                    : attribute.Name.ToString();
                element.Attributes.Add(new ControlMapAttribute(
                    attributeName,
                    attribute.Value,
                    attributeLine,
                    attributeColumn));
            }

            foreach (XNode node in source.Nodes())
            {
                if (node is not XText textNode || string.IsNullOrWhiteSpace(textNode.Value)) continue;

                document.Diagnostics.Add(ControlMapDiagnostics.At(
                    ControlMapDiagnostics.UnexpectedElement,
                    document.Path,
                    line,
                    column,
                    "text content",
                    $"inside <{element.Name}>"));
                break;
            }

            if (parent == null)
                document.RootElements.Add(element);
            else
                parent.Children.Add(element);

            PushChildren(pending, source, element);
        }
    }

    // Pushes in reverse so elements pop in document order
    private static void PushChildren(
        Stack<(XElement Source, ControlMapElement? Parent)> pending,
        XElement source,
        ControlMapElement? parent)
    {
        List<XElement> children = [..source.Elements()];
        for (int childIndex = children.Count - 1; childIndex >= 0; childIndex--)
            pending.Push((children[childIndex], parent));
    }

    private static (int Line, int Column) Position(IXmlLineInfo lineInfo) =>
        lineInfo.HasLineInfo() ? (lineInfo.LineNumber, lineInfo.LinePosition) : (1, 1);

    private static bool IsQualifiedName(string value)
    {
        if (value.Length == 0) return false;

        foreach (string part in value.Split('.'))
        {
            if (!IsIdentifier(part))
                return false;
        }

        return true;
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0) return false;
        if (value[0] != '_' && !char.IsLetter(value[0])) return false;

        foreach (char character in value)
        {
            if (character != '_' && !char.IsLetterOrDigit(character))
                return false;
        }

        return true;
    }
}
