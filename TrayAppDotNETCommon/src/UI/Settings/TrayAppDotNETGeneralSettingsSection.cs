using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using TrayAppDotNETCommon.Services.Install;
using TrayAppDotNETCommon.UI.Controls;

namespace TrayAppDotNETCommon.UI.Settings;

public sealed class TrayAppDotNETGeneralSettingsSectionOptions
{
    public required SettingsPalette Palette { get; init; }
    public required CornerRadius ButtonRadius { get; init; }
    public required CornerRadius CardRadius { get; init; }
    public required Func<string, string, string> Localize { get; init; }
    public required Action Save { get; init; }
    public required Func<string, string, string, string, Task<bool>> ConfirmAsync { get; init; }
    public required Func<string, string, Task> ShowMessage { get; init; }
    public required Func<bool> GetRunOnStartup { get; init; }
    public required Action<bool> SetRunOnStartup { get; init; }
    public required Func<string?> GetCurrentStartupShortcutTarget { get; init; }
    public required Action RetargetStartupShortcut { get; init; }
    public required Func<IReadOnlyList<TrayAppDotNETInstallationInfo>> DetectInstallations { get; init; }
    public required int CurrentBuildNumber { get; init; }
}

public sealed class TrayAppDotNETInstallCardOptions
{
    public required InstallScope Scope { get; init; }
    public required string Title { get; init; }
    public required string ExecutablePath { get; init; }
    public required bool Elevated { get; init; }
    public required Func<TrayAppDotNETInstallResult> Install { get; init; }
    public required Func<Action, Task> UninstallAsync { get; init; }
}

public sealed record TrayAppDotNETStoreInstallOptions(string Title, Func<string> Description);

public sealed class TrayAppDotNETGeneralSettingsSection
{
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
        SettingsToggle startupToggle = TrayAppDotNETSettingsUI.Toggle(p, _options.GetRunOnStartup(), (_, enabled) =>
        {
            _options.SetRunOnStartup(enabled);
            _options.Save();
            RefreshStartupDescription();
        });

