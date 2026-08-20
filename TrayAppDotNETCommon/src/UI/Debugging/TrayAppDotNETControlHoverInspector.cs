#if DEBUG
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TrayAppDotNETCommon.UI.Debugging;

/// <summary>Enables the shared control hover inspector for an opted-in debug process.</summary>
internal static class TrayAppDotNETControlHoverInspector
{
    internal const string EnvironmentVariableName = "TrayAppDotNET_CONTROL_INSPECTOR";

    private static readonly bool IsEnabled =
        ControlHoverInspectorActivation.IsEnabled(Environment.GetEnvironmentVariable(EnvironmentVariableName));

    private static IClassicDesktopStyleApplicationLifetime? _lifetime;
    private static ControlHoverInspectorSession? _session;

    public static void Attach(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        if (!IsEnabled) return;
        if (_session != null) return;

        try
        {
            ControlHoverInspectorSession session = new();
            _lifetime = lifetime;
            _session = session;
            lifetime.Exit += OnLifetimeExit;
        }
        catch (Exception exception)
        {
            TADNLog.Log(
                $"Control hover inspector startup failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void OnLifetimeExit(object? sender, ControlledApplicationLifetimeExitEventArgs eventArgs)
    {
        IClassicDesktopStyleApplicationLifetime? lifetime = _lifetime;
        _lifetime = null;
        if (lifetime != null)
            lifetime.Exit -= OnLifetimeExit;

        ControlHoverInspectorSession? session = _session;
        _session = null;
        session?.Dispose();
    }
}

internal static class ControlHoverInspectorActivation
{
    public static bool IsEnabled(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        return value.Trim().ToUpperInvariant() switch
        {
            "0" or "FALSE" or "NO" or "OFF" => false,
            _ => true
        };
    }
}

internal sealed class ControlHoverInspectorSession : IDisposable
{
    private readonly UIResourceScope _subscriptions = new(
        nameof(ControlHoverInspectorSession),
        exception => TADNLog.Log(
            $"Control hover inspector cleanup failed: {exception.GetType().Name}: {exception.Message}"));

    private ControlHoverInspectorWindow? _inspectorWindow;
    private TopLevel? _lastTopLevel;
    private IInputElement? _lastHitElement;
    private bool _freezeShortcutPressed;
    private bool _isFrozen;
    private bool _disposed;

    internal bool IsInspectorVisible => _inspectorWindow?.IsVisible == true;

    internal bool IsFrozen => _isFrozen;

    internal string? InspectorStatusText => _inspectorWindow?.StatusText;

    public ControlHoverInspectorSession()
    {
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

        if (ReferenceEquals(topLevel, _lastTopLevel) && ReferenceEquals(hitElement, _lastHitElement)) return;

        _lastTopLevel = topLevel;
        _lastHitElement = hitElement;
        ShowInspector();
        _inspectorWindow?.ShowSnapshot(ControlHoverInspectorSnapshotBuilder.Build(topLevel, hitElement));
    }

    private void OnPointerExited(TopLevel topLevel, PointerEventArgs eventArgs)
    {
        if (_disposed || _isFrozen || topLevel is ControlHoverInspectorWindow) return;
        if (!ReferenceEquals(topLevel, _lastTopLevel)) return;

        ReportNoControl();
    }

    private void ReportNoControl()
    {
        if (_lastTopLevel == null && _lastHitElement == null) return;

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
        if (!_isFrozen)
        {
            _lastTopLevel = null;
            _lastHitElement = null;
        }

        ShowInspector();
        _inspectorWindow?.SetFrozen(_isFrozen);
    }
}

internal static class ControlHoverInspectorShortcut
{
    public const string Hint = "Ctrl+Alt+Q: Freeze / Unfreeze";

    private static readonly KeyModifiers FreezeModifiers = KeyModifiers.Control | KeyModifiers.Alt;

    public static bool IsFreezeToggle(Key key, KeyModifiers modifiers) =>
        key == Key.Q && modifiers == FreezeModifiers;
}
#endif
