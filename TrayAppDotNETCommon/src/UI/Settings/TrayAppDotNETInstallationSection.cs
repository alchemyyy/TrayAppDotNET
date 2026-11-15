using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Install;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Controls;

namespace TrayAppDotNETCommon.UI.Settings;

public sealed class TrayAppDotNETInstallCardOptions
{
    public required InstallScope Scope { get; init; }
    public required string Title { get; init; }
    public required string ExecutablePath { get; init; }
    public required bool Elevated { get; init; }
    public required Func<IProgress<TrayAppDotNETInstallProgress>?, TrayAppDotNETInstallResult> Install { get; init; }

    /// <summary>
    /// Opens the uninstaller for this scope, reporting stages to the given progress sink, and completes with whether
    /// the uninstall started once the uninstaller window closes.
    /// </summary>
    public required Func<IProgress<TrayAppDotNETInstallProgress>, Task<bool>> ShowUninstallerAsync { get; init; }
}

public sealed record TrayAppDotNETStoreInstallOptions(string Title, Func<string> Description);

public sealed class TrayAppDotNETInstallationSectionOptions
{
    public required SettingsPalette Palette { get; init; }
    public required CornerRadius ButtonRadius { get; init; }
    public required CornerRadius CardRadius { get; init; }
    public required Func<string, string> L { get; init; }
    public required Func<string, string, string, string?, Task<bool>> ConfirmAsync { get; init; }
    public required Func<string, string, Task> ShowMessage { get; init; }

    /// <summary>One card per install scope, in the order shown.</summary>
    public required IReadOnlyList<TrayAppDotNETInstallCardOptions> InstallCards { get; init; }

    /// <summary>The Microsoft Store card after the install cards, which has no controls; null leaves it out.</summary>
    public TrayAppDotNETStoreInstallOptions? StoreInstall { get; init; }

    public required Func<IReadOnlyList<TrayAppDotNETInstallationInfo>> DetectInstallations { get; init; }
    public required int CurrentBuildNumber { get; init; }

    /// <summary>
    /// Runs after every install or uninstall, so an existing startup shortcut follows the copies that remain.
    /// </summary>
    public required Action RetargetStartupShortcut { get; init; }
}

/// <summary>
/// The About page's Installation section: one card per install scope with Install (or Update) and Uninstall, which
/// shows the running operation's progress under its description, then the optional Microsoft Store card.
/// </summary>
public sealed class TrayAppDotNETInstallationSection
{
    private readonly TrayAppDotNETInstallationSectionOptions _options;
    private readonly List<Action> _refreshers = [];

