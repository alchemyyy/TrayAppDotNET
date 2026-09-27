using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Threading;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.Services.Install;
using TrayAppDotNETCommon.UI.Controls;

namespace TrayAppDotNETCommon.UI.Settings;

public sealed class TrayAppDotNETGeneralSettingsSectionOptions
{
    public required SettingsPalette Palette { get; init; }
    public required CornerRadius ButtonRadius { get; init; }
    public required CornerRadius CardRadius { get; init; }
    public required Func<string, string> L { get; init; }
    public required Action Save { get; init; }
    public required Func<string, string, string, string, Task<bool>> ConfirmAsync { get; init; }
    public required Func<string, string, Task> ShowMessage { get; init; }
    public required Func<bool> GetRunOnStartup { get; init; }
    public required Action<bool> SetRunOnStartup { get; init; }

    /// <summary>Supplying both start-minimized delegates adds that sub-option to the Run on startup card.</summary>
    public Func<bool>? GetStartMinimized { get; init; }

    public Action<bool>? SetStartMinimized { get; init; }

    public required Func<string?> GetCurrentStartupShortcutTarget { get; init; }
    public required Action RetargetStartupShortcut { get; init; }
    public required Func<IReadOnlyList<TrayAppDotNETInstallationInfo>> DetectInstallations { get; init; }
    public required int CurrentBuildNumber { get; init; }
    public Action Shutdown { get; init; } = ShutdownDesktopApp;

    private static void ShutdownDesktopApp()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}

public sealed class TrayAppDotNETInstallCardOptions
{
    public required InstallScope Scope { get; init; }
    public required string Title { get; init; }
    public required string ExecutablePath { get; init; }
    public required bool Elevated { get; init; }
    public required Func<IProgress<TrayAppDotNETInstallProgress>?, TrayAppDotNETInstallResult> Install { get; init; }
    public required Func<Action, IProgress<TrayAppDotNETInstallProgress>?, Task> UninstallAsync { get; init; }
}

public sealed record TrayAppDotNETStoreInstallOptions(string Title, Func<string> Description);

public sealed class TrayAppDotNETGeneralSettingsSection
{
    // An uninstall that returns without ever reporting was cancelled in its confirmation dialog
    private static readonly TimeSpan UninstallSilenceTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly TrayAppDotNETGeneralSettingsSectionOptions _options;
    private readonly List<Action> _refreshers = [];
    private TextBlock? _startupDescription;

