using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Threading;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerGlyphCatalog = TaskManagerTrayAppDotNET.Visuals.GlyphCatalog;

namespace TaskManagerTrayAppDotNET.UI;

internal readonly record struct ProcessAffinityTarget(
    ProcessEndTaskItem Process,
    ProcessAffinityInfo Affinity);

/// <summary>Owns transient row menus and dispatches process actions away from the UI thread.</summary>
internal sealed class ProcessRowContextMenuController : IDisposable
{
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
        IReadOnlyList<ProcessEndTaskItem> processes = request.EndTaskRequest.Processes;
        bool isMultiple = request.IsMultiple;
        ContextMenuEntryBuilder entries = new();
        entries.Add(new ContextMenuEntry(Text: "Copy", () => ExecuteCopy(request.CellCopyText))
        {
            HoverChanged = isHovered => SetCopyPreviewHover(ProcessCopyPreviewMode.Cell, isHovered)
        });
        entries.Add(new ContextMenuEntry(
            Text: isMultiple ? "Copy rows" : "Copy row",
            () => ExecuteCopy(request.RowCopyText))
        {
            HoverChanged = isHovered => SetCopyPreviewHover(ProcessCopyPreviewMode.Row, isHovered)
        });
        entries.AddSeparator();
        entries.Add(text: isMultiple ? "End tasks" : "End task", () => _requestEndTask(request.EndTaskRequest));
        entries.Add(
            text: isMultiple ? "End process trees" : "End process tree",
            () => ExecuteEndProcessTrees(processes));
        entries.AddSeparator();
        entries.AddSubmenu(
            text: isMultiple ? "Set priorities" : "Set priority",
            () => BuildPriorityEntries(processes));
        entries.Add(
            text: isMultiple ? "Set affinities" : "Set affinity",
            () => ShowAffinityWindow(processes));
        if (isMultiple) return entries.ToList();

        entries.AddSeparator();
        entries.Add(text: "Create memory dump file", () => ExecuteCreateMemoryDump(target));
        entries.Add(text: "Open file location", () => ExecuteBackground(
            failureTitle: "Open file location failed",
            target,
            ProcessNativeActions.TryOpenFileLocation));
        entries.Add(text: "Properties", () => ExecuteBackground(
            failureTitle: "Properties failed",
            target,
            ProcessNativeActions.TryOpenProperties));

