#if DEBUG
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace TrayAppDotNETCommon.UI.Debugging;

/// <summary>Matches runtime values against the generated AXAML property containers owned by a window.</summary>
internal sealed class RuntimeAXAMLResourceMatcher
{
    private const int MaximumResourceContainers = 8;
    private const int MaximumResourcesPerContainer = 512;

    private static readonly string[] ResourceSuffixes =
    [
        "HorizontalAlignment",
        "VerticalAlignment",
        "BorderThickness",
        "CornerRadius",
        "FontFamily",
        "FontWeight",
        "FontSize",
        "MinHeight",
        "MinWidth",
        "MaxHeight",
        "MaxWidth",
        "Thickness",
        "Alignment",
        "Padding",
        "Margin",
        "Opacity",
        "Height",
        "Width",
        "Color",
        "Size"
    ];

    private readonly List<RuntimeAXAMLResourceValue> _resources;
    private readonly Dictionary<string, int> _familyScores;

    private RuntimeAXAMLResourceMatcher(
        List<RuntimeAXAMLResourceValue> resources,
        Dictionary<string, int> familyScores)
    {
        _resources = resources;
        _familyScores = familyScores;
    }

    /// <summary>Reads only generated AXAML property containers and scores matches across one control component.</summary>
    public static RuntimeAXAMLResourceMatcher Create(
        IReadOnlyList<RuntimeAXAMLResourceValue> resources,
        IReadOnlyList<RuntimePropertyValue> componentValues)
    {
        Dictionary<string, HashSet<string>> matchesByFamily = new(StringComparer.Ordinal);

        foreach (RuntimePropertyValue componentValue in componentValues)
        {
            foreach (RuntimeAXAMLResourceValue resource in resources)
            {
                if (!string.Equals(
                        componentValue.ComparisonKey,
                        resource.ComparisonKey,
                        StringComparison.Ordinal)) continue;

                string family = ResourceFamily(resource.ResourceKey);
                if (!matchesByFamily.TryGetValue(family, out HashSet<string>? propertyIdentities))
                {
                    propertyIdentities = new HashSet<string>(StringComparer.Ordinal);
                    matchesByFamily.Add(family, propertyIdentities);
                }

                propertyIdentities.Add(componentValue.Identity);
            }
        }

        Dictionary<string, int> familyScores = new(StringComparer.Ordinal);
        foreach ((string family, HashSet<string> propertyIdentities) in matchesByFamily)
            familyScores.Add(family, propertyIdentities.Count);

        return new RuntimeAXAMLResourceMatcher([.. resources], familyScores);
    }