    public TrayAppDotNETInstallationSection(TrayAppDotNETInstallationSectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>Adds the Installation header and the cards, each showing what its scope holds now.</summary>
    public void AddTo(StackPanel stack)
    {
        SettingsPalette p = _options.Palette;
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_General_Installation_Header)), p));

        foreach (TrayAppDotNETInstallCardOptions installCard in _options.InstallCards)
            stack.Children.Add(BuildInstallCard(installCard));

        if (_options.StoreInstall is { } storeInstall)
        {
            Border storeCard = TrayAppDotNETSettingsCards.MutableCard(
                storeInstall.Title,
                storeInstall.Description(),
                rightControl: null,
                p,
                _options.CardRadius,
                out TextBlock storeDescription,
                [L(nameof(CommonStrings.Settings_General_Installation_SearchKeywords))]);
            _refreshers.Add(() => storeDescription.Text = storeInstall.Description());
            stack.Children.Add(storeCard);
        }

        RefreshRows();
    }

    private void RefreshAfterInstallChange()
    {
        _options.RetargetStartupShortcut();
        RefreshRows();
    }

    private Border BuildInstallCard(TrayAppDotNETInstallCardOptions entry)
    {
        SettingsPalette p = _options.Palette;
        SettingsButton installButton = Button(L(nameof(CommonStrings.Common_Install)))
            .MapTo(ControlMap.InstallationSection.InstallCard.Install);
        SettingsButton uninstallButton = Button(L(nameof(CommonStrings.Settings_General_Uninstall_Button)))
            .MapTo(ControlMap.InstallationSection.InstallCard.Uninstall);
        installButton.Margin = new Thickness(left: 0, top: 0, right: 8, bottom: 0);
        StackPanel buttons = TrayAppDotNETSettingsUI.Horizontal(installButton, uninstallButton);

        ProgressBar progressBar = BuildProgressBar(p);
        TextBlock progressStatus = TrayAppDotNETSettingsUI.DescriptionText(
            string.Empty,
            p,
            SettingsCardsLayout.ProgressStatusMargin);
        StackPanel progressRow = new() { Margin = SettingsCardsLayout.ProgressRowMargin, IsVisible = false };
        progressRow.Children.Add(progressBar);
        progressRow.Children.Add(progressStatus);

        Border card = TrayAppDotNETSettingsCards.MutableCard(
            entry.Title,
            description: "...",
            buttons,
            p,
            _options.CardRadius,
            out TextBlock description,
            [L(nameof(CommonStrings.Settings_General_Installation_SearchKeywords))],
            belowDescription: progressRow).MapTo(ControlMap.InstallationSection.InstallCard.ID);

        // Per-card operation state; every access happens on the UI thread
        bool operationInProgress = false;
        int operationGeneration = 0;

        _refreshers.Add(() =>
        {
            TrayAppDotNETInstallationInfo info = _options.DetectInstallations()
                                                     .FirstOrDefault(i => i.Scope == entry.Scope)
                                                 ?? new TrayAppDotNETInstallationInfo(
                                                     entry.Scope,
                                                     entry.ExecutablePath,
                                                     TrayAppDotNETInstallStatus.NotInstalled,
                                                     InstalledVersion: null);
            ApplyInstallRow(info, description, installButton, uninstallButton, entry.ExecutablePath, entry.Elevated);
        });

        installButton.Click += async (_, _) =>
        {
            if (operationInProgress) return;

            bool ok = await _options.ConfirmAsync(
                L(entry.Scope == InstallScope.ProgramFiles
                    ? nameof(CommonStrings.Settings_General_InstallSystemWideConfirm_Title)
                    : nameof(CommonStrings.Settings_General_InstallConfirm_Title)),
                string.Format(
                    CultureInfo.CurrentCulture,
                    L(entry.Scope == InstallScope.ProgramFiles
                        ? nameof(CommonStrings.Settings_General_InstallSystemWideConfirm_Message_Format)
                        : nameof(CommonStrings.Settings_General_InstallConfirm_Message_Format)),
                    entry.ExecutablePath),
                L(nameof(CommonStrings.Common_Install)),
                L(nameof(CommonStrings.Common_Cancel)));
            if (!ok || operationInProgress) return;

            int generation = BeginOperation();
            Progress<TrayAppDotNETInstallProgress> progress =
                CreateUIProgress(update => ApplyProgress(generation, update));
            TrayAppDotNETInstallResult? result = null;
            TrayAppDotNETInstallProgress? failure = null;
            try
            {
                result = await Task.Run(() => entry.Install(progress));
                if (result is { Success: false, UserCancelled: false } && !string.IsNullOrEmpty(result.ErrorMessage))
                    failure = TrayAppDotNETInstallProgress.Failed(result.ErrorMessage);
            }
            catch (Exception exception)
            {
                TADNLog.Log($"Install to {entry.Scope} threw: {exception.Message}");
                failure = TrayAppDotNETInstallProgress.Failed(exception.Message);
            }

            // The final error replaces whatever stage message the installer left behind
            if (failure != null)
                ApplyProgress(generation, failure);
            EndOperation(generation, failure);

            if (failure != null)
            {
                await _options.ShowMessage(
                    L(nameof(CommonStrings.Settings_General_InstallFailed_Title)),
                    failure.Message);
            }

            if (result is { Success: true })
                await PromptRestartFromInstalledAsync(entry);
        };

        uninstallButton.Click += async (_, _) =>
        {
            if (operationInProgress) return;

            int generation = BeginOperation();
            bool terminalArrived = false;
            // The uninstall runs in external processes and keeps reporting after the uninstaller window closed
            Progress<TrayAppDotNETInstallProgress> progress = CreateUIProgress(update =>
            {
                if (generation != operationGeneration || terminalArrived) return;

                ApplyProgress(generation, update);
                if (!update.IsComplete && !update.IsFailure) return;

                terminalArrived = true;
                EndOperation(generation, update);
                if (update.IsFailure) _ = ReportUninstallFailureAsync(update.Message);
            });

            bool started;
            try
            {
                started = await entry.ShowUninstallerAsync(progress);
            }
            catch (Exception exception)
            {
                TADNLog.Log($"Uninstall from {entry.Scope} threw: {exception}");
                if (terminalArrived) return;

                terminalArrived = true;
                TrayAppDotNETInstallProgress failure = TrayAppDotNETInstallProgress.Failed(exception.Message);
                ApplyProgress(generation, failure);
                EndOperation(generation, failure);
                await ReportUninstallFailureAsync(exception.Message);
                return;
            }

            // Cancelled in the uninstaller window, or the window showed why it could not start: nothing ran
            if (!started && !terminalArrived) EndOperation(generation, terminalUpdate: null);
        };

        return card;

        void ShowOperation()
        {
            operationInProgress = true;
            progressBar.IsVisible = true;
            progressRow.IsVisible = true;
            installButton.IsEnabled = false;
            uninstallButton.IsEnabled = false;
        }

        int BeginOperation()
        {
            operationGeneration++;
            progressBar.Value = 0;
            progressStatus.Text = string.Empty;
            ShowOperation();
            return operationGeneration;
        }

        void ApplyProgress(int generation, TrayAppDotNETInstallProgress update)
        {
            if (generation != operationGeneration) return;

            progressBar.Value = Math.Clamp(update.Percent, min: 0, TrayAppDotNETInstallProgress.CompletePercent);
            progressStatus.Text = update.Message;
        }

        void EndOperation(int generation, TrayAppDotNETInstallProgress? terminalUpdate)
        {
            if (generation != operationGeneration) return;

            operationInProgress = false;
            installButton.IsEnabled = true;
            uninstallButton.IsEnabled = true;
            progressBar.IsVisible = false;
            // Only a failure message outlives the operation
            bool keepFailureMessage = terminalUpdate is { IsFailure: true };
            progressRow.IsVisible = keepFailureMessage;
            if (!keepFailureMessage)
                progressStatus.Text = string.Empty;
            RefreshAfterInstallChange();
        }
    }

    /// <summary>Creates the thin accent progress bar shown under an install card description.</summary>
    private ProgressBar BuildProgressBar(SettingsPalette palette) =>
        new()
        {
            Minimum = 0,
            Maximum = TrayAppDotNETInstallProgress.CompletePercent,
            Value = 0,
            MinWidth = 0,
            MinHeight = SettingsCardsLayout.ProgressBarHeight,
            Height = SettingsCardsLayout.ProgressBarHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = TrayAppDotNETSettingsUI.Brush(palette.Accent),
            Background = TrayAppDotNETSettingsUI.Brush(palette.ControlBackground),
            BorderThickness = new Thickness(0),
            CornerRadius = _options.CardRadius
        };

    /// <summary>Wraps a UI update so reports from worker threads and external processes land on the UI thread.</summary>
    private static Progress<TrayAppDotNETInstallProgress> CreateUIProgress(
        Action<TrayAppDotNETInstallProgress> applyUpdate) =>
        new(update =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                applyUpdate(update);
                return;
            }

            Dispatcher.UIThread.Post(() => applyUpdate(update));
        });

    private async Task PromptRestartFromInstalledAsync(TrayAppDotNETInstallCardOptions entry)
    {
        bool restart = await _options.ConfirmAsync(
            L(nameof(CommonStrings.Settings_General_InstallComplete_Title)),
            string.Format(
                CultureInfo.CurrentCulture,
                L(nameof(CommonStrings.Settings_General_InstallCompleteRestart_Message_Format)),
                entry.ExecutablePath),
            L(nameof(CommonStrings.Settings_General_RestartFromInstalled_Button)),
            L(nameof(CommonStrings.Settings_General_NotNow_Button)));
        if (!restart) return;

        await StartInstalledInstanceAsync(entry.ExecutablePath);
    }

    /// <summary>
    /// Restarts as the installed copy: this app's exit path runs first, then the installed copy starts with the same
    /// settings folder override, so it takes over without having to stop anything.
    /// </summary>
    private async Task StartInstalledInstanceAsync(string executablePath)
    {
        try
        {
            TrayAppDotNETAppLifetime.Restart(executablePath, TrayAppDotNETProgram.SettingsDirectoryCommandLine());
        }
        catch (Exception ex)
        {
            await _options.ShowMessage(L(nameof(CommonStrings.Settings_General_RestartFailed_Title)), ex.Message);
        }
    }

    /// <summary>Tells the user an uninstall did not finish; the card keeps the reason under its description.</summary>
    private async Task ReportUninstallFailureAsync(string message)
    {
        try
        {
            await _options.ShowMessage(L(nameof(CommonStrings.Settings_General_UninstallFailed_Title)), message);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Uninstall failure message could not be shown: {exception}");
        }
    }

    private void ApplyInstallRow(
        TrayAppDotNETInstallationInfo info,
        TextBlock description,
        SettingsButton installButton,
        SettingsButton uninstallButton,
        string installPath,
        bool elevated)
    {
        string elevationSuffix = elevated
            ? L(nameof(CommonStrings.Settings_General_RequiresAdmin_Suffix))
            : string.Empty;

        switch (info.Status)
        {
            case TrayAppDotNETInstallStatus.NotInstalled:
                description.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    L(nameof(CommonStrings.Settings_General_NotInstalled_Format)),
                    installPath,
                    elevationSuffix);
                installButton.Text = L(nameof(CommonStrings.Common_Install));
                installButton.IsVisible = true;
                uninstallButton.IsVisible = false;
                break;
            case TrayAppDotNETInstallStatus.InstalledUpToDate:
                description.Text = info.InstalledVersion is { } version
                    ? string.Format(
                        CultureInfo.CurrentCulture,
                        L(nameof(CommonStrings.Settings_General_InstalledWithBuild_Format)),
                        version,
                        installPath)
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        L(nameof(CommonStrings.Settings_General_Installed_Format)),
                        installPath);
                installButton.IsVisible = false;
                uninstallButton.Text = L(nameof(CommonStrings.Settings_General_Uninstall_Button));
                uninstallButton.IsVisible = true;
                break;
            case TrayAppDotNETInstallStatus.InstalledOutOfDate:
                description.Text = info.InstalledVersion is { } oldVersion
                    ? string.Format(
                        CultureInfo.CurrentCulture,
                        L(nameof(CommonStrings.Settings_General_InstalledOutOfDate_Format)),
                        oldVersion,
                        _options.CurrentBuildNumber,
                        elevationSuffix)
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        L(nameof(CommonStrings.Settings_General_InstalledOlderBuild_Format)),
                        installPath,
                        elevationSuffix);
                installButton.Text = L(nameof(CommonStrings.Settings_General_Update_Button));
                installButton.IsVisible = true;
                uninstallButton.Text = L(nameof(CommonStrings.Settings_General_Uninstall_Button));
                uninstallButton.IsVisible = true;
                break;
            case TrayAppDotNETInstallStatus.CurrentlyRunning:
                description.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    L(nameof(CommonStrings.Settings_General_CurrentlyRunning_Format)),
                    installPath);
                installButton.IsVisible = false;
                uninstallButton.Text = L(nameof(CommonStrings.Settings_General_Uninstall_Button));
                uninstallButton.IsVisible = true;
                break;
        }
    }

    private void RefreshRows()
    {
        foreach (Action refresh in _refreshers)
            refresh();
    }

    private SettingsButton Button(string text) =>
        TrayAppDotNETSettingsCards.Button(text, _options.Palette, _options.ButtonRadius);

    private string L(string key) => _options.L(key);
}
