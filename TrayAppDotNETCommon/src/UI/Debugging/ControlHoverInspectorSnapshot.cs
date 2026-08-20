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

/// <summary>Captures effective Avalonia values and the visual layout chain for a hover target.</summary>
internal static class ControlHoverInspectorSnapshotBuilder
{
    private const int MaximumVisualPathElements = 32;
    private const int MaximumDisplayedValueLength = 240;

    private static readonly HashSet<string> LayoutPropertyNames = new(StringComparer.Ordinal)
    {
        "Bottom",
        "BorderThickness",
        "Bounds",
        "Clip",
        "ClipToBounds",
        "Column",
        "ColumnDefinitions",
        "ColumnSpan",
        "Dock",
        "Extent",
        "FlowDirection",
        "Height",
        "HorizontalAlignment",
        "HorizontalContentAlignment",
        "HorizontalScrollBarVisibility",
        "IsScrollChainingEnabled",
        "Left",
        "Margin",
        "MaxHeight",
        "MaxWidth",
        "MinHeight",
        "MinWidth",
        "Offset",
        "Orientation",
        "Padding",
        "RenderTransform",
        "RenderTransformOrigin",
        "Right",
        "Row",
        "RowDefinitions",
        "RowSpan",
        "Spacing",
        "Stretch",
        "StretchDirection",
        "Top",
        "UseLayoutRounding",
        "VerticalAlignment",
        "VerticalContentAlignment",
        "VerticalScrollBarVisibility",
        "Viewport",
        "Width"
    };

    /// <summary>Builds a complete snapshot for the element currently under the pointer.</summary>
    public static ControlHoverInspectorSnapshot Build(TopLevel topLevel, IInputElement hitElement)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        ArgumentNullException.ThrowIfNull(hitElement);

        ControlNameScope.For(topLevel).AssignVisualTree();

        List<ControlHoverInspectorNode> roots = [];
        roots.Add(BuildIdentityNode(topLevel, hitElement));

        if (hitElement is Visual hitVisual)
            roots.Add(BuildLayoutAncestryNode(topLevel, hitVisual));

        if (hitElement is AvaloniaObject avaloniaObject)
        {
            roots.Add(BuildPropertyTree(
                avaloniaObject,
                "Effective Avalonia properties",
                static property => true,
                expandRoot: true));
        }

        return new ControlHoverInspectorSnapshot(ElementLabel(hitElement), roots);
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
            ControlNameScope.TryGetDetails(styledElement, out ControlNameDetails? nameDetails);
            identity.Children.Add(new ControlHoverInspectorNode(
                $"Name: {(string.IsNullOrWhiteSpace(styledElement.Name) ? "<unnamed>" : styledElement.Name)}"));
            if (nameDetails != null)
            {
                if (string.IsNullOrWhiteSpace(styledElement.Name))
                    identity.Children.Add(new ControlHoverInspectorNode($"Generated debug name: {nameDetails.Name}"));

                string nameSource = nameDetails.Origin switch
                {
                    ControlNameOrigin.Explicit => "explicit source name",
                    ControlNameOrigin.TopLevel => "generated top-level name",
                    ControlNameOrigin.Source => "generated during source construction",
                    ControlNameOrigin.VisualFallback => "generated from the realized visual tree",
                    _ => nameDetails.Origin.ToString()
                };
                identity.Children.Add(new ControlHoverInspectorNode($"Name source: {nameSource}"));
            }