        // Window discovery occurs only when the user opens a row menu.
        if (ProcessNativeActions.HasTopLevelWindow(target.ProcessID))
        {
            entries.AddSeparator();
            entries.Add(text: "Switch to", () => ExecuteWindowAction(
                failureTitle: "Switch to failed",
                target,
                ProcessNativeActions.TrySwitchToWindow));
            entries.Add(text: "Bring to front", () => ExecuteWindowAction(
                failureTitle: "Bring to front failed",
                target,
                ProcessNativeActions.TryBringWindowToFront));
            entries.Add(text: "Minimize", () => ExecuteWindowAction(
                failureTitle: "Minimize failed",
                target,
                ProcessNativeActions.TryMinimizeWindow));
            entries.Add(text: "Maximize", () => ExecuteWindowAction(
                failureTitle: "Maximize failed",
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
            if (!_disposed) _reportError(arg1: "Copy failed", exception.Message);
        }
    }

    private IReadOnlyList<ContextMenuEntry> BuildPriorityEntries(
        IReadOnlyList<ProcessEndTaskItem> processes)
    {
        ProcessPriorityLevel? currentPriority = null;
        if (processes.Count == 1)
        {
            if (!ProcessNativeActions.TryGetPriority(
                    processes[0].Target,
                    out ProcessPriorityLevel selectedPriority,
                    out string errorMessage))
            {
                _reportError(arg1: "Set priority failed", errorMessage);
                return [];
            }

            currentPriority = selectedPriority;
        }

        ContextMenuEntryBuilder entries = new();
        AddPriorityEntry(entries, label: "Realtime", ProcessPriorityLevel.Realtime, currentPriority, processes);
        AddPriorityEntry(entries, label: "High", ProcessPriorityLevel.High, currentPriority, processes);
        AddPriorityEntry(entries, label: "Above normal", ProcessPriorityLevel.AboveNormal, currentPriority, processes);
        AddPriorityEntry(entries, label: "Normal", ProcessPriorityLevel.Normal, currentPriority, processes);
        AddPriorityEntry(entries, label: "Below normal", ProcessPriorityLevel.BelowNormal, currentPriority, processes);
        AddPriorityEntry(entries, label: "Low", ProcessPriorityLevel.Idle, currentPriority, processes);
        return entries.ToList();
    }

    private void AddPriorityEntry(
        ContextMenuEntryBuilder entries,
        string label,
        ProcessPriorityLevel priority,
        ProcessPriorityLevel? currentPriority,
        IReadOnlyList<ProcessEndTaskItem> processes)
    {
        entries.Add(new ContextMenuEntry(
            label,
            () => ExecuteSetPriority(processes, priority))
        {
            TrailingGlyphMetadata = priority == currentPriority
                ? TaskManagerGlyphCatalog.SELECTED
                : null
        });
    }

    private void ExecuteEndProcessTrees(IReadOnlyList<ProcessEndTaskItem> processes) =>
        _ = ExecuteEndProcessTreesAsync(processes);

    private async Task ExecuteEndProcessTreesAsync(IReadOnlyList<ProcessEndTaskItem> processes)
    {
        BatchActionResult result;
        try
        {
            result = await Task.Run(() =>
            {
                List<string> failures = [];
                bool refreshNeeded = false;
                for (int processIndex = 0; processIndex < processes.Count; processIndex++)
                {
                    ProcessEndTaskItem process = processes[processIndex];
                    if (CriticalProcessActions.IsTargetGone(process.Target))
                    {
                        refreshNeeded = true;
                        continue;
                    }

                    if (ProcessNativeActions.TryTerminateDescendants(
                            process.Target,
                            out string errorMessage))
                    {
                        refreshNeeded = true;
                        continue;
                    }

                    // Descendant termination can partially succeed before reporting a failure
                    refreshNeeded = true;
                    if (CriticalProcessActions.IsTargetGone(process.Target)) continue;

                    failures.Add(processes.Count > 1
                        ? FormatProcessFailure(process, errorMessage)
                        : errorMessage);
                }

                ProcessTerminationBatchResult rootResult =
                    ProcessTerminationBatchFunctions.Execute(
                        processes,
                        _terminateProcess,
                        CriticalProcessActions.IsTargetGone);
                if (!string.IsNullOrEmpty(rootResult.ErrorMessage))
                    failures.Add(rootResult.ErrorMessage);
                return new BatchActionResult(
                    refreshNeeded || rootResult.RefreshNeeded,
                    string.Join(separator: "\n", failures));
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"End process trees failed: {exception}");
            result = new BatchActionResult(RefreshNeeded: true, exception.Message);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed) return;
            if (result.RefreshNeeded) _requestRefresh();
            if (string.IsNullOrEmpty(result.ErrorMessage)) return;

            _reportError(
                processes.Count > 1 ? "End process trees failed" : "End process tree failed",
                result.ErrorMessage);
        });
    }

    private void ExecuteSetPriority(
        IReadOnlyList<ProcessEndTaskItem> processes,
        ProcessPriorityLevel priority)
    {
        if (processes.Count == 1)
        {
            ExecuteBackground(
                failureTitle: "Set priority failed",
                processes[0].Target,
                (actionTarget, out errorMessage) =>
                    ProcessNativeActions.TrySetPriority(actionTarget, priority, out errorMessage));
            return;
        }

        ExecuteBatchBackground(
            failureTitle: "Set priorities failed",
            processes,
            (actionTarget, out errorMessage) =>
                ProcessNativeActions.TrySetPriority(actionTarget, priority, out errorMessage),
            refreshOnSuccess: true);
    }

