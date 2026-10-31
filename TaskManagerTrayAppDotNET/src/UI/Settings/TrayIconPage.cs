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
    private StackPanel BuildTrayIconPage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_TrayIcon_SectionHeader)), palette);
        stack.Children.Add(ComboCard(
            L(nameof(AppStrings.Settings_TrayIcon_GraphStyle_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_GraphStyle_Description)),
            [
                (nameof(TrayGraphStyle.Current), L(nameof(AppStrings.Settings_TrayIcon_GraphStyle_Current))),
                (nameof(TrayGraphStyle.Marquee), L(nameof(AppStrings.Settings_TrayIcon_GraphStyle_Marquee)))
            ],
            _settings.TrayGraphStyle.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayGraphStyle value))
                    _settings.TrayGraphStyle = value;
            },
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_TrayIcon_GraphStyle_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.GraphStyle));
        stack.Children.Add(ComboCard(
            L(nameof(AppStrings.Settings_TrayIcon_DataSource_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_DataSource_Description)),
            [
                (nameof(TrayGraphDataSource.CPUAverage), L(nameof(AppStrings.Settings_TrayIcon_DataSource_CPUAverage))),
                (nameof(TrayGraphDataSource.CPUHighestCore),
                    L(nameof(AppStrings.Settings_TrayIcon_DataSource_CPUHighestCore))),
                (nameof(TrayGraphDataSource.Memory), L(nameof(AppStrings.Settings_TrayIcon_DataSource_Memory)))
            ],
            _settings.TrayGraphDataSource.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayGraphDataSource value))
                    _settings.TrayGraphDataSource = value;
            },
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_TrayIcon_DataSource_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.DataSource));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_TrayIcon_ShowHighestCoreTrace_Title)),
            L(nameof(AppStrings.Settings_TrayIcon_ShowHighestCoreTrace_Description)),
            _settings.ShowTrayCPUHighestCoreTrace,
            value => _settings.ShowTrayCPUHighestCoreTrace = value,
            palette,
            searchKeywords: [L(nameof(AppStrings.Settings_TrayIcon_ShowHighestCoreTrace_SearchKeywords))],
            node: ControlMap.Settings.TrayIconPage.ShowHighestCoreTrace));
        return stack;
    }
}
