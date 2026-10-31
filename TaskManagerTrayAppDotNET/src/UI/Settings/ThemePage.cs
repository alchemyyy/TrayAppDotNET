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
    private StackPanel BuildThemePage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack(L(nameof(AppStrings.Settings_Appearance_SectionHeader)), palette);

        AddAppearanceSection(stack);
        AddContextMenuAppearanceSection(stack);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Theme_Window_Header)),
            palette));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Theme_CollapseNavigationWhenNarrow_Title)),
            string.Format(
                CultureInfo.CurrentCulture,
                L(nameof(AppStrings.Settings_Theme_CollapseNavigationWhenNarrow_Description_Format)),
                _taskManagerResources.AxamlTaskManagerWindow.SidebarCollapseThreshold),
            _settings.CollapseSidebarWhenNarrow,
            value => _settings.CollapseSidebarWhenNarrow = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_Theme_CollapseNavigationWhenNarrow_SearchKeywords))],
            node: ControlMap.Settings.ThemePage.CollapseNavigationWhenNarrow));
        return stack;
    }
}
