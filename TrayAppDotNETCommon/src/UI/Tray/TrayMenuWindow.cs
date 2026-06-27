using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.WarmWindows;

namespace TrayAppDotNETCommon.UI.Tray;

public enum TrayMenuWindowPlacement
{
    Classic,
    Modern,
}

public sealed record TrayMenuEntry(string Text, Action Click)
{
    public string? TrailingGlyph { get; init; }
    public bool HasTopRule { get; init; }
    public bool HasBottomRule { get; init; }
}

public sealed class TrayMenuEntryBuilder
{
    private readonly List<TrayMenuEntry> _entries = [];
    private bool _nextHasTopRule;

    public int Count => _entries.Count;

    public void Add(string text, Action click, string? trailingGlyph = null)
    {
        Add(new TrayMenuEntry(text, click) { TrailingGlyph = trailingGlyph, });
    }

    public void Add(TrayMenuEntry entry)
    {
        _entries.Add(entry with { HasTopRule = entry.HasTopRule || _nextHasTopRule, });
        _nextHasTopRule = false;
    }

    public void AddSeparator()
    {
        if (_entries.Count == 0) return;

        TrayMenuEntry last = _entries[^1];
        _entries[^1] = last with { HasBottomRule = true };
        _nextHasTopRule = true;
    }

    public List<TrayMenuEntry> ToList() => [.. _entries];
}

public sealed class TrayMenuWindowOptions
{
    public required SettingsPalette Palette { get; init; }
    public bool Rounded { get; init; } = true;
    public int FontSize { get; init; } = 15;
    public Color? SeparatorColor { get; init; }
    public Color? ShadowColor { get; init; }
    public bool ScrollToBottom { get; init; }
    public double TrailingGlyphFontSize { get; init; } = 12;

    public int EdgePadding { get; init; } = 8;
    public int OffscreenPosition { get; init; } = -32000;
    public int FallbackWorkAreaWidth { get; init; } = 1920;
    public int FallbackWorkAreaHeight { get; init; } = 1080;
    public int PixelMinSize { get; init; } = 1;

    public Thickness RootBorderThickness { get; init; } = new(0);
    public CornerRadius RootCornerRadius { get; init; } = new(8);
    public Thickness RootPadding { get; init; } = new(2);
    public CornerRadius ItemCornerRadius { get; init; } = new(4);
    public Thickness ItemPadding { get; init; } = new(6);
    public Thickness ItemMargin { get; init; } = new(2, 0);
    public Thickness RuleMargin { get; init; } = new(-2, 0);
    public Thickness TrailingGlyphMargin { get; init; } = new(24, 0, 0, 0);
    public double ItemMinWidth { get; init; } = 150;
    public double RuleHeight { get; init; } = 1;
    public double RowRuleSpacing { get; init; } = 4;
    public double RowSpacing { get; init; } = 2;
    public double ShadowOffsetY { get; init; } = 2;
    public double ShadowBlur { get; init; } = 20;
}

public class TrayMenuWindow : Window, ITrayAppDotNETWarmWindow
{
    private readonly TrayMenuWindowOptions _options;
    private readonly ScrollViewer _scrollViewer;
    public bool IsWarmPriming { get; set; }
    public bool IsManagedByWarmSlot { get; set; }
    public event EventHandler? WarmDismissed;

    public TrayMenuWindow(IReadOnlyList<TrayMenuEntry> entries, TrayMenuWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);

        _options = options;

        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        CanResize = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;

        StackPanel items = new();
        foreach (TrayMenuEntry entry in entries)
        {
            items.Children.Add(new TrayMenuItemControl(
                entry,
                _options,
                () => InvokeAndClose(entry.Click)));
        }

        _scrollViewer = new ScrollViewer
        {
            Content = items,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Focusable = false,
        };