    private void ExecuteCreateMemoryDump(ProcessTerminationTarget target) => ExecuteBackground(
        failureTitle: "Create memory dump failed",
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
        string? successTitle = null) =>
        _ = ExecuteBackgroundAsync(failureTitle, action, refreshOnSuccess, successTitle);

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
            result = new ProcessActionResult(Succeeded: false, exception.Message);
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

    private void ExecuteBatchBackground(
        string failureTitle,
        IReadOnlyList<ProcessEndTaskItem> processes,
        TryProcessAction action,
        bool refreshOnSuccess) =>
        _ = ExecuteBatchBackgroundAsync(failureTitle, processes, action, refreshOnSuccess);

    private async Task ExecuteBatchBackgroundAsync(
        string failureTitle,
        IReadOnlyList<ProcessEndTaskItem> processes,
        TryProcessAction action,
        bool refreshOnSuccess)
    {
        BatchActionResult result;
        try
        {
            result = await Task.Run(() =>
            {
                List<string> failures = [];
                bool refreshNeeded = false;
                for (int processIndex = 0; processIndex < processes.Count; processIndex++)
                {
                    ProcessEndTaskItem process = processes[processIndex];
                    if (CriticalProcessActions.IsTargetGone(process.Target))
                    {
                        refreshNeeded = true;
                        continue;
                    }

                    if (action(process.Target, out string errorMessage))
                    {
                        refreshNeeded = true;
                        continue;
                    }

                    if (CriticalProcessActions.IsTargetGone(process.Target))
                    {
                        refreshNeeded = true;
                        continue;
                    }

                    failures.Add(FormatProcessFailure(process, errorMessage));
                }

                return new BatchActionResult(refreshNeeded, string.Join(separator: "\n", failures));
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"{failureTitle}: {exception}");
            result = new BatchActionResult(RefreshNeeded: false, exception.Message);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed) return;
            if (refreshOnSuccess && result.RefreshNeeded) _requestRefresh();
            if (!string.IsNullOrEmpty(result.ErrorMessage))
                _reportError(failureTitle, result.ErrorMessage);
        });
    }

    private static string FormatProcessFailure(ProcessEndTaskItem process, string errorMessage)
    {
        string processName = string.IsNullOrWhiteSpace(process.ProcessName)
            ? $"PID {process.Target.ProcessID}"
            : $"{process.ProcessName} (PID {process.Target.ProcessID})";
        string detail = string.IsNullOrWhiteSpace(errorMessage)
            ? "The process action failed."
            : errorMessage;
        return $"{processName}: {detail}";
    }

    private void ShowAffinityWindow(IReadOnlyList<ProcessEndTaskItem> processes)
    {
        List<ProcessAffinityTarget> affinityTargets = new(processes.Count);
        List<string> failures = [];
        for (int processIndex = 0; processIndex < processes.Count; processIndex++)
        {
            ProcessEndTaskItem process = processes[processIndex];
            if (CriticalProcessActions.IsTargetGone(process.Target)) continue;
            if (ProcessNativeActions.TryGetAffinity(
                    process.Target,
                    out ProcessAffinityInfo affinity,
                    out string errorMessage))
            {
                affinityTargets.Add(new ProcessAffinityTarget(process, affinity));
                continue;
            }

            if (!CriticalProcessActions.IsTargetGone(process.Target))
            {
                failures.Add(processes.Count > 1
                    ? FormatProcessFailure(process, errorMessage)
                    : errorMessage);
            }
        }

        if (failures.Count > 0)
        {
            _reportError(
                processes.Count > 1 ? "Set affinities failed" : "Set affinity failed",
                string.Join(separator: "\n", failures));
            return;
        }

        if (affinityTargets.Count == 0)
        {
            _requestRefresh();
            return;
        }

        Window? owner = _owner;
        if (owner == null) return;

        ProcessAffinityWindow affinityWindow = new(
            affinityTargets,
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

#if DEBUG
    /// <summary>Applies current AXAML metrics to open action editors without replacing their input state.</summary>
    internal void ApplyAXAMLResources(TaskManagerWindowResources resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        foreach (Window actionWindow in _actionWindows)
        {
            if (actionWindow is ProcessAffinityWindow affinityWindow)
                affinityWindow.ApplyAXAMLResources(resources);
        }
    }
#endif

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
        public static ProcessActionResult Success { get; } = new(Succeeded: true, string.Empty);
    }

    private readonly record struct BatchActionResult(
        bool RefreshNeeded,
        string ErrorMessage);
}

