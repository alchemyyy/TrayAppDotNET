#if DEBUG
using GlyphCatalogHotReload = TrayAppDotNETCommon.Visuals.GlyphCatalogHotReload;
#endif
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BatteryTrayAppDotNET.Services;
using FlyoutPowerMode = BatteryTrayAppDotNET.Models.BatteryPowerMode;
using TrayAppDotNETCommon.UI.ControlMapping;
using Glyph = TrayAppDotNETCommon.Visuals.Glyph;
using GlyphApplicator = TrayAppDotNETCommon.Visuals.GlyphApplicator;

namespace BatteryTrayAppDotNET.UI.Flyout;

public sealed class BatteryFlyoutWindow : FlyoutWindowCommon
{
    private const double DragThreshold = 4;
    private const double HeaderHeight = 40;
    private const double HeaderIconButtonWidth = 40;
    private const double HeaderIconButtonHeight = 32;
    private const double HeaderIconButtonFontSize = 18;
    private const double HeaderPowerIconButtonFontSize = HeaderIconButtonFontSize - 4;
    private const double UndockButtonWidth = 32;
    private const double UndockButtonHeight = 32;
    private const double UndockButtonFontSize = 20;
    private const double UndockButtonGlyphLineHeight = 26;

    private static readonly Thickness HeaderPadding = new(left: 12, top: 4, right: 12, bottom: 4);
    private static readonly Thickness UndockButtonFloatingMargin = new(left: 0, top: 8, right: 8, bottom: 0);

    private static readonly CornerRadius HeaderTopCornerRadius =
        new(topLeft: 7, topRight: 7, bottomRight: 0, bottomLeft: 0);

    private static readonly CornerRadius HeaderBottomCornerRadius =
        new(topLeft: 0, topRight: 0, bottomRight: 7, bottomLeft: 7);

    private static readonly CornerRadius HeaderIconButtonCornerRadius = new(4);
    private static readonly CornerRadius UndockButtonCornerRadius = new(4);

    private readonly BatteryMonitorService _batteryMonitor;
    private readonly BatteryPowerModeService _powerModes;
    private readonly AppSettings _settings;
    private readonly Action _openSettings;
    private BatteryHealthWindow? _healthWindow;
    private FlyoutUndockButtonController? _undockButtonController;
    private LiveContent? _live;
    private bool? _energySaverAlwaysEnabled;
    private bool _energySaverReadFailed;
    private bool? _energySaverRequest;
    private bool _energySaverWriteRunning;
    private bool _energySaverAwaitingConfirmation;
    private bool _energySaverConfirmationReadDone;
    private bool _energySaverReadRunning;
    private long _energySaverStatusRevision;
    private bool _liveUpdateQueued;

    private static BatteryFlyoutResources.FlyoutAxamlProperties Layout => BatteryFlyoutResources.Current.AxamlFlyout;

    public BatteryFlyoutWindow(BatteryMonitorService batteryMonitor, AppSettings settings, Action openSettings,
        BatteryPowerModeService? powerModes = null)
        : base(ControlMap.Flyout.Close)
    {
        _batteryMonitor = batteryMonitor;
        // The application shares its service across warm-window eviction and recreation.
        // Standalone hosts can inject their own service or let this window own one.
        _powerModes = powerModes ?? WindowResources.Own(new BatteryPowerModeService());
        _settings = settings;
        _openSettings = openSettings;
        InitializeDocking(_settings);

        SetFixedFlyoutWidth(Layout.WindowWidth);
        this.MapTo(ControlMap.Flyout.ID);

        _batteryMonitor.StateChanged += OnBatteryStateChanged;
        WindowResources.Add(() => _batteryMonitor.StateChanged -= OnBatteryStateChanged);
        _powerModes.StateChanged += OnPowerModeStateChanged;
        WindowResources.Add(() => _powerModes.StateChanged -= OnPowerModeStateChanged);
        _settings.Changed += OnSettingsChanged;
        WindowResources.Add(() => _settings.Changed -= OnSettingsChanged);
#if DEBUG
        GlyphCatalogHotReload.ResourcesReloaded += OnGlyphCatalogResourcesReloaded;
        WindowResources.Add(() => GlyphCatalogHotReload.ResourcesReloaded -= OnGlyphCatalogResourcesReloaded);
        CommonAXAMLHotReload.ResourcesReloaded += OnGlyphCatalogResourcesReloaded;
        WindowResources.Add(() => CommonAXAMLHotReload.ResourcesReloaded -= OnGlyphCatalogResourcesReloaded);
        BatteryFlyoutResources.ResourcesReloaded += OnGlyphCatalogResourcesReloaded;
        WindowResources.Add(() => BatteryFlyoutResources.ResourcesReloaded -= OnGlyphCatalogResourcesReloaded);
#endif
        RefreshPowerModes();
        RefreshEnergySaverStatus();
        RebuildNow();
    }

    protected override void OnShowing()
    {
        RefreshPowerModes();
        RefreshEnergySaverStatus();
    }

    // A gesture owns the power mode slider until it completes
    protected override bool IsContentInteractionActive => _live?.PowerMode.Slider.IsUserAdjusting == true;

    private void RefreshPowerModes() =>
        _powerModes.RefreshAsync().ForgetLogged("BatteryFlyoutWindow.RefreshPowerModes");

