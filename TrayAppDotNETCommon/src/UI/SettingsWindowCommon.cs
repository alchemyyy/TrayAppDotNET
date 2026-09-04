using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Transport;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Settings;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI;

public sealed record SettingsPageDescriptor<TPageKey>(
    TPageKey Key,
    string Label,
    Func<Control> BuildPage,
    Glyph? NavigationGlyph = null,
    Func<Color, Control>? NavigationIconFactory = null,
    double NavigationIconScale = 1.0,
    ITransform? NavigationIconTransform = null)
    where TPageKey : notnull;

/// <summary>
/// Shared settings-window shell: custom chrome, navigation, page hosting, confirmation overlay,
/// rounded-corner policy, and common card wrappers.
/// </summary>
public abstract partial class SettingsWindowCommon<TPageKey> : Window
    where TPageKey : notnull
{
    // Keep the custom frame available for borderless windows. It stays disabled while BorderOnly supplies
    // native resizing because drawing both frames doubles the border and insets the caption buttons
    private readonly bool EnableCustomWindowBorder = false;
    private const int CaptionButtonCount = 3;
    private const int WorkAreaEdgeTolerancePixels = 1;

    private ContentControl _content = new();
    private readonly SettingsWindowCommonResources _settingsResources;
    private readonly CommonBindingsResources _commonBindingResources;
    private IReadOnlyList<SettingsPageDescriptor<TPageKey>> _pageDescriptors = [];
    private Dictionary<TPageKey, Func<Control>> _pages = [];
    private Dictionary<TPageKey, SettingsNavItem> _navItems = [];
    private IReadOnlyList<SettingsNavItem> _sidebarNavigationActions = [];
    private IReadOnlyList<SettingsNavItem> _sidebarFooterActions = [];
    private readonly Dictionary<TPageKey, double> _pageScrollOffsets = [];
    private readonly HashSet<TrayAppDotNETColorPickerWindow> _openColorPickers = [];
    private readonly UIResourceScope _windowResources;
    private UIContentGeneration? _shellGeneration;
    private UIContentGeneration? _pageGeneration;
    private UIResourceScope? _buildingPageResources;
    private SettingsScrollHost? _scrollHost;
    private SettingsSidebar? _sidebar;
    private Control? _sidebarHeader;
    private ColumnDefinition? _sidebarColumn;
    private SettingsSidebarResizeHandle? _sidebarResizeHandle;
    private Control? _sidebarOverlay;
    private Grid? _pageOverlayHost;
    private bool _pageOverlayAlignsToContentArea;
    private Border? _titleBarDragZone;
    private double _currentSidebarWidth;
    private bool? _sidebarCollapseOverride;
    private bool? _sidebarControlsCollapsedState;
    private TaskCompletionSource<bool>? _confirmTcs;
    private Border? _confirmOverlay;
    private TextBlock? _confirmTitle;
    private TextBlock? _confirmMessage;
    private SettingsButton? _confirmOk;
    private SettingsButton? _confirmCancel;
    private Control? _confirmPreviousFocus;
    private readonly Win32Properties.CustomWndProcHookCallback _wndProcHook;
    private bool _shellInitialized;
    private bool _hasShownPage;
    private bool _wndProcHookAttached;
    private int? _nativeCornerPreference;
    private SettingsPalette? _palette;

    private enum SettingsWindowSizeProfile
    {
        Standard,
        Compact
    }

#if DEBUG
    private bool _hotReloadSubscriptionsAttached;
    private bool _hasTrackedAXAMLWindowDimensions;
    private SettingsWindowSizeProfile _hotReloadSizeProfile;
    private double _axamlWindowWidth;
    private double _axamlWindowHeight;
    private double _axamlWindowMinWidth;
    private double _axamlWindowMinHeight;
#endif

    protected TPageKey CurrentPageKey { get; private set; } = default!;
    protected bool IsClosing { get; private set; }

    /// <summary>Gets the source-level control naming scope for this settings-window instance.</summary>
    protected ControlNameScope ControlNames { get; }

    protected SettingsPalette Palette => _palette ??= ResolvePalette();

    /// <summary>Gets the footer actions owned by the currently committed or building shell.</summary>
    protected IReadOnlyList<SettingsNavItem> SidebarFooterActions => _sidebarFooterActions;
    protected abstract bool EnableRoundedCorners { get; }
    protected abstract TPageKey DefaultPageKey { get; }
    protected abstract string HeaderText { get; }
    protected abstract string OpenSettingsFolderText { get; }
    protected abstract string SettingsFolderPath { get; }
    protected abstract SettingsPalette ResolvePalette();
    protected abstract IReadOnlyList<SettingsPageDescriptor<TPageKey>> CreatePageDescriptors();
    protected abstract void Save();

    /// <summary>Builds the navigation sidebar for this window.</summary>
    protected virtual SettingsSidebar BuildSidebar() => new(
        _settingsResources.AxamlSettingsWindow.HeaderMargin,
        _settingsResources.AxamlSettingsWindow.NavMargin,
        _settingsResources.AxamlSettingsWindow.FooterMargin);

    /// <summary>Builds the content shown in the sidebar's application-header row.</summary>
    protected virtual Control BuildSidebarHeader(TextBlock title, SettingsPalette palette) => title;

    /// <summary>Updates the sidebar header without changing its measured geometry.</summary>
    protected virtual void UpdateSidebarHeader(Control sidebarHeader, bool usesCompactRail)
    {
        sidebarHeader.Opacity = usesCompactRail ? 0 : 1;
        sidebarHeader.IsHitTestVisible = !usesCompactRail;
    }

    /// <summary>Builds optional content floated over the sidebar column without affecting its layout.</summary>
    protected virtual Control? BuildSidebarOverlay(SettingsPalette palette) => null;

    /// <summary>Updates floated sidebar content after its effective collapse state changes.</summary>
    protected virtual void UpdateSidebarOverlay(Control sidebarOverlay, bool isCollapsed)
    {
    }

    /// <summary>Builds optional action rows placed before the sidebar's page-navigation rows.</summary>
    protected virtual IReadOnlyList<SettingsNavItem> CreateSidebarNavigationActions(SettingsPalette palette) => [];

    /// <summary>Builds optional action rows placed before the sidebar's footer page-navigation rows.</summary>
    protected virtual IReadOnlyList<SettingsNavItem> CreateSidebarFooterActions(SettingsPalette palette) => [];

    /// <summary>Updates optional sidebar actions after the effective collapse state changes.</summary>
    protected virtual void UpdateSidebarNavigationActions(
        IReadOnlyList<SettingsNavItem> navigationActions,
        bool isCollapsed)
    {
    }

    protected virtual Color ConfirmOverlayBackdrop =>
        AppTheme.Default.FlyoutOverlayBackdrop.For(AppTheme.Default.IsLightTheme);

    protected virtual bool UseProminentConfirmationDialog => false;

    /// <summary>Notifies derived windows when the in-window confirmation overlay opens or closes.</summary>
    protected virtual void OnConfirmOverlayVisibilityChanged(bool isVisible)
    {
    }

    protected virtual double SidebarWidth => _settingsResources.AxamlSettingsWindow.DefaultSidebarWidth;

    /// <summary>Gets the retained navigation-rail width while the sidebar is collapsed.</summary>
    protected virtual double CollapsedSidebarWidth => 0;

    /// <summary>Gets the app settings object that owns the optional custom navigation width.</summary>
    protected virtual ISettingsSidebarWidthSettings? SidebarWidthSettings => null;

    protected virtual Thickness ContentPadding => _settingsResources.AxamlSettingsWindow.ScrollHostMargin;
    protected virtual bool UseWindows11SettingsNavigation => false;
    protected virtual bool ShowSettingsSearchBox => true;
    protected virtual bool UseExtendedTitleBarDragZone => true;
    protected virtual bool PageContentExtendsIntoTitleBar => false;
    protected virtual bool UsePageContentTitleBarDragZone => true;
    protected virtual bool IsFooterNavigationPage(TPageKey pageKey) => false;
    protected virtual bool PageOwnsScrolling(TPageKey pageKey) => false;

    /// <summary>Gets the width reserved by the minimize, maximize, and close buttons.</summary>
    protected double TitleBarCaptionButtonAreaWidth =>
        CaptionButtonCount * _settingsResources.AxamlSettingsWindow.CaptionButtonWidth;

    /// <summary>Returns page content that must render in the shell overlay instead of its content column.</summary>
    protected virtual Control? ResolvePageOverlay(Control pageRoot) => null;

    /// <summary>Returns whether the page overlay should exclude the visible navigation sidebar.</summary>
    protected virtual bool PageOverlayAlignsToContentArea(Control pageRoot) => false;

    /// <summary>Gets whether the navigation sidebar automatically collapses below its threshold.</summary>
    protected virtual bool EnableResponsiveSidebarCollapse => false;

    /// <summary>Gets the window width below which the navigation sidebar is collapsed.</summary>
    protected virtual double SidebarCollapseThreshold => 0;

#if DEBUG
    /// <summary>Gets whether common AXAML owns this window's dimensions during source reload.</summary>
    protected virtual bool ApplyCommonAXAMLWindowDimensionsOnReload => true;

    /// <summary>Captures derived-window state before hot reload replaces the settings shell.</summary>
    protected virtual void OnBeforeHotReloadShellRebuild()
    {
    }

    /// <summary>Restores derived-window state after hot reload replaces the settings shell.</summary>
    protected virtual void OnAfterHotReloadShellRebuild()
    {
    }
#endif

    /// <summary>Toggles an explicit sidebar state that overrides responsive collapsing for this window.</summary>
    protected void ToggleSidebarCollapse()
    {
        _sidebarCollapseOverride = !ResolveSidebarCollapsed(ResolveWindowWidth());
        UpdateSidebarLayout();
    }

    /// <summary>Reapplies sidebar controls whose visuals depend on live resources.</summary>
    protected void RefreshSidebarCollapseControls()
    {
        _sidebarControlsCollapsedState = null;
        UpdateSidebarLayout();
    }

    /// <summary>Returns true when a derived window handled navigation without replacing its content.</summary>
    protected virtual bool HandleNavigationRequest(TPageKey pageKey) => false;

    protected SettingsWindowCommon()
    {
        _settingsResources = new SettingsWindowCommonResources();
        _commonBindingResources = new CommonBindingsResources();
#if DEBUG
        SynchronizeCommonAXAMLResources();
#endif
        _windowResources = new UIResourceScope(GetType().Name);
        ControlNames = ControlNameScope.For(this);
        Resources.MergedDictionaries.Add(_settingsResources);
        Resources.MergedDictionaries.Add(_commonBindingResources);
        _wndProcHook = WndProcHook;
        Opened += OnWindowOpened;
        Closed += OnWindowClosed;
        Deactivated += OnWindowDeactivated;
        PositionChanged += OnWindowPositionChanged;
        Resized += OnWindowResized;
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
#if DEBUG
        _windowResources.Add(DetachHotReloadSubscriptions);
#endif
        _windowResources.Add(() => RemoveHandler(KeyUpEvent, OnWindowKeyUp));
        _windowResources.Add(() => RemoveHandler(KeyDownEvent, OnWindowKeyDown));
        _windowResources.Add(() => RemoveHandler(PointerMovedEvent, OnWindowPointerMoved));
        _windowResources.Add(() => RemoveHandler(PointerPressedEvent, OnWindowPointerPressed));
        _windowResources.Add(DetachWndProcHook);
        _windowResources.Add(() => Resized -= OnWindowResized);
        _windowResources.Add(() => PositionChanged -= OnWindowPositionChanged);
        _windowResources.Add(() => Deactivated -= OnWindowDeactivated);
        _windowResources.Add(() => Closed -= OnWindowClosed);
        _windowResources.Add(() => Opened -= OnWindowOpened);
    }

    protected void ConfigureSettingsWindow(string title, WindowIcon? icon) =>
        ConfigureSettingsWindow(title, SettingsWindowSizeProfile.Standard, icon);

    protected void ConfigureCompactSettingsWindow(string title, WindowIcon? icon) =>
        ConfigureSettingsWindow(title, SettingsWindowSizeProfile.Compact, icon);

    private void ConfigureSettingsWindow(string title, SettingsWindowSizeProfile sizeProfile, WindowIcon? icon)
    {
        Title = title;
        ApplyWindowDimensions(sizeProfile);
        Icon = icon;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // BorderOnly supplies native resize hit testing while the extended client area retains custom chrome
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        CanResize = true;
    }

    private void ApplyWindowDimensions(SettingsWindowSizeProfile sizeProfile)
    {
        switch (sizeProfile)
        {
            case SettingsWindowSizeProfile.Standard:
                Width = _settingsResources.AxamlSettingsWindow.StandardWindowWidth;
                Height = _settingsResources.AxamlSettingsWindow.StandardWindowHeight;
                MinWidth = _settingsResources.AxamlSettingsWindow.StandardWindowMinWidth;
                MinHeight = _settingsResources.AxamlSettingsWindow.StandardWindowMinHeight;
#if DEBUG
                TrackAXAMLWindowDimensions(sizeProfile);
#endif
                return;

            case SettingsWindowSizeProfile.Compact:
                Width = _settingsResources.AxamlSettingsWindow.CompactWindowWidth;
                Height = _settingsResources.AxamlSettingsWindow.CompactWindowHeight;
                MinWidth = _settingsResources.AxamlSettingsWindow.CompactWindowMinWidth;
                MinHeight = _settingsResources.AxamlSettingsWindow.CompactWindowMinHeight;
#if DEBUG
                TrackAXAMLWindowDimensions(sizeProfile);
#endif
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(sizeProfile), sizeProfile, message: null);
        }
    }

    protected void InitializeSettingsShell()
    {
        if (_shellInitialized) return;

        BuildAndCommitShell(DefaultPageKey);
        _shellInitialized = true;
#if DEBUG
        AttachHotReloadSubscriptions();
#endif
    }

    protected virtual void OnSettingsWindowClosed()
    {
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        CancelPendingConfirm();
        _confirmPreviousFocus = GetTopLevel(this)?.FocusManager.GetFocusedElement() as Control;
        _confirmTitle!.Text = title;
        _confirmMessage!.Text = message;
        _confirmOk!.Text = confirmText;
        _confirmCancel!.Text = cancelText;
        _confirmTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetConfirmOverlayVisible(true);
        _confirmOk.Focus();
        return _confirmTcs.Task;
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (FocusManager.GetFocusedElement() is not TextBox focusedTextBox) return;

        SettingsNumberBox? numberBox = focusedTextBox.GetVisualAncestors()
            .OfType<SettingsNumberBox>()
            .FirstOrDefault();
        Visual editorBoundary = numberBox is not null ? numberBox : focusedTextBox;
        if (eventArgs.Source is Visual source
            && (ReferenceEquals(source, editorBoundary)
                || source.GetVisualAncestors().Any(ancestor => ReferenceEquals(ancestor, editorBoundary))))
            return;

        TrayAppDotNETSettingsUI.BlurTextEditor(focusedTextBox);
    }

    public void ShowAtDefaultPositionAndActivate()
    {
        if (!IsVisible)
        {
            Show();
            BringToForeground();
            return;
        }

        RestoreForDefaultPosition();
        MoveToDefaultPosition();
        BringToForeground();
    }

    /// <summary>Shows a cold window only after its first compositor frame contains the completed shell.</summary>
    public void ShowAtDefaultPositionAndActivateAfterFirstFrame() =>
        _ = ShowAtDefaultPositionAndActivateAfterFirstFrameAsync();

    /// <summary>Shows and activates the window, completing after its first-frame reveal and foreground sequence.</summary>
    public Task ShowAtDefaultPositionAndActivateAfterFirstFrameAsync()
    {
        if (IsVisible)
        {
            ShowAtDefaultPositionAndActivate();
            return Task.CompletedTask;
        }

        IntPtr windowHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        bool isCloaked = TrySetNativeWindowCloak(windowHandle, isCloaked: true);
        double restoredOpacity = Opacity;
        if (!isCloaked) Opacity = 0;

        try
        {
            Show();
        }
        catch
        {
            if (isCloaked)
                _ = TrySetNativeWindowCloak(windowHandle, isCloaked: false);
            else
                Opacity = restoredOpacity;
            throw;
        }

        return RevealAfterFirstFrameAsync(windowHandle, isCloaked, restoredOpacity);
    }

    private async Task RevealAfterFirstFrameAsync(
        IntPtr windowHandle,
        bool isCloaked,
        double restoredOpacity)
    {
        try
        {
            // Loaded runs after layout and UI-thread render-data generation. Waiting for the resulting
            // compositor batch prevents Windows from exposing its blank initial surface.
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            CompositionVisual? windowVisual = ElementComposition.GetElementVisual(this);
            if (windowVisual != null)
            {
                CompositionBatch batch = windowVisual.Compositor.RequestCompositionBatchCommitAsync();
                await batch.Rendered.ConfigureAwait(false);
            }

            if (isCloaked) _ = DWMAPI.DwmFlush();
        }
        catch (Exception exception)
        {
            TADNLog.Log(
                $"{GetType().Name}.RevealAfterFirstFrameAsync failed: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }

        await Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                if (isCloaked)
                    _ = TrySetNativeWindowCloak(windowHandle, isCloaked: false);
                else
                    Opacity = restoredOpacity;
                if (IsVisible) BringToForeground();
            },
            DispatcherPriority.Send);
    }

    private static bool TrySetNativeWindowCloak(IntPtr windowHandle, bool isCloaked)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == IntPtr.Zero) return false;

        int cloakValue = isCloaked ? 1 : 0;
        return DWMAPI.DwmSetWindowAttribute(
            windowHandle,
            DWMAPI.DWMWA_CLOAK,
            ref cloakValue,
            sizeof(int)) == 0;
    }

    protected void SelectPage(TPageKey key) => NavigateToSettingsPage(key);

    protected void RefreshCurrentPage()
    {
        if (_isShowingSettingsSearch && !string.IsNullOrWhiteSpace(_settingsSearchQuery))
        {
            RebuildShell(CurrentPageKey);
            return;
        }

        ShowPage(CurrentPageKey, force: true);
    }

    protected void RebuildShell(TPageKey selectedPageKey)
    {
        if (_hasShownPage && !_isShowingSettingsSearch && _scrollHost != null)
            _pageScrollOffsets[CurrentPageKey] = _scrollHost.VerticalOffset;

        string searchQuery = _settingsSearchQuery;
        bool restoreSearch = _isShowingSettingsSearch && !string.IsNullOrWhiteSpace(searchQuery);
        SettingsSearchView? previousSearchView = _settingsSearchView;
        _isShowingSettingsSearch = false;
        _settingsSearchView = null;
        _settingsSearchQuery = string.Empty;

        RefreshPalette();
        try
        {
            BuildAndCommitShell(selectedPageKey);
        }
        catch
        {
            _isShowingSettingsSearch = restoreSearch;
            _settingsSearchView = previousSearchView;
            _settingsSearchQuery = searchQuery;
            throw;
        }

        if (restoreSearch)
            RestoreSettingsSearchAfterShellRebuild(searchQuery);
    }