            identity.Children.Add(new ControlHoverInspectorNode(
                $"Classes: {(styledElement.Classes.Count == 0 ? "<none>" : string.Join(" ", styledElement.Classes))}"));
            identity.Children.Add(new ControlHoverInspectorNode(
                $"DataContext: {(styledElement.DataContext == null ? "<null>" : FullTypeName(styledElement.DataContext))}"));
        }

        if (hitElement is Control control)
        {
            identity.Children.Add(new ControlHoverInspectorNode(
                $"Enabled: {control.IsEffectivelyEnabled}; visible: {control.IsEffectivelyVisible}; hit test: {control.IsHitTestVisible}"));
        }

        if (hitElement is Visual hitVisual)
        {
            Control? semanticControl = FindNearestNamedOrCustomControl(hitVisual);
            if (semanticControl != null && !ReferenceEquals(semanticControl, hitElement))
            {
                identity.Children.Add(new ControlHoverInspectorNode(
                    $"Nearest named/custom control: {ElementLabel(semanticControl)}"));
            }
        }

        return identity;
    }

    private static ControlHoverInspectorNode BuildLayoutAncestryNode(TopLevel topLevel, Visual hitVisual)
    {
        List<Visual> ancestry = [];
        Visual? current = hitVisual;
        while (current != null && ancestry.Count < MaximumVisualPathElements)
        {
            ancestry.Add(current);
            current = current.GetVisualParent();
        }

        ControlHoverInspectorNode root = new($"Layout ancestry: target to root ({ancestry.Count})", isExpanded: true);
        for (int index = 0; index < ancestry.Count; index++)
        {
            Visual visual = ancestry[index];
            string relationship = index == 0 ? "target" : $"parent {index}";
            ControlHoverInspectorNode visualNode = new(
                $"[{relationship}] {ElementLabel(visual)}",
                isExpanded: index <= 1);
            AppendGeometry(visualNode, topLevel, visual);
            AppendLayoutMetrics(visualNode, visual);
            visualNode.Children.Add(BuildPropertyTree(
                visual,
                "Layout-related Avalonia properties",
                static property => LayoutPropertyNames.Contains(property.Name),
                expandRoot: false));
            root.Children.Add(visualNode);
        }

        if (current != null)
            root.Children.Add(new ControlHoverInspectorNode("... visual ancestry truncated"));

        return root;
    }

    private static void AppendGeometry(
        ControlHoverInspectorNode visualNode,
        TopLevel topLevel,
        Visual visual)
    {
        visualNode.Children.Add(new ControlHoverInspectorNode($"Bounds in parent: {FormatRect(visual.Bounds)}"));

        Matrix? transform = visual.TransformToVisual(topLevel);
        if (transform.HasValue)
        {
            Matrix matrix = transform.Value;
            double cumulativeScaleX = Math.Sqrt((matrix.M11 * matrix.M11) + (matrix.M12 * matrix.M12));
            double cumulativeScaleY = Math.Sqrt((matrix.M21 * matrix.M21) + (matrix.M22 * matrix.M22));
            visualNode.Children.Add(new ControlHoverInspectorNode(
                $"Transform to top level: {FormatMatrix(matrix)}"));
            visualNode.Children.Add(new ControlHoverInspectorNode(
                $"Cumulative scale to top level: X={FormatNumber(cumulativeScaleX)}, Y={FormatNumber(cumulativeScaleY)}"));
        }
        else
        {
            visualNode.Children.Add(new ControlHoverInspectorNode("Transform to top level: <unavailable>"));
        }

        Point? topLevelOrigin = visual.TranslatePoint(new Point(0, 0), topLevel);
        if (topLevelOrigin.HasValue)
        {
            visualNode.Children.Add(new ControlHoverInspectorNode(
                $"Origin in top level: {FormatPoint(topLevelOrigin.Value)}"));
        }

        try
        {
            PixelPoint screenOrigin = visual.PointToScreen(new Point(0, 0));
            visualNode.Children.Add(new ControlHoverInspectorNode(
                $"Origin on screen: X={screenOrigin.X}, Y={screenOrigin.Y} physical pixels"));
        }
        catch (ArgumentException)
        {
            visualNode.Children.Add(new ControlHoverInspectorNode("Origin on screen: <not attached>"));
        }
    }

    private static void AppendLayoutMetrics(ControlHoverInspectorNode visualNode, Visual visual)
    {
        if (visual is Layoutable layoutable)
        {
            visualNode.Children.Add(new ControlHoverInspectorNode($"Desired size: {FormatSize(layoutable.DesiredSize)}"));
            visualNode.Children.Add(new ControlHoverInspectorNode($"Margin: {FormatThickness(layoutable.Margin)}"));
            visualNode.Children.Add(new ControlHoverInspectorNode(
                $"Requested size: Width={FormatNumber(layoutable.Width)}, Height={FormatNumber(layoutable.Height)}"));
            visualNode.Children.Add(new ControlHoverInspectorNode(
                $"Width constraints: Min={FormatNumber(layoutable.MinWidth)}, Max={FormatNumber(layoutable.MaxWidth)}"));
            visualNode.Children.Add(new ControlHoverInspectorNode(
                $"Height constraints: Min={FormatNumber(layoutable.MinHeight)}, Max={FormatNumber(layoutable.MaxHeight)}"));
            visualNode.Children.Add(new ControlHoverInspectorNode(
                $"Alignment: Horizontal={layoutable.HorizontalAlignment}, Vertical={layoutable.VerticalAlignment}"));
            visualNode.Children.Add(new ControlHoverInspectorNode(
                $"Layout rounding: {layoutable.UseLayoutRounding}"));
        }

        if (visual is Control control)
        {
            visualNode.Children.Add(new ControlHoverInspectorNode(
                $"Grid placement: Row={Grid.GetRow(control)}, Column={Grid.GetColumn(control)}, " +
                $"RowSpan={Grid.GetRowSpan(control)}, ColumnSpan={Grid.GetColumnSpan(control)}"));
        }

        if (visual is Grid grid)
        {
            ControlHoverInspectorNode definitions = new("Grid definitions", isExpanded: false);
            for (int index = 0; index < grid.RowDefinitions.Count; index++)
            {
                RowDefinition row = grid.RowDefinitions[index];
                definitions.Children.Add(new ControlHoverInspectorNode(
                    $"Row {index}: {row.Height}; actual={FormatNumber(row.ActualHeight)}"));
            }

            for (int index = 0; index < grid.ColumnDefinitions.Count; index++)
            {
                ColumnDefinition column = grid.ColumnDefinitions[index];
                definitions.Children.Add(new ControlHoverInspectorNode(
                    $"Column {index}: {column.Width}; actual={FormatNumber(column.ActualWidth)}"));
            }

            if (definitions.Children.Count == 0)
                definitions.Children.Add(new ControlHoverInspectorNode("<implicit single cell>"));

            visualNode.Children.Add(definitions);
        }

        if (visual is ScrollViewer scrollViewer)
        {
            visualNode.Children.Add(new ControlHoverInspectorNode(
                $"Scroll: Offset={FormatVector(scrollViewer.Offset)}, Extent={FormatSize(scrollViewer.Extent)}, " +
                $"Viewport={FormatSize(scrollViewer.Viewport)}"));
        }
    }

    private static ControlHoverInspectorNode BuildPropertyTree(
        AvaloniaObject avaloniaObject,
        string title,
        Predicate<AvaloniaProperty> includeProperty,
        bool expandRoot)
    {
        List<AvaloniaProperty> properties = [];
        HashSet<AvaloniaProperty> seenProperties = [];
        IEnumerable<AvaloniaProperty> registeredProperties =
            AvaloniaPropertyRegistry.Instance.GetRegistered(avaloniaObject);
        foreach (AvaloniaProperty property in registeredProperties)
        {
            if (!includeProperty(property) || !seenProperties.Add(property)) continue;
            properties.Add(property);
        }

        properties.Sort(CompareProperties);

        List<ControlHoverInspectorNode> appliedProperties = [];
        List<ControlHoverInspectorNode> defaultProperties = [];
        foreach (AvaloniaProperty property in properties)
        {
            try
            {
                AvaloniaPropertyValue diagnostic = avaloniaObject.GetDiagnostic(property);
                bool isSet = avaloniaObject.IsSet(property);
                ControlHoverInspectorNode propertyNode = BuildPropertyNode(property, diagnostic, isSet);
                if (isSet || diagnostic.Priority != BindingPriority.Unset)
                    appliedProperties.Add(propertyNode);
                else
                    defaultProperties.Add(propertyNode);
            }
            catch (Exception exception)
            {
                appliedProperties.Add(new ControlHoverInspectorNode(
                    $"{property.Name} = <unavailable: {exception.GetType().Name}: {Sanitize(exception.Message)}>"));
            }
        }

        ControlHoverInspectorNode root = new($"{title} ({properties.Count})", expandRoot);
        ControlHoverInspectorNode appliedRoot = new(
            $"Applied / inherited values ({appliedProperties.Count})",
            isExpanded: true);
        foreach (ControlHoverInspectorNode propertyNode in appliedProperties)
            appliedRoot.Children.Add(propertyNode);

        if (appliedRoot.Children.Count == 0)
            appliedRoot.Children.Add(new ControlHoverInspectorNode("<none>"));

        ControlHoverInspectorNode defaultsRoot = new($"Defaults ({defaultProperties.Count})", isExpanded: false);
        foreach (ControlHoverInspectorNode propertyNode in defaultProperties)
            defaultsRoot.Children.Add(propertyNode);

        if (defaultsRoot.Children.Count == 0)
            defaultsRoot.Children.Add(new ControlHoverInspectorNode("<none>"));

        root.Children.Add(appliedRoot);
        root.Children.Add(defaultsRoot);
        return root;
    }

    private static ControlHoverInspectorNode BuildPropertyNode(
        AvaloniaProperty property,
        AvaloniaPropertyValue diagnostic,
        bool isSet)
    {
        ControlHoverInspectorNode propertyNode = new(
            $"{property.Name} = {FormatValue(diagnostic.Value)} [{diagnostic.Priority}]");
        propertyNode.Children.Add(new ControlHoverInspectorNode(
            $"Owner: {property.OwnerType.FullName ?? property.OwnerType.Name}"));
        propertyNode.Children.Add(new ControlHoverInspectorNode(
            $"Value type: {property.PropertyType.FullName ?? property.PropertyType.Name}"));
        propertyNode.Children.Add(new ControlHoverInspectorNode($"Is set: {isSet}"));

        if (!string.IsNullOrWhiteSpace(diagnostic.Diagnostic))
            propertyNode.Children.Add(new ControlHoverInspectorNode($"Source: {Sanitize(diagnostic.Diagnostic)}"));

        if (diagnostic.IsOverriddenCurrentValue)
            propertyNode.Children.Add(new ControlHoverInspectorNode("SetCurrentValue override: true"));

        return propertyNode;
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

    private static Control? FindNearestNamedOrCustomControl(Visual hitVisual)
    {
        Control? fallback = null;
        Visual? current = hitVisual;
        while (current != null)
        {
            if (current is Control candidate)
            {
                fallback ??= candidate;
                if (!string.IsNullOrWhiteSpace(candidate.Name) || !IsAvaloniaFrameworkType(candidate.GetType()))
                    return candidate;
            }

            current = current.GetVisualParent();
        }

        return fallback;
    }

    private static bool IsAvaloniaFrameworkType(Type type)
    {
        string? namespaceName = type.Namespace;
        if (namespaceName == null) return false;

        return string.Equals(namespaceName, "Avalonia", StringComparison.Ordinal)
               || namespaceName.StartsWith("Avalonia.", StringComparison.Ordinal);
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

        foreach (string className in styledElement.Classes)
            label.Append('.').Append(className);

        return label.ToString();
    }

    private static string FullTypeName(object value)
    {
        Type type = value.GetType();
        return type.FullName ?? type.Name;
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
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? $"<{value.GetType().Name}>"
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
