using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.UI.ControlMapping;

namespace TrayAppDotNETCommon.UI.Controls;

public sealed record TrayAppDotNETDialogTitleBarOptions
{
    public required Window Window { get; init; }

    /// <summary>Gets the title text; null shows the window's Title and follows its changes.</summary>
    public string? Title { get; init; }

    public required SettingsPalette Palette { get; init; }
    public required string CloseTooltip { get; init; }
    public required Action Close { get; init; }

    /// <summary>Gets the node of the title bar scope; the bar itself is the window's move handle.</summary>
    public ControlMapNodeID? BarNode { get; init; }

    public ControlMapNodeID? CloseNode { get; init; }

    /// <summary>Gets the bar height; NaN sizes the bar to the caption close button.</summary>
    public double Height { get; init; } = double.NaN;

    /// <summary>Gets the title font size; NaN uses the settings UI default.</summary>
    public double FontSize { get; init; } = double.NaN;

    public FontWeight TitleFontWeight { get; init; } = FontWeight.Normal;

    public Thickness TitleMargin { get; init; }

    /// <summary>Gets the bar background; null leaves the window background showing through.</summary>
    public Color? Background { get; init; }

    /// <summary>Gets extra caption buttons, such as a pin, placed left of the close button.</summary>
    public IReadOnlyList<Control> CaptionButtons { get; init; } = [];
}

/// <summary>Shared chrome for borderless TrayAppDotNET dialogs.</summary>
public static class TrayAppDotNETDialogChrome
{
    /// <summary>
    /// Builds a title bar: a drag area that moves the window, the title, and the shared caption close button with its
    /// red hover. Every handler is released with <paramref name="resources"/>.
    /// </summary>
    public static Grid TitleBar(TrayAppDotNETDialogTitleBarOptions options, UIResourceScope resources)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resources);

        Grid bar = new()
        {
            Background = options.Background is { } background
                ? TrayAppDotNETSettingsUI.Brush(background)
                : Brushes.Transparent
        };
        if (!double.IsNaN(options.Height)) bar.Height = options.Height;
        bar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        bar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        if (options.BarNode is { } barNode) bar.MapTo(barNode);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };

        Window window = options.Window;
        EventHandler<PointerPressedEventArgs> beginMove = (_, e) =>
        {
            if (!e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed) return;
            if (e.Source is Visual source && buttons.IsVisualAncestorOf(source)) return;

            window.BeginMoveDrag(e);
            e.Handled = true;
        };
        bar.PointerPressed += beginMove;
        resources.Add(() => bar.PointerPressed -= beginMove);

        TextBlock title = double.IsNaN(options.FontSize)
            ? TrayAppDotNETSettingsUI.Text(options.Title ?? string.Empty, options.Palette)
            : TrayAppDotNETSettingsUI.Text(options.Title ?? string.Empty, options.Palette, options.FontSize);
        title.FontWeight = options.TitleFontWeight;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Margin = options.TitleMargin;
        title.IsHitTestVisible = false;
        if (options.Title == null)
        {
            IDisposable titleBinding = title.Bind(TextBlock.TextProperty, window.GetObservable(Window.TitleProperty));
            resources.Add(titleBinding.Dispose);
        }

        bar.Children.Add(title);

        foreach (Control captionButton in options.CaptionButtons)
            buttons.Children.Add(captionButton);

        TrayAppDotNETCaptionCloseButton close = new(options.Palette);
        if (options.CloseNode is { } closeNode) close.MapTo(closeNode);
        TrayAppDotNETToolTip.SetTip(close, options.CloseTooltip);
        TrayAppDotNETToolTip.SuppressWhileEngaged(close);
        Action closeAction = options.Close;
        EventHandler onClose = (_, _) => closeAction();
        close.Click += onClose;
        resources.Add(() => close.Click -= onClose);
        buttons.Children.Add(close);
        Grid.SetColumn(buttons, value: 1);
        bar.Children.Add(buttons);
        return bar;
    }
}
