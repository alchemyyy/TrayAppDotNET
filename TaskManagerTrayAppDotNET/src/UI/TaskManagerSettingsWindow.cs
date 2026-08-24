using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Settings;
using TrayAppDotNETCommon.Visuals;

namespace TaskManagerTrayAppDotNET.UI;

public enum TaskManagerSettingsPage
{
    General,
    Theme,
    About
}

/// <summary>Classic TrayAppDotNET settings window for Task Manager.</summary>
public sealed class TaskManagerSettingsWindow : SettingsWindowCommon<TaskManagerSettingsPage>
{
    private const double GridSizeInputWidth = 88;
    private const int ToolTipDelayMinimumMilliseconds = 0;
    private const int ToolTipDelayMaximumMilliseconds = 10_000;

    private readonly AppSettings _settings;
    private readonly Action<string, InstallScope> _showUninstaller;

    public TaskManagerSettingsWindow(
        AppSettings settings,
        Action<string, InstallScope> showUninstaller)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(showUninstaller);

        _settings = settings;
        _showUninstaller = showUninstaller;
        ConfigureCompactSettingsWindow("Task Manager settings", icon: null);
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

    private StackPanel BuildThemePage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack("Appearance", palette);

        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader("Details grid", palette));
        stack.Children.Add(DoubleCard(
            "Font size",
            "Set the text size used by process rows.",
            _settings.GridFontSize,
            AppSettings.GridFontSizeMinimum,
            AppSettings.GridFontSizeMaximum,
            value => _settings.GridFontSize = value,
            palette,
            " DIP",
            ["grid text size", "zoom"]));
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
                (nameof(TrayAppDotNETThemeMode.System), "Use Windows setting"),
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
        stack.Children.Add(ComboCard(
            "Animations",
            "Choose whether interface animations follow Windows, remain disabled, or remain enabled.",
            [
                (nameof(TrayAppDotNETAnimationMode.System), "Use Windows setting"),
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

    private Border DoubleCard(
        string title,
        string description,
        double value,
        double minimum,
        double maximum,
        Action<double> set,
        SettingsPalette palette,
        string suffix,
        IReadOnlyList<string> searchKeywords)
    {
        double currentValue = Math.Clamp(value, minimum, maximum);
        TextBox input = TrayAppDotNETSettingsUI.TextBox(
            palette,
            GridSizeInputWidth,
            FormatGridSize(currentValue));
        input.TextAlignment = TextAlignment.Right;

        Action commit = () =>
        {
            if (!TryParseGridSize(input.Text, out double parsedValue))
            {
                input.Text = FormatGridSize(currentValue);
                return;
            }

            double nextValue = Math.Clamp(parsedValue, minimum, maximum);
            input.Text = FormatGridSize(nextValue);
            if (Math.Abs(nextValue - currentValue) < 0.001) return;

            currentValue = nextValue;
            set(nextValue);
            Save();
        };
        input.LostFocus += (_, _) => commit();
        input.KeyDown += (_, eventArgs) =>
        {
            switch (eventArgs.Key)
            {
                case Key.Enter:
                    commit();
                    eventArgs.Handled = true;
                    break;
                case Key.Escape:
                    input.Text = FormatGridSize(currentValue);
                    eventArgs.Handled = true;
                    break;
            }
        };

        TextBlock suffixText = TrayAppDotNETSettingsUI.Text(suffix, palette);
        return Card(
            title,
            description,
            TrayAppDotNETSettingsUI.Horizontal(input, suffixText),
            palette,
            searchKeywords);
    }

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

    private static bool TryParseGridSize(string? text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string FormatGridSize(double value) =>
        value.ToString("0.##", CultureInfo.CurrentCulture);
}
