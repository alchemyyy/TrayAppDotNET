using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Debugging;

/// <summary>Identifies how instrumented application code assigned an Avalonia property.</summary>
public enum DebugPropertyAssignmentOperation
{
    CLRSetter,
    SetValue,
    SetCurrentValue,
    ClearValue,
    Binding,
    AttachedProperty,
    Builder
}

/// <summary>Records debug-only property and AXAML source provenance without affecting Release builds.</summary>
public static class DebugUIProvenance
{
    /// <summary>Associates a catalog glyph with its AXAML resource key.</summary>
    [Conditional("DEBUG")]
    public static void RegisterGlyphResource(Glyph glyph, string resourceKey)
    {
#if DEBUG
        ArgumentNullException.ThrowIfNull(glyph);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        DebugGlyphProvenanceRegistry.Register(glyph, resourceKey);
#endif
    }

    /// <summary>Transfers a catalog glyph's resource identity to its text host.</summary>
    [Conditional("DEBUG")]
    public static void RecordGlyphApplication(
        TextBlock textBlock,
        Glyph glyph,
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string sourceMember = "")
    {
#if DEBUG
        ArgumentNullException.ThrowIfNull(textBlock);
        ArgumentNullException.ThrowIfNull(glyph);
        DebugGlyphProvenanceRegistry.RecordApplication(
            textBlock,
            glyph,
            sourceFilePath,
            sourceLine,
            sourceMember);
#endif
    }

    [Conditional("DEBUG")]
    public static void RecordProperty(
        AvaloniaObject target,
        AvaloniaProperty property,
        object? value,
        DebugPropertyAssignmentOperation operation = DebugPropertyAssignmentOperation.CLRSetter,
        [CallerArgumentExpression(nameof(value))] string valueExpression = "",
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLine = 0,
        [CallerMemberName] string sourceMember = "",
        string? resourceKey = null)
    {
#if DEBUG
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        DebugPropertyProvenanceRegistry.Record(
            target,
            property,
            value,
            operation,
            valueExpression,
            sourceFilePath,
            sourceLine,
            0,
            sourceMember,
            resourceKey);
#endif
    }

    /// <summary>Records all generated assignments associated with one builder boundary.</summary>
    [Conditional("DEBUG")]
    public static void RecordBuilder(
        AvaloniaObject target,
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLine = 0)
    {
#if DEBUG
        ArgumentNullException.ThrowIfNull(target);

        IReadOnlyList<CSharpBuilderProvenanceEntry> entries =
            CSharpBuilderProvenanceRegistry.Find(sourceFilePath, sourceLine);
        foreach (CSharpBuilderProvenanceEntry entry in entries)
        {
            object? value;
            try
            {
                value = target.GetValue(entry.Property);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                continue;
            }

            DebugPropertyProvenanceRegistry.Record(
                target,
                entry.Property,
                value,
                entry.Operation,
                entry.ValueExpression,
                entry.BoundarySourcePath,
                entry.AssignmentSourceLine,
                entry.AssignmentSourceColumn,
                entry.AssignmentSourceMember,
                entry.ResourceKey);
        }
#endif
    }

#if DEBUG
    /// <summary>Registers one generated AXAML catalog when its owning assembly loads.</summary>
    public static void RegisterAXAML(Assembly assembly, IReadOnlyList<AXAMLProvenanceEntry> entries) =>
        AXAMLProvenanceRegistry.Register(assembly, entries);

    /// <summary>Registers one generated common-builder assignment catalog.</summary>
    public static void RegisterCSharpBuilders(IReadOnlyList<CSharpBuilderProvenanceEntry> entries) =>
        CSharpBuilderProvenanceRegistry.Register(entries);

    internal static DebugPropertyAssignmentHistory GetPropertyHistory(
        AvaloniaObject target,
        AvaloniaProperty property) =>
        DebugPropertyProvenanceRegistry.GetHistory(target, property);

    internal static DebugPropertyAssignmentHistory GetRecentPropertyHistory(
        AvaloniaObject target,
        AvaloniaProperty property,
        int maximumAssignments) =>
        DebugPropertyProvenanceRegistry.GetRecentHistory(target, property, maximumAssignments);

    internal static IReadOnlyList<AXAMLProvenanceEntry> FindAXAMLPropertyEntries(
        IReadOnlyList<string> ownerTypeNames,
        string elementTypeName,
        string? controlName,
        string propertyName) =>
        AXAMLProvenanceRegistry.FindPropertyEntries(ownerTypeNames, elementTypeName, controlName, propertyName);

    internal static IReadOnlyList<AXAMLProvenanceEntry> FindAXAMLResourceDefinitions(string resourceKey) =>
        AXAMLProvenanceRegistry.FindResourceDefinitions(resourceKey);
#endif
}