        Border startupCard = TrayAppDotNETSettingsCards.MutableCard(
            L("Settings_General_RunOnStartup_Title", "Run on startup"),
            RunOnStartupDescription(),
            startupToggle,
            p,
            _options.CardRadius,
            out TextBlock startupDescriptionText);
        _startupDescription = startupDescriptionText;
        return startupCard;
    }

    public void AddInstallationSection(
        StackPanel stack,
        IReadOnlyList<TrayAppDotNETInstallCardOptions> installCards,
        TrayAppDotNETStoreInstallOptions? storeInstall = null)
    {
        SettingsPalette p = _options.Palette;
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_General_Installation_Header", "Installation"), p));

        foreach (TrayAppDotNETInstallCardOptions installCard in installCards)
            stack.Children.Add(BuildInstallCard(installCard));

        if (storeInstall != null)
        {
            Border storeCard = TrayAppDotNETSettingsCards.MutableCard(
                storeInstall.Title,
                storeInstall.Description(),
                null,
                p,
                _options.CardRadius,
                out TextBlock storeDescription);
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
        SettingsButton installButton = Button(L("Common_Install", "Install"));
        SettingsButton uninstallButton = Button(L("Settings_General_Uninstall_Button", "Uninstall"));
        installButton.Margin = new Thickness(0, 0, 8, 0);
        StackPanel buttons = TrayAppDotNETSettingsUI.Horizontal(installButton, uninstallButton);

        Border card = TrayAppDotNETSettingsCards.MutableCard(
            entry.Title,
            "...",
            buttons,
            p,
            _options.CardRadius,
            out TextBlock description);

        _refreshers.Add(() =>
        {
            TrayAppDotNETInstallationInfo info = _options.DetectInstallations()
                                                     .FirstOrDefault(i => i.Scope == entry.Scope)
                                                 ?? new TrayAppDotNETInstallationInfo(
                                                     entry.Scope,
                                                     entry.ExecutablePath,
                                                     TrayAppDotNETInstallStatus.NotInstalled,
                                                     null);
            ApplyInstallRow(info, description, installButton, uninstallButton, entry.ExecutablePath, entry.Elevated);
        });

        installButton.Click += async (_, _) =>
        {
            bool ok = await _options.ConfirmAsync(
                L(entry.Scope == InstallScope.ProgramFiles
                    ? "Settings_General_InstallSystemWideConfirm_Title"
                    : "Settings_General_InstallConfirm_Title", "Install app?"),
                string.Format(
                    CultureInfo.CurrentCulture,
                    L(entry.Scope == InstallScope.ProgramFiles
                        ? "Settings_General_InstallSystemWideConfirm_Message_Format"
                        : "Settings_General_InstallConfirm_Message_Format", "Install to \"{0}\"."),
                    entry.ExecutablePath),
                L("Common_Install", "Install"),
                L("Common_Cancel", "Cancel"));
            if (!ok) return;

            installButton.IsEnabled = false;
            try
            {
                TrayAppDotNETInstallResult result = await Task.Run(entry.Install);
                if (result is { Success: false, UserCancelled: false } && !string.IsNullOrEmpty(result.ErrorMessage))
                    await _options.ShowMessage(L("Settings_General_InstallFailed_Title", "Install failed"),
                        result.ErrorMessage);
            }
            finally
            {
                installButton.IsEnabled = true;
                RefreshAfterInstallChange();
            }
        };

        uninstallButton.Click += async (_, _) =>
        {
            await entry.UninstallAsync(RefreshAfterInstallChange);
            RefreshAfterInstallChange();
        };

        return card;
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
            ? L("Settings_General_RequiresAdmin_Suffix", " Requires administrator approval.")
            : string.Empty;

        switch (info.Status)
        {
            case TrayAppDotNETInstallStatus.NotInstalled:
                description.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    L("Settings_General_NotInstalled_Format", "Not installed at {0}.{1}"),
                    installPath,
                    elevationSuffix);
                installButton.Text = L("Common_Install", "Install");
                installButton.IsVisible = true;
                uninstallButton.IsVisible = false;
                break;
            case TrayAppDotNETInstallStatus.InstalledUpToDate:
                description.Text = info.InstalledVersion is { } version
                    ? string.Format(
                        CultureInfo.CurrentCulture,
                        L("Settings_General_InstalledWithBuild_Format", "Build {0} installed at {1}."),
                        version,
                        installPath)
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        L("Settings_General_Installed_Format", "Installed at {0}."),
                        installPath);
                installButton.IsVisible = false;
                uninstallButton.Text = L("Settings_General_Uninstall_Button", "Uninstall");
                uninstallButton.IsVisible = true;
                break;
            case TrayAppDotNETInstallStatus.InstalledOutOfDate:
                description.Text = info.InstalledVersion is { } oldVersion
                    ? string.Format(
                        CultureInfo.CurrentCulture,
                        L("Settings_General_InstalledOutOfDate_Format",
                            "Installed build {0}; current build is {1}.{2}"),
                        oldVersion,
                        _options.CurrentBuildNumber,
                        elevationSuffix)
                    : string.Format(
                        CultureInfo.CurrentCulture,
                        L("Settings_General_InstalledOlderBuild_Format", "Older install at {0}.{1}"),
                        installPath,
                        elevationSuffix);
                installButton.Text = L("Settings_General_Update_Button", "Update");
                installButton.IsVisible = true;
                uninstallButton.Text = L("Settings_General_Uninstall_Button", "Uninstall");
                uninstallButton.IsVisible = true;
                break;
            case TrayAppDotNETInstallStatus.CurrentlyRunning:
                description.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    L("Settings_General_CurrentlyRunning_Format", "Currently running from {0}."),
                    installPath);
                installButton.IsVisible = false;
                uninstallButton.Text = L("Settings_General_Uninstall_Button", "Uninstall");
                uninstallButton.IsVisible = true;
                break;
        }
    }

    private void RefreshStartupDescription()
    {
        if (_startupDescription != null)
            _startupDescription.Text = RunOnStartupDescription();
    }

    private void RefreshRows()
    {
        foreach (Action refresh in _refreshers)
            refresh();
    }

    private string RunOnStartupDescription()
    {
        string? target = _options.GetCurrentStartupShortcutTarget();
        if (string.IsNullOrEmpty(target))
            return L("Settings_General_RunOnStartup_Description", "Start the app automatically when you sign in.");

        return string.Format(
            CultureInfo.CurrentCulture,
            L("Settings_General_RunOnStartup_OnDescriptionFormat", "{0}\n{1}"),
            L("Settings_General_RunOnStartup_OnHeaderLine", "Startup shortcut target:"),
            target);
    }

    private SettingsButton Button(string text) =>
        TrayAppDotNETSettingsCards.Button(text, _options.Palette, _options.ButtonRadius);

    private string L(string key, string fallback) => _options.Localize(key, fallback);
}
