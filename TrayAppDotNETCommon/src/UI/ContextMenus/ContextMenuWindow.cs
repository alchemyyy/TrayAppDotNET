using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Tray;
using TrayAppDotNETCommon.UI.WarmWindows;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.ContextMenus;

public enum ContextMenuPlacement
{
    Classic,
    Modern
}

public sealed record ContextMenuEntry(string Text, Action Click)
{
    public Glyph? LeadingGlyph { get; init; }
    public string? TrailingGlyph { get; init; }
    public Func<IReadOnlyList<ContextMenuEntry>>? SubmenuFactory { get; init; }
    public Action<bool>? HoverChanged { get; init; }
    public bool HasTopRule { get; init; }
    public bool HasBottomRule { get; init; }
}

public sealed class ContextMenuEntryBuilder
{
    private readonly List<ContextMenuEntry> _entries = [];
    private bool _nextHasTopRule;

    public int Count => _entries.Count;

    public void Add(string text, Action click, string? trailingGlyph = null) => Add(new ContextMenuEntry(text, click) { TrailingGlyph = trailingGlyph });

    public void AddSubmenu(string text, Func<IReadOnlyList<ContextMenuEntry>> submenuFactory) =>
        Add(new ContextMenuEntry(text, static () => { }) { SubmenuFactory = submenuFactory });

    public void Add(ContextMenuEntry entry)
    {
        _entries.Add(entry with { HasTopRule = entry.HasTopRule || _nextHasTopRule });
        _nextHasTopRule = false;
    }

    public void AddSeparator()
    {
        if (_entries.Count == 0) return;

        ContextMenuEntry last = _entries[^1];
        _entries[^1] = last with { HasBottomRule = true };
        _nextHasTopRule = true;
    }

    public List<ContextMenuEntry> ToList() => [.. _entries];
}

public class ContextMenuWindowOptions
{
    public required SettingsPalette Palette { get; init; }
    public bool Rounded { get; init; } = true;
    public double FontSize { get; init; } = 15;
    public FontWeight FontWeight { get; init; } = FontWeight.Normal;

    /// <summary>Sets the minimum highlighted item height, or uses automatic sizing when left as NaN.</summary>
    public double ItemHeight { get; init; } = double.NaN;
    public Color? SeparatorColor { get; init; }
    public Color? ShadowColor { get; init; }
    public bool ScrollToBottom { get; init; }

    /// <summary>Invokes pointer selections after the left button is released over the item.</summary>
    public bool InvokeOnPointerReleased { get; init; }

    /// <summary>Invokes the selected action before dismissing the menu.</summary>
    public bool InvokeBeforeClose { get; init; }

    /// <summary>Controls whether showing the menu transfers keyboard focus into its window.</summary>
    public bool ActivateOnShow { get; init; } = true;

    public double LeadingGlyphFontSize { get; init; } = 16;
    public double LeadingGlyphColumnWidth { get; init; } = 24;
    public double TrailingGlyphFontSize { get; init; } = 12;
    public string SubmenuGlyph { get; init; } = "\uE76C";
    public ITrayAppDotNETTrayMenuSettings? ContextMenuSettings { get; init; }
    public bool UseSystemSubmenuShowDelay { get; init; }
    public int SubmenuShowDelayMilliseconds { get; init; } =
        TimeConstants.TrayMenuSubmenuShowDelayDefaultMs;

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

public class ContextMenuWindow : Window, ITrayAppDotNETWarmWindow
{
    internal static readonly int SystemSubmenuShowDelayMilliseconds = ResolveSystemSubmenuShowDelayMilliseconds();

