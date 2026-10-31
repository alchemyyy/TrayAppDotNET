using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Settings;
using TrayAppDotNETCommon.Visuals;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerGlyphCatalog = TaskManagerTrayAppDotNET.Visuals.GlyphCatalog;

namespace TaskManagerTrayAppDotNET.UI;

public sealed partial class TaskManagerSettingsWindow
{
    /// <summary>
    /// Lists every table shortcut with the gestures its control map leaf declares, so the page shows exactly what the
    /// tables obey.
    /// </summary>
    private StackPanel BuildHotkeysPage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_Hotkeys_SectionHeader)), palette);
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Hotkeys_Tables_Header)),
            palette));
        foreach (TaskManagerGridShortcutEntry entry in TaskManagerGridShortcuts.Entries)
        {
            string gestures = DescribeShortcut(entry.Action, ShortcutGestureSeparator);
            if (gestures.Length == 0) continue;

            TextBlock gestureText = TrayAppDotNETSettingsUI.Text(gestures, palette);
            gestureText.TextAlignment = TextAlignment.Right;
            stack.Children.Add(Card(
                TaskManagerGridShortcuts.GetTitle(entry.Action),
                TaskManagerGridShortcuts.GetDescription(entry.Action),
                gestureText,
                palette,
                searchKeywords: [gestures, L(nameof(AppStrings.Settings_Hotkeys_Tables_SearchKeywords))]));
        }

        return stack;
    }
}
