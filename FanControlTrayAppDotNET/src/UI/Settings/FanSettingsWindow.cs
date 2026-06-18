using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Settings;
using FanHotkeyAction = TrayAppDotNETCommon.Models.HotkeyAction;
using FanHotkeyApplyResult = TrayAppDotNETCommon.Services.HotkeyApplyResult;
using FanHotkeyBinding = TrayAppDotNETCommon.Models.HotkeyBinding;
using FanInstallScope = TrayAppDotNETCommon.InstallScope;

namespace FanControlTrayAppDotNET.UI;

public enum FanSettingsPage
{
    General,
    FanProperties,
    Flyout,
    TrayIcon,
    Hotkeys,
    Theme,
    About,
}

public sealed class FanSettingsWindow : SettingsWindowCommon<FanSettingsPage>
{
    private readonly AppSettings _settings;
    private readonly Action<string, FanInstallScope> _showUninstaller;
    private readonly List<FanSettingsSlotEntry> _fanSlots = [];
    private StackPanel? _fanSlotPanel;
    private Border? _draggedSlotRow;
    private FanSettingsSlotEntry? _draggedSlot;
    private Point _dragStart;
    private double _draggedSlotPointerOffsetY;
    private double _draggedSlotHeight;
    private int _draggedSlotTargetIndex = -1;
    private TrayAppDotNETAboutPage? _aboutPage;

    public FanSettingsWindow()
        : this(new AppSettings(), static (_, _) => { })
    {
    }

    public FanSettingsWindow(AppSettings settings, Action<string, FanInstallScope> showUninstaller)
    {
        _settings = settings;
        _showUninstaller = showUninstaller;
        ConfigureSettingsWindow(
            L("SettingsWindow_Title", "Settings"),
            width: 960,
            height: 670,
            minWidth: 720,
            minHeight: 520,
            AppTheme.LoadAppIcon());
        InitializeSettingsShell();
    }

    internal new void SelectPage(FanSettingsPage page) => base.SelectPage(page);

    protected override SettingsPalette Palette =>
        CreatePalette(AppServices.Theme, _settings, ResolveEffectiveIsLight());

    protected override bool EnableRoundedCorners => _settings.EnableRoundedCorners;

    protected override FanSettingsPage DefaultPageKey => FanSettingsPage.General;

    protected override string HeaderText => L("SettingsWindow_Header", "Settings");

    protected override string OpenSettingsFolderText =>
        L("SettingsWindow_OpenSettingsFolder", "Open settings folder");

    protected override string SettingsFolderPath => AppSettings.GetDefaultDirectory();

    protected override Color ConfirmOverlayBackdrop =>
        (AppServices.Theme ?? AppTheme.Default).FlyoutOverlayBackdrop.For(ResolveEffectiveIsLight());

    protected override void Save()
    {
        _settings.Save();
        _settings.RaiseChanged();
    }

    protected override bool ResolveEffectiveIsLightForBindings() => ResolveEffectiveIsLight();

    protected override IReadOnlyList<SettingsPageDescriptor<FanSettingsPage>> CreatePageDescriptors() =>
    [
        new(FanSettingsPage.General, Loc("Settings_Common_Page_General"),
            () => BuildSettingsPage(FanSettingsPage.General, BuildGeneralPage)),
        new(FanSettingsPage.FanProperties, L("Settings_Common_Page_FanProperties", "Fan properties"),
            () => BuildSettingsPage(FanSettingsPage.FanProperties, BuildFanPropertiesPage)),
        new(FanSettingsPage.Flyout, L("Settings_Common_Page_Flyout", "Flyout"),
            () => BuildSettingsPage(FanSettingsPage.Flyout, BuildFlyoutPage)),
        new(FanSettingsPage.TrayIcon, L("Settings_Common_Page_TrayIcon", "Tray icon"),
            () => BuildSettingsPage(FanSettingsPage.TrayIcon, BuildTrayIconPage)),
        new(FanSettingsPage.Hotkeys, Loc("Settings_Common_Page_Hotkeys"),
            () => BuildSettingsPage(FanSettingsPage.Hotkeys, BuildHotkeysPage)),
        new(FanSettingsPage.Theme, Loc("Settings_Common_Page_Theme"),
            () => BuildSettingsPage(FanSettingsPage.Theme, BuildThemePage)),
        new(FanSettingsPage.About, Loc("Settings_Common_Page_About"),
            () => BuildSettingsPage(FanSettingsPage.About, BuildAboutPage)),
    ];

