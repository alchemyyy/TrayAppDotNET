using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.WarmWindows;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Tray;

public enum TrayMenuWindowPlacement
{
    Classic,
    Modern
}

public sealed record TrayMenuEntry(string Text, Action Click)
{
    public Glyph? LeadingGlyph { get; init; }
    public string? TrailingGlyph { get; init; }
    public bool HasTopRule { get; init; }
    public bool HasBottomRule { get; init; }
}

public sealed class TrayMenuEntryBuilder
{
    private readonly List<TrayMenuEntry> _entries = [];
    private bool _nextHasTopRule;

    public int Count => _entries.Count;

    public void Add(string text, Action click, string? trailingGlyph = null) => Add(new TrayMenuEntry(text, click) { TrailingGlyph = trailingGlyph });

    public void Add(TrayMenuEntry entry)
    {
        _entries.Add(entry with { HasTopRule = entry.HasTopRule || _nextHasTopRule });
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

    /// <summary>Invokes pointer selections after the left button is released over the item.</summary>
    public bool InvokeOnPointerReleased { get; init; }

    /// <summary>Invokes the selected action before dismissing the menu.</summary>
    public bool InvokeBeforeClose { get; init; }

    public double LeadingGlyphFontSize { get; init; } = 16;
    public double LeadingGlyphColumnWidth { get; init; } = 24;
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
    public Thickness LeadingGlyphMargin { get; init; } = new(0, 0, 8, 0);
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
    private readonly UIResourceScope _windowResources;
    private UIContentGeneration? _contentGeneration;
    private ScrollViewer? _scrollViewer;
    private bool _closed;
    private bool _closedFromDeactivation;
    private bool _closedFromSelection;
    public bool IsWarmPriming { get; set; }
    public bool IsManagedByWarmSlot { get; set; }
    public bool ClosedFromDeactivation => _closedFromDeactivation;
    public bool ClosedFromSelection => _closedFromSelection;
    public event EventHandler? WarmDismissed;

    public TrayMenuWindow(IReadOnlyList<TrayMenuEntry> entries, TrayMenuWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _windowResources = new UIResourceScope(GetType().Name);

        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        CanResize = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;

        StackPanel items = new();
        UIResourceScope contentResources = new($"{GetType().Name}.Content");
        foreach (TrayMenuEntry entry in entries)
        {
            TrayMenuItemControl item = contentResources.Own(new TrayMenuItemControl(
                entry,
                _options,
                () => InvokeAndClose(entry.Click)));
            items.Children.Add(item);
        }

        _scrollViewer = new ScrollViewer
        {
            Content = items,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Focusable = false
        };

        Border root = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(_options.Palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(_options.Palette.Border),
            BorderThickness = _options.RootBorderThickness,
            CornerRadius = ResolveCornerRadius(_options.RootCornerRadius),
            Padding = _options.RootPadding,
            Child = _scrollViewer
        };

        if (_options.ShadowColor is { } shadowColor)
        {
            root.BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetY = _options.ShadowOffsetY, Blur = _options.ShadowBlur, Color = shadowColor
            });
        }

