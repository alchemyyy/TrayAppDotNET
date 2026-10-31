using Avalonia;
using Avalonia.Controls;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.Services.Install;
using TrayAppDotNETCommon.UI.ContextMenus;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Settings;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI;

/// <summary>What the shared About page shows about an app.</summary>
public sealed class TrayAppDotNETAboutPageAppInfo
{
    public required string ApplicationName { get; init; }
    public required string Tagline { get; init; }
    public required int BuildNumber { get; init; }
    public required string CommitHash { get; init; }
    public required string SettingsFolderPath { get; init; }
    public ITrayAppDotNETUpdateSettings? UpdateSettings { get; init; }
    public Func<UpdateCheckService?> UpdateService { get; init; } = static () => null;
    public bool SupportsFlyoutUpdateButton { get; init; } = true;

    /// <inheritdoc cref="TrayAppDotNETAboutPageOptions.UpdatePromptNode"/>
    public ControlMapNodeID? UpdatePromptNode { get; init; }

    public IReadOnlyList<TrayAppDotNETKnownIssue> KnownIssues { get; init; } = [];
}

/// <summary>
/// Builders for the pages and sections every app shares: General (language, startup, installation, rendering),
/// Appearance, Hotkeys and About. They read <see cref="SettingsWindowCommon{TPageKey}.CommonSettings"/> and
/// <see cref="SettingsWindowCommon{TPageKey}.Theme"/>, so a window only supplies what is specific to its app.
/// </summary>
public abstract partial class SettingsWindowCommon<TPageKey>
{
    private AppSettingsCommon RequiredCommonSettings =>
        CommonSettings ?? throw new InvalidOperationException(
            $"{GetType().Name} must supply CommonSettings to build the shared settings sections.");

    /// <summary>The About page, owned by the page being built so its update refresh stops with it.</summary>
    protected StackPanel BuildCommonAboutPage(TrayAppDotNETAboutPageAppInfo app)
    {
        ArgumentNullException.ThrowIfNull(app);
        TrayAppDotNETAboutPage page = OwnPageResource(new TrayAppDotNETAboutPage(new TrayAppDotNETAboutPageOptions
        {
            Palette = Palette,
            ButtonRadius = RadiusMedium,
            CardRadius = RadiusLarge,
            UpdatePromptOwnerBackdrop = ConfirmOverlayBackdrop,
            L = L,
            Save = Save,
            ApplicationName = app.ApplicationName,
            Tagline = app.Tagline,
            BuildNumber = app.BuildNumber,
            CommitHash = app.CommitHash,
            SettingsFolderPath = app.SettingsFolderPath,
            UpdateSettings = app.UpdateSettings,
            UpdateService = app.UpdateService,
            ConfirmAsync = ConfirmAsync,
            PromptOwner = () => this,
            SupportsFlyoutUpdateButton = app.SupportsFlyoutUpdateButton,
            RebuildAboutPage = () => RebuildShell(CurrentPageKey),
            UpdatePromptNode = app.UpdatePromptNode,
            KnownIssues = app.KnownIssues
        }));
        return page.Build();
    }

    /// <summary>
    /// The General section (language, startup, installation) bound to <see cref="CommonSettings"/> and the app's
    /// install services.
    /// </summary>
    protected TrayAppDotNETGeneralSettingsSection CreateGeneralSettingsSection(
        TrayAppDotNETAppInstallServices install,
        IReadOnlyList<string> availableLanguages,
        Func<bool>? getStartMinimized = null,
        Action<bool>? setStartMinimized = null)
    {
        ArgumentNullException.ThrowIfNull(install);
        AppSettingsCommon settings = RequiredCommonSettings;
        return new TrayAppDotNETGeneralSettingsSection(new TrayAppDotNETGeneralSettingsSectionOptions
        {
            Palette = Palette,
            ButtonRadius = RadiusMedium,
            CardRadius = RadiusLarge,
            L = L,
            Save = Save,
            ConfirmAsync = ConfirmAsync,
            ShowMessage = ShowMessage,
            GetRunOnStartup = () => install.Startup.GetRunOnStartup(),
            SetRunOnStartup = enabled =>
            {
                install.Startup.SetRunOnStartup(enabled);
                settings.RunOnStartup = enabled;
            },
            GetLanguage = () => settings.Language,
            SetLanguage = language => settings.Language = language,
            AvailableLanguages = availableLanguages,
            GetStartMinimized = getStartMinimized,
            SetStartMinimized = setStartMinimized,
            GetCurrentStartupShortcutTarget = () => install.Startup.GetCurrentShortcutTarget(),
            RetargetStartupShortcut = () => install.Startup.RetargetShortcutIfPresent(),
            DetectInstallations = () => install.Installation.DetectAll(),
            CurrentBuildNumber = install.BuildNumber
        });
    }

