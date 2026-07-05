using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TrayAppDotNETCommon.AxamlPropertyLinker;

[Generator]
public sealed class AxamlPropertyLinkerGenerator : IIncrementalGenerator
{
    private const string GeneratedRuntimeFileName = "AxamlPropertyLinker.Runtime.g.cs";
    private const string GeneratedNamespaceSuffix = ".GeneratedAxaml";
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string DefaultRootNamespace = "AxamlPropertyLinkerGenerated";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<AxamlClassModel?> axamlFiles = context.AdditionalTextsProvider
            .Where(static text => text.Path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .Select(static (text, cancellationToken) => ParseAdditionalText(text, cancellationToken));

        IncrementalValueProvider<string> rootNamespace = context.AnalyzerConfigOptionsProvider
            .Select(static (optionsProvider, _) => ReadRootNamespace(optionsProvider));

        IncrementalValueProvider<(ImmutableArray<AxamlClassModel?> Files, string RootNamespace)> source =
            axamlFiles.Collect().Combine(rootNamespace);

        context.RegisterSourceOutput(source, static (sourceProductionContext, value) =>
        {
            List<AxamlClassModel> classes = new();
            foreach (AxamlClassModel? axamlClass in value.Files)
            {
                if (axamlClass == null) continue;
                classes.Add(axamlClass);
            }

            if (classes.Count == 0) return;

            string generatedNamespace = value.RootNamespace + GeneratedNamespaceSuffix;
            sourceProductionContext.AddSource(GeneratedRuntimeFileName, GenerateRuntime(generatedNamespace));

            List<AxamlClassModel> mergedClasses = [.. MergeClasses(classes)];
            sourceProductionContext.AddSource(
                "AxamlPropertyLinker.StaticAccessors.g.cs",
                GenerateStaticAccessors(mergedClasses, generatedNamespace));

            foreach (AxamlClassModel axamlClass in mergedClasses)
            {
                string hintName = HintName(axamlClass);
                sourceProductionContext.AddSource(hintName, GenerateClass(axamlClass, generatedNamespace));
            }
        });
    }

    private static AxamlClassModel? ParseAdditionalText(AdditionalText text, CancellationToken cancellationToken)
    {
        Microsoft.CodeAnalysis.Text.SourceText? sourceText = text.GetText(cancellationToken);
        if (sourceText == null) return null;

        XDocument document;
        try
        {
            document = XDocument.Parse(sourceText.ToString(), LoadOptions.None);
        }
        catch
        {
            return null;
        }

        XElement? root = document.Root;
        if (root == null) return null;

        XName classAttributeName = XName.Get("Class", XamlNamespace);
        string? fullClassName = root.Attribute(classAttributeName)?.Value;
        if (string.IsNullOrWhiteSpace(fullClassName)) return null;
        string classFullName = fullClassName!;

        int classSeparatorIndex = classFullName.LastIndexOf('.');
        if (classSeparatorIndex <= 0 || classSeparatorIndex == classFullName.Length - 1)
            return null;

        string classNamespace = classFullName.Substring(0, classSeparatorIndex);
        string className = classFullName.Substring(classSeparatorIndex + 1);
        if (!IsQualifiedNamespace(classNamespace) || !IsIdentifier(className))
            return null;

        Dictionary<string, ResourceGroupBuilder> groupBuilders = new(StringComparer.Ordinal);
        XName keyAttributeName = XName.Get("Key", XamlNamespace);
        foreach (XElement element in document.Descendants())
        {
            XAttribute? keyAttribute = element.Attribute(keyAttributeName);
            if (keyAttribute == null) continue;

            ResourceEntry? entry = CreateResourceEntry(element.Name.LocalName, keyAttribute.Value);
            if (entry == null) continue;

            if (!groupBuilders.TryGetValue(entry.Prefix, out ResourceGroupBuilder groupBuilder))
            {
                groupBuilder = new ResourceGroupBuilder(entry.Prefix);
                groupBuilders.Add(entry.Prefix, groupBuilder);
            }

            groupBuilder.Add(entry);
        }

        if (groupBuilders.Count == 0) return null;

        List<ResourceGroup> groups = new();
        foreach (ResourceGroupBuilder groupBuilder in groupBuilders.Values.OrderBy(static group => group.Prefix))
            groups.Add(groupBuilder.Build());

        bool isResourceDictionary = string.Equals(root.Name.LocalName, "ResourceDictionary", StringComparison.Ordinal);
        return new AxamlClassModel(
            text.Path,
            classNamespace,
            className,
            isResourceDictionary,
            groups.ToImmutableArray());
    }

