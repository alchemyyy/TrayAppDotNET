using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TaskManagerTrayAppDotNET.Services;
using TrayAppDotNETCommon.Visuals;
using TaskManagerGlyphCatalog = TaskManagerTrayAppDotNET.Visuals.GlyphCatalog;

namespace TaskManagerTrayAppDotNET.UI;

public enum TaskManagerPage
{
    Processes,
    Performance,
    AppHistory,
    StartupApps,
    Users,
    Services,
    Settings
}

/// <summary>The Task Manager shell, built on the shared TrayAppDotNET settings-window chrome.</summary>
internal sealed class TaskManagerWindow : SettingsWindowCommon<TaskManagerPage>
{
    private const uint WindowMessageMoving = 0x0216;
    private const uint WindowMessageExitSizeMove = 0x0232;
    private const double SidebarCaretRotationDegrees = 90;
    private const string CollapseNavigationText = "Collapse navigation";
    private const string ExpandNavigationText = "Expand navigation";

    private const string EndTaskConfirmationMessage =
        "If an open program is associated with this process, it will close and you will lose any unsaved data. " +
        "If you end a system process, it might result in system instability. Are you sure you want to continue?";

    private const string EndTasksConfirmationMessage =
        "Open programs associated with these processes will close and you may lose unsaved data. Ending system " +
        "processes might result in system instability. Are you sure you want to continue?";

    private const string RestartExplorerConfirmationMessage =
        "This will close every running explorer.exe process, including the desktop, taskbar, and open File " +
        "Explorer windows, and then start a fresh explorer.exe process.";

    private const string ElevatedTerminationExplanation =
        "Task Manager can start TaskManagerTrayAppDotNET.KillHelper.exe with administrator privileges so it can " +
        "end elevated processes. Windows may display a security warning and a UAC prompt. If you cancel, Task " +
        "Manager will continue running with standard process permissions.";

    private static readonly ProcessDataSchema IdleProcessSchema = ProcessDataSchema.Create(
        []);

    private static readonly int[] NoWarmProcessIDs = [];
    private readonly AppSettings _settings;
    private readonly AppTheme _theme;
    private readonly ProcessSnapshotService _snapshotService;
    private readonly PerformanceSnapshotService _performanceSnapshotService;
    private readonly ProcessIconService _processIconService;
    private readonly ProcessTerminationService _processTerminationService;
    private readonly AppHistoryStore _appHistoryStore = new();
    private readonly WindowsUserSessionService _userSessionService = new(TADNLog.Log);
    private readonly WindowsServiceManager _windowsServiceManager = new();
    private readonly Action _exitApplication;
    private readonly TaskManagerWindowResources _taskManagerResources = TaskManagerWindowResources.Current;
    private readonly Win32Properties.CustomWndProcHookCallback _windowDragWndProcHook;
    private TaskManagerPageLayout? _activePageLayout;
    private ITaskManagerSearchOverlayPage? _searchOverlayPage;
    private ProcessDetailsPage? _processDetailsPage;
    private TaskManagerSettingsWindow? _settingsWindow;
    private string? _selectedPerformanceDeviceID;
#if DEBUG
    private double _axamlWindowWidth;
    private double _axamlWindowHeight;
    private double _axamlWindowMinWidth;
    private double _axamlWindowMinHeight;
    private ProcessDetailsHotReloadState? _processHotReloadState;
    private TaskManagerTableHotReloadState? _tableHotReloadState;
#endif
    private bool _windowDragWndProcHookAttached;
    private bool _activePageWorkEnabled;
    private bool _avoidSearchBoxDuringRestoreDrag;
    private bool _restoreDragSearchRangeResolved;
    private int _restoreDragSearchLeftWithinWindow;
    private int _restoreDragSearchRightWithinWindow;
    private bool _allowClose;
    private bool _initialElevationAttemptConsumed;
    private bool _manualElevationPromptPending;
    private bool _isConfirmationOverlayVisible;
    private bool _exitRequested;

