using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Settings;
using GlyphApplicator = TrayAppDotNETCommon.Visuals.GlyphApplicator;
using FanHotkeyAction = TrayAppDotNETCommon.Models.HotkeyAction;
using FanHotkeyApplyResult = TrayAppDotNETCommon.Services.HotkeyApplyResult;
using FanHotkeyBinding = TrayAppDotNETCommon.Models.HotkeyBinding;
using FanInstallScope = TrayAppDotNETCommon.Models.InstallScope;

namespace FanControlTrayAppDotNET.UI.Settings;

public enum FanSettingsPage
{
    General,
    FanProperties,
    Flyout,
    TrayIcon,
    Hotkeys,
    Theme,
    About
}

public sealed class FanSettingsWindow : SettingsWindowCommon<FanSettingsPage>
{
    private const int NonFunctioningFanColumnCount = 2;
    private const double NonFunctioningFanColumnGap = 8.0;
    private const double NonFunctioningFanCardBottomGap = 6.0;

    private readonly AppSettings _settings;
    private readonly Action<string, FanInstallScope> _showUninstaller;
    private readonly List<FanPropertiesPageGeneration> _fanPropertiesPageGenerations = [];
    private readonly List<TrayAppDotNETAboutPage> _aboutPageGenerations = [];

    public FanSettingsWindow()
        : this(new AppSettings(), static (_, _) => { })
    {
    }

    public FanSettingsWindow(AppSettings settings, Action<string, FanInstallScope> showUninstaller)
    {
        _settings = settings;
        _showUninstaller = showUninstaller;
        ConfigureSettingsWindow(L("SettingsWindow_Title", "Settings"), AppTheme.LoadAppIcon());
        InitializeSettingsShell();
    }

    internal new void SelectPage(FanSettingsPage page) => base.SelectPage(page);

