using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace TrayAppDotNETCommon.AxamlPropertyLinker;

/// <summary>
/// Adds typed getters for one-dot AXAML resource keys to each x:Class owner, and for every resource dictionary marked
/// with <c>[AxamlResourceStore]</c> the compiled-AXAML constructor, the shared <c>Current</c> instance, the Debug
/// hot-reload store, and the reload event its mode requires.
/// </summary>
[Generator]
public sealed class AxamlPropertyLinkerGenerator : IIncrementalGenerator
{
    internal const string DocumentsTrackingName = "AxamlDocuments";
    internal const string ClassModelsTrackingName = "AxamlClassModels";
    internal const string StoreModelsTrackingName = "AxamlResourceStores";

    private const string GeneratedRuntimeFileName = "AxamlPropertyLinker.Runtime.g.cs";
    private const string GeneratedNamespaceSuffix = ".GeneratedAxaml";
    private const string StoreAttributeMetadataName = "TrayAppDotNETCommon.UI.AxamlResourceStoreAttribute";
    private const string ResourceDictionaryMetadataName = "Avalonia.Controls.ResourceDictionary";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<AxamlDocument> documents = AxamlDocument.CreateProvider(context);

#if DEBUG
        DebugProvenanceGenerator.Initialize(context, documents);
#endif

        IncrementalValuesProvider<AxamlClassModel> classes = documents
            .Select(static (document, _) => ParseClass(document))
            .Where(static model => model != null)
            .Select(static (model, _) => model!)
            .WithTrackingName(ClassModelsTrackingName);