    internal static SettingsPalette CreatePalette(AppTheme? theme, AppSettings settings, bool isLight)
    {
        AppTheme resolvedTheme = theme ?? AppTheme.Default;
        return new SettingsPalette(
            resolvedTheme.ResolveBackground(settings, isLight),
            resolvedTheme.ResolveForeground(settings, isLight),
            resolvedTheme.Border.For(isLight),
            resolvedTheme.Hover.For(isLight),
            resolvedTheme.Pressed.For(isLight),
            resolvedTheme.CardBackground.For(isLight),
            resolvedTheme.ControlBackground.For(isLight),
            resolvedTheme.SecondaryForeground.For(isLight),
            resolvedTheme.DisabledForeground.For(isLight),
            resolvedTheme.Accent.For(isLight),
            resolvedTheme.ToggleSwitchOnTrack.For(isLight),
            resolvedTheme.ToggleSwitchOnThumb.For(isLight),
            resolvedTheme.TextBoxFocused.For(isLight),
            resolvedTheme.SliderProgress.For(isLight),
            resolvedTheme.SliderTrack.For(isLight),
            resolvedTheme.SliderThumb.For(isLight),
            resolvedTheme.CloseButtonHover.For(isLight),
            resolvedTheme.CloseButtonPressed.For(isLight),
            resolvedTheme.CloseButtonGlyphActive.For(isLight));
    }

    private bool ResolveEffectiveIsLight() => AppTheme.ResolveEffectiveIsLightTheme(_settings);

    private Control BuildSettingsPage(FanSettingsPage page, Func<Control> buildPage)
    {
        if (page != FanSettingsPage.About)
            StopAboutUpdateRefresh();

        return buildPage();
    }

    protected override void OnSettingsWindowClosed()
    {
        StopAboutUpdateRefresh();
        base.OnSettingsWindowClosed();
    }

