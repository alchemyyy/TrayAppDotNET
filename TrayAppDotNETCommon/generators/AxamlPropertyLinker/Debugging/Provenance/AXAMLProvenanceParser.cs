using System.Collections.Immutable;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TrayAppDotNETCommon.AxamlPropertyLinker;

internal static class AXAMLProvenanceParser
{
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static AXAMLProvenanceDocument Parse(
        XDocument document,
        string sourcePath,
        string ownerTypeName)
    {
        List<AXAMLProvenanceItem> entries = [];
        XElement? root = document.Root;
        if (root == null)
            return new AXAMLProvenanceDocument(sourcePath, ownerTypeName, ImmutableArray<AXAMLProvenanceItem>.Empty);

        foreach (XElement element in root.DescendantsAndSelf())
        {
            AddResourceDefinition(entries, element);
            AddElementRole(entries, element);
            AddPropertyElement(entries, element);
            AddPropertyAttributes(entries, element);
        }

        return new AXAMLProvenanceDocument(sourcePath, ownerTypeName, [.. entries]);
    }

    private static void AddResourceDefinition(List<AXAMLProvenanceItem> entries, XElement element)
    {
        string? resourceKey = ResourceKey(element);
        if (resourceKey == null) return;

        AddEntry(
            entries,
            AXAMLProvenanceItemKind.ResourceDefinition,
            element,
            element,
            propertyName: null,
            resourceKey,
            ElementValueExpression(element),
            FindSelector(element));
    }

    private static void AddElementRole(List<AXAMLProvenanceItem> entries, XElement element)
    {
        string localName = element.Name.LocalName;
        switch (localName)
        {
            case "Style":
                AddStyle(entries, element);
                return;
            case "Setter":
                AddStyleSetter(entries, element);
                return;
            case "ControlTheme":
                AddControlTheme(entries, element);
                return;
        }

        if (IsTemplateElement(element))
            AddTemplate(entries, element);

        if (IsBindingElement(localName))
            AddBindingElement(entries, element);
        else if (IsResourceReferenceElement(localName))
            AddResourceReferenceElement(entries, element);
    }

    private static void AddStyle(List<AXAMLProvenanceItem> entries, XElement element)
    {
        XAttribute? selectorAttribute = element.Attribute("Selector");
        string? selector = selectorAttribute?.Value;
        AddEntry(
            entries,
            AXAMLProvenanceItemKind.Style,
            element,
            selectorAttribute ?? (XObject)element,
            propertyName: null,
            ResourceKey(element),
            selector,
            selector);
    }

    private static void AddStyleSetter(List<AXAMLProvenanceItem> entries, XElement element)
    {
        XAttribute? propertyAttribute = element.Attribute("Property");
        string? propertyName = propertyAttribute?.Value;
        XAttribute? valueAttribute = element.Attribute("Value");
        XElement? valueElement = FindSetterValueElement(element);
        string? valueExpression = valueAttribute?.Value ?? PropertyElementValueExpression(valueElement);
        string? referencedResourceKey = FirstResourceKey(valueExpression);
        XObject location =
            (XObject?)valueAttribute ?? (XObject?)valueElement ?? (XObject?)propertyAttribute ?? element;

        AddEntry(
            entries,
            AXAMLProvenanceItemKind.StyleSetter,
            element,
            location,
            propertyName,
            referencedResourceKey,
            valueExpression,
            FindSelector(element));

        AddMarkupEntries(
            entries,
            element,
            location,
            propertyName,
            valueExpression,
            FindSelector(element));
    }

    private static void AddControlTheme(List<AXAMLProvenanceItem> entries, XElement element)
    {
        string? resourceKey = ResourceKey(element);
        string? targetType = element.Attribute("TargetType")?.Value;
        AddEntry(
            entries,
            AXAMLProvenanceItemKind.ControlTheme,
            element,
            element,
            propertyName: "Theme",
            resourceKey,
            targetType,
            ControlThemeSelector(resourceKey, targetType));
    }

    private static void AddTemplate(List<AXAMLProvenanceItem> entries, XElement element)
    {
        string? resourceKey = ResourceKey(element) ?? NearestControlThemeKey(element);
        string? propertyName = FindPropertyName(element);
        AddEntry(
            entries,
            AXAMLProvenanceItemKind.Template,
            element,
            element,
            propertyName,
            resourceKey,
            ElementValueExpression(element),
            FindSelector(element));
    }