    public TaskManagerWindow(
        AppSettings settings,
        AppTheme theme,
        ProcessSnapshotService snapshotService,
        PerformanceSnapshotService performanceSnapshotService,
        ProcessIconService processIconService,
        ProcessTerminationService processTerminationService,
        Action exitApplication)
    {
        _settings = settings;
        _theme = theme;
        _snapshotService = snapshotService;
        _performanceSnapshotService = performanceSnapshotService;
        _processIconService = processIconService;
        _processTerminationService = processTerminationService;
        _exitApplication = exitApplication;
        _windowDragWndProcHook = WindowDragWndProcHook;
        ConfigureSettingsWindow(Constants.DisplayName, icon: null);
        ApplyInitialAXAMLWindowDimensions();
        Topmost = settings.AlwaysOnTop;
        Closed += OnWindowClosedForDragHook;
        Closing += OnWindowClosing;
        PropertyChanged += OnWindowPropertyChanged;
        AddHandler(
            PointerPressedEvent,
            OnWindowPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        InitializeSettingsShell();
#if DEBUG
        TaskManagerWindowResources.ResourcesReloaded += OnAXAMLResourcesReloaded;
#endif
    }

    protected override bool EnableRoundedCorners => _settings.EnableRoundedCorners;
    protected override bool UseWindows11SettingsNavigation => true;
    protected override ISettingsSidebarWidthSettings SidebarWidthSettings => _settings;
    protected override bool ShowSettingsSearchBox => false;
    protected override bool UseExtendedTitleBarDragZone => false;
    protected override bool PageContentExtendsIntoTitleBar => true;
    protected override bool UsePageContentTitleBarDragZone => false;
    protected override bool UseProminentConfirmationDialog => true;
    protected override bool IsFooterNavigationPage(TaskManagerPage pageKey) => pageKey == TaskManagerPage.Settings;

    protected override bool PageOwnsScrolling(TaskManagerPage pageKey) =>
        pageKey != TaskManagerPage.Settings;

    protected override Control? ResolvePageOverlay(Control pageRoot) =>
        pageRoot is TaskManagerPageLayout page ? page.PageOverlay : null;

    protected override bool PageOverlayAlignsToContentArea(Control pageRoot) =>
        _settings.LeftAlignProcessSearchBar && pageRoot is ITaskManagerSearchOverlayPage;

    protected override bool EnableResponsiveSidebarCollapse => _settings.CollapseSidebarWhenNarrow;
    protected override double SidebarCollapseThreshold =>
        _taskManagerResources.AxamlTaskManagerWindow.SidebarCollapseThreshold;
#if DEBUG
    protected override bool ApplyCommonAXAMLWindowDimensionsOnReload => false;

    // AXAML hot-reload exception: Common and glyph dictionaries replace the shared shell, whose
    // page cleanup closes open column, affinity, and reorder editors; their live controls cannot be
    // safely reparented with active pointer, focus, and owner-window state
    protected override void OnBeforeHotReloadShellRebuild() => CaptureHotReloadShellState();

    protected override void OnAfterHotReloadShellRebuild() => RestoreHotReloadShellState();

    private void CaptureHotReloadShellState()
    {
        CapturePerformancePageState();
        _processHotReloadState = CurrentPageKey == TaskManagerPage.Processes
            ? _processDetailsPage?.CaptureHotReloadState()
            : null;
        _tableHotReloadState = _activePageLayout is TaskManagerTablePage tablePage
            ? tablePage.CaptureHotReloadState()
            : null;
    }

    private void RestoreHotReloadShellState()
    {
        ProcessDetailsHotReloadState? state = _processHotReloadState;
        TaskManagerTableHotReloadState? tableState = _tableHotReloadState;
        _processHotReloadState = null;
        _tableHotReloadState = null;
        if (state.HasValue
            && CurrentPageKey == TaskManagerPage.Processes
            && _processDetailsPage != null)
        {
            UpdateLayout();
            _processDetailsPage.RestoreHotReloadState(state.Value);
        }

        if (tableState.HasValue && _activePageLayout is TaskManagerTablePage tablePage)
        {
            UpdateLayout();
            tablePage.RestoreHotReloadState(tableState.Value);
        }
    }
#endif
    protected override double CollapsedSidebarWidth =>
        _taskManagerResources.AxamlTaskManagerWindow.CollapsedSidebarWidth;

    protected override bool HandleNavigationRequest(TaskManagerPage pageKey)
    {
        if (pageKey != TaskManagerPage.Settings) return false;
        ShowClassicSettingsWindow();
        return true;
    }

    protected override Thickness ContentPadding => default;
    protected override double SidebarWidth => _taskManagerResources.AxamlTaskManagerWindow.SidebarWidth;
    protected override TaskManagerPage DefaultPageKey => TaskManagerPage.Processes;
    protected override string HeaderText => Constants.DisplayName;
    protected override string OpenSettingsFolderText => "Open Task Manager settings folder";
    protected override string SettingsFolderPath => AppSettings.GetDefaultDirectory();

    protected override Color ConfirmOverlayBackdrop =>
        _theme.FlyoutOverlayBackdrop.For(ResolveEffectiveIsLight());

    protected override void OnConfirmOverlayVisibilityChanged(bool isVisible)
    {
        _isConfirmationOverlayVisible = isVisible;
        _processDetailsPage?.SetConfirmationOverlayVisible(isVisible);
    }

    protected override SettingsSidebar BuildSidebar() => new TaskManagerSidebar(_taskManagerResources);

    protected override Control BuildSidebarHeader(TextBlock title, SettingsPalette palette)
    {
        double iconSize =
            _taskManagerResources.AxamlTaskManagerWindow.CollapsedSidebarHeaderIconSize;
        SkiaCompositeGlyphIcon icon = new(TaskManagerGlyphCatalog.TASK_MANAGER_APP_COMPOSITE)
        {
            Width = iconSize,
            Height = iconSize,
            IconColor = palette.Foreground,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            IsHitTestVisible = false
        };
        return new TaskManagerSidebarHeader(title, icon);
    }

    protected override void UpdateSidebarHeader(Control sidebarHeader, bool usesCompactRail)
    {
        if (sidebarHeader is TaskManagerSidebarHeader taskManagerHeader)
        {
            taskManagerHeader.SetCompact(usesCompactRail);
            return;
        }

        base.UpdateSidebarHeader(sidebarHeader, usesCompactRail);
    }

    protected override IReadOnlyList<SettingsNavItem> CreateSidebarNavigationActions(SettingsPalette palette)
    {
        TaskManagerSidebarCaretIcon caretIcon = new(
            TaskManagerGlyphCatalog.CARET_LEFT,
            palette,
            SettingsNavItem.NavigationGlyphFontSize,
            _taskManagerResources.AxamlTaskManagerWindow.SidebarCaretGlyphOpacity,
            _taskManagerResources);
        SettingsNavItem sidebarCollapseButton = new(
            string.Empty,
            palette,
            RadiusTiny,
            RadiusMedium,
            useWindows11Style: true,
            customNavigationIcon: caretIcon,
            navigationIconTransform: new RotateTransform(SidebarCaretRotationDegrees));
        sidebarCollapseButton.Click += OnSidebarCollapseButtonClick;
        return [sidebarCollapseButton];
    }

    protected override void UpdateSidebarNavigationActions(
        IReadOnlyList<SettingsNavItem> navigationActions,
        bool isCollapsed)
    {
        SettingsNavItem sidebarCollapseButton = navigationActions[0];
        sidebarCollapseButton.Width = Math.Max(
            val1: 0,
            CollapsedSidebarWidth
            - sidebarCollapseButton.Margin.Left
            - sidebarCollapseButton.Margin.Right);
        sidebarCollapseButton.HorizontalAlignment = HorizontalAlignment.Left;
        sidebarCollapseButton.SetNavigationGlyph(
            isCollapsed
                ? TaskManagerGlyphCatalog.CARET_RIGHT
                : TaskManagerGlyphCatalog.CARET_LEFT);
        string navigationText = isCollapsed ? ExpandNavigationText : CollapseNavigationText;
        TrayAppDotNETToolTip.SetTip(sidebarCollapseButton, navigationText);
        AutomationProperties.SetName(sidebarCollapseButton, navigationText);
    }

    protected override void OnOpened(EventArgs eventArgs)
    {
        // Register before the shared shell hook so its handled-message return value remains last
        if (!_windowDragWndProcHookAttached && OperatingSystem.IsWindows())
        {
            Win32Properties.AddWndProcHookCallback(this, _windowDragWndProcHook);
            _windowDragWndProcHookAttached = true;
        }

        base.OnOpened(eventArgs);
        UpdateActivePageActivity();
    }

    private void OnSidebarCollapseButtonClick(object? sender, EventArgs eventArgs) => ToggleSidebarCollapse();

    protected override SettingsPalette ResolvePalette() =>
        VolumeSettingsPalette.Create(_theme, _settings, ResolveEffectiveIsLight());

    protected override bool ResolveEffectiveIsLightForBindings() => ResolveEffectiveIsLight();

    protected override IReadOnlyList<SettingsPageDescriptor<TaskManagerPage>> CreatePageDescriptors() =>
    [
        new(TaskManagerPage.Processes, Label: "Processes", BuildProcessesPage, TaskManagerGlyphCatalog.PROCESSES),
        new(TaskManagerPage.Performance, Label: "Performance", BuildPerformancePage,
            TaskManagerGlyphCatalog.PERFORMANCE),
        new(TaskManagerPage.AppHistory, Label: "App history", BuildAppHistoryPage, TaskManagerGlyphCatalog.APP_HISTORY),
        new(TaskManagerPage.StartupApps, Label: "Startup apps", BuildStartupAppsPage,
            TaskManagerGlyphCatalog.STARTUP_APPS),
        new(TaskManagerPage.Users, Label: "Users", BuildUsersPage, TaskManagerGlyphCatalog.USERS),
        new(TaskManagerPage.Services, Label: "Services", BuildServicesPage, TaskManagerGlyphCatalog.SERVICES),
        new(TaskManagerPage.Settings, Label: "Settings", BuildSettingsPage, SettingsNavigationGlyphs.Settings)
    ];

    protected override void Save() => _settings.Save();

    protected override void OnSettingsWindowClosed()
    {
#if DEBUG
        TaskManagerWindowResources.ResourcesReloaded -= OnAXAMLResourcesReloaded;
#endif
        _activePageLayout?.SetPageActive(false);
        _activePageWorkEnabled = false;
        ApplyIdleProcessSamplingPolicy();
        Closing -= OnWindowClosing;
        PropertyChanged -= OnWindowPropertyChanged;
        RemoveHandler(PointerPressedEvent, OnWindowPointerPressed);
        base.OnSettingsWindowClosed();
    }

    /// <summary>Rebuilds the shared shell after the app theme or settings change.</summary>
    internal void RefreshTheme()
    {
        Topmost = _settings.AlwaysOnTop;
        CapturePerformancePageState();
        RebuildShell(CurrentPageKey);
    }

    private void CapturePerformancePageState()
    {
        if (_activePageLayout is PerformancePage performancePage)
            _selectedPerformanceDeviceID = performancePage.SelectedDeviceID;
    }

#if DEBUG
    /// <summary>Applies current-page resources or rebuilds stateless Task Manager surfaces.</summary>
    private void OnAXAMLResourcesReloaded()
    {
        if (IsClosing) return;

        ApplyHotReloadedAXAMLWindowDimensions();
        RefreshSidebarCollapseControls();
        if (CurrentPageKey == TaskManagerPage.Processes && _processDetailsPage != null)
        {
            // AXAML hot-reload exception: compact-sidebar widths, margins, header icon, and
            // other construction-time shell geometry cannot be replaced here without discarding
            // live Processes editors and input; caret visuals are refreshed in place above
            _processDetailsPage.ApplyAXAMLResources(_taskManagerResources);
            return;
        }

        // Rebuild intentionally: most Task Manager controls copy construction-time AXAML values
        // AXAML hot-reload exception: in-flight generic-page actions remain owned by the disposed
        // page; transferring operation state safely would require ownership above the page
        CaptureHotReloadShellState();
        try
        {
            RefreshTheme();
            RestoreHotReloadShellState();
        }
        catch
        {
            _processHotReloadState = null;
            _tableHotReloadState = null;
            throw;
        }
    }

    private void ApplyHotReloadedAXAMLWindowDimensions()
    {
        double nextWidth = _taskManagerResources.AxamlTaskManagerWindow.Width;
        double nextHeight = _taskManagerResources.AxamlTaskManagerWindow.Height;
        double nextMinWidth = _taskManagerResources.AxamlTaskManagerWindow.MinWidth;
        double nextMinHeight = _taskManagerResources.AxamlTaskManagerWindow.MinHeight;

        if (nextWidth != _axamlWindowWidth) Width = nextWidth;
        if (nextHeight != _axamlWindowHeight) Height = nextHeight;
        if (nextMinWidth != _axamlWindowMinWidth) MinWidth = nextMinWidth;
        if (nextMinHeight != _axamlWindowMinHeight) MinHeight = nextMinHeight;

        _axamlWindowWidth = nextWidth;
        _axamlWindowHeight = nextHeight;
        _axamlWindowMinWidth = nextMinWidth;
        _axamlWindowMinHeight = nextMinHeight;
    }
#endif

    private void ApplyInitialAXAMLWindowDimensions()
    {
        Width = _taskManagerResources.AxamlTaskManagerWindow.Width;
        Height = _taskManagerResources.AxamlTaskManagerWindow.Height;
        MinWidth = _taskManagerResources.AxamlTaskManagerWindow.MinWidth;
        MinHeight = _taskManagerResources.AxamlTaskManagerWindow.MinHeight;
#if DEBUG
        _axamlWindowWidth = Width;
        _axamlWindowHeight = Height;
        _axamlWindowMinWidth = MinWidth;
        _axamlWindowMinHeight = MinHeight;
#endif
    }

    /// <summary>Starts one silent elevation attempt after normal startup is complete.</summary>
    internal void StartInitialElevatedTerminationAttempt()
    {
        if (_initialElevationAttemptConsumed ||
            _manualElevationPromptPending ||
            _allowClose ||
            !IsVisible)
            return;

        _initialElevationAttemptConsumed = true;
        IntPtr ownerWindowHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        _ = RunInitialElevatedTerminationAttemptAsync(ownerWindowHandle);
    }

    /// <summary>Allows app shutdown to close the otherwise warm, hide-on-close window.</summary>
    internal void RequestPermanentClose()
    {
        _allowClose = true;
        if (_settingsWindow != null)
        {
            _settingsWindow.Closed -= OnSettingsWindowClosed;
            _settingsWindow.Close();
            _settingsWindow = null;
        }

        Close();
    }

    private void ShowClassicSettingsWindow()
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.ShowAtDefaultPositionAndActivate();
            return;
        }

