#if DEBUG
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace TrayAppDotNETCommon.UI;

/// <summary>Assigns readable, top-level-unique names to programmatically created controls.</summary>
public sealed class ControlNameScope
{
    private const int MaximumParentTokenLength = 48;
    private const string GeneratedIndexFormat = "D4";

    private static readonly ConditionalWeakTable<TopLevel, ControlNameScope> Scopes = new();
    private static readonly ConditionalWeakTable<StyledElement, ControlNameDetails> NameDetails = new();

    private readonly Lock _nameLock = new();
    private readonly TopLevel _topLevel;
    private readonly string _topLevelToken;
    private readonly Dictionary<string, WeakReference<StyledElement>> _claimedNames =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedIssues = new(StringComparer.Ordinal);
    private int _nextIndex;

    private ControlNameScope(TopLevel topLevel)
    {
        _topLevel = topLevel;
        _topLevelToken = SanitizeToken(topLevel.GetType().Name, "TopLevel");

        if (string.IsNullOrWhiteSpace(topLevel.Name))
        {
            bool assignedAvaloniaName = TrySetName(topLevel, _topLevelToken);
            ControlNameOrigin origin = assignedAvaloniaName
                ? ControlNameOrigin.TopLevel
                : ControlNameOrigin.VisualFallback;
            RegisterDetails(topLevel, new ControlNameDetails(_topLevelToken, _topLevelToken, 0, origin));
            if (!assignedAvaloniaName)
            {
                ReportIssue(
                    $"{topLevel.GetType().Name} created its naming scope after Avalonia styling; " +
                    "create the scope in the window constructor.");
            }
        }
        else
        {
            RegisterExistingName(topLevel);
        }
    }

    /// <summary>Gets the naming scope whose monotonic index belongs to one top-level instance.</summary>
    public static ControlNameScope For(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        return Scopes.GetValue(topLevel, static value => new ControlNameScope(value));
    }