    private void OnBatteryStateChanged()
    {
        if (IsWindowClosed) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnBatteryStateChanged, DispatcherPriority.Background);
            return;
        }

        // Readings arrive every few seconds. They update values in place so hover, focus, and open tooltips
        // survive; only structural changes rebuild the content.
        TryReleaseEnergySaverRequest(snapshotArrived: true);
        QueueLiveUpdate();

        // Mode and Energy Saver changes made elsewhere reach an open flyout with the next reading.
        // A hidden flyout reads both again when it is shown, so it runs no power plan queries in the background.
        if (!IsVisible) return;
        RefreshPowerModes();
        RefreshEnergySaverStatus();
    }

    private void OnPowerModeStateChanged()
    {
        // Raised on worker threads while the service applies or reads a mode.
        // Read Energy Saver back once a switch settles in case its per-scheme threshold did not carry over.
        if (!_powerModes.IsApplying) RefreshEnergySaverStatus(invalidate: true);
        QueueLiveUpdate();
    }

    private void RefreshEnergySaverStatus(bool invalidate = false)
    {
        if (IsWindowClosed) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => RefreshEnergySaverStatus(invalidate), DispatcherPriority.Background);
            return;
        }

        if (invalidate) ++_energySaverStatusRevision;
        // A running write reads the result itself when it finishes
        if (_energySaverReadRunning || _energySaverWriteRunning) return;
        _energySaverReadRunning = true;
        RefreshEnergySaverStatusAsync(_energySaverStatusRevision)
            .ForgetLogged("BatteryFlyoutWindow.RefreshEnergySaverStatus");
    }

    private async Task RefreshEnergySaverStatusAsync(long revision)
    {
        CancellationToken cancellationToken = WindowResources.CancellationToken;
        try
        {
            bool? enabled = await Task.Run(
                () => WindowsPowerModeBackend.ReadEnergySaverAsync(cancellationToken), cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _energySaverReadRunning = false;
                if (IsWindowClosed || cancellationToken.IsCancellationRequested) return;
                if (revision != _energySaverStatusRevision || _energySaverWriteRunning)
                {
                    RefreshEnergySaverStatus();
                    return;
                }

                // A failed read keeps the last known state, so one failed query cannot flash the button off
                if (enabled.HasValue) _energySaverAlwaysEnabled = enabled;
                _energySaverReadFailed = !enabled.HasValue;
                if (_energySaverAwaitingConfirmation) _energySaverConfirmationReadDone = true;
                TryReleaseEnergySaverRequest(snapshotArrived: false);
                UpdateLiveContent();
            }, DispatcherPriority.Background, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal when a warm flyout is evicted or the application closes.
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryFlyoutWindow.RefreshEnergySaverStatus: {ex.Message}");
            Dispatcher.UIThread.Post(() =>
            {
                _energySaverReadRunning = false;
                if (IsWindowClosed) return;
                _energySaverReadFailed = true;
                UpdateLiveContent();
            }, DispatcherPriority.Background);
        }
    }

#if DEBUG
    /// <summary>
    /// Rebuilds code-created flyout glyphs after a catalog source reload.
    /// </summary>
    private void OnGlyphCatalogResourcesReloaded()
    {
        if (IsWindowClosed) return;

        QueueRebuild();
    }