    private static void AddBindingElement(List<AXAMLProvenanceItem> entries, XElement element)
    {
        XElement contextElement = FindPropertyOwner(element) ?? element;
        AddEntry(
            entries,
            AXAMLProvenanceItemKind.Binding,
            contextElement,
            element,
            FindPropertyName(element),
            resourceKey: null,
            ElementValueExpression(element),
            FindSelector(element));
    }

    private static void AddResourceReferenceElement(List<AXAMLProvenanceItem> entries, XElement element)
    {
        XElement contextElement = FindPropertyOwner(element) ?? element;
        string? resourceKey = element.Attribute("ResourceKey")?.Value;
        if (string.IsNullOrWhiteSpace(resourceKey)) resourceKey = element.Value.Trim();

        AddEntry(
            entries,
            AXAMLProvenanceItemKind.ResourceReference,
            contextElement,
            element,
            FindPropertyName(element),
            string.IsNullOrWhiteSpace(resourceKey) ? null : resourceKey,
            ElementValueExpression(element),
            FindSelector(element));
    }

    private static void AddPropertyElement(List<AXAMLProvenanceItem> entries, XElement element)
    {
        if (!IsPropertyElement(element)) return;

        XElement? ownerElement = element.Parent;
        if (ownerElement == null || string.Equals(ownerElement.Name.LocalName, b: "Setter", StringComparison.Ordinal))
            return;

        string? propertyName = PropertyNameFromPropertyElement(element);
        string? valueExpression = PropertyElementValueExpression(element);
        AddEntry(
            entries,
            AXAMLProvenanceItemKind.PropertyAssignment,
            ownerElement,
            element,
            propertyName,
            FirstResourceKey(valueExpression),
            valueExpression,
            FindSelector(element));

        AddMarkupEntries(
            entries,
            ownerElement,
            element,
            propertyName,
            valueExpression,
            FindSelector(element));
    }

    private static void AddPropertyAttributes(List<AXAMLProvenanceItem> entries, XElement element)
    {
        string? owningResourceKey = ResourceKey(element);
        string? selector = FindSelector(element);
        foreach (XAttribute attribute in element.Attributes())
        {
            if (ShouldSkipPropertyAttribute(element, attribute)) continue;

            string propertyName = AttributeName(attribute);
            string valueExpression = attribute.Value;
            string? referencedResourceKey = FirstResourceKey(valueExpression);
            AddEntry(
                entries,
                AXAMLProvenanceItemKind.PropertyAssignment,
                element,
                attribute,
                propertyName,
                referencedResourceKey ?? owningResourceKey,
                valueExpression,
                selector);

            AddMarkupEntries(
                entries,
                element,
                attribute,
                propertyName,
                valueExpression,
                selector);
        }
    }

    private static void AddMarkupEntries(
        List<AXAMLProvenanceItem> entries,
        XElement contextElement,
        XObject location,
        string? propertyName,
        string? valueExpression,
        string? selector)
    {
        if (string.IsNullOrWhiteSpace(valueExpression)) return;

        IReadOnlyList<AXAMLMarkupExtension> markupExtensions = AXAMLMarkupExtensionParser.Find(valueExpression);
        foreach (AXAMLMarkupExtension markupExtension in markupExtensions)
        {
            AXAMLProvenanceItemKind kind = markupExtension.Kind switch
            {
                AXAMLMarkupExtensionKind.Binding => AXAMLProvenanceItemKind.Binding,
                AXAMLMarkupExtensionKind.ResourceReference => AXAMLProvenanceItemKind.ResourceReference,
                _ => throw new ArgumentOutOfRangeException(nameof(markupExtension.Kind), markupExtension.Kind,
                    message: null)
            };

            AddEntry(
                entries,
                kind,
                contextElement,
                location,
                propertyName,
                markupExtension.ResourceKey,
                valueExpression,
                selector);
        }
    }