    protected override SettingsPalette ResolvePalette() =>
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
            BuildGeneralPage),
        new(FanSettingsPage.FanProperties, L("Settings_Common_Page_FanProperties", "Fan properties"),
            BuildFanPropertiesPage),
        new(FanSettingsPage.Flyout, L("Settings_Common_Page_Flyout", "Flyout"),
            BuildFlyoutPage),
        new(FanSettingsPage.TrayIcon, L("Settings_Common_Page_TrayIcon", "Tray icon"),
            BuildTrayIconPage),
        new(FanSettingsPage.Hotkeys, Loc("Settings_Common_Page_Hotkeys"),
            BuildHotkeysPage),
        new(FanSettingsPage.Theme, Loc("Settings_Common_Page_Theme"),
            BuildThemePage),
        new(FanSettingsPage.About, Loc("Settings_Common_Page_About"),
            BuildAboutPage)
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
            resolvedTheme.SearchListItemSelected.For(isLight),
            resolvedTheme.SearchListItemHover.For(isLight),
            resolvedTheme.SliderProgress.For(isLight),
            resolvedTheme.SliderTrack.For(isLight),
            resolvedTheme.SliderThumb.For(isLight),
            resolvedTheme.CloseButtonHover.For(isLight),
            resolvedTheme.CloseButtonPressed.For(isLight),
            resolvedTheme.CloseButtonGlyphActive.For(isLight));
    }

    private bool ResolveEffectiveIsLight() => AppTheme.ResolveEffectiveIsLightTheme(_settings);

    private StackPanel BuildGeneralPage()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L("Settings_General_SectionHeader", "General"), p);

        TrayAppDotNETGeneralSettingsSection commonSection = CreateGeneralSettingsSection(p);
        stack.Children.Add(commonSection.BuildStartupCard());

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
                }
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
                }
            }
        ]);
        CreateRenderingSettingsSection(p).AddCards(stack);

        stack.Children.Add(BoolCard(
            L("Settings_General_DefaultToRPMMode_Title", "Default to RPM mode"),
            L("Settings_General_DefaultToRPMMode_Description", "Newly discovered fans start in RPM mode."),
            _settings.DefaultToRPMMode,
            v => _settings.DefaultToRPMMode = v,
            p));

        return stack;
    }

    private TrayAppDotNETRenderingSettingsSection CreateRenderingSettingsSection(SettingsPalette p) =>
        new(new TrayAppDotNETRenderingSettingsSectionOptions
        {
            Palette = p,
            CardRadius = RadiusLarge,
            Localize = L,
            Save = Save,
            ConfirmAsync = ConfirmAsync,
            ShowMessage = ShowMessage,
            RenderingSettings = _settings,
            WarmWindowSettings = _settings,
            SupportsFlyoutWarmWindow = true,
            SupportsTrayContextMenuWarmWindow = true
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

        FanPropertiesPageGeneration pageGeneration = new();
        _fanPropertiesPageGenerations.Add(pageGeneration);
        AddPageCleanup(() => RetireFanPropertiesPage(pageGeneration));
        RebuildFanSlots(pageGeneration);
        stack.Children.Add(RawCard(pageGeneration.FanSlotPanel, p));

        SettingsButton apply = Button(L("Settings_FanProperties_ApplyFanSwaps_Button", "Apply swaps"), p);
        apply.HorizontalAlignment = HorizontalAlignment.Right;
        apply.Margin = new Thickness(0, 6, 0, 14);
        apply.IsEnabled = pageGeneration.FanSlots.Count > 1;
        apply.Click += (_, _) => ApplyFanSlotSwaps(pageGeneration);
        stack.Children.Add(apply);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L("Settings_FanProperties_NonFunctioning_Header", "Manually tag fans as non-functioning"), p));

        List<Fan> liveFans = GetLiveFans();
        if (liveFans.Count == 0)
        {
            stack.Children.Add(RawCard(TrayAppDotNETSettingsUI.DescriptionText(
                L("Settings_FanProperties_NoFans", "No live fans detected."), p), p));
            return stack;
        }

        Grid nonFunctioningFanGrid = new();
        for (int i = 0; i < NonFunctioningFanColumnCount; i++)
            nonFunctioningFanGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (int i = 0; i < liveFans.Count; i++)
        {
            if (i % NonFunctioningFanColumnCount == 0)
                nonFunctioningFanGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            Fan fan = liveFans[i];
            int column = i % NonFunctioningFanColumnCount;
            int row = i / NonFunctioningFanColumnCount;
            Border card = BoolCard(
                fan.DisplayName,
                fan.ControllerDisplayLabel,
                fan.ForcedNonFunctioning,
                value =>
                {
                    fan.ForcedNonFunctioning = value;
                    AppServices.LHMService?.PersistLiveState(save: false);
                },
                p);
            card.Margin = new Thickness(
                column == 0 ? 0 : NonFunctioningFanColumnGap / 2.0,
                0,
                column == NonFunctioningFanColumnCount - 1 ? 0 : NonFunctioningFanColumnGap / 2.0,
                NonFunctioningFanCardBottomGap);
            Grid.SetColumn(card, column);
            Grid.SetRow(card, row);
            nonFunctioningFanGrid.Children.Add(card);
        }

        stack.Children.Add(nonFunctioningFanGrid);

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
            p,
            afterSave: () => RebuildShell(FanSettingsPage.Flyout)));
        if (_settings.AllowFlyoutUndock)
        {
            stack.Children.Add(BoolCard(
                L("Settings_Flyout_ClampUndockedToScreen_Title", "Keep undocked flyout on screen"),
                L("Settings_Flyout_ClampUndockedToScreen_Description",
                    "Keep the undocked flyout fully inside one monitor's work area when it restores or repositions."),
                _settings.ClampUndockedFlyoutToScreen,
                v => _settings.ClampUndockedFlyoutToScreen = v,
                p));
        }
        stack.Children.Add(BoolCard(
            L("Settings_Flyout_ShowNonFunctioningFans_Title", "Show non-functioning fans"),
            L("Settings_Flyout_ShowNonFunctioningFans_Description",
                "Include detached or forced non-functioning fans in the flyout."),
            _settings.ShowNonFunctioningFans,
            v => _settings.ShowNonFunctioningFans = v,
            p));
        stack.Children.Add(StringComboCard(
            L("Settings_Flyout_ShowMultipleSliderValues_Title", "Show multiple slider values"),
            L("Settings_Flyout_ShowMultipleSliderValues_Description",
                "Show both the manual and curve slider thumbs when possible. Only in manual means this only appears when the slider is in manual mode."),
            MultipleSliderValuesOptions(),
            _settings.ShowMultipleSliderValuesMode,
            v => _settings.ShowMultipleSliderValuesMode = v,
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
                (ContextMenuPosition.Modern, "Modern")
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
                (ThemeMode.Dark, Loc("Settings_Theme_ThemeStyle_Dark"))
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

        SettingsComboBox curveSliderThumbCombo = TrayAppDotNETSettingsUI.ComboBox(p, autoSizeToText: true,
            autoSizeMode: SettingsComboBoxAutoSizeMode.SelectedItem);
        foreach (SliderThumbGlyphOption option in _settings.SliderThumbOptions.Where(static o => o.IsGlyph))
            curveSliderThumbCombo.Items.Add(new SettingsComboBoxItem(option.Name, option.Name, p));
        TrayAppDotNETSettingsUI.SelectComboByTag(curveSliderThumbCombo, _settings.CurveSliderThumbGlyph);
        curveSliderThumbCombo.SelectionChanged += (_, _) =>
        {
            if (TrayAppDotNETSettingsUI.SelectedTag(curveSliderThumbCombo) is not { Length: > 0 } tag) return;
            if (_settings.SliderThumbOptions.Any(o => o is { IsGlyph: true } && o.Name == tag))
                _settings.CurveSliderThumbGlyph = tag;
            Save();
        };
        stack.Children.Add(Card("Curve slider thumb", "Shape used by non-manual flyout sliders.",
            curveSliderThumbCombo, p));

        _ = isLight;
        return stack;
    }

    private StackPanel BuildAboutPage()
    {
        TrayAppDotNETAboutPage aboutPage = OwnPageResource(new TrayAppDotNETAboutPage(
            new TrayAppDotNETAboutPageOptions
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
            PromptOwner = () => this,
            Log = static message => TADNLog.Log(message),
            RebuildAboutPage = () => RebuildShell(FanSettingsPage.About),
            StaleCheckTimerIntervalMs = TimeConstants.AboutStaleCheckTimerIntervalMs,
            UpdateStaleGraceMs = TimeConstants.UpdateStaleGraceMs
        }));
        _aboutPageGenerations.Add(aboutPage);
        AddPageCleanup(() => _aboutPageGenerations.Remove(aboutPage));
        return aboutPage.Build();
    }

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
        keyBox.Cursor = TrayAppDotNETCursors.IBeam;

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
                BindingID = id
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
            GlyphApplicator.ApplyTo(status, GlyphCatalog.WARNING);
            TrayAppDotNETToolTip.SetTip(status, Loc("Settings_Hotkeys_Status_HotkeyServiceUnavailable"));
        }
        else if (applyResult?.Failed.TryGetValue(binding, out string? error) == true)
        {
            GlyphApplicator.ApplyTo(status, GlyphCatalog.WARNING);
            TrayAppDotNETToolTip.SetTip(status, error);
        }
        else if (binding.IsBound) TrayAppDotNETToolTip.SetTip(status, Loc("Settings_Hotkeys_Status_Registered"));

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
            Child = grid
        };
    }

    private void RebuildFanSlots(FanPropertiesPageGeneration pageGeneration)
    {
        pageGeneration.FanSlots.Clear();
        foreach (Fan fan in GetLiveFans())
        {
            pageGeneration.FanSlots.Add(new FanSettingsSlotEntry(
                KeyForFan(fan),
                fan.DisplayName,
                fan.ControllerDisplayLabel));
        }

        RenderFanSlots(pageGeneration);
    }

    private void RenderFanSlots(FanPropertiesPageGeneration pageGeneration)
    {
        pageGeneration.FanSlotPanel.Children.Clear();
        if (pageGeneration.FanSlots.Count == 0)
        {
            pageGeneration.FanSlotPanel.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
                L("Settings_FanProperties_NoFans", "No live fans detected."), Palette));
            return;
        }

        for (int index = 0; index < pageGeneration.FanSlots.Count; index++)
        {
            pageGeneration.FanSlotPanel.Children.Add(
                BuildFanSlotRow(pageGeneration, pageGeneration.FanSlots[index], index, Palette));
        }
    }

    private Border BuildFanSlotRow(
        FanPropertiesPageGeneration pageGeneration,
        FanSettingsSlotEntry slot,
        int index,
        SettingsPalette p)
    {
        TextBlock handle = TrayAppDotNETSettingsUI.Text(GlyphCatalog.DRAG_HANDLE.Text, p, 16);
        GlyphApplicator.ApplyTo(handle, GlyphCatalog.DRAG_HANDLE);
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
            Cursor = TrayAppDotNETCursors.Hand
        };

        bool pointerOver = false;
        bool pointerPressed = false;
        UpdateFanSlotRowVisual(pageGeneration, row, slot, p, pointerOver, pointerPressed);

        row.PointerEntered += (_, _) =>
        {
            pointerOver = true;
            UpdateFanSlotRowVisual(pageGeneration, row, slot, p, pointerOver, pointerPressed);
        };
        row.PointerExited += (_, _) =>
        {
            pointerOver = false;
            pointerPressed = false;
            UpdateFanSlotRowVisual(pageGeneration, row, slot, p, pointerOver, pointerPressed);
        };
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            if (pageGeneration.CapturedPointer != null)
            {
                e.Handled = true;
                return;
            }

            pageGeneration.DraggedSlot = slot;
            pageGeneration.DraggedSlotRow = row;
            pageGeneration.DragStart = e.GetPosition(pageGeneration.FanSlotPanel);
            pageGeneration.DraggedSlotPointerOffsetY = e.GetPosition(row).Y;
            pageGeneration.DraggedSlotHeight = Math.Max(1, row.Bounds.Height);
            pageGeneration.DraggedSlotTargetIndex = pageGeneration.FanSlots.IndexOf(slot);
            pointerPressed = true;
            UpdateFanSlotRowVisual(pageGeneration, row, slot, p, pointerOver, pointerPressed);
            CaptureFanSlotPointer(
                pageGeneration,
                e.Pointer,
                row,
                () =>
                {
                    pointerPressed = false;
                    ResetFanSlotGesture(pageGeneration, e.Pointer);
                    UpdateFanSlotRowVisual(pageGeneration, row, slot, p, pointerOver, pointerPressed);
                });
            e.Handled = true;
        };
        row.PointerMoved += (_, e) =>
        {
            if (!ReferenceEquals(pageGeneration.CapturedPointer, e.Pointer)) return;
            if (pageGeneration.DraggedSlot == null) return;
            Point current = e.GetPosition(pageGeneration.FanSlotPanel);
            if (Math.Abs(current.Y - pageGeneration.DragStart.Y) < 4) return;
            double draggedMidpoint = current.Y - pageGeneration.DraggedSlotPointerOffsetY
                + pageGeneration.DraggedSlotHeight / 2.0;
            pageGeneration.DraggedSlotTargetIndex =
                FanSlotInsertionIndexFromMidpoint(pageGeneration, draggedMidpoint);
            ApplyFanSlotDragPreview(pageGeneration);
            row.RenderTransform = new TranslateTransform(0, current.Y - pageGeneration.DragStart.Y);
            e.Handled = true;
        };
        row.PointerReleased += (_, e) =>
        {
            if (!ReferenceEquals(pageGeneration.CapturedPointer, e.Pointer)) return;
            pointerPressed = false;
            EndFanSlotDrag(pageGeneration, e.Pointer);
        };
        row.PointerCaptureLost += (_, e) =>
        {
            if (!ReferenceEquals(pageGeneration.CapturedPointer, e.Pointer)) return;
            pointerPressed = false;
            if (pageGeneration is { IsRetiring: false, IsResettingGesture: false })
                EndFanSlotDrag(pageGeneration, e.Pointer);
        };
        row.KeyDown += (_, e) =>
        {
            if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
            if (e.Key is not (Key.Up or Key.Down)) return;
            int currentIndex = pageGeneration.FanSlots.IndexOf(slot);
            int nextIndex = e.Key == Key.Up ? currentIndex - 1 : currentIndex + 1;
            if (currentIndex >= 0 && nextIndex >= 0 && nextIndex < pageGeneration.FanSlots.Count)
            {
                pageGeneration.FanSlots.RemoveAt(currentIndex);
                pageGeneration.FanSlots.Insert(nextIndex, slot);
                RenderFanSlots(pageGeneration);
            }

            e.Handled = true;
        };

        TrayAppDotNETToolTip.SetTip(row, "Drag to reorder, or press Ctrl+Up/Ctrl+Down.");
        _ = index;
        return row;
    }

    private static void UpdateFanSlotRowVisual(
        FanPropertiesPageGeneration pageGeneration,
        Border row,
        FanSettingsSlotEntry slot,
        SettingsPalette p,
        bool pointerOver,
        bool pointerPressed)
    {
        bool dragging = ReferenceEquals(slot, pageGeneration.DraggedSlot);
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

    private static int FanSlotInsertionIndexFromMidpoint(
        FanPropertiesPageGeneration pageGeneration,
        double draggedMidpointY)
    {
        int insertion = 0;
        for (int index = 0; index < pageGeneration.FanSlotPanel.Children.Count; index++)
        {
            Control child = pageGeneration.FanSlotPanel.Children[index];
            if (ReferenceEquals(child, pageGeneration.DraggedSlotRow)) continue;
            Point? topLeft = child.TranslatePoint(new Point(0, 0), pageGeneration.FanSlotPanel);
            if (topLeft == null) continue;
            if (draggedMidpointY > topLeft.Value.Y + child.Bounds.Height / 2.0) insertion++;
            else break;
        }

        int max = pageGeneration.FanSlots.Count - (pageGeneration.DraggedSlot != null ? 1 : 0);
        return Math.Clamp(insertion, 0, Math.Max(0, max));
    }

    private static void ApplyFanSlotDragPreview(FanPropertiesPageGeneration pageGeneration)
    {
        if (pageGeneration.DraggedSlot == null || pageGeneration.DraggedSlotRow == null) return;
        ResetFanSlotDragPreview(pageGeneration);

        int sourceIndex = pageGeneration.FanSlots.IndexOf(pageGeneration.DraggedSlot);
        if (sourceIndex < 0) return;

        int targetIndex = Math.Clamp(
            pageGeneration.DraggedSlotTargetIndex,
            0,
            Math.Max(0, pageGeneration.FanSlots.Count - 1));
        double offset = Math.Max(1, pageGeneration.DraggedSlotHeight
            + Math.Max(0, pageGeneration.DraggedSlotRow.Margin.Bottom));
        if (targetIndex < sourceIndex)
        {
            for (int i = targetIndex; i < sourceIndex; i++)
                SetFanSlotPreviewOffset(pageGeneration, i, offset);
        }
        else if (targetIndex > sourceIndex)
        {
            for (int index = sourceIndex + 1;
                 index <= targetIndex && index < pageGeneration.FanSlotPanel.Children.Count;
                 index++)
            {
                SetFanSlotPreviewOffset(pageGeneration, index, -offset);
            }
        }
    }

    private static void SetFanSlotPreviewOffset(
        FanPropertiesPageGeneration pageGeneration,
        int index,
        double offset)
    {
        if (index < 0 || index >= pageGeneration.FanSlotPanel.Children.Count) return;
        if (ReferenceEquals(pageGeneration.FanSlotPanel.Children[index], pageGeneration.DraggedSlotRow)) return;
        pageGeneration.FanSlotPanel.Children[index].RenderTransform = new TranslateTransform(0, offset);
    }

    private static void ResetFanSlotDragPreview(FanPropertiesPageGeneration pageGeneration)
    {
        foreach (Control child in pageGeneration.FanSlotPanel.Children)
        {
            if (ReferenceEquals(child, pageGeneration.DraggedSlotRow)) continue;
            child.RenderTransform = null;
        }
    }

    private void EndFanSlotDrag(FanPropertiesPageGeneration pageGeneration, IPointer? pointer)
    {
        FanSettingsSlotEntry? dragged = pageGeneration.DraggedSlot;
        int targetIndex = pageGeneration.DraggedSlotTargetIndex;
        bool hadDrag = dragged != null;
        ResetFanSlotGesture(pageGeneration, pointer);
        if (dragged != null && targetIndex >= 0)
        {
            int currentIndex = pageGeneration.FanSlots.IndexOf(dragged);
            if (currentIndex >= 0 && targetIndex != currentIndex)
            {
                pageGeneration.FanSlots.RemoveAt(currentIndex);
                pageGeneration.FanSlots.Insert(
                    Math.Clamp(targetIndex, 0, pageGeneration.FanSlots.Count), dragged);
            }
        }

        if (hadDrag) RenderFanSlots(pageGeneration);
    }

    private static void ResetFanSlotGesture(
        FanPropertiesPageGeneration pageGeneration,
        IPointer? fallbackPointer)
    {
        bool wasResetting = pageGeneration.IsResettingGesture;
        pageGeneration.IsResettingGesture = true;
        IPointer? capturedPointer = pageGeneration.CapturedPointer ?? fallbackPointer;
        pageGeneration.CapturedPointer = null;
        Border? draggedRow = pageGeneration.DraggedSlotRow;
        pageGeneration.DraggedSlotRow = null;
        pageGeneration.DraggedSlot = null;
        pageGeneration.DragStart = default;
        pageGeneration.DraggedSlotTargetIndex = -1;
        pageGeneration.DraggedSlotPointerOffsetY = 0;
        pageGeneration.DraggedSlotHeight = 0;
        try
        {
            draggedRow?.RenderTransform = null;
            foreach (Control child in pageGeneration.FanSlotPanel.Children)
                child.RenderTransform = null;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"FanSettingsWindow slot visual reset failed: {exception.Message}");
        }
        finally
        {
            ReleaseFanSlotPointer(capturedPointer, "slot drag");
            pageGeneration.IsResettingGesture = wasResetting;
        }
    }

    private static void CaptureFanSlotPointer(
        FanPropertiesPageGeneration pageGeneration,
        IPointer pointer,
        Control target,
        Action rollbackGesture)
    {
        if (pageGeneration.CapturedPointer != null)
            throw new InvalidOperationException("The Fan Properties page already owns a pointer capture.");

        pageGeneration.CapturedPointer = pointer;
        try
        {
            pointer.Capture(target);
        }
        catch
        {
            if (ReferenceEquals(pageGeneration.CapturedPointer, pointer))
                pageGeneration.CapturedPointer = null;
            bool wasResetting = pageGeneration.IsResettingGesture;
            pageGeneration.IsResettingGesture = true;
            try
            {
                rollbackGesture();
            }
            finally
            {
                pageGeneration.IsResettingGesture = wasResetting;
            }

            throw;
        }
    }

    private static void ReleaseFanSlotPointer(IPointer? pointer, string gestureName)
    {
        if (pointer == null) return;

        try
        {
            pointer.Capture(null);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"FanSettingsWindow {gestureName} pointer release failed: {exception.Message}");
        }
    }

    private void RetireFanPropertiesPage(FanPropertiesPageGeneration pageGeneration)
    {
        pageGeneration.IsRetiring = true;
        try
        {
            ResetFanSlotGesture(pageGeneration, null);
        }
        finally
        {
            pageGeneration.FanSlotPanel.Children.Clear();
            pageGeneration.FanSlots.Clear();
            _fanPropertiesPageGenerations.Remove(pageGeneration);
        }
    }

    private void ApplyFanSlotSwaps(FanPropertiesPageGeneration pageGeneration)
    {
        List<Fan> fans = GetLiveFans();
        if (fans.Count < 2 || fans.Count != pageGeneration.FanSlots.Count) return;

        Dictionary<string, FanUserSettings> snapshots = fans
            .ToDictionary(KeyForFan, f => f.SnapshotUserSettings(), StringComparer.OrdinalIgnoreCase);
        if (pageGeneration.FanSlots.Any(slot => !snapshots.ContainsKey(slot.Key))) return;

        for (int index = 0; index < fans.Count; index++)
            fans[index].ApplyUserSettings(snapshots[pageGeneration.FanSlots[index].Key]);

        AppServices.LHMService?.PersistLiveState(save: false);
        Save();
        RebuildFanSlots(pageGeneration);
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
        (TrayClickAction.OpenSettings, "Open settings")
    ];

    /// <summary>
    /// Creates flyout multiple slider value display options.
    /// </summary>
    private static IReadOnlyList<(MultipleSliderValuesDisplayMode Value, string Text)> MultipleSliderValuesOptions() =>
    [
        (MultipleSliderValuesDisplayMode.Disabled, "Disabled"),
        (MultipleSliderValuesDisplayMode.Enabled, "Enabled"),
        (MultipleSliderValuesDisplayMode.OnlyInManual, "Only in manual")
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
            CurrentBuildNumber = BuildInfo.BuildNumber
        });

    private static string FormatHotkey(FanHotkeyBinding binding)
    {
        string modifiers = TrayAppDotNETHotkeyKeys.ModifierText(binding.Modifiers);
        string key = TrayAppDotNETHotkeyKeys.KeyName(binding.VirtualKey);
        return string.IsNullOrEmpty(modifiers) ? key : modifiers + " + " + key;
    }

    private static IReadOnlyList<TrayAppDotNETHotkeyModifierOption> HotkeyModifierOptions =>
        TrayAppDotNETHotkeyModifierOptions.Create(Loc);

    /// <summary>Owns retained controls and gesture state for one Fan Properties page candidate.</summary>
    private sealed class FanPropertiesPageGeneration
    {
        public List<FanSettingsSlotEntry> FanSlots { get; } = [];
        public StackPanel FanSlotPanel { get; } = new();
        public Border? DraggedSlotRow { get; set; }
        public FanSettingsSlotEntry? DraggedSlot { get; set; }
        public IPointer? CapturedPointer { get; set; }
        public Point DragStart { get; set; }
        public double DraggedSlotPointerOffsetY { get; set; }
        public double DraggedSlotHeight { get; set; }
        public int DraggedSlotTargetIndex { get; set; } = -1;
        public bool IsResettingGesture { get; set; }
        public bool IsRetiring { get; set; }
    }

    private sealed record FanSettingsSlotEntry(string Key, string DisplayName, string Detail);
}
