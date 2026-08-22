#if DEBUG
using System.Collections;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace TrayAppDotNETCommon.UI.Debugging;

/// <summary>Contains one captured inspector update for the debug window.</summary>
internal sealed class ControlHoverInspectorSnapshot
{
    public ControlHoverInspectorSnapshot(
        string targetLabel,
        IReadOnlyList<ControlHoverInspectorNode> roots)
    {
        TargetLabel = targetLabel;
        Roots = roots;
    }

    public string TargetLabel { get; }

    public IReadOnlyList<ControlHoverInspectorNode> Roots { get; }
}

/// <summary>Describes one expandable row in the hover inspector.</summary>
internal sealed class ControlHoverInspectorNode
{
    public ControlHoverInspectorNode(string text, bool isExpanded = false)
    {
        Text = text;
        IsExpanded = isExpanded;
    }

    public string Text { get; }

    public bool IsExpanded { get; }

    public List<ControlHoverInspectorNode> Children { get; } = [];
}

/// <summary>Captures UI state quickly, then builds bounded inspector trees from detached data.</summary>
internal static class ControlHoverInspectorSnapshotBuilder
{
    internal const int MaximumVisualPathElements = 32;
    internal const int MaximumAppliedPropertiesPerTree = 160;
    internal const int MaximumSnapshotNodeCount = 2048;
    internal const int MaximumDisplayedAssignmentsPerProperty = 4;
    internal const int MaximumDisplayedAXAMLEntriesPerProperty = 8;
    internal const int MaximumDisplayedResourceDefinitions = 4;
    internal const int MaximumComponentElements = 8;

    private const int MaximumDisplayedClasses = 16;
    private const int MaximumDisplayedValueLength = 240;
    private const int MaximumComponentDepth = 2;

    /// <summary>Builds a compact snapshot synchronously for tests and non-interactive callers.</summary>
    public static ControlHoverInspectorSnapshot Build(TopLevel topLevel, IInputElement hitElement)
    {
        ControlHoverInspectorCapture capture = Capture(topLevel, hitElement);
        return Build(capture, CancellationToken.None);
    }

    /// <summary>Copies bounded Avalonia state into a detached DTO on the UI thread.</summary>
    public static ControlHoverInspectorCapture Capture(TopLevel topLevel, IInputElement hitElement)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        ArgumentNullException.ThrowIfNull(hitElement);

        List<AvaloniaObject> componentObjects = FindComponentObjects(hitElement);
        List<CapturedInspectorObject> capturedObjects = [];
        List<RuntimePropertyValue> componentValues = [];
        for (int objectIndex = 0; objectIndex < componentObjects.Count; objectIndex++)
        {
            CapturedInspectorObject capturedObject = CaptureObject(
                topLevel,
                componentObjects[objectIndex],
                objectIndex,
                componentValues);
            capturedObjects.Add(capturedObject);
        }

        IReadOnlyList<RuntimeAXAMLResourceValue> runtimeResources =
            RuntimeAXAMLResourceMatcher.CaptureResources(topLevel);
        ControlHoverInspectorNode? ancestryNode = hitElement is Visual hitVisual
            ? BuildAncestryNode(hitVisual)
            : null;

