using Microsoft.CodeAnalysis;

namespace TrayAppDotNETCommon.XmlSourceGenerator;

/// <summary>Turns an <c>[XmlRoot]</c> type and everything it reaches into symbol-free <see cref="RootModel"/>s.</summary>
internal static class ModelBuilder
{
    private const string XmlRootAttribute = "System.Xml.Serialization.XmlRootAttribute";
    private const string XmlAttributeAttribute = "System.Xml.Serialization.XmlAttributeAttribute";
    private const string XmlElementAttribute = "System.Xml.Serialization.XmlElementAttribute";
    private const string XmlArrayAttribute = "System.Xml.Serialization.XmlArrayAttribute";
    private const string XmlArrayItemAttribute = "System.Xml.Serialization.XmlArrayItemAttribute";
    private const string XmlIgnoreAttribute = "System.Xml.Serialization.XmlIgnoreAttribute";

    public static RootModel BuildRoot(INamedTypeSymbol root, CancellationToken cancellationToken)
    {
        HashSet<INamedTypeSymbol> seen = new(SymbolEqualityComparer.Default) { root };
        Queue<INamedTypeSymbol> pending = new();
        pending.Enqueue(root);
        List<TypeModel> types = [];
        List<DiagnosticModel> diagnostics = [];

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            INamedTypeSymbol type = pending.Dequeue();
            List<MemberModel> members = [];

            foreach (IPropertySymbol property in SerializableProperties(type))
            {
                if (CreateMember(property, diagnostics) is not { } member) continue;

                members.Add(member.Model);
                if (IsComplex(member.ValueType) && member.ValueType is INamedTypeSymbol named && seen.Add(named))
                    pending.Enqueue(named);
            }

            types.Add(new TypeModel(
                TypeName(type),
                GetRootName(type),
                type.AllInterfaces.Any(static i => i.SpecialType == SpecialType.System_IDisposable),
                new EquatableArray<MemberModel>([.. members])));
        }

