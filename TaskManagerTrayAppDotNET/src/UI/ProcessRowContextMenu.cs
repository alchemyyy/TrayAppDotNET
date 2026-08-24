using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TaskManagerTrayAppDotNET.Services;

namespace TaskManagerTrayAppDotNET.UI;

internal delegate bool TryTerminateProcessAction(
    ProcessTerminationTarget target,
    out string errorMessage);

/// <summary>Owns transient row menus and dispatches process actions away from the UI thread.</summary>
internal sealed class ProcessRowContextMenuController : IDisposable
{
    private const int ContextMenuFontSize = 15;
    private const string SubmenuGlyph = "\uE76C";
    private const string SelectedGlyph = "\uE73E";

    private readonly SettingsPalette _palette;
    private readonly bool _enableRoundedCorners;
    private readonly TryTerminateProcessAction _terminateProcess;
    private readonly Action _requestRefresh;
    private readonly Action<string, string> _reportError;
    private readonly Action<string, string>? _reportInformation;
    private readonly HashSet<Window> _actionWindows = [];
    private ProcessRowContextMenuWindow? _menuWindow;
    private Window? _owner;
    private PixelPoint _menuPosition;
    private bool _disposed;

    public ProcessRowContextMenuController(
        SettingsPalette palette,
        bool enableRoundedCorners,
        TryTerminateProcessAction terminateProcess,
        Action requestRefresh,
        Action<string, string> reportError,
        Action<string, string>? reportInformation = null)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(terminateProcess);
        ArgumentNullException.ThrowIfNull(requestRefresh);
        ArgumentNullException.ThrowIfNull(reportError);