    /// <summary>Names a control using a semantic parent token and returns the same control.</summary>
    public TControl Assign<TControl>(TControl control, string parentName)
        where TControl : Control
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentName);

        AssignControl(control, SanitizeToken(parentName, _topLevelToken), ControlNameOrigin.Source);
        return control;
    }

    /// <summary>Names a control from its parent identity and returns the same control.</summary>
    public TControl Assign<TControl>(TControl control, StyledElement parent)
        where TControl : Control
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(parent);

        AssignControl(control, ResolveParentToken(parent), ControlNameOrigin.Source);
        return control;
    }

    /// <summary>Names every unnamed control in a logical subtree using a semantic parent token.</summary>
    public void AssignLogicalSubtree(Control root, string parentName)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentName);
        AssignLogicalSubtreeCore(root, SanitizeToken(parentName, _topLevelToken));
    }

    /// <summary>Names every unnamed control in a logical subtree using the supplied parent identity.</summary>
    public void AssignLogicalSubtree(Control root, StyledElement parent)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(parent);
        AssignLogicalSubtreeCore(root, ResolveParentToken(parent));
    }

    /// <summary>Names realized visual controls and reports naming-policy violations in Debug builds.</summary>
    public void AssignVisualTree()
    {
        List<(Control Control, Control? Parent)> controls = CollectVisualControls();
        RegisterExistingNames(controls);

        foreach ((Control control, Control? parent) in controls)
        {
            string parentToken = parent == null ? _topLevelToken : ResolveParentToken(parent);
            AssignControl(control, parentToken, ControlNameOrigin.VisualFallback);
        }

        Audit(controls);
    }

    internal static bool TryGetDetails(StyledElement element, out ControlNameDetails? details) =>
        NameDetails.TryGetValue(element, out details);

    private void AssignLogicalSubtreeCore(Control root, string rootParentToken)
    {
        List<(Control Control, Control? Parent)> controls = CollectLogicalControls(root);
        RegisterExistingNames(controls);

        foreach ((Control control, Control? parent) in controls)
        {
            string parentToken = parent == null ? rootParentToken : ResolveParentToken(parent);
            AssignControl(control, parentToken, ControlNameOrigin.Source);
        }
    }

    private static List<(Control Control, Control? Parent)> CollectLogicalControls(Control root)
    {
        List<(Control Control, Control? Parent)> controls = [];
        HashSet<Control> visited = new(ReferenceEqualityComparer.Instance);
        Stack<(Control Control, Control? Parent)> pending = [];
        pending.Push((root, null));

        while (pending.Count > 0)
        {
            (Control control, Control? parent) = pending.Pop();
            if (!visited.Add(control)) continue;

            controls.Add((control, parent));
            List<Control> children = [];
            foreach (ILogical logicalChild in control.GetLogicalChildren())
            {
                if (logicalChild is Control childControl)
                    children.Add(childControl);
            }

            for (int index = children.Count - 1; index >= 0; index--)
                pending.Push((children[index], control));
        }

        return controls;
    }

    private List<(Control Control, Control? Parent)> CollectVisualControls()
    {
        List<(Control Control, Control? Parent)> controls = [];
        HashSet<Visual> visited = new(ReferenceEqualityComparer.Instance);
        Stack<(Visual Visual, Control? Parent)> pending = [];
        pending.Push((_topLevel, null));

        while (pending.Count > 0)
        {
            (Visual visual, Control? parent) = pending.Pop();
            if (!visited.Add(visual)) continue;

            Control? currentParent = parent;
            if (visual is Control control)
            {
                controls.Add((control, parent));
                currentParent = control;
            }

            List<Visual> children = [];
            foreach (Visual visualChild in visual.GetVisualChildren())
                children.Add(visualChild);

            for (int index = children.Count - 1; index >= 0; index--)
                pending.Push((children[index], currentParent));
        }

        return controls;
    }

    private void RegisterExistingNames(List<(Control Control, Control? Parent)> controls)
    {
        foreach ((Control control, _) in controls)
        {
            if (!string.IsNullOrWhiteSpace(control.Name))
                RegisterExistingName(control);
        }
    }

    private void AssignControl(Control control, string parentToken, ControlNameOrigin origin)
    {
        if (NameDetails.TryGetValue(control, out _))
        {
            if (!string.IsNullOrWhiteSpace(control.Name))
                ClaimExistingName(control, control.Name);
            return;
        }

        if (!string.IsNullOrWhiteSpace(control.Name))
        {
            RegisterExistingName(control);
            return;
        }

        string controlType = SanitizeToken(ControlTypeName(control.GetType()), "Control");
        int index = Interlocked.Increment(ref _nextIndex);
        string generatedName =
            $"{controlType}_{parentToken}_{index.ToString(GeneratedIndexFormat, CultureInfo.InvariantCulture)}";

        bool assignedAvaloniaName;
        ControlNameOrigin requestedOrigin = origin;
        lock (_nameLock)
        {
            while (IsClaimedByAnotherControl(generatedName, control))
            {
                index = Interlocked.Increment(ref _nextIndex);
                generatedName =
                    $"{controlType}_{parentToken}_{index.ToString(GeneratedIndexFormat, CultureInfo.InvariantCulture)}";
            }

            assignedAvaloniaName = TrySetName(control, generatedName);
            if (!assignedAvaloniaName)
                origin = ControlNameOrigin.VisualFallback;
            _claimedNames[generatedName] = new WeakReference<StyledElement>(control);
        }

        if (!assignedAvaloniaName && requestedOrigin == ControlNameOrigin.Source)
        {
            ReportIssue(
                $"Source naming reached {control.GetType().Name} after Avalonia styling in " +
                $"{_topLevel.GetType().Name}; assign it before adding it to a live parent.");
        }

        string token =
            $"{controlType}{index.ToString(GeneratedIndexFormat, CultureInfo.InvariantCulture)}";
        RegisterDetails(control, new ControlNameDetails(generatedName, token, index, origin));
    }

    private void RegisterExistingName(StyledElement element)
    {
        string? name = element.Name;
        if (string.IsNullOrWhiteSpace(name)) return;

        if (NameDetails.TryGetValue(element, out ControlNameDetails? existingDetails)
            && string.Equals(existingDetails.Name, name, StringComparison.Ordinal))
        {
            ClaimExistingName(element, name);
            return;
        }

        string token = SanitizeToken(name, _topLevelToken);
        RegisterDetails(element, new ControlNameDetails(name, token, 0, ControlNameOrigin.Explicit));
        ClaimExistingName(element, name);
    }

    private void ClaimExistingName(StyledElement element, string name)
    {
        bool duplicateAttachedControl = false;
        lock (_nameLock)
        {
            if (IsClaimedByAnotherControl(name, element))
            {
                WeakReference<StyledElement> existingReference = _claimedNames[name];
                if (existingReference.TryGetTarget(out StyledElement? existingElement)
                    && existingElement is Visual existingVisual
                    && element is Visual visual
                    && ReferenceEquals(TopLevel.GetTopLevel(existingVisual), _topLevel)
                    && ReferenceEquals(TopLevel.GetTopLevel(visual), _topLevel))
                {
                    duplicateAttachedControl = true;
                }
            }

            if (!duplicateAttachedControl)
                _claimedNames[name] = new WeakReference<StyledElement>(element);
        }

        if (duplicateAttachedControl)
        {
            ReportIssue(
                $"Duplicate control name '{name}' in {_topLevel.GetType().Name}: " +
                $"{element.GetType().Name} conflicts with another attached control.");
        }
    }

    private bool IsClaimedByAnotherControl(string name, StyledElement element)
    {
        if (!_claimedNames.TryGetValue(name, out WeakReference<StyledElement>? reference)) return false;
        if (!reference.TryGetTarget(out StyledElement? target)) return false;
        return !ReferenceEquals(target, element);
    }

    private string ResolveParentToken(StyledElement parent)
    {
        if (NameDetails.TryGetValue(parent, out ControlNameDetails? details))
            return details.Token;

        if (string.IsNullOrWhiteSpace(parent.Name) && parent is Control parentControl)
            AssignControl(parentControl, _topLevelToken, ControlNameOrigin.Source);
        else
            RegisterExistingName(parent);

        return NameDetails.TryGetValue(parent, out details)
            ? details.Token
            : _topLevelToken;
    }

    private void Audit(List<(Control Control, Control? Parent)> controls)
    {
        Dictionary<string, Control> names = new(StringComparer.Ordinal);
        foreach ((Control control, _) in controls)
        {
            string? name = control.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                if (!NameDetails.TryGetValue(control, out _))
                    ReportIssue($"Unnamed {control.GetType().Name} remains in {_topLevel.GetType().Name}.");
                continue;
            }

            if (!IsValidIdentifier(name))
            {
                ReportIssue(
                    $"Control name '{name}' on {control.GetType().Name} in {_topLevel.GetType().Name} " +
                    "is not a valid identifier.");
            }

            if (names.TryGetValue(name, out Control? duplicate) && !ReferenceEquals(duplicate, control))
            {
                ReportIssue(
                    $"Duplicate control name '{name}' in {_topLevel.GetType().Name}: " +
                    $"{duplicate.GetType().Name} and {control.GetType().Name}.");
                continue;
            }

            names[name] = control;
        }
    }

    private void ReportIssue(string issue)
    {
        lock (_nameLock)
        {
            if (!_reportedIssues.Add(issue)) return;
        }

        LogIssue(issue);
    }

    [Conditional("DEBUG")]
    private static void LogIssue(string issue) => TADNLog.Log($"Control naming audit: {issue}");

    private static void RegisterDetails(StyledElement element, ControlNameDetails details)
    {
        NameDetails.Remove(element);
        NameDetails.Add(element, details);
    }

    private static bool TrySetName(StyledElement element, string name)
    {
        try
        {
            element.Name = name;
            return true;
        }
        catch (InvalidOperationException)
        {
            // Styled elements retain a debug identity when Avalonia no longer permits Name mutation
            return false;
        }
    }

    private static string ControlTypeName(Type type)
    {
        string typeName = type.Name;
        int genericMarker = typeName.IndexOf('`', StringComparison.Ordinal);
        return genericMarker < 0 ? typeName : typeName[..genericMarker];
    }

    private static string SanitizeToken(string value, string fallback)
    {
        StringBuilder token = new(Math.Min(value.Length, MaximumParentTokenLength));
        foreach (char character in value)
        {
            if (token.Length >= MaximumParentTokenLength) break;

            bool isLetter = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            bool isDigit = character is >= '0' and <= '9';
            token.Append(isLetter || isDigit || character == '_' ? character : '_');
        }

        if (token.Length == 0) return fallback;
        if (token[0] is >= '0' and <= '9') token.Insert(0, 'P');
        return token.ToString();
    }

    private static bool IsValidIdentifier(string value)
    {
        if (value.Length == 0) return false;

        char first = value[0];
        bool validFirst = first == '_'
                          || (first is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        if (!validFirst) return false;

        for (int index = 1; index < value.Length; index++)
        {
            char character = value[index];
            bool isLetter = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            bool isDigit = character is >= '0' and <= '9';
            if (!isLetter && !isDigit && character != '_') return false;
        }

        return true;
    }
}