    /// <summary>Returns value-equal resources ordered by their agreement with the surrounding component.</summary>
    public IReadOnlyList<RuntimeAXAMLResourceMatch> Find(string comparisonKey)
    {
        List<RuntimeAXAMLResourceMatch> matches = [];
        foreach (RuntimeAXAMLResourceValue resource in _resources)
        {
            if (!string.Equals(comparisonKey, resource.ComparisonKey, StringComparison.Ordinal)) continue;

            string family = ResourceFamily(resource.ResourceKey);
            int familyScore = _familyScores.GetValueOrDefault(family);
            IReadOnlyList<AXAMLProvenanceEntry> definitions =
                DebugUIProvenance.FindAXAMLResourceDefinitions(resource.ResourceKey);
            matches.Add(new RuntimeAXAMLResourceMatch(
                resource.ResourceKey,
                resource.ValueDisplay,
                definitions,
                familyScore));
        }

        matches.Sort(static (left, right) =>
        {
            int scoreComparison = right.FamilyScore.CompareTo(left.FamilyScore);
            return scoreComparison != 0
                ? scoreComparison
                : string.Compare(left.ResourceKey, right.ResourceKey, StringComparison.Ordinal);
        });
        return matches;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Debug-only inspection intentionally reflects the active window's generated AXAML containers.")]
    public static IReadOnlyList<RuntimeAXAMLResourceValue> CaptureResources(object owner)
    {
        List<RuntimeAXAMLResourceValue> resources = [];
        HashSet<string> recordedKeys = new(StringComparer.Ordinal);
        int containerCount = 0;

        for (Type? ownerType = owner.GetType(); ownerType != null && ownerType != typeof(object); ownerType = ownerType.BaseType)
        {
            FieldInfo[] fields = ownerType.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            foreach (FieldInfo field in fields)
            {
                object? container;
                try
                {
                    container = field.GetValue(owner);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    continue;
                }

                if (container == null || !container.GetType().Name.EndsWith("AxamlProperties", StringComparison.Ordinal))
                    continue;

                CaptureContainer(container, resources, recordedKeys);
                containerCount++;
                if (containerCount >= MaximumResourceContainers) return resources;
            }
        }

        return resources;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Debug-only generated AXAML container properties are available in ordinary Debug builds.")]
    private static void CaptureContainer(
        object container,
        List<RuntimeAXAMLResourceValue> resources,
        HashSet<string> recordedKeys)
    {
        Type containerType = container.GetType();
        const string TypeSuffix = "AxamlProperties";
        string prefix = containerType.Name[..^TypeSuffix.Length];
        PropertyInfo[] properties = containerType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        int propertyCount = Math.Min(properties.Length, MaximumResourcesPerContainer);
        for (int index = 0; index < propertyCount; index++)
        {
            PropertyInfo property = properties[index];
            if (property.GetIndexParameters().Length != 0) continue;

            string resourceKey = prefix + "." + property.Name;
            if (!recordedKeys.Add(resourceKey)) continue;

            object? value;
            try
            {
                value = property.GetValue(container);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                continue;
            }

            DebugValueSnapshot valueSnapshot = DebugValueSnapshot.Create(value);
            resources.Add(new RuntimeAXAMLResourceValue(
                resourceKey,
                RuntimeValueComparisonKey.Create(value),
                valueSnapshot.Display));
        }
    }

    private static string ResourceFamily(string resourceKey)
    {
        foreach (string suffix in ResourceSuffixes)
        {
            if (resourceKey.Length > suffix.Length
                && resourceKey.EndsWith(suffix, StringComparison.Ordinal))
            {
                return resourceKey[..^suffix.Length];
            }
        }

        return resourceKey;
    }

}

/// <summary>Identifies one runtime property value within a bounded control component.</summary>
internal readonly record struct RuntimePropertyValue(string Identity, string ComparisonKey);

/// <summary>Contains one detached value read from a generated AXAML property container.</summary>
internal readonly record struct RuntimeAXAMLResourceValue(
    string ResourceKey,
    string ComparisonKey,
    string ValueDisplay);

/// <summary>Describes one runtime value match to an AXAML resource definition.</summary>
internal readonly record struct RuntimeAXAMLResourceMatch(
    string ResourceKey,
    string ValueDisplay,
    IReadOnlyList<AXAMLProvenanceEntry> Definitions,
    int FamilyScore);

/// <summary>Creates stable comparison keys without retaining live Avalonia values.</summary>
internal static class RuntimeValueComparisonKey
{
    public static string Create(object? value)
    {
        if (value == null) return "null";

        return value switch
        {
            string text => "string:" + text,
            bool boolean => boolean ? "bool:true" : "bool:false",
            byte number => Number(number),
            sbyte number => Number(number),
            short number => Number(number),
            ushort number => Number(number),
            int number => Number(number),
            uint number => Number(number),
            long number => Number(number),
            ulong number => Number(number),
            float number => FloatingPoint(number),
            double number => FloatingPoint(number),
            decimal number => Number(number),
            _ => Fallback(value)
        };
    }

    private static string Number<TNumber>(TNumber value)
        where TNumber : IFormattable =>
        "number:" + value.ToString(null, CultureInfo.InvariantCulture);

    private static string FloatingPoint(double value) =>
        "number:" + value.ToString("R", CultureInfo.InvariantCulture);

    private static string Fallback(object value)
    {
        DebugValueSnapshot snapshot = DebugValueSnapshot.Create(value);
        return snapshot.TypeName + ":" + snapshot.Display;
    }
}
#endif