/// <summary>Nonmodal processor-affinity editor for identity-checked process instances.</summary>
internal sealed class ProcessAffinityWindow : Window
{
    private readonly ProcessAffinityTarget[] _targets;
    private readonly Action<string, string> _reportError;
    private readonly List<CheckBox> _processorChecks = [];
    private readonly SettingsButton _applyButton;
    private bool _isApplying;
#if DEBUG
    private Grid? _root;
    private TextBlock? _explanation;
    private WrapPanel? _processorPanel;
    private Grid? _actions;
    private SettingsButton? _clearButton;
    private double _axamlWidth;
    private double _axamlHeight;
    private double _axamlMinWidth;
    private double _axamlMinHeight;
#endif

    public ProcessAffinityWindow(
        IReadOnlyList<ProcessAffinityTarget> targets,
        SettingsPalette palette,
        Action<string, string> reportError)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(reportError);
        if (targets.Count == 0)
            throw new ArgumentException("At least one process is required.", nameof(targets));

        _targets = new ProcessAffinityTarget[targets.Count];
        for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            _targets[targetIndex] = targets[targetIndex];
        _reportError = reportError;
        TaskManagerWindowResources resources = TaskManagerWindowResources.Current;
        Title = targets.Count == 1
            ? $"Processor affinity - PID {targets[0].Process.Target.ProcessID}"
            : $"Processor affinities - {targets.Count} processes";
        Width = resources.AxamlProcessAffinity.WindowWidth;
        Height = resources.AxamlProcessAffinity.WindowHeight;
        MinWidth = resources.AxamlProcessAffinity.WindowWidth;
        MinHeight = resources.AxamlProcessAffinity.WindowMinHeight;
#if DEBUG
        _axamlWidth = Width;
        _axamlHeight = Height;
        _axamlMinWidth = MinWidth;
        _axamlMinHeight = MinHeight;
#endif
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = TrayAppDotNETSettingsUI.Brush(palette.Background);
        Content = BuildContent(palette, resources, out _applyButton);
    }

    private Control BuildContent(
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        out SettingsButton applyButton)
    {
        Grid root = new()
        {
            Margin = resources.AxamlProcessAffinity.ContentMargin,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };
#if DEBUG
        _root = root;
#endif

        TextBlock explanation = TrayAppDotNETSettingsUI.Text(
            text: _targets.Length == 1
                ? "Select the processors on which this process may run."
                : "Select the processors on which the selected processes may run. Mixed values are preserved.",
            palette,
            resources.AxamlProcessAffinity.ExplanationFontSize);
        explanation.Margin = resources.AxamlProcessAffinity.ExplanationMargin;
#if DEBUG
        _explanation = explanation;
#endif
        root.Children.Add(explanation);

        WrapPanel processorPanel = new()
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = resources.AxamlProcessAffinity.ProcessorItemWidth,
            ItemHeight = resources.AxamlProcessAffinity.ProcessorItemHeight
        };
#if DEBUG
        _processorPanel = processorPanel;