        return new RootModel(
            TypeName(root),
            new EquatableArray<TypeModel>([.. types]),
            new EquatableArray<DiagnosticModel>([.. diagnostics]));
    }

    private static (MemberModel Model, ITypeSymbol ValueType)? CreateMember(
        IPropertySymbol property,
        List<DiagnosticModel> diagnostics)
    {
        ITypeSymbol type = property.Type;
        bool isCollection = TryGetListItemType(type, out ITypeSymbol? itemType);
        ITypeSymbol valueType = itemType ?? type;

        if (!IsSupported(valueType))
            return Unsupported(property, valueType, diagnostics);

        TypeShape shape = Shape(type);
        TypeShape? itemShape = itemType == null ? null : Shape(itemType);

        AttributeData? attr = FindAttribute(property, XmlAttributeAttribute);
        if (attr != null)
        {
            if (isCollection || !IsAttributeType(type)) return Unsupported(property, type, diagnostics);

            return (new MemberModel(
                property.Name, AttributeName(attr, property.Name), string.Empty, MemberShape.Attribute, shape,
                itemShape), valueType);
        }

        AttributeData? array = FindAttribute(property, XmlArrayAttribute);
        AttributeData? element = FindAttribute(property, XmlElementAttribute);

        if (isCollection)
        {
            if (element != null)
            {
                string collectionElementName = AttributeName(element, property.Name);
                return (new MemberModel(
                    property.Name, collectionElementName, collectionElementName, MemberShape.Element, shape,
                    itemShape), valueType);
            }

            string itemName = GetItemName(property, itemType);
            string wrapperName = array != null ? AttributeName(array, property.Name) : property.Name;
            return (new MemberModel(property.Name, wrapperName, itemName, MemberShape.Array, shape, itemShape),
                valueType);
        }

        string elementName = element != null ? AttributeName(element, property.Name) : property.Name;
        return (new MemberModel(property.Name, elementName, string.Empty, MemberShape.Element, shape, itemShape),
            valueType);
    }

    private static (MemberModel Model, ITypeSymbol ValueType)? Unsupported(
        IPropertySymbol property,
        ITypeSymbol type,
        List<DiagnosticModel> diagnostics)
    {
        diagnostics.Add(new DiagnosticModel(
            property.ToDisplayString(),
            type.ToDisplayString(),
            LocationModel.From(property.Locations.FirstOrDefault())));
        return null;
    }

    private static TypeShape Shape(ITypeSymbol type)
    {
        if (Nullable.GetUnderlyingTypeSymbol(type) is { } underlying)
        {
            TypeShape inner = Shape(underlying);
            return new TypeShape(TypeName(type), inner.Kind, IsValueType: true, IsNullableReference: false,
                inner.IsFlags, inner);
        }

        ValueKind kind = type.SpecialType switch
        {
            SpecialType.System_String => ValueKind.String,
            SpecialType.System_Boolean => ValueKind.Boolean,
            SpecialType.System_Int32 => ValueKind.Int32,
            SpecialType.System_UInt32 => ValueKind.UInt32,
            SpecialType.System_Int64 => ValueKind.Int64,
            SpecialType.System_Double => ValueKind.Double,
            _ when type.TypeKind == TypeKind.Enum => ValueKind.Enum,
            _ when IsDateTime(type) => ValueKind.DateTime,
            _ => ValueKind.Complex
        };

        bool isFlags = kind == ValueKind.Enum && type.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.FlagsAttribute");
        return new TypeShape(
            TypeName(type),
            kind,
            type.IsValueType,
            type is { NullableAnnotation: NullableAnnotation.Annotated, IsValueType: false },
            isFlags,
            NullableUnderlying: null);
    }

    /// <summary>
    /// Public read/write instance properties, base members first. An override or <c>new</c> redeclaration keeps
    /// its base position but contributes its own XML attributes, so a derived type can rename an inherited element.
    /// </summary>
    private static IEnumerable<IPropertySymbol> SerializableProperties(INamedTypeSymbol type)
    {
        List<INamedTypeSymbol> hierarchy = [];
        for (INamedTypeSymbol? current = type;
             current != null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType)
            hierarchy.Add(current);

        Dictionary<string, IPropertySymbol> mostDerived = [];
        foreach (INamedTypeSymbol current in hierarchy)
        {
            foreach (IPropertySymbol property in InstanceProperties(current))
                mostDerived.TryAdd(property.Name, property);
        }

        HashSet<string> seen = [];
        for (int index = hierarchy.Count - 1; index >= 0; index--)
        {
            foreach (IPropertySymbol declared in InstanceProperties(hierarchy[index]))
            {
                if (!seen.Add(declared.Name)) continue;

                IPropertySymbol property = mostDerived[declared.Name];
                if (property.GetMethod == null || property.SetMethod == null) continue;
                if (property.GetMethod.DeclaredAccessibility != Accessibility.Public
                    || property.SetMethod.DeclaredAccessibility != Accessibility.Public)
                    continue;
                if (HasAttribute(property, XmlIgnoreAttribute)) continue;

                yield return property;
            }
        }
    }

    private static IEnumerable<IPropertySymbol> InstanceProperties(INamedTypeSymbol type) =>
        type.GetMembers().OfType<IPropertySymbol>()
            .Where(static property => !property.IsStatic && property.Parameters.Length == 0);

    private static bool TryGetListItemType(ITypeSymbol type, out ITypeSymbol? itemType)
    {
        itemType = null;
        if (type is not INamedTypeSymbol { IsGenericType: true } named
            || named.ConstructedFrom.ToDisplayString() != "System.Collections.Generic.List<T>")
            return false;

        itemType = named.TypeArguments[0];
        return true;
    }

    private static bool IsSupported(ITypeSymbol type)
    {
        if (Nullable.GetUnderlyingTypeSymbol(type) is { } underlying) type = underlying;
        return IsSimple(type) || IsComplex(type);
    }

    private static bool IsAttributeType(ITypeSymbol type)
    {
        if (Nullable.GetUnderlyingTypeSymbol(type) is { } underlying) type = underlying;
        return IsSimple(type);
    }

    private static bool IsSimple(ITypeSymbol type) =>
        type.SpecialType is SpecialType.System_String
            or SpecialType.System_Boolean
            or SpecialType.System_Int32
            or SpecialType.System_UInt32
            or SpecialType.System_Int64
            or SpecialType.System_Double
        || type.TypeKind == TypeKind.Enum
        || IsDateTime(type);

    private static bool IsComplex(ITypeSymbol type)
    {
        if (Nullable.GetUnderlyingTypeSymbol(type) != null) return false;
        if (IsSimple(type)) return false;
        if (type is not INamedTypeSymbol named) return false;
        if (named.TypeKind is not (TypeKind.Class or TypeKind.Struct)) return false;

        return named.ContainingNamespace?.ToDisplayString().StartsWith(value: "System", StringComparison.Ordinal) !=
               true
               && HasPublicParameterlessConstructor(named);
    }

    private static bool HasPublicParameterlessConstructor(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Struct) return true;

        return type.InstanceConstructors.Any(static ctor =>
            ctor.Parameters.Length == 0 && ctor.DeclaredAccessibility == Accessibility.Public);
    }

    private static string GetItemName(IPropertySymbol property, ITypeSymbol? itemType)
    {
        AttributeData? item = FindAttribute(property, XmlArrayItemAttribute);
        if (item != null) return AttributeName(item, itemType?.Name ?? "Item");

        return itemType?.SpecialType == SpecialType.System_String
            ? "Item"
            : itemType?.Name ?? "Item";
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName) =>
        FindAttribute(symbol, metadataName) != null;

    private static AttributeData? FindAttribute(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().FirstOrDefault(attr =>
            attr.AttributeClass?.ToDisplayString() == metadataName);

    private static string GetRootName(INamedTypeSymbol type)
    {
        AttributeData? attr = FindAttribute(type, XmlRootAttribute);
        return attr == null ? type.Name : AttributeName(attr, type.Name);
    }

    private static string AttributeName(AttributeData attr, string fallback)
    {
        if (attr.ConstructorArguments.Length > 0
            && attr.ConstructorArguments[0].Value is string ctorValue
            && !string.IsNullOrEmpty(ctorValue))
            return ctorValue;

        foreach (KeyValuePair<string, TypedConstant> named in attr.NamedArguments)
        {
            if (named.Key is "ElementName" or "AttributeName"
                && named.Value.Value is string value
                && !string.IsNullOrEmpty(value))
                return value;
        }

        return fallback;
    }

    private static string TypeName(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static bool IsDateTime(ITypeSymbol type) =>
        type.ToDisplayString() == "System.DateTime";
}

internal static class Nullable
{
    public static ITypeSymbol? GetUnderlyingTypeSymbol(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol
            {
                IsGenericType: true, ConstructedFrom.SpecialType: SpecialType.System_Nullable_T
            } named)
            return named.TypeArguments[0];

        return null;
    }
}