        IncrementalValuesProvider<ResourceStoreModel> stores = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                StoreAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, _) => CreateStoreModel(attributeContext))
            .WithTrackingName(StoreModelsTrackingName);

        IncrementalValueProvider<string> rootNamespace = context.AnalyzerConfigOptionsProvider
            .Select(static (optionsProvider, _) => CSharpNames.ReadRootNamespace(optionsProvider.GlobalOptions));

        context.RegisterSourceOutput(
            classes.Collect().Combine(stores.Collect()).Combine(rootNamespace),
            static (sourceProductionContext, value) =>
                Emit(sourceProductionContext, value.Left.Left, value.Left.Right, value.Right));
    }

    private static void Emit(
        SourceProductionContext context,
        ImmutableArray<AxamlClassModel> classes,
        ImmutableArray<ResourceStoreModel> stores,
        string rootNamespace)
    {
        if (classes.Length == 0 && stores.Length == 0) return;

        List<AxamlClassModel> mergedClasses = MergeClasses(classes, out Dictionary<string, List<string>> pathsByClass);
        Dictionary<string, AxamlClassModel> classesByName = new(StringComparer.Ordinal);
        foreach (AxamlClassModel axamlClass in mergedClasses)
            classesByName[axamlClass.FullName] = axamlClass;

        Dictionary<string, (ResourceStoreModel Store, string SourcePath)> validStores =
            ValidateStores(context, stores, pathsByClass, classesByName);

        string generatedNamespace = CSharpNames.EscapeQualifiedName(rootNamespace + GeneratedNamespaceSuffix);
        HashSet<ResourceKind> usedKinds = [];
        foreach (AxamlClassModel axamlClass in mergedClasses)
        {
            bool hasStore = validStores.TryGetValue(axamlClass.FullName, out (ResourceStoreModel Store, string SourcePath) store);
            if (axamlClass.Groups.Length == 0 && !hasStore) continue;

            foreach (ResourceGroup group in axamlClass.Groups)
            {
                foreach (ResourceEntry resource in group.Resources)
                    usedKinds.Add(resource.Kind);
            }

            context.AddSource(
                CSharpNames.HintName(axamlClass.FullName, ".AxamlPropertyLinker.g.cs"),
                GenerateClass(axamlClass, generatedNamespace, hasStore ? store : null));
        }

        if (usedKinds.Count > 0)
            context.AddSource(GeneratedRuntimeFileName, GenerateRuntime(generatedNamespace, usedKinds));
    }

    private static Dictionary<string, (ResourceStoreModel Store, string SourcePath)> ValidateStores(
        SourceProductionContext context,
        ImmutableArray<ResourceStoreModel> stores,
        Dictionary<string, List<string>> pathsByClass,
        Dictionary<string, AxamlClassModel> classesByName)
    {
        Dictionary<string, (ResourceStoreModel Store, string SourcePath)> validStores = new(StringComparer.Ordinal);
        foreach (ResourceStoreModel store in stores.OrderBy(static value => value.FullName, StringComparer.Ordinal))
        {
            if (store.Problem != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    AxamlResourceStoreDiagnostics.InvalidOwner,
                    store.Location.ToLocation(),
                    store.FullName,
                    store.Problem));
                continue;
            }

            if (!pathsByClass.TryGetValue(store.FullName, out List<string>? paths))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    AxamlResourceStoreDiagnostics.MissingAxaml,
                    store.Location.ToLocation(),
                    store.FullName));
                continue;
            }

            if (paths.Count > 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    AxamlResourceStoreDiagnostics.DuplicateAxaml,
                    store.Location.ToLocation(),
                    store.FullName,
                    string.Join(", ", paths)));
                continue;
            }

            if (validStores.ContainsKey(store.FullName)) continue;

            // The XML error is the real problem; the store still compiles so that only the AXAML is reported
            if (classesByName[store.FullName].ParseError is { } parseError)
            {
                LinePosition position = new(Math.Max(val1: 0, parseError.Line - 1), Math.Max(val1: 0, parseError.Column - 1));
                context.ReportDiagnostic(Diagnostic.Create(
                    AxamlResourceStoreDiagnostics.MalformedAxaml,
                    Location.Create(paths[0], textSpan: default, new LinePositionSpan(position, position)),
                    store.FullName,
                    parseError.Message));
            }

            validStores.Add(store.FullName, (store, paths[0]));
        }

        return validStores;
    }

    private static AxamlClassModel? ParseClass(AxamlDocument axamlDocument)
    {
        // Malformed XML still names its x:Class, so a store can report the parse error rather than a missing file
        if (axamlDocument.ParseError is { } parseError)
        {
            return CSharpNames.TrySplitClassName(parseError.DeclaredClass, out string brokenNamespace, out string brokenClass)
                ? new AxamlClassModel(axamlDocument.Path, brokenNamespace, brokenClass, [], parseError)
                : null;
        }

        XElement? root = axamlDocument.Document?.Root;
        if (root == null) return null;

        string? fullClassName = root.Attribute(XName.Get(localName: "Class", CSharpNames.XamlNamespace))?.Value;
        if (!CSharpNames.TrySplitClassName(fullClassName, out string classNamespace, out string className))
            return null;

        Dictionary<string, ResourceGroupBuilder> groupBuilders = new(StringComparer.Ordinal);
        XName keyAttributeName = XName.Get(localName: "Key", CSharpNames.XamlNamespace);
        foreach (XElement element in root.DescendantsAndSelf())
        {
            XAttribute? keyAttribute = element.Attribute(keyAttributeName);
            if (keyAttribute == null) continue;

            ResourceEntry? entry = CreateResourceEntry(element.Name.LocalName, keyAttribute.Value);
            if (entry == null) continue;

            if (!groupBuilders.TryGetValue(entry.Prefix, out ResourceGroupBuilder? groupBuilder))
            {
                groupBuilder = new ResourceGroupBuilder(entry.Prefix);
                groupBuilders.Add(entry.Prefix, groupBuilder);
            }

            groupBuilder.Add(entry);
        }

        List<ResourceGroup> groups = [];
        foreach (ResourceGroupBuilder groupBuilder in groupBuilders.Values.OrderBy(static group => group.Prefix,
                     StringComparer.Ordinal))
            groups.Add(groupBuilder.Build());

        return new AxamlClassModel(axamlDocument.Path, classNamespace, className, [..groups], ParseError: null);
    }

    private static ResourceEntry? CreateResourceEntry(string elementName, string key)
    {
        int separatorIndex = key.IndexOf('.');
        if (separatorIndex <= 0 || separatorIndex == key.Length - 1)
            return null;

        if (key.IndexOf(value: '.', separatorIndex + 1) >= 0)
            return null;

        string prefix = key.Substring(0, separatorIndex);
        string propertyName = key.Substring(separatorIndex + 1);
        if (!CSharpNames.IsIdentifier(prefix) || !CSharpNames.IsIdentifier(propertyName))
            return null;

        ResourceKind? kind = ResourceKindFromElement(elementName, propertyName);
        return kind == null ? null : new ResourceEntry(prefix, propertyName, kind.Value);
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
            "GlyphDefinition" => ResourceKind.Glyph,
            "ThemeColor" => ResourceKind.ThemeColor,
            "String" => propertyName.EndsWith(value: "Color", StringComparison.Ordinal)
                ? ResourceKind.Color
                : ResourceKind.String,
            _ => null
        };
    }

    private static ResourceStoreModel CreateStoreModel(GeneratorAttributeSyntaxContext context)
    {
        INamedTypeSymbol symbol = (INamedTypeSymbol)context.TargetSymbol;
        string classNamespace = symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : symbol.ContainingNamespace.ToDisplayString();
        string fullName = classNamespace.Length == 0 ? symbol.Name : classNamespace + "." + symbol.Name;
        SourceLocation location = SourceLocation.From(context.TargetNode.GetLocation());

        AttributeData attribute = context.Attributes[0];
        string? mode = null;
        string? label = null;
        if (attribute.ConstructorArguments.Length == 2)
        {
            mode = EnumMemberName(attribute.ConstructorArguments[0]);
            label = attribute.ConstructorArguments[1].Value as string;
        }

        string? problem = null;
        if (symbol.ContainingType != null)
            problem = "must be a top-level class, because an AXAML x:Class cannot name a nested type";
        else if (symbol.IsGenericType)
            problem = "must not be generic, because an AXAML x:Class names a closed type";
        else if (symbol.IsAbstract || symbol.IsStatic)
            problem = "must not be abstract or static, because the store constructs it";
        else if (!IsPartial(symbol))
            problem = "must be partial so the generated part can add its constructor and store";
        else if (!DerivesFrom(symbol, ResourceDictionaryMetadataName))
            problem = "must derive from Avalonia.Controls.ResourceDictionary";
        else if (symbol.InstanceConstructors.Any(static constructor => !constructor.IsImplicitlyDeclared))
            problem = "must not declare a constructor; the generated constructor loads the compiled AXAML";
        else if (mode == null || !Enum.TryParse(mode, out StoreMode _))
            problem = "names an unknown AxamlReloadMode";
        else if (string.IsNullOrWhiteSpace(label))
            problem = "needs a non-empty label for hot-reload log messages";

        return new ResourceStoreModel(fullName, mode ?? string.Empty, label ?? string.Empty, location, problem);
    }

    private static string? EnumMemberName(TypedConstant constant)
    {
        if (constant.Type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType) return null;

        foreach (ISymbol member in enumType.GetMembers())
        {
            if (member is IFieldSymbol { HasConstantValue: true } field && Equals(field.ConstantValue, constant.Value))
                return field.Name;
        }

        return null;
    }

    private static bool IsPartial(INamedTypeSymbol symbol)
    {
        foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is ClassDeclarationSyntax declaration
                && declaration.Modifiers.Any(static modifier => modifier.ValueText == "partial"))
                return true;
        }

        return false;
    }

    private static bool DerivesFrom(INamedTypeSymbol symbol, string baseMetadataName)
    {
        for (INamedTypeSymbol? type = symbol.BaseType; type != null; type = type.BaseType)
        {
            if (string.Equals(type.ToDisplayString(), baseMetadataName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static List<AxamlClassModel> MergeClasses(
        ImmutableArray<AxamlClassModel> classes,
        out Dictionary<string, List<string>> pathsByClass)
    {
        pathsByClass = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        Dictionary<string, AxamlClassBuilder> builders = new(StringComparer.Ordinal);
        foreach (AxamlClassModel axamlClass in classes.OrderBy(static value => value.Path,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (!builders.TryGetValue(axamlClass.FullName, out AxamlClassBuilder? builder))
            {
                builder = new AxamlClassBuilder(axamlClass.Path, axamlClass.Namespace, axamlClass.ClassName);
                builders.Add(axamlClass.FullName, builder);
                pathsByClass.Add(axamlClass.FullName, []);
            }

            pathsByClass[axamlClass.FullName].Add(axamlClass.Path);
            builder.ParseError ??= axamlClass.ParseError;
            foreach (ResourceGroup group in axamlClass.Groups)
                builder.Add(group);
        }

        List<AxamlClassModel> merged = [];
        foreach (AxamlClassBuilder builder in builders.Values
                     .OrderBy(static value => value.Namespace, StringComparer.Ordinal)
                     .ThenBy(static value => value.ClassName, StringComparer.Ordinal))
            merged.Add(builder.Build());

        return merged;
    }

    private static string GenerateClass(
        AxamlClassModel axamlClass,
        string generatedNamespace,
        (ResourceStoreModel Store, string SourcePath)? store)
    {
        StringBuilder builder = new();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.Append("namespace ");
        builder.AppendLine(CSharpNames.EscapeQualifiedName(axamlClass.Namespace));
        builder.AppendLine("{");
        // The hand-written part declares accessibility, modifiers, and the base type
        builder.Append("    partial class ");
        builder.AppendLine(CSharpNames.Escape(axamlClass.ClassName));
        builder.AppendLine("    {");

        if (store != null)
            AppendStore(builder, axamlClass.ClassName, store.Value.Store, store.Value.SourcePath);

        foreach (ResourceGroup group in axamlClass.Groups)
            AppendGroup(builder, axamlClass, group, generatedNamespace);

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendStore(StringBuilder builder, string className, ResourceStoreModel store, string sourcePath)
    {
        StoreMode mode = (StoreMode)Enum.Parse(typeof(StoreMode), store.Mode);
        string type = CSharpNames.Escape(className);
        string label = CSharpNames.StringLiteral(store.Label);
        string storeType = $"global::TrayAppDotNETCommon.UI.AXAMLResourceHotReloadStore<{type}>";
        bool hasOwnEvent = mode is StoreMode.Synchronized or StoreMode.Replaced;

        builder.AppendLine("        /// <summary>Initializes the dictionary from its compiled AXAML.</summary>");
        builder.Append("        public ").Append(type)
            .AppendLine("() => global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);");
        builder.AppendLine();
        builder.AppendLine("#if DEBUG");
        if (hasOwnEvent)
        {
            builder.AppendLine("        /// <summary>Raised on the UI thread after the AXAML source reloads successfully.</summary>");
            builder.AppendLine("        internal static event global::System.Action? ResourcesReloaded;");
            builder.AppendLine();
        }

        builder.Append("        private static readonly ").Append(storeType).AppendLine(" ResourceStore =");
        builder.Append("            ").Append(storeType).AppendLine(".CreateForSource(");
        builder.Append("                ").Append(label).AppendLine(",");
        builder.Append("                static () => new ").Append(type).AppendLine("(),");
        builder.Append("                ").Append(NotifyExpression(mode, label)).AppendLine(",");
        builder.Append("                ").Append(CSharpNames.StringLiteral(sourcePath)).AppendLine(",");
        builder.Append("                ").Append(SynchronizeExpression(mode)).AppendLine(");");
        builder.AppendLine();
        builder.AppendLine("        /// <summary>Gets the latest successfully loaded dictionary.</summary>");
        builder.Append("        internal static ").Append(type).AppendLine(" Current => ResourceStore.Current;");
        builder.AppendLine();
        builder.AppendLine("        /// <summary>Reloads the AXAML source immediately on the calling UI thread.</summary>");
        builder.AppendLine("        internal static void ReloadNow() => ResourceStore.ReloadNow();");

        // Owners of a per-owner dictionary build their own instances, so Release keeps no shared one
        if (mode != StoreMode.CommonPerOwner)
        {
            builder.AppendLine("#else");
            builder.Append("        private static readonly global::System.Lazy<").Append(type)
                .Append("> ResourceStore = new(static () => new ").Append(type).AppendLine("());");
            builder.AppendLine();
            builder.AppendLine("        /// <summary>Gets the shared compiled dictionary.</summary>");
            builder.Append("        internal static ").Append(type).AppendLine(" Current => ResourceStore.Value;");
        }

        builder.AppendLine("#endif");
    }

    private static string NotifyExpression(StoreMode mode, string label) => mode switch
    {
        StoreMode.Common or StoreMode.CommonPerOwner =>
            $"static () => global::TrayAppDotNETCommon.UI.CommonAXAMLHotReload.NotifyResourcesReloaded({label})",
        StoreMode.Synchronized or StoreMode.Replaced =>
            $"static () => global::TrayAppDotNETCommon.UI.AXAMLHotReload.NotifyEach(ResourcesReloaded, {label})",
        StoreMode.GlyphCatalog =>
            $"static () => global::TrayAppDotNETCommon.Visuals.GlyphCatalogHotReload.NotifyResourcesReloaded({label})",
        StoreMode.AppTheme =>
            $"static () => global::TrayAppDotNETCommon.Visuals.AppThemeHotReload.NotifyResourcesReloaded({label})",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, message: null)
    };

    private static string SynchronizeExpression(StoreMode mode) => mode switch
    {
        StoreMode.Common or StoreMode.CommonPerOwner or StoreMode.Synchronized =>
            "global::TrayAppDotNETCommon.UI.AXAMLHotReload.SynchronizeResources",
        StoreMode.AppTheme => "global::TrayAppDotNETCommon.Visuals.AppThemeHotReload.SynchronizeColors",
        StoreMode.Replaced or StoreMode.GlyphCatalog => "synchronizeReload: null",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, message: null)
    };

    private static void AppendGroup(
        StringBuilder builder,
        AxamlClassModel axamlClass,
        ResourceGroup group,
        string generatedNamespace)
    {
        string structName = group.Prefix + "AxamlProperties";
        string accessorName = "Axaml" + group.Prefix;
        string ownerType = CSharpNames.Escape(axamlClass.ClassName);

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
        builder.Append(ownerType);
        builder.AppendLine(" _owner;");
        builder.AppendLine();
        builder.Append("            public ");
        builder.Append(structName);
        builder.Append('(');
        builder.Append(ownerType);
        builder.AppendLine(" owner)");
        builder.AppendLine("            {");
        builder.AppendLine("                _owner = owner;");
        builder.AppendLine("            }");

        foreach (ResourceEntry resource in group.Resources)
            AppendProperty(builder, resource, generatedNamespace);

        builder.AppendLine("        }");
    }

    private static void AppendProperty(StringBuilder builder, ResourceEntry resource, string generatedNamespace)
    {
        builder.AppendLine();
        builder.Append("            public ");
        builder.Append(ReturnType(resource.Kind));
        builder.Append(' ');
        builder.Append(CSharpNames.Escape(resource.PropertyName));
        builder.AppendLine(" =>");
        builder.Append("                global::");
        builder.Append(generatedNamespace);
        builder.Append(".AxamlPropertyLinkerRuntime.");
        builder.Append(ReaderMethod(resource.Kind));
        builder.Append("(_owner, ");
        builder.Append(CSharpNames.StringLiteral(resource.Prefix + "."));
        builder.Append(", ");
        builder.Append(CSharpNames.StringLiteral(resource.PropertyName));
        builder.AppendLine(");");
    }

    private static string GenerateRuntime(string generatedNamespace, HashSet<ResourceKind> usedKinds)
    {
        StringBuilder builder = new();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.Append("namespace ").AppendLine(generatedNamespace);
        builder.AppendLine("{");
        builder.AppendLine("    internal static class AxamlPropertyLinkerRuntime");
        builder.AppendLine("    {");
        builder.Append(
            """
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

            """);

        if (usedKinds.Contains(ResourceKind.Glyph))
        {
            builder.Append(
                """
                        public static global::TrayAppDotNETCommon.Visuals.Glyph Glyph(object owner, string prefix, string name)
                        {
                            if (Resource(owner, prefix, name) is not global::TrayAppDotNETCommon.Visuals.GlyphDefinition definition)
                                throw InvalidType(prefix, name, nameof(global::TrayAppDotNETCommon.Visuals.GlyphDefinition));

                            global::TrayAppDotNETCommon.Visuals.Glyph glyph = definition.ToGlyph();
                            global::TrayAppDotNETCommon.UI.Debugging.DebugUIProvenance.RegisterGlyphResource(glyph, prefix + name);
                            return glyph;
                        }

                """);
        }

        if (usedKinds.Contains(ResourceKind.ThemeColor))
        {
            builder.Append(
                """
                        public static global::TrayAppDotNETCommon.Visuals.ThemeColor ThemeColor(object owner, string prefix, string name) =>
                            Resource(owner, prefix, name) is global::TrayAppDotNETCommon.Visuals.ThemeColor value
                                ? value
                                : throw InvalidType(prefix, name, nameof(global::TrayAppDotNETCommon.Visuals.ThemeColor));

                """);
        }

        builder.Append(
            """
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

            """);
        return builder.ToString();
    }

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
            ResourceKind.Glyph => "global::TrayAppDotNETCommon.Visuals.Glyph",
            ResourceKind.ThemeColor => "global::TrayAppDotNETCommon.Visuals.ThemeColor",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: null)
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
            ResourceKind.Glyph => "Glyph",
            ResourceKind.ThemeColor => "ThemeColor",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: null)
        };
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
        Glyph,
        ThemeColor
    }

    // Mirrors TrayAppDotNETCommon.UI.AxamlReloadMode by member name, so reordering the runtime enum cannot drift
    private enum StoreMode
    {
        Common,
        CommonPerOwner,
        Synchronized,
        Replaced,
        GlyphCatalog,
        AppTheme
    }

    private sealed record AxamlClassModel(
        string Path,
        string Namespace,
        string ClassName,
        EquatableArray<ResourceGroup> Groups,
        AxamlParseError? ParseError)
    {
        public string FullName => Namespace + "." + ClassName;
    }

    private sealed record ResourceStoreModel(
        string FullName,
        string Mode,
        string Label,
        SourceLocation Location,
        string? Problem);

    private sealed class AxamlClassBuilder(string path, string ns, string className)
    {
        private readonly Dictionary<string, ResourceGroupBuilder> _groups = new(StringComparer.Ordinal);

        public readonly string Path = path;
        public readonly string Namespace = ns;
        public readonly string ClassName = className;

        /// <summary>The XML error of a malformed file declaring this class, when one exists.</summary>
        public AxamlParseError? ParseError;

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
            List<ResourceGroup> groups = [];
            foreach (ResourceGroupBuilder group in _groups.Values.OrderBy(static value => value.Prefix,
                         StringComparer.Ordinal))
                groups.Add(group.Build());

            return new AxamlClassModel(Path, Namespace, ClassName, [..groups], ParseError);
        }
    }

    private sealed record ResourceGroup(string Prefix, EquatableArray<ResourceEntry> Resources);

    private sealed class ResourceGroupBuilder(string prefix)
    {
        private readonly Dictionary<string, ResourceEntry> _resources = new(StringComparer.Ordinal);

        public readonly string Prefix = prefix;

        public void Add(ResourceEntry resource)
        {
            if (_resources.TryGetValue(resource.PropertyName, out ResourceEntry? existing) &&
                existing.Kind != resource.Kind)
                return;

            _resources[resource.PropertyName] = resource;
        }

        public ResourceGroup Build()
        {
            List<ResourceEntry> resources = [];
            foreach (ResourceEntry resource in _resources.Values.OrderBy(static value => value.PropertyName,
                         StringComparer.Ordinal))
                resources.Add(resource);

            return new ResourceGroup(Prefix, [..resources]);
        }
    }

    private sealed record ResourceEntry(string Prefix, string PropertyName, ResourceKind Kind);
}

/// <summary>Errors reported against <c>[AxamlResourceStore]</c> declarations.</summary>
internal static class AxamlResourceStoreDiagnostics
{
    private const string Category = "TrayAppDotNET.AxamlResourceStore";

    public static readonly DiagnosticDescriptor MissingAxaml = new(
        id: "TADNAX001",
        title: "AXAML resource store has no AXAML file",
        messageFormat: "[AxamlResourceStore] class '{0}' has no AXAML AdditionalFile whose x:Class names it",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateAxaml = new(
        id: "TADNAX002",
        title: "AXAML resource store has several AXAML files",
        messageFormat: "[AxamlResourceStore] class '{0}' is the x:Class of more than one AXAML file: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidOwner = new(
        id: "TADNAX003",
        title: "Invalid AXAML resource store class",
        messageFormat: "[AxamlResourceStore] class '{0}' {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MalformedAxaml = new(
        id: "TADNAX004",
        title: "AXAML resource store file is not well-formed XML",
        messageFormat: "The AXAML of [AxamlResourceStore] class '{0}' is not well-formed XML: {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