    private static ResourceEntry? CreateResourceEntry(string elementName, string key)
    {
        int separatorIndex = key.IndexOf('.');
        if (separatorIndex <= 0 || separatorIndex == key.Length - 1)
            return null;

        if (key.IndexOf('.', separatorIndex + 1) >= 0)
            return null;

        string prefix = key.Substring(0, separatorIndex);
        string propertyName = key.Substring(separatorIndex + 1);
        if (!IsIdentifier(prefix) || !IsIdentifier(propertyName))
            return null;

        ResourceKind? kind = ResourceKindFromElement(elementName, propertyName);
        if (kind == null) return null;

        return new ResourceEntry(prefix, propertyName, kind.Value);
    }

    private static ResourceKind? ResourceKindFromElement(string elementName, string propertyName)
    {
        return elementName switch
        {
            "Double" => ResourceKind.Double,
            "Int32" => ResourceKind.Int,
            "Thickness" => ResourceKind.Thickness,
            "CornerRadius" => ResourceKind.CornerRadius,
            "TranslateTransform" => ResourceKind.TranslateTransform,
            "Color" => ResourceKind.Color,
            "String" => propertyName.EndsWith("Color", StringComparison.Ordinal)
                ? ResourceKind.Color
                : ResourceKind.String,
            _ => null
        };
    }

    private static IEnumerable<AxamlClassModel> MergeClasses(List<AxamlClassModel> classes)
    {
        Dictionary<string, AxamlClassBuilder> builders = new(StringComparer.Ordinal);
        foreach (AxamlClassModel axamlClass in classes.OrderBy(static value => value.Path, StringComparer.OrdinalIgnoreCase))
        {
            string key = axamlClass.Namespace + "." + axamlClass.ClassName;
            if (!builders.TryGetValue(key, out AxamlClassBuilder builder))
            {
                builder = new AxamlClassBuilder(
                    axamlClass.Path,
                    axamlClass.Namespace,
                    axamlClass.ClassName,
                    axamlClass.IsResourceDictionary);
                builders.Add(key, builder);
            }

            foreach (ResourceGroup group in axamlClass.Groups)
                builder.Add(group);
        }

        List<AxamlClassModel> merged = new();
        foreach (AxamlClassBuilder builder in builders.Values.OrderBy(static value => value.Namespace).ThenBy(static value => value.ClassName))
            merged.Add(builder.Build());

        return merged;
    }

