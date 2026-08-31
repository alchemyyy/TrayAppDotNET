using Avalonia;
using Avalonia.Media;
using TrayLocalization = TrayAppDotNETCommon.Localization.LocalizationManager;

namespace FanControlTrayAppDotNET.UI.Tray;

public sealed class FanTrayMenuWindow(
    AppSettings settings,
    SettingsPalette palette,
    bool rounded,
    int fontSize,
    Action openSettings,
    Action exit)
    : ContextMenuWindow(BuildEntries(openSettings, exit),
        new ContextMenuWindowOptions
        {
            Palette = palette,
            Rounded = rounded,
            FontSize = fontSize,
            ContextMenuSettings = settings,
            ShadowColor = ResolveMenuShadowColor(settings)
        })
{
    internal void ShowAt(
        TrayAppDotNETShellTrayIcon trayIcon,
        PixelPoint cursorPoint,
        ContextMenuPosition placement) =>
        base.ShowAt(trayIcon, cursorPoint, ToCommonPlacement(placement));

    private static List<ContextMenuEntry> BuildEntries(Action openSettings, Action exit)
    {
        ContextMenuEntryBuilder entries = new();
        entries.Add(L(nameof(AppStrings.Tray_Settings)), openSettings);
        entries.AddSeparator();
        entries.Add(L(nameof(AppStrings.Tray_Exit)), exit);
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
