using System.Xml.Linq;
using static TrayAppDotNETCommon.Models.TrayAppDotNETSettingsXml;

namespace BatteryTrayAppDotNET.Models;

public sealed partial class AppSettings
{
    private void SaveXml(Stream stream)
    {
        XDocument document = new(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                "AppSettings",
                Bool(nameof(RunOnStartup), RunOnStartup),
                Bool(nameof(Autosave), Autosave),
                Enum(nameof(ThemeMode), ThemeMode),
                NullableThemeColorElement(nameof(TextColor), TextColor),
                NullableThemeColorElement(nameof(BackgroundColor), BackgroundColor),
                NullableThemeColorElement(nameof(TrayIconColor), TrayIconColor),
                Bool(nameof(EnableRoundedCorners), EnableRoundedCorners),
                Enum(nameof(AnimationMode), AnimationMode),
                Int(nameof(ToolTipShowDelayMs), ToolTipShowDelayMs),
                Enum(nameof(ContextMenuPosition), ContextMenuPosition),
                Int(nameof(ContextMenuFontSize), ContextMenuFontSize),
                Bool(nameof(CheckForUpdatesEnabled), CheckForUpdatesEnabled),
                Bool(nameof(ShowUpdateNotificationsEnabled), ShowUpdateNotificationsEnabled),
                Bool(nameof(ShowUpdateButtonInFlyout), ShowUpdateButtonInFlyout),
                Int(nameof(UpdateCheckIntervalMs), UpdateCheckIntervalMs),
                Bool(nameof(KeepFlyoutWarm), KeepFlyoutWarm),
                Bool(nameof(KeepTrayContextMenuWarm), KeepTrayContextMenuWarm),
                HotkeysElement(Hotkeys)));

        SaveDocument(stream, document);
    }

    private static AppSettings LoadXml(Stream stream)
    {
        XElement root = LoadRoot(
            stream,
            "AppSettings",
            "Missing AppSettings root.",
            "Unexpected AppSettings root.");

        AppSettings settings = new() { SuppressChangeNotification = true };
        try
        {
            settings.RunOnStartup = ReadBool(root, nameof(RunOnStartup), settings.RunOnStartup);
            settings.Autosave = ReadBool(root, nameof(Autosave), settings.Autosave);
            settings.ThemeMode = ReadEnum(root, nameof(ThemeMode), settings.ThemeMode);
            settings.TextColor = ReadNullableThemeColor(root, nameof(TextColor), settings.TextColor);
            settings.BackgroundColor = ReadNullableThemeColor(root, nameof(BackgroundColor), settings.BackgroundColor);
            settings.TrayIconColor = ReadNullableThemeColor(root, nameof(TrayIconColor), settings.TrayIconColor);
            settings.EnableRoundedCorners =
                ReadBool(root, nameof(EnableRoundedCorners), settings.EnableRoundedCorners);
            settings.AnimationMode = ReadEnum(root, nameof(AnimationMode), settings.AnimationMode);
            settings.ToolTipShowDelayMs =
                ReadInt(root, nameof(ToolTipShowDelayMs), settings.ToolTipShowDelayMs);
            settings.ContextMenuPosition =
                ReadEnum(root, nameof(ContextMenuPosition), settings.ContextMenuPosition);
            settings.ContextMenuFontSize =
                ReadInt(root, nameof(ContextMenuFontSize), settings.ContextMenuFontSize);
            settings.CheckForUpdatesEnabled =
                ReadBool(root, nameof(CheckForUpdatesEnabled), settings.CheckForUpdatesEnabled);
            settings.ShowUpdateNotificationsEnabled =
                ReadBool(root, nameof(ShowUpdateNotificationsEnabled), settings.ShowUpdateNotificationsEnabled);
            settings.ShowUpdateButtonInFlyout =
                ReadBool(root, nameof(ShowUpdateButtonInFlyout), settings.ShowUpdateButtonInFlyout);
            settings.UpdateCheckIntervalMs =
                ReadInt(root, nameof(UpdateCheckIntervalMs), settings.UpdateCheckIntervalMs);
            settings.KeepFlyoutWarm = ReadBool(root, nameof(KeepFlyoutWarm), settings.KeepFlyoutWarm);
            settings.KeepTrayContextMenuWarm =
                ReadBool(root, nameof(KeepTrayContextMenuWarm), settings.KeepTrayContextMenuWarm);
            settings.Hotkeys = ReadHotkeys(root.Element("Hotkeys"));
        }
        finally
        {
            settings.SuppressChangeNotification = false;
        }

        settings.WireColorCallbacks();
        return settings;
    }
}