        _settingsWindow = new TaskManagerSettingsWindow(_settings, ShowUninstallerWindow);
        _settingsWindow.Closed += OnSettingsWindowClosed;
        _settingsWindow.ShowAtDefaultPositionAndActivate();
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is TaskManagerSettingsWindow settingsWindow)
            settingsWindow.Closed -= OnSettingsWindowClosed;
        if (ReferenceEquals(sender, _settingsWindow))
            _settingsWindow = null;
    }

    private void ShowUninstallerWindow(string installDirectory, InstallScope scope)
    {
        TaskManagerUninstallerWindow uninstaller = new(installDirectory, scope);
        Window owner = _settingsWindow != null ? _settingsWindow : this;
        uninstaller.Show(owner);
    }

    private Control BuildProcessesPage()
    {
        ProcessDetailsPage page = new(
            _snapshotService,
            _processIconService,
            _settings,
            Palette,
            _taskManagerResources,
            _processTerminationService.Arm,
            TryTerminateProcess,
            _processTerminationService.GetElevatedHelperStatus,
            RequestManualElevatedTermination,
            ConfirmEndTaskAsync,
            ConfirmRestartExplorerAsync,
            ConfirmDeleteSavedSearchAsync,
            RestartExplorerAsync,
            ReportMessage,
            StartProcess);
        return RegisterPage(page);
    }

    private Control BuildPerformancePage()
    {
        PerformancePage page = new(
            _settings,
            Palette,
            _taskManagerResources,
            _performanceSnapshotService,
            _selectedPerformanceDeviceID);
        return RegisterPage(page);
    }

    private Control BuildAppHistoryPage() => RegisterPage(new AppHistoryPage(
        _snapshotService,
        _processIconService,
        _appHistoryStore,
        _settings,
        Palette,
        _taskManagerResources,
        StartProcess));

    private Control BuildStartupAppsPage() => RegisterPage(new StartupAppsPage(
        new StartupAppsService(),
        _processIconService,
        _settings,
        Palette,
        _taskManagerResources,
        StartProcess,
        ReportMessage));

    private Control BuildUsersPage() => RegisterPage(new UsersPage(
        _snapshotService,
        _processIconService,
        _userSessionService,
        _settings,
        Palette,
        _taskManagerResources,
        StartProcess,
        ReportMessage));

    private Control BuildServicesPage() => RegisterPage(new ServicesPage(
        _windowsServiceManager,
        _processIconService,
        _settings,
        Palette,
        _taskManagerResources,
        StartProcess,
        ConfirmDisableServiceAsync,
        ReportMessage));

    private Control RegisterPage<TPage>(TPage page)
        where TPage : TaskManagerPageLayout, IDisposable
    {
        if (page is ITaskManagerSearchOverlayPage searchOverlayPage)
            searchOverlayPage.SetSearchCaptionButtonAreaWidth(TitleBarCaptionButtonAreaWidth);

        page.AttachedToVisualTree += OnPageAttached;
        page.DetachedFromVisualTree += OnPageDetached;
        AddPageCleanup(() =>
        {
            page.AttachedToVisualTree -= OnPageAttached;
            page.DetachedFromVisualTree -= OnPageDetached;
            DeactivatePage();
        });
        return OwnPageResource(page);

        void OnPageAttached(object? sender, VisualTreeAttachmentEventArgs eventArgs)
        {
            _activePageLayout?.SetPageActive(false);
            ApplyIdleProcessSamplingPolicy();
            _activePageLayout = page;
            _searchOverlayPage = page as ITaskManagerSearchOverlayPage;
            _processDetailsPage = page as ProcessDetailsPage;
            _processDetailsPage?.SetConfirmationOverlayVisible(_isConfirmationOverlayVisible);
            _activePageWorkEnabled = ShouldEnableActivePageWork();
            page.SetPageActive(_activePageWorkEnabled);
        }

        void DeactivatePage()
        {
            if (!ReferenceEquals(_activePageLayout, page)) return;

            page.SetPageActive(false);
            _activePageLayout = null;
            _searchOverlayPage = null;
            _processDetailsPage = null;
            _activePageWorkEnabled = false;
            ApplyIdleProcessSamplingPolicy();
        }

        void OnPageDetached(object? sender, VisualTreeAttachmentEventArgs eventArgs) =>
            DeactivatePage();
    }

    private bool ShouldEnableActivePageWork() =>
        !_allowClose && IsVisible && WindowState != WindowState.Minimized;

    private void UpdateActivePageActivity()
    {
        bool shouldEnable = ShouldEnableActivePageWork();
        if (_activePageWorkEnabled == shouldEnable) return;

        _activePageWorkEnabled = shouldEnable;
        if (!shouldEnable)
        {
            _activePageLayout?.SetPageActive(false);
            ApplyIdleProcessSamplingPolicy();
            return;
        }

        ApplyIdleProcessSamplingPolicy();
        _activePageLayout?.SetPageActive(true);
    }

    private void ApplyIdleProcessSamplingPolicy()
    {
        _snapshotService.SetActiveSchema(IdleProcessSchema);
        _snapshotService.SetWarmProcesses(
            IdleProcessSchema.VisibleMask,
            NoWarmProcessIDs,
            count: 0,
            sampleEveryProcess: false);
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        TaskManagerPageLayout? page = _activePageLayout;
        PointerPoint pointerPoint = eventArgs.GetCurrentPoint(this);
        if (pointerPoint.Properties.IsLeftButtonPressed)
            _processDetailsPage?.ClearSelectionForExternalPointerSource(eventArgs.Source);

        if (eventArgs.Handled
            || page == null
            || !pointerPoint.Properties.IsLeftButtonPressed
            || (eventArgs.KeyModifiers & KeyModifiers.Control) != 0
            || !page.TryGetMainContentTop(this, out double contentTop)
            || pointerPoint.Position.Y >= contentTop
            || IsInteractiveHeaderControl(eventArgs.Source))
            return;

        if (eventArgs.ClickCount == 2)
        {
            ResetRestoredWindowDrag();
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            PrepareRestoredWindowDrag();
            BeginMoveDrag(eventArgs);
        }

        eventArgs.Handled = true;
    }

    private void PrepareRestoredWindowDrag()
    {
        ResetRestoredWindowDrag();
        _avoidSearchBoxDuringRestoreDrag = WindowState == WindowState.Maximized
                                           && _searchOverlayPage != null;
    }

    private IntPtr WindowDragWndProcHook(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        try
        {
            switch (message)
            {
                case WindowMessageMoving:
                    ApplyRestoredWindowDragOffset(windowHandle, lParam);
                    break;

                case WindowMessageExitSizeMove:
                    ResetRestoredWindowDrag();
                    break;
            }
        }
        catch (Exception exception)
        {
            ResetRestoredWindowDrag();
            TADNLog.Log($"Task Manager restored-window drag adjustment failed: {exception}");
        }

        return IntPtr.Zero;
    }

    private unsafe void ApplyRestoredWindowDragOffset(
        IntPtr windowHandle,
        IntPtr rectanglePointer)
    {
        ITaskManagerSearchOverlayPage? searchOverlayPage = _searchOverlayPage;
        if (!_avoidSearchBoxDuringRestoreDrag
            || searchOverlayPage == null
            || rectanglePointer == IntPtr.Zero)
            return;

        RECT* proposedBounds = (RECT*)rectanglePointer;
        if (!_restoreDragSearchRangeResolved)
        {
            if (!searchOverlayPage.TryGetSearchDragRegionPixelWidths(
                    out int searchWidth,
                    out int leadingActionWidth))
                return;

            int proposedWidth = proposedBounds->Right - proposedBounds->Left;
            if (proposedWidth <= searchWidth) return;

            double renderScaling = RenderScaling;
            if (!double.IsFinite(renderScaling) || renderScaling <= 0)
                renderScaling = 1;
            int pageContentLeft = 0;
            if (_settings.LeftAlignProcessSearchBar)
            {
                double proposedWidthDips = proposedWidth / renderScaling;
                double pageContentLeftDips = ResolvePageContentLeftInset(proposedWidthDips);
                pageContentLeft = (int)Math.Round(
                    pageContentLeftDips * renderScaling,
                    MidpointRounding.AwayFromZero);
                pageContentLeft += ResolveClientLeftInset(windowHandle);
            }

            int captionButtonAreaWidth = (int)Math.Ceiling(
                TitleBarCaptionButtonAreaWidth * renderScaling);
            int captionSpacing = (int)Math.Ceiling(
                _taskManagerResources.AxamlTaskManagerDetails.SearchCaptionSpacing * renderScaling);

            RestoredWindowDragSearchRange searchRange =
                RestoredWindowDragGeometry.CalculateSearchRangeWithinWindow(
                    proposedWidth,
                    searchWidth,
                    leadingActionWidth,
                    _settings.LeftAlignProcessSearchBar,
                    pageContentLeft,
                    captionButtonAreaWidth,
                    captionSpacing);
            _restoreDragSearchLeftWithinWindow = searchRange.Left;
            _restoreDragSearchRightWithinWindow = searchRange.Right;
            _restoreDragSearchRangeResolved = true;
        }

        if (!User32.GetCursorPos(out User32.POINT cursorPosition)) return;

        int horizontalOffset = RestoredWindowDragGeometry.CalculateHorizontalWindowOffset(
            cursorPosition.X,
            proposedBounds->Left,
            _restoreDragSearchLeftWithinWindow,
            _restoreDragSearchRightWithinWindow,
            _taskManagerResources.AxamlTaskManagerWindow.SearchDragOutsideMarginPixels);
        // Adjust the proposed rectangle so the native move loop retains the active cursor grab
        proposedBounds->Left += horizontalOffset;
        proposedBounds->Right += horizontalOffset;
    }

    /// <summary>Gets the horizontal native-frame inset between the window and client origins.</summary>
    private static int ResolveClientLeftInset(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero
            || !User32.GetWindowRect(windowHandle, out RECT windowBounds))
            return 0;

        User32.POINT screenOriginInClientCoordinates = default;
        if (!User32.ScreenToClient(windowHandle, ref screenOriginInClientCoordinates)) return 0;

        int clientOriginScreenX = -screenOriginInClientCoordinates.X;
        return clientOriginScreenX - windowBounds.Left;
    }

    private void ResetRestoredWindowDrag()
    {
        _avoidSearchBoxDuringRestoreDrag = false;
        _restoreDragSearchRangeResolved = false;
        _restoreDragSearchLeftWithinWindow = 0;
        _restoreDragSearchRightWithinWindow = 0;
    }

    private void OnWindowClosedForDragHook(object? sender, EventArgs eventArgs)
    {
        ResetRestoredWindowDrag();
        if (_windowDragWndProcHookAttached && OperatingSystem.IsWindows())
        {
            Win32Properties.RemoveWndProcHookCallback(this, _windowDragWndProcHook);
            _windowDragWndProcHookAttached = false;
        }

        Closed -= OnWindowClosedForDragHook;
    }

    private static bool IsInteractiveHeaderControl(object? source)
    {
        if (source is not Visual visual) return false;

        Visual? current = visual;
        while (current != null)
        {
            if (current is TextBox
                or SettingsButton
                or SettingsToggle
                or SettingsNavItem
                or ProcessSavedSearchController.InsetGlyphButton)
                return true;
            current = current.GetVisualParent();
        }

        return false;
    }

    private StackPanel BuildSettingsPage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack(title: "Settings", palette);
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader(text: "Processes", palette));
        stack.Children.Add(ComboCard(
            title: "Process grouping style",
            description:
            "Choose how processes are organized when Group processes is enabled on the Processes page.",
            [
                (nameof(ProcessGroupingStyle.ParentProcess), "Parent process"),
                (nameof(ProcessGroupingStyle.Semantic), "Semantic application")
            ],
            _settings.ProcessGroupingStyle.ToString(),
            tag =>
            {
                if (Enum.TryParse(tag, out ProcessGroupingStyle value))
                    _settings.ProcessGroupingStyle = value;
            },
            palette));
        stack.Children.Add(BoolCard(
            title: "Live column resizing",
            description:
            "Update Processes column widths and positions while dragging a divider. Turn this off to show a resize guide and apply the width on release.",
            _settings.EnableLiveDetailsColumnResizing,
            enabled => _settings.EnableLiveDetailsColumnResizing = enabled,
            palette));
        return stack;
    }

    private bool TryTerminateProcess(ProcessTerminationTarget target, out string errorMessage) =>
        _processTerminationService.TryTerminate(target, out errorMessage);

    private async Task RunInitialElevatedTerminationAttemptAsync(IntPtr ownerWindowHandle)
    {
        try
        {
            _ = await _processTerminationService.EnableElevatedHelperAsync(ownerWindowHandle);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Task Manager initial elevated termination attempt failed: {exception}");
        }
    }

    private void RequestManualElevatedTermination()
    {
        // Manual choice owns elevation once its explanation starts, including a later Not now result
        _initialElevationAttemptConsumed = true;
        _ = PromptForManualElevatedTerminationAsync();
    }

    private async Task PromptForManualElevatedTerminationAsync()
    {
        if (_allowClose || _manualElevationPromptPending || !IsVisible) return;

        ElevatedHelperStatus currentStatus = _processTerminationService.GetElevatedHelperStatus();
        if (currentStatus.State is ElevatedHelperState.Starting or
            ElevatedHelperState.Ready or
            ElevatedHelperState.Disposed)
            return;

        _manualElevationPromptPending = true;
        try
        {
            bool enable = await ConfirmAsync(
                title: "Enable elevated process termination?",
                ElevatedTerminationExplanation,
                confirmText: "Enable",
                cancelText: "Not now");
            if (!enable || _allowClose || !IsVisible) return;

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();

            IntPtr ownerWindowHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            ElevatedHelperStatus completedStatus = await _processTerminationService
                .EnableElevatedHelperAsync(ownerWindowHandle);
            if (_allowClose || !IsVisible) return;

            switch (completedStatus.State)
            {
                case ElevatedHelperState.Declined:
                    await ShowMessage(
                        title: "Elevated termination not enabled",
                        "Windows administrator approval was canceled. Task Manager will continue with standard " +
                        "process permissions.");
                    break;
                case ElevatedHelperState.Failed:
                    await ShowMessage(
                        title: "Elevated termination failed",
                        string.IsNullOrWhiteSpace(completedStatus.ErrorMessage)
                            ? "The elevated termination helper could not be started."
                            : completedStatus.ErrorMessage);
                    break;
            }
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Task Manager manual elevated termination prompt failed: {exception}");
        }
        finally
        {
            _manualElevationPromptPending = false;
        }
    }

    private Task<bool> ConfirmEndTaskAsync(ProcessEndTaskRequest request)
    {
        if (request.Count > 1)
        {
            return ConfirmAsync(
                $"Do you want to end {request.Count} selected processes?",
                EndTasksConfirmationMessage,
                confirmText: "End processes",
                cancelText: "Cancel");
        }

        ProcessEndTaskItem process = request.Processes[0];
        string processName = string.IsNullOrWhiteSpace(process.ProcessName)
            ? $"PID {process.Target.ProcessID}"
            : process.ProcessName;
        return ConfirmAsync(
            $"Do you want to end {processName}?",
            EndTaskConfirmationMessage,
            confirmText: "End process",
            cancelText: "Cancel");
    }

    private Task<bool> ConfirmDeleteSavedSearchAsync(ProcessSavedSearch savedSearch)
    {
        ArgumentNullException.ThrowIfNull(savedSearch);
        return ConfirmAsync(
            title: "Delete saved search?",
            $"\"{savedSearch.Name}\" uses a regular expression. Delete this saved search?",
            confirmText: "Delete",
            cancelText: "Cancel");
    }

    private Task<bool> ConfirmRestartExplorerAsync()
    {
        if (_settings.SkipRestartExplorerConfirmation) return Task.FromResult(true);

        return ConfirmAsync(
            title: "Restart Windows Explorer?",
            RestartExplorerConfirmationMessage,
            confirmText: "Restart explorer",
            cancelText: "Cancel");
    }

    private Task<bool> ConfirmDisableServiceAsync(WindowsServiceSnapshot service)
    {
        ArgumentNullException.ThrowIfNull(service);
        string serviceLabel = string.IsNullOrWhiteSpace(service.DisplayName)
            ? service.ServiceName
            : service.DisplayName;
        return ConfirmAsync(
            title: "Disable service?",
            $"Windows will not start {serviceLabel} again until its startup type is changed. "
            + "Disabling a running service does not stop its current instance.",
            confirmText: "Disable",
            cancelText: "Cancel");
    }

    private Task<ExplorerRestartResult> RestartExplorerAsync() =>
        Task.Run(() => CriticalProcessActions.RestartExplorer(TryTerminateProcess));

    private void ReportMessage(string title, string message) => _ = ShowMessage(title, message);

    private bool StartProcess(string command)
    {
        if (CriticalProcessActions.TryStart(command, out string errorMessage)) return true;

        _ = ShowMessage(title: "Run new task failed", errorMessage);
        return false;
    }

    private bool ResolveEffectiveIsLight() => _settings.ThemeMode switch
    {
        TrayAppDotNETThemeMode.Light => true,
        TrayAppDotNETThemeMode.Dark => false,
        _ => _theme.IsLightTheme
    };

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == IsVisibleProperty || change.Property == WindowStateProperty)
            UpdateActivePageActivity();

        if (_allowClose
            || !_settings.MinimizeToTray
            || change.Property != WindowStateProperty
            || WindowState != WindowState.Minimized)
            return;

        Hide();
        WindowState = WindowState.Normal;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (_allowClose || Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime)
            return;

        eventArgs.Cancel = true;
        if (_settings.CloseToTray)
        {
            Hide();
            return;
        }

        if (_exitRequested) return;
        _exitRequested = true;
        Dispatcher.UIThread.Post(_exitApplication);
    }

    private sealed class TaskManagerSidebarHeader : Grid
    {
        private readonly TextBlock _title;
        private readonly Control _icon;

        public TaskManagerSidebarHeader(TextBlock title, Control icon)
        {
            _title = title;
            _icon = icon;
            Children.Add(_title);
            Children.Add(_icon);
        }

        public void SetCompact(bool isCompact)
        {
            _title.Opacity = isCompact ? 0 : 1;
            _title.IsHitTestVisible = !isCompact;
            _icon.Opacity = isCompact ? 1 : 0;
        }
    }

    private sealed class TaskManagerSidebarCaretIcon : Grid, ISettingsNavigationGlyphIcon
    {
        private readonly TextBlock _caret;
        private readonly TaskManagerWindowResources _resources;
        private Color _iconColor;

        public TaskManagerSidebarCaretIcon(
            Glyph glyph,
            SettingsPalette palette,
            double fontSize,
            double opacity,
            TaskManagerWindowResources resources)
        {
            _iconColor = palette.Foreground;
            _resources = resources;
            RenderTransformOrigin = RelativePoint.Center;
            _caret = TrayAppDotNETSettingsUI.Text(string.Empty, palette, fontSize);
            _caret.HorizontalAlignment = HorizontalAlignment.Center;
            _caret.VerticalAlignment = VerticalAlignment.Center;
            _caret.Opacity = opacity;
            Children.Add(_caret);
            SetGlyph(glyph);
        }

        public Color IconColor
        {
            get => _iconColor;
            set
            {
                if (_iconColor == value) return;

                _iconColor = value;
                _caret.Foreground = TrayAppDotNETSettingsUI.Brush(value);
            }
        }

        public void SetGlyph(Glyph glyph)
        {
            _caret.RenderTransform = null;
            GlyphApplicator.ApplyTo(_caret, glyph);

            bool isLeftCaret = string.Equals(
                glyph.Text,
                TaskManagerGlyphCatalog.CARET_LEFT.Text,
                StringComparison.Ordinal);
            bool isRightCaret = string.Equals(
                glyph.Text,
                TaskManagerGlyphCatalog.CARET_RIGHT.Text,
                StringComparison.Ordinal);
            if (!isLeftCaret && !isRightCaret) return;

            double translateY = isLeftCaret
                ? _resources.AxamlTaskManagerWindow.SidebarNavigationCaretLeftTranslateY
                : _resources.AxamlTaskManagerWindow.SidebarNavigationCaretRightTranslateY;
            // The host rotates 90 degrees, so local X becomes screen-space Y
            double translateX = _resources.AxamlTaskManagerWindow.SidebarNavigationCaretTranslateY;

            _caret.RenderTransform = translateX == 0 && translateY == 0
                ? null
                : new TranslateTransform(translateX, translateY);
        }
    }

    private sealed class TaskManagerSidebar(TaskManagerWindowResources resources) : SettingsSidebar(
        resources.AxamlTaskManagerWindow.SidebarHeaderMargin,
        resources.AxamlTaskManagerWindow.SidebarNavigationMargin,
        resources.AxamlTaskManagerWindow.SidebarFooterMargin);
}