    public TrayAppDotNETGeneralSettingsSection(TrayAppDotNETGeneralSettingsSectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public Border BuildStartupCard()
    {
        SettingsPalette p = _options.Palette;
        bool runOnStartup = _options.GetRunOnStartup();
        CheckBox? startMinimizedCheckBox = BuildStartMinimizedCheckBox(p, runOnStartup);
        SettingsToggle startupToggle = TrayAppDotNETSettingsUI.Toggle(p, runOnStartup, (_, enabled) =>
        {
            _options.SetRunOnStartup(enabled);
            _options.Save();
            RefreshStartupDescription();
            startMinimizedCheckBox?.IsEnabled = enabled;
        });

        List<string> searchKeywords = [L(nameof(CommonStrings.Settings_General_RunOnStartup_SearchKeywords))];
        if (startMinimizedCheckBox != null)
            searchKeywords.Add(L(nameof(CommonStrings.Settings_General_StartMinimized_SearchKeywords)));

        Border startupCard = TrayAppDotNETSettingsCards.MutableCard(
            L(nameof(CommonStrings.Settings_General_RunOnStartup_Title)),
            RunOnStartupDescription(),
            startupToggle,
            p,
            _options.CardRadius,
            out TextBlock startupDescriptionText,
            searchKeywords,
            belowDescription: startMinimizedCheckBox);
        _startupDescription = startupDescriptionText;
        return startupCard;
    }

    /// <summary>Builds the start-minimized sub-option, which only applies while Run on startup is on.</summary>
    private CheckBox? BuildStartMinimizedCheckBox(SettingsPalette palette, bool runOnStartup)
    {
        if (_options.GetStartMinimized == null || _options.SetStartMinimized == null) return null;

        Action<bool> setStartMinimized = _options.SetStartMinimized;
        CheckBox checkBox = new()
        {
            Content = TrayAppDotNETSettingsUI.Text(L(nameof(CommonStrings.Settings_General_StartMinimized_Title)), palette),
            IsChecked = _options.GetStartMinimized(),
            IsEnabled = runOnStartup,
            Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = SettingsCardsLayout.SubOptionMargin
        };
        TrayAppDotNETToolTip.SetTip(checkBox, L(nameof(CommonStrings.Settings_General_StartMinimized_Description)));
        checkBox.IsCheckedChanged += (_, _) =>
        {
            setStartMinimized(checkBox.IsChecked == true);
            _options.Save();
        };
        return checkBox;
    }

    public void AddInstallationSection(
        StackPanel stack,
        IReadOnlyList<TrayAppDotNETInstallCardOptions> installCards,
        TrayAppDotNETStoreInstallOptions? storeInstall = null)
    {
        SettingsPalette p = _options.Palette;
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_General_Installation_Header)), p));

        foreach (TrayAppDotNETInstallCardOptions installCard in installCards)
            stack.Children.Add(BuildInstallCard(installCard));

        if (storeInstall != null)
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

    public void RefreshAfterInstallChange()
    {
        _options.RetargetStartupShortcut();
        RefreshStartupDescription();
        RefreshRows();
    }

    private Border BuildInstallCard(TrayAppDotNETInstallCardOptions entry)
    {
        SettingsPalette p = _options.Palette;
        SettingsButton installButton = Button(L(nameof(CommonStrings.Common_Install)));
        SettingsButton uninstallButton = Button(L(nameof(CommonStrings.Settings_General_Uninstall_Button)));
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
            belowDescription: progressRow);

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
            bool updateArrived = false;
            bool terminalArrived = false;
            DispatcherTimer silenceTimer = new() { Interval = UninstallSilenceTimeout };
            silenceTimer.Tick += (_, _) =>
            {
                silenceTimer.Stop();
                if (updateArrived) return;

                EndOperation(generation, terminalUpdate: null);
            };
            // The uninstall runs in external processes and keeps reporting after the call returns
            Progress<TrayAppDotNETInstallProgress> progress = CreateUIProgress(update =>
            {
                if (generation != operationGeneration || terminalArrived) return;

                updateArrived = true;
                silenceTimer.Stop();
                // A first update that outlived the silence fallback re-opens the card
                if (!operationInProgress)
                    ShowOperation();
                ApplyProgress(generation, update);
                if (!update.IsComplete && !update.IsFailure) return;

                terminalArrived = true;
                EndOperation(generation, update);
            });

            try
            {
                await entry.UninstallAsync(RefreshAfterInstallChange, progress);
            }
            catch (Exception exception)
            {
                TADNLog.Log($"Uninstall from {entry.Scope} threw: {exception.Message}");
                silenceTimer.Stop();
                terminalArrived = true;
                TrayAppDotNETInstallProgress failure = TrayAppDotNETInstallProgress.Failed(exception.Message);
                ApplyProgress(generation, failure);
                EndOperation(generation, failure);
                return;
            }

            if (updateArrived || !operationInProgress || generation != operationGeneration) return;

            silenceTimer.Start();
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

    private async Task StartInstalledInstanceAsync(string executablePath)
    {
        try
        {
            if (!File.Exists(executablePath))
                throw new FileNotFoundException(message: "Installed executable was not found.", executablePath);

            string? workingDirectory = Path.GetDirectoryName(executablePath);
            if (!ExplorerProcessLauncher.TryShellExecute(
                    executablePath,
                    arguments: null,
                    workingDirectory,
                    verb: null,
                    out _,
                    out string errorMessage))
                throw new InvalidOperationException(errorMessage);

            _options.Shutdown();
        }
        catch (Exception ex)
        {
            await _options.ShowMessage(L(nameof(CommonStrings.Settings_General_RestartFailed_Title)), ex.Message);
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

    private void RefreshStartupDescription() => _startupDescription?.Text = RunOnStartupDescription();

    private void RefreshRows()
    {
        foreach (Action refresh in _refreshers)
            refresh();
    }

    private string RunOnStartupDescription()
    {
        string? target = _options.GetCurrentStartupShortcutTarget();
        if (string.IsNullOrEmpty(target))
            return L(nameof(CommonStrings.Settings_General_RunOnStartup_Description));

        return string.Format(
            CultureInfo.CurrentCulture,
            L(nameof(CommonStrings.Settings_General_RunOnStartup_OnDescriptionFormat)),
            L(nameof(CommonStrings.Settings_General_RunOnStartup_OnHeaderLine)),
            target);
    }

    private SettingsButton Button(string text) =>
        TrayAppDotNETSettingsCards.Button(text, _options.Palette, _options.ButtonRadius);

    private string L(string key) => _options.L(key);
}
