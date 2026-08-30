using Avalonia;
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
    private const int SearchDragOutsideMarginPixels = 8;
    private const string EndTaskConfirmationMessage =
        "If an open program is associated with this process, it will close and you will lose any unsaved data. " +
        "If you end a system process, it might result in system instability. Are you sure you want to continue?";
    private const string ElevatedTerminationExplanation =
        "Task Manager can start TaskManagerTrayAppDotNET.KillHelper.exe with administrator privileges so it can " +
        "end elevated processes. Windows may display a security warning and a UAC prompt. If you cancel, Task " +
        "Manager will continue running with standard process permissions.";

    private static readonly Glyph ProcessesGlyph = Glyph.Fluent("\uECAA");
    private static readonly Glyph PerformanceGlyph = Glyph.Fluent("\uE9D9");
    private static readonly Glyph AppHistoryGlyph = Glyph.Fluent("\uE81C");
    private static readonly Glyph StartupAppsGlyph = Glyph.Fluent("\uE768");
    private static readonly Glyph UsersGlyph = Glyph.Fluent("\uE716");
    private static readonly Glyph ServicesGlyph = Glyph.Fluent("\uEA86");
    private static readonly Glyph GlobalNavigationButtonGlyph = Glyph.Fluent(
        "\uE700",
        FontWeight.Normal);

    private readonly AppSettings _settings;
    private readonly AppTheme _theme;
    private readonly ProcessSnapshotService _snapshotService;
    private readonly PerformanceSnapshotService _performanceSnapshotService;
    private readonly ProcessIconService _processIconService;
    private readonly ProcessTerminationService _processTerminationService;
    private readonly Action _exitApplication;
    private readonly TaskManagerWindowResources _taskManagerResources = TaskManagerWindowResources.Current;
    private readonly Win32Properties.CustomWndProcHookCallback _windowDragWndProcHook;
    private TaskManagerPageLayout? _activePageLayout;
    private ProcessDetailsPage? _processDetailsPage;
    private TaskManagerSettingsWindow? _settingsWindow;
    private string? _selectedPerformanceDeviceID;
    private bool _windowDragWndProcHookAttached;
    private bool _avoidSearchBoxDuringRestoreDrag;
    private bool _restoreDragSearchRangeResolved;
    private int _restoreDragSearchLeftWithinWindow;
    private int _restoreDragSearchRightWithinWindow;
    private bool _allowClose;
    private bool _initialElevationAttemptConsumed;
    private bool _manualElevationPromptPending;
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
        Resources.MergedDictionaries.Add(_taskManagerResources);

        ConfigureSettingsWindow(Constants.DisplayName, icon: null);
        Width = _taskManagerResources.AxamlTaskManagerWindow.Width;
        Height = _taskManagerResources.AxamlTaskManagerWindow.Height;
        MinWidth = _taskManagerResources.AxamlTaskManagerWindow.MinWidth;
        MinHeight = _taskManagerResources.AxamlTaskManagerWindow.MinHeight;
        Topmost = settings.AlwaysOnTop;
        Closed += OnWindowClosedForDragHook;
        Closing += OnWindowClosing;
        PropertyChanged += OnWindowPropertyChanged;
        AddHandler(
            PointerPressedEvent,
            OnHeaderBackgroundPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        InitializeSettingsShell();
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
        pageKey is TaskManagerPage.Processes or TaskManagerPage.Performance;
    protected override Control? ResolvePageOverlay(Control pageRoot) =>
        pageRoot is TaskManagerPageLayout page ? page.PageOverlay : null;
    protected override bool PageOverlayAlignsToContentArea(Control pageRoot) =>
        _settings.LeftAlignProcessSearchBar && pageRoot is ProcessDetailsPage;
    protected override bool EnableResponsiveSidebarCollapse => _settings.CollapseSidebarWhenNarrow;
    protected override double SidebarCollapseThreshold =>
        _taskManagerResources.AxamlTaskManagerWindow.SidebarCollapseThreshold;
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

    protected override void OnOpened(EventArgs eventArgs)
    {
        // Register before the shared shell hook so its handled-message return value remains last
        if (!_windowDragWndProcHookAttached && OperatingSystem.IsWindows())
        {
            Win32Properties.AddWndProcHookCallback(this, _windowDragWndProcHook);
            _windowDragWndProcHookAttached = true;
        }

        base.OnOpened(eventArgs);
    }

    protected override Control BuildSidebarHeader(TextBlock title, SettingsPalette palette)
    {
        title.VerticalAlignment = VerticalAlignment.Center;
        double buttonSize =
            _taskManagerResources.AxamlTaskManagerWindow.GlobalNavigationButtonSize;
        SettingsButton globalNavigationButton = new(
            GlobalNavigationButtonGlyph,
            palette,
            transparentBase: true)
        {
            Width = buttonSize,
            Height = buttonSize,
            MinHeight = buttonSize,
            Padding =
                _taskManagerResources.AxamlTaskManagerWindow.GlobalNavigationButtonPadding
        };
        globalNavigationButton.Label.FontSize =
            _taskManagerResources.AxamlTaskManagerWindow.GlobalNavigationButtonGlyphFontSize;

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing =
                _taskManagerResources.AxamlTaskManagerWindow.GlobalNavigationButtonSpacing,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { title, globalNavigationButton }
        };
    }

    protected override SettingsPalette ResolvePalette() =>
        VolumeSettingsPalette.Create(_theme, _settings, ResolveEffectiveIsLight());

    protected override bool ResolveEffectiveIsLightForBindings() => ResolveEffectiveIsLight();

    protected override IReadOnlyList<SettingsPageDescriptor<TaskManagerPage>> CreatePageDescriptors() =>
    [
        new(TaskManagerPage.Processes, "Processes", BuildProcessesPage, ProcessesGlyph),
        new(TaskManagerPage.Performance, "Performance", BuildPerformancePage, PerformanceGlyph),
        new(TaskManagerPage.AppHistory, "App history", () => BuildPlaceholderPage("App history"), AppHistoryGlyph),
        new(TaskManagerPage.StartupApps, "Startup apps", () => BuildPlaceholderPage("Startup apps"), StartupAppsGlyph),
        new(TaskManagerPage.Users, "Users", () => BuildPlaceholderPage("Users"), UsersGlyph),
        new(TaskManagerPage.Services, "Services", () => BuildPlaceholderPage("Services"), ServicesGlyph),
        new(TaskManagerPage.Settings, "Settings", BuildSettingsPage, SettingsNavigationGlyphs.Settings)
    ];

    protected override void Save() => _settings.Save();

    protected override void OnSettingsWindowClosed()
    {
        Closing -= OnWindowClosing;
        PropertyChanged -= OnWindowPropertyChanged;
        RemoveHandler(PointerPressedEvent, OnHeaderBackgroundPointerPressed);
        base.OnSettingsWindowClosed();
    }

    /// <summary>Rebuilds the shared shell after the app theme or settings change.</summary>
    internal void RefreshTheme()
    {
        Topmost = _settings.AlwaysOnTop;
        PerformancePage? performancePage = _activePageLayout as PerformancePage;
        if (performancePage != null)
            _selectedPerformanceDeviceID = performancePage.SelectedDeviceID;
        RebuildShell(CurrentPageKey);
    }

    /// <summary>Starts one silent elevation attempt after normal startup is complete.</summary>
    internal void StartInitialElevatedTerminationAttempt()
    {
        if (_initialElevationAttemptConsumed ||
            _manualElevationPromptPending ||
            _allowClose ||
            !IsVisible)
        {
            return;
        }

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
            ReportMessage,
            StartProcess);
        _processDetailsPage = page;
        _activePageLayout = page;
        AddPageCleanup(() =>
        {
            if (ReferenceEquals(_processDetailsPage, page))
                _processDetailsPage = null;
            if (ReferenceEquals(_activePageLayout, page))
                _activePageLayout = null;
        });
        return OwnPageResource(page);
    }

    private Control BuildPerformancePage()
    {
        PerformancePage page = new(
            _settings,
            Palette,
            _taskManagerResources,
            _performanceSnapshotService,
            _selectedPerformanceDeviceID);
        _activePageLayout = page;
        AddPageCleanup(() =>
        {
            if (ReferenceEquals(_activePageLayout, page))
                _activePageLayout = null;
        });
        return OwnPageResource(page);
    }

    private void OnHeaderBackgroundPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        TaskManagerPageLayout? page = _activePageLayout;
        PointerPoint pointerPoint = eventArgs.GetCurrentPoint(this);
        if (eventArgs.Handled
            || page == null
            || !pointerPoint.Properties.IsLeftButtonPressed
            || (eventArgs.KeyModifiers & KeyModifiers.Control) != 0
            || !page.TryGetMainContentTop(this, out double contentTop)
            || pointerPoint.Position.Y >= contentTop
            || IsInteractiveHeaderControl(eventArgs.Source))
        {
            return;
        }

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
                                           && _processDetailsPage != null;
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
        ProcessDetailsPage? processDetailsPage = _processDetailsPage;
        if (!_avoidSearchBoxDuringRestoreDrag
            || processDetailsPage == null
            || rectanglePointer == IntPtr.Zero)
        {
            return;
        }

        RECT* proposedBounds = (RECT*)rectanglePointer;
        if (!_restoreDragSearchRangeResolved)
        {
            if (!processDetailsPage.TryGetSearchBoxPixelWidth(out int searchWidth)) return;

            int proposedWidth = proposedBounds->Right - proposedBounds->Left;
            if (proposedWidth <= searchWidth) return;

            int pageContentLeft = 0;
            if (_settings.LeftAlignProcessSearchBar)
            {
                double renderScaling = RenderScaling;
                if (!double.IsFinite(renderScaling) || renderScaling <= 0)
                    renderScaling = 1;
                double proposedWidthDips = proposedWidth / renderScaling;
                double pageContentLeftDips = ResolvePageContentLeftInset(proposedWidthDips);
                pageContentLeft = (int)Math.Round(
                    pageContentLeftDips * renderScaling,
                    MidpointRounding.AwayFromZero);
                pageContentLeft += ResolveClientLeftInset(windowHandle);
            }

            _restoreDragSearchLeftWithinWindow =
                RestoredWindowDragGeometry.CalculateSearchLeftWithinWindow(
                    proposedWidth,
                    searchWidth,
                    _settings.LeftAlignProcessSearchBar,
                    pageContentLeft);
            _restoreDragSearchRightWithinWindow =
                _restoreDragSearchLeftWithinWindow + searchWidth;
            _restoreDragSearchRangeResolved = true;
        }

        if (!User32.GetCursorPos(out User32.POINT cursorPosition)) return;

        int horizontalOffset = RestoredWindowDragGeometry.CalculateHorizontalWindowOffset(
            cursorPosition.X,
            proposedBounds->Left,
            _restoreDragSearchLeftWithinWindow,
            _restoreDragSearchRightWithinWindow,
            SearchDragOutsideMarginPixels);
        // Adjust the proposed rectangle so the native move loop retains the active cursor grab
        proposedBounds->Left += horizontalOffset;
        proposedBounds->Right += horizontalOffset;
    }

    /// <summary>Gets the horizontal native-frame inset between the window and client origins.</summary>
    private static int ResolveClientLeftInset(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero
            || !User32.GetWindowRect(windowHandle, out RECT windowBounds))
        {
            return 0;
        }

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
            if (current is TextBox or SettingsButton or SettingsToggle or SettingsNavItem)
                return true;
            current = current.GetVisualParent();
        }

        return false;
    }

    private StackPanel BuildSettingsPage()
    {
        SettingsPalette palette = Palette;
        StackPanel stack = PageStack("Settings", palette);
        stack.Margin = _taskManagerResources.AxamlTaskManagerDetails.PlaceholderMargin;
        stack.Children.Add(TrayAppDotNETSettingsUI.SubsectionHeader("Processes", palette));
        stack.Children.Add(BoolCard(
            "Live column resizing",
            "Update Processes column widths and positions while dragging a divider. Turn this off to show a resize guide and apply the width on release.",
            _settings.EnableLiveDetailsColumnResizing,
            enabled => _settings.EnableLiveDetailsColumnResizing = enabled,
            palette));
        return stack;
    }

    private TaskManagerPageLayout BuildPlaceholderPage(string pageName)
    {
        SettingsPalette palette = Palette;
        TaskManagerPageLayout page = new(pageName, palette, _taskManagerResources);
        _activePageLayout = page;
        AddPageCleanup(() =>
        {
            if (ReferenceEquals(_activePageLayout, page))
                _activePageLayout = null;
        });

        StackPanel stack = new();
        stack.Margin = _taskManagerResources.AxamlTaskManagerDetails.PlaceholderMargin;
        TextBlock description = TrayAppDotNETSettingsUI.DescriptionText(
            "This page is intentionally a shell in the initial implementation.",
            palette);
        stack.Children.Add(RawCard(description, palette));
        page.MainContent.Children.Add(stack);
        return page;
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
        {
            return;
        }

        _manualElevationPromptPending = true;
        try
        {
            bool enable = await ConfirmAsync(
                "Enable elevated process termination?",
                ElevatedTerminationExplanation,
                "Enable",
                "Not now");
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
                        "Elevated termination not enabled",
                        "Windows administrator approval was canceled. Task Manager will continue with standard " +
                        "process permissions.");
                    break;
                case ElevatedHelperState.Failed:
                    await ShowMessage(
                        "Elevated termination failed",
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
        string processName = string.IsNullOrWhiteSpace(request.ProcessName)
            ? $"PID {request.Target.ProcessID}"
            : request.ProcessName;
        return ConfirmAsync(
            $"Do you want to end {processName}?",
            EndTaskConfirmationMessage,
            "End process",
            "Cancel");
    }

    private void ReportMessage(string title, string message) => _ = ShowMessage(title, message);

    private bool StartProcess(string command)
    {
        if (CriticalProcessActions.TryStart(command, out string errorMessage)) return true;

        _ = ShowMessage("Run new task failed", errorMessage);
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
        if (_allowClose
            || !_settings.MinimizeToTray
            || change.Property != WindowStateProperty
            || WindowState != WindowState.Minimized)
        {
            return;
        }

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
}
