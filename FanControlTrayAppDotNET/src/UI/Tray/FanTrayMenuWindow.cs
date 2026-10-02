using Avalonia;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;

namespace FanControlTrayAppDotNET.UI.Tray;

public sealed class FanTrayMenuWindow : ContextMenuWindow
{
    /// <summary>
    /// Builds the tray menu. ContextMenu is a shared control map template, so the menu carries its own surface id.
    /// </summary>
    public FanTrayMenuWindow(
        AppSettings settings,
        SettingsPalette palette,
        bool rounded,
        int fontSize,
        Action openSettings,
        Action exit)
        : base(BuildEntries(openSettings, exit),
            new ContextMenuWindowOptions
            {
                Palette = palette,
                Rounded = rounded,
                FontSize = fontSize,
                ContextMenuSettings = settings,
                ShadowColor = ResolveMenuShadowColor(settings)
            }) =>
        this.MapTo(ControlMap.TrayMenu.ID);

    internal void ShowAt(
        TrayAppDotNETShellTrayIcon trayIcon,
        PixelPoint cursorPoint,
        ContextMenuPosition placement) =>
        base.ShowAt(trayIcon, cursorPoint, ToCommonPlacement(placement));

    private static List<ContextMenuEntry> BuildEntries(Action openSettings, Action exit)
    {
        ContextMenuEntryBuilder entries = new();
        entries.Add(new ContextMenuEntry(L(nameof(AppStrings.Tray_Settings)), openSettings)
        {
            Node = ControlMap.TrayMenu.Settings
        });
        entries.AddSeparator();
        entries.Add(new ContextMenuEntry(L(nameof(AppStrings.Tray_Exit)), exit) { Node = ControlMap.TrayMenu.Exit });
        return entries.ToList();
    }

    private static Color ResolveMenuShadowColor(AppSettings settings)
    {
        bool isLight = AppTheme.ResolveEffectiveIsLightTheme(settings);
        return (AppServices.Theme ?? AppTheme.Default).MenuShadow.For(isLight);
    }

    private static ContextMenuPlacement ToCommonPlacement(ContextMenuPosition placement) =>
        placement == ContextMenuPosition.Modern
            ? ContextMenuPlacement.Modern
            : ContextMenuPlacement.Classic;

    private static string L(string key) => TrayLocalization.Instance[key];
}
