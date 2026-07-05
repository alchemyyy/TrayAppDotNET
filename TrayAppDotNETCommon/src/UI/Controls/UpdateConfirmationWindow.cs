using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Services;

namespace TrayAppDotNETCommon.UI.Controls;

internal static class UpdateConfirmationLayout
{
    private static readonly Lazy<UpdateConfirmationWindowResources> Resources = new(
        static () => new UpdateConfirmationWindowResources());

    private static UpdateConfirmationWindowResources AXAMLResources => Resources.Value;

    public static double WindowWidth => AXAMLResources.AxamlUpdateConfirmation.WindowWidth;
    public static double WindowMinWidth => AXAMLResources.AxamlUpdateConfirmation.WindowMinWidth;
    public static int MaxVisibleChangelogLines => AXAMLResources.AxamlUpdateConfirmation.MaxVisibleChangelogLines;
    public static double ChangelogLineHeight => AXAMLResources.AxamlUpdateConfirmation.ChangelogLineHeight;
    public static Thickness RootBorderThickness => AXAMLResources.AxamlUpdateConfirmation.RootBorderThickness;
    public static CornerRadius RootCornerRadius => AXAMLResources.AxamlUpdateConfirmation.RootCornerRadius;
    public static CornerRadius ZeroCornerRadius => AXAMLResources.AxamlUpdateConfirmation.ZeroCornerRadius;
    public static double TitleBarHeight => AXAMLResources.AxamlUpdateConfirmation.TitleBarHeight;
    public static Thickness BodyMargin => AXAMLResources.AxamlUpdateConfirmation.BodyMargin;
    public static Thickness DescriptionMargin => AXAMLResources.AxamlUpdateConfirmation.DescriptionMargin;
    public static Thickness ChangelogBorderThickness =>
        AXAMLResources.AxamlUpdateConfirmation.ChangelogBorderThickness;
    public static CornerRadius ChangelogCornerRadius => AXAMLResources.AxamlUpdateConfirmation.ChangelogCornerRadius;
    public static Thickness ChangelogPadding => AXAMLResources.AxamlUpdateConfirmation.ChangelogPadding;
    public static Thickness ChangelogScrollPadding => AXAMLResources.AxamlUpdateConfirmation.ChangelogScrollPadding;
    public static double ChangelogMaxHeightExtra => AXAMLResources.AxamlUpdateConfirmation.ChangelogMaxHeightExtra;
    public static Thickness ButtonPadding => AXAMLResources.AxamlUpdateConfirmation.ButtonPadding;
    public static Thickness CancelButtonMargin => AXAMLResources.AxamlUpdateConfirmation.CancelButtonMargin;
    public static Thickness ButtonsMargin => AXAMLResources.AxamlUpdateConfirmation.ButtonsMargin;
    public static Thickness TitleMargin => AXAMLResources.AxamlUpdateConfirmation.TitleMargin;
}

public sealed class TrayAppDotNETUpdateConfirmationWindow : Window
{
    public TrayAppDotNETUpdateConfirmationWindow(UpdateInfo info, SettingsPalette palette, bool rounded)
        : this(
            string.Format(CultureInfo.CurrentCulture, L("UpdateDialog_TitleFormat", "Update available: {0}"),
                info.ReleaseName),
            L("UpdateDialog_DefaultDescription", "A newer release is available."),
            string.IsNullOrWhiteSpace(info.Changelog)
                ? L("UpdateDialog_NoChangelog", "No changelog provided.")
                : info.Changelog,
            L("UpdateDialog_Install", "Install"),
            L("UpdateDialog_Cancel", "Cancel"),
            palette,
            rounded)
    {
    }

