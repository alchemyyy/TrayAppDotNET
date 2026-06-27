using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Services;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed class TrayAppDotNETUpdateConfirmationWindow : Window
{
    private const int MaxVisibleChangelogLines = 16;
    private const double ChangelogLineHeightPx = 16;

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
        string changelog,
        string confirmText,
        string cancelText,
        SettingsPalette palette,
        bool rounded)
    {
        Title = title;
        Width = 520;
        MinWidth = 420;
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
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(rounded ? 8 : 0),
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
        string changelog,
        string confirmText,
        string cancelText,
        SettingsPalette palette,
        bool rounded)
    {
        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition(new GridLength(32)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        root.Children.Add(BuildTitleBar(title, palette));

        Grid body = new() { Margin = new Thickness(28, 8, 28, 20), };
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(body, 1);

        TextBlock header = TrayAppDotNETSettingsUI.SectionHeader(title, palette);
        Grid.SetRow(header, 0);
        body.Children.Add(header);

        TextBlock descriptionText = TrayAppDotNETSettingsUI.DescriptionText(description, palette);
        descriptionText.Margin = new Thickness(0, 0, 0, 12);
        Grid.SetRow(descriptionText, 1);
        body.Children.Add(descriptionText);

        TextBlock changelogText = TrayAppDotNETSettingsUI.Text(changelog, palette, 12);
        changelogText.FontFamily = new FontFamily("Consolas, Cascadia Mono, Segoe UI");
        changelogText.LineHeight = ChangelogLineHeightPx;
        changelogText.TextWrapping = TextWrapping.Wrap;

        Border changelogBox = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.ControlBackground),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(rounded ? 4 : 0),
            Padding = new Thickness(12, 8),
            Child = new ScrollViewer
            {
                MaxHeight = MaxVisibleChangelogLines * ChangelogLineHeightPx + 4,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = changelogText,
            },
        };
        Grid.SetRow(changelogBox, 2);
        body.Children.Add(changelogBox);

        SettingsButton cancel = TrayAppDotNETSettingsUI.Button(cancelText, palette);
        cancel.Padding = new Thickness(20, 8);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => Close(false);

        SettingsButton install = TrayAppDotNETSettingsUI.Button(confirmText, palette);
        install.Padding = new Thickness(20, 8);
        install.Click += (_, _) => Close(true);

        StackPanel buttons = TrayAppDotNETSettingsUI.Horizontal(cancel, install);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(0, 20, 0, 0);
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
        titleText.Margin = new Thickness(16, 0, 0, 0);
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
