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
    private const string SelectedGlyph = "\uE73E";

    private readonly SettingsPalette _palette;
    private readonly bool _enableRoundedCorners;
    private readonly ITrayAppDotNETTrayMenuSettings _trayMenuSettings;
    private readonly TryTerminateProcessAction _terminateProcess;
    private readonly Action<ProcessEndTaskRequest> _requestEndTask;
    private readonly Action _requestRefresh;
    private readonly Action<string, string> _reportError;
    private readonly Action<ProcessCopyPreviewMode> _setCopyPreview;
    private readonly Action<string, string>? _reportInformation;
    private readonly HashSet<Window> _actionWindows = [];
    private TaskManagerContextMenuWindow? _menuWindow;
    private Window? _owner;
    private PixelPoint _menuPosition;
    private ProcessCopyPreviewMode _hoveredCopyPreview;
    private bool _disposed;

    public ProcessRowContextMenuController(
        SettingsPalette palette,
        bool enableRoundedCorners,
        ITrayAppDotNETTrayMenuSettings trayMenuSettings,
        TryTerminateProcessAction terminateProcess,
        Action<ProcessEndTaskRequest> requestEndTask,
        Action requestRefresh,
        Action<string, string> reportError,
        Action<ProcessCopyPreviewMode> setCopyPreview,
        Action<string, string>? reportInformation = null)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(trayMenuSettings);
        ArgumentNullException.ThrowIfNull(terminateProcess);
        ArgumentNullException.ThrowIfNull(requestEndTask);
        ArgumentNullException.ThrowIfNull(requestRefresh);
        ArgumentNullException.ThrowIfNull(reportError);
        ArgumentNullException.ThrowIfNull(setCopyPreview);

        _palette = palette;
        _enableRoundedCorners = enableRoundedCorners;
        _trayMenuSettings = trayMenuSettings;
        _terminateProcess = terminateProcess;
        _requestEndTask = requestEndTask;
        _requestRefresh = requestRefresh;
        _reportError = reportError;
        _setCopyPreview = setCopyPreview;
        _reportInformation = reportInformation;
    }

    /// <summary>Shows a common TADN menu for one immutable process identity at a screen position.</summary>
    public void Show(Window owner, ProcessRowContextMenuRequest request)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(owner);

        CloseMenu();
        _owner = owner;
        _menuPosition = request.ScreenPosition;
        List<ContextMenuEntry> entries = BuildMainEntries(request);
        ShowMenu(entries);
    }

    private List<ContextMenuEntry> BuildMainEntries(ProcessRowContextMenuRequest request)
    {
        ProcessTerminationTarget target = request.Target;
        ContextMenuEntryBuilder entries = new();
        entries.Add(new ContextMenuEntry("Copy", () => ExecuteCopy(request.CellCopyText))
        {
            HoverChanged = isHovered => SetCopyPreviewHover(ProcessCopyPreviewMode.Cell, isHovered)
        });
        entries.Add(new ContextMenuEntry("Copy row", () => ExecuteCopy(request.RowCopyText))
        {
            HoverChanged = isHovered => SetCopyPreviewHover(ProcessCopyPreviewMode.Row, isHovered)
        });
        entries.AddSeparator();
        entries.Add("End task", () => _requestEndTask(request.EndTaskRequest));
        entries.Add("End process tree", () => ExecuteEndProcessTree(target));
        entries.AddSeparator();
        entries.AddSubmenu("Set priority", () => BuildPriorityEntries(target));
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

    private void SetCopyPreviewHover(ProcessCopyPreviewMode previewMode, bool isHovered)
    {
        if (isHovered)
        {
            _hoveredCopyPreview = previewMode;
            _setCopyPreview(previewMode);
            return;
        }

        if (_hoveredCopyPreview != previewMode) return;
        _hoveredCopyPreview = ProcessCopyPreviewMode.None;
        _setCopyPreview(ProcessCopyPreviewMode.None);
    }

    private void ClearCopyPreview()
    {
        _hoveredCopyPreview = ProcessCopyPreviewMode.None;
        _setCopyPreview(ProcessCopyPreviewMode.None);
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

    private IReadOnlyList<ContextMenuEntry> BuildPriorityEntries(ProcessTerminationTarget target)
    {
        if (!ProcessNativeActions.TryGetPriority(
                target,
                out ProcessPriorityLevel currentPriority,
                out string errorMessage))
        {
            _reportError("Set priority failed", errorMessage);
            return [];
        }

        ContextMenuEntryBuilder entries = new();
        AddPriorityEntry(entries, "Realtime", ProcessPriorityLevel.Realtime, currentPriority, target);
        AddPriorityEntry(entries, "High", ProcessPriorityLevel.High, currentPriority, target);
        AddPriorityEntry(entries, "Above normal", ProcessPriorityLevel.AboveNormal, currentPriority, target);
        AddPriorityEntry(entries, "Normal", ProcessPriorityLevel.Normal, currentPriority, target);
        AddPriorityEntry(entries, "Below normal", ProcessPriorityLevel.BelowNormal, currentPriority, target);
        AddPriorityEntry(entries, "Low", ProcessPriorityLevel.Idle, currentPriority, target);
        return entries.ToList();
    }

    private void AddPriorityEntry(
        ContextMenuEntryBuilder entries,
        string label,
        ProcessPriorityLevel priority,
        ProcessPriorityLevel currentPriority,
        ProcessTerminationTarget target)
    {
        entries.Add(new ContextMenuEntry(
            label,
            () => ExecuteSetPriority(target, priority))
        {
            TrailingGlyph = priority == currentPriority ? SelectedGlyph : null
        });
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

    private void ShowMenu(IReadOnlyList<ContextMenuEntry> entries)
    {
        Window? owner = _owner;
        if (owner == null) return;

        CloseMenu();
        TaskManagerContextMenuWindow menuWindow = new(
            entries,
            _palette,
            _enableRoundedCorners,
            _trayMenuSettings);
        _menuWindow = menuWindow;
        menuWindow.Closed += OnMenuClosed;
        menuWindow.ShowAt(owner, _menuPosition);
    }

    private void CloseMenu()
    {
        ClearCopyPreview();
        TaskManagerContextMenuWindow? menuWindow = _menuWindow;
        if (menuWindow == null) return;

        _menuWindow = null;
        menuWindow.Closed -= OnMenuClosed;
        menuWindow.Close();
    }

    private void OnMenuClosed(object? sender, EventArgs eventArgs)
    {
        ClearCopyPreview();
        if (sender is TaskManagerContextMenuWindow menuWindow)
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
