using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Settings;

namespace BatteryTrayAppDotNET.UI.Settings;

public sealed partial class BatterySettingsWindow
{
    private StackPanel BuildGeneralPage()
    {
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_General_SectionHeader)), Palette);
        TrayAppDotNETGeneralSettingsSection commonSection =
            CreateGeneralSettingsSection(AppServices.Install, AppLocalizationCultures.Names);
        stack.Children.Add(commonSection.BuildLanguageCard());
        stack.Children.Add(commonSection.BuildStartupCard());
        AddStandardInstallationSection(stack, commonSection, AppServices.Install, _showUninstaller, includeStoreCard: true);
        AddRenderingSection(stack, supportsFlyoutWarmWindow: true, supportsTrayContextMenuWarmWindow: true);
        return stack;
    }
}
