using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Settings;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.UI;

public enum TaskManagerSettingsPage
{
    General,
    TrayIcon,
    Theme,
    About
}

/// <summary>Classic TrayAppDotNET settings window for Task Manager.</summary>
public sealed class TaskManagerSettingsWindow : SettingsWindowCommon<TaskManagerSettingsPage>
{
    private const int ToolTipDelayMinimumMilliseconds = 0;
    private const int ToolTipDelayMaximumMilliseconds = 10_000;

    private readonly AppSettings _settings;
    private readonly Action<string, InstallScope> _showUninstaller;
    private readonly TaskManagerWindowResources _taskManagerResources = TaskManagerWindowResources.Current;

    public TaskManagerSettingsWindow(
        AppSettings settings,
        Action<string, InstallScope> showUninstaller)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(showUninstaller);

        _settings = settings;
        _showUninstaller = showUninstaller;
        ConfigureCompactSettingsWindow("Task Manager settings", icon: null);
        Topmost = settings.AlwaysOnTop;
        InitializeSettingsShell();
    }

    internal new void SelectPage(TaskManagerSettingsPage page) => base.SelectPage(page);

    protected override bool EnableRoundedCorners => _settings.EnableRoundedCorners;

    protected override bool UseWindows11SettingsNavigation => _settings.UseWindows11SettingsNavigation;

    protected override TaskManagerSettingsPage DefaultPageKey => TaskManagerSettingsPage.General;

    protected override string HeaderText => "Task Manager";

    protected override string OpenSettingsFolderText => "Open Task Manager settings folder";

    protected override string SettingsFolderPath => AppSettings.GetDefaultDirectory();

    protected override Color ConfirmOverlayBackdrop =>
        (AppServices.Theme ?? AppTheme.Default).FlyoutOverlayBackdrop.For(ResolveEffectiveIsLight());

    protected override SettingsPalette ResolvePalette() =>
        VolumeSettingsPalette.Create(AppServices.Theme, _settings, ResolveEffectiveIsLight());

    protected override bool ResolveEffectiveIsLightForBindings() => ResolveEffectiveIsLight();

    protected override IReadOnlyList<SettingsPageDescriptor<TaskManagerSettingsPage>> CreatePageDescriptors() =>
    [
        new(
            TaskManagerSettingsPage.General,
            "General",
            () => NamePage(TaskManagerSettingsPage.General, BuildGeneralPage()),
            SettingsNavigationGlyphs.General),
        new(
            TaskManagerSettingsPage.TrayIcon,
            "Tray icon",
            () => NamePage(TaskManagerSettingsPage.TrayIcon, BuildTrayIconPage()),
            SettingsNavigationGlyphs.TrayIcon),
        new(
            TaskManagerSettingsPage.Theme,
            "Appearance",
            () => NamePage(TaskManagerSettingsPage.Theme, BuildThemePage()),
            SettingsNavigationGlyphs.Theme),
        new(
            TaskManagerSettingsPage.About,
            "About",
            () => NamePage(TaskManagerSettingsPage.About, BuildAboutPage()),
            SettingsNavigationGlyphs.About)
    ];

    protected override void Save() => _settings.Save();

    private StackPanel BuildGeneralPage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack("General", palette);

        TrayAppDotNETGeneralSettingsSection commonSection = CreateGeneralSettingsSection(palette);
        stack.Children.Add(commonSection.BuildStartupCard());
        stack.Children.Add(BoolCard(
            "Autosave settings",
            "Save changes to the Task Manager settings file as they are made.",
            _settings.Autosave,
            value => _settings.Autosave = value,
            palette,
            searchKeywords: ["save settings automatically"]));
        stack.Children.Add(BuildWindowManagementCard(palette));

        commonSection.AddInstallationSection(
            stack,
            [
                new TrayAppDotNETInstallCardOptions
                {
                    Scope = InstallScope.LocalAppData,
                    Title = "Install for current user",
                    ExecutablePath = AppServices.InstallLayout.LocalAppDataInstallExecutable,
                    Elevated = false,
                    Install = static () => AppServices.Installation.InstallToLocalAppData(),
                    UninstallAsync = _ =>
                    {
                        _showUninstaller(
                            AppServices.InstallLayout.LocalAppDataInstallDirectory,
                            InstallScope.LocalAppData);
                        return Task.CompletedTask;
                    }
                },
                new TrayAppDotNETInstallCardOptions
                {
                    Scope = InstallScope.ProgramFiles,
                    Title = "Install system-wide",
                    ExecutablePath = AppServices.InstallLayout.ProgramFilesInstallExecutable,
                    Elevated = true,
                    Install = static () => AppServices.Installation.InstallSystemWide(),
                    UninstallAsync = _ =>
                    {
                        _showUninstaller(
                            AppServices.InstallLayout.ProgramFilesInstallDirectory,
                            InstallScope.ProgramFiles);
                        return Task.CompletedTask;
                    }
                }
            ]);

        CreateRenderingSettingsSection(palette).AddCards(stack);
        return stack;
    }

    private StackPanel BuildTrayIconPage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack("Tray icon", palette);
        stack.Children.Add(ComboCard(
            "Style",
            "Show only the latest value or a recency-weighted sliding history.",
            [
                (nameof(TrayGraphStyle.Current), "Current"),
                (nameof(TrayGraphStyle.Marquee), "Marquee")
            ],
            _settings.TrayGraphStyle.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayGraphStyle value))
                    _settings.TrayGraphStyle = value;
            },
            palette,
            searchKeywords: ["graph current marquee history sliding"]));
        stack.Children.Add(ComboCard(
            "Data source",
            "Choose the system utilization measured by the tray graph.",
            [
                (nameof(TrayGraphDataSource.CPUAverage), "CPU Usage (Average)"),
                (nameof(TrayGraphDataSource.CPUHighestCore), "CPU Usage (Highest Core)"),
                (nameof(TrayGraphDataSource.Memory), "Memory (RAM)")
            ],
            _settings.TrayGraphDataSource.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayGraphDataSource value))
                    _settings.TrayGraphDataSource = value;
            },
            palette,
            searchKeywords: ["CPU processor core memory RAM metric"]));
        return stack;
    }

    private Border BuildWindowManagementCard(SettingsPalette palette)
    {
        StackPanel options = new()
        {
            Margin = _taskManagerResources.AxamlTaskManagerSettings.WindowManagementOptionsMargin,
            Spacing = _taskManagerResources.AxamlTaskManagerSettings.WindowManagementOptionSpacing
        };
        options.Children.Add(CreateWindowManagementCheckBox(
            "Always on top",
            _settings.AlwaysOnTop,
            value =>
            {
                _settings.AlwaysOnTop = value;
                Topmost = value;
            },
            palette));
        options.Children.Add(CreateWindowManagementCheckBox(
            "Close to Tray",
            _settings.CloseToTray,
            value => _settings.CloseToTray = value,
            palette));
        options.Children.Add(CreateWindowManagementCheckBox(
            "Minimize to Tray",
            _settings.MinimizeToTray,
            value => _settings.MinimizeToTray = value,
            palette));

        StackPanel content = new();
        content.Children.Add(TrayAppDotNETSettingsUI.TitleText("Window management", palette));
        content.Children.Add(options);
        return RawCard(
            content,
            palette,
            ["always on top", "close to tray", "minimize to tray"]);
    }

    private CheckBox CreateWindowManagementCheckBox(
        string text,
        bool isChecked,
        Action<bool> set,
        SettingsPalette palette)
    {
        CheckBox checkBox = new()
        {
            Content = TrayAppDotNETSettingsUI.Text(text, palette),
            IsChecked = isChecked,
            Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground)
        };
        checkBox.IsCheckedChanged += (_, _) =>
        {
            set(checkBox.IsChecked == true);
            Save();
        };
        return checkBox;
    }

    private StackPanel BuildThemePage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack("Appearance", palette);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader("Processes grid", palette));
        stack.Children.Add(DoubleCard(
            "Font size",
            "Set the text size used by process rows.",
            _settings.GridFontSize,
            AppSettings.GridFontSizeMinimum,
            AppSettings.GridFontSizeMaximum,
            value => _settings.GridFontSize = value,
            palette,
            " DIP",
            ["grid text size", "zoom"],
            decimalPlaces: 1,
            step: 0.5));
        stack.Children.Add(ComboCard(
            "Font weight",
            "Set the text weight used by process rows and column headers.",
            [
                (nameof(DetailsGridFontWeight.Thin), "Thin"),
                (nameof(DetailsGridFontWeight.ExtraLight), "Extra light"),
                (nameof(DetailsGridFontWeight.Light), "Light"),
                (nameof(DetailsGridFontWeight.SemiLight), "Semi-light"),
                (nameof(DetailsGridFontWeight.Normal), "Normal"),
                (nameof(DetailsGridFontWeight.Medium), "Medium"),
                (nameof(DetailsGridFontWeight.SemiBold), "Semi-bold"),
                (nameof(DetailsGridFontWeight.Bold), "Bold"),
                (nameof(DetailsGridFontWeight.ExtraBold), "Extra bold"),
                (nameof(DetailsGridFontWeight.Black), "Black")
            ],
            _settings.GridFontWeight.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out DetailsGridFontWeight value))
                    _settings.GridFontWeight = value;
            },
            palette,
            searchKeywords: ["grid text thickness", "bold"]));
        stack.Children.Add(IntCard(
            "Row height",
            "Set the height of each process row.",
            _settings.GridRowHeight,
            AppSettings.GridRowHeightMinimum,
            AppSettings.GridRowHeightMaximum,
            value => _settings.GridRowHeight = value,
            palette,
            " DIP",
            ["grid spacing", "zoom"]));
        stack.Children.Add(BoolCard(
            "Live column resizing",
            "Resize column contents while dragging instead of applying the new width on release.",
            _settings.EnableLiveDetailsColumnResizing,
            value => _settings.EnableLiveDetailsColumnResizing = value,
            palette,
            searchKeywords: ["column resize preview"]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader("Theme", palette));
        stack.Children.Add(ComboCard(
            "Theme mode",
            "Choose whether Task Manager follows Windows or uses a fixed light or dark theme.",
            [
                (nameof(TrayAppDotNETThemeMode.System), "System"),
                (nameof(TrayAppDotNETThemeMode.Light), "Light"),
                (nameof(TrayAppDotNETThemeMode.Dark), "Dark")
            ],
            _settings.ThemeMode.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayAppDotNETThemeMode value))
                    _settings.ThemeMode = value;
            },
            palette,
            afterSave: () => RebuildShell(TaskManagerSettingsPage.Theme),
            searchKeywords: ["light dark system"]));
        stack.Children.Add(BoolCard(
            L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_Title)),
            L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_Description)),
            _settings.UseWindows11SettingsNavigation,
            value => _settings.UseWindows11SettingsNavigation = value,
            palette,
            afterSave: () => RebuildShell(TaskManagerSettingsPage.Theme),
            searchKeywords: [L(nameof(CommonStrings.Settings_Theme_Windows11Navigation_SearchKeywords))]));

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader("Window", palette));
        stack.Children.Add(BoolCard(
            "Rounded corners",
            "Use rounded corners on Task Manager and its menus.",
            _settings.EnableRoundedCorners,
            value => _settings.EnableRoundedCorners = value,
            palette,
            afterSave: () => RebuildShell(TaskManagerSettingsPage.Theme),
            searchKeywords: ["square sharp corners"]));
        stack.Children.Add(BoolCard(
            "Collapse navigation when narrow",
            "Hide the left navigation menu when the Task Manager window is narrower than 750 pixels.",
            _settings.CollapseSidebarWhenNarrow,
            value => _settings.CollapseSidebarWhenNarrow = value,
            palette,
            searchKeywords: ["sidebar left menu responsive"]));
        stack.Children.Add(ComboCard(
            "Animations",
            "Choose whether interface animations follow Windows, remain disabled, or remain enabled.",
            [
                (nameof(TrayAppDotNETAnimationMode.System), "System"),
                (nameof(TrayAppDotNETAnimationMode.Disabled), "Disabled"),
                (nameof(TrayAppDotNETAnimationMode.Enabled), "Enabled")
            ],
            _settings.AnimationMode.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out TrayAppDotNETAnimationMode value))
                    _settings.AnimationMode = value;
            },
            palette,
            afterSave: ApplyAnimationMode,
            searchKeywords: ["motion transitions"]));

        stack.Children.Add(IntCard(
            "Tooltip delay",
            "Set how long the pointer must hover before a tooltip appears.",
            _settings.ToolTipShowDelayMs,
            ToolTipDelayMinimumMilliseconds,
            ToolTipDelayMaximumMilliseconds,
            value =>
            {
                _settings.ToolTipShowDelayMs = value;
                TrayAppDotNETToolTip.ShowDelayMs = value;
                TrayAppDotNETToolTip.ApplyShowDelayToSubtree(this);
            },
            palette,
            " ms",
            ["hover tooltip timing"]));

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
                UpdatePromptOwnerBackdrop = ConfirmOverlayBackdrop,
                L = L,
                Save = Save,
                ApplicationName = Constants.DisplayName,
                Tagline = "Fast process monitoring and management for TrayAppDotNET.",
                BuildNumber = BuildInfo.BuildNumber,
                CommitHash = BuildInfo.CommitHash,
                Publisher = Constants.Publisher,
                HelpLink = Constants.HelpLink,
                OpenSettingsFolderText = OpenSettingsFolderText,
                SettingsFolderPath = SettingsFolderPath,
                ConfirmAsync = ConfirmAsync,
                PromptOwner = () => this,
                Log = TADNLog.Log,
                SupportsFlyoutUpdateButton = false
            }));
        return aboutPage.Build();
    }

    private TrayAppDotNETGeneralSettingsSection CreateGeneralSettingsSection(SettingsPalette palette) =>
        new(new TrayAppDotNETGeneralSettingsSectionOptions
        {
            Palette = palette,
            ButtonRadius = RadiusMedium,
            CardRadius = RadiusLarge,
            L = L,
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

    private TrayAppDotNETRenderingSettingsSection CreateRenderingSettingsSection(SettingsPalette palette) =>
        new(new TrayAppDotNETRenderingSettingsSectionOptions
        {
            Palette = palette,
            CardRadius = RadiusLarge,
            L = L,
            Save = Save,
            ConfirmAsync = ConfirmAsync,
            ShowMessage = ShowMessage,
            RenderingSettings = _settings,
            TrayMenuSettings = _settings
        });

    private Control NamePage(TaskManagerSettingsPage page, Control control)
    {
        ControlNames.AssignLogicalSubtree(control, page.ToString());
        return control;
    }

    private bool ResolveEffectiveIsLight() => _settings.ThemeMode switch
    {
        TrayAppDotNETThemeMode.Light => true,
        TrayAppDotNETThemeMode.Dark => false,
        _ => AppServices.Theme?.IsLightTheme ?? AppTheme.Default.IsLightTheme
    };

    private void ApplyAnimationMode()
    {
        if (Application.Current != null)
            TrayAppDotNETAnimationPolicy.Apply(Application.Current, _settings.AnimationMode);
        RebuildShell(TaskManagerSettingsPage.Theme);
    }

}