    public TrayAppDotNETUpdateConfirmationWindow(
        string title,
        string description,
        string? changelog,
        string confirmText,
        string? cancelText,
        SettingsPalette palette,
        bool rounded)
    {
        Title = title;
        Width = UpdateConfirmationLayout.WindowWidth;
        MinWidth = UpdateConfirmationLayout.WindowMinWidth;
        SizeToContent = SizeToContent.Height;
        WindowDecorations = WindowDecorations.None;
        Background = TrayAppDotNETSettingsUI.Brush(palette.Background);
        ShowInTaskbar = false;
        CanResize = false;
        FontFamily = TrayAppDotNETSettingsUI.UIFont;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        Content = new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            BorderThickness = UpdateConfirmationLayout.RootBorderThickness,
            CornerRadius = rounded
                ? UpdateConfirmationLayout.RootCornerRadius
                : UpdateConfirmationLayout.ZeroCornerRadius,
            Child = BuildContent(title, description, changelog, confirmText, cancelText, palette, rounded),
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(false);
                e.Handled = true;
            }
        };
    }

    private Grid BuildContent(
        string title,
        string description,
        string? changelog,
        string confirmText,
        string? cancelText,
        SettingsPalette palette,
        bool rounded)
    {
        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition(new GridLength(UpdateConfirmationLayout.TitleBarHeight)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        root.Children.Add(BuildTitleBar(title, palette));

        Grid body = new() { Margin = UpdateConfirmationLayout.BodyMargin, };
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(body, 1);

        TextBlock header = TrayAppDotNETSettingsUI.SectionHeader(title, palette);
        Grid.SetRow(header, 0);
        body.Children.Add(header);

        TextBlock descriptionText = TrayAppDotNETSettingsUI.DescriptionText(description, palette);
        descriptionText.Margin = UpdateConfirmationLayout.DescriptionMargin;
        Grid.SetRow(descriptionText, 1);
        body.Children.Add(descriptionText);

        if (!string.IsNullOrWhiteSpace(changelog))
        {
            TextBlock changelogText = TrayAppDotNETSettingsUI.Text(changelog, palette, 12);
            changelogText.FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI");
            changelogText.LineHeight = UpdateConfirmationLayout.ChangelogLineHeight;
            changelogText.TextWrapping = TextWrapping.Wrap;

            Border changelogBox = new()
            {
                Background = TrayAppDotNETSettingsUI.Brush(palette.ControlBackground),
                BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
                BorderThickness = UpdateConfirmationLayout.ChangelogBorderThickness,
                CornerRadius = rounded
                    ? UpdateConfirmationLayout.ChangelogCornerRadius
                    : UpdateConfirmationLayout.ZeroCornerRadius,
                Padding = UpdateConfirmationLayout.ChangelogPadding,
                Child = new ScrollViewer
                {
                    MaxHeight =
                        UpdateConfirmationLayout.MaxVisibleChangelogLines *
                        UpdateConfirmationLayout.ChangelogLineHeight +
                        UpdateConfirmationLayout.ChangelogMaxHeightExtra,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = changelogText,
                },
            };
            Grid.SetRow(changelogBox, 2);
            body.Children.Add(changelogBox);
        }

        SettingsButton install = TrayAppDotNETSettingsUI.Button(confirmText, palette);
        install.Padding = UpdateConfirmationLayout.ButtonPadding;
        install.Click += (_, _) => Close(true);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (!string.IsNullOrWhiteSpace(cancelText))
        {
            SettingsButton cancel = TrayAppDotNETSettingsUI.Button(cancelText, palette);
            cancel.Padding = UpdateConfirmationLayout.ButtonPadding;
            cancel.Margin = UpdateConfirmationLayout.CancelButtonMargin;
            cancel.Click += (_, _) => Close(false);
            buttons.Children.Add(cancel);
        }

        buttons.Children.Add(install);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = UpdateConfirmationLayout.ButtonsMargin;
        Grid.SetRow(buttons, 3);
        body.Children.Add(buttons);

        root.Children.Add(body);
        return root;
    }

    private Grid BuildTitleBar(string title, SettingsPalette palette)
    {
        Grid bar = new()
        {
            Background = Brushes.Transparent,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), },
        };

        TextBlock titleText = TrayAppDotNETSettingsUI.Text(title, palette, 13);
        titleText.VerticalAlignment = VerticalAlignment.Center;
        titleText.Margin = UpdateConfirmationLayout.TitleMargin;
        bar.Children.Add(titleText);

        TrayAppDotNETCaptionCloseButton close = new(palette);
        TrayAppDotNETToolTip.SetTip(close, L("UpdateDialog_CaptionClose_Tooltip", "Close"));
        TrayAppDotNETToolTip.SuppressWhileEngaged(close);
        close.Click += (_, _) => Close(false);
        Grid.SetColumn(close, 1);
        bar.Children.Add(close);

        bar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed) return;
            BeginMoveDrag(e);
            e.Handled = true;
        };

        return bar;
    }

    private static string L(string key, string fallback)
    {
        try
        {
            string value = LocalizationManager.Instance[key];
            return string.IsNullOrWhiteSpace(value) || value == key ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }
}
