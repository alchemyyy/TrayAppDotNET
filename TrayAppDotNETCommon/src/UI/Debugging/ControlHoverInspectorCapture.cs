#if DEBUG
namespace TrayAppDotNETCommon.UI.Debugging;

/// <summary>Contains detached UI-thread data that can be processed without Avalonia controls.</summary>
internal sealed class ControlHoverInspectorCapture(
    string targetLabel,
    ControlHoverInspectorNode identityNode,
    ControlHoverInspectorNode? ancestryNode,
    IReadOnlyList<CapturedInspectorObject> componentObjects,
    IReadOnlyList<RuntimePropertyValue> componentValues,
    IReadOnlyList<RuntimeAXAMLResourceValue> runtimeResources)
{
    public string TargetLabel { get; } = targetLabel;

    public ControlHoverInspectorNode IdentityNode { get; } = identityNode;

    public ControlHoverInspectorNode? AncestryNode { get; } = ancestryNode;

    public IReadOnlyList<CapturedInspectorObject> ComponentObjects { get; } = componentObjects;

    public IReadOnlyList<RuntimePropertyValue> ComponentValues { get; } = componentValues;

    public IReadOnlyList<RuntimeAXAMLResourceValue> RuntimeResources { get; } = runtimeResources;
}

/// <summary>Contains detached properties for one target or bounded content element.</summary>
internal sealed class CapturedInspectorObject(
    string relationship,
    string label,
    string elementTypeName,
    string? controlName,
    IReadOnlyList<string> axamlOwnerTypeNames,
    IReadOnlyList<CapturedInspectorProperty> properties)
{
    public string Relationship { get; } = relationship;

    public string Label { get; } = label;

    public string ElementTypeName { get; } = elementTypeName;

    public string? ControlName { get; } = controlName;

    public IReadOnlyList<string> AXAMLOwnerTypeNames { get; } = axamlOwnerTypeNames;

    public IReadOnlyList<CapturedInspectorProperty> Properties { get; } = properties;
}

/// <summary>Contains one detached effective Avalonia property and its recorded assignment history.</summary>
internal sealed class CapturedInspectorProperty(
    string name,
    string ownerTypeName,
    string propertyTypeName,
    string valueDisplay,
    string comparisonKey,
    string priorityDisplay,
    bool isSet,
    string? diagnosticSource,
    bool isOverriddenCurrentValue,
    DebugPropertyAssignmentHistory assignmentHistory,
    CapturedBrushProvenance? brushProvenance,
    string? unavailableMessage)
{
    public string Name { get; } = name;

    public string OwnerTypeName { get; } = ownerTypeName;

    public string PropertyTypeName { get; } = propertyTypeName;

    public string ValueDisplay { get; } = valueDisplay;

    public string ComparisonKey { get; } = comparisonKey;

    public string PriorityDisplay { get; } = priorityDisplay;

    public bool IsSet { get; } = isSet;

    public string? DiagnosticSource { get; } = diagnosticSource;

    public bool IsOverriddenCurrentValue { get; } = isOverriddenCurrentValue;

    public DebugPropertyAssignmentHistory AssignmentHistory { get; } = assignmentHistory;

    public CapturedBrushProvenance? BrushProvenance { get; } = brushProvenance;

    public string? UnavailableMessage { get; } = unavailableMessage;
}

/// <summary>Contains detached color-assignment history for a property value that was a solid brush.</summary>
internal readonly record struct CapturedBrushProvenance(
    string ColorDisplay,
    DebugPropertyAssignmentHistory AssignmentHistory);
#endif
