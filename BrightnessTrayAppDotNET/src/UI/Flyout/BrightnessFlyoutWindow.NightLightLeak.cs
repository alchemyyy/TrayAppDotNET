using Avalonia.Controls;
using Avalonia.Threading;
using BrightnessTrayAppDotNET.Interop.NightLight;
using TrayAppDotNETCommon.Services.Install;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.Utils;
using BrightnessAppTheme = BrightnessTrayAppDotNET.Visuals.AppTheme;

namespace BrightnessTrayAppDotNET.UI.Flyout;

public sealed partial class BrightnessFlyoutWindow
{
    /// <summary>
    /// Shows the warning over an activated flyout while Windows' fix for Explorer's Night Light leak is missing
    /// (<see cref="NightLightLeakFix"/>). Fix applies it; Ignore stops the warning for good once a second prompt
    /// confirms it. Closing the warning, or hiding the flyout, decides nothing, so the warning shows on the next open.
    /// It is posted behind the reveal, so it opens over the revealed flyout and keeps the focus the activation takes.
    /// </summary>
    private void QueueNightLightLeakWarning()
    {
        if (!ShowActivated || !ShouldWarnAboutNightLightLeak()) return;

        CancellationToken cancellationToken = WindowResources.CancellationToken;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (cancellationToken.IsCancellationRequested || !IsWindowAlive || !IsVisible) return;
                if (!ShouldWarnAboutNightLightLeak()) return;

                // Shares the update prompt's one flow per flyout, so neither opens over the other
                TrayAppDotNETModalPrompt.RunExclusiveAsync(this, RunNightLightLeakWarningAsync)
                    .ForgetLogged("BrightnessFlyoutWindow.QueueNightLightLeakWarning");
            },
            DispatcherPriority.Background);
    }

    private bool ShouldWarnAboutNightLightLeak() =>
        NightLightLeakFix.State == NightLightLeakFixState.Missing
        && _settings is { IgnoreNightLightLeakWarning: false };

    private async Task<bool> RunNightLightLeakWarningAsync()
    {
        while (IsWindowAlive && IsVisible)
        {
            TrayAppDotNETConfirmationResult choice = await ShowNightLightLeakPromptAsync(CreateNightLightLeakPrompt(
                ControlMap.NightLightLeakWarning.ID,
                L(nameof(AppStrings.Flyout_NightLightLeak_Title)),
                L(nameof(AppStrings.Flyout_NightLightLeak_Description)),
                L(nameof(AppStrings.Flyout_NightLightLeak_Fix)),
                alternateText: L(nameof(AppStrings.Flyout_NightLightLeak_Ignore)),
                footerText: L(nameof(AppStrings.Flyout_NightLightLeak_Notice)),
                useModalContentLayout: true));
            switch (choice)
            {
                case TrayAppDotNETConfirmationResult.Confirmed:
                    return await ApplyNightLightLeakFixAsync();
                case TrayAppDotNETConfirmationResult.Alternate:
                    if (!IsWindowAlive || !IsVisible) return false;
                    if (await ShowNightLightLeakPromptAsync(CreateNightLightLeakPrompt(
                            ControlMap.NightLightLeakIgnoreConfirmation.ID,
                            L(nameof(AppStrings.Flyout_NightLightLeak_IgnoreTitle)),
                            L(nameof(AppStrings.Flyout_NightLightLeak_IgnoreDescription)),
                            L(nameof(AppStrings.Flyout_NightLightLeak_Ignore)),
                            cancelText: L(nameof(CommonStrings.SettingsWindow_ConfirmOverlay_Cancel))))
                        == TrayAppDotNETConfirmationResult.Confirmed)
                    {
                        IgnoreNightLightLeakWarning();
                        return true;
                    }

                    // Declining the ignore returns to the warning while the flyout is still open
                    continue;
                case TrayAppDotNETConfirmationResult.Cancelled:
                default:
                    return false;
            }
        }

        return false;
    }

    private async Task<bool> ApplyNightLightLeakFixAsync()
    {
        // The flyout may hide while the UAC prompt has the focus; the result then opens on its own
        TrayAppDotNETElevatedSettingChangeResult result = await NightLightLeakFix.ApplyAsync();
        if (result == TrayAppDotNETElevatedSettingChangeResult.Declined || IsWindowClosed) return false;

        // The state read back after the change decides, whatever the relaunch reported
        bool isFixed = NightLightLeakFix.State != NightLightLeakFixState.Missing;
        TrayAppDotNETConfirmationWindow notice = isFixed
            ? CreateNightLightLeakPrompt(
                ControlMap.NightLightLeakFixResult.ID,
                L(nameof(AppStrings.Flyout_NightLightLeak_RestartTitle)),
                L(nameof(AppStrings.Flyout_NightLightLeak_RestartDescription)),
                L(nameof(CommonStrings.SettingsWindow_ConfirmOverlay_OK)))
            : CreateNightLightLeakPrompt(
                ControlMap.NightLightLeakFixResult.ID,
                L(nameof(AppStrings.Flyout_NightLightLeak_FailedTitle)),
                L(nameof(AppStrings.Flyout_NightLightLeak_FailedDescription)),
                L(nameof(CommonStrings.SettingsWindow_ConfirmOverlay_OK)));
        _ = await ShowNightLightLeakPromptAsync(notice);
        return isFixed;
    }

    private void IgnoreNightLightLeakWarning()
    {
        if (_settings == null) return;

        _settings.IgnoreNightLightLeakWarning = true;
        _settings.Save();
        TADNLog.Log("NightLightLeakFix: the warning is ignored from now on.");
    }

    private TrayAppDotNETConfirmationWindow CreateNightLightLeakPrompt(
        ControlMapNodeID node,
        string title,
        string description,
        string confirmText,
        string? alternateText = null,
        string? cancelText = null,
        string? footerText = null,
        bool useModalContentLayout = false)
    {
        bool isLight = BrightnessAppTheme.ResolveIsLight(_settings, _theme);
        return new TrayAppDotNETConfirmationWindow(
            title,
            description,
            confirmText,
            AppSettingsPalette.CreateForFlyout(_theme, _settings, isLight),
            _settings?.EnableRoundedCorners == true,
            alternateText,
            cancelText,
            modalDetails: null,
            footerText,
            useModalContentLayout)
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        }.MapTo(node);
    }

    private Task<TrayAppDotNETConfirmationResult> ShowNightLightLeakPromptAsync(
        TrayAppDotNETConfirmationWindow prompt) =>
        TrayAppDotNETModalPrompt.ShowAsync(
            this,
            prompt,
            _theme.FlyoutOverlayBackdrop.For(BrightnessAppTheme.ResolveIsLight(_settings, _theme)),
            TADNLog.Log);
}
