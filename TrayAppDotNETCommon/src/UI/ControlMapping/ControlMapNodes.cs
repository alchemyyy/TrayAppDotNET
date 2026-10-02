using Avalonia.Input;
using Avalonia.Metadata;

namespace TrayAppDotNETCommon.UI.ControlMapping;

/// <summary>Kind of focus root a control map surface represents.</summary>
public enum SurfaceKind : byte
{
    Window,
    Popup,
    Tray,
    Global
}

/// <summary>Interaction model of a control map leaf; each kind implies its own activation keys.</summary>
public enum LeafKind : byte
{
    Button,
    Toggle,
    Option,
    Select,
    Slider,
    Number,
    Text,
    Navigation,
    ListItem,
    Region,
    Handle,
    Command
}

/// <summary>Arrow keys that move focus between the children of a control map scope.</summary>
public enum ArrowNavigation : byte
{
    None,
    Horizontal,
    Vertical,
    Spatial
}

/// <summary>How focus reaches the children of a control map scope.</summary>
public enum ScopeEntry : byte
{
    // Tab moves into the children according to the scope's tab mode
    Tab,

    // The focusable container is one stop; Enter steps in and Escape steps out
    Enter
}

/// <summary>Pointer action a control map leaf's Pointer attribute names.</summary>
public enum PointerGestureKind : byte
{
    Click,
    DoubleClick,
    RightClick,
    MiddleClick,
    Wheel,
    Drag
}

/// <summary>One parsed pointer gesture: the modifiers it requires and the pointer action.</summary>
public readonly record struct PointerGesture(KeyModifiers Modifiers, PointerGestureKind Kind);

/// <summary>One node of a control map: a template, surface, scope, slot, or leaf.</summary>
public abstract class ControlMapNode
{
    /// <summary>Gets or sets the identifier, unique among siblings, that names the generated node id.</summary>
    public string ID { get; set; } = string.Empty;

    /// <summary>Gets or sets the type and member where the handler is attached or the container is built.</summary>
    public string? Source { get; set; }

    /// <summary>Gets or sets a short statement of what the node does.</summary>
    public string? Description { get; set; }

    /// <summary>Gets the containing node, or null for a root-level template or surface.</summary>
    public ControlMapContainer? Parent { get; internal set; }

    /// <summary>Gets the zero-based document position among the parent's children, which is the local tab index.</summary>
    public int Index { get; internal set; }

    /// <summary>Gets the dot-separated ids from the root-level node down to this node.</summary>
    public string Path { get; internal set; } = string.Empty;

    /// <summary>Gets the map that declares this node, set when the map indexes its nodes.</summary>
    public ControlMap? Map { get; internal set; }

    /// <summary>Gets the generated id of this node; valid once the owning map has indexed its nodes.</summary>
    public ControlMapNodeID NodeID => new(Map?.Name ?? string.Empty, Path);
}

/// <summary>Control map node whose children share one focus scope.</summary>
public abstract class ControlMapContainer : ControlMapNode
{
    /// <summary>Gets the child nodes in tab order.</summary>
    [Content]
    public List<ControlMapNode> Children { get; } = [];
}

/// <summary>Reusable scope definition that surfaces and scopes instantiate through their Template attribute.</summary>
public sealed class Template : ControlMapContainer;

/// <summary>
/// Position inside a template where the instantiating node's own children go. A template with several slots names
/// them through ID; children choose one through their Slot attribute and default to the unnamed slot.
/// </summary>
public sealed class Slot : ControlMapNode;

/// <summary>
/// Alternative child order of the containing node, used while code activates the variant named by ID.
/// Children the order does not list are absent in that layout.
/// </summary>
public sealed class Variant : ControlMapNode
{
    private const char IDSeparator = ' ';

    private string[]? _orderIDs;

    /// <summary>Gets or sets the space-separated child ids, and named slot ids, in this variant's tab order.</summary>
    public string Order { get; set; } = string.Empty;

