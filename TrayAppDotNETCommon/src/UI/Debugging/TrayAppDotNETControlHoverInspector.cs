#if DEBUG
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace TrayAppDotNETCommon.UI.Debugging;

/// <summary>Installs the debug hotkey that fully enables or disables the control hover inspector.</summary>
internal static class TrayAppDotNETControlHoverInspector
{
    private static IClassicDesktopStyleApplicationLifetime? _lifetime;
    private static UIResourceScope? _activationSubscriptions;
    private static ControlHoverInspectorSession? _session;
    private static bool _activationShortcutPressed;

    public static void Attach(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        if (_lifetime != null) return;

        UIResourceScope activationSubscriptions = new(
            nameof(TrayAppDotNETControlHoverInspector),
            exception => TADNLog.Log(
                $"Control hover inspector hotkey cleanup failed: {exception.GetType().Name}: {exception.Message}"));
        try
        {
            IDisposable keyDownHandler = InputElement.KeyDownEvent.AddClassHandler<TopLevel>(
                OnActivationKeyDown,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            activationSubscriptions.Own(keyDownHandler);

            IDisposable keyUpHandler = InputElement.KeyUpEvent.AddClassHandler<TopLevel>(
                OnActivationKeyUp,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            activationSubscriptions.Own(keyUpHandler);

            _lifetime = lifetime;
            _activationSubscriptions = activationSubscriptions;
            lifetime.Exit += OnLifetimeExit;
        }
        catch (Exception exception)
        {
            activationSubscriptions.Dispose();
            TADNLog.Log(
                $"Control hover inspector startup failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void OnActivationKeyDown(TopLevel topLevel, KeyEventArgs eventArgs)
    {
        if (!ControlHoverInspectorShortcut.IsActivationToggle(eventArgs.Key, eventArgs.KeyModifiers)) return;

        eventArgs.Handled = true;
        if (_activationShortcutPressed) return;

        _activationShortcutPressed = true;
        ControlHoverInspectorSession? session = _session;
        if (session != null)
        {
            _session = null;
            session.Dispose();
            return;
        }

        try
        {
            _session = new ControlHoverInspectorSession();
        }
        catch (Exception exception)
        {
            TADNLog.Log(
                $"Control hover inspector activation failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void OnActivationKeyUp(TopLevel topLevel, KeyEventArgs eventArgs)
    {
        if (!_activationShortcutPressed || eventArgs.Key != Key.D) return;

        _activationShortcutPressed = false;
        eventArgs.Handled = true;
    }

    private static void OnLifetimeExit(object? sender, ControlledApplicationLifetimeExitEventArgs eventArgs)
    {
        IClassicDesktopStyleApplicationLifetime? lifetime = _lifetime;
        _lifetime = null;
        if (lifetime != null)
            lifetime.Exit -= OnLifetimeExit;

        UIResourceScope? activationSubscriptions = _activationSubscriptions;
        _activationSubscriptions = null;
        activationSubscriptions?.Dispose();
        _activationShortcutPressed = false;

        ControlHoverInspectorSession? session = _session;
        _session = null;
        session?.Dispose();
    }
}

internal sealed class ControlHoverInspectorSession : IDisposable
{
    private readonly UIResourceScope _subscriptions = new(
        nameof(ControlHoverInspectorSession),
        exception => TADNLog.Log(
            $"Control hover inspector cleanup failed: {exception.GetType().Name}: {exception.Message}"));

    private readonly ControlHoverInspectorCaptureQueue _captureQueue;

    private ControlHoverInspectorWindow? _inspectorWindow;
    private TopLevel? _lastTopLevel;
    private IInputElement? _lastHitElement;
    private CancellationTokenSource? _snapshotCancellation;
    private long _snapshotGeneration;
    private bool _freezeShortcutPressed;
    private bool _isFrozen;
    private bool _disposed;

    internal bool IsInspectorVisible => _inspectorWindow?.IsVisible == true;

    internal bool IsFrozen => _isFrozen;

    internal string? InspectorStatusText => _inspectorWindow?.StatusText;

    public ControlHoverInspectorSession()
    {
        _captureQueue = new ControlHoverInspectorCaptureQueue(
            static callback =>
            {
                Dispatcher.UIThread.Post(callback, DispatcherPriority.Background);
            },
            CaptureSnapshot);

        try
        {
            IDisposable pointerEnteredHandler = InputElement.PointerEnteredEvent.AddClassHandler<TopLevel>(
                OnPointerEnteredOrMoved,
                RoutingStrategies.Direct,
                handledEventsToo: true);
            _subscriptions.Own(pointerEnteredHandler);

            IDisposable pointerMovedHandler = InputElement.PointerMovedEvent.AddClassHandler<TopLevel>(
                OnPointerEnteredOrMoved,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _subscriptions.Own(pointerMovedHandler);

            IDisposable pointerExitedHandler = InputElement.PointerExitedEvent.AddClassHandler<TopLevel>(
                OnPointerExited,
                RoutingStrategies.Direct,
                handledEventsToo: true);
            _subscriptions.Own(pointerExitedHandler);

            IDisposable keyDownHandler = InputElement.KeyDownEvent.AddClassHandler<TopLevel>(
                OnKeyDown,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _subscriptions.Own(keyDownHandler);

            IDisposable keyUpHandler = InputElement.KeyUpEvent.AddClassHandler<TopLevel>(
                OnKeyUp,
                RoutingStrategies.Tunnel,
                handledEventsToo: true);
            _subscriptions.Own(keyUpHandler);

            ShowInspector();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _subscriptions.Dispose();
        _captureQueue.CancelPending();
        CancelSnapshotProcessing();
        _lastTopLevel = null;
        _lastHitElement = null;
        _freezeShortcutPressed = false;

        ControlHoverInspectorWindow? inspectorWindow = _inspectorWindow;
        _inspectorWindow = null;
        if (inspectorWindow == null) return;

        inspectorWindow.Closed -= OnInspectorWindowClosed;
        try
        {
            inspectorWindow.Close();
        }
        catch (Exception exception)
        {
            TADNLog.Log(
                $"Control hover inspector close failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void ShowInspector()
    {
        ControlHoverInspectorWindow inspectorWindow = EnsureInspectorWindow();
        if (!inspectorWindow.IsVisible)
            inspectorWindow.Show();
    }

    private ControlHoverInspectorWindow EnsureInspectorWindow()
    {
        ControlHoverInspectorWindow? inspectorWindow = _inspectorWindow;
        if (inspectorWindow != null) return inspectorWindow;

        inspectorWindow = new ControlHoverInspectorWindow();
        inspectorWindow.SetFrozen(_isFrozen);
        inspectorWindow.Closed += OnInspectorWindowClosed;
        _inspectorWindow = inspectorWindow;
        return inspectorWindow;
    }

    private void OnInspectorWindowClosed(object? sender, EventArgs eventArgs)
    {
        if (!ReferenceEquals(sender, _inspectorWindow)) return;

        _inspectorWindow!.Closed -= OnInspectorWindowClosed;
        _inspectorWindow = null;
        _captureQueue.CancelPending();
        CancelSnapshotProcessing();
        _lastTopLevel = null;
        _lastHitElement = null;
    }

    private void OnPointerEnteredOrMoved(TopLevel topLevel, PointerEventArgs eventArgs)
    {
        if (_disposed || _isFrozen || topLevel is ControlHoverInspectorWindow) return;

        Point pointerPosition = eventArgs.GetPosition(topLevel);
        IInputElement? hitElement = topLevel.InputHitTest(pointerPosition, enabledElementsOnly: false);
        if (hitElement == null)
        {
            ReportNoControl();
            return;
        }

        if (!_captureQueue.HasPendingCapture
            && ReferenceEquals(topLevel, _lastTopLevel)
            && ReferenceEquals(hitElement, _lastHitElement))
            return;

        _captureQueue.Enqueue(topLevel, hitElement);
    }

    private void OnPointerExited(TopLevel topLevel, PointerEventArgs eventArgs)
    {
        if (_disposed || _isFrozen || topLevel is ControlHoverInspectorWindow) return;

        ReportNoControl();
    }

    private void ReportNoControl()
    {
        bool hadCapture = _captureQueue.HasPendingCapture || _lastTopLevel != null || _lastHitElement != null;
        _captureQueue.CancelPending();
        CancelSnapshotProcessing();
        if (!hadCapture) return;

        _lastTopLevel = null;
        _lastHitElement = null;
        _inspectorWindow?.ShowNoControl();
    }

    private void OnKeyDown(TopLevel topLevel, KeyEventArgs eventArgs)
    {
        if (_disposed || !ControlHoverInspectorShortcut.IsFreezeToggle(eventArgs.Key, eventArgs.KeyModifiers))
            return;

        eventArgs.Handled = true;
        if (_freezeShortcutPressed) return;

        _freezeShortcutPressed = true;
        ToggleFrozen();
    }

    private void OnKeyUp(TopLevel topLevel, KeyEventArgs eventArgs)
    {
        if (_disposed || eventArgs.Key != Key.Q || !_freezeShortcutPressed) return;

        _freezeShortcutPressed = false;
        eventArgs.Handled = true;
    }

    internal void ToggleFrozen()
    {
        if (_disposed) return;

        _isFrozen = !_isFrozen;
        _captureQueue.CancelPending();
        if (!_isFrozen)
        {
            CancelSnapshotProcessing();
            _lastTopLevel = null;
            _lastHitElement = null;
        }

        ShowInspector();
        _inspectorWindow?.SetFrozen(_isFrozen);
    }

    private void CaptureSnapshot(TopLevel topLevel, IInputElement hitElement)
    {
        if (_disposed || _isFrozen || topLevel is ControlHoverInspectorWindow) return;
        if (ReferenceEquals(topLevel, _lastTopLevel) && ReferenceEquals(hitElement, _lastHitElement)) return;

        if (hitElement is Visual hitVisual
            && !ReferenceEquals(TopLevel.GetTopLevel(hitVisual), topLevel))
        {
            ReportNoControl();
            return;
        }

        try
        {
            ControlHoverInspectorCapture capture = ControlHoverInspectorSnapshotBuilder.Capture(topLevel, hitElement);
            (long generation, CancellationToken cancellationToken) = BeginSnapshotProcessing();
            _lastTopLevel = topLevel;
            _lastHitElement = hitElement;

            ShowInspector();
            _inspectorWindow?.ShowPendingCapture(capture);
            _ = ProcessSnapshotAsync(capture, generation, cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            CancelSnapshotProcessing();
            TADNLog.Log(
                $"Control hover inspector capture failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private (long Generation, CancellationToken CancellationToken) BeginSnapshotProcessing()
    {
        CancelSnapshotProcessing();
        CancellationTokenSource cancellation = new();
        _snapshotCancellation = cancellation;
        return (_snapshotGeneration, cancellation.Token);
    }

    private void CancelSnapshotProcessing()
    {
        _snapshotGeneration++;
        CancellationTokenSource? cancellation = _snapshotCancellation;
        _snapshotCancellation = null;
        if (cancellation == null) return;

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private async Task ProcessSnapshotAsync(
        ControlHoverInspectorCapture capture,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            ControlHoverInspectorSnapshot snapshot = await Task.Run(
                    () => ControlHoverInspectorSnapshotBuilder.Build(capture, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Dispatcher.UIThread.Post(
                () => ApplySnapshot(snapshot, generation),
                DispatcherPriority.Background);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Dispatcher.UIThread.Post(
                () => ReportSnapshotFailure(generation, exception),
                DispatcherPriority.Background);
        }
    }

    private void ApplySnapshot(ControlHoverInspectorSnapshot snapshot, long generation)
    {
        if (_disposed || generation != _snapshotGeneration) return;

        CompleteSnapshotProcessing();
        ShowInspector();
        _inspectorWindow?.ShowSnapshot(snapshot);
    }

    private void ReportSnapshotFailure(long generation, Exception exception)
    {
        if (_disposed || generation != _snapshotGeneration) return;

        CompleteSnapshotProcessing();
        _lastTopLevel = null;
        _lastHitElement = null;
        TADNLog.Log(
            $"Control hover inspector processing failed: {exception.GetType().Name}: {exception.Message}");
    }

    private void CompleteSnapshotProcessing()
    {
        CancellationTokenSource? cancellation = _snapshotCancellation;
        _snapshotCancellation = null;
        cancellation?.Dispose();
    }
}

internal static class ControlHoverInspectorShortcut
{
    public const string Hint = "Ctrl+Alt+Q: Freeze / Unfreeze | Ctrl+Alt+D: Disable";

    private static readonly KeyModifiers RequiredModifiers = KeyModifiers.Control | KeyModifiers.Alt;

    public static bool IsActivationToggle(Key key, KeyModifiers modifiers) =>
        key == Key.D && modifiers == RequiredModifiers;

    public static bool IsFreezeToggle(Key key, KeyModifiers modifiers) =>
        key == Key.Q && modifiers == RequiredModifiers;
}
#endif