    /// <summary>
    /// The installation cards every app shows: per-user and system-wide copies, each with Install/Update and
    /// Uninstall, and optionally the Windows Store card. <paramref name="showUninstaller"/> opens the app's uninstaller
    /// for a scope and completes with whether the uninstall started.
    /// </summary>
    protected void AddStandardInstallationSection(
        StackPanel stack,
        TrayAppDotNETGeneralSettingsSection section,
        TrayAppDotNETAppInstallServices install,
        Func<InstallScope, IProgress<TrayAppDotNETInstallProgress>?, Task<bool>> showUninstaller,
        bool includeStoreCard)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(install);
        ArgumentNullException.ThrowIfNull(showUninstaller);
        section.AddInstallationSection(
            stack,
            [
                new TrayAppDotNETInstallCardOptions
                {
                    Scope = InstallScope.LocalAppData,
                    Title = L(nameof(CommonStrings.Settings_General_LocalUser_Title)),
                    ExecutablePath = install.Layout.LocalAppDataInstallExecutable,
                    Elevated = false,
                    Install = progress => install.Installation.InstallToLocalAppData(progress: progress),
                    ShowUninstallerAsync = progress => showUninstaller(InstallScope.LocalAppData, progress)
                },
                new TrayAppDotNETInstallCardOptions
                {
                    Scope = InstallScope.ProgramFiles,
                    Title = L(nameof(CommonStrings.Settings_General_SystemWide_Title)),
                    ExecutablePath = install.Layout.ProgramFilesInstallExecutable,
                    Elevated = true,
                    Install = progress => install.Installation.InstallSystemWide(progress: progress),
                    ShowUninstallerAsync = progress => showUninstaller(InstallScope.ProgramFiles, progress)
                }
            ],
            includeStoreCard
                ? new TrayAppDotNETStoreInstallOptions(
                    L(nameof(CommonStrings.Settings_General_WindowsStore_Title)),
                    () => StoreInstallDescription(install))
                : null);
    }

    private static string StoreInstallDescription(TrayAppDotNETAppInstallServices install)
    {
        TrayAppDotNETInstallationInfo? info = install.Installation.DetectAll()
            .FirstOrDefault(i => i.Scope == InstallScope.WindowsStore);
        return info?.Status == TrayAppDotNETInstallStatus.CurrentlyRunning
            ? L(nameof(CommonStrings.Settings_General_StoreRunning))
            : L(nameof(CommonStrings.Settings_General_StoreNotInstalled));
    }

    /// <summary>
    /// The rendering backend card plus the keep-warm and submenu delay cards the app supports. Only apps whose tray
    /// menus have submenus pass <paramref name="supportsSubmenus"/>.
    /// </summary>
    protected void AddRenderingSection(
        StackPanel stack,
        bool supportsFlyoutWarmWindow,
        bool supportsTrayContextMenuWarmWindow,
        bool supportsSubmenus = false)
    {
        AppSettingsCommon settings = RequiredCommonSettings;
        new TrayAppDotNETRenderingSettingsSection(new TrayAppDotNETRenderingSettingsSectionOptions
        {
            Palette = Palette,
            CardRadius = RadiusLarge,
            L = L,
            Save = Save,
            ConfirmAsync = ConfirmAsync,
            ShowMessage = ShowMessage,
            RenderingSettings = settings,
            WarmWindowSettings = settings,
            SubmenuSettings = supportsSubmenus ? settings : null,
            SupportsFlyoutWarmWindow = supportsFlyoutWarmWindow,
            SupportsTrayContextMenuWarmWindow = supportsTrayContextMenuWarmWindow
        }).AddCards(stack);
    }

    /// <summary>
    /// The Appearance cards every app shares, in one order: theme style, Windows 11 navigation, text and background
    /// colors, rounded corners, animations and tooltip delay. Every change that alters the look rebuilds the window.
    /// </summary>
    protected void AddAppearanceSection(StackPanel stack)
    {
        AppSettingsCommon settings = RequiredCommonSettings;
        AppTheme theme = SettingsTheme ?? AppTheme.Default;
        SettingsPalette p = Palette;
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_Theme_Appearance_Header)), p));
        stack.Children.Add(StringComboCard(
            L(nameof(CommonStrings.Settings_Theme_ThemeStyle_Title)),
            L(nameof(CommonStrings.Settings_Theme_ThemeStyle_Description)),
            [
                (TrayAppDotNETThemeMode.System, L(nameof(CommonStrings.Settings_Theme_ThemeStyle_System))),
                (TrayAppDotNETThemeMode.Light, L(nameof(CommonStrings.Settings_Theme_ThemeStyle_Light))),
                (TrayAppDotNETThemeMode.Dark, L(nameof(CommonStrings.Settings_Theme_ThemeStyle_Dark)))
            ],
            settings.ThemeMode,
            value => settings.ThemeMode = value,
            p,
            RebuildCurrentPage,
            searchKeywords: [L(nameof(CommonStrings.Settings_Theme_ThemeStyle_SearchKeywords))],
            node: ControlMap.AppearanceSection.ThemeMode));
        stack.Children.Add(BoolCard(
            L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_Title)),
            L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_Description)),
            settings.UseWindows11SettingsNavigation,
            value => settings.UseWindows11SettingsNavigation = value,
            p,
            RebuildCurrentPage,
            [L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_SearchKeywords))],
            ControlMap.AppearanceSection.Windows11Navigation));
        stack.Children.Add(VariantColorCard(
            L(nameof(CommonStrings.Settings_Theme_TextColor_Title)),
            L(nameof(CommonStrings.Settings_Theme_TextColor_Description)),
            L(nameof(CommonStrings.Settings_Theme_TextColor_LightTooltip)),
            L(nameof(CommonStrings.Settings_Theme_TextColor_DarkTooltip)),
            settings.TextColor,
            theme.Foreground.Light,
            theme.Foreground.Dark,
            p,
            [L(nameof(CommonStrings.Settings_Theme_TextColor_SearchKeywords))],
            ControlMap.AppearanceSection.TextColor.ID));
        stack.Children.Add(VariantColorCard(
            L(nameof(CommonStrings.Settings_Theme_BackgroundColor_Title)),
            L(nameof(CommonStrings.Settings_Theme_BackgroundColor_Description)),
            L(nameof(CommonStrings.Settings_Theme_BackgroundColor_LightTooltip)),
            L(nameof(CommonStrings.Settings_Theme_BackgroundColor_DarkTooltip)),
            settings.BackgroundColor,
            theme.Background.Light,
            theme.Background.Dark,
            p,
            [L(nameof(CommonStrings.Settings_Theme_BackgroundColor_SearchKeywords))],
            ControlMap.AppearanceSection.BackgroundColor.ID));
        stack.Children.Add(BoolCard(
            L(nameof(CommonStrings.Settings_Theme_RoundedCorners_Title)),
            L(nameof(CommonStrings.Settings_Theme_RoundedCorners_Description)),
            settings.EnableRoundedCorners,
            value => settings.EnableRoundedCorners = value,
            p,
            RebuildCurrentPage,
            [L(nameof(CommonStrings.Settings_Theme_RoundedCorners_SearchKeywords))],
            ControlMap.AppearanceSection.RoundedCorners));
        stack.Children.Add(StringComboCard(
            L(nameof(CommonStrings.Settings_Theme_Animations_Title)),
            L(nameof(CommonStrings.Settings_Theme_Animations_Description)),
            [
                (TrayAppDotNETAnimationMode.System, L(nameof(CommonStrings.Settings_Theme_Animations_System))),
                (TrayAppDotNETAnimationMode.Disabled, L(nameof(CommonStrings.Settings_Theme_Animations_Disabled))),
                (TrayAppDotNETAnimationMode.Enabled, L(nameof(CommonStrings.Settings_Theme_Animations_Enabled)))
            ],
            settings.AnimationMode,
            value => settings.AnimationMode = value,
            p,
            () =>
            {
                if (Application.Current != null)
                    TrayAppDotNETAnimationPolicy.Apply(Application.Current, settings.AnimationMode);
                RebuildCurrentPage();
            },
            searchKeywords: [L(nameof(CommonStrings.Settings_Theme_Animations_SearchKeywords))],
            node: ControlMap.AppearanceSection.AnimationMode));
        stack.Children.Add(IntCard(
            L(nameof(CommonStrings.Settings_Theme_ToolTipShowDelay_Title)),
            L(nameof(CommonStrings.Settings_Theme_ToolTipShowDelay_Description)),
            settings.ToolTipShowDelayMs,
            TimeConstants.ToolTipShowDelayMinMs,
            TimeConstants.ToolTipShowDelayMaxMs,
            value =>
            {
                settings.ToolTipShowDelayMs = value;
                TrayAppDotNETToolTip.ShowDelayMs = value;
                TrayAppDotNETToolTip.ApplyShowDelayToSubtree(this);
            },
            p,
            L(nameof(CommonStrings.Common_MillisecondsSuffix)),
            [L(nameof(CommonStrings.Settings_Theme_ToolTipShowDelay_SearchKeywords))],
            ControlMap.AppearanceSection.ToolTipShowDelay));
    }

    /// <summary>The tray context menu's font size card under a "Context menu" header.</summary>
    protected void AddContextMenuAppearanceSection(StackPanel stack)
    {
        AppSettingsCommon settings = RequiredCommonSettings;
        SettingsPalette p = Palette;
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(
            L(nameof(CommonStrings.Settings_General_ContextMenu_Header)), p));
        stack.Children.Add(IntCard(
            L(nameof(CommonStrings.Settings_Theme_FontSize_Title)),
            L(nameof(CommonStrings.Settings_Theme_FontSize_Description)),
            settings.ContextMenuFontSize,
            AppSettingsCommon.ContextMenuFontSizeMin,
            AppSettingsCommon.ContextMenuFontSizeMax,
            value => settings.ContextMenuFontSize = value,
            p,
            searchKeywords: [L(nameof(CommonStrings.Settings_Theme_FontSize_SearchKeywords))],
            node: ControlMap.ContextMenuAppearanceSection.ContextMenuFontSize));
    }

    /// <summary>
    /// The Hotkeys page: a description (an app may override the shared one with its own
    /// <c>Settings_Hotkeys_SectionDescription</c> resource), one card per row, then anything
    /// <paramref name="addAfterRows"/> adds.
    /// </summary>
    protected StackPanel BuildHotkeysPage<TAction, TBinding>(
        AppSettingsCommon<TBinding, TAction> settings,
        Func<GlobalHotkeyService<TAction, TBinding>?> service,
        IReadOnlyList<TrayAppDotNETHotkeyRow<TAction>> rows,
        Action<StackPanel, TrayAppDotNETHotkeysSection<TAction, TBinding>>? addAfterRows = null)
        where TAction : struct, Enum
        where TBinding : HotkeyBindingBase<TAction>, new()
    {
        SettingsPalette p = Palette;
        StackPanel stack = PageStack(L(nameof(CommonStrings.Settings_Hotkeys_SectionHeader)), p);
        stack.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            L(nameof(CommonStrings.Settings_Hotkeys_SectionDescription)),
            p,
            TrayAppDotNETHotkeysSectionResources.Current.AxamlHotkeysSection.DescriptionMargin));
        TrayAppDotNETHotkeysSection<TAction, TBinding> section = new(
            new TrayAppDotNETHotkeysSectionOptions<TAction, TBinding>
            {
                Palette = p,
                CardRadius = RadiusLarge,
                ButtonRadius = RadiusMedium,
                L = L,
                Save = Save,
                Settings = settings,
                Service = service,
                Rows = rows
            });
        section.AddTo(stack);
        addAfterRows?.Invoke(stack, section);
        return stack;
    }

    private void RebuildCurrentPage() => RebuildShell(CurrentPageKey);

    /// <summary>
    /// The page scroll bar's right-click menu looks like the app's tray menu: its font size, rounded corners and
    /// shadow. A window without app settings, or a derived window that sets its own options, keeps the defaults.
    /// </summary>
    private ContextMenuWindowOptions CreateScrollBarMenuOptions(SettingsPalette palette)
    {
        AppSettingsCommon? settings = CommonSettings;
        if (settings == null) return new ContextMenuWindowOptions { Palette = palette };

        return new ContextMenuWindowOptions
        {
            Palette = palette,
            Rounded = EnableRoundedCorners,
            FontSize = settings.ContextMenuFontSize,
            ShadowColor = (SettingsTheme ?? AppTheme.Default).MenuShadow.For(ResolveEffectiveIsLight())
        };
    }
}