    /// <summary>Gets the parsed order; the control map generator has already validated each id.</summary>
    public IReadOnlyList<string> OrderIDs =>
        _orderIDs ??= Order.Split(IDSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Focus root such as a window, popup, tray icon, or the global hotkey table.</summary>
public sealed class Surface : ControlMapContainer
{
    /// <summary>Gets or sets the kind of focus root.</summary>
    public SurfaceKind Kind { get; set; } = SurfaceKind.Window;

    /// <summary>Gets or sets how Tab moves through the surface.</summary>
    public KeyboardNavigationMode Tab { get; set; } = KeyboardNavigationMode.Cycle;

    /// <summary>Gets or sets the id of the template that supplies the surface's structure, if any.</summary>
    public string? Template { get; set; }
}

/// <summary>Group of control map nodes that owns tab and arrow behavior for its children.</summary>
public sealed class Scope : ControlMapContainer
{
    /// <summary>Gets or sets how Tab moves through the scope.</summary>
    public KeyboardNavigationMode Tab { get; set; } = KeyboardNavigationMode.Local;

    /// <summary>Gets or sets which arrow keys move focus between the scope's children.</summary>
    public ArrowNavigation Arrows { get; set; } = ArrowNavigation.None;

    /// <summary>Gets or sets whether the container itself takes focus, as painted tables and drill-in cards do.</summary>
    public bool IsFocusable { get; set; }

    /// <summary>Gets or sets how focus reaches the scope's children.</summary>
    public ScopeEntry Entry { get; set; } = ScopeEntry.Tab;

    /// <summary>Gets or sets whether the scope has one runtime instance per item.</summary>
    public bool IsRepeated { get; set; }

    /// <summary>
    /// Gets or sets whether the user arranges the scope's children, so their tab order follows the order code adds
    /// them in rather than document order.
    /// </summary>
    public bool IsArranged { get; set; }

    /// <summary>Gets or sets the id of the template that supplies the scope's structure, if any.</summary>
    public string? Template { get; set; }

    /// <summary>Gets or sets the named slot of the parent's template that receives this scope; unnamed when unset.</summary>
    public string? Slot { get; set; }
}

/// <summary>One activatable control, painted region, or keyboard-only command.</summary>
public sealed class Leaf : ControlMapNode
{
    private const char GestureSeparator = ';';
    private const char PartSeparator = '+';

    private KeyGesture[]? _keyGestures;
    private KeyGesture[]? _focusKeyGestures;
    private PointerGesture[]? _pointerGestures;

    /// <summary>Gets or sets the interaction model.</summary>
    public LeafKind Kind { get; set; }

    /// <summary>Gets or sets whether Tab stops on the leaf.</summary>
    public bool IsTabStop { get; set; } = true;

    /// <summary>Gets or sets whether the leaf has one runtime instance per item.</summary>
    public bool IsRepeated { get; set; }

    /// <summary>Gets or sets the semicolon-separated accelerators that activate the leaf from inside its key scope.</summary>
    public string? Keys { get; set; }

    /// <summary>Gets or sets the id of the ancestor whose focus subtree activates the keys; the parent when unset.</summary>
    public string? KeyScope { get; set; }

    /// <summary>Gets or sets the semicolon-separated key gestures handled only while the leaf itself has focus.</summary>
    public string? FocusKeys { get; set; }

    /// <summary>Gets or sets the semicolon-separated pointer gestures the activation handler serves.</summary>
    public string? Pointer { get; set; }

    /// <summary>Gets or sets the activation hook, such as Click or PointerReleased.</summary>
    public string? Event { get; set; }

    /// <summary>Gets or sets the named slot of the parent's template that receives this leaf; unnamed when unset.</summary>
    public string? Slot { get; set; }

    /// <summary>Gets the parsed accelerators; the control map generator has already validated their names.</summary>
    public IReadOnlyList<KeyGesture> KeyGestures => _keyGestures ??= ParseKeyGestures(Keys);

    /// <summary>Gets the parsed focus-local gestures; the control map generator has already validated their names.</summary>
    public IReadOnlyList<KeyGesture> FocusKeyGestures => _focusKeyGestures ??= ParseKeyGestures(FocusKeys);

    /// <summary>Gets the parsed pointer gestures; the control map generator has already validated them.</summary>
    public IReadOnlyList<PointerGesture> PointerGestures => _pointerGestures ??= ParsePointerGestures(Pointer);

    private static KeyGesture[] ParseKeyGestures(string? keys)
    {
        if (string.IsNullOrWhiteSpace(keys)) return [];

        string[] parts = keys.Split(GestureSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        KeyGesture[] gestures = new KeyGesture[parts.Length];
        for (int partIndex = 0; partIndex < parts.Length; partIndex++)
            gestures[partIndex] = KeyGesture.Parse(parts[partIndex]);

        return gestures;
    }

    private static PointerGesture[] ParsePointerGestures(string? pointer)
    {
        if (string.IsNullOrWhiteSpace(pointer)) return [];

        string[] gestureTexts = pointer.Split(GestureSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        PointerGesture[] gestures = new PointerGesture[gestureTexts.Length];
        for (int gestureIndex = 0; gestureIndex < gestureTexts.Length; gestureIndex++)
        {
            string[] parts = gestureTexts[gestureIndex].Split(PartSeparator);
            KeyModifiers modifiers = KeyModifiers.None;
            for (int partIndex = 0; partIndex < parts.Length - 1; partIndex++)
                modifiers |= ParseModifier(parts[partIndex]);

            gestures[gestureIndex] = new PointerGesture(modifiers, ParsePointerKind(parts[^1]));
        }

        return gestures;
    }

    private static KeyModifiers ParseModifier(string modifier) => modifier switch
    {
        "Ctrl" => KeyModifiers.Control,
        "Shift" => KeyModifiers.Shift,
        "Alt" => KeyModifiers.Alt,
        _ => throw new FormatException($"'{modifier}' is not a pointer gesture modifier.")
    };

    private static PointerGestureKind ParsePointerKind(string kind) => kind switch
    {
        "Click" => PointerGestureKind.Click,
        "DoubleClick" => PointerGestureKind.DoubleClick,
        "RightClick" => PointerGestureKind.RightClick,
        "MiddleClick" => PointerGestureKind.MiddleClick,
        "Wheel" => PointerGestureKind.Wheel,
        "Drag" => PointerGestureKind.Drag,
        _ => throw new FormatException($"'{kind}' is not a pointer gesture.")
    };
}
