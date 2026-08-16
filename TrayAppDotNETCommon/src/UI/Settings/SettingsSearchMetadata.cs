using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace TrayAppDotNETCommon.UI.Settings;

internal enum SettingsSearchRole
{
    None,
    PageHeader,
    SubsectionHeader,
    Card
}

/// <summary>Associates code-created settings controls with their search structure.</summary>
internal static class SettingsSearchMetadata
{
    private sealed class RoleHolder
    {
        public SettingsSearchRole Role;
    }

    private static readonly ConditionalWeakTable<Control, RoleHolder> Roles = new();

    public static T Mark<T>(T control, SettingsSearchRole role)
        where T : Control
    {
        RoleHolder holder = Roles.GetOrCreateValue(control);
        holder.Role = role;
        return control;
    }

    public static SettingsSearchRole GetRole(Control control) =>
        Roles.TryGetValue(control, out RoleHolder? holder) ? holder.Role : SettingsSearchRole.None;
}