    private static string GenerateClass(AxamlClassModel axamlClass, string generatedNamespace)
    {
        StringBuilder builder = new();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.Append("namespace ");
        builder.AppendLine(axamlClass.Namespace);
        builder.AppendLine("{");
        builder.Append("    public partial class ");
        builder.AppendLine(axamlClass.ClassName);
        builder.AppendLine("    {");

        foreach (ResourceGroup group in axamlClass.Groups)
            AppendGroup(builder, axamlClass, group, generatedNamespace);

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string GenerateStaticAccessors(List<AxamlClassModel> axamlClasses, string generatedNamespace)
    {
        Dictionary<string, ResourceGroupBuilder> groupBuilders = new(StringComparer.Ordinal);
        foreach (AxamlClassModel axamlClass in axamlClasses)
        {
            foreach (ResourceGroup group in axamlClass.Groups)
            {
                if (!groupBuilders.TryGetValue(group.Prefix, out ResourceGroupBuilder? groupBuilder))
                {
                    groupBuilder = new ResourceGroupBuilder(group.Prefix);
                    groupBuilders.Add(group.Prefix, groupBuilder);
                }

                foreach (ResourceEntry resource in group.Resources)
                    groupBuilder.Add(resource);
            }
        }

        StringBuilder builder = new();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.Append("namespace ");
        builder.AppendLine(generatedNamespace);
        builder.AppendLine("{");

        foreach (ResourceGroupBuilder groupBuilder in groupBuilders.Values.OrderBy(static value => value.Prefix))
            AppendStaticAccessorClass(builder, groupBuilder.Build());

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendStaticAccessorClass(StringBuilder builder, ResourceGroup group)
    {
        builder.AppendLine();
        builder.Append("    internal static class Axaml");
        builder.AppendLine(group.Prefix);
        builder.AppendLine("    {");

        foreach (ResourceEntry resource in group.Resources.OrderBy(static value => value.PropertyName))
            AppendStaticAccessor(builder, resource);

        builder.AppendLine("    }");
    }

    private static void AppendStaticAccessor(StringBuilder builder, ResourceEntry resource)
    {
        builder.AppendLine();
        builder.Append("        public static ");
        builder.Append(ReturnType(resource.Kind));
        builder.Append(' ');
        builder.Append(resource.PropertyName);
        builder.AppendLine("(object owner) =>");
        builder.Append("            AxamlPropertyLinkerRuntime.");
        builder.Append(ReaderMethod(resource.Kind));
        builder.Append("(owner, ");
        builder.Append(StringLiteral(resource.Prefix + "."));
        builder.Append(", nameof(");
        builder.Append(resource.PropertyName);
        builder.AppendLine("));");
    }

    private static void AppendGroup(
        StringBuilder builder,
        AxamlClassModel axamlClass,
        ResourceGroup group,
        string generatedNamespace)
    {
        string structName = group.Prefix + "AxamlProperties";
        string accessorName = "Axaml" + group.Prefix;

        builder.AppendLine();
        builder.Append("        internal ");
        builder.Append(structName);
        builder.Append(' ');
        builder.Append(accessorName);
        builder.Append(" => new ");
        builder.Append(structName);
        builder.AppendLine("(this);");
        builder.AppendLine();
        builder.Append("        internal readonly struct ");
        builder.AppendLine(structName);
        builder.AppendLine("        {");
        builder.Append("            private readonly ");
        builder.Append(axamlClass.ClassName);
        builder.AppendLine(" _owner;");
        builder.AppendLine();
        builder.Append("            public ");
        builder.Append(structName);
        builder.Append('(');
        builder.Append(axamlClass.ClassName);
        builder.AppendLine(" owner)");
        builder.AppendLine("            {");
        builder.AppendLine("                _owner = owner;");
        builder.AppendLine("            }");

        foreach (ResourceEntry resource in group.Resources.OrderBy(static value => value.PropertyName))
            AppendProperty(builder, resource, generatedNamespace);

        builder.AppendLine("        }");
    }

    private static void AppendProperty(StringBuilder builder, ResourceEntry resource, string generatedNamespace)
    {
        builder.AppendLine();
        builder.Append("            public ");
        builder.Append(ReturnType(resource.Kind));
        builder.Append(' ');
        builder.Append(resource.PropertyName);
        builder.AppendLine(" =>");
        builder.Append("                global::");
        builder.Append(generatedNamespace);
        builder.Append(".AxamlPropertyLinkerRuntime.");
        builder.Append(ReaderMethod(resource.Kind));
        builder.Append("(_owner, ");
        builder.Append(StringLiteral(resource.Prefix + "."));
        builder.Append(", nameof(");
        builder.Append(resource.PropertyName);
        builder.AppendLine("));");
    }

    private static string GenerateRuntime(string generatedNamespace) =>
        $$"""
        // <auto-generated/>
        #nullable enable

        namespace {{generatedNamespace}}
        {
            internal static class AxamlPropertyLinkerRuntime
            {
                public static double Double(object owner, string prefix, string name) =>
                    Resource(owner, prefix, name) switch
                    {
                        double value => value,
                        int value => value,
                        string value => double.Parse(value, global::System.Globalization.CultureInfo.InvariantCulture),
                        object value => global::System.Convert.ToDouble(value, global::System.Globalization.CultureInfo.InvariantCulture),
                    };

                public static int Int(object owner, string prefix, string name) =>
                    (int)global::System.Math.Round(Double(owner, prefix, name));

                public static string String(object owner, string prefix, string name) =>
                    Resource(owner, prefix, name) switch
                    {
                        string value => value,
                        object value => global::System.Convert.ToString(value, global::System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    };

                public static global::Avalonia.Thickness Thickness(object owner, string prefix, string name) =>
                    Resource(owner, prefix, name) is global::Avalonia.Thickness value
                        ? value
                        : throw InvalidType(prefix, name, nameof(global::Avalonia.Thickness));

                public static global::Avalonia.CornerRadius CornerRadius(object owner, string prefix, string name) =>
                    Resource(owner, prefix, name) is global::Avalonia.CornerRadius value
                        ? value
                        : throw InvalidType(prefix, name, nameof(global::Avalonia.CornerRadius));

                public static global::Avalonia.Media.Color Color(object owner, string prefix, string name) =>
                    Resource(owner, prefix, name) switch
                    {
                        global::Avalonia.Media.Color value => value,
                        string value => global::Avalonia.Media.Color.Parse(value),
                        object => throw InvalidType(prefix, name, nameof(global::Avalonia.Media.Color)),
                    };

                public static global::Avalonia.Media.TranslateTransform TranslateTransform(object owner, string prefix, string name)
                {
                    if (Resource(owner, prefix, name) is not global::Avalonia.Media.TranslateTransform value)
                        throw InvalidType(prefix, name, nameof(global::Avalonia.Media.TranslateTransform));

                    return new global::Avalonia.Media.TranslateTransform(value.X, value.Y);
                }

                private static object Resource(object owner, string prefix, string name)
                {
                    string key = prefix + name;
                    if (owner is global::Avalonia.Controls.ResourceDictionary resources)
                    {
                        object? dictionaryValue = resources[key];
                        if (dictionaryValue != null)
                            return dictionaryValue;
                    }

                    if (owner is global::Avalonia.Controls.IResourceNode resourceNode &&
                        resourceNode.TryGetResource(key, null, out object? resourceNodeValue) &&
                        resourceNodeValue != null)
                    {
                        return resourceNodeValue;
                    }

                    throw new global::System.InvalidOperationException($"Missing AXAML resource '{key}'.");
                }

                private static global::System.InvalidOperationException InvalidType(
                    string prefix,
                    string name,
                    string expectedType) =>
                    new($"AXAML resource '{prefix}{name}' is not a {expectedType}.");
            }
        }
        """;

    private static string ReturnType(ResourceKind kind)
    {
        return kind switch
        {
            ResourceKind.Double => "double",
            ResourceKind.Int => "int",
            ResourceKind.String => "string",
            ResourceKind.Thickness => "global::Avalonia.Thickness",
            ResourceKind.CornerRadius => "global::Avalonia.CornerRadius",
            ResourceKind.Color => "global::Avalonia.Media.Color",
            ResourceKind.TranslateTransform => "global::Avalonia.Media.TranslateTransform",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static string ReaderMethod(ResourceKind kind)
    {
        return kind switch
        {
            ResourceKind.Double => "Double",
            ResourceKind.Int => "Int",
            ResourceKind.String => "String",
            ResourceKind.Thickness => "Thickness",
            ResourceKind.CornerRadius => "CornerRadius",
            ResourceKind.Color => "Color",
            ResourceKind.TranslateTransform => "TranslateTransform",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static string HintName(AxamlClassModel axamlClass)
    {
        string fullName = axamlClass.Namespace + "." + axamlClass.ClassName;
        StringBuilder builder = new();
        foreach (char character in fullName)
        {
            builder.Append(IsIdentifierPart(character) ? character : '_');
        }

        builder.Append(".AxamlPropertyLinker.g.cs");
        return builder.ToString();
    }

    private static string ReadRootNamespace(AnalyzerConfigOptionsProvider optionsProvider)
    {
        if (optionsProvider.GlobalOptions.TryGetValue("build_property.RootNamespace", out string? rootNamespace) &&
            !string.IsNullOrWhiteSpace(rootNamespace) &&
            IsQualifiedNamespace(rootNamespace))
        {
            return rootNamespace;
        }

        if (optionsProvider.GlobalOptions.TryGetValue("build_property.MSBuildProjectName", out string? projectName) &&
            !string.IsNullOrWhiteSpace(projectName) &&
            IsIdentifier(projectName))
        {
            return projectName;
        }

        return DefaultRootNamespace;
    }

    private static bool IsQualifiedNamespace(string value)
    {
        string[] parts = value.Split('.');
        if (parts.Length == 0) return false;

        foreach (string part in parts)
        {
            if (!IsIdentifier(part)) return false;
        }

        return true;
    }

    private static bool IsIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!IsIdentifierStart(value[0])) return false;

        for (int index = 1; index < value.Length; index++)
        {
            if (!IsIdentifierPart(value[index])) return false;
        }

        return true;
    }

    private static bool IsIdentifierStart(char character) =>
        character == '_' || char.IsLetter(character);

    private static bool IsIdentifierPart(char character) =>
        character == '_' || char.IsLetterOrDigit(character);

    private static string StringLiteral(string value)
    {
        StringBuilder builder = new();
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

    private enum ResourceKind
    {
        Double,
        Int,
        String,
        Thickness,
        CornerRadius,
        Color,
        TranslateTransform,
    }

    private sealed class AxamlClassModel(
        string path,
        string ns,
        string className,
        bool isResourceDictionary,
        ImmutableArray<ResourceGroup> groups)
    {
        public readonly string Path = path;
        public readonly string Namespace = ns;
        public readonly string ClassName = className;
        public readonly bool IsResourceDictionary = isResourceDictionary;
        public readonly ImmutableArray<ResourceGroup> Groups = groups;
    }

    private sealed class AxamlClassBuilder(string path, string ns, string className, bool isResourceDictionary)
    {
        private readonly Dictionary<string, ResourceGroupBuilder> _groups = new(StringComparer.Ordinal);

        public readonly string Path = path;
        public readonly string Namespace = ns;
        public readonly string ClassName = className;
        public readonly bool IsResourceDictionary = isResourceDictionary;

        public void Add(ResourceGroup group)
        {
            if (!_groups.TryGetValue(group.Prefix, out ResourceGroupBuilder? builder))
            {
                builder = new ResourceGroupBuilder(group.Prefix);
                _groups.Add(group.Prefix, builder);
            }

            foreach (ResourceEntry resource in group.Resources)
                builder.Add(resource);
        }

        public AxamlClassModel Build()
        {
            List<ResourceGroup> groups = new();
            foreach (ResourceGroupBuilder group in _groups.Values.OrderBy(static value => value.Prefix))
                groups.Add(group.Build());

            return new AxamlClassModel(Path, Namespace, ClassName, IsResourceDictionary, groups.ToImmutableArray());
        }
    }

    private sealed class ResourceGroup(string prefix, ImmutableArray<ResourceEntry> resources)
    {
        public readonly string Prefix = prefix;
        public readonly ImmutableArray<ResourceEntry> Resources = resources;
    }

    private sealed class ResourceGroupBuilder(string prefix)
    {
        private readonly Dictionary<string, ResourceEntry> _resources = new(StringComparer.Ordinal);

        public readonly string Prefix = prefix;

        public void Add(ResourceEntry resource)
        {
            if (_resources.TryGetValue(resource.PropertyName, out ResourceEntry? existing) &&
                existing.Kind != resource.Kind)
            {
                return;
            }

            _resources[resource.PropertyName] = resource;
        }

        public ResourceGroup Build()
        {
            List<ResourceEntry> resources = new();
            foreach (ResourceEntry resource in _resources.Values.OrderBy(static value => value.PropertyName))
                resources.Add(resource);

            return new ResourceGroup(Prefix, resources.ToImmutableArray());
        }
    }

    private sealed class ResourceEntry(string prefix, string propertyName, ResourceKind kind)
    {
        public readonly string Prefix = prefix;
        public readonly string PropertyName = propertyName;
        public readonly ResourceKind Kind = kind;
    }
}
