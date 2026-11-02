using System.Xml.Serialization;
using TrayAppDotNETCommon.Serialization;

namespace TrayAppDotNETCommon.Models;

/// <summary>
/// Hotkey actions of the apps that only bind their flyout and settings window (Battery, Fan, Volume). Apps with more
/// actions keep their own enum and binding type.
/// </summary>
public enum HotkeyAction
{
    OpenSettings,
    OpenFlyout
}

/// <summary>
/// One persisted hotkey binding; every app's binding type derives from this, so all of them persist the same
/// attributes.
/// Identity = (Action, Parameter, BindingID): per (action, parameter) pair there can be N bindings,
/// distinguished by BindingID. BindingID == 0 is the legacy/primary row; legacy XML files without
/// the persisted value become the primary row by default.
/// Modifiers and VirtualKey are raw Win32 values (MOD_* and VK_*) so the storage shape matches RegisterHotKey
/// directly and the settings model does not depend on UI-framework input enums.
/// </summary>
public abstract class HotkeyBindingBase<TAction> : ITrayXmlUnknownValueHandler
    where TAction : struct, Enum
{
    [XmlAttribute]
    public TAction Action { get; set; }

    /// <summary>
    /// Free-form action-specific parameter.
    /// Empty for fixed-target actions; project-specific actions are free to define their own encoding.
    /// </summary>
    [XmlAttribute]
    public string Parameter { get; set; } = string.Empty;

    // MOD_* flags from RegisterHotKey (no MOD_NOREPEAT - added at registration time).
    [XmlAttribute]
    public uint Modifiers { get; set; }

    // VK_* virtual key code passed to RegisterHotKey.
    [XmlAttribute]
    public uint VirtualKey { get; set; }

    [XmlAttribute]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Disambiguator for multiple bindings sharing the same (Action, Parameter).
    /// 0 = primary/legacy row; 1, 2, ... = additional bindings added by the user.
    /// Missing in legacy XML maps to 0.
    /// </summary>
    [XmlAttribute]
    public int BindingID { get; set; }

    /// <summary>
    /// Tombstone flag: true means the user explicitly removed this binding through the UI.
    /// Tombstones are kept in the persisted list so a default seeder can tell that a default
    /// was removed on purpose and must not be re-added on the next launch.
    /// </summary>
    [XmlAttribute]
    public bool RemovedByUser { get; set; }

    [XmlIgnore]
    public bool IsBound => VirtualKey != 0 && Modifiers != 0;

    /// <summary>True when the stored action name is not one this build knows; such a binding is dropped on load.</summary>
    [XmlIgnore]
    public bool HasUnknownAction { get; private set; }

    void ITrayXmlUnknownValueHandler.OnTrayXmlUnknownValue(string memberName, string value)
    {
        if (memberName == nameof(Action)) HasUnknownAction = true;
    }

    /// <summary>
    /// "Any binding for this (Action, Parameter)" lookup.
    /// Use the 3-arg overload when row identity matters for persistence/status.
    /// </summary>
    public bool Matches(TAction action, string? parameter) =>
        EqualityComparer<TAction>.Default.Equals(Action, action)
        && string.Equals(Parameter, parameter ?? string.Empty, StringComparison.Ordinal);

    // Strict identity check including the per-row BindingID disambiguator.
    public bool Matches(TAction action, string? parameter, int bindingID) =>
        Matches(action, parameter) && BindingID == bindingID;

    /// <summary>True when this binding is live and uses the same key combination.</summary>
    public bool HasSameChord(uint modifiers, uint virtualKey) =>
        !RemovedByUser && Modifiers == modifiers && VirtualKey == virtualKey;
}

/// <inheritdoc cref="HotkeyBindingBase{TAction}"/>
public sealed class HotkeyBinding : HotkeyBindingBase<HotkeyAction>;

/// <summary>
/// Load-time cleanup of persisted hotkey lists, shared by every app and binding type.
/// </summary>
public static class HotkeyDefaults
{
    /// <summary>True if (action, parameter, bindingID) is the identity of one of <paramref name="defaults"/>.</summary>
    public static bool IsDefaultIdentity<TBinding, TAction>(
        IReadOnlyList<TBinding> defaults,
        TAction action,
        string parameter,
        int bindingID)
        where TBinding : HotkeyBindingBase<TAction>
        where TAction : struct, Enum
    {
        foreach (TBinding binding in defaults)
        {
            if (HasIdentity(binding, action, parameter, bindingID)) return true;
        }

        return false;
    }

    /// <summary>
    /// Normalizes a loaded list in place: drops bindings whose action this build does not know (a newer build or a
    /// hand edit wrote it), keeps the first of rows sharing one (Action, Parameter, BindingID) identity, and appends
    /// every entry of <paramref name="freshDefaults"/> whose identity is absent. A tombstoned row
    /// (<c>RemovedByUser</c>) counts as present, so a default the user removed stays removed.
    /// Returns true when the list changed.
    /// </summary>
    public static bool Normalize<TBinding, TAction>(List<TBinding> hotkeys, IReadOnlyList<TBinding> freshDefaults)
        where TBinding : HotkeyBindingBase<TAction>
        where TAction : struct, Enum
    {
        int countBefore = hotkeys.Count;
        HashSet<(TAction, string, int)> seen = [];
        hotkeys.RemoveAll(binding =>
            binding.HasUnknownAction || !seen.Add((binding.Action, binding.Parameter, binding.BindingID)));
        bool changed = hotkeys.Count != countBefore;

        foreach (TBinding binding in freshDefaults)
        {
            if (!seen.Add((binding.Action, binding.Parameter, binding.BindingID))) continue;

            hotkeys.Add(binding);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// One-time removal of a default an older build seeded on the primary row of <paramref name="action"/>: drops it
    /// while it still has the retired chord, plus any tombstone for that identity. A binding the user changed is kept.
    /// </summary>
    public static void RetireLegacyBinding<TBinding, TAction>(
        List<TBinding> hotkeys,
        TAction action,
        uint modifiers,
        uint virtualKey)
        where TBinding : HotkeyBindingBase<TAction>
        where TAction : struct, Enum =>
        hotkeys.RemoveAll(binding =>
            HasIdentity(binding, action, string.Empty, bindingID: 0)
            && (binding.RemovedByUser || (binding.Modifiers == modifiers && binding.VirtualKey == virtualKey)));

    private static bool HasIdentity<TBinding, TAction>(TBinding binding, TAction action, string parameter, int bindingID)
        where TBinding : HotkeyBindingBase<TAction>
        where TAction : struct, Enum =>
        EqualityComparer<TAction>.Default.Equals(binding.Action, action)
        && string.Equals(binding.Parameter, parameter, StringComparison.Ordinal)
        && binding.BindingID == bindingID;
}
