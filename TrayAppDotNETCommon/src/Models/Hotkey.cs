namespace TrayAppDotNETCommon.Models;

/// <summary>
/// Shared hotkey action set. Apps can use the actions they support and ignore the rest.
/// </summary>
public enum HotkeyAction
{
    OpenSettings,
    OpenFlyout,
    OpenNetworkSettings,
    OpenAdapterSettings,
}

public interface IHotkeyBinding<out TAction>
    where TAction : struct, Enum
{
    TAction Action { get; }
    string Parameter { get; }
    uint Modifiers { get; }
    uint VirtualKey { get; }
    bool Enabled { get; }
    int BindingID { get; }
    bool RemovedByUser { get; }
    bool IsBound { get; }
}

/// <summary>
/// One persisted hotkey binding.
/// Identity = (Action, Parameter, BindingID): per (action, parameter) pair there can be N bindings,
/// distinguished by BindingID. BindingID == 0 is the legacy/primary row; legacy XML files without
/// the persisted value become the primary row by default.
/// Modifiers and VirtualKey are raw Win32 values (MOD_* and VK_*) so the storage shape matches RegisterHotKey
/// directly and the settings model does not depend on WPF input enums.
/// </summary>
public sealed class HotkeyBinding : IHotkeyBinding<HotkeyAction>
{
    public HotkeyAction Action { get; set; }

    /// <summary>
    /// Free-form action-specific parameter.
    /// Empty for fixed-target actions; project-specific actions are free to define their own encoding.
    /// </summary>
    public string Parameter { get; set; } = string.Empty;

    // MOD_* flags from RegisterHotKey (no MOD_NOREPEAT - added at registration time).
    public uint Modifiers { get; set; }

    // VK_* virtual key code passed to RegisterHotKey.
    public uint VirtualKey { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Disambiguator for multiple bindings sharing the same (Action, Parameter).
    /// 0 = primary/legacy row; 1, 2, ... = additional bindings added by the user.
    /// Missing in legacy XML maps to 0.
    /// </summary>
    public int BindingID { get; set; }

    /// <summary>
    /// Tombstone flag: true means the user explicitly removed this binding through the UI.
    /// Tombstones are kept in the persisted list so a default seeder can tell that a default
    /// was removed on purpose and must not be re-added on the next launch.
    /// </summary>
    public bool RemovedByUser { get; set; }

    public bool IsBound => VirtualKey != 0 && Modifiers != 0;

    /// <summary>
    /// "Any binding for this (Action, Parameter)" lookup.
    /// Use the 3-arg overload when row identity matters for persistence/status.
    /// </summary>
    public bool Matches(HotkeyAction action, string? parameter) =>
        Action == action && string.Equals(Parameter, parameter ?? string.Empty, StringComparison.Ordinal);

    // Strict identity check including the per-row BindingID disambiguator.
    public bool Matches(HotkeyAction action, string? parameter, int bindingID) =>
        Action == action
        && string.Equals(Parameter, parameter ?? string.Empty, StringComparison.Ordinal)
        && BindingID == bindingID;
}

/// <summary>
/// Shared hotkey catalog helpers. Common intentionally ships with no built-in defaults;
/// apps can either use the no-op default catalog or pass their own default bindings to the overloads.
/// </summary>
public static class HotkeyDefaults
{
    /// <summary>
    /// Common's built-in default catalog. Empty by design because default hotkeys are app policy.
    /// </summary>
    public static IReadOnlyList<HotkeyBinding> Create() => [];

    public static bool IsDefaultIdentity(HotkeyAction action, string parameter, int bindingID) =>
        IsDefaultIdentity(Create(), action, parameter, bindingID);

    public static bool IsDefaultIdentity(
        IEnumerable<HotkeyBinding> defaults,
        HotkeyAction action,
        string parameter,
        int bindingID)
    {
        foreach (HotkeyBinding d in defaults)
        {
            if (d.Matches(action, parameter, bindingID))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Removes redundant hotkey rows that share the same identity tuple
    /// (Action, Parameter, BindingID), keeping the first occurrence.
    /// </summary>
    public static bool DedupeByIdentity(IList<HotkeyBinding> hotkeys)
    {
        HashSet<(HotkeyAction, string, int)> seen = [];
        int writeIndex = 0;
        for (int readIndex = 0; readIndex < hotkeys.Count; readIndex++)
        {
            HotkeyBinding b = hotkeys[readIndex];
            (HotkeyAction, string, int) key = (b.Action, b.Parameter, b.BindingID);
            if (!seen.Add(key)) continue;

            if (writeIndex != readIndex) hotkeys[writeIndex] = b;
            writeIndex++;
        }

        if (writeIndex == hotkeys.Count) return false;

        int removeCount = hotkeys.Count - writeIndex;
        for (int i = 0; i < removeCount; i++) hotkeys.RemoveAt(hotkeys.Count - 1);
        return true;
    }

    public static bool EnsureDefaults(IList<HotkeyBinding> hotkeys) =>
        EnsureDefaults(hotkeys, Create());

    /// <summary>
    /// Adds any default bindings that are not already represented by the same
    /// (Action, Parameter, BindingID), including tombstoned rows.
    /// </summary>
    public static bool EnsureDefaults(
        IList<HotkeyBinding> hotkeys,
        IEnumerable<HotkeyBinding> defaults)
    {
        bool added = false;
        foreach (HotkeyBinding d in defaults)
        {
            bool present = false;
            foreach (HotkeyBinding existing in hotkeys)
            {
                if (!existing.Matches(d.Action, d.Parameter, d.BindingID)) continue;

                present = true;
                break;
            }

            if (present) continue;

            hotkeys.Add(new HotkeyBinding
            {
                Action = d.Action,
                Parameter = d.Parameter,
                Modifiers = d.Modifiers,
                VirtualKey = d.VirtualKey,
                Enabled = d.Enabled,
                BindingID = d.BindingID,
            });
            added = true;
        }

        return added;
    }
}