#if DEBUG
    /// <summary>
    /// Rebuilds code-created settings glyphs after a catalog source reload.
    /// </summary>
    private void OnGlyphCatalogResourcesReloaded()
    {
        if (IsClosing || !_shellInitialized) return;

        OnBeforeHotReloadShellRebuild();
        RebuildShell(CurrentPageKey);
        OnAfterHotReloadShellRebuild();
    }

    /// <summary>Rebuilds the open shell after a common layout dictionary reloads.</summary>
    private void OnCommonAXAMLResourcesReloaded()
    {
        if (IsClosing || !_shellInitialized) return;

        SynchronizeCommonAXAMLResources();
        ApplyChangedAXAMLWindowDimensions();
        OnBeforeHotReloadShellRebuild();
        RebuildShell(CurrentPageKey);
        OnAfterHotReloadShellRebuild();
    }

    private void AttachHotReloadSubscriptions()
    {
        if (_hotReloadSubscriptionsAttached || IsClosing) return;

        CommonAXAMLHotReload.ResourcesReloaded += OnCommonAXAMLResourcesReloaded;
        try
        {
            GlyphCatalogHotReload.ResourcesReloaded += OnGlyphCatalogResourcesReloaded;
            _hotReloadSubscriptionsAttached = true;
        }
        catch
        {
            CommonAXAMLHotReload.ResourcesReloaded -= OnCommonAXAMLResourcesReloaded;
            throw;
        }
    }

    private void DetachHotReloadSubscriptions()
    {
        if (!_hotReloadSubscriptionsAttached) return;

        _hotReloadSubscriptionsAttached = false;
        GlyphCatalogHotReload.ResourcesReloaded -= OnGlyphCatalogResourcesReloaded;
        CommonAXAMLHotReload.ResourcesReloaded -= OnCommonAXAMLResourcesReloaded;
    }

    private void TrackAXAMLWindowDimensions(SettingsWindowSizeProfile sizeProfile)
    {
        _hotReloadSizeProfile = sizeProfile;
        _hasTrackedAXAMLWindowDimensions = true;
        switch (sizeProfile)
        {
            case SettingsWindowSizeProfile.Standard:
                _axamlWindowWidth = _settingsResources.AxamlSettingsWindow.StandardWindowWidth;
                _axamlWindowHeight = _settingsResources.AxamlSettingsWindow.StandardWindowHeight;
                _axamlWindowMinWidth = _settingsResources.AxamlSettingsWindow.StandardWindowMinWidth;
                _axamlWindowMinHeight = _settingsResources.AxamlSettingsWindow.StandardWindowMinHeight;
                return;

            case SettingsWindowSizeProfile.Compact:
                _axamlWindowWidth = _settingsResources.AxamlSettingsWindow.CompactWindowWidth;
                _axamlWindowHeight = _settingsResources.AxamlSettingsWindow.CompactWindowHeight;
                _axamlWindowMinWidth = _settingsResources.AxamlSettingsWindow.CompactWindowMinWidth;
                _axamlWindowMinHeight = _settingsResources.AxamlSettingsWindow.CompactWindowMinHeight;
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(sizeProfile), sizeProfile, message: null);
        }
    }

    private void ApplyChangedAXAMLWindowDimensions()
    {
        if (!ApplyCommonAXAMLWindowDimensionsOnReload) return;

        if (!_hasTrackedAXAMLWindowDimensions) return;

        double width;
        double height;
        double minWidth;
        double minHeight;
        switch (_hotReloadSizeProfile)
        {
            case SettingsWindowSizeProfile.Standard:
                width = _settingsResources.AxamlSettingsWindow.StandardWindowWidth;
                height = _settingsResources.AxamlSettingsWindow.StandardWindowHeight;
                minWidth = _settingsResources.AxamlSettingsWindow.StandardWindowMinWidth;
                minHeight = _settingsResources.AxamlSettingsWindow.StandardWindowMinHeight;
                break;

            case SettingsWindowSizeProfile.Compact:
                width = _settingsResources.AxamlSettingsWindow.CompactWindowWidth;
                height = _settingsResources.AxamlSettingsWindow.CompactWindowHeight;
                minWidth = _settingsResources.AxamlSettingsWindow.CompactWindowMinWidth;
                minHeight = _settingsResources.AxamlSettingsWindow.CompactWindowMinHeight;
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(_hotReloadSizeProfile),
                    _hotReloadSizeProfile,
                    message: null);
        }

        if (!_axamlWindowWidth.Equals(width)) Width = width;
        if (!_axamlWindowHeight.Equals(height)) Height = height;
        if (!_axamlWindowMinWidth.Equals(minWidth)) MinWidth = minWidth;
        if (!_axamlWindowMinHeight.Equals(minHeight)) MinHeight = minHeight;

        _axamlWindowWidth = width;
        _axamlWindowHeight = height;
        _axamlWindowMinWidth = minWidth;
        _axamlWindowMinHeight = minHeight;
    }

    private void SynchronizeCommonAXAMLResources()
    {
        CommonAXAMLHotReload.SynchronizeResources(
            _settingsResources,
            SettingsWindowCommonResources.Current);
        CommonAXAMLHotReload.SynchronizeResources(
            _commonBindingResources,
            CommonBindingsResources.Current);
    }