        _contentGeneration = new UIContentGeneration(
            $"{GetType().Name}.Content",
            root,
            contentResources);
        Content = _contentGeneration.Root;
        Deactivated += OnDeactivated;
        KeyDown += OnKeyDown;
        _windowResources.Add(() => KeyDown -= OnKeyDown);
        _windowResources.Add(() => Deactivated -= OnDeactivated);
    }

    public void ShowAt(
        TrayAppDotNETShellTrayIcon trayIcon,
        PixelPoint cursorPoint,
        TrayMenuWindowPlacement placement)
    {
        if (_closed) return;

        _closedFromDeactivation = false;
        _closedFromSelection = false;
        Opacity = 0;
        Position = new PixelPoint(_options.OffscreenPosition, _options.OffscreenPosition);
        Show();

        Dispatcher.UIThread.Post(() =>
        {
            if (_windowResources.IsDisposed || !IsVisible) return;
            ScrollViewer? scrollViewer = _scrollViewer;
            if (scrollViewer == null) return;

            PixelRect workArea = ResolveWorkArea(cursorPoint);
            scrollViewer.MaxHeight = Math.Max(
                _options.PixelMinSize,
                (workArea.Height - 2 * _options.EdgePadding) / RenderScaling);

            UpdateLayout();
            Position = ResolvePosition(trayIcon, cursorPoint, placement, workArea);
            if (_options.ScrollToBottom) ScrollToBottom();
            Opacity = 1;
            Activate();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Shows the menu inside a containing window, horizontally aligned to one anchor and vertically outside another.
    /// </summary>
    public void ShowOver(Control alignmentAnchor, Control edgeAnchor, Window containingWindow)
    {
        ArgumentNullException.ThrowIfNull(alignmentAnchor);
        ArgumentNullException.ThrowIfNull(edgeAnchor);
        ArgumentNullException.ThrowIfNull(containingWindow);
        if (_closed) return;

        PixelRect alignmentBounds = ScreenBounds(alignmentAnchor);
        PixelRect edgeBounds = ScreenBounds(edgeAnchor);
        PixelRect anchorBounds = new(
            alignmentBounds.X,
            edgeBounds.Y,
            alignmentBounds.Width,
            edgeBounds.Height);
        PixelRect containingBounds = ScreenBounds(containingWindow);
        int availableHeight = ResolveOverlayAvailableHeight(containingBounds, anchorBounds);

        _closedFromDeactivation = false;
        _closedFromSelection = false;
        Opacity = 0;
        Position = containingBounds.Position;
        Show(containingWindow);

        Dispatcher.UIThread.Post(() =>
        {
            if (_windowResources.IsDisposed || !IsVisible) return;
            ScrollViewer? scrollViewer = _scrollViewer;
            if (scrollViewer == null) return;

            double chromeHeight = _options.RootBorderThickness.Top
                                  + _options.RootBorderThickness.Bottom
                                  + _options.RootPadding.Top
                                  + _options.RootPadding.Bottom;
            scrollViewer.MaxHeight = Math.Max(
                _options.PixelMinSize,
                availableHeight / RenderScaling - chromeHeight);

            UpdateLayout();
            int menuWidth = Math.Max(
                _options.PixelMinSize,
                (int)Math.Ceiling(Bounds.Width * RenderScaling));
            int menuHeight = Math.Max(
                _options.PixelMinSize,
                (int)Math.Ceiling(Bounds.Height * RenderScaling));
            Position = ResolveOverlayPosition(
                containingBounds,
                anchorBounds,
                new PixelSize(menuWidth, menuHeight));
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

        int minTop = workArea.Y + _options.EdgePadding;
        int maxTop = Math.Max(minTop, workArea.Bottom - menuHeight - _options.EdgePadding);

        if (placement == TrayMenuWindowPlacement.Modern)
        {
            PixelRect? iconRect = null;
            if (trayIcon.TryGetIconRect(out PixelRect resolvedIconRect))
                iconRect = resolvedIconRect;

            return TrayPopupPositioning.ResolveDockedPosition(
                workArea,
                new PixelSize(menuWidth, menuHeight),
                iconRect,
                _options.EdgePadding);
        }

        return new PixelPoint(cursorPoint.X, Math.Clamp(cursorPoint.Y, minTop, maxTop));
    }

    private PixelRect ResolveWorkArea(PixelPoint cursorPoint) =>
        TrayWorkArea.Resolve(
            Screens,
            cursorPoint,
            new PixelRect(0, 0, _options.FallbackWorkAreaWidth, _options.FallbackWorkAreaHeight));

    internal static PixelPoint ResolveOverlayPosition(
        PixelRect containingBounds,
        PixelRect anchorBounds,
        PixelSize menuSize)
    {
        int minLeft = containingBounds.X;
        int maxLeft = Math.Max(minLeft, containingBounds.Right - menuSize.Width);
        int minTop = containingBounds.Y;
        int maxTop = Math.Max(minTop, containingBounds.Bottom - menuSize.Height);
        bool showAbove = anchorBounds.Center.Y >= containingBounds.Center.Y;
        int requestedTop = showAbove
            ? anchorBounds.Y - menuSize.Height
            : anchorBounds.Bottom;

        return new PixelPoint(
            Math.Clamp(anchorBounds.X, minLeft, maxLeft),
            Math.Clamp(requestedTop, minTop, maxTop));
    }

    internal static int ResolveOverlayAvailableHeight(PixelRect containingBounds, PixelRect anchorBounds) =>
        Math.Max(
            1,
            anchorBounds.Center.Y >= containingBounds.Center.Y
                ? anchorBounds.Y - containingBounds.Y
                : containingBounds.Bottom - anchorBounds.Bottom);

    private static PixelRect ScreenBounds(Control control)
    {
        PixelPoint topLeft = control.PointToScreen(new Point(0, 0));
        PixelPoint bottomRight = control.PointToScreen(new Point(control.Bounds.Width, control.Bounds.Height));
        return new PixelRect(
            topLeft.X,
            topLeft.Y,
            Math.Max(1, bottomRight.X - topLeft.X),
            Math.Max(1, bottomRight.Y - topLeft.Y));
    }

    private void ScrollToBottom()
    {
        ScrollViewer? scrollViewer = _scrollViewer;
        if (scrollViewer == null) return;

        double maxOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, maxOffset);
    }

    private void InvokeAndClose(Action action)
    {
        _closedFromSelection = true;
        _closedFromDeactivation = false;
        if (_options.InvokeBeforeClose)
        {
            try
            {
                action();
            }
            finally
            {
                DismissForWarmCache();
            }

            return;
        }

        DismissForWarmCache();
        action();
    }

    public virtual void DismissForWarmCache()
    {
        if (IsWarmPriming) return;

        if (IsManagedByWarmSlot)
        {
            Hide();
            if (this is ITrayAppDotNETWarmResourceOwner resourceOwner)
                resourceOwner.TrimHiddenWarmResources();

            WarmDismissed?.Invoke(this, EventArgs.Empty);
            return;
        }

        Close();
    }

    public virtual void CloseForWarmEviction()
    {
        if (this is ITrayAppDotNETWarmResourceOwner resourceOwner)
            resourceOwner.DisposeWarmResources();

        IsManagedByWarmSlot = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        UIContentGeneration? contentGeneration = Interlocked.Exchange(ref _contentGeneration, null);
        try
        {
            Content = null;
        }
        finally
        {
            contentGeneration?.Dispose();
            _scrollViewer = null;
            _windowResources.Dispose();
            WarmDismissed = null;
            base.OnClosed(e);
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_closedFromSelection) return;

        _closedFromDeactivation = true;
        DismissForWarmCache();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        DismissForWarmCache();
        e.Handled = true;
    }

    private CornerRadius ResolveCornerRadius(CornerRadius roundedRadius) =>
        _options.Rounded ? roundedRadius : new CornerRadius(0);

    private sealed class TrayMenuItemControl : Border, IDisposable
    {
        private readonly TrayMenuWindowOptions _options;
        private readonly Border _itemBorder;
        private readonly Action _click;
        private bool _isPointerOver;
        private bool _disposed;

        public TrayMenuItemControl(
            TrayMenuEntry entry,
            TrayMenuWindowOptions options,
            Action click)
        {
            _options = options;
            _click = click;
            Background = Brushes.Transparent;
            Cursor = TrayAppDotNETCursors.Hand;
            Focusable = true;

            _itemBorder = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = ResolveCornerRadius(options, options.ItemCornerRadius),
                Padding = options.ItemPadding,
                Margin = options.ItemMargin,
                MinWidth = options.ItemMinWidth,
                Child = BuildContent(entry, options)
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
                IsVisible = entry.HasBottomRule
            };
            Grid.SetRow(rule, 3);
            layout.Children.Add(rule);

            Child = layout;

            PointerEntered += OnPointerEntered;
            PointerExited += OnPointerExited;
            PointerPressed += OnPointerPressed;
            PointerReleased += OnPointerReleased;
            KeyDown += OnKeyDown;
        }

        private static Control BuildContent(TrayMenuEntry entry, TrayMenuWindowOptions options)
        {
            TextBlock label = TrayAppDotNETSettingsUI.Text(entry.Text, options.Palette, options.FontSize);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.TextTrimming = TextTrimming.CharacterEllipsis;

            if (entry.LeadingGlyph == null && string.IsNullOrEmpty(entry.TrailingGlyph))
                return label;

            Grid content = new()
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(
                        entry.LeadingGlyph == null ? 0 : options.LeadingGlyphColumnWidth)),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(
                        string.IsNullOrEmpty(entry.TrailingGlyph) ? new GridLength(0) : GridLength.Auto)
                }
            };

            if (entry.LeadingGlyph is { } leadingGlyph)
            {
                TextBlock leadingGlyphText = TrayAppDotNETSettingsUI.Text(
                    string.Empty,
                    options.Palette,
                    options.LeadingGlyphFontSize);
                GlyphApplicator.ApplyTo(leadingGlyphText, leadingGlyph);
                leadingGlyphText.Margin = options.LeadingGlyphMargin;
                leadingGlyphText.HorizontalAlignment = HorizontalAlignment.Center;
                leadingGlyphText.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(leadingGlyphText, 0);
                content.Children.Add(leadingGlyphText);
            }

            Grid.SetColumn(label, 1);
            content.Children.Add(label);

            if (string.IsNullOrEmpty(entry.TrailingGlyph))
                return content;

            TextBlock trailingGlyph =
                TrayAppDotNETSettingsUI.Text(entry.TrailingGlyph, options.Palette, options.TrailingGlyphFontSize);
            trailingGlyph.FontFamily = TrayAppDotNETSettingsUI.IconFont;
            trailingGlyph.Margin = options.TrailingGlyphMargin;
            trailingGlyph.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trailingGlyph, 2);
            content.Children.Add(trailingGlyph);

            return content;
        }

        private void UpdateVisual()
        {
            Color background = _isPointerOver ? _options.Palette.Hover : Colors.Transparent;
            _itemBorder.Background = TrayAppDotNETSettingsUI.Brush(background);
        }

        private void OnPointerEntered(object? sender, PointerEventArgs e)
        {
            if (_disposed) return;
            _isPointerOver = true;
            UpdateVisual();
        }

        private void OnPointerExited(object? sender, PointerEventArgs e)
        {
            if (_disposed) return;
            _isPointerOver = false;
            UpdateVisual();
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_disposed || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            if (!_options.InvokeOnPointerReleased)
                _click();

            e.Handled = true;
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_disposed || !_options.InvokeOnPointerReleased || e.InitialPressMouseButton != MouseButton.Left)
                return;

            if (_isPointerOver)
                _click();

            e.Handled = true;
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (_disposed || e.Key is not (Key.Enter or Key.Space)) return;

            _click();
            e.Handled = true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            PointerEntered -= OnPointerEntered;
            PointerExited -= OnPointerExited;
            PointerPressed -= OnPointerPressed;
            PointerReleased -= OnPointerReleased;
            KeyDown -= OnKeyDown;
            Cursor = null;
            Child = null;
        }

        private static CornerRadius ResolveCornerRadius(TrayMenuWindowOptions options, CornerRadius roundedRadius) =>
            options.Rounded ? roundedRadius : new CornerRadius(0);
    }
}
