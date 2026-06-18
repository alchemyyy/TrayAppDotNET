#pragma warning disable CA1822

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BatteryTrayAppDotNET.Models;
using TrayAppDotNETCommon.UI.Settings;
using BatteryInstallScope = TrayAppDotNETCommon.InstallScope;

namespace BatteryTrayAppDotNET.UI.Settings;

public enum BatterySettingsPage
{
    General,
    TrayIcon,
    Hotkeys,
    Theme,
    About,
}

public sealed class BatterySettingsWindow : SettingsWindowCommon<BatterySettingsPage>
{
    private readonly AppSettings _settings;
    private readonly Action<string, BatteryInstallScope> _showUninstaller;
    private TrayAppDotNETAboutPage? _aboutPage;

    public BatterySettingsWindow()
        : this(new AppSettings(), static (_, _) => { })
    {
    }

    public BatterySettingsWindow(AppSettings settings, Action<string, BatteryInstallScope> showUninstaller)
    {
        _settings = settings;
        _showUninstaller = showUninstaller;
        ConfigureSettingsWindow(
            L("SettingsWindow_Title", "Settings"),
            width: 900,
            height: 640,
            minWidth: 680,
            minHeight: 500,
            AppTheme.LoadAppIcon());
        InitializeSettingsShell();
    }

    internal new void SelectPage(BatterySettingsPage page) => base.SelectPage(page);

    protected override SettingsPalette Palette =>
        BatterySettingsPalette.Create(AppServices.Theme, _settings, ResolveEffectiveIsLight());

    protected override bool EnableRoundedCorners => _settings.EnableRoundedCorners;

    protected override BatterySettingsPage DefaultPageKey => BatterySettingsPage.General;

    protected override string HeaderText => L("SettingsWindow_Header", "Settings");

    protected override string OpenSettingsFolderText =>
        L("SettingsWindow_OpenSettingsFolder", "Open settings folder");

    protected override string SettingsFolderPath => AppSettings.GetDefaultDirectory();

    protected override Color ConfirmOverlayBackdrop =>
        (AppServices.Theme ?? AppTheme.Default).FlyoutOverlayBackdrop.For(ResolveEffectiveIsLight());

    protected override IReadOnlyList<SettingsPageDescriptor<BatterySettingsPage>> CreatePageDescriptors() =>
    [
        new(BatterySettingsPage.General, L("Settings_Common_Page_General", "General"), BuildGeneralPage),
        new(BatterySettingsPage.TrayIcon, L("Settings_Common_Page_TrayIcon", "Tray Icon"), BuildTrayIconPage),
        new(BatterySettingsPage.Hotkeys, L("Settings_Common_Page_Hotkeys", "Hotkeys"), BuildHotkeysPage),
        new(BatterySettingsPage.Theme, L("Settings_Common_Page_Theme", "Theme"), BuildThemePage),
        new(BatterySettingsPage.About, L("Settings_Common_Page_About", "About"), BuildAboutPage),
    ];

    protected override void Save()
    {
        _settings.Save();
        _settings.RaiseChanged();
    }

    protected override bool ResolveEffectiveIsLightForBindings() => ResolveEffectiveIsLight();

    protected override void OnSettingsWindowClosed()
    {
        StopAboutUpdateRefresh();
        base.OnSettingsWindowClosed();
    }

    internal void StopAboutUpdateRefresh()
    {
        _aboutPage?.StopUpdateRefresh();
        _aboutPage = null;
    }

    private bool ResolveEffectiveIsLight() => _settings.ThemeMode switch
    {
        ThemeMode.Light => true,
        ThemeMode.Dark => false,
        _ => AppServices.Theme?.IsLightTheme ?? AppTheme.Default.IsLightTheme,
    };

    private Control BuildSettingsPage(BatterySettingsPage page, Func<Control> buildPage)
    {
        if (page != BatterySettingsPage.About)
            StopAboutUpdateRefresh();

        return buildPage();
    }