    private readonly ContextMenuWindowOptions _options;
    private readonly UIResourceScope _windowResources;
    private readonly ContextMenuWindow? _parentMenu;
    private UIContentGeneration? _contentGeneration;
    private ScrollViewer? _scrollViewer;
    private DispatcherTimer? _submenuHoverTimer;
    private ContextMenuWindow? _childMenu;
    private ContextMenuItemControl? _childMenuOwner;
    private ContextMenuItemControl? _hoveredItem;
    private ContextMenuEntry? _hoveredEntry;
    private bool _closed;
    private bool _closedFromDeactivation;
    private bool _closedFromSelection;
    private bool _deactivationCheckPending;
    public bool IsWarmPriming { get; set; }
    public bool IsManagedByWarmSlot { get; set; }
    public bool ClosedFromDeactivation => _closedFromDeactivation;
    public bool ClosedFromSelection => _closedFromSelection;
    public event EventHandler? WarmDismissed;

    public ContextMenuWindow(IReadOnlyList<ContextMenuEntry> entries, ContextMenuWindowOptions options)
        : this(options, parentMenu: null)
    {
        InitializeStandardMenu(entries);
    }

    /// <summary>Initializes the common menu window shell for a derived menu control.</summary>
    protected ContextMenuWindow(ContextMenuWindowOptions options)
        : this(options, parentMenu: null)
    {
    }

    private ContextMenuWindow(
        IReadOnlyList<ContextMenuEntry> entries,
        ContextMenuWindowOptions options,
        ContextMenuWindow? parentMenu)
        : this(options, parentMenu)
    {
        ArgumentNullException.ThrowIfNull(entries);
        InitializeStandardMenu(entries);
    }

    private ContextMenuWindow(ContextMenuWindowOptions options, ContextMenuWindow? parentMenu)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _parentMenu = parentMenu;
        _windowResources = new UIResourceScope(GetType().Name);

        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        CanResize = false;
        Topmost = true;
        ShowActivated = options.ActivateOnShow;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Deactivated += OnDeactivated;
        KeyDown += OnKeyDown;
        _windowResources.Add(() => KeyDown -= OnKeyDown);
        _windowResources.Add(() => Deactivated -= OnDeactivated);
    }

    private void InitializeStandardMenu(IReadOnlyList<ContextMenuEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        StackPanel items = new();
        bool hasSubmenus = false;
        UIResourceScope contentResources = new($"{GetType().Name}.Content");
        foreach (ContextMenuEntry entry in entries)
        {
            hasSubmenus |= entry.SubmenuFactory != null;
            ContextMenuItemControl item = contentResources.Own(new ContextMenuItemControl(
                entry,
                _options,
                OnItemInvoked,
                OnItemHoverChanged));
            items.Children.Add(item);
        }

        InitializeMenuContent(items, contentResources, hasSubmenus);
    }

    /// <summary>Installs custom menu rows into the common context-menu window shell.</summary>
    protected void InitializeMenuContent(
        Control itemsContent,
        UIResourceScope contentResources,
        bool hasSubmenus = false)
    {
        ArgumentNullException.ThrowIfNull(itemsContent);
        ArgumentNullException.ThrowIfNull(contentResources);
        if (_contentGeneration != null)
            throw new InvalidOperationException("Context menu content has already been initialized.");

        if (hasSubmenus)
        {
            _submenuHoverTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(Math.Max(1, ResolveSubmenuShowDelayMilliseconds()))
            };
            _submenuHoverTimer.Tick += OnSubmenuHoverTimerTick;
            _windowResources.Add(() =>
            {
                _submenuHoverTimer.Stop();
                _submenuHoverTimer.Tick -= OnSubmenuHoverTimerTick;
            });
        }

        _scrollViewer = new ScrollViewer
        {
            Content = itemsContent,
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
        ControlNameScope.For(this).AssignLogicalSubtree(root, this);
        Content = _contentGeneration.Root;
    }