#endif
        ulong availableProcessorMask = 0;
        for (int targetIndex = 0; targetIndex < _targets.Length; targetIndex++)
            availableProcessorMask |= _targets[targetIndex].Affinity.SystemMask;
        for (int processorIndex = 0; processorIndex < 64; processorIndex++)
        {
            ulong processorBit = 1UL << processorIndex;
            if ((availableProcessorMask & processorBit) == 0) continue;

            bool isSelectedByAny = false;
            bool isSelectedByAll = true;
            for (int targetIndex = 0; targetIndex < _targets.Length; targetIndex++)
            {
                ProcessAffinityInfo affinity = _targets[targetIndex].Affinity;
                if ((affinity.SystemMask & processorBit) == 0) continue;
                bool isSelected = (affinity.ProcessMask & processorBit) != 0;
                isSelectedByAny |= isSelected;
                isSelectedByAll &= isSelected;
            }

            CheckBox processorCheck = new()
            {
                Content = $"CPU {processorIndex}",
                IsThreeState = _targets.Length > 1,
                IsChecked = isSelectedByAll
                    ? true
                    : isSelectedByAny
                        ? null
                        : false,
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
        Grid.SetRow(processorScroll, value: 1);
        root.Children.Add(processorScroll);

        Grid actions = new()
        {
            Margin = resources.AxamlProcessAffinity.ActionsMargin,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            }
        };
#if DEBUG
        _actions = actions;
#endif
        SettingsButton selectAllButton = TrayAppDotNETSettingsUI.Button(text: "Select all", palette);
        selectAllButton.Click += OnSelectAllClick;
        actions.Children.Add(selectAllButton);
        SettingsButton clearButton = TrayAppDotNETSettingsUI.Button(text: "Clear", palette);
        clearButton.Margin = new Thickness(
            resources.AxamlProcessAffinity.ActionButtonSpacing,
            top: 0,
            right: 0,
            bottom: 0);
        clearButton.Click += OnClearClick;
#if DEBUG
        _clearButton = clearButton;
#endif
        Grid.SetColumn(clearButton, value: 1);
        actions.Children.Add(clearButton);
        SettingsButton cancelButton = TrayAppDotNETSettingsUI.Button(text: "Cancel", palette);
        cancelButton.Click += OnCancelClick;
        Grid.SetColumn(cancelButton, value: 3);
        actions.Children.Add(cancelButton);
        applyButton = TrayAppDotNETSettingsUI.Button(text: "Apply", palette);
        applyButton.Margin = new Thickness(
            resources.AxamlProcessAffinity.ActionButtonSpacing,
            top: 0,
            right: 0,
            bottom: 0);
        applyButton.Click += OnApplyClick;
        Grid.SetColumn(applyButton, value: 4);
        actions.Children.Add(applyButton);
        Grid.SetRow(actions, value: 2);
        root.Children.Add(actions);
        return root;
    }

#if DEBUG
    /// <summary>Applies current affinity AXAML metrics while retaining checked processors.</summary>
    internal void ApplyAXAMLResources(TaskManagerWindowResources resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        double nextWidth = resources.AxamlProcessAffinity.WindowWidth;
        double nextHeight = resources.AxamlProcessAffinity.WindowHeight;
        double nextMinWidth = resources.AxamlProcessAffinity.WindowWidth;
        double nextMinHeight = resources.AxamlProcessAffinity.WindowMinHeight;
        if (nextWidth != _axamlWidth) Width = nextWidth;
        if (nextHeight != _axamlHeight) Height = nextHeight;
        if (nextMinWidth != _axamlMinWidth) MinWidth = nextMinWidth;
        if (nextMinHeight != _axamlMinHeight) MinHeight = nextMinHeight;
        _axamlWidth = nextWidth;
        _axamlHeight = nextHeight;
        _axamlMinWidth = nextMinWidth;
        _axamlMinHeight = nextMinHeight;

        _root!.Margin = resources.AxamlProcessAffinity.ContentMargin;
        _explanation!.FontSize = resources.AxamlProcessAffinity.ExplanationFontSize;
        _explanation.Margin = resources.AxamlProcessAffinity.ExplanationMargin;
        _processorPanel!.ItemWidth = resources.AxamlProcessAffinity.ProcessorItemWidth;
        _processorPanel.ItemHeight = resources.AxamlProcessAffinity.ProcessorItemHeight;
        _actions!.Margin = resources.AxamlProcessAffinity.ActionsMargin;
        _clearButton!.Margin = new Thickness(
            resources.AxamlProcessAffinity.ActionButtonSpacing,
            top: 0,
            right: 0,
            bottom: 0);
        _applyButton.Margin = new Thickness(
            resources.AxamlProcessAffinity.ActionButtonSpacing,
            top: 0,
            right: 0,
            bottom: 0);
    }