#endif

    /// <summary>
    /// Applies current theme colors through the existing shared brushes.
    /// </summary>
    protected void RefreshPalette()
    {
        SettingsPalette resolved = ResolvePalette();
        if (_palette == null)
        {
            _palette = resolved;
            return;
        }

        if (!ReferenceEquals(_palette, resolved))
            _palette.UpdateFrom(resolved);

        foreach (SettingsNavItem navigationItem in _navItems.Values)
            navigationItem.RefreshPalette();
        foreach (SettingsNavItem navigationAction in _sidebarNavigationActions)
            navigationAction.RefreshPalette();
        foreach (SettingsNavItem footerAction in _sidebarFooterActions)
            footerAction.RefreshPalette();
    }

    /// <summary>Adds cleanup owned by the settings page currently being constructed.</summary>
    protected void AddPageCleanup(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        UIResourceScope? resources = _buildingPageResources;
        if (resources == null)
            throw new InvalidOperationException("Page cleanup can only be registered while building a page.");

        resources.Add(cleanup);
    }

    /// <summary>Registers and returns a disposable resource owned by the page being constructed.</summary>
    protected T OwnPageResource<T>(T resource)
        where T : IDisposable
    {
        UIResourceScope? resources = _buildingPageResources;
        return resources == null
            ? throw new InvalidOperationException("Page resources can only be registered while building a page.")
            : resources.Own(resource);
    }

    protected static string L(string key) => LocalizationManager.Instance[key];

    protected static string Loc(string key) => L(key);

    protected CornerRadius RadiusTiny => RoundedCornerRadius(_settingsResources.AxamlSettingsWindow.RadiusTiny);
    protected CornerRadius RadiusMedium => RoundedCornerRadius(_settingsResources.AxamlSettingsWindow.RadiusMedium);
    protected CornerRadius RadiusLarge => RoundedCornerRadius(_settingsResources.AxamlSettingsWindow.RadiusLarge);

    protected StackPanel PageStack(string title, SettingsPalette palette) =>
        TrayAppDotNETSettingsCards.PageStack(title, palette);

    protected SettingsButton Button(string text, SettingsPalette palette) =>
        TrayAppDotNETSettingsCards.Button(text, palette, RadiusMedium);

    protected SettingsButton Button(Glyph glyph, SettingsPalette palette) =>
        TrayAppDotNETSettingsCards.Button(glyph, palette, RadiusMedium);

    protected Border BoolCard(
        string title,
        string description,
        bool value,
        Action<bool> set,
        SettingsPalette palette,
        Action? afterSave = null,
        IReadOnlyList<string>? searchKeywords = null) =>
        TrayAppDotNETSettingsCards.BoolCard(
            title,
            description,
            value,
            set,
            palette,
            RadiusLarge,
            Save,
            afterSave,
            searchKeywords);

    protected Border IntCard(
        string title,
        string description,
        int value,
        int min,
        int max,
        Action<int> set,
        SettingsPalette palette,
        string suffix = "",
        IReadOnlyList<string>? searchKeywords = null) =>
        TrayAppDotNETSettingsCards.IntCard(
            title,
            description,
            value,
            min,
            max,
            set,
            palette,
            RadiusLarge,
            Save,
            suffix,
            searchKeywords);

    protected Border DoubleCard(
        string title,
        string description,
        double value,
        double min,
        double max,
        Action<double> set,
        SettingsPalette palette,
        string suffix = "",
        IReadOnlyList<string>? searchKeywords = null,
        int decimalPlaces = 1,
        double step = 0.1) =>
        TrayAppDotNETSettingsCards.DoubleCard(
            title,
            description,
            value,
            min,
            max,
            set,
            palette,
            RadiusLarge,
            Save,
            suffix,
            searchKeywords,
            decimalPlaces,
            step);

    protected Border ComboCard(
        string title,
        string description,
        IReadOnlyList<(string Tag, string Text)> items,
        string selectedTag,
        Action<string> set,
        SettingsPalette palette,
        Action? afterSave = null,
        bool autoSizeToText = false,
        SettingsComboBoxAutoSizeMode autoSizeMode = SettingsComboBoxAutoSizeMode.LongestItem,
        IReadOnlyList<string>? searchKeywords = null) =>
        TrayAppDotNETSettingsCards.ComboCard(
            title,
            description,
            items,
            selectedTag,
            set,
            palette,
            RadiusLarge,
            Save,
            afterSave,
            autoSizeToText,
            autoSizeMode,
            searchKeywords);

    protected Border Card(
        string title,
        string description,
        Control? rightControl,
        SettingsPalette palette,
        IReadOnlyList<string>? searchKeywords = null) =>
        TrayAppDotNETSettingsCards.Card(title, description, rightControl, palette, RadiusLarge, searchKeywords);

    protected Border RawCard(
        Control content,
        SettingsPalette palette,
        IReadOnlyList<string>? searchKeywords = null) =>
        TrayAppDotNETSettingsCards.RawCard(content, palette, RadiusLarge, searchKeywords);

    protected Border MutableCard(
        string title,
        string description,
        Control? rightControl,
        SettingsPalette palette,
        out TextBlock descriptionText,
        IReadOnlyList<string>? searchKeywords = null) =>
        TrayAppDotNETSettingsCards.MutableCard(
            title,
            description,
            rightControl,
            palette,
            RadiusLarge,
            out descriptionText,
            searchKeywords);

    protected Task ShowMessage(string title, string message) =>
        ConfirmAsync(title, message, confirmText: "OK", cancelText: "OK");

    private Border BuildRoot()
    {
        SettingsPalette palette = Palette;
        _pageDescriptors = CreatePageDescriptors();
        _pages.Clear();
        _navItems.Clear();

        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition(
            new GridLength(_settingsResources.AxamlSettingsWindow.TitleBarHeight)));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        Grid body = new();
        _currentSidebarWidth = ResolveConfiguredSidebarWidth();
        _sidebarColumn = new ColumnDefinition(new GridLength(_currentSidebarWidth));
        body.ColumnDefinitions.Add(_sidebarColumn);
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        Grid.SetRow(body, PageContentExtendsIntoTitleBar ? 0 : 1);
        if (PageContentExtendsIntoTitleBar)
            Grid.SetRowSpan(body, value: 2);
        root.Children.Add(body);

        _sidebar = BuildSidebar();
        _sidebar.Background = Brushes.Transparent;
        _sidebar.Margin = PageContentExtendsIntoTitleBar
            ? new Thickness(left: 0, _settingsResources.AxamlSettingsWindow.TitleBarHeight, right: 0, bottom: 0)
            : default;
        Grid.SetColumn(_sidebar, value: 0);
        body.Children.Add(_sidebar);

        TextBlock headerTitle = TrayAppDotNETSettingsUI.Text(
            HeaderText,
            palette,
            _settingsResources.AxamlSettingsWindow.HeaderFontSize,
            FontWeight.SemiBold);
        _sidebarHeader = BuildSidebarHeader(headerTitle, palette);
        _sidebar.SetHeader(_sidebarHeader);

        StackPanel nav = _sidebar.Navigation;
        StackPanel footer = _sidebar.Footer;
        _sidebarNavigationActions = CreateSidebarNavigationActions(palette);
        foreach (SettingsNavItem navigationAction in _sidebarNavigationActions)
            nav.Children.Add(navigationAction);
        _sidebarFooterActions = CreateSidebarFooterActions(palette);
        foreach (SettingsNavItem footerAction in _sidebarFooterActions)
            footer.Children.Add(footerAction);

        foreach (SettingsPageDescriptor<TPageKey> page in _pageDescriptors)
        {
            _pages[page.Key] = page.BuildPage;
            AddNavItem(IsFooterNavigationPage(page.Key) ? footer : nav, page, palette);
        }

        if (ShowSettingsSearchBox)
        {
            _settingsSearchBox = new SettingsSearchBox(
                palette,
                L(nameof(CommonStrings.SettingsWindow_SearchPlaceholder)));
            _settingsSearchBox.SearchTextChanged += OnSettingsSearchTextChanged;
            footer.Children.Add(_settingsSearchBox);
        }

        _scrollHost = TrayAppDotNETSettingsUI.ScrollHost(
            _content,
            palette,
            ContentPadding);
        Grid.SetColumn(_scrollHost, value: 1);
        body.Children.Add(_scrollHost);

        ISettingsSidebarWidthSettings? sidebarWidthSettings = SidebarWidthSettings;
        if (sidebarWidthSettings != null)
        {
            _sidebarResizeHandle = new SettingsSidebarResizeHandle(
                body,
                _settingsResources.AxamlSettingsWindow.SidebarResizeHitTargetWidth,
                _settingsResources.AxamlSettingsWindow.SidebarMinimumWidth,
                GetDisplayedSidebarWidth,
                GetAvailableSidebarMaximumWidth,
                PreviewSidebarWidth,
                PersistSidebarWidth,
                ResetSidebarWidth);
            if (PageContentExtendsIntoTitleBar)
            {
                _sidebarResizeHandle.Margin = new Thickness(
                    left: 0,
                    _settingsResources.AxamlSettingsWindow.TitleBarHeight,
                    right: 0,
                    bottom: 0);
            }

            Grid.SetColumn(_sidebarResizeHandle, value: 0);
            body.Children.Add(_sidebarResizeHandle);
        }

        _sidebarOverlay = BuildSidebarOverlay(palette);
        _sidebarControlsCollapsedState = null;
        if (_sidebarOverlay != null)
        {
            _sidebarOverlay.Margin = _sidebar.Margin;
            Grid.SetColumn(_sidebarOverlay, value: 0);
            body.Children.Add(_sidebarOverlay);
        }

        _pageOverlayHost = new Grid();
        Grid.SetRow(_pageOverlayHost, value: 0);
        Grid.SetRowSpan(_pageOverlayHost, value: 2);
        root.Children.Add(_pageOverlayHost);

        Control titleBar = BuildTitleBar(palette);
        Grid.SetRow(titleBar, value: 0);
        Grid.SetRowSpan(titleBar, value: 2);
        root.Children.Add(titleBar);

        _confirmOverlay = BuildConfirmOverlay();
        _confirmOverlay.IsVisible = false;
        Grid.SetRow(_confirmOverlay, UseProminentConfirmationDialog ? 0 : 1);
        Grid.SetRowSpan(_confirmOverlay, UseProminentConfirmationDialog ? 2 : 1);
        root.Children.Add(_confirmOverlay);

        UpdateSidebarLayout();

        CornerRadius outerRadius = RoundedCornerRadius(_settingsResources.AxamlSettingsWindow.OuterCornerRadius);
        CornerRadius innerRadius = RoundedCornerRadius(_settingsResources.AxamlSettingsWindow.InnerCornerRadius);

        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            BorderThickness = EnableCustomWindowBorder
                ? _settingsResources.AxamlSettingsWindow.RootBorderThickness
                : _settingsResources.AxamlSettingsWindow.ZeroThickness,
            CornerRadius = outerRadius,
            ClipToBounds = false,
            Child = new Border
            {
                Background = TrayAppDotNETSettingsUI.Brush(palette.Background),
                CornerRadius = innerRadius,
                ClipToBounds = EnableRoundedCorners,
                Margin = EnableCustomWindowBorder
                    ? _settingsResources.AxamlSettingsWindow.InnerBorderMargin
                    : _settingsResources.AxamlSettingsWindow.ZeroThickness,
                Child = root
            }
        };
    }

    private Grid BuildTitleBar(SettingsPalette palette)
    {
        Grid titleBar = new()
        {
            Background = PageContentExtendsIntoTitleBar ? null : Brushes.Transparent,
            Height = UseExtendedTitleBarDragZone
                ? _settingsResources.AxamlSettingsWindow.TitleBarDragZoneHeight
                : _settingsResources.AxamlSettingsWindow.TitleBarHeight,
            VerticalAlignment = VerticalAlignment.Top
        };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        if (PageContentExtendsIntoTitleBar && UsePageContentTitleBarDragZone)
        {
            _titleBarDragZone = new Border
            {
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            AttachTitleBarDrag(_titleBarDragZone);
            titleBar.Children.Add(_titleBarDragZone);
        }
        else
            AttachTitleBarDrag(titleBar);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        SettingsButton minimize = CaptionButton(
            GlyphCatalog.CHROME_MINIMIZE,
            palette,
            minimizeButton: true);
        SettingsButton maximize = CaptionButton(
            WindowState == WindowState.Maximized
                ? GlyphCatalog.CHROME_RESTORE
                : GlyphCatalog.CHROME_MAXIMIZE,
            palette);
        SettingsButton close = CaptionButton(GlyphCatalog.CHROME_CLOSE, palette, closeButton: true);
        SetCaptionButtonTip(minimize, L(nameof(CommonStrings.SettingsWindow_Caption_Minimize)));
        SetCaptionButtonTip(maximize, L(nameof(CommonStrings.SettingsWindow_Caption_Maximize)));
        SetCaptionButtonTip(close, L(nameof(CommonStrings.Common_Close)));
        minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        maximize.Click += (_, _) => ToggleMaximize();
        close.Click += (_, _) => Close();
        buttons.Children.Add(minimize);
        buttons.Children.Add(maximize);
        buttons.Children.Add(close);
        Grid.SetColumn(buttons, value: 1);
        titleBar.Children.Add(buttons);
        return titleBar;
    }

    private void AttachTitleBarDrag(Control dragControl)
    {
        dragControl.PointerPressed += (_, eventArgs) =>
        {
            if (eventArgs.Source is SettingsButton) return;
            if (!eventArgs.GetCurrentPoint(dragControl).Properties.IsLeftButtonPressed) return;
            if (eventArgs.ClickCount == 2) ToggleMaximize();
            else BeginMoveDrag(eventArgs);
        };
    }

    private void ApplyPageOverlay(Control? pageRoot)
    {
        Grid? overlayHost = _pageOverlayHost;
        if (overlayHost == null) return;

        Control? overlay = pageRoot == null ? null : ResolvePageOverlay(pageRoot);
        _pageOverlayAlignsToContentArea = overlay != null
                                          && pageRoot != null
                                          && PageOverlayAlignsToContentArea(pageRoot);
        UpdatePageOverlayLayout();
        overlayHost.Children.Clear();
        if (overlay != null)
        {
            ControlNames.AssignLogicalSubtree(overlay, this);
            overlayHost.Children.Add(overlay);
        }
    }

    private SettingsButton CaptionButton(
        Glyph glyph,
        SettingsPalette palette,
        bool closeButton = false,
        bool minimizeButton = false)
    {
        SettingsButton button = new(glyph, palette, transparentBase: true)
        {
            Width = _settingsResources.AxamlSettingsWindow.CaptionButtonWidth,
            Height = _settingsResources.AxamlSettingsWindow.CaptionButtonHeight,
            CornerRadius = _settingsResources.AxamlSettingsWindow.ZeroCornerRadius,
            Padding = _settingsResources.AxamlSettingsWindow.ZeroThickness,
            IsSettingsWindowCloseButton = closeButton,
            IsSettingsWindowMinimizeButton = minimizeButton,
            Label = { FontSize = _settingsResources.AxamlSettingsWindow.CaptionButtonGlyphFontSize }
        };
        if (closeButton)
        {
            button.PointerEntered += (_, _) =>
            {
                button.Background = TrayAppDotNETSettingsUI.Brush(palette.CloseButtonHover);
                button.Label.Foreground = TrayAppDotNETSettingsUI.Brush(palette.CloseButtonGlyphActive);
            };
            button.PointerExited += (_, _) =>
            {
                button.Background = Brushes.Transparent;
                button.Label.Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground);
            };
        }

        return button;
    }

    private static void SetCaptionButtonTip(SettingsButton button, string text)
    {
        TrayAppDotNETToolTip.SetTip(button, text);
        TrayAppDotNETToolTip.SuppressWhileEngaged(button);
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void RestoreForDefaultPosition()
    {
        if (OperatingSystem.IsWindows())
        {
            IntPtr hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero && User32.IsIconic(hwnd))
                _ = User32.ShowWindow(hwnd, User32.SW_RESTORE);
        }

        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;
    }

    private void MoveToDefaultPosition()
    {
        UpdateLayout();

        PixelRect workArea = Screens.Primary?.WorkingArea
                             ?? Screens.ScreenFromPoint(Position)?.WorkingArea
                             ?? new PixelRect(
                                 _settingsResources.AxamlSettingsWindow.FallbackWorkAreaX,
                                 _settingsResources.AxamlSettingsWindow.FallbackWorkAreaY,
                                 _settingsResources.AxamlSettingsWindow.FallbackWorkAreaWidth,
                                 _settingsResources.AxamlSettingsWindow.FallbackWorkAreaHeight);
        int pixelMinSize = (int)Math.Round(_settingsResources.AxamlSettingsWindow.PixelMinSize);
        int width = Math.Max(pixelMinSize, (int)Math.Ceiling(Math.Max(Bounds.Width, Width) * RenderScaling));
        int height = Math.Max(pixelMinSize, (int)Math.Ceiling(Math.Max(Bounds.Height, Height) * RenderScaling));
        int left = workArea.X + Math.Max(val1: 0, workArea.Width - width) / 2;
        int top = workArea.Y + Math.Max(val1: 0, workArea.Height - height) / 2;

        Position = new PixelPoint(left, top);
    }

    private void BringToForeground()
    {
        if (OperatingSystem.IsWindows())
        {
            IntPtr hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero)
            {
                _ = User32.ShowWindow(hwnd, User32.SW_RESTORE);
                _ = User32.SetForegroundWindow(hwnd);
            }
        }

        Activate();
    }

    private void AddNavItem(
        StackPanel navigationPanel,
        SettingsPageDescriptor<TPageKey> page,
        SettingsPalette palette)
    {
        bool useWindows11Style = UseWindows11SettingsNavigation;
        Control? customNavigationIcon = useWindows11Style
            ? page.NavigationIconFactory?.Invoke(palette.Foreground)
            : null;
        SettingsNavItem item = new(
            page.Label,
            palette,
            RadiusTiny,
            RadiusMedium,
            useWindows11Style,
            page.NavigationGlyph,
            customNavigationIcon,
            page.NavigationIconScale,
            page.NavigationIconTransform);
        item.Click += (_, _) =>
        {
            if (item.IsSelected)
            {
                _pageScrollOffsets[page.Key] = 0;
                _scrollHost?.SetVerticalOffset(0);
                return;
            }

            if (!HandleNavigationRequest(page.Key))
                NavigateToSettingsPage(page.Key);
        };
        _navItems[page.Key] = item;
        navigationPanel.Children.Add(item);
    }

    private void ShowPage(TPageKey key, bool force = false)
    {
        if (!_pages.TryGetValue(key, out Func<Control>? factory)) return;
        if (!force
            && !_isShowingSettingsSearch
            && _hasShownPage
            && EqualityComparer<TPageKey>.Default.Equals(CurrentPageKey, key))
            return;

        if (_hasShownPage && !_isShowingSettingsSearch && _scrollHost != null)
            _pageScrollOffsets[CurrentPageKey] = _scrollHost.VerticalOffset;

        TPageKey previousPageKey = CurrentPageKey;
        bool previousHasShownPage = _hasShownPage;
        UIContentGeneration? previous = _pageGeneration;
        Control? previousRoot = previous?.Root;
        Dictionary<TPageKey, bool> previousNavSelections = [];
        foreach ((TPageKey navKey, SettingsNavItem item) in _navItems)
            previousNavSelections[navKey] = item.IsSelected;

        UIContentGeneration replacement;
        CurrentPageKey = key;
        try
        {
            replacement = BuildPageGeneration(key, factory);
        }
        catch
        {
            CurrentPageKey = previousPageKey;
            throw;
        }

        try
        {
            _content.Content = replacement.Root;
            ApplyPageOverlay(replacement.Root);
            _scrollHost?.SetContentScrollingEnabled(!PageOwnsScrolling(key));
            _pageGeneration = replacement;
            _settingsSearchView = null;
            _isShowingSettingsSearch = false;
            foreach ((TPageKey navKey, SettingsNavItem item) in _navItems)
                item.IsSelected = EqualityComparer<TPageKey>.Default.Equals(navKey, key);
            _hasShownPage = true;
        }
        catch (Exception exception)
        {
            CurrentPageKey = previousPageKey;
            _hasShownPage = previousHasShownPage;
            _pageGeneration = previous;
            RestorePageCommitState(previousRoot, previousNavSelections, exception);
            replacement.Dispose();
            throw;
        }

        previous?.Dispose();
        RestorePageScroll(key, !force, replacement.ID);
    }

    private void BuildAndCommitShell(TPageKey selectedPageKey)
    {
        CancelPendingConfirm();

        ContentControl previousContent = _content;
        IReadOnlyList<SettingsPageDescriptor<TPageKey>> previousPageDescriptors = _pageDescriptors;
        Dictionary<TPageKey, Func<Control>> previousPages = _pages;
        Dictionary<TPageKey, SettingsNavItem> previousNavItems = _navItems;
        SettingsScrollHost? previousScrollHost = _scrollHost;
        SettingsSidebar? previousSidebar = _sidebar;
        Control? previousSidebarHeader = _sidebarHeader;
        ColumnDefinition? previousSidebarColumn = _sidebarColumn;
        SettingsSidebarResizeHandle? previousSidebarResizeHandle = _sidebarResizeHandle;
        Control? previousSidebarOverlay = _sidebarOverlay;
        bool? previousSidebarControlsCollapsedState = _sidebarControlsCollapsedState;
        IReadOnlyList<SettingsNavItem> previousSidebarNavigationActions = _sidebarNavigationActions;
        IReadOnlyList<SettingsNavItem> previousSidebarFooterActions = _sidebarFooterActions;
        Grid? previousPageOverlayHost = _pageOverlayHost;
        bool previousPageOverlayAlignsToContentArea = _pageOverlayAlignsToContentArea;
        Border? previousTitleBarDragZone = _titleBarDragZone;
        double previousCurrentSidebarWidth = _currentSidebarWidth;
        SettingsSearchBox? previousSettingsSearchBox = _settingsSearchBox;
        Border? previousConfirmOverlay = _confirmOverlay;
        TextBlock? previousConfirmTitle = _confirmTitle;
        TextBlock? previousConfirmMessage = _confirmMessage;
        SettingsButton? previousConfirmOK = _confirmOk;
        SettingsButton? previousConfirmCancel = _confirmCancel;
        TPageKey previousPageKey = CurrentPageKey;
        bool previousHasShownPage = _hasShownPage;
        UIContentGeneration? previousShellGeneration = _shellGeneration;
        UIContentGeneration? previousPageGeneration = _pageGeneration;
        object? previousWindowContent = Content;

        UIContentGeneration? replacementShellGeneration = null;
        UIContentGeneration? replacementPageGeneration = null;
        Dictionary<TPageKey, Func<Control>> replacementPages = [];
        Dictionary<TPageKey, SettingsNavItem> replacementNavItems = [];

        try
        {
            _content = new ContentControl();
            _pageDescriptors = [];
            _pages = replacementPages;
            _navItems = replacementNavItems;
            _sidebarNavigationActions = [];
            _sidebarFooterActions = [];
            _scrollHost = null;
            _sidebar = null;
            _sidebarHeader = null;
            _sidebarColumn = null;
            _sidebarResizeHandle = null;
            _sidebarOverlay = null;
            _sidebarControlsCollapsedState = null;
            _pageOverlayHost = null;
            _pageOverlayAlignsToContentArea = false;
            _titleBarDragZone = null;
            _settingsSearchBox = null;
            _confirmOverlay = null;
            _confirmTitle = null;
            _confirmMessage = null;
            _confirmOk = null;
            _confirmCancel = null;
            CurrentPageKey = selectedPageKey;
            _hasShownPage = false;

            Border replacementRoot = BuildRoot();
            if (!_pages.TryGetValue(selectedPageKey, out Func<Control>? selectedPageFactory))
                throw new InvalidOperationException($"Settings page '{selectedPageKey}' is not registered.");

            replacementPageGeneration = BuildPageGeneration(selectedPageKey, selectedPageFactory);
            _content.Content = replacementPageGeneration.Root;
            ApplyPageOverlay(replacementPageGeneration.Root);
            _scrollHost?.SetContentScrollingEnabled(!PageOwnsScrolling(selectedPageKey));
            foreach ((TPageKey navKey, SettingsNavItem item) in _navItems)
                item.IsSelected = EqualityComparer<TPageKey>.Default.Equals(navKey, selectedPageKey);
            _hasShownPage = true;

            UIResourceScope shellResources = new($"{GetType().Name}.Shell");
            shellResources.Add(replacementNavItems.Clear);
            shellResources.Add(replacementPages.Clear);
            if (_scrollHost != null)
                shellResources.Own(_scrollHost);
            if (_sidebarResizeHandle != null)
                shellResources.Own(_sidebarResizeHandle);
            if (_settingsSearchBox != null)
                shellResources.Own(_settingsSearchBox);
            ControlNames.AssignLogicalSubtree(replacementRoot, this);
            replacementShellGeneration = new UIContentGeneration(
                $"{GetType().Name}.Shell",
                replacementRoot,
                shellResources);

            Content = replacementShellGeneration.Root;
            UpdateWindowCornerRadius();
            _shellGeneration = replacementShellGeneration;
            _pageGeneration = replacementPageGeneration;
        }
        catch (Exception exception)
        {
            replacementPageGeneration?.Dispose();
            replacementShellGeneration?.Dispose();

            try
            {
                Content = previousWindowContent;
            }
            catch (Exception rollbackException)
            {
                TADNLog.Log(
                    $"{GetType().Name} shell rollback failed after {exception.GetType().Name}: " +
                    $"{rollbackException.GetType().Name}: {rollbackException.Message}");
            }

            _content = previousContent;
            _pageDescriptors = previousPageDescriptors;
            _pages = previousPages;
            _navItems = previousNavItems;
            _scrollHost = previousScrollHost;
            _sidebar = previousSidebar;
            _sidebarHeader = previousSidebarHeader;
            _sidebarColumn = previousSidebarColumn;
            _sidebarResizeHandle = previousSidebarResizeHandle;
            _sidebarOverlay = previousSidebarOverlay;
            _sidebarControlsCollapsedState = previousSidebarControlsCollapsedState;
            _sidebarNavigationActions = previousSidebarNavigationActions;
            _sidebarFooterActions = previousSidebarFooterActions;
            _pageOverlayHost = previousPageOverlayHost;
            _pageOverlayAlignsToContentArea = previousPageOverlayAlignsToContentArea;
            _titleBarDragZone = previousTitleBarDragZone;
            _currentSidebarWidth = previousCurrentSidebarWidth;
            _settingsSearchBox = previousSettingsSearchBox;
            _confirmOverlay = previousConfirmOverlay;
            _confirmTitle = previousConfirmTitle;
            _confirmMessage = previousConfirmMessage;
            _confirmOk = previousConfirmOK;
            _confirmCancel = previousConfirmCancel;
            CurrentPageKey = previousPageKey;
            _hasShownPage = previousHasShownPage;
            _shellGeneration = previousShellGeneration;
            _pageGeneration = previousPageGeneration;
            throw;
        }

        previousPageGeneration?.Dispose();
        previousShellGeneration?.Dispose();
        RestorePageScroll(selectedPageKey, resetBeforeLayout: false, replacementPageGeneration.ID);
    }

    private UIContentGeneration BuildPageGeneration(TPageKey key, Func<Control> factory)
    {
        UIResourceScope resources = new($"{GetType().Name}.Page.{key}");
        _buildingPageResources = resources;
        try
        {
            Control root = factory();
            ControlNames.AssignLogicalSubtree(root, this);
            OwnDisposablePageControls(root, resources);
            return new UIContentGeneration($"{GetType().Name}.Page.{key}", root, resources);
        }
        catch
        {
            resources.Dispose();
            throw;
        }
        finally
        {
            _buildingPageResources = null;
        }
    }

    private void RestorePageScroll(TPageKey key, bool resetBeforeLayout, long pageGenerationID)
    {
        SettingsScrollHost? scrollHost = _scrollHost;
        if (scrollHost == null) return;

        double requestedOffset = _pageScrollOffsets.GetValueOrDefault(key, defaultValue: 0);

        if (resetBeforeLayout || requestedOffset <= 0)
            scrollHost.SetVerticalOffset(requestedOffset);
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!EqualityComparer<TPageKey>.Default.Equals(CurrentPageKey, key)) return;
                if (_pageGeneration?.ID != pageGenerationID) return;
                _scrollHost?.SetVerticalOffset(requestedOffset);
            },
            DispatcherPriority.Loaded);
    }

    private void RestorePageCommitState(
        Control? previousRoot,
        Dictionary<TPageKey, bool> previousNavSelections,
        Exception commitException)
    {
        try
        {
            _content.Content = previousRoot;
            ApplyPageOverlay(previousRoot);
        }
        catch (Exception rollbackException)
        {
            TADNLog.Log(
                $"{GetType().Name} page rollback failed after {commitException.GetType().Name}: " +
                $"{rollbackException.GetType().Name}: {rollbackException.Message}");
        }

        foreach ((TPageKey navKey, SettingsNavItem item) in _navItems)
        {
            if (previousNavSelections.TryGetValue(navKey, out bool wasSelected))
                item.IsSelected = wasSelected;
        }
    }

    private static void OwnDisposablePageControls(Control root, UIResourceScope resources)
    {
        List<Control> pending = [root];
        HashSet<Control> visited = new(ReferenceEqualityComparer.Instance);
        while (pending.Count > 0)
        {
            int lastIndex = pending.Count - 1;
            Control control = pending[lastIndex];
            pending.RemoveAt(lastIndex);
            if (!visited.Add(control)) continue;

            if (control is IDisposable disposable)
            {
                resources.Own(disposable);
                // Disposable controls own their dynamic descendants and form a lifetime boundary
                continue;
            }

            switch (control)
            {
                case Panel panel:
                    foreach (Control child in panel.Children)
                        pending.Add(child);
                    break;

                case Decorator { Child: not null } decorator:
                    pending.Add(decorator.Child);
                    break;

                case ContentControl { Content: Control child }:
                    pending.Add(child);
                    break;

                case ItemsControl itemsControl:
                    foreach (object? item in itemsControl.Items)
                    {
                        if (item is Control itemControl)
                            pending.Add(itemControl);
                    }

                    break;
            }
        }
    }

    private Border BuildConfirmOverlay()
    {
        SettingsPalette palette = Palette;
        double titleFontSize = UseProminentConfirmationDialog
            ? _settingsResources.AxamlSettingsWindow.ConfirmProminentTitleFontSize
            : _settingsResources.AxamlSettingsWindow.ConfirmTitleFontSize;
        _confirmTitle = TrayAppDotNETSettingsUI.Text(
            L(nameof(CommonStrings.SettingsWindow_ConfirmOverlay_DefaultTitle)),
            palette,
            titleFontSize,
            FontWeight.SemiBold);
        _confirmTitle.TextWrapping = TextWrapping.Wrap;
        _confirmTitle.Margin = UseProminentConfirmationDialog
            ? _settingsResources.AxamlSettingsWindow.ConfirmProminentTitleMargin
            : _settingsResources.AxamlSettingsWindow.ConfirmTitleMargin;
        _confirmMessage = UseProminentConfirmationDialog
            ? TrayAppDotNETSettingsUI.Text(
                L(nameof(CommonStrings.SettingsWindow_ConfirmOverlay_DefaultMessage)),
                palette,
                _settingsResources.AxamlSettingsWindow.ConfirmProminentMessageFontSize)
            : TrayAppDotNETSettingsUI.DescriptionText(
                L(nameof(CommonStrings.SettingsWindow_ConfirmOverlay_DefaultMessage)),
                palette,
                _settingsResources.AxamlSettingsWindow.ConfirmMessageMargin);
        _confirmMessage.TextWrapping = TextWrapping.Wrap;
        _confirmOk = UseProminentConfirmationDialog
            ? BuildProminentConfirmButton(
                L(nameof(CommonStrings.SettingsWindow_ConfirmOverlay_Confirm)),
                palette)
            : Button(L(nameof(CommonStrings.SettingsWindow_ConfirmOverlay_Confirm)), palette);
        _confirmCancel = UseProminentConfirmationDialog
            ? BuildProminentConfirmButton(
                L(nameof(CommonStrings.SettingsWindow_ConfirmOverlay_Cancel)),
                palette)
            : Button(L(nameof(CommonStrings.SettingsWindow_ConfirmOverlay_Cancel)), palette);
        _confirmOk.Click += (_, _) => CompleteConfirm(true);
        _confirmCancel.Click += (_, _) => CompleteConfirm(false);

        return UseProminentConfirmationDialog
            ? BuildProminentConfirmOverlay(palette)
            : BuildCompactConfirmOverlay(palette);
    }

    private static SettingsButton BuildProminentConfirmButton(string text, SettingsPalette palette) =>
        new(
            text,
            palette,
            palette.ControlBackgroundDeep,
            palette.HoverDeep,
            palette.PressedDeep);

    private Border BuildCompactConfirmOverlay(SettingsPalette palette)
    {
        TextBlock confirmTitle = _confirmTitle
                                 ?? throw new InvalidOperationException("The confirmation title was not initialized.");
        TextBlock confirmMessage = _confirmMessage
                                   ?? throw new InvalidOperationException(
                                       "The confirmation message was not initialized.");
        _confirmCancel!.Margin = _settingsResources.AxamlSettingsWindow.ConfirmCancelMargin;
        _confirmOk!.MinWidth = _settingsResources.AxamlSettingsWindow.ConfirmButtonMinWidth;
        _confirmCancel.MinWidth = _settingsResources.AxamlSettingsWindow.ConfirmButtonMinWidth;

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(_confirmCancel);
        buttons.Children.Add(_confirmOk);

        Border dialog = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.CardBackground),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            BorderThickness = _settingsResources.AxamlSettingsWindow.ConfirmDialogBorderThickness,
            CornerRadius = RadiusLarge,
            Padding = _settingsResources.AxamlSettingsWindow.ConfirmDialogPadding,
            MinWidth = _settingsResources.AxamlSettingsWindow.ConfirmDialogMinWidth,
            MaxWidth = _settingsResources.AxamlSettingsWindow.ConfirmDialogMaxWidth,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Children = { confirmTitle, confirmMessage, buttons } }
        };
        return new Border { Background = TrayAppDotNETSettingsUI.Brush(ConfirmOverlayBackdrop), Child = dialog };
    }

    private Border BuildProminentConfirmOverlay(SettingsPalette palette)
    {
        _confirmOk!.Height = _settingsResources.AxamlSettingsWindow.ConfirmProminentButtonHeight;
        _confirmCancel!.Height = _settingsResources.AxamlSettingsWindow.ConfirmProminentButtonHeight;
        CornerRadius buttonCornerRadius = RoundedCornerRadius(
            _settingsResources.AxamlSettingsWindow.ConfirmProminentButtonCornerRadius);
        _confirmOk.CornerRadius = buttonCornerRadius;
        _confirmCancel.CornerRadius = buttonCornerRadius;
        _confirmOk.Label.FontSize = _settingsResources.AxamlSettingsWindow.ConfirmProminentButtonFontSize;
        _confirmCancel.Label.FontSize = _settingsResources.AxamlSettingsWindow.ConfirmProminentButtonFontSize;
        _confirmOk.HorizontalAlignment = HorizontalAlignment.Stretch;
        _confirmCancel.HorizontalAlignment = HorizontalAlignment.Stretch;

        Grid buttons = new()
        {
            ColumnSpacing = _settingsResources.AxamlSettingsWindow.ConfirmProminentButtonSpacing,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }
        };
        buttons.Children.Add(_confirmOk);
        Grid.SetColumn(_confirmCancel, value: 1);
        buttons.Children.Add(_confirmCancel);

        Border body = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.CardBackground),
            Padding = _settingsResources.AxamlSettingsWindow.ConfirmProminentBodyPadding,
            Child = new StackPanel { Children = { _confirmTitle!, _confirmMessage! } }
        };
        Border footer = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.FooterBackground),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            BorderThickness = _settingsResources.AxamlSettingsWindow.ConfirmProminentFooterBorderThickness,
            Padding = _settingsResources.AxamlSettingsWindow.ConfirmProminentFooterPadding,
            Child = buttons
        };
        Grid.SetRow(footer, value: 1);

        Grid content = new()
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            Children = { body, footer }
        };
        // FlyoutFrame preserves the outer border while an opaque inner surface clips rounded content
        FlyoutFrame dialog = new(
            content,
            palette.CardBackground,
            palette.Border,
            EnableRoundedCorners)
        {
            Margin = _settingsResources.AxamlSettingsWindow.ConfirmProminentDialogMargin,
            MinWidth = _settingsResources.AxamlSettingsWindow.ConfirmProminentDialogMinWidth,
            MaxWidth = _settingsResources.AxamlSettingsWindow.ConfirmProminentDialogMaxWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        return new Border { Background = TrayAppDotNETSettingsUI.Brush(ConfirmOverlayBackdrop), Child = dialog };
    }

    private void CompleteConfirm(bool result)
    {
        TaskCompletionSource<bool>? tcs = _confirmTcs;
        _confirmTcs = null;
        SetConfirmOverlayVisible(false);
        RestoreConfirmFocus();
        tcs?.TrySetResult(result);
    }

    private void CancelPendingConfirm()
    {
        TaskCompletionSource<bool>? tcs = _confirmTcs;
        _confirmTcs = null;
        SetConfirmOverlayVisible(false);
        RestoreConfirmFocus();
        tcs?.TrySetResult(false);
    }

    private void SetConfirmOverlayVisible(bool isVisible)
    {
        Border? confirmOverlay = _confirmOverlay;
        if (confirmOverlay == null || confirmOverlay.IsVisible == isVisible) return;

        confirmOverlay.IsVisible = isVisible;
        OnConfirmOverlayVisibilityChanged(isVisible);
    }

    private void RestoreConfirmFocus()
    {
        Control? previousFocus = _confirmPreviousFocus;
        _confirmPreviousFocus = null;
        previousFocus?.Focus();
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        AttachWndProcHook();
        UpdateSidebarLayout();
        UpdateWindowCornerRadius();
    }

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e) => UpdateWindowCornerRadius();

    private void OnWindowResized(object? sender, WindowResizedEventArgs e)
    {
        UpdateSidebarLayout(e.ClientSize.Width);
        UpdateWindowCornerRadius();
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs eventArgs) =>
        _sidebarResizeHandle?.SetControlModifierDown(
            (eventArgs.KeyModifiers & KeyModifiers.Control) != 0);

    private void OnWindowKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (_confirmTcs != null)
        {
            switch (eventArgs.Key)
            {
                case Key.Escape:
                    CompleteConfirm(false);
                    eventArgs.Handled = true;
                    return;
                case Key.Tab:
                case Key.Left:
                case Key.Right:
                    Control? focusedElement = GetTopLevel(this)?
                        .FocusManager
                        .GetFocusedElement() as Control;
                    SettingsButton? nextButton = ReferenceEquals(focusedElement, _confirmOk)
                        ? _confirmCancel
                        : _confirmOk;
                    nextButton?.Focus();
                    eventArgs.Handled = true;
                    return;
                case Key.Enter:
                case Key.Space:
                    return;
                default:
                    eventArgs.Handled = true;
                    return;
            }
        }

        bool isControlModifierDown = eventArgs.Key is Key.LeftCtrl or Key.RightCtrl
                                     || (eventArgs.KeyModifiers & KeyModifiers.Control) != 0;
        _sidebarResizeHandle?.SetControlModifierDown(isControlModifierDown);
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs eventArgs)
    {
        bool isControlModifierDown = eventArgs.Key is not (Key.LeftCtrl or Key.RightCtrl)
                                     && (eventArgs.KeyModifiers & KeyModifiers.Control) != 0;
        _sidebarResizeHandle?.SetControlModifierDown(isControlModifierDown);
    }

    private void OnWindowDeactivated(object? sender, EventArgs eventArgs) =>
        _sidebarResizeHandle?.SetControlModifierDown(false);

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (IsClosing) return;
        IsClosing = true;
        CancelPendingConfirm();

        try
        {
            OnSettingsWindowClosed();
        }
        catch (Exception exception)
        {
            TADNLog.Log(
                $"{GetType().Name}.OnSettingsWindowClosed failed: {exception.GetType().Name}: {exception.Message}");
        }

        foreach (TrayAppDotNETColorPickerWindow picker in _openColorPickers.ToArray())
        {
            try
            {
                picker.Close();
            }
            catch (Exception exception)
            {
                TADNLog.Log($"{GetType().Name} color picker close failed: {exception.Message}");
            }
        }

        _openColorPickers.Clear();

        RunWindowCloseCleanup(nameof(DetachWndProcHook), DetachWndProcHook);
        RunWindowCloseCleanup(nameof(DisposeSettingsSearch), DisposeSettingsSearch);
        RunWindowCloseCleanup(operation: "ClearWindowContent", () => Content = null);

        UIContentGeneration? pageGeneration = Interlocked.Exchange(ref _pageGeneration, value: null);
        UIContentGeneration? shellGeneration = Interlocked.Exchange(ref _shellGeneration, value: null);
        if (pageGeneration != null)
            RunWindowCloseCleanup(operation: "DisposePageGeneration", pageGeneration.Dispose);
        if (shellGeneration != null)
            RunWindowCloseCleanup(operation: "DisposeShellGeneration", shellGeneration.Dispose);

        UIResourceScope? buildingPageResources = Interlocked.Exchange(ref _buildingPageResources, value: null);
        if (buildingPageResources != null)
            RunWindowCloseCleanup(operation: "DisposeBuildingPageResources", buildingPageResources.Dispose);
        RunWindowCloseCleanup(operation: "ClearPageContent", () => _content.Content = null);
        _pages.Clear();
        _navItems.Clear();
        _sidebarNavigationActions = [];
        _sidebarFooterActions = [];
        _pageDescriptors = [];
        _pageScrollOffsets.Clear();
        _scrollHost = null;
        _sidebar = null;
        _sidebarHeader = null;
        _sidebarColumn = null;
        _sidebarResizeHandle = null;
        _sidebarOverlay = null;
        _sidebarControlsCollapsedState = null;
        _pageOverlayHost = null;
        _pageOverlayAlignsToContentArea = false;
        _titleBarDragZone = null;
        _currentSidebarWidth = 0;
        _confirmOverlay = null;
        _confirmTitle = null;
        _confirmMessage = null;
        _confirmOk = null;
        _confirmCancel = null;
        _confirmPreviousFocus = null;
        _windowResources.Dispose();
    }

    private void UpdateSidebarLayout() => UpdateSidebarLayout(ResolveWindowWidth());

    private void UpdateSidebarLayout(double windowWidth)
    {
        SettingsSidebar? sidebar = _sidebar;
        ColumnDefinition? sidebarColumn = _sidebarColumn;
        if (sidebar == null || sidebarColumn == null) return;

        bool isCollapsed = ResolveSidebarCollapsed(windowWidth);
        double pageContentLeftInset = ResolvePageContentLeftInset(windowWidth);
        bool usesCompactRail = isCollapsed && pageContentLeftInset > 0;
        sidebar.IsVisible = !isCollapsed || usesCompactRail;
        sidebar.ClipToBounds = usesCompactRail;
        if (_sidebarHeader != null)
            UpdateSidebarHeader(_sidebarHeader, usesCompactRail);
        foreach (SettingsNavItem navigationItem in _navItems.Values)
            navigationItem.SetCompact(usesCompactRail);
        foreach (SettingsNavItem navigationAction in _sidebarNavigationActions)
            navigationAction.SetCompact(usesCompactRail);
        foreach (SettingsNavItem footerAction in _sidebarFooterActions)
            footerAction.SetCompact(usesCompactRail);
        _settingsSearchBox?.IsVisible = !usesCompactRail;
        _sidebarResizeHandle?.IsVisible = !isCollapsed;
        sidebarColumn.Width = new GridLength(pageContentLeftInset);
        _titleBarDragZone?.Width = isCollapsed
            ? Math.Max(
                _settingsResources.AxamlSettingsWindow.TitleBarHeight,
                pageContentLeftInset)
            : pageContentLeftInset;
        if (_sidebarControlsCollapsedState != isCollapsed)
        {
            if (_sidebarOverlay != null)
                UpdateSidebarOverlay(_sidebarOverlay, isCollapsed);
            UpdateSidebarNavigationActions(_sidebarNavigationActions, isCollapsed);
            _sidebarControlsCollapsedState = isCollapsed;
        }

        UpdatePageOverlayLayout();
    }

    /// <summary>Resolves the page area's left inset for a candidate window width.</summary>
    protected double ResolvePageContentLeftInset(double windowWidth)
    {
        if (ResolveSidebarCollapsed(windowWidth))
            return Math.Min(Math.Max(val1: 0, CollapsedSidebarWidth), Math.Max(val1: 0, windowWidth));

        double maximumWidth = GetAvailableSidebarMaximumWidth(windowWidth);
        return SettingsSidebarWidthLayout.ResolvePersistedWidth(
            _currentSidebarWidth,
            SidebarWidth,
            _settingsResources.AxamlSettingsWindow.SidebarMinimumWidth,
            maximumWidth);
    }

    private bool ResolveSidebarCollapsed(double windowWidth)
    {
        if (_sidebarCollapseOverride.HasValue)
            return _sidebarCollapseOverride.Value;

        return EnableResponsiveSidebarCollapse && windowWidth < SidebarCollapseThreshold;
    }

    private void UpdatePageOverlayLayout()
    {
        Grid? overlayHost = _pageOverlayHost;
        if (overlayHost == null) return;

        double leftInset = _pageOverlayAlignsToContentArea
            ? _sidebarColumn?.Width.Value ?? 0
            : 0;
        overlayHost.Margin = new Thickness(leftInset, top: 0, right: 0, bottom: 0);
    }

    private double ResolveWindowWidth()
    {
        double windowWidth = ClientSize.Width;
        if (!double.IsFinite(windowWidth) || windowWidth <= 0)
            windowWidth = Bounds.Width;
        if (!double.IsFinite(windowWidth) || windowWidth <= 0)
            windowWidth = Width;
        return windowWidth;
    }

    private double ResolveConfiguredSidebarWidth()
    {
        double persistedWidth = SidebarWidthSettings?.SettingsSidebarWidth ?? 0;
        return SettingsSidebarWidthLayout.ResolvePersistedWidth(
            persistedWidth,
            SidebarWidth,
            _settingsResources.AxamlSettingsWindow.SidebarMinimumWidth,
            _settingsResources.AxamlSettingsWindow.SidebarMaximumWidth);
    }

    private double GetDisplayedSidebarWidth()
    {
        double displayedWidth = _sidebarColumn?.Width.Value ?? _currentSidebarWidth;
        return double.IsFinite(displayedWidth) && displayedWidth > 0
            ? displayedWidth
            : _currentSidebarWidth;
    }

    private double GetAvailableSidebarMaximumWidth() =>
        GetAvailableSidebarMaximumWidth(ResolveWindowWidth());

    private double GetAvailableSidebarMaximumWidth(double windowWidth) =>
        SettingsSidebarWidthLayout.GetAvailableMaximumWidth(
            windowWidth,
            _settingsResources.AxamlSettingsWindow.SidebarMinimumWidth,
            _settingsResources.AxamlSettingsWindow.SidebarMaximumWidth,
            _settingsResources.AxamlSettingsWindow.SidebarMinimumContentWidth);

    private void PreviewSidebarWidth(double width)
    {
        _currentSidebarWidth = SettingsSidebarWidthLayout.ResolvePersistedWidth(
            width,
            SidebarWidth,
            _settingsResources.AxamlSettingsWindow.SidebarMinimumWidth,
            _settingsResources.AxamlSettingsWindow.SidebarMaximumWidth);
        UpdateSidebarLayout();
    }

    private void PersistSidebarWidth(double width)
    {
        PreviewSidebarWidth(width);
        ISettingsSidebarWidthSettings? settings = SidebarWidthSettings;
        if (settings == null
            || SettingsSidebarWidthLayout.AreEqual(settings.SettingsSidebarWidth, _currentSidebarWidth))
            return;

        settings.SettingsSidebarWidth = _currentSidebarWidth;
        Save();
    }

    private void ResetSidebarWidth()
    {
        _currentSidebarWidth = SettingsSidebarWidthLayout.ResolvePersistedWidth(
            persistedWidth: 0,
            SidebarWidth,
            _settingsResources.AxamlSettingsWindow.SidebarMinimumWidth,
            _settingsResources.AxamlSettingsWindow.SidebarMaximumWidth);
        UpdateSidebarLayout();

        ISettingsSidebarWidthSettings? settings = SidebarWidthSettings;
        if (settings == null || settings.SettingsSidebarWidth == 0) return;

        settings.SettingsSidebarWidth = 0;
        Save();
    }

    private void RunWindowCloseCleanup(string operation, Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception exception)
        {
            TADNLog.Log(
                $"{GetType().Name}.{operation} failed during close: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private void AttachWndProcHook()
    {
        if (_wndProcHookAttached || !OperatingSystem.IsWindows()) return;

        Win32Properties.AddWndProcHookCallback(this, _wndProcHook);
        _wndProcHookAttached = true;
    }

    private void DetachWndProcHook()
    {
        if (!_wndProcHookAttached || !OperatingSystem.IsWindows()) return;

        Win32Properties.RemoveWndProcHookCallback(this, _wndProcHook);
        _wndProcHookAttached = false;
    }

    private static IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != User32.WM_MOUSEACTIVATE) return IntPtr.Zero;

        handled = true;
        return new IntPtr(User32.MA_ACTIVATE);
    }

    private void UpdateWindowCornerRadius()
    {
        if (Content is not Border { Child: Border contentSurface } windowFrame) return;

        bool useSharpCorners = ShouldUseSharpWindowCorners();
        windowFrame.CornerRadius = useSharpCorners
            ? _settingsResources.AxamlSettingsWindow.ZeroCornerRadius
            : RoundedCornerRadius(_settingsResources.AxamlSettingsWindow.OuterCornerRadius);
        contentSurface.CornerRadius = useSharpCorners
            ? _settingsResources.AxamlSettingsWindow.ZeroCornerRadius
            : RoundedCornerRadius(_settingsResources.AxamlSettingsWindow.InnerCornerRadius);
        ApplyNativeCornerPreference(useSharpCorners);
    }

    private bool ShouldUseSharpWindowCorners()
    {
        if (WindowState is WindowState.Maximized or WindowState.FullScreen) return true;
        if (WindowState == WindowState.Minimized) return false;

        PixelRect windowBounds = GetWindowPixelBounds();
        PixelRect? workArea = Screens.ScreenFromWindow(this)?.WorkingArea
                              ?? Screens.ScreenFromBounds(windowBounds)?.WorkingArea;
        return workArea is { } activeWorkArea
               && SpansFullWorkAreaAxis(windowBounds, activeWorkArea);
    }

    private PixelRect GetWindowPixelBounds()
    {
        if (OperatingSystem.IsWindows())
        {
            IntPtr windowHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (windowHandle != IntPtr.Zero && User32.GetWindowRect(windowHandle, out RECT nativeBounds))
            {
                return new PixelRect(
                    nativeBounds.Left,
                    nativeBounds.Top,
                    Math.Max(val1: 0, nativeBounds.Right - nativeBounds.Left),
                    Math.Max(val1: 0, nativeBounds.Bottom - nativeBounds.Top));
            }
        }

        double width = ClientSize.Width > 0 ? ClientSize.Width : Bounds.Width;
        double height = ClientSize.Height > 0 ? ClientSize.Height : Bounds.Height;
        int pixelWidth = Math.Max(val1: 0, (int)Math.Ceiling(width * RenderScaling));
        int pixelHeight = Math.Max(val1: 0, (int)Math.Ceiling(height * RenderScaling));
        return new PixelRect(Position.X, Position.Y, pixelWidth, pixelHeight);
    }

    private void ApplyNativeCornerPreference(bool useSharpCorners)
    {
        if (!OperatingSystem.IsWindows()) return;

        int preference = useSharpCorners || !EnableRoundedCorners
            ? DWMAPI.DWMWCP_DONOTROUND
            : DWMAPI.DWMWCP_DEFAULT;
        if (_nativeCornerPreference == preference) return;

        IntPtr windowHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (windowHandle == IntPtr.Zero) return;

        _ = DWMAPI.DwmSetWindowAttribute(
            windowHandle,
            DWMAPI.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref preference,
            sizeof(int));
        _nativeCornerPreference = preference;
    }

    /// <summary>Returns whether the window covers either complete work-area axis.</summary>
    internal static bool SpansFullWorkAreaAxis(PixelRect windowBounds, PixelRect workArea)
    {
        bool spansFullWidth = windowBounds.X <= workArea.X + WorkAreaEdgeTolerancePixels
                              && windowBounds.Right >= workArea.Right - WorkAreaEdgeTolerancePixels;
        bool spansFullHeight = windowBounds.Y <= workArea.Y + WorkAreaEdgeTolerancePixels
                               && windowBounds.Bottom >= workArea.Bottom - WorkAreaEdgeTolerancePixels;
        return spansFullWidth || spansFullHeight;
    }

    private CornerRadius RoundedCornerRadius(CornerRadius cornerRadius) =>
        EnableRoundedCorners
            ? cornerRadius
            : _settingsResources.AxamlSettingsWindow.ZeroCornerRadius;
}
