using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace TrayAppDotNETCommon.UI.ControlMapping;

/// <summary>
/// One activation of a control, from a click or from the keyboard: the control activated and the modifiers held.
/// The property names match the pointer event arguments, so click handlers read the same members.
/// </summary>
public readonly record struct ControlActivation(Control Source, KeyModifiers KeyModifiers);

/// <summary>
/// Attaches control map identity to code-built controls. A tagged control takes its tab index, tab navigation, focus
/// behavior, and accelerators from the map once it joins a window.
/// </summary>
public static class ControlMapBinding
{
    /// <summary>Defines the map node a control represents.</summary>
    public static readonly AttachedProperty<ControlMapNodeID?> NodeProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, ControlMapNodeID?>("Node");

    /// <summary>Defines the layout variants active for a control and everything below it.</summary>
    public static readonly AttachedProperty<string[]?> VariantsProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, string[]?>("Variants");

    private static readonly ConditionalWeakTable<Control, Action<ControlActivation>> Activations = new();
    private static readonly ConditionalWeakTable<Control, Dictionary<ControlMapNodeID, Action<ControlActivation>>>
        Commands = new();
    private static readonly ConditionalWeakTable<Control, object> Hooked = new();
    private static readonly object HookedMarker = new();

    static ControlMapBinding()
    {
        NodeProperty.Changed.AddClassHandler<Control>(OnNodeChanged);
        VariantsProperty.Changed.AddClassHandler<Control>(OnVariantsChanged);
    }

    /// <summary>Tags a control with the map node it represents and returns the same control.</summary>
    public static TControl MapTo<TControl>(this TControl control, ControlMapNodeID id) where TControl : Control
    {
        control.SetValue(NodeProperty, id);
        return control;
    }

    /// <summary>Tags a control when an id is given; factories pass their optional node parameter through this.</summary>
    public static TControl MapTo<TControl>(this TControl control, ControlMapNodeID? id) where TControl : Control =>
        id is { } nodeID ? control.MapTo(nodeID) : control;

    /// <summary>Returns the map node a control was tagged with, or null.</summary>
    public static ControlMapNodeID? GetNode(Control control) => control.GetValue(NodeProperty);

    /// <summary>
    /// Registers how a control activates, so Enter and Space on the focused control and any accelerator that targets
    /// it run the same code a click does. Controls that already handle those keys themselves need it only for
    /// accelerators.
    /// </summary>
    public static TControl MapActivation<TControl>(this TControl control, Action<ControlActivation> activate)
        where TControl : Control
    {
        Activations.AddOrUpdate(control, activate);
        return control;
    }

    /// <summary>Returns the activation registered for a control, or null.</summary>
    public static Action<ControlActivation>? GetActivation(Control control) =>
        Activations.TryGetValue(control, out Action<ControlActivation>? activate) ? activate : null;

    /// <summary>
    /// Registers the handler of a Command leaf. The dispatcher finds it on the control bound to the command's key
    /// scope or on any ancestor, so registering on the window covers a whole surface.
    /// </summary>
    public static TControl MapCommand<TControl>(
        this TControl owner,
        ControlMapNodeID command,
        Action<ControlActivation> handler)
        where TControl : Control
    {
        Dictionary<ControlMapNodeID, Action<ControlActivation>> commands =
            Commands.GetValue(owner, static _ => []);
        commands[command] = handler;
        return owner;
    }

    /// <summary>Registers the handler of a Command leaf that needs no activation context.</summary>
    public static TControl MapCommand<TControl>(this TControl owner, ControlMapNodeID command, Action handler)
        where TControl : Control =>
        owner.MapCommand(command, _ => handler());

    /// <summary>Returns the handler a control registered for a command, or null.</summary>
    public static Action<ControlActivation>? GetCommand(Control owner, ControlMapNodeID command) =>
        Commands.TryGetValue(owner, out Dictionary<ControlMapNodeID, Action<ControlActivation>>? commands)
        && commands.TryGetValue(command, out Action<ControlActivation>? handler)
            ? handler
            : null;

    /// <summary>
    /// Turns a layout variant on or off for a control and its descendants. Containers that declare the variant use
    /// its child order while it is active.
    /// </summary>
    public static TControl MapVariant<TControl>(this TControl control, string variant, bool isActive = true)
        where TControl : Control
    {
        string[] current = control.GetValue(VariantsProperty) ?? [];
        bool isPresent = Array.IndexOf(current, variant) >= 0;
        if (isPresent == isActive) return control;

        List<string> updated = [..current];
        if (isActive)
            updated.Add(variant);
        else
            updated.Remove(variant);

        control.SetValue(VariantsProperty, updated.Count == 0 ? null : updated.ToArray());
        return control;
    }

    private static void OnNodeChanged(Control control, AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (!Hooked.TryGetValue(control, out _))
        {
            Hooked.Add(control, HookedMarker);
            control.AttachedToVisualTree += OnAttachedToVisualTree;
            control.DetachedFromVisualTree += OnDetachedFromVisualTree;
        }

        TopLevel? topLevel = TopLevel.GetTopLevel(control);
        if (topLevel == null) return;

        ControlMapNavigator.For(topLevel).BindSubtree(control);
    }

    private static void OnVariantsChanged(Control control, AvaloniaPropertyChangedEventArgs eventArgs)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(control);
        if (topLevel == null) return;

        ControlMapNavigator.For(topLevel).Reapply(control);
    }

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs eventArgs)
    {
        if (sender is not Control control || GetNode(control) == null) return;

        TopLevel? topLevel = TopLevel.GetTopLevel(control);
        if (topLevel == null) return;

        ControlMapNavigator.For(topLevel).Bind(control);
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs eventArgs)
    {
        if (sender is not Control control) return;

        ControlMapNavigator.Unbind(control);
    }
}