    private static void AddEntry(
        List<AXAMLProvenanceItem> entries,
        AXAMLProvenanceItemKind kind,
        XElement contextElement,
        XObject location,
        string? propertyName,
        string? resourceKey,
        string? valueExpression,
        string? selector)
    {
        (int line, int column) = SourceLocation(location);
        entries.Add(new AXAMLProvenanceItem(
            kind,
            line,
            column,
            ElementTypeName(contextElement),
            ElementPath(contextElement),
            FindControlName(contextElement),
            EmptyToNull(propertyName),
            EmptyToNull(resourceKey),
            EmptyToNull(valueExpression),
            EmptyToNull(selector)));
    }

    private static string? FirstResourceKey(string? valueExpression)
    {
        if (string.IsNullOrWhiteSpace(valueExpression)) return null;

        IReadOnlyList<AXAMLMarkupExtension> extensions = AXAMLMarkupExtensionParser.Find(valueExpression);
        foreach (AXAMLMarkupExtension extension in extensions)
        {
            if (extension.Kind == AXAMLMarkupExtensionKind.ResourceReference)
                return extension.ResourceKey;
        }

        return null;
    }

    private static bool ShouldSkipPropertyAttribute(XElement element, XAttribute attribute)
    {
        if (attribute.IsNamespaceDeclaration) return true;

        if (attribute.Name.NamespaceName == XamlNamespace)
        {
            switch (attribute.Name.LocalName)
            {
                case "Class":
                case "Key":
                case "Name":
                    return true;
            }
        }

        if (string.Equals(attribute.Name.LocalName, b: "Name", StringComparison.Ordinal) &&
            string.IsNullOrEmpty(attribute.Name.NamespaceName))
            return true;

        return element.Name.LocalName switch
        {
            "Style" => string.Equals(attribute.Name.LocalName, b: "Selector", StringComparison.Ordinal),
            "Setter" => attribute.Name.LocalName is "Property" or "Value",
            _ => false
        };
    }

    private static string? ResourceKey(XElement element)
    {
        XName keyAttributeName = XName.Get(localName: "Key", XamlNamespace);
        return EmptyToNull(element.Attribute(keyAttributeName)?.Value);
    }

    private static string? NearestControlThemeKey(XElement element)
    {
        foreach (XElement ancestor in element.AncestorsAndSelf())
        {
            if (string.Equals(ancestor.Name.LocalName, b: "ControlTheme", StringComparison.Ordinal))
                return ResourceKey(ancestor);
        }

        return null;
    }

    private static string? FindSelector(XElement element)
    {
        foreach (XElement ancestor in element.AncestorsAndSelf())
        {
            if (string.Equals(ancestor.Name.LocalName, b: "Style", StringComparison.Ordinal))
                return EmptyToNull(ancestor.Attribute("Selector")?.Value);

            if (string.Equals(ancestor.Name.LocalName, b: "ControlTheme", StringComparison.Ordinal))
            {
                string? resourceKey = ResourceKey(ancestor);
                string? targetType = ancestor.Attribute("TargetType")?.Value;
                return ControlThemeSelector(resourceKey, targetType);
            }
        }

        return null;
    }

    private static string? ControlThemeSelector(string? resourceKey, string? targetType)
    {
        if (!string.IsNullOrWhiteSpace(resourceKey)) return "ControlTheme:" + resourceKey;
        return string.IsNullOrWhiteSpace(targetType) ? null : "ControlTheme:" + targetType;
    }

    private static XElement? FindSetterValueElement(XElement setter)
    {
        foreach (XElement child in setter.Elements())
        {
            if (child.Name.LocalName.EndsWith(value: ".Value", StringComparison.Ordinal))
                return child;
        }

        return null;
    }

    private static XElement? FindPropertyOwner(XElement element)
    {
        foreach (XElement ancestor in element.Ancestors())
        {
            if (!IsPropertyElement(ancestor)) continue;
            return ancestor.Parent;
        }

        return null;
    }

    private static string? FindPropertyName(XElement element)
    {
        foreach (XElement ancestor in element.Ancestors())
        {
            if (IsPropertyElement(ancestor))
                return PropertyNameFromPropertyElement(ancestor);

            if (string.Equals(ancestor.Name.LocalName, b: "Setter", StringComparison.Ordinal))
                return EmptyToNull(ancestor.Attribute("Property")?.Value);
        }

        return null;
    }