    private StackPanel BuildGeneralPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L("Settings_General_SectionHeader", "General"), p);

        TrayAppDotNETGeneralSettingsSection commonSection = CreateGeneralSettingsSection(p);
        stack.Children.Add(commonSection.BuildStartupCard());
        CreateMemorySettingsSection(p).AddCards(stack);
        CreateKeepWarmSettingsSection(p).AddCards(stack);

        stack.Children.Add(BoolCard(
            L("Settings_General_DefaultToRPMMode_Title", "Default to RPM mode"),
            L("Settings_General_DefaultToRPMMode_Description", "Newly discovered fans start in RPM mode."),
            _settings.DefaultToRPMMode,
            v => _settings.DefaultToRPMMode = v,
            p));

        commonSection.AddInstallationSection(stack,
        [
            new TrayAppDotNETInstallCardOptions
            {
                Scope = FanInstallScope.LocalAppData,
                Title = L("Settings_General_LocalUser_Title", "Local user"),
                ExecutablePath = AppServices.InstallLayout.LocalAppDataInstallExecutable,
                Elevated = false,
                Install = static () => AppServices.Installation.InstallToLocalAppData(),
                UninstallAsync = refresh =>
                {
                    _showUninstaller(AppServices.InstallLayout.LocalAppDataInstallDirectory,
                        FanInstallScope.LocalAppData);
                    return Task.CompletedTask;
                },
            },
            new TrayAppDotNETInstallCardOptions
            {
                Scope = FanInstallScope.ProgramFiles,
                Title = L("Settings_General_SystemWide_Title", "System-wide"),
                ExecutablePath = AppServices.InstallLayout.ProgramFilesInstallExecutable,
                Elevated = true,
                Install = static () => AppServices.Installation.InstallSystemWide(),
                UninstallAsync = refresh =>
                {
                    _showUninstaller(AppServices.InstallLayout.ProgramFilesInstallDirectory,
                        FanInstallScope.ProgramFiles);
                    return Task.CompletedTask;
                },
            },
        ]);

        return stack;
    }

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

    private StackPanel BuildFanPropertiesPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L("Settings_FanProperties_SectionHeader", "Fan properties"), p);

        stack.Children.Add(IntCard(
            L("Settings_FanProperties_DefaultJumpstart_Title", "Default jumpstart"),
            L("Settings_FanProperties_DefaultJumpstart_Description", "Initial duty cycle for newly discovered fans."),
            _settings.DefaultJumpstartDutyCycle,
            0,
            100,
            v => _settings.DefaultJumpstartDutyCycle = v,
            p,
            "%"));
        stack.Children.Add(IntCard(
            L("Settings_FanProperties_DefaultDeltaMax_Title", "Default max delta"),
            L("Settings_FanProperties_DefaultDeltaMax_Description", "Default maximum fan speed change per second."),
            _settings.DefaultDeltaMaxDutyCycle,
            0,
            100,
            v => _settings.DefaultDeltaMaxDutyCycle = v,
            p,
            "%/s"));
        stack.Children.Add(ComboCard(
            L("Settings_FanProperties_DefaultCurve_Title", "Default curve"),
            L("Settings_FanProperties_DefaultCurve_Description", "Curve assigned to newly discovered fans."),
            CurveOptions(),
            string.IsNullOrWhiteSpace(_settings.DefaultAssignedCurve) ? "None" : _settings.DefaultAssignedCurve,
            tag => _settings.DefaultAssignedCurve = tag,
            p,
            autoSizeToText: true));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_FanProperties_Reassign_Header", "Reassign saved fan settings"), p));
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            L("Settings_FanProperties_Reassign_Description",
                "Drag rows, or use Ctrl+Up/Ctrl+Down, then apply to move saved settings between physical fan slots."),
            p,
            new Thickness(0, 0, 0, 12)));

        _fanSlotPanel = new StackPanel();
        RebuildFanSlots();
        stack.Children.Add(RawCard(_fanSlotPanel, p));

        SettingsButton apply = Button(L("Settings_FanProperties_ApplyFanSwaps_Button", "Apply swaps"), p);
        apply.HorizontalAlignment = HorizontalAlignment.Right;
        apply.Margin = new Thickness(0, 6, 0, 14);
        apply.IsEnabled = _fanSlots.Count > 1;
        apply.Click += (_, _) => ApplyFanSlotSwaps();
        stack.Children.Add(apply);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_FanProperties_NonFunctioning_Header", "Non-functioning fans"), p));
        foreach (Fan fan in GetLiveFans())
        {
            stack.Children.Add(BoolCard(
                fan.DisplayName,
                fan.ControllerDisplayLabel,
                fan.ForcedNonFunctioning,
                value =>
                {
                    fan.ForcedNonFunctioning = value;
                    AppServices.LHMService?.PersistLiveState(save: false);
                },
                p));
        }

        if (GetLiveFans().Count == 0)
            stack.Children.Add(RawCard(TrayAppDotNETSettingsUI.DescriptionText(
                L("Settings_FanProperties_NoFans", "No live fans detected."), p), p));

        return stack;
    }

    private StackPanel BuildFlyoutPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L("Settings_Flyout_SectionHeader", "Flyout"), p);

        stack.Children.Add(BoolCard(
            L("Settings_Flyout_RestoreUndockState_Title", "Restore undocked state"),
            L("Settings_Flyout_RestoreUndockState_Description", "Reopen the flyout at its saved floating position."),
            _settings.RestoreFlyoutUndockedOnStartup,
            v => _settings.RestoreFlyoutUndockedOnStartup = v,
            p));
        stack.Children.Add(BoolCard(
            L("Settings_Flyout_ShowUndockButton_Title", "Allow undocking"),
            L("Settings_Flyout_ShowUndockButton_Description", "Show the undock/redock control in the flyout header."),
            _settings.AllowFlyoutUndock,
            v => _settings.AllowFlyoutUndock = v,
            p));
        stack.Children.Add(BoolCard(
            L("Settings_Flyout_ShowNonFunctioningFans_Title", "Show non-functioning fans"),
            L("Settings_Flyout_ShowNonFunctioningFans_Description",
                "Include detached or forced non-functioning fans in the flyout."),
            _settings.ShowNonFunctioningFans,
            v => _settings.ShowNonFunctioningFans = v,
            p));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(L("Settings_Flyout_Layout_Header", "Layout"), p));
        stack.Children.Add(IntCard("Card spacing", "Vertical spacing between fan cards.", _settings.FlyoutCardSpacing,
            0, 48, v => _settings.FlyoutCardSpacing = v, p));
        stack.Children.Add(IntCard("Card horizontal inset", "Horizontal inset inside the flyout list.",
            _settings.FlyoutCardHorizontalInset, 0, 48, v => _settings.FlyoutCardHorizontalInset = v, p));
        stack.Children.Add(IntCard("Title bar spacing", "Gap between the title bar and first card.",
            _settings.FlyoutTitleBarCardSpacing, 0, 48, v => _settings.FlyoutTitleBarCardSpacing = v, p));
        stack.Children.Add(BoolCard("Card borders", "Draw persistent borders around flyout cards.",
            _settings.EnableCardBorders, v => _settings.EnableCardBorders = v, p));
        stack.Children.Add(BoolCard("Hovered card borders", "Draw borders only while hovering cards.",
            _settings.EnableHoveredCardBorders, v => _settings.EnableHoveredCardBorders = v, p));
        stack.Children.Add(BoolCard("Hide grouped fan borders", "Suppress borders on fan rows inside a group.",
            _settings.HideGroupedFanCardBorders, v => _settings.HideGroupedFanCardBorders = v, p));
        stack.Children.Add(BoolCard("Use group background", "Use the group card background for grouped fan rows.",
            _settings.UseGroupBackgroundForGroupedFanCards, v => _settings.UseGroupBackgroundForGroupedFanCards = v,
            p));
        stack.Children.Add(BoolCard("Square title bar corners",
            "Keep the flyout title bar square even when rounded corners are enabled.",
            _settings.SquareFlyoutTitleBarCorners, v => _settings.SquareFlyoutTitleBarCorners = v, p));

        return stack;
    }

    private StackPanel BuildTrayIconPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L("Settings_TrayIcon_SectionHeader", "Tray icon"), p);

        stack.Children.Add(BoolCard("Tray wheel", "Allow mouse wheel events over the tray icon.",
            _settings.TrayScrollEnabled, v => _settings.TrayScrollEnabled = v, p));
        stack.Children.Add(BoolCard("CPU temperature tooltip", "Show CPU temperature in the tray tooltip.",
            _settings.ShowCPUTempInTooltip, v => _settings.ShowCPUTempInTooltip = v, p));
        stack.Children.Add(BoolCard("GPU temperature tooltip", "Show GPU temperature in the tray tooltip.",
            _settings.ShowGPUTempInTooltip, v => _settings.ShowGPUTempInTooltip = v, p));
        stack.Children.Add(StringComboCard(
            "Context menu position",
            "Classic opens at the cursor; Modern centers on the tray icon.",
            [
                (ContextMenuPosition.Classic, "Classic"),
                (ContextMenuPosition.Modern, "Modern"),
            ],
            _settings.ContextMenuPosition,
            v => _settings.ContextMenuPosition = v,
            p));
        stack.Children.Add(StringComboCard(
            "Double click",
            "Action to run on tray double click.",
            TrayClickActionOptions(),
            _settings.TrayDoubleClickAction,
            v => _settings.TrayDoubleClickAction = v,
            p));
        stack.Children.Add(StringComboCard("Ctrl + left click", "Modifier tray action.", TrayClickActionOptions(),
            _settings.TrayCtrlLeftClickAction, v => _settings.TrayCtrlLeftClickAction = v, p));
        stack.Children.Add(StringComboCard("Alt + left click", "Modifier tray action.", TrayClickActionOptions(),
            _settings.TrayAltLeftClickAction, v => _settings.TrayAltLeftClickAction = v, p));
        stack.Children.Add(StringComboCard("Ctrl + right click", "Modifier tray action.", TrayClickActionOptions(),
            _settings.TrayCtrlRightClickAction, v => _settings.TrayCtrlRightClickAction = v, p));
        stack.Children.Add(StringComboCard("Alt + right click", "Modifier tray action.", TrayClickActionOptions(),
            _settings.TrayAltRightClickAction, v => _settings.TrayAltRightClickAction = v, p));
        stack.Children.Add(StringComboCard("Ctrl + double click", "Modifier tray action.", TrayClickActionOptions(),
            _settings.TrayCtrlDoubleLeftClickAction, v => _settings.TrayCtrlDoubleLeftClickAction = v, p));
        stack.Children.Add(StringComboCard("Alt + double click", "Modifier tray action.", TrayClickActionOptions(),
            _settings.TrayAltDoubleLeftClickAction, v => _settings.TrayAltDoubleLeftClickAction = v, p));
        return stack;
    }

    private StackPanel BuildHotkeysPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc("Settings_Hotkeys_SectionHeader"), p);
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            Loc("Settings_Hotkeys_SectionDescription"), p, new Thickness(0, 0, 0, 16)));

        AddHotkeyRow(stack, FanHotkeyAction.OpenFlyout,
            Loc("Settings_Hotkeys_OpenFlyout_Title"),
            Loc("Settings_Hotkeys_OpenFlyout_Description"),
            p);
        AddHotkeyRow(stack, FanHotkeyAction.OpenSettings,
            Loc("Settings_Hotkeys_OpenSettings_Title"),
            Loc("Settings_Hotkeys_OpenSettings_Description"),
            p);
        return stack;
    }

    private StackPanel BuildThemePage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(Loc("Settings_Theme_SectionHeader"), p);
        AppTheme theme = AppServices.Theme ?? AppTheme.Default;
        bool isLight = ResolveEffectiveIsLight();

        stack.Children.Add(IntCard("Context menu font size", "Controls tray menu text size.",
            _settings.ContextMenuFontSize, 10, 28, v => _settings.ContextMenuFontSize = v, p));
        stack.Children.Add(StringComboCard(
            Loc("Settings_Theme_ThemeStyle_Title"),
            Loc("Settings_Theme_ThemeStyle_Description"),
            [
                (ThemeMode.System, Loc("Settings_Theme_ThemeStyle_System")),
                (ThemeMode.Light, Loc("Settings_Theme_ThemeStyle_Light")),
                (ThemeMode.Dark, Loc("Settings_Theme_ThemeStyle_Dark")),
            ],
            _settings.ThemeMode,
            v => _settings.ThemeMode = v,
            p,
            afterSave: () => RebuildShell(FanSettingsPage.Theme)));
        stack.Children.Add(BoolCard(
            Loc("Settings_Theme_RoundedCorners_Title"),
            Loc("Settings_Theme_RoundedCorners_Description"),
            _settings.EnableRoundedCorners,
            v => _settings.EnableRoundedCorners = v,
            p,
            afterSave: () => RebuildShell(FanSettingsPage.Theme)));

        stack.Children.Add(VariantColorCard("Text", Loc("Settings_Theme_TextColor_Title"),
            Loc("Settings_Theme_TextColor_Description"), Loc("Settings_Theme_TextColor_LightTooltip"),
            Loc("Settings_Theme_TextColor_DarkTooltip"), _settings.TextColor, theme.Foreground.Light,
            theme.Foreground.Dark, p));
        stack.Children.Add(VariantColorCard("Background", Loc("Settings_Theme_BackgroundColor_Title"),
            Loc("Settings_Theme_BackgroundColor_Description"), Loc("Settings_Theme_BackgroundColor_LightTooltip"),
            Loc("Settings_Theme_BackgroundColor_DarkTooltip"), _settings.BackgroundColor, theme.Background.Light,
            theme.Background.Dark, p));
        stack.Children.Add(VariantColorCard("FlyoutBackground", "Flyout background", "Override the flyout background.",
            "Light flyout background", "Dark flyout background", _settings.FlyoutBackgroundColor,
            theme.FlyoutBackground.Light, theme.FlyoutBackground.Dark, p));
        stack.Children.Add(VariantColorCard("FlyoutTitleBar", "Flyout title bar",
            "Override the flyout title bar background.", "Light title bar", "Dark title bar",
            _settings.FlyoutTitleBarBackgroundColor, theme.FlyoutTitleBarBackground.Light,
            theme.FlyoutTitleBarBackground.Dark, p));
        stack.Children.Add(VariantColorCard("FanCard", "Fan card", "Override standalone fan card backgrounds.",
            "Light fan card", "Dark fan card", _settings.FanCardBackgroundColor, theme.FanCardBackground.Light,
            theme.FanCardBackground.Dark, p));
        stack.Children.Add(VariantColorCard("GroupCard", "Group card", "Override group card backgrounds.",
            "Light group card", "Dark group card", _settings.GroupCardBackgroundColor, theme.GroupCardBackground.Light,
            theme.GroupCardBackground.Dark, p));
        stack.Children.Add(VariantColorCard("CardBorder", "Card border", "Override flyout card border color.",
            "Light border", "Dark border", _settings.CardBorderColor, theme.FlyoutCardBorder.Light,
            theme.FlyoutCardBorder.Dark, p));
        stack.Children.Add(VariantColorCard("TrayIcon", Loc("Settings_Theme_StaticIconColor_Title"),
            Loc("Settings_Theme_StaticIconColor_Description"), Loc("Settings_Theme_StaticIconColor_LightTooltip"),
            Loc("Settings_Theme_StaticIconColor_DarkTooltip"), _settings.TrayIconColor, theme.Foreground.Light,
            theme.Foreground.Dark, p));

        SettingsComboBox sliderThumbCombo = TrayAppDotNETSettingsUI.ComboBox(p, autoSizeToText: true,
            autoSizeMode: SettingsComboBoxAutoSizeMode.SelectedItem);
        foreach (SliderThumbGlyphOption option in _settings.SliderThumbOptions)
            sliderThumbCombo.Items.Add(new SettingsComboBoxItem(option.Name, option.Name, p));
        TrayAppDotNETSettingsUI.SelectComboByTag(sliderThumbCombo, _settings.SliderThumbGlyph);
        sliderThumbCombo.SelectionChanged += (_, _) =>
        {
            if (TrayAppDotNETSettingsUI.SelectedTag(sliderThumbCombo) is not { Length: > 0 } tag) return;
            if (_settings.SliderThumbOptions.Any(o => o.Name == tag))
                _settings.SliderThumbGlyph = tag;
            Save();
        };
        stack.Children.Add(Card("Slider thumb", "Shape used by flyout sliders.", sliderThumbCombo, p));

        _ = isLight;
        return stack;
    }

    private StackPanel BuildAboutPage()
    {
        StopAboutUpdateRefresh();
        _aboutPage = new TrayAppDotNETAboutPage(new TrayAppDotNETAboutPageOptions
        {
            Palette = Palette,
            ButtonRadius = RadiusMedium,
            CardRadius = RadiusLarge,
            Localize = L,
            Save = Save,
            ApplicationName = Constants.ApplicationName,
            Tagline = L("Settings_About_Tagline", "A tray-based fan controller."),
            BuildNumber = BuildInfo.BuildNumber,
            Publisher = Constants.Publisher,
            HelpLink = Constants.HelpLink,
            UpdateSettings = _settings,
            UpdateService = static () => AppServices.UpdateCheckService,
            ConfirmAsync = ConfirmAsync,
            Log = static message => TADNLog.Log(message),
            RebuildAboutPage = () => RebuildShell(FanSettingsPage.About),
            StaleCheckTimerIntervalMs = TimeConstants.AboutStaleCheckTimerIntervalMs,
            UpdateStaleGraceMs = TimeConstants.UpdateStaleGraceMs,
        });
        return _aboutPage.Build();
    }

    private void StopAboutUpdateRefresh() =>
        _aboutPage?.StopUpdateRefresh();

    private void AddHotkeyRow(StackPanel stack, FanHotkeyAction action, string title, string description,
        SettingsPalette p)
    {
        StackPanel entries = new() { Spacing = 0 };
        uint selectedModifiers = 0;
        uint selectedVk = 0;

        SettingsComboBox modifiers = TrayAppDotNETSettingsUI.ComboBox(p, 170);
        modifiers.Padding = new Thickness(8, 0, 2, 0);
        foreach (TrayAppDotNETHotkeyModifierOption option in HotkeyModifierOptions)
            modifiers.Items.Add(new SettingsComboBoxItem(option.Modifiers, option.Label, p));

        TextBox keyBox = TrayAppDotNETSettingsUI.TextBox(p, 60);
        keyBox.IsReadOnly = true;
        keyBox.Cursor = new Cursor(StandardCursorType.Ibeam);

        SettingsButton addButton = Button(Loc("Settings_Hotkeys_Add_Button"), p);
        addButton.MinWidth = 70;
        addButton.IsEnabled = false;

        void UpdateAddButtonState()
        {
            if (selectedModifiers == 0 || selectedVk == 0)
            {
                addButton.Text = Loc("Settings_Hotkeys_Add_Button");
                addButton.IsEnabled = false;
                return;
            }

            bool exists = _settings.Hotkeys.Any(b =>
                !b.RemovedByUser
                && b.Matches(action, string.Empty)
                && b.Modifiers == selectedModifiers
                && b.VirtualKey == selectedVk);
            addButton.Text = exists
                ? Loc("Settings_Hotkeys_Exists_Button")
                : Loc("Settings_Hotkeys_Add_Button");
            addButton.IsEnabled = !exists;
        }

        void Refresh()
        {
            FanHotkeyApplyResult? applyResult = null;
            try { applyResult = AppServices.HotkeyService?.Apply(_settings.Hotkeys); }
            catch (Exception ex) { TADNLog.Log($"FanSettingsWindow.Hotkeys.Apply: {ex.Message}"); }

            entries.Children.Clear();
            foreach (FanHotkeyBinding binding in _settings.Hotkeys
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
            if (vk is 0 or 0x7B)
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
            int id = _settings.Hotkeys.Where(h => h.Matches(action, string.Empty)).Select(h => h.BindingID)
                .DefaultIfEmpty(0).Max() + 1;
            _settings.Hotkeys.Add(new FanHotkeyBinding
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

        stack.Children.Add(RawCard(grid, p));
        Refresh();
    }

    private Border BuildHotkeyEntryCard(
        FanHotkeyAction action,
        FanHotkeyBinding binding,
        FanHotkeyApplyResult? applyResult,
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
            TrayAppDotNETToolTip.SetTip(status, Loc("Settings_Hotkeys_Status_HotkeyServiceUnavailable"));
        }
        else if (applyResult?.Failed.TryGetValue(binding, out string? error) == true)
        {
            status.Text = GlyphCatalog.WARNING;
            TrayAppDotNETToolTip.SetTip(status, error);
        }
        else if (binding.IsBound)
        {
            TrayAppDotNETToolTip.SetTip(status, Loc("Settings_Hotkeys_Status_Registered"));
        }

        SettingsButton delete = Button("x", p);
        delete.Width = 32;
        delete.Height = 29;
        delete.Padding = new Thickness(0);
        delete.Label.FontSize = 20;
        delete.Click += (_, _) =>
        {
            _settings.Hotkeys.RemoveAll(b => b.Matches(action, string.Empty, binding.BindingID));
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

    private void RebuildFanSlots()
    {
        _fanSlots.Clear();
        foreach (Fan fan in GetLiveFans())
        {
            _fanSlots.Add(new FanSettingsSlotEntry(
                KeyForFan(fan),
                fan.DisplayName,
                fan.ControllerDisplayLabel));
        }

        RenderFanSlots();
    }

    private void RenderFanSlots()
    {
        if (_fanSlotPanel == null) return;
        _fanSlotPanel.Children.Clear();
        if (_fanSlots.Count == 0)
        {
            _fanSlotPanel.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
                L("Settings_FanProperties_NoFans", "No live fans detected."), Palette));
            return;
        }

        for (int i = 0; i < _fanSlots.Count; i++)
            _fanSlotPanel.Children.Add(BuildFanSlotRow(_fanSlots[i], i, Palette));
    }

    private Border BuildFanSlotRow(FanSettingsSlotEntry slot, int index, SettingsPalette p)
    {
        TextBlock handle = TrayAppDotNETSettingsUI.Text(GlyphCatalog.DRAG_HANDLE, p, 16);
        handle.FontFamily = TrayAppDotNETSettingsUI.IconFont;
        handle.Width = 28;
        handle.VerticalAlignment = VerticalAlignment.Center;
        handle.HorizontalAlignment = HorizontalAlignment.Center;

        StackPanel text = new();
        text.Children.Add(TrayAppDotNETSettingsUI.TitleText(slot.DisplayName, p));
        text.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(slot.Detail, p));

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.Children.Add(handle);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        Border row = new()
        {
            Tag = slot,
            Background = TrayAppDotNETSettingsUI.Brush(p.ControlBackground),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = RadiusMedium,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 4),
            Child = grid,
            Focusable = true,
            Cursor = new Cursor(StandardCursorType.Hand),
        };

        bool pointerOver = false;
        bool pointerPressed = false;
        UpdateFanSlotRowVisual(row, slot, p, pointerOver, pointerPressed);

        row.PointerEntered += (_, _) =>
        {
            pointerOver = true;
            UpdateFanSlotRowVisual(row, slot, p, pointerOver, pointerPressed);
        };
        row.PointerExited += (_, _) =>
        {
            pointerOver = false;
            pointerPressed = false;
            UpdateFanSlotRowVisual(row, slot, p, pointerOver, pointerPressed);
        };
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            _draggedSlot = slot;
            _draggedSlotRow = row;
            _dragStart = e.GetPosition(_fanSlotPanel);
            _draggedSlotPointerOffsetY = e.GetPosition(row).Y;
            _draggedSlotHeight = Math.Max(1, row.Bounds.Height);
            _draggedSlotTargetIndex = _fanSlots.IndexOf(slot);
            pointerPressed = true;
            UpdateFanSlotRowVisual(row, slot, p, pointerOver, pointerPressed);
            e.Pointer.Capture(row);
            e.Handled = true;
        };
        row.PointerMoved += (_, e) =>
        {
            if (_draggedSlot == null || _fanSlotPanel == null) return;
            Point current = e.GetPosition(_fanSlotPanel);
            if (Math.Abs(current.Y - _dragStart.Y) < 4) return;
            double draggedMidpoint = current.Y - _draggedSlotPointerOffsetY + _draggedSlotHeight / 2.0;
            _draggedSlotTargetIndex = FanSlotInsertionIndexFromMidpoint(draggedMidpoint);
            ApplyFanSlotDragPreview();
            row.RenderTransform = new TranslateTransform(0, current.Y - _dragStart.Y);
            e.Handled = true;
        };
        row.PointerReleased += (_, e) =>
        {
            pointerPressed = false;
            EndFanSlotDrag(e.Pointer);
        };
        row.PointerCaptureLost += (_, _) =>
        {
            pointerPressed = false;
            EndFanSlotDrag(null);
        };
        row.KeyDown += (_, e) =>
        {
            if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
            if (e.Key is not (Key.Up or Key.Down)) return;
            int currentIndex = _fanSlots.IndexOf(slot);
            int nextIndex = e.Key == Key.Up ? currentIndex - 1 : currentIndex + 1;
            if (currentIndex >= 0 && nextIndex >= 0 && nextIndex < _fanSlots.Count)
            {
                _fanSlots.RemoveAt(currentIndex);
                _fanSlots.Insert(nextIndex, slot);
                RenderFanSlots();
            }

            e.Handled = true;
        };

        TrayAppDotNETToolTip.SetTip(row, "Drag to reorder, or press Ctrl+Up/Ctrl+Down.");
        _ = index;
        return row;
    }

    private void UpdateFanSlotRowVisual(
        Border row,
        FanSettingsSlotEntry slot,
        SettingsPalette p,
        bool pointerOver,
        bool pointerPressed)
    {
        bool dragging = ReferenceEquals(slot, _draggedSlot);
        Color background = pointerPressed
            ? p.Pressed
            : pointerOver
                ? p.Hover
                : p.ControlBackground;
        row.Background = TrayAppDotNETSettingsUI.Brush(background);
        row.BorderBrush = TrayAppDotNETSettingsUI.Brush(dragging ? p.Accent : Colors.Transparent);
        row.BorderThickness = dragging ? new Thickness(1) : new Thickness(0);
        row.Opacity = dragging ? 0.82 : 1.0;
        row.SetValue(ZIndexProperty, dragging ? 1 : 0);
    }

    private int FanSlotInsertionIndexFromMidpoint(double draggedMidpointY)
    {
        if (_fanSlotPanel == null) return -1;
        int insertion = 0;
        for (int i = 0; i < _fanSlotPanel.Children.Count; i++)
        {
            Control child = _fanSlotPanel.Children[i];
            if (ReferenceEquals(child, _draggedSlotRow)) continue;
            Point? topLeft = child.TranslatePoint(new Point(0, 0), _fanSlotPanel);
            if (topLeft == null) continue;
            if (draggedMidpointY > topLeft.Value.Y + child.Bounds.Height / 2.0) insertion++;
            else break;
        }

        int max = _fanSlots.Count - (_draggedSlot != null ? 1 : 0);
        return Math.Clamp(insertion, 0, Math.Max(0, max));
    }

    private void ApplyFanSlotDragPreview()
    {
        if (_fanSlotPanel == null || _draggedSlot == null || _draggedSlotRow == null) return;
        ResetFanSlotDragPreview();

        int sourceIndex = _fanSlots.IndexOf(_draggedSlot);
        if (sourceIndex < 0) return;

        int targetIndex = Math.Clamp(_draggedSlotTargetIndex, 0, Math.Max(0, _fanSlots.Count - 1));
        double offset = Math.Max(1, _draggedSlotHeight + Math.Max(0, _draggedSlotRow.Margin.Bottom));
        if (targetIndex < sourceIndex)
        {
            for (int i = targetIndex; i < sourceIndex; i++)
                SetFanSlotPreviewOffset(i, offset);
        }
        else if (targetIndex > sourceIndex)
        {
            for (int i = sourceIndex + 1; i <= targetIndex && i < _fanSlotPanel.Children.Count; i++)
                SetFanSlotPreviewOffset(i, -offset);
        }
    }

    private void SetFanSlotPreviewOffset(int index, double offset)
    {
        if (_fanSlotPanel == null) return;
        if (index < 0 || index >= _fanSlotPanel.Children.Count) return;
        if (ReferenceEquals(_fanSlotPanel.Children[index], _draggedSlotRow)) return;
        _fanSlotPanel.Children[index].RenderTransform = new TranslateTransform(0, offset);
    }

    private void ResetFanSlotDragPreview()
    {
        if (_fanSlotPanel == null) return;
        foreach (Control child in _fanSlotPanel.Children)
        {
            if (ReferenceEquals(child, _draggedSlotRow)) continue;
            child.RenderTransform = null;
        }
    }

    private void EndFanSlotDrag(IPointer? pointer)
    {
        FanSettingsSlotEntry? dragged = _draggedSlot;
        int targetIndex = _draggedSlotTargetIndex;
        bool hadDrag = dragged != null;
        _draggedSlotRow?.RenderTransform = null;
        if (_fanSlotPanel != null)
            foreach (Control child in _fanSlotPanel.Children)
                child.RenderTransform = null;
        _draggedSlotRow = null;
        _draggedSlot = null;
        _draggedSlotTargetIndex = -1;
        _draggedSlotPointerOffsetY = 0;
        _draggedSlotHeight = 0;
        pointer?.Capture(null);
        if (dragged != null && targetIndex >= 0)
        {
            int currentIndex = _fanSlots.IndexOf(dragged);
            if (currentIndex >= 0 && targetIndex != currentIndex)
            {
                _fanSlots.RemoveAt(currentIndex);
                _fanSlots.Insert(Math.Clamp(targetIndex, 0, _fanSlots.Count), dragged);
            }
        }

        if (hadDrag) RenderFanSlots();
    }

    private void ApplyFanSlotSwaps()
    {
        List<Fan> fans = GetLiveFans();
        if (fans.Count < 2 || fans.Count != _fanSlots.Count) return;

        Dictionary<string, FanUserSettings> snapshots = fans
            .ToDictionary(KeyForFan, f => f.SnapshotUserSettings(), StringComparer.OrdinalIgnoreCase);
        if (_fanSlots.Any(slot => !snapshots.ContainsKey(slot.Key))) return;

        for (int i = 0; i < fans.Count; i++)
            fans[i].ApplyUserSettings(snapshots[_fanSlots[i].Key]);

        AppServices.LHMService?.PersistLiveState(save: false);
        Save();
        RebuildFanSlots();
    }

    private static List<Fan> GetLiveFans() => AppServices.LHMService?.Fans.ToList() ?? [];

    private static string KeyForFan(Fan fan) =>
        !string.IsNullOrWhiteSpace(fan.DataSourceKey)
            ? fan.DataSourceKey
            : $"{fan.ControllerModel}.{fan.ControlsName}.{fan.FansName}";

    private static List<(string Tag, string Text)> CurveOptions()
    {
        List<(string Tag, string Text)> items = [("None", "None")];
        foreach (Curve curve in Curve.Curves.Values.OrderBy(c => c.CurveName, StringComparer.OrdinalIgnoreCase))
            items.Add((curve.CurveName, curve.CurveName));
        return items;
    }

    private static IReadOnlyList<(TrayClickAction Value, string Text)> TrayClickActionOptions() =>
    [
        (TrayClickAction.Nothing, "Nothing"),
        (TrayClickAction.OpenSettings, "Open settings"),
    ];

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

    private static string FormatHotkey(FanHotkeyBinding binding)
    {
        string modifiers = TrayAppDotNETHotkeyKeys.ModifierText(binding.Modifiers);
        string key = TrayAppDotNETHotkeyKeys.KeyName(binding.VirtualKey);
        return string.IsNullOrEmpty(modifiers) ? key : modifiers + " + " + key;
    }

    private static IReadOnlyList<TrayAppDotNETHotkeyModifierOption> HotkeyModifierOptions =>
        TrayAppDotNETHotkeyModifierOptions.Create(Loc);

    private sealed record FanSettingsSlotEntry(string Key, string DisplayName, string Detail);
}