    private StackPanel BuildGeneralPage() =>
        (StackPanel)BuildSettingsPage(BatterySettingsPage.General, () =>
        {
            SettingsPalette p = Palette;
            StackPanel stack = PageStack(L("Settings_General_SectionHeader", "General"), p);

            TrayAppDotNETGeneralSettingsSection commonSection = CreateGeneralSettingsSection(p);
            stack.Children.Add(commonSection.BuildStartupCard());
            CreateMemorySettingsSection(p).AddCards(stack);
            CreateKeepWarmSettingsSection(p).AddCards(stack);
            commonSection.AddInstallationSection(
                stack,
                [
                    new TrayAppDotNETInstallCardOptions
                    {
                        Scope = BatteryInstallScope.LocalAppData,
                        Title = L("Settings_General_LocalUser_Title", "Local user"),
                        ExecutablePath = AppServices.InstallLayout.LocalAppDataInstallExecutable,
                        Elevated = false,
                        Install = static () => AppServices.Installation.InstallToLocalAppData(),
                        UninstallAsync = _ =>
                        {
                            _showUninstaller(
                                AppServices.InstallLayout.LocalAppDataInstallDirectory,
                                BatteryInstallScope.LocalAppData);
                            return Task.CompletedTask;
                        },
                    },
                    new TrayAppDotNETInstallCardOptions
                    {
                        Scope = BatteryInstallScope.ProgramFiles,
                        Title = L("Settings_General_SystemWide_Title", "System-wide"),
                        ExecutablePath = AppServices.InstallLayout.ProgramFilesInstallExecutable,
                        Elevated = true,
                        Install = static () => AppServices.Installation.InstallSystemWide(),
                        UninstallAsync = _ =>
                        {
                            _showUninstaller(
                                AppServices.InstallLayout.ProgramFilesInstallDirectory,
                                BatteryInstallScope.ProgramFiles);
                            return Task.CompletedTask;
                        },
                    },
                ],
                new TrayAppDotNETStoreInstallOptions(
                    L("Settings_General_WindowsStore_Title", "Windows Store"),
                    StoreInstallDescription));

            return stack;
        });

    private TrayAppDotNETGeneralSettingsSection CreateGeneralSettingsSection(SettingsPalette p) =>
        new(new TrayAppDotNETGeneralSettingsSectionOptions
        {
            Palette = p,
            ButtonRadius = RadiusMedium,
            CardRadius = RadiusLarge,
            Localize = L,
            Save = Save,
            ConfirmAsync = ConfirmAsync,
            ShowMessage = ShowMessage,
            GetRunOnStartup = static () => AppServices.Startup.GetRunOnStartup(),
            SetRunOnStartup = enabled =>
            {
                AppServices.Startup.SetRunOnStartup(enabled);
                _settings.RunOnStartup = enabled;
            },
            GetCurrentStartupShortcutTarget = static () => AppServices.Startup.GetCurrentShortcutTarget(),
            RetargetStartupShortcut = static () => AppServices.Startup.RetargetShortcutIfPresent(),
            DetectInstallations = static () => AppServices.Installation.DetectAll(),
            CurrentBuildNumber = BuildInfo.BuildNumber,
        });

    private TrayAppDotNETKeepWarmSettingsSection CreateKeepWarmSettingsSection(SettingsPalette p) =>
        new(new TrayAppDotNETKeepWarmSettingsSectionOptions
        {
            Palette = p,
            CardRadius = RadiusLarge,
            Localize = L,
            Save = Save,
            Settings = _settings,
            SupportsFlyout = true,
            SupportsTrayContextMenu = true,
        });

    private TrayAppDotNETMemorySettingsSection CreateMemorySettingsSection(SettingsPalette p) =>
        new(new TrayAppDotNETMemorySettingsSectionOptions
        {
            Palette = p,
            CardRadius = RadiusLarge,
            Localize = L,
            Save = Save,
            Settings = _settings,
        });

    private static string StoreInstallDescription()
    {
        TrayAppDotNETInstallationInfo? info = AppServices.Installation.DetectAll()
            .FirstOrDefault(i => i.Scope == BatteryInstallScope.WindowsStore);
        return info?.Status == TrayAppDotNETInstallStatus.CurrentlyRunning
            ? L("Settings_General_StoreRunning", "Running from Windows Store")
            : L("Settings_General_StoreNotInstalled", "Not installed from Windows Store");
    }