        Border root = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(_options.Palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_options.Palette.Border),
            BorderThickness = _options.RootBorderThickness,
            CornerRadius = ResolveCornerRadius(_options.RootCornerRadius),
            Padding = _options.RootPadding,
            Child = _scrollViewer,
        };

        if (_options.ShadowColor is { } shadowColor)
        {
            root.BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetY = _options.ShadowOffsetY, Blur = _options.ShadowBlur, Color = shadowColor,
            });
        }

        Content = root;
        Deactivated += (_, _) => DismissForWarmCache();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;

            DismissForWarmCache();
            e.Handled = true;
        };
    }

    public void ShowAt(
        TrayAppDotNETShellTrayIcon trayIcon,
        PixelPoint cursorPoint,
        TrayMenuWindowPlacement placement)
    {
        Opacity = 0;
        Position = new PixelPoint(_options.OffscreenPosition, _options.OffscreenPosition);
        Show();

        Dispatcher.UIThread.Post(() =>
        {
            PixelRect workArea = ResolveWorkArea(cursorPoint);
            _scrollViewer.MaxHeight = Math.Max(
                _options.PixelMinSize,
                (workArea.Height - (2 * _options.EdgePadding)) / RenderScaling);

            UpdateLayout();
            Position = ResolvePosition(trayIcon, cursorPoint, placement, workArea);
            if (_options.ScrollToBottom) ScrollToBottom();
            Opacity = 1;
            Activate();
        }, DispatcherPriority.Loaded);
    }

    private PixelPoint ResolvePosition(
        TrayAppDotNETShellTrayIcon trayIcon,
        PixelPoint cursorPoint,
        TrayMenuWindowPlacement placement,
        PixelRect workArea)
    {
        double scale = RenderScaling;
        int menuWidth = Math.Max(
            _options.PixelMinSize,
            (int)Math.Ceiling(Bounds.Width * scale));
        int menuHeight = Math.Max(
            _options.PixelMinSize,
            (int)Math.Ceiling(Bounds.Height * scale));

        int minLeft = workArea.X + _options.EdgePadding;
        int minTop = workArea.Y + _options.EdgePadding;
        int maxLeft = Math.Max(minLeft, workArea.Right - menuWidth - _options.EdgePadding);
        int maxTop = Math.Max(minTop, workArea.Bottom - menuHeight - _options.EdgePadding);

        if (placement == TrayMenuWindowPlacement.Modern)
        {
            int left = workArea.Right - menuWidth - _options.EdgePadding;
            int top = workArea.Bottom - menuHeight - _options.EdgePadding;
            if (trayIcon.TryGetIconRect(out PixelRect iconRect))
            {
                left = iconRect.Center.X - menuWidth / 2;
                top = iconRect.Center.Y - menuHeight / 2;
            }

            return new PixelPoint(
                Math.Clamp(left, minLeft, maxLeft),
                Math.Clamp(top, minTop, maxTop));
        }

        return new PixelPoint(cursorPoint.X, Math.Clamp(cursorPoint.Y, minTop, maxTop));
    }

    private PixelRect ResolveWorkArea(PixelPoint cursorPoint) =>
        (Screens.ScreenFromPoint(cursorPoint) ?? Screens.Primary)?.WorkingArea
        ?? new PixelRect(0, 0, _options.FallbackWorkAreaWidth, _options.FallbackWorkAreaHeight);

    private void ScrollToBottom()
    {
        double maxOffset = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, maxOffset);
    }

    private void InvokeAndClose(Action action)
    {
        DismissForWarmCache();
        action();
    }

    public virtual void DismissForWarmCache()
    {
        if (IsWarmPriming) return;

        if (IsManagedByWarmSlot)
        {
            Hide();
            WarmDismissed?.Invoke(this, EventArgs.Empty);
            return;
        }

        Close();
    }

    public virtual void CloseForWarmEviction()
    {
        IsManagedByWarmSlot = false;
        Close();
    }

    private CornerRadius ResolveCornerRadius(CornerRadius roundedRadius) =>
        _options.Rounded ? roundedRadius : new CornerRadius(0);

    private sealed class TrayMenuItemControl : Border
    {
        private readonly TrayMenuWindowOptions _options;
        private readonly Border _itemBorder;
        private bool _isPointerOver;

        public TrayMenuItemControl(
            TrayMenuEntry entry,
            TrayMenuWindowOptions options,
            Action click)
        {
            _options = options;
            Background = Brushes.Transparent;
            Cursor = new Cursor(StandardCursorType.Hand);
            Focusable = true;

            _itemBorder = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = ResolveCornerRadius(options, options.ItemCornerRadius),
                Padding = options.ItemPadding,
                Margin = options.ItemMargin,
                MinWidth = options.ItemMinWidth,
                Child = BuildContent(entry, options),
            };

            Grid layout = new();
            layout.RowDefinitions.Add(
                new RowDefinition(new GridLength(entry.HasTopRule ? options.RowRuleSpacing : options.RowSpacing)));
            layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            layout.RowDefinitions.Add(
                new RowDefinition(new GridLength(entry.HasBottomRule ? options.RowRuleSpacing : options.RowSpacing)));
            layout.RowDefinitions.Add(new RowDefinition(new GridLength(entry.HasBottomRule ? options.RuleHeight : 0)));

            Grid.SetRow(_itemBorder, 1);
            layout.Children.Add(_itemBorder);

            Border rule = new()
            {
                Height = options.RuleHeight,
                Background = TrayAppDotNETSettingsUI.Brush(options.SeparatorColor ?? options.Palette.Border),
                Margin = options.RuleMargin,
                IsVisible = entry.HasBottomRule,
            };
            Grid.SetRow(rule, 3);
            layout.Children.Add(rule);

            Child = layout;

            PointerEntered += (_, _) =>
            {
                _isPointerOver = true;
                UpdateVisual();
            };
            PointerExited += (_, _) =>
            {
                _isPointerOver = false;
                UpdateVisual();
            };
            PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

                click();
                e.Handled = true;
            };
            KeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Enter or Key.Space)) return;

                click();
                e.Handled = true;
            };
        }

        private static Control BuildContent(TrayMenuEntry entry, TrayMenuWindowOptions options)
        {
            TextBlock label = TrayAppDotNETSettingsUI.Text(entry.Text, options.Palette, options.FontSize);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.TextTrimming = TextTrimming.CharacterEllipsis;

            if (string.IsNullOrEmpty(entry.TrailingGlyph))
                return label;

            Grid content = new()
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto),
                },
            };

            Grid.SetColumn(label, 0);
            content.Children.Add(label);

            TextBlock glyph =
                TrayAppDotNETSettingsUI.Text(entry.TrailingGlyph, options.Palette, options.TrailingGlyphFontSize);
            glyph.FontFamily = TrayAppDotNETSettingsUI.IconFont;
            glyph.Margin = options.TrailingGlyphMargin;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(glyph, 1);
            content.Children.Add(glyph);

            return content;
        }

        private void UpdateVisual()
        {
            Color background = _isPointerOver ? _options.Palette.Hover : Colors.Transparent;
            _itemBorder.Background = TrayAppDotNETSettingsUI.Brush(background);
        }

        private static CornerRadius ResolveCornerRadius(TrayMenuWindowOptions options, CornerRadius roundedRadius) =>
            options.Rounded ? roundedRadius : new CornerRadius(0);
    }
}
