using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace TrayAppDotNETCommon.UI.Controls;

/// <summary>
/// Shared pointer/pressed-state behavior for flyout buttons whose content may update in place.
/// </summary>
public sealed class FlyoutButtonState
{
    private readonly Border _button;
    private readonly Func<IBrush> _normalBrush;
    private readonly Func<IBrush> _hoverBrush;
    private readonly Func<IBrush> _pressedBrush;
    private readonly Action<PointerReleasedEventArgs> _click;
    private readonly Action<PointerReleasedEventArgs>? _rightClick;
    private bool _isEnabled;
    private bool _isPointerOver;
    private bool _isPressed;

    private FlyoutButtonState(
        Border button,
        Func<IBrush> normalBrush,
        Func<IBrush> hoverBrush,
        Func<IBrush> pressedBrush,
        Action<PointerReleasedEventArgs> click,
        bool enabled,
        Action<PointerReleasedEventArgs>? rightClick)
    {
        _button = button;
        _normalBrush = normalBrush;
        _hoverBrush = hoverBrush;
        _pressedBrush = pressedBrush;
        _click = click;
        _rightClick = rightClick;
        _isEnabled = enabled;

        _button.IsEnabled = enabled;
        _button.Cursor = enabled ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow);

        _button.PointerEntered += OnPointerEntered;
        _button.PointerExited += OnPointerExited;
        _button.PointerPressed += OnPointerPressed;
        _button.PointerReleased += OnPointerReleased;
        _button.PointerCaptureLost += OnPointerCaptureLost;

        Refresh();
    }

    public bool IsPointerOver => _isPointerOver;

    public bool IsPressed => _isPressed;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            if (!_isEnabled) _isPressed = false;
            _button.IsEnabled = value;
            _button.Cursor = value ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow);
            Refresh();
        }
    }

    public static FlyoutButtonState Attach(
        Border button,
        Func<IBrush> normalBrush,
        Func<IBrush> hoverBrush,
        Func<IBrush> pressedBrush,
        Action<PointerReleasedEventArgs> click,
        bool enabled = true,
        Action<PointerReleasedEventArgs>? rightClick = null) =>
        new(button, normalBrush, hoverBrush, pressedBrush, click, enabled, rightClick);

    public void Refresh()
    {
        _button.Background = !_isEnabled
            ? _normalBrush()
            : _isPressed
                ? _pressedBrush()
                : _isPointerOver
                    ? _hoverBrush()
                    : _normalBrush();
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        _isPointerOver = true;
        Refresh();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _isPointerOver = false;
        _isPressed = false;
        Refresh();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_isEnabled || e.GetCurrentPoint(_button).Properties.PointerUpdateKind !=
            PointerUpdateKind.LeftButtonPressed) return;

        _isPressed = true;
        Refresh();
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isEnabled) return;

        if (e.InitialPressMouseButton == MouseButton.Right && _rightClick != null)
        {
            _rightClick(e);
            e.Handled = true;
            return;
        }

        if (e.InitialPressMouseButton != MouseButton.Left) return;

        bool releasedInside = TrayAppDotNETFlyoutUI.IsPointerInside(_button, e);
        _isPressed = false;
        _isPointerOver = releasedInside;
        Refresh();
        if (releasedInside) _click(e);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isPressed = false;
        Refresh();
    }
}
