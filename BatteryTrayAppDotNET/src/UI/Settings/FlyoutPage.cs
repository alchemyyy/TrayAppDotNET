#pragma warning disable CA1822

using Avalonia.Controls;

namespace BatteryTrayAppDotNET.UI.Settings;

public sealed partial class BatterySettingsWindow
{
    private StackPanel BuildFlyoutPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_Flyout_SectionHeader)), p);

        stack.Children.Add(BoolCard(
            L(nameof(CommonStrings.Settings_Flyout_RestoreUndockState_Title)),
            L(nameof(CommonStrings.Settings_Flyout_RestoreUndockState_Description)),
            _settings.RestoreFlyoutUndockedOnStartup,
            v => _settings.RestoreFlyoutUndockedOnStartup = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_RestoreUndockState_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.RestoreFlyoutUndockedOnStartup));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_Flyout_Visibility_Header)), p));
        stack.Children.Add(BoolCard(
            L(nameof(CommonStrings.Settings_Flyout_ShowUndockButton_Title)),
            L(nameof(CommonStrings.Settings_Flyout_ShowUndockButton_Description)),
            _settings.AllowFlyoutUndock,
            v => _settings.AllowFlyoutUndock = v,
            p,
            () => RebuildShell(BatterySettingsPage.Flyout),
            [
                L(nameof(AppStrings.Settings_Flyout_ShowUndockButton_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.AllowFlyoutUndock));

        if (_settings.AllowFlyoutUndock)
        {
            stack.Children.Add(BoolCard(
                L(nameof(CommonStrings.Settings_Flyout_ClampUndockedToScreen_Title)),
                L(nameof(CommonStrings.Settings_Flyout_ClampUndockedToScreen_Description)),
                _settings.ClampUndockedFlyoutToScreen,
                v => _settings.ClampUndockedFlyoutToScreen = v,
                p,
                searchKeywords:
                [
                    L(nameof(AppStrings.Settings_Flyout_ClampUndockedToScreen_SearchKeywords))
                ],
                node: ControlMap.Settings.FlyoutPage.ClampUndockedFlyoutToScreen));
        }

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_Flyout_Layout_Header)), p));
        stack.Children.Add(BoolCard(
            L(nameof(AppStrings.Settings_Flyout_HeaderAtBottom_Title)),
            L(nameof(AppStrings.Settings_Flyout_HeaderAtBottom_Description)),
            _settings.FlyoutHeaderAtBottom,
            v => _settings.FlyoutHeaderAtBottom = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_HeaderAtBottom_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.FlyoutHeaderAtBottom));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(AppStrings.Settings_Flyout_BatteryEstimates_Header)), p));
        stack.Children.Add(IntCard(
            L(nameof(AppStrings.Settings_Flyout_DischargeEstimateChecks_Title)),
            L(nameof(AppStrings.Settings_Flyout_DischargeEstimateChecks_Description)),
            _settings.DischargeEstimateChecks,
            AppSettings.DischargeEstimateChecksMin,
            AppSettings.DischargeEstimateChecksMax,
            v => _settings.DischargeEstimateChecks = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_DischargeEstimateChecks_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.DischargeEstimateChecks));
        stack.Children.Add(DoubleCard(
            L(nameof(AppStrings.Settings_Flyout_DischargeEstimateHalfLifeChecks_Title)),
            L(nameof(AppStrings.Settings_Flyout_DischargeEstimateHalfLifeChecks_Description)),
            _settings.DischargeEstimateHalfLifeChecks,
            AppSettings.DischargeEstimateHalfLifeChecksMin,
            AppSettings.DischargeEstimateHalfLifeChecksMax,
            v => _settings.DischargeEstimateHalfLifeChecks = v,
            p,
            searchKeywords:
            [
                L(nameof(AppStrings.Settings_Flyout_DischargeEstimateHalfLifeChecks_SearchKeywords))
            ],
            node: ControlMap.Settings.FlyoutPage.DischargeEstimateHalfLifeChecks));

        return stack;
    }
}