#endif

    private void OnSettingsChanged()
    {
        if (IsWindowClosed) return;

        CancellationToken cancellationToken = WindowResources.CancellationToken;
        Dispatcher.UIThread.Post(() =>
        {
            if (IsWindowClosed || cancellationToken.IsCancellationRequested) return;
            if (Docking.RedockIfUndockingDisabled()) return;

            QueueRebuild();
        });
    }

    /// <summary>
    /// Builds a complete content generation and publishes it, so a failed build keeps the previous UI alive.
    /// </summary>
    protected override void RebuildContent()
    {
        bool restorePowerModeFocus = FocusManager?.GetFocusedElement() is FlyoutSlider;
        SetFixedFlyoutWidth(Layout.WindowWidth);
        (UIContentGeneration replacement, FlyoutUndockButtonController? replacementUndockButtonController,
            LiveContent replacementLive) = BuildContentGeneration();
        FlyoutUndockButtonController? previousUndockButtonController = _undockButtonController;
        _undockButtonController = replacementUndockButtonController;
        try
        {
            CommitContentGeneration(replacement);
        }
        catch
        {
            _undockButtonController = previousUndockButtonController;
            throw;
        }

        _live = replacementLive;
        if (restorePowerModeFocus) replacementLive.PowerMode.Slider.Focus();
    }

    private (UIContentGeneration Generation, FlyoutUndockButtonController? UndockButtonController, LiveContent Live)
        BuildContentGeneration()
    {
        UIResourceScope resources = new($"{nameof(BatteryFlyoutWindow)}.Content");
        try
        {
            AppTheme theme = AppServices.Theme ?? AppTheme.Default;
            bool isLight = AppTheme.ResolveIsLight(_settings, theme);
            SettingsPalette p = AppSettingsPalette.Create(theme, _settings, isLight);
            FlyoutControlPalette fp = ToFlyoutPalette(p, theme, isLight);
            Color flyoutBackground = theme.ResolveFlyoutBackground(_settings, isLight);
            Color headerBackground = theme.ResolveFlyoutTitleBarBackground(_settings, isLight);

            StackPanel body = new()
            {
                Margin = Layout.BodyMargin,
                Spacing = Layout.SectionSpacing
            };
            ControlNames.Assign(body, parentName: "FlyoutBody");
            SummaryView summary = BuildBatterySummary(p);
            body.Children.Add(summary.Root);
            body.Children.Add(Separator(p));
            MetricsView metrics = BuildMetrics(p);
            body.Children.Add(metrics.Root);
            body.Children.Add(Separator(p));
            PowerModeView powerMode = BuildPowerControls(fp, p, resources);
            body.Children.Add(powerMode.Root);

            DockPanel root = new() { LastChildFill = true };
            (Border header, FlyoutUndockButtonController? undockButtonController, EnergySaverButton energySaver) =
                BuildHeader(fp, p, headerBackground, resources);
            DockPanel.SetDock(header, _settings.FlyoutHeaderAtBottom ? Dock.Bottom : Dock.Top);
            root.Children.Add(header);
            root.Children.Add(body);

            Grid content = ControlNames.Assign(new Grid(), parentName: "FlyoutContent");
            content.Children.Add(root);
            if (_settings is { FlyoutHeaderAtBottom: true, AllowFlyoutUndock: true })
            {
                undockButtonController = BuildUndockButton(fp, resources, UndockButtonFloatingMargin);
                Border floatingUndock = undockButtonController.Button.MapTo(ControlMap.Flyout.Undock);
                floatingUndock.HorizontalAlignment = HorizontalAlignment.Right;
                floatingUndock.VerticalAlignment = VerticalAlignment.Top;
                content.Children.Add(floatingUndock);
            }

            FlyoutFrame frame = new(
                content,
                flyoutBackground,
                p.Border,
                _settings.EnableRoundedCorners);
            ControlNames.Assign(frame, parentName: "FlyoutFrame");
            frame.MapVariant(ControlMap.Variants.HeaderOnTop, !_settings.FlyoutHeaderAtBottom);
            AttachWindowDrag(frame, resources);

            LiveContent live = new(theme, isLight, p, summary, metrics, powerMode, energySaver);
            resources.Add(() =>
            {
                if (ReferenceEquals(_live, live))
                    _live = null;
            });
            // Fill every value before the generation is published so it never shows placeholders
            UpdateLiveContent(live);

            ControlNames.AssignLogicalSubtree(frame, nameof(BatteryFlyoutWindow));

            UIContentGeneration generation = new(
                $"{nameof(BatteryFlyoutWindow)}.Content",
                frame,
                resources);
            return (generation, undockButtonController, live);
        }
        catch
        {
            resources.Dispose();
            throw;
        }
    }

    /// <summary>Queues one coalesced in-place refresh of the visible content's values.</summary>
    private void QueueLiveUpdate()
    {
        if (IsWindowClosed) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(QueueLiveUpdate, DispatcherPriority.Background);
            return;
        }

        if (_liveUpdateQueued) return;

        _liveUpdateQueued = true;
        CancellationToken cancellationToken = WindowResources.CancellationToken;
        Dispatcher.UIThread.Post(
            () =>
            {
                _liveUpdateQueued = false;
                if (IsWindowClosed || cancellationToken.IsCancellationRequested) return;
                UpdateLiveContent();
            },
            DispatcherPriority.Background);
    }

    private void UpdateLiveContent()
    {
        // A hidden flyout rebuilds with current values when it is next shown
        if (IsWindowClosed || _live is not { } live || (!IsVisible && !IsWarmPriming)) return;
        UpdateLiveContent(live);
    }

    private void UpdateLiveContent(LiveContent live)
    {
        BatterySnapshot snapshot = _batteryMonitor.Snapshot;
        BatteryTimeEstimates estimates = _batteryMonitor.Estimates;
        UpdateSummary(live, snapshot, estimates);
        UpdateMetrics(live.Metrics, snapshot, estimates);
        UpdateEnergySaver(live.EnergySaver);
        UpdatePowerMode(live.PowerMode);
    }

    private (Border Header, FlyoutUndockButtonController? UndockButtonController, EnergySaverButton EnergySaver)
        BuildHeader(
            FlyoutControlPalette p,
            SettingsPalette settingsPalette,
            Color headerBackground,
            UIResourceScope resources)
    {
        bool bottomHeader = _settings.FlyoutHeaderAtBottom;
        Grid grid = new();
        FlyoutUndockButtonController? undockButtonController = null;
        EnergySaverButton energySaver = BuildEnergySaverButton(p, settingsPalette);

        if (bottomHeader)
        {
            StackPanel actions = BuildHeaderActions(p, energySaver, settingsLast: true);
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(actions);
        }
        else
        {
            StackPanel left = BuildHeaderActions(p, energySaver, settingsLast: false);
            left.HorizontalAlignment = HorizontalAlignment.Left;
            left.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(left);

            if (_settings.AllowFlyoutUndock)
            {
                undockButtonController = BuildUndockButton(p, resources);
                Border undock = undockButtonController.Button.MapTo(ControlMap.Flyout.Header.Undock);
                undock.HorizontalAlignment = HorizontalAlignment.Right;
                undock.VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(undock);
            }
        }

        Border header = new()
        {
            Height = HeaderHeight,
            Background = Brush(headerBackground),
            CornerRadius = Rounded(bottomHeader ? HeaderBottomCornerRadius : HeaderTopCornerRadius),
            Padding = HeaderPadding,
            Child = grid
        };
        header.MapTo(ControlMap.Flyout.Header.ID);
        return (ControlNames.Assign(header, parentName: "FlyoutHeader"), undockButtonController, energySaver);
    }

    private StackPanel BuildHeaderActions(FlyoutControlPalette p, EnergySaverButton energySaver, bool settingsLast)
    {
        Border settingsButton = BuildHeaderIconButton(
            GlyphCatalog.SETTINGS,
            p,
            HeaderIconButtonFontSize,
            OpensWindow(_openSettings),
            L(nameof(CommonStrings.Flyout_Settings_Tooltip))).MapTo(ControlMap.Flyout.Header.OpenSettings);
        ControlNames.Assign(settingsButton, parentName: "SettingsButton");

        Border powerButton = BuildHeaderIconButton(
            GlyphCatalog.LIGHTNING_BOLT,
            p,
            HeaderPowerIconButtonFontSize,
            OpenModernPowerSettings,
            L(nameof(AppStrings.Flyout_PowerSettings_Tooltip))).MapTo(ControlMap.Flyout.Header.OpenPowerSettings);
        ControlNames.Assign(powerButton, parentName: "PowerSettingsButton");

        Border healthButton = BuildHeaderIconButton(
            TrayAppDotNETCommon.Visuals.SettingsNavigationGlyphs.About,
            p,
            HeaderIconButtonFontSize,
            ShowBatteryHealth,
            L(nameof(AppStrings.Flyout_BatteryHealth_Tooltip))).MapTo(ControlMap.Flyout.Header.ShowBatteryHealth);
        ControlNames.Assign(healthButton, parentName: "BatteryHealthButton");
        healthButton.Focusable = true;
        AutomationProperties.SetName(healthButton, L(nameof(AppStrings.Flyout_BatteryHealth_Tooltip)));
        healthButton.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            e.Handled = true;
            ShowBatteryHealth();
        };

        StackPanel actions = new()
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(energySaver.Button);

        if (settingsLast)
        {
            actions.Children.Add(healthButton);
            actions.Children.Add(powerButton);
            actions.Children.Add(settingsButton);
        }
        else
        {
            actions.Children.Add(settingsButton);
            actions.Children.Add(healthButton);
            actions.Children.Add(powerButton);
        }

        return actions;
    }

    private EnergySaverButton BuildEnergySaverButton(FlyoutControlPalette p, SettingsPalette settingsPalette)
    {
        FlyoutControlPalette onPalette = p with
        {
            Foreground = settingsPalette.ToggleOnThumb,
            ControlBackground = settingsPalette.ToggleOnTrack,
            Hover = settingsPalette.ToggleOnTrack,
            Pressed = settingsPalette.ToggleOnTrack,
            Border = settingsPalette.ToggleOnTrack
        };
        EnergySaverButton button = new(
            L(nameof(AppStrings.Flyout_EnergySaver)),
            p,
            onPalette,
            Rounded(HeaderIconButtonCornerRadius),
            ToggleEnergySaver);
        ControlNames.Assign(button.Button, parentName: "EnergySaverButton");
        button.Button.MapTo(ControlMap.Flyout.Header.EnergySaver);
        return button;
    }

    private void UpdateEnergySaver(EnergySaverButton button)
    {
        bool isOn = IsEnergySaverShownOn();
        bool isAvailable = IsEnergySaverAvailable;
        string label = L(nameof(AppStrings.Flyout_EnergySaver));
        string state = F(
            nameof(AppStrings.Flyout_LabeledValue_Format),
            label,
            L(isOn ? nameof(AppStrings.Flyout_State_On) : nameof(AppStrings.Flyout_State_Off)));
        button.Apply(
            isOn,
            isAvailable,
            state,
            isAvailable
                ? $"{state}. {L(nameof(AppStrings.Flyout_EnergySaver_Tooltip))}"
                : L(nameof(AppStrings.Flyout_EnergySaver_Unavailable_Tooltip)));
    }

    /// <summary>The threshold is unknown only after a failed read, never while the first read is running.</summary>
    private bool IsEnergySaverAvailable =>
        _energySaverRequest.HasValue || _energySaverAlwaysEnabled.HasValue || !_energySaverReadFailed;

    /// <summary>What Windows reports: Energy Saver running now, or set to Always.</summary>
    private bool ObservedEnergySaverOn() =>
        _batteryMonitor.Snapshot.EnergySaverEnabled || _energySaverAlwaysEnabled == true;

    private bool IsEnergySaverShownOn() => _energySaverRequest ?? ObservedEnergySaverOn();

    private void ToggleEnergySaver()
    {
        if (IsWindowClosed || !IsEnergySaverAvailable) return;

        // Show the choice at once. The backend orders the write after any mode switch still in progress.
        _energySaverRequest = !IsEnergySaverShownOn();
        _energySaverAwaitingConfirmation = false;
        _energySaverConfirmationReadDone = false;
        UpdateLiveContent();
        if (!_energySaverWriteRunning)
            WriteEnergySaverAsync().ForgetLogged("BatteryFlyoutWindow.WriteEnergySaverAsync");
    }

    private async Task WriteEnergySaverAsync()
    {
        CancellationToken cancellationToken = WindowResources.CancellationToken;
        _energySaverWriteRunning = true;
        try
        {
            // Rapid clicks end on the newest choice
            while (!IsWindowClosed && _energySaverRequest is { } requested)
            {
                bool success = await Task.Run(
                    () => WindowsPowerModeBackend.ApplyEnergySaverAsync(requested, cancellationToken),
                    cancellationToken);
                if (!success)
                {
                    TADNLog.Log($"BatteryFlyoutWindow.WriteEnergySaverAsync({requested}): power plan write failed");
                    // Show what Windows reports instead of holding a choice that did not apply
                    if (_energySaverRequest == requested) _energySaverRequest = null;
                    break;
                }

                if (_energySaverRequest == requested) break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryFlyoutWindow.WriteEnergySaverAsync: {ex.Message}");
            _energySaverRequest = null;
        }
        finally
        {
            _energySaverWriteRunning = false;
        }

        if (IsWindowClosed) return;
        _energySaverAwaitingConfirmation = _energySaverRequest.HasValue;
        _energySaverConfirmationReadDone = false;
        _batteryMonitor.ForceRefresh();
        RefreshEnergySaverStatus(invalidate: true);
        UpdateLiveContent();
    }

    /// <summary>
    /// Hands the button back to Windows' state once Windows reports the choice. A battery reading that arrives after
    /// the post-write threshold read settles the choice either way, so a refused change cannot stay on screen.
    /// </summary>
    private void TryReleaseEnergySaverRequest(bool snapshotArrived)
    {
        if (_energySaverRequest is not { } requested
            || !_energySaverAwaitingConfirmation
            || !_energySaverConfirmationReadDone) return;
        if (ObservedEnergySaverOn() != requested && !snapshotArrived) return;

        _energySaverRequest = null;
        _energySaverAwaitingConfirmation = false;
        _energySaverConfirmationReadDone = false;
    }

    private void ShowBatteryHealth()
    {
        if (IsWindowClosed || !IsVisible) return;
        if (_healthWindow != null)
        {
            _healthWindow.Activate();
            return;
        }

        ShowBatteryHealthAsync().ForgetLogged("BatteryFlyoutWindow.ShowBatteryHealth");
    }

    private async Task ShowBatteryHealthAsync()
    {
        using BatteryHealthWindow dialog = new(_batteryMonitor, _settings);
        _healthWindow = dialog;
        try
        {
            await ShowOwnedDialogAsync(dialog);
        }
        finally
        {
            _healthWindow = null;
        }
    }

    private Border BuildHeaderIconButton(
        Glyph glyph,
        FlyoutControlPalette p,
        double fontSize,
        Action click,
        string tooltip) =>
        TrayAppDotNETFlyoutUI.IconButton(
            glyph,
            p,
            _ => click(),
            HeaderIconButtonWidth,
            HeaderIconButtonHeight,
            fontSize,
            tooltip: tooltip,
            cornerRadius: Rounded(HeaderIconButtonCornerRadius),
            lineHeight: TrayAppDotNETFlyoutUI.IconLineHeight(fontSize));

    private FlyoutUndockButtonController BuildUndockButton(
        FlyoutControlPalette p,
        UIResourceScope resources,
        Thickness? margin = null)
    {
        FlyoutUndockButtonController controller = new(new FlyoutUndockButtonOptions
        {
            Owner = this,
            Docking = Docking,
            Palette = p,
            DraggingChanged = SetUndockButtonDragging,
            InteractionCompleted = _ => FlushPendingRebuild(),
            Width = UndockButtonWidth,
            Height = UndockButtonHeight,
            FontSize = UndockButtonFontSize,
            FontWeight = FontWeight.Normal,
            DragThreshold = DragThreshold,
            IsVisible = _settings.AllowFlyoutUndock,
            Margin = margin ?? new Thickness(0),
            CornerRadius = Rounded(UndockButtonCornerRadius)
        })
        {
            Glyph =
            {
                FontFamily = TrayAppDotNETSettingsUI.IconFont,
                FontWeight = FontWeight.Normal,
                Foreground = Brush(p.IconForeground),
                LineHeight = UndockButtonGlyphLineHeight
            }
        };
        ControlNames.Assign(controller.Button, parentName: "UndockButton");
        UseDefaultGlyphRendering(controller.Glyph);
        resources.Add(() =>
        {
            if (ReferenceEquals(_undockButtonController, controller))
                _undockButtonController = null;
        });
        return resources.Own(controller);
    }

    private static void UseDefaultGlyphRendering(TextBlock glyph)
    {
        TextOptions.SetTextRenderingMode(glyph, TextRenderingMode.Unspecified);
        TextOptions.SetTextHintingMode(glyph, TextHintingMode.Unspecified);
        TextOptions.SetBaselinePixelAlignment(glyph, BaselinePixelAlignment.Unspecified);
    }

    private CornerRadius Rounded(CornerRadius radius) =>
        _settings.EnableRoundedCorners ? radius : new CornerRadius(0);

    protected override void OnDockStateChanged(FlyoutDockStateChange change)
    {
        _undockButtonController?.UpdateVisual();

        // The header layout differs while undocked
        if (change is FlyoutDockStateChange.Undocked or FlyoutDockStateChange.Redocked) RebuildNow();
    }

    private SummaryView BuildBatterySummary(SettingsPalette p)
    {
        Grid summary = new()
        {
            ColumnSpacing = Layout.SummarySpacing,
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*")
        };
        TextBlock battery = Text(string.Empty, p, Layout.BatteryGlyphFontSize);
        battery.VerticalAlignment = VerticalAlignment.Center;
        battery.IsHitTestVisible = false;
        summary.Children.Add(ControlNames.Assign(battery, parentName: "BatteryGlyph"));

        TextBlock charge = Text(string.Empty, p, Layout.ChargeFontSize, FontWeight.SemiBold);
        charge.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(charge, 1);
        summary.Children.Add(charge);

        StackPanel status = new() { Spacing = Layout.SummaryTextSpacing, VerticalAlignment = VerticalAlignment.Center };
        TextBlock time = Text(string.Empty, p, Layout.TimeFontSize);
        status.Children.Add(time);
        TextBlock statusText = Text(string.Empty, p, Layout.StatusFontSize);
        statusText.Foreground = Brush(p.SecondaryForeground);
        statusText.TextWrapping = TextWrapping.Wrap;
        status.Children.Add(statusText);
        Grid.SetColumn(status, 2);
        summary.Children.Add(status);
        return new SummaryView(
            ControlNames.Assign(summary, parentName: "BatterySummary"), battery, charge, time, statusText);
    }

    private static void UpdateSummary(LiveContent live, BatterySnapshot snapshot, BatteryTimeEstimates estimates)
    {
        SummaryView view = live.Summary;
        Glyph batteryGlyph = snapshot.BatteryPresent ? BatteryGlyphResolver.Resolve(snapshot) : GlyphCatalog.BATTERY_0;
        if (view.Glyph.Text != batteryGlyph.Text) GlyphApplicator.ApplyTo(view.Glyph, batteryGlyph);
        SetForeground(view.Glyph, snapshot.BatteryPresent
            ? live.Theme.ResolveBatteryFill(snapshot, live.IsLight)
            : live.Palette.SecondaryForeground);
        view.Charge.Text = FormatChargePercent(snapshot);

        string? time = SummaryTimeText(snapshot, estimates);
        view.Time.Text = time;
        view.Time.IsVisible = time != null;
        view.Status.Text = BuildStatus(snapshot);
    }

    private static string? SummaryTimeText(BatterySnapshot snapshot, BatteryTimeEstimates estimates)
    {
        if (snapshot is not { BatteryPresent: true, IsFullyCharged: false }) return null;
        if (snapshot.IsCharging)
            return estimates.ChargeTime.HasValue
                ? F(nameof(AppStrings.Flyout_UntilFull_Format), FormatEstimate(estimates.ChargeTime))
                : null;

        // Predicted life is known on external power too, but only a draining battery has time remaining
        if (snapshot.IsOnExternalPower) return null;
        TimeSpan? remaining = estimates.PredictedLife ?? estimates.PresentDischarge;
        return remaining.HasValue ? F(nameof(AppStrings.Flyout_Remaining_Format), FormatEstimate(remaining)) : null;
    }

    private MetricsView BuildMetrics(SettingsPalette p)
    {
        Grid metrics = new()
        {
            // The usage column takes the width its content needs so its values are never clipped;
            // the estimates column takes the rest
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = Layout.MetricColumnSpacing
        };
        Grid times = CreateMetricColumn();
        TextBlock predictedLife = AddDetailRow(times, L(nameof(AppStrings.Flyout_PredictedLife_Label)), p,
            L(nameof(AppStrings.Flyout_PredictedLife_Tooltip))).Value;
        TextBlock presentDischarge = AddDetailRow(times, L(nameof(AppStrings.Flyout_PresentDischarge_Label)), p,
            L(nameof(AppStrings.Flyout_PresentDischarge_Tooltip))).Value;
        TextBlock chargeTime = AddDetailRow(times, L(nameof(AppStrings.Flyout_TimeUntilFull_Label)), p,
            L(nameof(AppStrings.Flyout_TimeUntilFull_Tooltip))).Value;
        metrics.Children.Add(ControlNames.Assign(times, parentName: "BatteryTimeEstimates"));

        Grid usage = CreateMetricColumn();
        TextBlock powerSource = AddDetailRow(usage, L(nameof(AppStrings.Flyout_PowerSource_Label)), p).Value;
        (TextBlock rateLabel, TextBlock rate) = AddDetailRow(usage, L(nameof(AppStrings.Flyout_DischargeRate_Label)), p);
        TextBlock remaining = AddDetailRow(usage, L(nameof(AppStrings.Flyout_Remaining_Label)), p).Value;
        Grid.SetColumn(usage, 1);
        metrics.Children.Add(ControlNames.Assign(usage, parentName: "BatteryUsage"));
        return new MetricsView(
            ControlNames.Assign(metrics, parentName: "BatteryDetails"),
            predictedLife,
            presentDischarge,
            chargeTime,
            powerSource,
            rateLabel,
            rate,
            remaining);
    }

    private static void UpdateMetrics(MetricsView view, BatterySnapshot snapshot, BatteryTimeEstimates estimates)
    {
        view.PredictedLife.Text = estimates.PredictedLife == null
                                  && snapshot is { BatteryPresent: true, IsOnExternalPower: false,
                                      RemainingCapacityMilliwattHours: > 0, DischargeRateWatts: > 0 }
            ? L(nameof(AppStrings.Flyout_Learning))
            : FormatEstimate(estimates.PredictedLife);
        view.PresentDischarge.Text = FormatEstimate(estimates.PresentDischarge);
        view.ChargeTime.Text = FormatEstimate(estimates.ChargeTime);
        view.PowerSource.Text = L(snapshot.IsOnExternalPower
            ? nameof(AppStrings.Flyout_PowerSource_External)
            : nameof(AppStrings.Flyout_PowerSource_Battery));
        // A charging battery has no discharge rate, so the row reports the rate it is charging at instead
        view.RateLabel.Text = L(snapshot.IsCharging
            ? nameof(AppStrings.Flyout_ChargeRate_Label)
            : nameof(AppStrings.Flyout_DischargeRate_Label));
        view.Rate.Text = FormatPower(snapshot.CurrentBatteryPowerWatts);
        view.Remaining.Text = FormatCapacity(snapshot.RemainingCapacityMilliwattHours);
    }

    private static string FormatEstimate(TimeSpan? value) =>
        value.HasValue
            ? value.Value <= TimeSpan.Zero
                ? F(nameof(AppStrings.Tray_Tooltip_Duration_Minutes_Format), 0)
                : FormatTimeSpan(value.Value)
            : L(nameof(AppStrings.Flyout_NotAvailable));

    /// <summary>Labels give way with an ellipsis before any value is clipped.</summary>
    private static Grid CreateMetricColumn() => new()
    {
        RowSpacing = Layout.MetricRowSpacing,
        ColumnSpacing = Layout.MetricValueSpacing,
        ColumnDefinitions = new ColumnDefinitions("*,Auto")
    };

    private PowerModeView BuildPowerControls(
        FlyoutControlPalette p,
        SettingsPalette settingsPalette,
        UIResourceScope resources)
    {
        Grid panel = new()
        {
            RowSpacing = Layout.ControlRowSpacing,
            ColumnDefinitions = new ColumnDefinitions("*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto")
        };
        string powerModeTitle = L(nameof(AppStrings.Flyout_PowerMode_Label));
        TextBlock heading = Text(string.Empty, settingsPalette, Layout.ControlLabelFontSize, FontWeight.SemiBold);
        panel.Children.Add(heading);

        FlyoutSlider slider = resources.Own(new FlyoutSlider
        {
            Minimum = 0,
            Maximum = 2,
            WheelStep = 1,
            KeyboardStep = 1,
            LargeKeyboardStep = 1,
            TrackColor = p.SliderTrack,
            ProgressColor = p.SliderProgress,
            ThumbColor = p.SliderThumb,
            TickValues = [0, 1, 2],
            TickColor = p.SecondaryForeground,
            Thumb = new SliderThumbGlyphOption(),
            HitTestVerticalPadding = Layout.SliderHitTestVerticalPadding
        });
        ControlNames.Assign(slider, parentName: "PowerModeSlider");
        slider.MapTo(ControlMap.Flyout.PowerMode);
        AutomationProperties.SetName(slider, powerModeTitle);
        TrayAppDotNETToolTip.SuppressWhileEngaged(slider);
        FlyoutPowerMode? requestedMode = null;
        slider.UserAdjustmentStarted += (_, _) =>
        {
            // An unchanged thumb click is also a choice, even if a read/apply completes during the gesture.
            requestedMode = PowerModeAt(slider.Value);
        };
        slider.ValueChanged += (_, value) =>
        {
            slider.Value = Math.Round(value, MidpointRounding.AwayFromZero);
            requestedMode = PowerModeAt(slider.Value);
            slider.ThumbOpacity = 1;
            slider.ProgressValueOverride = null;
            string modeLabel = PowerModeLabel(requestedMode);
            heading.Text = F(nameof(AppStrings.Flyout_LabeledValue_Format), powerModeTitle, modeLabel);
            TrayAppDotNETToolTip.SetTip(slider, modeLabel);
        };
        slider.UserAdjustmentCompleted += (_, _) =>
        {
            FlyoutPowerMode? mode = requestedMode;
            requestedMode = null;
            // The service compares against the newest intent, which may differ from this gesture's initial mode.
            if (mode.HasValue) SetPowerMode(mode.Value);
            // The gesture no longer owns the slider, so show the service's mode again
            UpdateLiveContent();
            FlushPendingRebuild();
        };

        Grid.SetRow(slider, 1);
        panel.Children.Add(slider);

        // Align the endpoint glyph centers with the slider thumb's minimum/maximum positions.
        double glyphInset = (slider.Thumb.Width - Layout.SliderGlyphSize) / 2;
        Grid endpointGlyphs = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Margin = new Thickness(glyphInset, top: 0, glyphInset, bottom: 0)
        };
        Control efficiency = PowerModeGlyph(GlyphCatalog.POWER_MODE_EFFICIENCY, p,
            PowerModeLabel(FlyoutPowerMode.PowerSaver));
        efficiency.HorizontalAlignment = HorizontalAlignment.Left;
        endpointGlyphs.Children.Add(efficiency);
        Control performance = PowerModeGlyph(GlyphCatalog.POWER_MODE_PERFORMANCE, p,
            PowerModeLabel(FlyoutPowerMode.Ultimate));
        performance.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(performance, 1);
        endpointGlyphs.Children.Add(performance);
        Grid.SetRow(endpointGlyphs, 2);
        panel.Children.Add(endpointGlyphs);
        return new PowerModeView(panel, heading, slider);
    }

    private void UpdatePowerMode(PowerModeView view)
    {
        // A gesture owns the slider and heading until it completes
        if (view.Slider.IsUserAdjusting) return;

        FlyoutPowerMode? activeMode = _powerModes.DisplayMode;
        string label = activeMode.HasValue || _powerModes.IsStateAvailable
            ? PowerModeLabel(activeMode)
            : L(nameof(AppStrings.Flyout_PowerMode_Unknown));
        view.Slider.Value = PowerModeIndex(activeMode);
        view.Slider.ThumbOpacity = activeMode.HasValue ? 1 : 0;
        view.Slider.ProgressValueOverride = activeMode.HasValue ? null : 0;

        string heading = F(nameof(AppStrings.Flyout_LabeledValue_Format), L(nameof(AppStrings.Flyout_PowerMode_Label)), label);
        if (view.Heading.Text == heading) return;
        view.Heading.Text = heading;
        TrayAppDotNETToolTip.SetTip(view.Slider, label);
    }

    private static TextBlock PowerModeGlyph(Glyph glyph, FlyoutControlPalette p, string tooltip)
    {
        TextBlock icon = TrayAppDotNETFlyoutUI.Text(glyph.Text, p, Layout.SliderGlyphSize,
            FontWeight.Normal, p.SecondaryForeground);
        GlyphApplicator.ApplyTo(icon, glyph);
        icon.Width = Layout.SliderGlyphSize;
        icon.TextAlignment = TextAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        TrayAppDotNETToolTip.SetTip(icon, tooltip);
        return icon;
    }

    private static int PowerModeIndex(FlyoutPowerMode? mode) => mode switch
    {
        FlyoutPowerMode.PowerSaver => 0,
        FlyoutPowerMode.Ultimate => 2,
        _ => 1
    };

    private static FlyoutPowerMode PowerModeAt(double value) => (int)value switch
    {
        0 => FlyoutPowerMode.PowerSaver,
        2 => FlyoutPowerMode.Ultimate,
        _ => FlyoutPowerMode.Balanced
    };

    private static string PowerModeLabel(FlyoutPowerMode? mode) => mode switch
    {
        FlyoutPowerMode.PowerSaver => L(nameof(AppStrings.Flyout_PowerMode_PowerSaver)),
        FlyoutPowerMode.Balanced => L(nameof(AppStrings.Flyout_PowerMode_Balanced)),
        FlyoutPowerMode.Ultimate => L(nameof(AppStrings.Flyout_PowerMode_Ultimate)),
        _ => L(nameof(AppStrings.Flyout_PowerMode_Custom))
    };

    private static string FormatChargePercent(BatterySnapshot snapshot) =>
        snapshot.BatteryPresent ? $"{snapshot.ChargePercentage}%" : "--";

    private static (TextBlock Label, TextBlock Value) AddDetailRow(
        Grid grid,
        string label,
        SettingsPalette p,
        string? tooltip = null)
    {
        int row = grid.RowDefinitions.Count;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        TextBlock labelText = Text(label, p, Layout.MetricFontSize);
        labelText.Foreground = Brush(p.SecondaryForeground);
        labelText.TextTrimming = TextTrimming.CharacterEllipsis;
        if (tooltip != null) TrayAppDotNETToolTip.SetTip(labelText, tooltip);
        Grid.SetRow(labelText, row);
        Grid.SetColumn(labelText, value: 0);
        grid.Children.Add(labelText);

        TextBlock valueText = Text(string.Empty, p, Layout.MetricFontSize, FontWeight.SemiBold);
        if (tooltip != null) TrayAppDotNETToolTip.SetTip(valueText, tooltip);
        valueText.TextAlignment = TextAlignment.Right;
        Grid.SetRow(valueText, row);
        Grid.SetColumn(valueText, value: 1);
        grid.Children.Add(valueText);
        return (labelText, valueText);
    }

    private static Border Separator(SettingsPalette p) =>
        new() { Height = Layout.SeparatorHeight, Background = Brush(p.Border), Opacity = Layout.SeparatorOpacity };

    private static TextBlock Text(string text, SettingsPalette p, double size, FontWeight weight = FontWeight.Normal) =>
        new()
        {
            Text = text,
            FontFamily = TrayAppDotNETSettingsUI.UIFont,
            FontSize = size,
            FontWeight = weight,
            Foreground = Brush(p.Foreground)
        };

    private static SolidColorBrush Brush(Color color) => new(color);

    private static void SetForeground(TextBlock text, Color color)
    {
        if (text.Foreground is ISolidColorBrush { Color: var current } && current == color) return;
        text.Foreground = Brush(color);
    }

    private static FlyoutControlPalette ToFlyoutPalette(SettingsPalette p, AppTheme theme, bool isLight) =>
        new(
            p.Foreground,
            p.SecondaryForeground,
            p.Border,
            p.Hover,
            p.Pressed,
            p.ControlBackground,
            p.CardBackground,
            theme.IconForeground.For(isLight),
            p.SliderTrack,
            p.SliderProgress,
            p.SliderThumb);

    private void SetPowerMode(FlyoutPowerMode mode)
    {
        if (IsWindowClosed) return;
        _powerModes.RequestAsync(mode).ForgetLogged("BatteryFlyoutWindow.SetPowerMode");
    }

    internal static void OpenModernPowerSettings()
    {
        if (!ExplorerProcessLauncher.TryShellExecute(
                "ms-settings:powersleep",
                arguments: null,
                workingDirectory: null,
                verb: null,
                out _,
                out string errorMessage))
            TADNLog.Log($"BatteryFlyoutWindow.OpenModernPowerSettings: {errorMessage}");
    }

    private static string BuildStatus(BatterySnapshot snapshot)
    {
        if (!snapshot.BatteryPresent) return L(nameof(AppStrings.Tray_Tooltip_NoBattery));
        if (snapshot.IsFullyCharged) return L(nameof(AppStrings.Tray_Tooltip_PluggedInFull));
        if (snapshot.IsCharging)
        {
            return snapshot.ChargeRateWatts.HasValue
                ? F(nameof(AppStrings.Flyout_ChargingAt_Format), snapshot.ChargeRateWatts.Value)
                : L(nameof(AppStrings.Tray_Tooltip_Charging));
        }

        if (snapshot.IsOnExternalPower) return L(nameof(AppStrings.Tray_Tooltip_PluggedIn));
        return L(nameof(AppStrings.Tray_Tooltip_OnBattery));
    }

    private static string FormatPower(float? watts) =>
        watts.HasValue ? $"{watts.Value:F1} W" : L(nameof(AppStrings.Flyout_NotAvailable));

    private static string FormatCapacity(float? milliwattHours) =>
        milliwattHours.HasValue ? $"{milliwattHours.Value / 1000f:F1} Wh" : L(nameof(AppStrings.Flyout_NotAvailable));

    private static string FormatTimeSpan(TimeSpan value)
    {
        if (value.TotalDays >= 1)
            return F(nameof(AppStrings.Tray_Tooltip_Duration_DaysHours_Format), (int)value.TotalDays, value.Hours);
        if (value.TotalHours >= 1)
            return F(nameof(AppStrings.Tray_Tooltip_Duration_HoursMinutes_Format), (int)value.TotalHours, value.Minutes);
        return F(nameof(AppStrings.Tray_Tooltip_Duration_Minutes_Format), Math.Max(val1: 1, value.Minutes));
    }

    private static string L(string key) => LocalizationManager.Instance[key];

    private static string F(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, L(key), arguments);

    protected override void OnFlyoutClosed()
    {
        _liveUpdateQueued = false;
        _undockButtonController = null;
        _live = null;
    }

    private sealed record SummaryView(Grid Root, TextBlock Glyph, TextBlock Charge, TextBlock Time, TextBlock Status);

    private sealed record MetricsView(
        Grid Root,
        TextBlock PredictedLife,
        TextBlock PresentDischarge,
        TextBlock ChargeTime,
        TextBlock PowerSource,
        TextBlock RateLabel,
        TextBlock Rate,
        TextBlock Remaining);

    private sealed record PowerModeView(Grid Root, TextBlock Heading, FlyoutSlider Slider);

    /// <summary>The controls of one content generation whose values follow battery and power state in place.</summary>
    private sealed record LiveContent(
        AppTheme Theme,
        bool IsLight,
        SettingsPalette Palette,
        SummaryView Summary,
        MetricsView Metrics,
        PowerModeView PowerMode,
        EnergySaverButton EnergySaver);

    /// <summary>The Energy Saver toggle, restyled in place so a state change never replaces or dims it.</summary>
    private sealed class EnergySaverButton
    {
        private readonly FlyoutControlPalette _offPalette;
        private readonly FlyoutControlPalette _onPalette;
        private readonly TextBlock _label;
        private readonly FlyoutButtonState _state;
        private FlyoutControlPalette _palette;
        private bool? _isOn;
        private string? _tooltip;

        public EnergySaverButton(
            string text,
            FlyoutControlPalette offPalette,
            FlyoutControlPalette onPalette,
            CornerRadius cornerRadius,
            Action toggle)
        {
            _offPalette = offPalette;
            _onPalette = onPalette;
            _palette = offPalette;
            _label = TrayAppDotNETFlyoutUI.Text(text, offPalette, Layout.ControlLabelFontSize, FontWeight.SemiBold);
            _label.HorizontalAlignment = HorizontalAlignment.Center;
            _label.VerticalAlignment = VerticalAlignment.Center;
            Button = new Border
            {
                Height = HeaderIconButtonHeight,
                Margin = Layout.EnergySaverButtonMargin,
                Padding = Layout.EnergySaverButtonPadding,
                BorderThickness = Layout.EnergySaverButtonBorderThickness,
                BorderBrush = Brush(offPalette.Border),
                CornerRadius = cornerRadius,
                Child = _label,
                Focusable = true
            };
            TrayAppDotNETToolTip.SuppressWhileEngaged(Button);
            _state = FlyoutButtonState.Attach(
                Button,
                () => Brush(_palette.ControlBackground),
                () => Brush(_palette.Hover),
                () => Brush(_palette.Pressed),
                _ => toggle());
            Button.KeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Enter or Key.Space)) return;
                e.Handled = true;
                toggle();
            };
        }

        public Border Button { get; }

        public void Apply(bool isOn, bool isAvailable, string automationName, string tooltip)
        {
            if (_isOn != isOn)
            {
                _isOn = isOn;
                _palette = isOn ? _onPalette : _offPalette;
                _label.Foreground = Brush(_palette.Foreground);
                Button.BorderBrush = Brush(_palette.Border);
                _state.Refresh();
                AutomationProperties.SetName(Button, automationName);
            }

            _state.IsEnabled = isAvailable;
            Button.Opacity = isAvailable ? 1 : Layout.DisabledOpacity;
            if (_tooltip == tooltip) return;
            _tooltip = tooltip;
            TrayAppDotNETToolTip.SetTip(Button, tooltip);
        }
    }
}