        return new ControlHoverInspectorCapture(
            ElementLabel(hitElement),
            BuildIdentityNode(topLevel, hitElement),
            ancestryNode,
            capturedObjects,
            componentValues,
            runtimeResources);
    }

    /// <summary>Builds the display tree from detached capture data on any thread.</summary>
    public static ControlHoverInspectorSnapshot Build(
        ControlHoverInspectorCapture capture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capture);
        cancellationToken.ThrowIfCancellationRequested();

        RuntimeAXAMLResourceMatcher resourceMatcher = RuntimeAXAMLResourceMatcher.Create(
            capture.RuntimeResources,
            capture.ComponentValues);

        List<ControlHoverInspectorNode> roots = [];
        roots.Add(capture.IdentityNode);
        roots.Add(BuildRelevantComponentTree(capture.ComponentObjects, resourceMatcher, cancellationToken));

        if (capture.AncestryNode != null)
            roots.Add(capture.AncestryNode);

        if (capture.ComponentObjects.Count > 0)
            roots.Add(BuildPropertyTree(capture.ComponentObjects[0], resourceMatcher, cancellationToken));

        cancellationToken.ThrowIfCancellationRequested();
        List<ControlHoverInspectorNode> boundedRoots = EnforceNodeLimit(roots);
        return new ControlHoverInspectorSnapshot(capture.TargetLabel, boundedRoots);
    }

    private static List<ControlHoverInspectorNode> EnforceNodeLimit(
        List<ControlHoverInspectorNode> roots)
    {
        Stack<ControlHoverInspectorNode> pending = [];
        for (int index = roots.Count - 1; index >= 0; index--)
            pending.Push(roots[index]);

        int nodeCount = 0;
        while (pending.Count > 0 && nodeCount <= MaximumSnapshotNodeCount)
        {
            ControlHoverInspectorNode node = pending.Pop();
            nodeCount++;
            for (int index = node.Children.Count - 1; index >= 0; index--)
                pending.Push(node.Children[index]);
        }

        if (nodeCount <= MaximumSnapshotNodeCount) return roots;

        List<ControlHoverInspectorNode> boundedRoots = [];
        Stack<(ControlHoverInspectorNode Source, List<ControlHoverInspectorNode> Destination)> copyPending = [];
        for (int index = roots.Count - 1; index >= 0; index--)
            copyPending.Push((roots[index], boundedRoots));

        int detailNodeLimit = MaximumSnapshotNodeCount - 1;
        int copiedNodeCount = 0;
        while (copyPending.Count > 0 && copiedNodeCount < detailNodeLimit)
        {
            (ControlHoverInspectorNode source, List<ControlHoverInspectorNode> destination) = copyPending.Pop();
            ControlHoverInspectorNode copy = new(source.Text, source.IsExpanded);
            destination.Add(copy);
            copiedNodeCount++;

            for (int index = source.Children.Count - 1; index >= 0; index--)
                copyPending.Push((source.Children[index], copy.Children));
        }

        boundedRoots.Add(new ControlHoverInspectorNode(
            $"... snapshot truncated at {MaximumSnapshotNodeCount} rows"));
        return boundedRoots;
    }

    private static ControlHoverInspectorNode BuildIdentityNode(TopLevel topLevel, IInputElement hitElement)
    {
        ControlHoverInspectorNode identity = new($"Target: {ElementLabel(hitElement)}", isExpanded: true);
        identity.Children.Add(new ControlHoverInspectorNode($"Type: {FullTypeName(hitElement)}"));

        if (topLevel is Window window)
        {
            identity.Children.Add(new ControlHoverInspectorNode($"Window: {FullTypeName(window)}"));
            identity.Children.Add(new ControlHoverInspectorNode(
                $"Title: {(string.IsNullOrWhiteSpace(window.Title) ? "<none>" : window.Title)}"));
        }
        else
        {
            identity.Children.Add(new ControlHoverInspectorNode($"Top level: {FullTypeName(topLevel)}"));
        }

        identity.Children.Add(new ControlHoverInspectorNode(
            $"Render scaling: {FormatNumber(topLevel.RenderScaling)} physical pixels per DIP"));

        if (hitElement is StyledElement styledElement)
        {
            identity.Children.Add(new ControlHoverInspectorNode(
                $"Name: {(string.IsNullOrWhiteSpace(styledElement.Name) ? "<unnamed>" : styledElement.Name)}"));
            identity.Children.Add(new ControlHoverInspectorNode($"Classes: {FormatClasses(styledElement)}"));
            identity.Children.Add(new ControlHoverInspectorNode(
                $"DataContext: {(styledElement.DataContext == null ? "<null>" : FullTypeName(styledElement.DataContext))}"));
        }

        if (hitElement is Control control)
        {
            identity.Children.Add(new ControlHoverInspectorNode(
                $"Enabled: {control.IsEffectivelyEnabled}; visible: {control.IsEffectivelyVisible}; hit test: {control.IsHitTestVisible}"));
        }

        return identity;
    }

    private static ControlHoverInspectorNode BuildAncestryNode(Visual hitVisual)
    {
        ControlHoverInspectorNode root = new("Visual ancestry: target to root");
        HashSet<Visual> visited = new(ReferenceEqualityComparer.Instance);
        Visual? current = hitVisual;
        int index = 0;
        while (current != null && index < MaximumVisualPathElements && visited.Add(current))
        {
            string relationship = index == 0 ? "target" : $"parent {index}";
            string metrics = current is Layoutable layoutable
                ? $"; bounds={FormatRect(current.Bounds)}; desired={FormatSize(layoutable.DesiredSize)}"
                : $"; bounds={FormatRect(current.Bounds)}";
            root.Children.Add(new ControlHoverInspectorNode(
                $"[{relationship}] {ElementLabel(current)}{metrics}",
                isExpanded: index <= 1));
            current = current.GetVisualParent();
            index++;
        }

        if (current != null)
            root.Children.Add(new ControlHoverInspectorNode("... visual ancestry truncated"));

        return root;
    }

    private static ControlHoverInspectorNode BuildRelevantComponentTree(
        IReadOnlyList<CapturedInspectorObject> componentObjects,
        RuntimeAXAMLResourceMatcher resourceMatcher,
        CancellationToken cancellationToken)
    {
        ControlHoverInspectorNode root = new("Relevant target and content properties", isExpanded: true);
        foreach (CapturedInspectorObject componentObject in componentObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ControlHoverInspectorNode objectNode = new(
                $"[{componentObject.Relationship}] {componentObject.Label}",
                isExpanded: true);
            foreach (CapturedInspectorProperty property in componentObject.Properties)
            {
                if (!IsRelevantProperty(property.Name)) continue;
                objectNode.Children.Add(BuildPropertyNode(componentObject, property, resourceMatcher));
            }

            if (objectNode.Children.Count > 0)
                root.Children.Add(objectNode);
        }

        if (root.Children.Count == 0)
            root.Children.Add(new ControlHoverInspectorNode("<none>"));

        return root;
    }

    private static CapturedInspectorObject CaptureObject(
        TopLevel topLevel,
        AvaloniaObject componentObject,
        int objectIndex,
        List<RuntimePropertyValue> componentValues)
    {
        List<CapturedInspectorProperty> capturedProperties = [];
        foreach (AvaloniaProperty property in GetRegisteredProperties(componentObject))
        {
            bool isRelevant = IsRelevantProperty(property.Name);
            if (objectIndex > 0 && !isRelevant) continue;

            try
            {
                AvaloniaPropertyValue diagnostic = componentObject.GetDiagnostic(property);
                bool isSet = componentObject.IsSet(property);
                if (!isSet && diagnostic.Priority == BindingPriority.Unset) continue;
                if (ShouldOmitProperty(property)) continue;

                string comparisonKey = RuntimeValueComparisonKey.Create(diagnostic.Value);
                CapturedInspectorProperty capturedProperty = CaptureProperty(
                    componentObject,
                    property,
                    diagnostic,
                    isSet,
                    comparisonKey);
                capturedProperties.Add(capturedProperty);
                if (isRelevant)
                {
                    componentValues.Add(new RuntimePropertyValue(
                        objectIndex.ToString(CultureInfo.InvariantCulture) + ":" + property.Name,
                        comparisonKey));
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                capturedProperties.Add(new CapturedInspectorProperty(
                    property.Name,
                    property.OwnerType.FullName ?? property.OwnerType.Name,
                    property.PropertyType.FullName ?? property.PropertyType.Name,
                    "<unavailable>",
                    string.Empty,
                    string.Empty,
                    false,
                    null,
                    false,
                    new DebugPropertyAssignmentHistory([], 0),
                    null,
                    $"{exception.GetType().Name}: {Sanitize(exception.Message)}"));
            }
        }

        string relationship = objectIndex == 0 ? "target" : $"content {objectIndex}";
        string elementTypeName = componentObject.GetType().FullName ?? componentObject.GetType().Name;
        string? controlName = (componentObject as StyledElement)?.Name;
        return new CapturedInspectorObject(
            relationship,
            ElementLabel(componentObject),
            elementTypeName,
            controlName,
            FindAXAMLOwnerTypeNames(topLevel, componentObject),
            capturedProperties);
    }

    private static CapturedInspectorProperty CaptureProperty(
        AvaloniaObject componentObject,
        AvaloniaProperty property,
        AvaloniaPropertyValue diagnostic,
        bool isSet,
        string comparisonKey)
    {
        DebugPropertyAssignmentHistory history = DebugUIProvenance.GetRecentPropertyHistory(
            componentObject,
            property,
            MaximumDisplayedAssignmentsPerProperty);
        CapturedBrushProvenance? brushProvenance = CaptureBrushProvenance(diagnostic.Value);
        return new CapturedInspectorProperty(
            property.Name,
            property.OwnerType.FullName ?? property.OwnerType.Name,
            property.PropertyType.FullName ?? property.PropertyType.Name,
            FormatValue(diagnostic.Value),
            comparisonKey,
            diagnostic.Priority.ToString(),
            isSet,
            string.IsNullOrWhiteSpace(diagnostic.Diagnostic) ? null : diagnostic.Diagnostic,
            diagnostic.IsOverriddenCurrentValue,
            history,
            brushProvenance,
            null);
    }

    private static CapturedBrushProvenance? CaptureBrushProvenance(object? value)
    {
        if (value is not SolidColorBrush brush) return null;

        DebugPropertyAssignmentHistory colorHistory = DebugUIProvenance.GetRecentPropertyHistory(
            brush,
            SolidColorBrush.ColorProperty,
            MaximumDisplayedAssignmentsPerProperty);
        return colorHistory.TotalAssignmentCount == 0
            ? null
            : new CapturedBrushProvenance(FormatValue(brush.Color), colorHistory);
    }

    private static List<AvaloniaObject> FindComponentObjects(IInputElement hitElement)
    {
        List<AvaloniaObject> componentObjects = [];
        if (hitElement is not AvaloniaObject rootObject) return componentObjects;

        componentObjects.Add(rootObject);
        if (hitElement is not ILogical rootLogical) return componentObjects;

        HashSet<ILogical> visited = new(ReferenceEqualityComparer.Instance) { rootLogical };
        Queue<(ILogical Element, int Depth)> pending = new();
        pending.Enqueue((rootLogical, 0));
        while (pending.Count > 0 && componentObjects.Count < MaximumComponentElements)
        {
            (ILogical element, int depth) = pending.Dequeue();
            if (depth >= MaximumComponentDepth) continue;

            foreach (ILogical child in element.GetLogicalChildren())
            {
                if (!visited.Add(child)) continue;
                if (child is AvaloniaObject childObject)
                    componentObjects.Add(childObject);

                if (componentObjects.Count >= MaximumComponentElements) break;
                pending.Enqueue((child, depth + 1));
            }
        }

        return componentObjects;
    }

    private static List<AvaloniaProperty> GetRegisteredProperties(AvaloniaObject avaloniaObject)
    {
        List<AvaloniaProperty> properties = [];
        HashSet<AvaloniaProperty> seenProperties = [];
        IEnumerable<AvaloniaProperty> registeredProperties =
            AvaloniaPropertyRegistry.Instance.GetRegistered(avaloniaObject);
        foreach (AvaloniaProperty property in registeredProperties)
        {
            if (seenProperties.Add(property)) properties.Add(property);
        }

        IReadOnlyList<AvaloniaProperty> registeredAttachedProperties =
            AvaloniaPropertyRegistry.Instance.GetRegisteredAttached(avaloniaObject.GetType());
        foreach (AvaloniaProperty property in registeredAttachedProperties)
        {
            if (seenProperties.Add(property)) properties.Add(property);
        }

        properties.Sort(CompareProperties);
        return properties;
    }

    private static bool IsRelevantProperty(string propertyName) => propertyName switch
    {
        "Background" or "BorderBrush" or "BorderThickness" or "CornerRadius"
            or "FontFamily" or "FontSize" or "FontWeight" or "Foreground"
            or "Height" or "IsEnabled" or "IsVisible" or "Margin" or "MaxHeight"
            or "MaxWidth" or "MinHeight" or "MinWidth" or "Opacity" or "Padding"
            or "Text" or "Tip" or "Width" => true,
        _ => false
    };

    private static bool ShouldOmitProperty(AvaloniaProperty property) =>
        property.OwnerType == typeof(ToolTip)
        && property.Name != "Tip";

    private static ControlHoverInspectorNode BuildPropertyTree(
        CapturedInspectorObject capturedObject,
        RuntimeAXAMLResourceMatcher resourceMatcher,
        CancellationToken cancellationToken)
    {
        List<ControlHoverInspectorNode> appliedProperties = [];
        foreach (CapturedInspectorProperty property in capturedObject.Properties)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (appliedProperties.Count >= MaximumAppliedPropertiesPerTree) continue;

            appliedProperties.Add(BuildPropertyNode(capturedObject, property, resourceMatcher));
        }

        int appliedPropertyCount = capturedObject.Properties.Count;
        ControlHoverInspectorNode root = new(
            $"Effective Avalonia properties: all {appliedPropertyCount}");
        foreach (ControlHoverInspectorNode propertyNode in appliedProperties)
            root.Children.Add(propertyNode);

        if (root.Children.Count == 0)
            root.Children.Add(new ControlHoverInspectorNode("<none>"));
        else if (appliedProperties.Count < appliedPropertyCount)
        {
            root.Children.Add(new ControlHoverInspectorNode(
                $"... {appliedPropertyCount - appliedProperties.Count} applied values omitted at snapshot limit"));
        }
        return root;
    }

    private static ControlHoverInspectorNode BuildPropertyNode(
        CapturedInspectorObject capturedObject,
        CapturedInspectorProperty property,
        RuntimeAXAMLResourceMatcher resourceMatcher)
    {
        if (property.UnavailableMessage != null)
        {
            return new ControlHoverInspectorNode(
                $"{property.Name} = <unavailable: {property.UnavailableMessage}>");
        }

        DebugPropertyAssignmentHistory history = property.AssignmentHistory;
        IReadOnlyList<AXAMLProvenanceEntry> axamlEntries = DebugUIProvenance.FindAXAMLPropertyEntries(
            capturedObject.AXAMLOwnerTypeNames,
            capturedObject.ElementTypeName,
            capturedObject.ControlName,
            property.Name);
        IReadOnlyList<RuntimeAXAMLResourceMatch> runtimeResourceMatches = IsRelevantProperty(property.Name)
            ? resourceMatcher.Find(property.ComparisonKey)
            : [];
        string sourceSummary = BuildSourceSummary(history, runtimeResourceMatches, axamlEntries);
        ControlHoverInspectorNode propertyNode = new(
            $"{property.Name} = {property.ValueDisplay} [{property.PriorityDisplay}]{sourceSummary}");
        propertyNode.Children.Add(new ControlHoverInspectorNode(
            $"Owner: {property.OwnerTypeName}"));
        propertyNode.Children.Add(new ControlHoverInspectorNode(
            $"Value type: {property.PropertyTypeName}"));
        propertyNode.Children.Add(new ControlHoverInspectorNode($"Is set: {property.IsSet}"));

        if (!string.IsNullOrWhiteSpace(property.DiagnosticSource))
            propertyNode.Children.Add(new ControlHoverInspectorNode($"Source: {Sanitize(property.DiagnosticSource)}"));

        if (property.IsOverriddenCurrentValue)
            propertyNode.Children.Add(new ControlHoverInspectorNode("SetCurrentValue override: true"));

        AppendInstrumentedAssignments(propertyNode, history);
        AppendAssignedBrushProvenance(propertyNode, property.BrushProvenance);
        AppendRuntimeAXAMLResources(propertyNode, runtimeResourceMatches);
        AppendAXAMLProvenance(propertyNode, axamlEntries);

        return propertyNode;
    }

    private static void AppendInstrumentedAssignments(
        ControlHoverInspectorNode propertyNode,
        DebugPropertyAssignmentHistory history)
    {
        if (history.TotalAssignmentCount == 0) return;

        ControlHoverInspectorNode assignmentsNode = new(
            $"Instrumented C# assignments ({history.TotalAssignmentCount})",
            isExpanded: false);
        long omittedAssignmentCount = history.TotalAssignmentCount - history.Assignments.Count;
        if (omittedAssignmentCount > 0)
        {
            assignmentsNode.Children.Add(new ControlHoverInspectorNode(
                $"... {omittedAssignmentCount} earlier assignments omitted"));
        }

        for (int index = 0; index < history.Assignments.Count; index++)
        {
            DebugPropertyAssignment assignment = history.Assignments[index];
            ControlHoverInspectorNode assignmentNode = new(
                $"#{assignment.Sequence} [{assignment.Operation}] "
                + $"{Sanitize(assignment.ValueExpression)} = {Sanitize(assignment.ValueDisplay)}");
            string sourcePosition = assignment.SourceColumn > 0
                ? $"{assignment.SourcePath}:{assignment.SourceLine}:{assignment.SourceColumn}"
                : $"{assignment.SourcePath}:{assignment.SourceLine}";
            assignmentNode.Children.Add(new ControlHoverInspectorNode(
                $"Source: {sourcePosition} ({assignment.SourceMember})"));
            assignmentNode.Children.Add(new ControlHoverInspectorNode(
                $"Recorded: {assignment.Timestamp:HH:mm:ss.fff} UTC; thread {assignment.ManagedThreadID}"));
            assignmentNode.Children.Add(new ControlHoverInspectorNode(
                $"Assigned value type: {assignment.ValueTypeName}"));
            if (!string.IsNullOrWhiteSpace(assignment.ResourceKey))
            {
                assignmentNode.Children.Add(new ControlHoverInspectorNode($"Resource key: {assignment.ResourceKey}"));
                AppendResourceDefinitions(assignmentNode, assignment.ResourceKey);
            }

            assignmentsNode.Children.Add(assignmentNode);
        }

        propertyNode.Children.Add(assignmentsNode);
    }

    private static void AppendAssignedBrushProvenance(
        ControlHoverInspectorNode propertyNode,
        CapturedBrushProvenance? brushProvenance)
    {
        if (!brushProvenance.HasValue) return;

        ControlHoverInspectorNode brushNode = new(
            $"Assigned SolidColorBrush provenance: Color={brushProvenance.Value.ColorDisplay}",
            isExpanded: false);
        AppendInstrumentedAssignments(brushNode, brushProvenance.Value.AssignmentHistory);
        propertyNode.Children.Add(brushNode);
    }

    private static string BuildSourceSummary(
        DebugPropertyAssignmentHistory history,
        IReadOnlyList<RuntimeAXAMLResourceMatch> runtimeResourceMatches,
        IReadOnlyList<AXAMLProvenanceEntry> axamlEntries)
    {
        if (history.Assignments.Count > 0)
        {
            DebugPropertyAssignment latestAssignment = history.Assignments[^1];
            if (!string.IsNullOrWhiteSpace(latestAssignment.ResourceKey))
            {
                string resourceKey = latestAssignment.ResourceKey;
                IReadOnlyList<AXAMLProvenanceEntry> definitions =
                    DebugUIProvenance.FindAXAMLResourceDefinitions(resourceKey);
                return definitions.Count > 0
                    ? AXAMLSourceSummary(resourceKey, definitions[0])
                    : $" <- AXAML {resourceKey}";
            }
        }

        RuntimeAXAMLResourceMatch? confidentMatch = ConfidentRuntimeResourceMatch(runtimeResourceMatches);
        if (confidentMatch.HasValue && confidentMatch.Value.Definitions.Count > 0)
        {
            return AXAMLSourceSummary(
                confidentMatch.Value.ResourceKey,
                confidentMatch.Value.Definitions[0]);
        }

        if (axamlEntries.Count == 1)
        {
            AXAMLProvenanceEntry entry = axamlEntries[0];
            string role = string.IsNullOrWhiteSpace(entry.ResourceKey)
                ? "AXAML"
                : "AXAML " + entry.ResourceKey;
            return $" <- {role} @ {entry.SourcePath}:{entry.Line}";
        }

        if (history.Assignments.Count == 0) return string.Empty;

        DebugPropertyAssignment assignment = history.Assignments[^1];
        return $" <- C# {assignment.SourcePath}:{assignment.SourceLine} ({Sanitize(assignment.ValueExpression)})";
    }

    private static string AXAMLSourceSummary(string resourceKey, AXAMLProvenanceEntry definition) =>
        $" <- AXAML {resourceKey} @ {definition.SourcePath}:{definition.Line}";

    private static RuntimeAXAMLResourceMatch? ConfidentRuntimeResourceMatch(
        IReadOnlyList<RuntimeAXAMLResourceMatch> matches)
    {
        if (matches.Count == 0) return null;

        RuntimeAXAMLResourceMatch bestMatch = matches[0];
        if (matches.Count == 1) return bestMatch;
        if (bestMatch.FamilyScore < 2) return null;

        return bestMatch.FamilyScore > matches[1].FamilyScore ? bestMatch : null;
    }

    private static void AppendRuntimeAXAMLResources(
        ControlHoverInspectorNode propertyNode,
        IReadOnlyList<RuntimeAXAMLResourceMatch> matches)
    {
        if (matches.Count == 0) return;

        RuntimeAXAMLResourceMatch? confidentMatch = ConfidentRuntimeResourceMatch(matches);
        string title = confidentMatch.HasValue
            ? $"Value-matched AXAML resource: {confidentMatch.Value.ResourceKey}"
            : $"Value-matched AXAML resource candidates ({matches.Count})";
        ControlHoverInspectorNode matchesNode = new(title);
        int displayedMatchCount = Math.Min(matches.Count, MaximumDisplayedAXAMLEntriesPerProperty);
        for (int matchIndex = 0; matchIndex < displayedMatchCount; matchIndex++)
        {
            RuntimeAXAMLResourceMatch match = matches[matchIndex];
            ControlHoverInspectorNode matchNode = new(
                $"{match.ResourceKey} = {Sanitize(match.ValueDisplay)}; component score {match.FamilyScore}");
            int displayedDefinitionCount = Math.Min(
                match.Definitions.Count,
                MaximumDisplayedResourceDefinitions);
            for (int definitionIndex = 0; definitionIndex < displayedDefinitionCount; definitionIndex++)
            {
                AXAMLProvenanceEntry definition = match.Definitions[definitionIndex];
                matchNode.Children.Add(new ControlHoverInspectorNode(
                    $"Source: {definition.SourcePath}:{definition.Line}:{definition.Column}"));
            }

            matchesNode.Children.Add(matchNode);
        }

        if (displayedMatchCount < matches.Count)
        {
            matchesNode.Children.Add(new ControlHoverInspectorNode(
                $"... {matches.Count - displayedMatchCount} value matches omitted"));
        }

        propertyNode.Children.Add(matchesNode);
    }

    private static void AppendAXAMLProvenance(
        ControlHoverInspectorNode propertyNode,
        IReadOnlyList<AXAMLProvenanceEntry> entries)
    {
        if (entries.Count == 0) return;

        ControlHoverInspectorNode axamlNode = new(
            $"AXAML source candidates ({entries.Count})",
            isExpanded: false);
        int displayedEntryCount = Math.Min(entries.Count, MaximumDisplayedAXAMLEntriesPerProperty);
        for (int index = 0; index < displayedEntryCount; index++)
            axamlNode.Children.Add(BuildAXAMLProvenanceNode(entries[index]));

        if (displayedEntryCount < entries.Count)
        {
            axamlNode.Children.Add(new ControlHoverInspectorNode(
                $"... {entries.Count - displayedEntryCount} AXAML candidates omitted"));
        }

        propertyNode.Children.Add(axamlNode);
    }

    private static List<string> FindAXAMLOwnerTypeNames(
        TopLevel topLevel,
        AvaloniaObject avaloniaObject)
    {
        List<string> ownerTypeNames = [];
        HashSet<Type> ownerTypes = [];

        if (avaloniaObject is Visual visual)
        {
            HashSet<Visual> visited = new(ReferenceEqualityComparer.Instance);
            Visual? current = visual;
            int examinedElementCount = 0;
            while (current != null
                   && examinedElementCount < MaximumVisualPathElements
                   && visited.Add(current))
            {
                AddTypeHierarchy(ownerTypeNames, ownerTypes, current.GetType());

                current = current.GetVisualParent();
                examinedElementCount++;
            }
        }

        AddTypeHierarchy(ownerTypeNames, ownerTypes, topLevel.GetType());

        return ownerTypeNames;
    }

    private static void AddTypeHierarchy(
        List<string> ownerTypeNames,
        HashSet<Type> ownerTypes,
        Type type)
    {
        Type? currentType = type;
        while (currentType != null && currentType != typeof(object))
        {
            if (ownerTypes.Add(currentType))
                ownerTypeNames.Add(currentType.FullName ?? currentType.Name);

            currentType = currentType.BaseType;
        }
    }

    private static ControlHoverInspectorNode BuildAXAMLProvenanceNode(AXAMLProvenanceEntry entry)
    {
        string description = string.IsNullOrWhiteSpace(entry.ValueExpression)
            ? entry.Kind.ToString()
            : $"{entry.Kind}: {Sanitize(entry.ValueExpression)}";
        ControlHoverInspectorNode entryNode = new(description);
        entryNode.Children.Add(new ControlHoverInspectorNode(
            $"Source: {entry.SourcePath}:{entry.Line}:{entry.Column}"));
        entryNode.Children.Add(new ControlHoverInspectorNode($"Owner: {entry.OwnerTypeName}"));
        entryNode.Children.Add(new ControlHoverInspectorNode($"Element path: {entry.ElementPath}"));

        if (!string.IsNullOrWhiteSpace(entry.ControlName))
            entryNode.Children.Add(new ControlHoverInspectorNode($"Control name: {entry.ControlName}"));
        if (!string.IsNullOrWhiteSpace(entry.Selector))
            entryNode.Children.Add(new ControlHoverInspectorNode($"Selector: {Sanitize(entry.Selector)}"));
        if (!string.IsNullOrWhiteSpace(entry.ResourceKey))
        {
            entryNode.Children.Add(new ControlHoverInspectorNode($"Resource key: {entry.ResourceKey}"));
            AppendResourceDefinitions(entryNode, entry.ResourceKey);
        }

        return entryNode;
    }

    private static void AppendResourceDefinitions(ControlHoverInspectorNode entryNode, string resourceKey)
    {
        IReadOnlyList<AXAMLProvenanceEntry> definitions =
            DebugUIProvenance.FindAXAMLResourceDefinitions(resourceKey);
        if (definitions.Count == 0) return;

        ControlHoverInspectorNode definitionsNode = new(
            $"Resource definition candidates ({definitions.Count})",
            isExpanded: false);
        int displayedDefinitionCount = Math.Min(definitions.Count, MaximumDisplayedResourceDefinitions);
        for (int index = 0; index < displayedDefinitionCount; index++)
        {
            AXAMLProvenanceEntry definition = definitions[index];
            definitionsNode.Children.Add(new ControlHoverInspectorNode(
                $"{definition.SourcePath}:{definition.Line}:{definition.Column} = "
                + Sanitize(definition.ValueExpression ?? "<complex value>")));
        }

        if (displayedDefinitionCount < definitions.Count)
        {
            definitionsNode.Children.Add(new ControlHoverInspectorNode(
                $"... {definitions.Count - displayedDefinitionCount} definitions omitted"));
        }

        entryNode.Children.Add(definitionsNode);
    }

    private static int CompareProperties(AvaloniaProperty left, AvaloniaProperty right)
    {
        int nameComparison = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
        if (nameComparison != 0) return nameComparison;

        return string.Compare(
            left.OwnerType.FullName,
            right.OwnerType.FullName,
            StringComparison.Ordinal);
    }

    internal static string ElementLabel(object element)
    {
        StringBuilder label = new(element.GetType().Name);
        if (element is not StyledElement styledElement) return label.ToString();

        string? displayName = styledElement.Name;
        if (string.IsNullOrWhiteSpace(displayName)
            && ControlNameScope.TryGetDetails(styledElement, out ControlNameDetails? nameDetails))
        {
            displayName = nameDetails!.Name;
        }

        if (!string.IsNullOrWhiteSpace(displayName))
            label.Append('#').Append(displayName);

        int displayedClassCount = Math.Min(styledElement.Classes.Count, MaximumDisplayedClasses);
        for (int index = 0; index < displayedClassCount; index++)
            label.Append('.').Append(styledElement.Classes[index]);

        if (displayedClassCount < styledElement.Classes.Count)
            label.Append(".(+").Append(styledElement.Classes.Count - displayedClassCount).Append(" more)");

        return Sanitize(label.ToString());
    }

    private static string FullTypeName(object value)
    {
        Type type = value.GetType();
        return type.FullName ?? type.Name;
    }

    private static string FormatClasses(StyledElement styledElement)
    {
        if (styledElement.Classes.Count == 0) return "<none>";

        StringBuilder classes = new();
        int displayedClassCount = Math.Min(styledElement.Classes.Count, MaximumDisplayedClasses);
        for (int index = 0; index < displayedClassCount; index++)
        {
            if (classes.Length > 0)
                classes.Append(' ');

            classes.Append(styledElement.Classes[index]);
        }

        if (displayedClassCount < styledElement.Classes.Count)
        {
            classes.Append(" ... (+")
                .Append(styledElement.Classes.Count - displayedClassCount)
                .Append(" more)");
        }

        return Sanitize(classes.ToString());
    }

    private static string FormatValue(object? value)
    {
        string formatted = value switch
        {
            null => "<null>",
            string text => $"\"{text}\"",
            double number => FormatNumber(number),
            float number => FormatNumber(number),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            Rect rect => FormatRect(rect),
            Size size => FormatSize(size),
            Point point => FormatPoint(point),
            PixelPoint point => $"X={point.X}, Y={point.Y}",
            Vector vector => FormatVector(vector),
            Thickness thickness => FormatThickness(thickness),
            Matrix matrix => FormatMatrix(matrix),
            StyledElement styledElement => ElementLabel(styledElement),
            Type type => type.FullName ?? type.Name,
            ICollection collection => $"{value.GetType().Name} (Count={collection.Count})",
            IEnumerable => $"{value.GetType().Name} (enumerable)",
            _ => DebugValueSnapshot.Create(value).Display
        };

        return Sanitize(formatted);
    }

    private static string FormatRect(Rect rect) =>
        $"X={FormatNumber(rect.X)}, Y={FormatNumber(rect.Y)}, " +
        $"W={FormatNumber(rect.Width)}, H={FormatNumber(rect.Height)}";

    private static string FormatSize(Size size) =>
        $"W={FormatNumber(size.Width)}, H={FormatNumber(size.Height)}";

    private static string FormatPoint(Point point) =>
        $"X={FormatNumber(point.X)}, Y={FormatNumber(point.Y)}";

    private static string FormatVector(Vector vector) =>
        $"X={FormatNumber(vector.X)}, Y={FormatNumber(vector.Y)}";

    private static string FormatThickness(Thickness thickness) =>
        $"L={FormatNumber(thickness.Left)}, T={FormatNumber(thickness.Top)}, " +
        $"R={FormatNumber(thickness.Right)}, B={FormatNumber(thickness.Bottom)}";

    private static string FormatMatrix(Matrix matrix) =>
        $"[{FormatNumber(matrix.M11)}, {FormatNumber(matrix.M12)}, " +
        $"{FormatNumber(matrix.M21)}, {FormatNumber(matrix.M22)}, " +
        $"{FormatNumber(matrix.M31)}, {FormatNumber(matrix.M32)}]";

    private static string FormatNumber(double value)
    {
        if (double.IsNaN(value)) return "Auto";
        if (double.IsPositiveInfinity(value)) return "+Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Sanitize(string value)
    {
        string singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (singleLine.Length <= MaximumDisplayedValueLength) return singleLine;
        return string.Concat(singleLine.AsSpan(0, MaximumDisplayedValueLength - 3), "...");
    }
}
#endif