        _palette = palette;
        _enableRoundedCorners = enableRoundedCorners;
        _terminateProcess = terminateProcess;
        _requestRefresh = requestRefresh;
        _reportError = reportError;
        _reportInformation = reportInformation;
    }

    /// <summary>Shows a common TADN menu for one immutable process identity at a screen position.</summary>
    public void Show(
        Window owner,
        PixelPoint screenPosition,
        ProcessTerminationTarget target,
        string copyText)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(owner);

        CloseMenu();
        _owner = owner;
        _menuPosition = screenPosition;
        List<TrayMenuEntry> entries = BuildMainEntries(target, copyText);
        ShowMenu(entries);
    }

    private List<TrayMenuEntry> BuildMainEntries(ProcessTerminationTarget target, string copyText)
    {
        TrayMenuEntryBuilder entries = new();
        entries.Add("Copy", () => ExecuteCopy(copyText));
        entries.AddSeparator();
        entries.Add("End task", () => ExecuteEndTask(target));
        entries.Add("End process tree", () => ExecuteEndProcessTree(target));
        entries.AddSeparator();
        entries.Add("Set priority", () => ShowPriorityMenu(target), SubmenuGlyph);
        entries.Add("Set affinity", () => ShowAffinityWindow(target));
        entries.AddSeparator();
        entries.Add("Create memory dump file", () => ExecuteCreateMemoryDump(target));
        entries.Add("Open file location", () => ExecuteBackground(
            "Open file location failed",
            target,
            ProcessNativeActions.TryOpenFileLocation));
        entries.Add("Properties", () => ExecuteBackground(
            "Properties failed",
            target,
            ProcessNativeActions.TryOpenProperties));

        // Window discovery occurs only when the user opens a row menu.
        if (ProcessNativeActions.HasTopLevelWindow(target.ProcessID))
        {
            entries.AddSeparator();
            entries.Add("Switch to", () => ExecuteWindowAction(
                "Switch to failed",
                target,
                ProcessNativeActions.TrySwitchToWindow));
            entries.Add("Bring to front", () => ExecuteWindowAction(
                "Bring to front failed",
                target,
                ProcessNativeActions.TryBringWindowToFront));
            entries.Add("Minimize", () => ExecuteWindowAction(
                "Minimize failed",
                target,
                ProcessNativeActions.TryMinimizeWindow));
            entries.Add("Maximize", () => ExecuteWindowAction(
                "Maximize failed",
                target,
                ProcessNativeActions.TryMaximizeWindow));
        }

        return entries.ToList();
    }

    private void ExecuteCopy(string copyText) => _ = ExecuteCopyAsync(copyText);

    private async Task ExecuteCopyAsync(string copyText)
    {
        Window? owner = _owner;
        if (owner == null || _disposed) return;

        try
        {
            IClipboard? clipboard = owner.Clipboard;
            if (clipboard == null)
                throw new InvalidOperationException("The system clipboard is unavailable.");

            await clipboard.SetTextAsync(copyText);
            await clipboard.FlushAsync();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Copy failed: {exception}");
            if (!_disposed) _reportError("Copy failed", exception.Message);
        }
    }

    private void ShowPriorityMenu(ProcessTerminationTarget target)
    {
        if (!ProcessNativeActions.TryGetPriority(
                target,
                out ProcessPriorityLevel currentPriority,
                out string errorMessage))
        {
            _reportError("Set priority failed", errorMessage);
            return;
        }

        TrayMenuEntryBuilder entries = new();
        AddPriorityEntry(entries, "Realtime", ProcessPriorityLevel.Realtime, currentPriority, target);
        AddPriorityEntry(entries, "High", ProcessPriorityLevel.High, currentPriority, target);
        AddPriorityEntry(entries, "Above normal", ProcessPriorityLevel.AboveNormal, currentPriority, target);
        AddPriorityEntry(entries, "Normal", ProcessPriorityLevel.Normal, currentPriority, target);
        AddPriorityEntry(entries, "Below normal", ProcessPriorityLevel.BelowNormal, currentPriority, target);
        AddPriorityEntry(entries, "Low", ProcessPriorityLevel.Idle, currentPriority, target);
        ShowMenu(entries.ToList());
    }

    private void AddPriorityEntry(
        TrayMenuEntryBuilder entries,
        string label,
        ProcessPriorityLevel priority,
        ProcessPriorityLevel currentPriority,
        ProcessTerminationTarget target)
    {
        entries.Add(new TrayMenuEntry(
            label,
            () => ExecuteSetPriority(target, priority))
        {
            TrailingGlyph = priority == currentPriority ? SelectedGlyph : null
        });
    }

    private void ExecuteEndTask(ProcessTerminationTarget target)
    {
        // ProcessTerminationService owns the currently armed handle and is UI-thread coordinated
        if (!_terminateProcess(target, out string errorMessage))
        {
            _reportError("End task failed", errorMessage);
            return;
        }

        _requestRefresh();
    }

    private void ExecuteEndProcessTree(ProcessTerminationTarget target) =>
        _ = ExecuteEndProcessTreeAsync(target);

    private async Task ExecuteEndProcessTreeAsync(ProcessTerminationTarget target)
    {
        ProcessActionResult descendantsResult;
        try
        {
            descendantsResult = await Task.Run(() =>
            {
                bool succeeded = ProcessNativeActions.TryTerminateDescendants(target, out string errorMessage);
                return new ProcessActionResult(succeeded, errorMessage);
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"End process tree failed: {exception}");
            descendantsResult = new ProcessActionResult(false, exception.Message);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed) return;

            // Keep the mutable pre-armed termination service serialized with row selection
            bool rootSucceeded = _terminateProcess(target, out string rootError);
            if (rootSucceeded) _requestRefresh();
            if (descendantsResult.Succeeded && rootSucceeded) return;

            string errorMessage = string.Join(
                "\n",
                new[] { descendantsResult.Message, rootError }
                    .Where(message => !string.IsNullOrWhiteSpace(message)));
            _reportError("End process tree failed", errorMessage);
        });
    }

    private void ExecuteSetPriority(ProcessTerminationTarget target, ProcessPriorityLevel priority) =>
        ExecuteBackground(
            "Set priority failed",
            target,
            (ProcessTerminationTarget actionTarget, out string errorMessage) =>
                ProcessNativeActions.TrySetPriority(actionTarget, priority, out errorMessage));

    private void ExecuteCreateMemoryDump(ProcessTerminationTarget target) => ExecuteBackground(
        "Create memory dump failed",
        () =>
        {
            bool succeeded = ProcessNativeActions.TryCreateMemoryDump(
                target,
                out string dumpPath,
                out string errorMessage);
            return new ProcessActionResult(succeeded, errorMessage, dumpPath);
        },
        refreshOnSuccess: false,
        successTitle: "Memory dump created");

    private void ExecuteWindowAction(
        string failureTitle,
        ProcessTerminationTarget target,
        TryProcessAction action)
    {
        if (action(target, out string errorMessage)) return;
        _reportError(failureTitle, errorMessage);
    }

    private void ExecuteBackground(
        string failureTitle,
        ProcessTerminationTarget target,
        TryProcessAction action) => ExecuteBackground(
            failureTitle,
            () =>
            {
                bool succeeded = action(target, out string errorMessage);
                return new ProcessActionResult(succeeded, errorMessage);
            });

    private void ExecuteBackground(
        string failureTitle,
        Func<ProcessActionResult> action,
        bool refreshOnSuccess = false,
        string? successTitle = null)
    {
        _ = ExecuteBackgroundAsync(failureTitle, action, refreshOnSuccess, successTitle);
    }

    private async Task ExecuteBackgroundAsync(
        string failureTitle,
        Func<ProcessActionResult> action,
        bool refreshOnSuccess,
        string? successTitle)
    {
        ProcessActionResult result;
        try
        {
            result = await Task.Run(action).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"{failureTitle}: {exception}");
            result = new ProcessActionResult(false, exception.Message);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed) return;
            if (!result.Succeeded)
            {
                _reportError(failureTitle, result.Message);
                return;
            }

            if (refreshOnSuccess) _requestRefresh();
            if (!string.IsNullOrEmpty(successTitle) && _reportInformation != null)
                _reportInformation(successTitle, result.Value);
        });
    }

    private void ShowAffinityWindow(ProcessTerminationTarget target)
    {
        if (!ProcessNativeActions.TryGetAffinity(
                target,
                out ProcessAffinityInfo affinity,
                out string errorMessage))
        {
            _reportError("Set affinity failed", errorMessage);
            return;
        }

        Window? owner = _owner;
        if (owner == null) return;

        ProcessAffinityWindow affinityWindow = new(
            target,
            affinity,
            _palette,
            _reportError);
        affinityWindow.Closed += OnActionWindowClosed;
        _actionWindows.Add(affinityWindow);
        affinityWindow.Show(owner);
    }

    private void ShowMenu(IReadOnlyList<TrayMenuEntry> entries)
    {
        Window? owner = _owner;
        if (owner == null) return;

        CloseMenu();
        ProcessRowContextMenuWindow menuWindow = new(
            entries,
            new TrayMenuWindowOptions
            {
                Palette = _palette,
                Rounded = _enableRoundedCorners,
                FontSize = ContextMenuFontSize
            });
        _menuWindow = menuWindow;
        menuWindow.Closed += OnMenuClosed;
        menuWindow.ShowAt(owner, _menuPosition);
    }

    private void CloseMenu()
    {
        ProcessRowContextMenuWindow? menuWindow = _menuWindow;
        if (menuWindow == null) return;

        _menuWindow = null;
        menuWindow.Closed -= OnMenuClosed;
        menuWindow.Close();
    }

    private void OnMenuClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is ProcessRowContextMenuWindow menuWindow)
            menuWindow.Closed -= OnMenuClosed;
        if (ReferenceEquals(sender, _menuWindow)) _menuWindow = null;
    }

    private void OnActionWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is not Window window) return;
        window.Closed -= OnActionWindowClosed;
        _actionWindows.Remove(window);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        CloseMenu();
        Window[] actionWindows = [.. _actionWindows];
        _actionWindows.Clear();
        for (int windowIndex = 0; windowIndex < actionWindows.Length; windowIndex++)
        {
            Window actionWindow = actionWindows[windowIndex];
            actionWindow.Closed -= OnActionWindowClosed;
            actionWindow.Close();
        }

        _owner = null;
    }

    private delegate bool TryProcessAction(
        ProcessTerminationTarget target,
        out string errorMessage);

    private readonly record struct ProcessActionResult(bool Succeeded, string Message, string Value = "")
    {
        public static ProcessActionResult Success { get; } = new(true, string.Empty);
    }
}

