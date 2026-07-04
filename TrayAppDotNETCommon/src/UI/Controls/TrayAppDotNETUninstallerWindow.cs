using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TrayAppDotNETCommon.UI.Controls;

internal static class TrayAppDotNETDialogChromeLayout
{
    private static readonly ControlAXAMLResources Resources = new(
        "avares://TrayAppDotNETCommon/UI/Controls/DialogChrome.axaml",
        "DialogChrome");

    public static double UninstallerWindowWidth => Resources.Double("UninstallerWindowWidth");
    public static double UninstallerWindowHeight => Resources.Double("UninstallerWindowHeight");
    public static double UninstallerWindowMinWidth => Resources.Double("UninstallerWindowMinWidth");
    public static double UninstallerWindowMinHeight => Resources.Double("UninstallerWindowMinHeight");
    public static double TitleBarHeight => Resources.Double("TitleBarHeight");
    public static Thickness RootBorderThickness => Resources.Thickness("RootBorderThickness");
    public static CornerRadius RootCornerRadius => Resources.CornerRadius("RootCornerRadius");
    public static CornerRadius CardCornerRadius => Resources.CornerRadius("CardCornerRadius");
    public static CornerRadius ZeroCornerRadius => Resources.CornerRadius("ZeroCornerRadius");
    public static Thickness TitleMargin => Resources.Thickness("TitleMargin");
    public static Thickness BodyMargin => Resources.Thickness("BodyMargin");
    public static Thickness DescriptionMargin => Resources.Thickness("DescriptionMargin");
    public static Thickness OptionRadioMargin => Resources.Thickness("OptionRadioMargin");
    public static Thickness OptionCardPadding => Resources.Thickness("OptionCardPadding");
    public static Thickness OptionCardMargin => Resources.Thickness("OptionCardMargin");
    public static Thickness ButtonPadding => Resources.Thickness("ButtonPadding");
    public static Thickness CancelButtonMargin => Resources.Thickness("CancelButtonMargin");
    public static Thickness ButtonsMargin => Resources.Thickness("ButtonsMargin");
}

public sealed record TrayAppDotNETUninstallerWindowOptions
{
    public required string ApplicationName { get; init; }
    public required string InstallDirectory { get; init; }
    public required string SettingsDirectory { get; init; }
    public required InstallScope InstallScope { get; init; }
    public required WindowIcon? Icon { get; init; }
    public required SettingsPalette Palette { get; init; }
    public required bool EnableRoundedCorners { get; init; }
    public required Func<string, string, string> Localize { get; init; }
    public required Action<InstallScope> RetargetStartupShortcut { get; init; }
    public required Func<InstallScope, bool, Process?> RunUninstall { get; init; }
}

/// <summary>
/// Shared custom-chrome uninstaller confirmation window.
/// </summary>
public class TrayAppDotNETUninstallerWindow : Window
{
    private const string SettingsChoiceGroupName = "SettingsChoice";

    private readonly TrayAppDotNETUninstallerWindowOptions _options;
    private readonly RadioButton _keepSettings;
    private readonly RadioButton _deleteSettings;

