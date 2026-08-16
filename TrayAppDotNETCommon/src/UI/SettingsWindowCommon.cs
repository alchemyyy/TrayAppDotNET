using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Settings;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI;

public sealed record SettingsPageDescriptor<TPageKey>(
    TPageKey Key,
    string Label,
    Func<Control> BuildPage)
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
    private const bool EnableCustomWindowBorder = false;
    private const int WorkAreaEdgeTolerancePixels = 1;

    private ContentControl _content = new();
    private readonly SettingsWindowCommonResources _settingsResources = new();
    private readonly CommonBindingsResources _commonBindingResources = new();
    private IReadOnlyList<SettingsPageDescriptor<TPageKey>> _pageDescriptors = [];
    private Dictionary<TPageKey, Func<Control>> _pages = [];
    private Dictionary<TPageKey, SettingsNavItem> _navItems = [];
    private readonly Dictionary<TPageKey, double> _pageScrollOffsets = [];
    private readonly HashSet<TrayAppDotNETColorPickerWindow> _openColorPickers = [];
    private readonly UIResourceScope _windowResources;
    private UIContentGeneration? _shellGeneration;
    private UIContentGeneration? _pageGeneration;
    private UIResourceScope? _buildingPageResources;
    private SettingsScrollHost? _scrollHost;
    private TaskCompletionSource<bool>? _confirmTcs;
    private Border? _confirmOverlay;
    private TextBlock? _confirmTitle;
    private TextBlock? _confirmMessage;
    private SettingsButton? _confirmOk;
    private SettingsButton? _confirmCancel;
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

    protected TPageKey CurrentPageKey { get; private set; } = default!;
    protected bool IsClosing { get; private set; }

    protected SettingsPalette Palette => _palette ??= ResolvePalette();
    protected abstract bool EnableRoundedCorners { get; }
    protected abstract TPageKey DefaultPageKey { get; }
    protected abstract string HeaderText { get; }
    protected abstract string OpenSettingsFolderText { get; }
    protected abstract string SettingsFolderPath { get; }
    protected abstract SettingsPalette ResolvePalette();
    protected abstract IReadOnlyList<SettingsPageDescriptor<TPageKey>> CreatePageDescriptors();
    protected abstract void Save();

    protected virtual Color ConfirmOverlayBackdrop =>
        AppTheme.Default.FlyoutOverlayBackdrop.For(AppTheme.Default.IsLightTheme);
    protected virtual double SidebarWidth => _settingsResources.AxamlSettingsWindow.DefaultSidebarWidth;

    protected SettingsWindowCommon()
    {
        _windowResources = new UIResourceScope(GetType().Name);
        Resources.MergedDictionaries.Add(_settingsResources);
        Resources.MergedDictionaries.Add(_commonBindingResources);
        _wndProcHook = WndProcHook;
        Opened += OnWindowOpened;
        Closed += OnWindowClosed;
        PositionChanged += OnWindowPositionChanged;
        Resized += OnWindowResized;
        GlyphCatalogHotReload.ResourcesReloaded += OnGlyphCatalogResourcesReloaded;
        _windowResources.Add(() => GlyphCatalogHotReload.ResourcesReloaded -= OnGlyphCatalogResourcesReloaded);
        _windowResources.Add(DetachWndProcHook);
        _windowResources.Add(() => Resized -= OnWindowResized);
        _windowResources.Add(() => PositionChanged -= OnWindowPositionChanged);
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
                return;

            case SettingsWindowSizeProfile.Compact:
                Width = _settingsResources.AxamlSettingsWindow.CompactWindowWidth;
                Height = _settingsResources.AxamlSettingsWindow.CompactWindowHeight;
                MinWidth = _settingsResources.AxamlSettingsWindow.CompactWindowMinWidth;
                MinHeight = _settingsResources.AxamlSettingsWindow.CompactWindowMinHeight;
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(sizeProfile), sizeProfile, null);
        }
    }

    protected void InitializeSettingsShell()
    {
        if (_shellInitialized) return;

        _shellInitialized = true;
        BuildAndCommitShell(DefaultPageKey);
    }

    protected virtual void OnSettingsWindowClosed()
    {
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        CancelPendingConfirm();
        _confirmTitle!.Text = title;
        _confirmMessage!.Text = message;
        _confirmOk!.Text = confirmText;
        _confirmCancel!.Text = cancelText;
        _confirmOverlay!.IsVisible = true;
        _confirmTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return _confirmTcs.Task;
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

    /// <summary>
    /// Rebuilds code-created settings glyphs after a catalog source reload.
    /// </summary>
    private void OnGlyphCatalogResourcesReloaded()
    {
        if (IsClosing || !_shellInitialized) return;

        RebuildShell(CurrentPageKey);
    }

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
        return resources == null ? throw new InvalidOperationException("Page resources can only be registered while building a page.") : resources.Own(resource);
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
        ConfirmAsync(title, message, "OK", "OK");

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
        body.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(SidebarWidth)));
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        Grid sidebar = new() { Background = Brushes.Transparent };
        sidebar.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        sidebar.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        sidebar.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetColumn(sidebar, 0);
        body.Children.Add(sidebar);

        TextBlock header = TrayAppDotNETSettingsUI.Text(
            HeaderText,
            palette,
            _settingsResources.AxamlSettingsWindow.HeaderFontSize,
            FontWeight.SemiBold);
        header.Margin = _settingsResources.AxamlSettingsWindow.HeaderMargin;
        Grid.SetRow(header, 0);
        sidebar.Children.Add(header);

        StackPanel nav = new() { Margin = _settingsResources.AxamlSettingsWindow.NavMargin };
        foreach (SettingsPageDescriptor<TPageKey> page in _pageDescriptors)
        {
            _pages[page.Key] = page.BuildPage;
            AddNavItem(nav, page.Key, page.Label, palette);
        }

        Grid.SetRow(nav, 1);
        sidebar.Children.Add(nav);

        StackPanel footer = new() { Margin = _settingsResources.AxamlSettingsWindow.FooterMargin };
        _settingsSearchBox = new SettingsSearchBox(
            palette,
            L(nameof(CommonStrings.SettingsWindow_SearchPlaceholder)));
        _settingsSearchBox.SearchTextChanged += OnSettingsSearchTextChanged;
        footer.Children.Add(_settingsSearchBox);
        Grid.SetRow(footer, 2);
        sidebar.Children.Add(footer);

        _scrollHost = TrayAppDotNETSettingsUI.ScrollHost(
            _content,
            palette,
            _settingsResources.AxamlSettingsWindow.ScrollHostMargin);
        Grid.SetColumn(_scrollHost, 1);
        body.Children.Add(_scrollHost);

        Control titleBar = BuildTitleBar(palette);
        Grid.SetRow(titleBar, 0);
        Grid.SetRowSpan(titleBar, 2);
        root.Children.Add(titleBar);

        _confirmOverlay = BuildConfirmOverlay();
        _confirmOverlay.IsVisible = false;
        Grid.SetRow(_confirmOverlay, 1);
        root.Children.Add(_confirmOverlay);

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
            Background = Brushes.Transparent,
            Height = _settingsResources.AxamlSettingsWindow.TitleBarDragZoneHeight,
            VerticalAlignment = VerticalAlignment.Top
        };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        titleBar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        titleBar.PointerPressed += (_, e) =>
        {
            if (e.Source is SettingsButton) return;
            if (!e.GetCurrentPoint(titleBar).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2) ToggleMaximize();
            else BeginMoveDrag(e);
        };

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        SettingsButton minimize = CaptionButton(GlyphCatalog.CHROME_MINIMIZE, palette);
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
        Grid.SetColumn(buttons, 1);
        titleBar.Children.Add(buttons);
        return titleBar;
    }

    private SettingsButton CaptionButton(Glyph glyph, SettingsPalette palette, bool closeButton = false)
    {
        SettingsButton button = new(glyph, palette, transparentBase: true)
        {
            Width = _settingsResources.AxamlSettingsWindow.CaptionButtonWidth,
            Height = _settingsResources.AxamlSettingsWindow.TitleBarHeight,
            CornerRadius = _settingsResources.AxamlSettingsWindow.ZeroCornerRadius,
            Padding = _settingsResources.AxamlSettingsWindow.ZeroThickness,
            Label =
            {
                FontSize = _settingsResources.AxamlSettingsWindow.CaptionButtonGlyphFontSize
            }
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
        int left = workArea.X + Math.Max(0, workArea.Width - width) / 2;
        int top = workArea.Y + Math.Max(0, workArea.Height - height) / 2;

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

    private void AddNavItem(StackPanel nav, TPageKey key, string label, SettingsPalette palette)
    {
        SettingsNavItem item = new(label, palette, RadiusTiny, RadiusMedium);
        item.Click += (_, _) => NavigateToSettingsPage(key);
        _navItems[key] = item;
        nav.Children.Add(item);
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
        RestorePageScroll(key, resetBeforeLayout: !force, replacement.ID);
    }

    private void BuildAndCommitShell(TPageKey selectedPageKey)
    {
        CancelPendingConfirm();

        ContentControl previousContent = _content;
        IReadOnlyList<SettingsPageDescriptor<TPageKey>> previousPageDescriptors = _pageDescriptors;
        Dictionary<TPageKey, Func<Control>> previousPages = _pages;
        Dictionary<TPageKey, SettingsNavItem> previousNavItems = _navItems;
        SettingsScrollHost? previousScrollHost = _scrollHost;
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
            _scrollHost = null;
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
            foreach ((TPageKey navKey, SettingsNavItem item) in _navItems)
                item.IsSelected = EqualityComparer<TPageKey>.Default.Equals(navKey, selectedPageKey);
            _hasShownPage = true;

            UIResourceScope shellResources = new($"{GetType().Name}.Shell");
            shellResources.Add(replacementNavItems.Clear);
            shellResources.Add(replacementPages.Clear);
            if (_scrollHost != null)
                shellResources.Own(_scrollHost);
            if (_settingsSearchBox != null)
                shellResources.Own(_settingsSearchBox);
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

        double requestedOffset = _pageScrollOffsets.GetValueOrDefault(key, 0);

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
        _confirmTitle = TrayAppDotNETSettingsUI.Text(
            L(nameof(CommonStrings.SettingsWindow_ConfirmOverlay_DefaultTitle)),
            palette,
            _settingsResources.AxamlSettingsWindow.ConfirmTitleFontSize,
            FontWeight.SemiBold);
        _confirmTitle.TextWrapping = TextWrapping.Wrap;
        _confirmTitle.Margin = _settingsResources.AxamlSettingsWindow.ConfirmTitleMargin;
        _confirmMessage = TrayAppDotNETSettingsUI.DescriptionText(
            L(nameof(CommonStrings.SettingsWindow_ConfirmOverlay_DefaultMessage)),
            palette,
            _settingsResources.AxamlSettingsWindow.ConfirmMessageMargin);
        _confirmOk = Button(L(nameof(CommonStrings.SettingsWindow_ConfirmOverlay_Confirm)), palette);
        _confirmCancel = Button(L(nameof(CommonStrings.SettingsWindow_ConfirmOverlay_Cancel)), palette);
        _confirmCancel.Margin = _settingsResources.AxamlSettingsWindow.ConfirmCancelMargin;
        _confirmOk.MinWidth = _settingsResources.AxamlSettingsWindow.ConfirmButtonMinWidth;
        _confirmCancel.MinWidth = _settingsResources.AxamlSettingsWindow.ConfirmButtonMinWidth;
        _confirmOk.Click += (_, _) => CompleteConfirm(true);
        _confirmCancel.Click += (_, _) => CompleteConfirm(false);

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
            Child = new StackPanel { Children = { _confirmTitle, _confirmMessage, buttons } }
        };
        return new Border { Background = TrayAppDotNETSettingsUI.Brush(ConfirmOverlayBackdrop), Child = dialog };
    }

    private void CompleteConfirm(bool result)
    {
        _confirmOverlay!.IsVisible = false;
        TaskCompletionSource<bool>? tcs = _confirmTcs;
        _confirmTcs = null;
        tcs?.TrySetResult(result);
    }

    private void CancelPendingConfirm()
    {
        _confirmOverlay?.IsVisible = false;
        TaskCompletionSource<bool>? tcs = _confirmTcs;
        _confirmTcs = null;
        tcs?.TrySetResult(false);
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        AttachWndProcHook();
        UpdateWindowCornerRadius();
    }

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e) => UpdateWindowCornerRadius();

    private void OnWindowResized(object? sender, WindowResizedEventArgs e) => UpdateWindowCornerRadius();

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
        RunWindowCloseCleanup("ClearWindowContent", () => Content = null);

        UIContentGeneration? pageGeneration = Interlocked.Exchange(ref _pageGeneration, null);
        UIContentGeneration? shellGeneration = Interlocked.Exchange(ref _shellGeneration, null);
        if (pageGeneration != null)
            RunWindowCloseCleanup("DisposePageGeneration", pageGeneration.Dispose);
        if (shellGeneration != null)
            RunWindowCloseCleanup("DisposeShellGeneration", shellGeneration.Dispose);

        UIResourceScope? buildingPageResources = Interlocked.Exchange(ref _buildingPageResources, null);
        if (buildingPageResources != null)
            RunWindowCloseCleanup("DisposeBuildingPageResources", buildingPageResources.Dispose);
        RunWindowCloseCleanup("ClearPageContent", () => _content.Content = null);
        _pages.Clear();
        _navItems.Clear();
        _pageDescriptors = [];
        _pageScrollOffsets.Clear();
        _scrollHost = null;
        _confirmOverlay = null;
        _confirmTitle = null;
        _confirmMessage = null;
        _confirmOk = null;
        _confirmCancel = null;
        _windowResources.Dispose();
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
        if (Content is not Border windowFrame || windowFrame.Child is not Border contentSurface) return;

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
        return workArea is PixelRect activeWorkArea
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
                    Math.Max(0, nativeBounds.Right - nativeBounds.Left),
                    Math.Max(0, nativeBounds.Bottom - nativeBounds.Top));
            }
        }

        double width = ClientSize.Width > 0 ? ClientSize.Width : Bounds.Width;
        double height = ClientSize.Height > 0 ? ClientSize.Height : Bounds.Height;
        int pixelWidth = Math.Max(0, (int)Math.Ceiling(width * RenderScaling));
        int pixelHeight = Math.Max(0, (int)Math.Ceiling(height * RenderScaling));
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