#endif

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

    private void OnApplyClick(object? sender, EventArgs eventArgs) => _ = ApplyAsync();

    private async Task ApplyAsync()
    {
        if (_isApplying) return;

        ulong[] selectedMasks = new ulong[_targets.Length];
        for (int targetIndex = 0; targetIndex < _targets.Length; targetIndex++)
            selectedMasks[targetIndex] = _targets[targetIndex].Affinity.ProcessMask;
        for (int checkIndex = 0; checkIndex < _processorChecks.Count; checkIndex++)
        {
            CheckBox processorCheck = _processorChecks[checkIndex];
            if (processorCheck is not { Tag: int processorIndex }
                || !processorCheck.IsChecked.HasValue)
                continue;

            ulong processorBit = 1UL << processorIndex;
            for (int targetIndex = 0; targetIndex < _targets.Length; targetIndex++)
            {
                if ((_targets[targetIndex].Affinity.SystemMask & processorBit) == 0) continue;
                if (processorCheck.IsChecked.Value)
                    selectedMasks[targetIndex] |= processorBit;
                else
                    selectedMasks[targetIndex] &= ~processorBit;
            }
        }

        for (int targetIndex = 0; targetIndex < _targets.Length; targetIndex++)
        {
            selectedMasks[targetIndex] &= _targets[targetIndex].Affinity.SystemMask;
            if (selectedMasks[targetIndex] != 0) continue;

            _reportError(
                _targets.Length > 1 ? "Set affinities failed" : "Set affinity failed",
                "Select at least one processor for every selected process.");
            return;
        }

        _isApplying = true;
        _applyButton.IsEnabled = false;
        try
        {
            string errorMessage = await Task.Run(() => ApplyAffinities(selectedMasks));
            if (!string.IsNullOrEmpty(errorMessage))
            {
                _reportError(
                    _targets.Length > 1 ? "Set affinities failed" : "Set affinity failed",
                    errorMessage);
                return;
            }

            Close();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Set affinities failed: {exception}");
            _reportError(
                _targets.Length > 1 ? "Set affinities failed" : "Set affinity failed",
                exception.Message);
        }
        finally
        {
            _isApplying = false;
            if (IsVisible) _applyButton.IsEnabled = true;
        }
    }

    private string ApplyAffinities(IReadOnlyList<ulong> selectedMasks)
    {
        List<string> failures = [];
        for (int targetIndex = 0; targetIndex < _targets.Length; targetIndex++)
        {
            ProcessAffinityTarget target = _targets[targetIndex];
            if (CriticalProcessActions.IsTargetGone(target.Process.Target)) continue;
            if (ProcessNativeActions.TrySetAffinity(
                    target.Process.Target,
                    selectedMasks[targetIndex],
                    out string errorMessage))
                continue;
            if (CriticalProcessActions.IsTargetGone(target.Process.Target)) continue;

            failures.Add(_targets.Length > 1
                ? FormatAffinityFailure(target.Process, errorMessage)
                : errorMessage);
        }

        return string.Join(separator: "\n", failures);
    }

    private static string FormatAffinityFailure(ProcessEndTaskItem process, string errorMessage)
    {
        string processName = string.IsNullOrWhiteSpace(process.ProcessName)
            ? $"PID {process.Target.ProcessID}"
            : $"{process.ProcessName} (PID {process.Target.ProcessID})";
        string detail = string.IsNullOrWhiteSpace(errorMessage)
            ? "The process affinity could not be changed."
            : errorMessage;
        return $"{processName}: {detail}";
    }
}