    public TrayAppDotNETUninstallerWindow(TrayAppDotNETUninstallerWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;

        Title = Localize("Uninstaller_Title", $"Uninstall {_options.ApplicationName}");
        Width = TrayAppDotNETDialogChromeLayout.UninstallerWindowWidth;
        Height = TrayAppDotNETDialogChromeLayout.UninstallerWindowHeight;
        MinWidth = TrayAppDotNETDialogChromeLayout.UninstallerWindowMinWidth;
        MinHeight = TrayAppDotNETDialogChromeLayout.UninstallerWindowMinHeight;
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = TrayAppDotNETSettingsUI.UIFont;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Icon = _options.Icon;

        _keepSettings = CreateChoiceRadio(isChecked: true);
        _deleteSettings = CreateChoiceRadio(isChecked: false);

        Content = BuildRoot();

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;

            Close();
            e.Handled = true;
        };
    }

    public Process? UninstallProcess { get; private set; }

    public bool ConfirmedUninstall { get; private set; }

    private static RadioButton CreateChoiceRadio(bool isChecked) =>
        new()
        {
            IsChecked = isChecked,
            GroupName = SettingsChoiceGroupName,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = TrayAppDotNETDialogChromeLayout.OptionRadioMargin,
        };

    private Border BuildRoot()
    {
        Grid chrome = new();
        chrome.RowDefinitions.Add(new RowDefinition(new GridLength(TrayAppDotNETDialogChromeLayout.TitleBarHeight)));
        chrome.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        chrome.Children.Add(BuildTitleBar());

        Grid body = BuildBody();
        Grid.SetRow(body, 1);
        chrome.Children.Add(body);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(_options.Palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_options.Palette.Border),
            BorderThickness = TrayAppDotNETDialogChromeLayout.RootBorderThickness,
            CornerRadius = Rounded(TrayAppDotNETDialogChromeLayout.RootCornerRadius),
            Child = chrome,
        };
    }

    private Grid BuildTitleBar()
    {
        Grid titleBar = new()
        {
            Background = Brushes.Transparent,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        titleBar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed) return;

            BeginMoveDrag(e);
            e.Handled = true;
        };

        TextBlock title = TrayAppDotNETSettingsUI.Text(
            Localize("Uninstaller_Title", $"Uninstall {_options.ApplicationName}"),
            _options.Palette,
            13);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = TrayAppDotNETDialogChromeLayout.TitleMargin;
        titleBar.Children.Add(title);

        TrayAppDotNETCaptionCloseButton close = new(_options.Palette);
        TrayAppDotNETToolTip.SetTip(close, Localize("Uninstaller_Caption_Close", "Close"));
        TrayAppDotNETToolTip.SuppressWhileEngaged(close);
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1);
        titleBar.Children.Add(close);
        return titleBar;
    }

    private Grid BuildBody()
    {
        Grid body = new() { Margin = TrayAppDotNETDialogChromeLayout.BodyMargin };
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock header = TrayAppDotNETSettingsUI.SectionHeader(
            Localize("Uninstaller_SectionHeader", $"Uninstall {_options.ApplicationName}"),
            _options.Palette);
        body.Children.Add(header);

        TextBlock description = TrayAppDotNETSettingsUI.DescriptionText(
            UninstallDescription(),
            _options.Palette,
            TrayAppDotNETDialogChromeLayout.DescriptionMargin);
        Grid.SetRow(description, 1);
        body.Children.Add(description);

        StackPanel choices = new();
        choices.Children.Add(BuildOptionCard(
            _keepSettings,
            Localize("Uninstaller_KeepSettings_Title", "Keep my settings"),
            Localize("Uninstaller_KeepSettings_Description",
                "Leave settings.xml in place so a future install picks them up.")));
        choices.Children.Add(BuildOptionCard(
            _deleteSettings,
            Localize("Uninstaller_DeleteSettings_Title", "Delete my settings"),
            string.Format(
                CultureInfo.CurrentCulture,
                Localize("Uninstaller_DeleteSettings_Description_Format",
                    "Also remove \"{0}\" including settings.xml."),
                _options.SettingsDirectory)));
        Grid.SetRow(choices, 2);
        body.Children.Add(choices);

        StackPanel buttons = BuildButtons();
        Grid.SetRow(buttons, 3);
        body.Children.Add(buttons);
        return body;
    }

    private StackPanel BuildButtons()
    {
        SettingsButton uninstall = TrayAppDotNETSettingsUI.Button(
            Localize("Uninstaller_UninstallButton", "Uninstall"),
            _options.Palette);
        uninstall.Padding = TrayAppDotNETDialogChromeLayout.ButtonPadding;

        SettingsButton cancel = TrayAppDotNETSettingsUI.Button(
            Localize("Uninstaller_Cancel", "Cancel"),
            _options.Palette);
        cancel.Padding = TrayAppDotNETDialogChromeLayout.ButtonPadding;
        cancel.Margin = TrayAppDotNETDialogChromeLayout.CancelButtonMargin;

        uninstall.Click += (_, _) =>
        {
            bool deleteSettings = _deleteSettings.IsChecked == true;
            uninstall.IsEnabled = false;
            cancel.IsEnabled = false;
            uninstall.Text = Localize("Uninstaller_UninstallingButton", "Uninstalling...");

            _options.RetargetStartupShortcut(_options.InstallScope);
            ConfirmedUninstall = true;
            UninstallProcess = _options.RunUninstall(_options.InstallScope, deleteSettings);
            Close();
        };
        cancel.Click += (_, _) => Close();

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = TrayAppDotNETDialogChromeLayout.ButtonsMargin,
            Children = { cancel, uninstall },
        };
    }

    private Border BuildOptionCard(RadioButton radio, string title, string description)
    {
        StackPanel text = new()
        {
            Children =
            {
                TrayAppDotNETSettingsUI.TitleText(title, _options.Palette),
                TrayAppDotNETSettingsUI.DescriptionText(description, _options.Palette),
            },
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.Children.Add(radio);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        Border card = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(_options.Palette.CardBackground),
            CornerRadius = Rounded(TrayAppDotNETDialogChromeLayout.CardCornerRadius),
            Padding = TrayAppDotNETDialogChromeLayout.OptionCardPadding,
            Margin = TrayAppDotNETDialogChromeLayout.OptionCardMargin,
            Child = grid,
        };
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;

            radio.IsChecked = true;
            e.Handled = true;
        };
        return card;
    }

    private string UninstallDescription()
    {
        string fallback = $"This will remove {_options.ApplicationName} installed at \"{{0}}\" and its entry in Windows Settings > Apps. Choose what to do with your settings.";
        string format = Localize("Uninstaller_Description_Format", fallback);
        return string.Format(CultureInfo.CurrentCulture, format, _options.InstallDirectory);
    }

    private CornerRadius Rounded(CornerRadius radius) =>
        _options.EnableRoundedCorners ? radius : TrayAppDotNETDialogChromeLayout.ZeroCornerRadius;

    private string Localize(string key, string fallback) => _options.Localize(key, fallback);
}