/// <summary>Positions the shared TADN context-menu window at an arbitrary grid pointer location.</summary>
internal sealed class ProcessRowContextMenuWindow(
    IReadOnlyList<TrayMenuEntry> entries,
    TrayMenuWindowOptions options)
    : TrayMenuWindow(entries, options)
{
    private const int ScreenEdgePadding = 8;
    private const int OffscreenCoordinate = -32_000;

    public void ShowAt(Window owner, PixelPoint screenPosition)
    {
        ArgumentNullException.ThrowIfNull(owner);

        Opacity = 0;
        Position = new PixelPoint(OffscreenCoordinate, OffscreenCoordinate);
        Show(owner);
        Dispatcher.UIThread.Post(() => PositionAt(screenPosition), DispatcherPriority.Loaded);
    }

    private void PositionAt(PixelPoint screenPosition)
    {
        if (!IsVisible) return;

        UpdateLayout();
        PixelRect workArea = (Screens.ScreenFromPoint(screenPosition) ?? Screens.Primary)?.WorkingArea
                             ?? new PixelRect(0, 0, 1920, 1080);
        int menuWidth = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        int menuHeight = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        int maximumX = Math.Max(workArea.X + ScreenEdgePadding, workArea.Right - menuWidth - ScreenEdgePadding);
        int maximumY = Math.Max(workArea.Y + ScreenEdgePadding, workArea.Bottom - menuHeight - ScreenEdgePadding);
        Position = new PixelPoint(
            Math.Clamp(screenPosition.X, workArea.X + ScreenEdgePadding, maximumX),
            Math.Clamp(screenPosition.Y, workArea.Y + ScreenEdgePadding, maximumY));
        Opacity = 1;
        Activate();
    }
}