internal enum ControlNameOrigin
{
    Explicit,
    TopLevel,
    Source,
    VisualFallback
}

internal sealed record ControlNameDetails(
    string Name,
    string Token,
    int Index,
    ControlNameOrigin Origin);
#else
using Avalonia;
using Avalonia.Controls;

namespace TrayAppDotNETCommon.UI;

#pragma warning disable CA1822 // Keep the Debug and Release API shapes identical

/// <summary>Provides the source-compatible no-op naming API used by Release builds.</summary>
public sealed class ControlNameScope
{
    private static readonly ControlNameScope Disabled = new();

    private ControlNameScope()
    {
    }

    /// <summary>Returns the shared disabled naming scope.</summary>
    public static ControlNameScope For(TopLevel topLevel) => Disabled;

    /// <summary>Returns the supplied control without naming it.</summary>
    public TControl Assign<TControl>(TControl control, string parentName)
        where TControl : Control => control;

    /// <summary>Returns the supplied control without naming it.</summary>
    public TControl Assign<TControl>(TControl control, StyledElement parent)
        where TControl : Control => control;

    /// <summary>Does nothing because control naming is disabled.</summary>
    public void AssignLogicalSubtree(Control root, string parentName)
    {
    }

    /// <summary>Does nothing because control naming is disabled.</summary>
    public void AssignLogicalSubtree(Control root, StyledElement parent)
    {
    }

    /// <summary>Does nothing because control naming is disabled.</summary>
    public void AssignVisualTree()
    {
    }
}

#pragma warning restore CA1822
#endif