    public void ShowAt(
        TrayAppDotNETShellTrayIcon trayIcon,
        PixelPoint cursorPoint,
        ContextMenuPlacement placement)
    {
        if (_closed) return;

        _closedFromDeactivation = false;
        _closedFromSelection = false;
        PixelRect stagingWorkArea = ResolveWorkArea(cursorPoint);
        Opacity = 0;
        Position = stagingWorkArea.Position;
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
            if (_options.ActivateOnShow) Activate();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Shows the menu at an arbitrary screen point within its owner's work area.</summary>
    public void ShowAt(Window owner, PixelPoint screenPosition)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ShowAtScreenPoint(screenPosition, owner);
    }

    /// <summary>Shows an unowned menu at an arbitrary screen point.</summary>
    public void ShowAt(PixelPoint screenPosition) => ShowAtScreenPoint(screenPosition, owner: null);

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
            if (_options.ActivateOnShow) Activate();
        }, DispatcherPriority.Loaded);
    }

    private void OnItemInvoked(ContextMenuItemControl item, ContextMenuEntry entry)
    {
        if (entry.SubmenuFactory != null)
        {
            SwitchSubmenu(item, entry);
            return;
        }

        InvokeAndClose(entry.Click);
    }

    private void OnItemHoverChanged(ContextMenuItemControl item, ContextMenuEntry entry, bool isHovered)
    {
        DispatcherTimer? submenuHoverTimer = _submenuHoverTimer;
        if (isHovered)
        {
            _hoveredItem = item;
            _hoveredEntry = entry;
            if (ReferenceEquals(item, _childMenuOwner))
            {
                submenuHoverTimer?.Stop();
                return;
            }

            bool requiresSubmenuTransition = entry.SubmenuFactory != null || _childMenu != null;
            if (!requiresSubmenuTransition)
            {
                submenuHoverTimer?.Stop();
                return;
            }

            int submenuShowDelayMilliseconds = ResolveSubmenuShowDelayMilliseconds();
            if (submenuShowDelayMilliseconds <= 0)
            {
                SwitchSubmenu(item, entry);
                return;
            }

            if (submenuHoverTimer == null) return;
            submenuHoverTimer.Stop();
            submenuHoverTimer.Interval = TimeSpan.FromMilliseconds(submenuShowDelayMilliseconds);
            submenuHoverTimer.Start();
            return;
        }

        if (!ReferenceEquals(item, _hoveredItem)) return;
        _hoveredItem = null;
        _hoveredEntry = null;
        submenuHoverTimer?.Stop();
    }

    private void OnSubmenuHoverTimerTick(object? sender, EventArgs eventArgs)
    {
        _submenuHoverTimer?.Stop();
        ContextMenuItemControl? hoveredItem = _hoveredItem;
        ContextMenuEntry? hoveredEntry = _hoveredEntry;
        if (hoveredItem == null || hoveredEntry == null) return;

        SwitchSubmenu(hoveredItem, hoveredEntry);
    }

    private void SwitchSubmenu(ContextMenuItemControl owner, ContextMenuEntry entry)
    {
        if (ReferenceEquals(owner, _childMenuOwner) && _childMenu is { IsVisible: true }) return;

        CloseChildMenu();
        Func<IReadOnlyList<ContextMenuEntry>>? submenuFactory = entry.SubmenuFactory;
        if (submenuFactory == null) return;

        IReadOnlyList<ContextMenuEntry> submenuEntries;
        try
        {
            submenuEntries = submenuFactory() ?? [];
        }
        catch (Exception exception)
        {
            TADNLog.Log(
                $"{GetType().Name} submenu creation failed for '{entry.Text}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return;
        }

        if (submenuEntries.Count == 0) return;

        ContextMenuWindow childMenu = new(submenuEntries, _options, this)
        {
            ShowActivated = false
        };
        _childMenu = childMenu;
        _childMenuOwner = owner;
        owner.SetSubmenuOpen(true);
        try
        {
            childMenu.ShowAsSubmenu(owner);
        }
        catch
        {
            _childMenu = null;
            _childMenuOwner = null;
            owner.SetSubmenuOpen(false);
            childMenu.Close();
            throw;
        }
    }

    private void ShowAsSubmenu(ContextMenuItemControl owner)
    {
        ContextMenuWindow parentMenu = _parentMenu
                                    ?? throw new InvalidOperationException("A submenu requires a parent menu.");
        Opacity = 0;
        Position = new PixelPoint(_options.OffscreenPosition, _options.OffscreenPosition);
        Show(parentMenu);

        Dispatcher.UIThread.Post(() =>
        {
            if (_windowResources.IsDisposed || !IsVisible || !parentMenu.IsVisible) return;
            ScrollViewer? scrollViewer = _scrollViewer;
            if (scrollViewer == null) return;

            PixelRect ownerBounds = ScreenBounds(owner);
            PixelPoint ownerCenter = new(
                ownerBounds.X + ownerBounds.Width / 2,
                ownerBounds.Y + ownerBounds.Height / 2);
            PixelRect workArea = ResolveWorkArea(ownerCenter);
            scrollViewer.MaxHeight = Math.Max(
                _options.PixelMinSize,
                (workArea.Height - 2 * _options.EdgePadding) / RenderScaling);

            UpdateLayout();
            int menuWidth = Math.Max(
                _options.PixelMinSize,
                (int)Math.Ceiling(Bounds.Width * RenderScaling));
            int menuHeight = Math.Max(
                _options.PixelMinSize,
                (int)Math.Ceiling(Bounds.Height * RenderScaling));
            Position = ResolveSubmenuPosition(
                workArea,
                ownerBounds,
                new PixelSize(menuWidth, menuHeight),
                _options.EdgePadding);
            Opacity = 1;
        }, DispatcherPriority.Loaded);
    }

    internal static PixelPoint ResolveSubmenuPosition(
        PixelRect workArea,
        PixelRect ownerBounds,
        PixelSize menuSize,
        int edgePadding)
    {
        int minimumX = workArea.X + edgePadding;
        int maximumX = Math.Max(minimumX, workArea.Right - menuSize.Width - edgePadding);
        int rightX = ownerBounds.Right;
        int leftX = ownerBounds.X - menuSize.Width;
        int requestedX = rightX <= maximumX
            ? rightX
            : leftX >= minimumX
                ? leftX
                : maximumX;
        int minimumY = workArea.Y + edgePadding;
        int maximumY = Math.Max(minimumY, workArea.Bottom - menuSize.Height - edgePadding);

        return new PixelPoint(
            Math.Clamp(requestedX, minimumX, maximumX),
            Math.Clamp(ownerBounds.Y, minimumY, maximumY));
    }

    internal static PixelPoint ResolveScreenPointPosition(
        PixelRect workArea,
        PixelPoint screenPosition,
        PixelSize menuSize,
        int edgePadding)
    {
        int minimumX = workArea.X + edgePadding;
        int minimumY = workArea.Y + edgePadding;
        int maximumX = Math.Max(minimumX, workArea.Right - menuSize.Width - edgePadding);
        int maximumY = Math.Max(minimumY, workArea.Bottom - menuSize.Height - edgePadding);
        return new PixelPoint(
            Math.Clamp(screenPosition.X, minimumX, maximumX),
            Math.Clamp(screenPosition.Y, minimumY, maximumY));
    }

    private PixelPoint ResolvePosition(
        TrayAppDotNETShellTrayIcon trayIcon,
        PixelPoint cursorPoint,
        ContextMenuPlacement placement,
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

        if (placement == ContextMenuPlacement.Modern)
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

    private void ShowAtScreenPoint(PixelPoint screenPosition, Window? owner)
    {
        if (_closed) return;

        _closedFromDeactivation = false;
        _closedFromSelection = false;
        Opacity = 0;
        Position = new PixelPoint(_options.OffscreenPosition, _options.OffscreenPosition);
        if (owner == null)
            Show();
        else
            Show(owner);

        Dispatcher.UIThread.Post(() =>
        {
            if (_windowResources.IsDisposed || !IsVisible) return;
            ScrollViewer? scrollViewer = _scrollViewer;
            if (scrollViewer == null) return;

            PixelRect workArea = ResolveWorkArea(screenPosition);
            scrollViewer.MaxHeight = Math.Max(
                _options.PixelMinSize,
                (workArea.Height - 2 * _options.EdgePadding) / RenderScaling);

            UpdateLayout();
            int menuWidth = Math.Max(
                _options.PixelMinSize,
                (int)Math.Ceiling(Bounds.Width * RenderScaling));
            int menuHeight = Math.Max(
                _options.PixelMinSize,
                (int)Math.Ceiling(Bounds.Height * RenderScaling));
            Position = ResolveScreenPointPosition(
                workArea,
                screenPosition,
                new PixelSize(menuWidth, menuHeight),
                _options.EdgePadding);
            if (_options.ScrollToBottom) ScrollToBottom();
            Opacity = 1;
            if (_options.ActivateOnShow) Activate();
        }, DispatcherPriority.Loaded);
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

    private void CloseChildMenu()
    {
        ContextMenuWindow? childMenu = _childMenu;
        if (childMenu == null) return;

        _childMenu = null;
        _childMenuOwner?.SetSubmenuOpen(false);
        _childMenuOwner = null;
        childMenu.CloseDescendantMenus();
        if (!childMenu._closed) childMenu.Close();
    }

    private void CloseDescendantMenus()
    {
        List<ContextMenuWindow> descendants = [];
        ContextMenuWindow? current = _childMenu;
        while (current != null)
        {
            descendants.Add(current);
            current = current._childMenu;
        }

        _childMenuOwner?.SetSubmenuOpen(false);
        _childMenuOwner = null;
        _childMenu = null;
        for (int menuIndex = 0; menuIndex < descendants.Count; menuIndex++)
        {
            ContextMenuWindow menu = descendants[menuIndex];
            menu._childMenuOwner?.SetSubmenuOpen(false);
            menu._childMenuOwner = null;
            menu._childMenu = null;
        }

        for (int menuIndex = descendants.Count - 1; menuIndex >= 0; menuIndex--)
        {
            ContextMenuWindow menu = descendants[menuIndex];
            if (!menu._closed) menu.Close();
        }
    }

    private ContextMenuWindow GetRootMenu()
    {
        ContextMenuWindow rootMenu = this;
        while (rootMenu._parentMenu != null)
            rootMenu = rootMenu._parentMenu;
        return rootMenu;
    }

    private bool IsAnyMenuActive()
    {
        ContextMenuWindow? menu = this;
        while (menu != null)
        {
            if (menu.IsActive) return true;
            menu = menu._childMenu;
        }

        return false;
    }

    /// <summary>Invokes an entry action using this menu's configured close ordering.</summary>
    protected void InvokeAndClose(Action action)
    {
        ContextMenuWindow rootMenu = GetRootMenu();
        rootMenu._closedFromSelection = true;
        rootMenu._closedFromDeactivation = false;
        if (_options.InvokeBeforeClose)
        {
            try
            {
                action();
            }
            finally
            {
                rootMenu.DismissForWarmCache();
            }

            return;
        }

        rootMenu.DismissForWarmCache();
        action();
    }

    public virtual void DismissForWarmCache()
    {
        ContextMenuWindow rootMenu = GetRootMenu();
        if (!ReferenceEquals(rootMenu, this))
        {
            rootMenu.DismissForWarmCache();
            return;
        }

        if (IsWarmPriming) return;
        CloseDescendantMenus();
        _submenuHoverTimer?.Stop();
        _hoveredItem = null;
        _hoveredEntry = null;

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
        ContextMenuWindow rootMenu = GetRootMenu();
        if (!ReferenceEquals(rootMenu, this))
        {
            rootMenu.CloseForWarmEviction();
            return;
        }

        CloseDescendantMenus();
        if (this is ITrayAppDotNETWarmResourceOwner resourceOwner)
            resourceOwner.DisposeWarmResources();

        IsManagedByWarmSlot = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        CloseDescendantMenus();
        ContextMenuWindow? parentMenu = _parentMenu;
        if (parentMenu != null && ReferenceEquals(parentMenu._childMenu, this))
        {
            parentMenu._childMenu = null;
            parentMenu._childMenuOwner?.SetSubmenuOpen(false);
            parentMenu._childMenuOwner = null;
        }

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
        ContextMenuWindow rootMenu = GetRootMenu();
        if (rootMenu._closedFromSelection || rootMenu._deactivationCheckPending) return;

        rootMenu._deactivationCheckPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            rootMenu._deactivationCheckPending = false;
            if (rootMenu._closed || rootMenu._closedFromSelection || rootMenu.IsAnyMenuActive()) return;
            if (!rootMenu.ShouldDismissAfterDeactivation()) return;

            rootMenu._closedFromDeactivation = true;
            rootMenu.DismissForWarmCache();
        }, DispatcherPriority.Input);
    }

    /// <summary>Lets derived menu controls retain ownership during a deferred deactivation check.</summary>
    protected virtual bool ShouldDismissAfterDeactivation() => true;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        DismissForWarmCache();
        e.Handled = true;
    }

    private static int ResolveSystemSubmenuShowDelayMilliseconds()
    {
        if (OperatingSystem.IsWindows() &&
            User32.SystemParametersInfo(User32.SPI_GETMENUSHOWDELAY, 0, out int delayMilliseconds, 0))
        {
            return Math.Clamp(delayMilliseconds, 0, TimeConstants.TrayMenuSubmenuShowDelayMaxMs);
        }

        return TimeConstants.TrayMenuSubmenuShowDelayDefaultMs;
    }

    private int ResolveSubmenuShowDelayMilliseconds() =>
        (_options.ContextMenuSettings?.UseSystemSubmenuShowDelay ?? _options.UseSystemSubmenuShowDelay)
            ? SystemSubmenuShowDelayMilliseconds
            : Math.Clamp(
                _options.ContextMenuSettings?.SubmenuShowDelayMs ?? _options.SubmenuShowDelayMilliseconds,
                TimeConstants.TrayMenuSubmenuShowDelayMinMs,
                TimeConstants.TrayMenuSubmenuShowDelayMaxMs);

    private CornerRadius ResolveCornerRadius(CornerRadius roundedRadius) =>
        _options.Rounded ? roundedRadius : new CornerRadius(0);

    private sealed class ContextMenuItemControl : Border, IDisposable
    {
        private readonly ContextMenuEntry _entry;
        private readonly ContextMenuWindowOptions _options;
        private readonly Border _itemBorder;
        private readonly Action<ContextMenuItemControl, ContextMenuEntry> _invoke;
        private readonly Action<ContextMenuItemControl, ContextMenuEntry, bool> _itemHoverChanged;
        private readonly Action<bool>? _hoverChanged;
        private bool _isPointerOver;
        private bool _isSubmenuOpen;
        private bool _disposed;

        public ContextMenuItemControl(
            ContextMenuEntry entry,
            ContextMenuWindowOptions options,
            Action<ContextMenuItemControl, ContextMenuEntry> invoke,
            Action<ContextMenuItemControl, ContextMenuEntry, bool> itemHoverChanged)
        {
            _entry = entry;
            _options = options;
            _invoke = invoke;
            _itemHoverChanged = itemHoverChanged;
            _hoverChanged = entry.HoverChanged;
            Background = Brushes.Transparent;
            Cursor = TrayAppDotNETCursors.Hand;
            Focusable = true;

            _itemBorder = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = ResolveCornerRadius(options, options.ItemCornerRadius),
                Padding = options.ItemPadding,
                Margin = options.ItemMargin,
                MinHeight = double.IsFinite(options.ItemHeight) ? options.ItemHeight : 0,
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

        private static Control BuildContent(ContextMenuEntry entry, ContextMenuWindowOptions options)
        {
            TextBlock label = TrayAppDotNETSettingsUI.Text(entry.Text, options.Palette, options.FontSize);
            label.FontWeight = options.FontWeight;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            string? resolvedTrailingGlyph = entry.SubmenuFactory != null
                ? options.SubmenuGlyph
                : entry.TrailingGlyph;

            if (entry.LeadingGlyph == null && string.IsNullOrEmpty(resolvedTrailingGlyph))
                return label;

            Grid content = new()
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(
                        entry.LeadingGlyph == null ? 0 : options.LeadingGlyphColumnWidth)),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(
                        string.IsNullOrEmpty(resolvedTrailingGlyph) ? new GridLength(0) : GridLength.Auto)
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

            if (string.IsNullOrEmpty(resolvedTrailingGlyph))
                return content;

            TextBlock trailingGlyphText = TrayAppDotNETSettingsUI.Text(
                resolvedTrailingGlyph,
                options.Palette,
                options.TrailingGlyphFontSize);
            trailingGlyphText.FontFamily = TrayAppDotNETSettingsUI.IconFont;
            trailingGlyphText.Margin = options.TrailingGlyphMargin;
            trailingGlyphText.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trailingGlyphText, 2);
            content.Children.Add(trailingGlyphText);

            return content;
        }

        private void UpdateVisual()
        {
            Color background = _isPointerOver || _isSubmenuOpen
                ? _options.Palette.Hover
                : Colors.Transparent;
            _itemBorder.Background = TrayAppDotNETSettingsUI.Brush(background);
        }

        public void SetSubmenuOpen(bool isOpen)
        {
            if (_disposed || _isSubmenuOpen == isOpen) return;

            _isSubmenuOpen = isOpen;
            UpdateVisual();
        }

        private void OnPointerEntered(object? sender, PointerEventArgs e)
        {
            if (_disposed) return;
            _isPointerOver = true;
            UpdateVisual();
            _itemHoverChanged(this, _entry, true);
            _hoverChanged?.Invoke(true);
        }

        private void OnPointerExited(object? sender, PointerEventArgs e)
        {
            if (_disposed) return;
            _isPointerOver = false;
            UpdateVisual();
            _itemHoverChanged(this, _entry, false);
            _hoverChanged?.Invoke(false);
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_disposed || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            if (!_options.InvokeOnPointerReleased)
                _invoke(this, _entry);

            e.Handled = true;
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_disposed || !_options.InvokeOnPointerReleased || e.InitialPressMouseButton != MouseButton.Left)
                return;

            if (_isPointerOver)
                _invoke(this, _entry);

            e.Handled = true;
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (_disposed) return;
            bool invokesEntry = e.Key is Key.Enter or Key.Space ||
                                e.Key == Key.Right && _entry.SubmenuFactory != null;
            if (!invokesEntry) return;

            _invoke(this, _entry);
            e.Handled = true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_isPointerOver)
            {
                _itemHoverChanged(this, _entry, false);
                _hoverChanged?.Invoke(false);
            }

            _isPointerOver = false;
            _isSubmenuOpen = false;
            PointerEntered -= OnPointerEntered;
            PointerExited -= OnPointerExited;
            PointerPressed -= OnPointerPressed;
            PointerReleased -= OnPointerReleased;
            KeyDown -= OnKeyDown;
            Cursor = null;
            Child = null;
        }

        private static CornerRadius ResolveCornerRadius(ContextMenuWindowOptions options, CornerRadius roundedRadius) =>
            options.Rounded ? roundedRadius : new CornerRadius(0);
    }
}