/// <summary>Nonmodal processor-affinity editor for one identity-checked process instance.</summary>
internal sealed class ProcessAffinityWindow : Window
{
    private const double WindowWidth = 420;
    private const double WindowHeight = 520;
    private const double ContentPadding = 16;
    private const double ItemSpacing = 8;
    private const double ProcessorItemWidth = 88;

    private readonly ProcessTerminationTarget _target;
    private readonly ProcessAffinityInfo _affinity;
    private readonly Action<string, string> _reportError;
    private readonly List<CheckBox> _processorChecks = [];

    public ProcessAffinityWindow(
        ProcessTerminationTarget target,
        ProcessAffinityInfo affinity,
        SettingsPalette palette,
        Action<string, string> reportError)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(reportError);

        _target = target;
        _affinity = affinity;
        _reportError = reportError;
        Title = $"Processor affinity - PID {target.ProcessID}";
        Width = WindowWidth;
        Height = WindowHeight;
        MinWidth = WindowWidth;
        MinHeight = 360;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = TrayAppDotNETSettingsUI.Brush(palette.Background);
        Content = BuildContent(palette);
    }

    private Control BuildContent(SettingsPalette palette)
    {
        Grid root = new()
        {
            Margin = new Thickness(ContentPadding),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };

        TextBlock explanation = TrayAppDotNETSettingsUI.Text(
            "Select the processors on which this process may run.",
            palette,
            14);
        explanation.Margin = new Thickness(0, 0, 0, ContentPadding);
        root.Children.Add(explanation);

        WrapPanel processorPanel = new()
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = ProcessorItemWidth,
            ItemHeight = 32
        };
        for (int processorIndex = 0; processorIndex < 64; processorIndex++)
        {
            ulong processorBit = 1UL << processorIndex;
            if ((_affinity.SystemMask & processorBit) == 0) continue;

            CheckBox processorCheck = new()
            {
                Content = $"CPU {processorIndex}",
                IsChecked = (_affinity.ProcessMask & processorBit) != 0,
                Tag = processorIndex,
                Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
                VerticalAlignment = VerticalAlignment.Center
            };
            _processorChecks.Add(processorCheck);
            processorPanel.Children.Add(processorCheck);
        }

        ScrollViewer processorScroll = new()
        {
            Content = processorPanel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(processorScroll, 1);
        root.Children.Add(processorScroll);

        Grid actions = new()
        {
            Margin = new Thickness(0, ContentPadding, 0, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        SettingsButton selectAllButton = TrayAppDotNETSettingsUI.Button("Select all", palette);
        selectAllButton.Click += OnSelectAllClick;
        actions.Children.Add(selectAllButton);
        SettingsButton clearButton = TrayAppDotNETSettingsUI.Button("Clear", palette);
        clearButton.Margin = new Thickness(ItemSpacing, 0, 0, 0);
        clearButton.Click += OnClearClick;
        Grid.SetColumn(clearButton, 1);
        actions.Children.Add(clearButton);
        SettingsButton cancelButton = TrayAppDotNETSettingsUI.Button("Cancel", palette);
        cancelButton.Click += OnCancelClick;
        Grid.SetColumn(cancelButton, 3);
        actions.Children.Add(cancelButton);
        SettingsButton applyButton = TrayAppDotNETSettingsUI.Button("Apply", palette);
        applyButton.Margin = new Thickness(ItemSpacing, 0, 0, 0);
        applyButton.Click += OnApplyClick;
        Grid.SetColumn(applyButton, 4);
        actions.Children.Add(applyButton);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        return root;
    }

    private void OnSelectAllClick(object? sender, EventArgs eventArgs)
    {
        for (int checkIndex = 0; checkIndex < _processorChecks.Count; checkIndex++)
            _processorChecks[checkIndex].IsChecked = true;
    }

    private void OnClearClick(object? sender, EventArgs eventArgs)
    {
        for (int checkIndex = 0; checkIndex < _processorChecks.Count; checkIndex++)
            _processorChecks[checkIndex].IsChecked = false;
    }

    private void OnCancelClick(object? sender, EventArgs eventArgs) => Close();

    private void OnApplyClick(object? sender, EventArgs eventArgs)
    {
        ulong selectedMask = 0;
        for (int checkIndex = 0; checkIndex < _processorChecks.Count; checkIndex++)
        {
            CheckBox processorCheck = _processorChecks[checkIndex];
            if (processorCheck.IsChecked == true && processorCheck.Tag is int processorIndex)
                selectedMask |= 1UL << processorIndex;
        }

        if (!ProcessNativeActions.TrySetAffinity(_target, selectedMask, out string errorMessage))
        {
            _reportError("Set affinity failed", errorMessage);
            return;
        }

        Close();
    }
}