    private static string? PropertyNameFromPropertyElement(XElement element)
    {
        string localName = element.Name.LocalName;
        int separatorIndex = localName.LastIndexOf('.');
        return separatorIndex < 0 || separatorIndex == localName.Length - 1
            ? null
            : localName[(separatorIndex + 1)..];
    }

    private static string? PropertyElementValueExpression(XElement? propertyElement)
    {
        if (propertyElement == null) return null;

        List<XElement> childElements = [.. propertyElement.Elements()];
        if (childElements.Count == 0) return EmptyToNull(propertyElement.Value.Trim());
        if (childElements.Count == 1)
        {
            XElement child = childElements[0];
            string? childValue = ElementValueExpression(child);
            return childValue == null
                ? "<" + ElementTypeName(child) + ">"
                : "<" + ElementTypeName(child) + "> " + childValue;
        }

        return string.Join(separator: ", ", childElements.Select(static child => "<" + ElementTypeName(child) + ">"));
    }

    private static string? ElementValueExpression(XElement element)
    {
        StringBuilder builder = new();
        foreach (XAttribute attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration) continue;
            if (attribute.Name is { NamespaceName: XamlNamespace, LocalName: "Key" }) continue;

            if (builder.Length > 0) builder.Append(' ');
            builder.Append(AttributeName(attribute));
            builder.Append("=\"");
            builder.Append(attribute.Value);
            builder.Append('"');
        }

        if (!element.HasElements)
        {
            string textValue = element.Value.Trim();
            if (textValue.Length > 0)
            {
                if (builder.Length > 0) builder.Append(" Value=\"");
                builder.Append(textValue);
                if (builder.Length > textValue.Length) builder.Append('"');
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static string ElementTypeName(XElement element)
    {
        string? prefix = element.GetPrefixOfNamespace(element.Name.Namespace);
        return string.IsNullOrEmpty(prefix)
            ? element.Name.LocalName
            : prefix + ":" + element.Name.LocalName;
    }

    private static string AttributeName(XAttribute attribute)
    {
        if (string.IsNullOrEmpty(attribute.Name.NamespaceName)) return attribute.Name.LocalName;

        string? prefix = attribute.Parent?.GetPrefixOfNamespace(attribute.Name.Namespace);
        return string.IsNullOrEmpty(prefix)
            ? attribute.Name.LocalName
            : prefix + ":" + attribute.Name.LocalName;
    }

    private static string ElementPath(XElement element)
    {
        List<XElement> ancestors = [.. element.AncestorsAndSelf()];
        ancestors.Reverse();
        StringBuilder builder = new();
        foreach (XElement ancestor in ancestors)
        {
            int siblingIndex = 1;
            foreach (XElement sibling in ancestor.ElementsBeforeSelf())
            {
                if (sibling.Name == ancestor.Name)
                    siblingIndex++;
            }

            builder.Append('/');
            builder.Append(ElementTypeName(ancestor));
            builder.Append('[');
            builder.Append(siblingIndex);
            builder.Append(']');
        }

        return builder.ToString();
    }

    private static string? FindControlName(XElement element)
    {
        XName xamlName = XName.Get(localName: "Name", XamlNamespace);
        string? name = element.Attribute(xamlName)?.Value ?? element.Attribute("Name")?.Value;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static (int line, int column) SourceLocation(XObject source)
    {
        IXmlLineInfo lineInfo = source;
        return lineInfo.HasLineInfo()
            ? (lineInfo.LineNumber, lineInfo.LinePosition)
            : (0, 0);
    }

    private static bool IsPropertyElement(XElement element) =>
        element.Name.LocalName.Contains(value: '.', StringComparison.Ordinal);

    private static bool IsTemplateElement(XElement element) =>
        !IsPropertyElement(element) && element.Name.LocalName.EndsWith(value: "Template", StringComparison.Ordinal);

    private static bool IsBindingElement(string localName) =>
        localName is "Binding" or "CompiledBinding" or "ReflectionBinding" or "TemplateBinding";

    private static bool IsResourceReferenceElement(string localName) =>
        localName is "StaticResource" or "DynamicResource";

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