    private StackPanel BuildTrayIconPage() =>
        (StackPanel)BuildSettingsPage(BatterySettingsPage.TrayIcon, () =>
        {
            SettingsPalette p = Palette;
            StackPanel stack = PageStack(L("Settings_TrayIcon_SectionHeader", "Tray Icon"), p);

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L("Settings_TrayIcon_ContextMenu_Header", "Context menu"), p));
            stack.Children.Add(ComboCard(
                L("Settings_TrayIcon_MenuPosition_Title", "Menu position"),
                L("Settings_TrayIcon_MenuPosition_Description",
                    "Choose where the right-click tray menu opens."),
                [
                    (ContextMenuPosition.Classic.ToString(), L("Settings_TrayIcon_MenuPosition_Classic", "Classic")),
                    (ContextMenuPosition.Modern.ToString(), L("Settings_TrayIcon_MenuPosition_Modern", "Modern")),
                ],
                _settings.ContextMenuPosition.ToString(),
                tag =>
                {
                    if (Enum.TryParse(tag, out ContextMenuPosition value))
                        _settings.ContextMenuPosition = value;
                },
                p,
                autoSizeToText: true,
                autoSizeMode: SettingsComboBoxAutoSizeMode.SelectedItem));

            return stack;
        });

    private StackPanel BuildHotkeysPage() =>
        (StackPanel)BuildSettingsPage(BatterySettingsPage.Hotkeys, () =>
        {
            SettingsPalette p = Palette;
            StackPanel stack = PageStack(L("Settings_Hotkeys_SectionHeader", "Hotkeys"), p);
            stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
                L("Settings_Hotkeys_SectionDescription",
                    "Add keyboard shortcuts for common battery tray actions."),
                p,
                new Thickness(0, 0, 0, 16)));

            TextBox searchBox = TrayAppDotNETSettingsUI.TextBox(p, 240);
            StackPanel searchRow = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12),
            };
            TextBlock searchLabel = TrayAppDotNETSettingsUI.TitleText(
                L("Settings_Hotkeys_SearchLabel", "Search"), p);
            searchLabel.VerticalAlignment = VerticalAlignment.Center;
            searchLabel.Margin = new Thickness(0, 0, 8, 0);
            searchRow.Children.Add(searchLabel);
            searchRow.Children.Add(searchBox);
            stack.Children.Add(searchRow);

            List<(Control Control, string SearchText)> rows = [];
            AddHotkeyRow(
                stack,
                rows,
                HotkeyAction.OpenFlyout,
                L("Settings_Hotkeys_OpenFlyout_Title", "Open flyout"),
                L("Settings_Hotkeys_OpenFlyout_Description", "Show battery details above the tray icon."),
                p);
            AddHotkeyRow(
                stack,
                rows,
                HotkeyAction.OpenSettings,
                L("Settings_Hotkeys_OpenSettings_Title", "Open settings"),
                L("Settings_Hotkeys_OpenSettings_Description", "Open the BatteryTrayAppDotNET settings window."),
                p);

            searchBox.TextChanged += (_, _) =>
            {
                string query = (searchBox.Text ?? string.Empty).Trim();
                foreach ((Control row, string searchText) in rows)
                {
                    row.IsVisible = query.Length == 0
                                    || searchText.Contains(query, StringComparison.OrdinalIgnoreCase);
                }
            };

            return stack;
        });

    private void AddHotkeyRow(
        StackPanel stack,
        List<(Control Control, string SearchText)> rows,
        HotkeyAction action,
        string title,
        string description,
        SettingsPalette p)
    {
        StackPanel entries = new() { Spacing = 0 };
        uint selectedModifiers = 0;
        uint selectedVk = 0;

        SettingsComboBox modifiers = TrayAppDotNETSettingsUI.ComboBox(p, 170);
        modifiers.Padding = new Thickness(8, 0, 2, 0);
        foreach (TrayAppDotNETHotkeyModifierOption option in TrayAppDotNETHotkeyModifierOptions.Create(L))
            modifiers.Items.Add(new SettingsComboBoxItem(option.Modifiers, option.Label, p));

        TextBox keyBox = TrayAppDotNETSettingsUI.TextBox(p, 60);
        keyBox.IsReadOnly = true;
        keyBox.Cursor = new Cursor(StandardCursorType.Ibeam);

        SettingsButton addButton = Button(L("Settings_Hotkeys_Add_Button", "Add"), p);
        addButton.MinWidth = 70;
        addButton.IsEnabled = false;

        void UpdateAddButtonState()
        {
            if (selectedModifiers == 0 || selectedVk == 0)
            {
                addButton.Text = L("Settings_Hotkeys_Add_Button", "Add");
                addButton.IsEnabled = false;
                return;
            }

            bool exists = _settings.Hotkeys.Any(b =>
                !b.RemovedByUser
                && b.Matches(action, string.Empty)
                && b.Modifiers == selectedModifiers
                && b.VirtualKey == selectedVk);
            addButton.Text = exists
                ? L("Settings_Hotkeys_Exists_Button", "Exists")
                : L("Settings_Hotkeys_Add_Button", "Add");
            addButton.IsEnabled = !exists;
        }

        void Refresh()
        {
            HotkeyApplyResult? applyResult = null;
            try { applyResult = AppServices.HotkeyService?.Apply(_settings.Hotkeys); }
            catch (Exception ex) { TADNLog.Log($"BatterySettingsWindow.Hotkeys.Apply: {ex.Message}"); }

            entries.Children.Clear();
            foreach (HotkeyBinding binding in _settings.Hotkeys
                         .Where(h => !h.RemovedByUser && h.Matches(action, string.Empty))
                         .OrderBy(h => h.BindingID))
                entries.Children.Add(BuildHotkeyEntryCard(action, binding, applyResult, Refresh, p));
            entries.IsVisible = entries.Children.Count > 0;
            UpdateAddButtonState();
        }

        modifiers.SelectionChanged += (_, _) =>
        {
            selectedModifiers = modifiers.SelectedItem is { Tag: uint mods } ? mods : 0;
            UpdateAddButtonState();
        };
        keyBox.KeyDown += (_, e) =>
        {
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.Escape)
            {
                e.Handled = true;
                return;
            }

            uint vk = TrayAppDotNETHotkeyKeys.VirtualKeyFromKey(e.Key);
            if (vk == 0 || vk == 0x7B)
            {
                e.Handled = true;
                return;
            }

            selectedVk = vk;
            keyBox.Text = TrayAppDotNETHotkeyKeys.KeyName(vk);
            UpdateAddButtonState();
            e.Handled = true;
        };
        addButton.Click += (_, _) =>
        {
            if (!addButton.IsEnabled || selectedModifiers == 0 || selectedVk == 0) return;
            int id = _settings.Hotkeys.Where(h => h.Matches(action, string.Empty))
                .Select(h => h.BindingID)
                .DefaultIfEmpty(0)
                .Max() + 1;
            _settings.Hotkeys.Add(new HotkeyBinding
            {
                Action = action,
                Parameter = string.Empty,
                Modifiers = selectedModifiers,
                VirtualKey = selectedVk,
                Enabled = true,
                BindingID = id,
            });
            selectedModifiers = 0;
            selectedVk = 0;
            modifiers.SelectedIndex = -1;
            keyBox.Text = string.Empty;
            Save();
            Refresh();
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 240 });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        StackPanel text = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        text.Children.Add(TrayAppDotNETSettingsUI.TitleText(title, p));
        text.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(description, p));
        grid.Children.Add(text);

        modifiers.Margin = new Thickness(0, 0, 8, 0);
        keyBox.Margin = new Thickness(0, 0, 8, 0);
        Grid.SetColumn(modifiers, 1);
        Grid.SetColumn(keyBox, 2);
        Grid.SetColumn(addButton, 3);
        grid.Children.Add(modifiers);
        grid.Children.Add(keyBox);
        grid.Children.Add(addButton);

        entries.Margin = new Thickness(0, 8, 8, 0);
        Grid.SetRow(entries, 1);
        Grid.SetColumn(entries, 1);
        Grid.SetColumnSpan(entries, 2);
        grid.Children.Add(entries);

        Border card = RawCard(grid, p);
        rows.Add((card, title + "\n" + description));
        stack.Children.Add(card);
        Refresh();
    }

    private Border BuildHotkeyEntryCard(
        HotkeyAction action,
        HotkeyBinding binding,
        HotkeyApplyResult? applyResult,
        Action refresh,
        SettingsPalette p)
    {
        TextBlock display = TrayAppDotNETSettingsUI.Text(FormatHotkey(binding), p);
        display.VerticalAlignment = VerticalAlignment.Center;
        display.Margin = new Thickness(12, 6, 0, 6);

        TextBlock status = TrayAppDotNETSettingsUI.Text(string.Empty, p);
        status.FontFamily = TrayAppDotNETSettingsUI.IconFont;
        status.VerticalAlignment = VerticalAlignment.Center;
        status.Margin = new Thickness(0, 0, 8, 0);

        if (AppServices.HotkeyService == null)
        {
            status.Text = GlyphCatalog.WARNING;
            TrayAppDotNETToolTip.SetTip(
                status,
                L("Settings_Hotkeys_Status_HotkeyServiceUnavailable", "Hotkey service is unavailable."));
        }
        else if (applyResult?.Failed.TryGetValue(binding, out string? error) == true)
        {
            status.Text = GlyphCatalog.WARNING;
            TrayAppDotNETToolTip.SetTip(status, error);
        }
        else if (binding.IsBound)
        {
            TrayAppDotNETToolTip.SetTip(
                status,
                L("Settings_Hotkeys_Status_Registered", "Registered"));
        }

        SettingsButton delete = Button("x", p);
        delete.Width = 32;
        delete.Height = 29;
        delete.Padding = new Thickness(0);
        delete.Label.FontSize = 20;
        TrayAppDotNETToolTip.SetTip(
            delete,
            L("Settings_Hotkeys_DeleteHotkey_ToolTip", "Delete hotkey"));
        delete.Click += (_, _) =>
        {
            if (AppSettings.IsDefaultHotkeyIdentity(action, string.Empty, binding.BindingID))
            {
                binding.RemovedByUser = true;
                binding.Enabled = false;
            }
            else
            {
                _settings.Hotkeys.RemoveAll(b => b.Matches(action, string.Empty, binding.BindingID));
            }

            Save();
            refresh();
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.Children.Add(display);
        Grid.SetColumn(status, 1);
        Grid.SetColumn(delete, 2);
        grid.Children.Add(status);
        grid.Children.Add(delete);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(p.ControlBackground),
            CornerRadius = RadiusMedium,
            Margin = new Thickness(0, 0, 0, 4),
            Child = grid,
        };
    }

    private static string FormatHotkey(HotkeyBinding binding)
    {
        string modifiers = TrayAppDotNETHotkeyKeys.ModifierText(binding.Modifiers);
        string key = TrayAppDotNETHotkeyKeys.KeyName(binding.VirtualKey);
        return string.IsNullOrEmpty(modifiers) ? key : modifiers + " + " + key;
    }

    private StackPanel BuildThemePage() =>
        (StackPanel)BuildSettingsPage(BatterySettingsPage.Theme, () =>
        {
            SettingsPalette p = Palette;
            StackPanel stack = PageStack(L("Settings_Theme_SectionHeader", "Theme"), p);

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L("Settings_Theme_ContextMenu_Header", "Context menu"), p));
            stack.Children.Add(IntCard(
                L("Settings_Theme_FontSize_Title", "Font size"),
                L("Settings_Theme_FontSize_Description", "Adjust the right-click tray menu font size."),
                _settings.ContextMenuFontSize,
                AppSettings.ContextMenuFontSizeMin,
                AppSettings.ContextMenuFontSizeMax,
                v => _settings.ContextMenuFontSize = v,
                p));

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L("Settings_Theme_Appearance_Header", "Appearance"), p));
            stack.Children.Add(ComboCard(
                L("Settings_Theme_ThemeStyle_Title", "Theme"),
                L("Settings_Theme_ThemeStyle_Description", "Choose the app theme mode."),
                [
                    (ThemeMode.System.ToString(), L("Settings_Theme_ThemeStyle_System", "System")),
                    (ThemeMode.Light.ToString(), L("Settings_Theme_ThemeStyle_Light", "Light")),
                    (ThemeMode.Dark.ToString(), L("Settings_Theme_ThemeStyle_Dark", "Dark")),
                ],
                _settings.ThemeMode.ToString(),
                tag =>
                {
                    if (Enum.TryParse(tag, out ThemeMode value))
                        _settings.ThemeMode = value;
                },
                p,
                afterSave: () => RebuildShell(BatterySettingsPage.Theme)));
            stack.Children.Add(VariantColorCard(
                "Text",
                L("Settings_Theme_TextColor_Title", "Text color"),
                L("Settings_Theme_TextColor_Description", "Override text color for each theme variant."),
                L("Settings_Theme_TextColor_LightTooltip", "Light theme text color"),
                L("Settings_Theme_TextColor_DarkTooltip", "Dark theme text color"),
                _settings.TextColor,
                (AppServices.Theme ?? AppTheme.Default).Foreground.Light,
                (AppServices.Theme ?? AppTheme.Default).Foreground.Dark,
                p));
            stack.Children.Add(VariantColorCard(
                "Background",
                L("Settings_Theme_BackgroundColor_Title", "Background color"),
                L("Settings_Theme_BackgroundColor_Description", "Override background color for each theme variant."),
                L("Settings_Theme_BackgroundColor_LightTooltip", "Light theme background color"),
                L("Settings_Theme_BackgroundColor_DarkTooltip", "Dark theme background color"),
                _settings.BackgroundColor,
                (AppServices.Theme ?? AppTheme.Default).Background.Light,
                (AppServices.Theme ?? AppTheme.Default).Background.Dark,
                p));

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L("Settings_Theme_Window_Header", "Windows"), p));
            stack.Children.Add(BoolCard(
                L("Settings_Theme_RoundedCorners_Title", "Rounded corners"),
                L("Settings_Theme_RoundedCorners_Description", "Use rounded corners on BatteryTrayAppDotNET windows."),
                _settings.EnableRoundedCorners,
                v => _settings.EnableRoundedCorners = v,
                p,
                afterSave: () => RebuildShell(BatterySettingsPage.Theme)));
            stack.Children.Add(ComboCard(
                L("Settings_Theme_Animations_Title", "Animations"),
                L("Settings_Theme_Animations_Description", "Controls whether tooltip fades and other UI animations are allowed."),
                [
                    (TrayAppDotNETAnimationMode.System.ToString(), L("Settings_Theme_Animations_System", "System")),
                    (TrayAppDotNETAnimationMode.Disabled.ToString(), L("Settings_Theme_Animations_Disabled", "Disabled")),
                    (TrayAppDotNETAnimationMode.Enabled.ToString(), L("Settings_Theme_Animations_Enabled", "Enabled")),
                ],
                _settings.AnimationMode.ToString(),
                tag =>
                {
                    if (Enum.TryParse(tag, out TrayAppDotNETAnimationMode value))
                        _settings.AnimationMode = value;
                },
                p,
                afterSave: () =>
                {
                    if (Application.Current != null)
                        TrayAppDotNETAnimationPolicy.Apply(Application.Current, _settings.AnimationMode);
                    RebuildShell(BatterySettingsPage.Theme);
                }));
            stack.Children.Add(IntCard(
                L("Settings_Theme_ToolTipShowDelay_Title", "Tooltip delay"),
                L("Settings_Theme_ToolTipShowDelay_Description", "Milliseconds to wait before showing a tooltip."),
                _settings.ToolTipShowDelayMs,
                TimeConstants.ToolTipShowDelayMinMs,
                TimeConstants.ToolTipShowDelayMaxMs,
                v =>
                {
                    _settings.ToolTipShowDelayMs = v;
                    TrayAppDotNETToolTip.ShowDelayMs = v;
                    TrayAppDotNETToolTip.ApplyShowDelayToSubtree(this);
                },
                p,
                " ms"));

            stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
                L("Settings_Theme_TrayIcon_Header", "Tray icon"), p));
            stack.Children.Add(VariantColorCard(
                "TrayIcon",
                L("Settings_Theme_StaticIconColor_Title", "Tray icon color"),
                L("Settings_Theme_StaticIconColor_Description",
                    "Override the tray icon color for each theme variant."),
                L("Settings_Theme_StaticIconColor_LightTooltip", "Light theme tray icon color"),
                L("Settings_Theme_StaticIconColor_DarkTooltip", "Dark theme tray icon color"),
                _settings.TrayIconColor,
                (AppServices.Theme ?? AppTheme.Default).Foreground.Light,
                (AppServices.Theme ?? AppTheme.Default).Foreground.Dark,
                p));

            return stack;
        });

    private StackPanel BuildAboutPage()
    {
        _aboutPage = new TrayAppDotNETAboutPage(new TrayAppDotNETAboutPageOptions
        {
            Palette = Palette,
            ButtonRadius = RadiusMedium,
            CardRadius = RadiusLarge,
            Localize = L,
            Save = Save,
            ApplicationName = Constants.ApplicationName,
            Tagline = L("Settings_About_Tagline", "A tray-based battery status monitor."),
            BuildNumber = BuildInfo.BuildNumber,
            Publisher = Constants.Publisher,
            HelpLink = Constants.HelpLink,
            UpdateSettings = _settings,
            UpdateService = static () => AppServices.UpdateCheckService,
            ConfirmAsync = ConfirmAsync,
            Shutdown = () =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
            },
            Log = TADNLog.Log,
            RebuildAboutPage = () => RebuildShell(BatterySettingsPage.About),
            StaleCheckTimerIntervalMs = TimeConstants.AboutStaleCheckTimerIntervalMs,
            UpdateStaleGraceMs = TimeConstants.UpdateStaleGraceMs,
        });
        return _aboutPage.Build();
    }
}
