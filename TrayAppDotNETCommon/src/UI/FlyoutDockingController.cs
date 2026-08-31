using Avalonia;

namespace TrayAppDotNETCommon.UI;

/// <summary>Exposes the persisted settings required by a dockable tray flyout.</summary>
public interface IFlyoutDockSettings
{
    bool AllowFlyoutUndock { get; }
    bool RestoreFlyoutUndockedOnStartup { get; }
    bool ClampUndockedFlyoutToScreen { get; }
    bool FlyoutUndocked { get; set; }
    bool FlyoutHasSavedPosition { get; set; }
    double FlyoutLeft { get; set; }
    double FlyoutTop { get; set; }

    void Save();
}

/// <summary>Identifies a persisted flyout docking transition.</summary>
public enum FlyoutDockStateChange
{
    Undocked,
    UndockedFromDrag,
    Redocked,
    PositionSaved
}

public sealed class FlyoutDockingOptions
{
    public required IFlyoutDockSettings Settings { get; init; }
    public required FlyoutWindowDragHelper DragHelper { get; init; }
    public required Func<PixelPoint> CurrentPosition { get; init; }
    public required Action<PixelPoint> SetPosition { get; init; }
    public required Func<PixelPoint> ResolveDockedPosition { get; init; }
    public required Func<PixelPoint, PixelPoint> ResolveSavedPosition { get; init; }
    public required Func<int> ResolveSnapTolerance { get; init; }
    public Action<FlyoutDockStateChange>? StateChanged { get; init; }
}

/// <summary>
/// Owns dock state, position persistence, and snap-to-redock behavior for a tray flyout.
/// </summary>
public sealed class FlyoutDockingController
{
    private readonly IFlyoutDockSettings _settings;
    private readonly Func<PixelPoint> _currentPosition;
    private readonly Action<PixelPoint> _setPosition;
    private readonly Func<PixelPoint> _resolveDockedPosition;
    private readonly Func<PixelPoint, PixelPoint> _resolveSavedPosition;
    private readonly Func<int> _resolveSnapTolerance;
    private readonly Action<FlyoutDockStateChange>? _stateChanged;

    public FlyoutDockingController(FlyoutDockingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _settings = options.Settings ?? throw new ArgumentNullException(nameof(options.Settings));
        DragHelper = options.DragHelper ?? throw new ArgumentNullException(nameof(options.DragHelper));
        _currentPosition = options.CurrentPosition ?? throw new ArgumentNullException(nameof(options.CurrentPosition));
        _setPosition = options.SetPosition ?? throw new ArgumentNullException(nameof(options.SetPosition));
        _resolveDockedPosition = options.ResolveDockedPosition
                                 ?? throw new ArgumentNullException(nameof(options.ResolveDockedPosition));
        _resolveSavedPosition = options.ResolveSavedPosition
                                ?? throw new ArgumentNullException(nameof(options.ResolveSavedPosition));
        _resolveSnapTolerance = options.ResolveSnapTolerance
                                ?? throw new ArgumentNullException(nameof(options.ResolveSnapTolerance));
        _stateChanged = options.StateChanged;
        IsUndocked = ShouldRestoreOnStartup(_settings);
    }

    public FlyoutWindowDragHelper DragHelper { get; }

    public bool IsUndocked { get; private set; }

    /// <summary>Returns whether persisted settings request a visible undocked flyout at startup.</summary>
    public static bool ShouldRestoreOnStartup(IFlyoutDockSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings is
        {
            AllowFlyoutUndock: true, RestoreFlyoutUndockedOnStartup: true, FlyoutUndocked: true,
            FlyoutHasSavedPosition: true
        };
    }

    /// <summary>Returns the current saved or docked position according to the active state.</summary>
    public PixelPoint ResolvePosition()
    {
        if (!IsUndocked || !_settings.FlyoutHasSavedPosition)
            return _resolveDockedPosition();

        PixelPoint savedPosition = new(
            (int)Math.Round(_settings.FlyoutLeft),
            (int)Math.Round(_settings.FlyoutTop));
        return _settings.ClampUndockedFlyoutToScreen
            ? _resolveSavedPosition(savedPosition)
            : savedPosition;
    }

    /// <summary>Captures the dock target and snap tolerance at the start of a drag.</summary>
    public (PixelPoint DockedPosition, int SnapTolerance) CaptureDockedPosition() =>
        (_resolveDockedPosition(), Math.Max(val1: 0, _resolveSnapTolerance()));

    /// <summary>Toggles between docked and undocked state.</summary>
    public bool ToggleUndocked() =>
        IsUndocked ? Redock() : UndockToSavedPosition();

    /// <summary>Persists docked state and notifies the owner.</summary>
    public bool Redock()
    {
        if (!IsUndocked) return false;

        IsUndocked = false;
        _settings.FlyoutUndocked = false;
        _settings.Save();
        _stateChanged?.Invoke(FlyoutDockStateChange.Redocked);
        return true;
    }

    /// <summary>Undocks and restores the saved floating position when one exists.</summary>
    public bool UndockToSavedPosition()
    {
        if (IsUndocked || !_settings.AllowFlyoutUndock) return false;

        IsUndocked = true;
        _settings.FlyoutUndocked = true;
        _settings.Save();
        if (_settings.FlyoutHasSavedPosition)
        {
            PixelPoint savedPosition = new(
                (int)Math.Round(_settings.FlyoutLeft),
                (int)Math.Round(_settings.FlyoutTop));
            _setPosition(_settings.ClampUndockedFlyoutToScreen
                ? _resolveSavedPosition(savedPosition)
                : savedPosition);
        }

        _stateChanged?.Invoke(FlyoutDockStateChange.Undocked);
        return true;
    }

    /// <summary>Transitions to undocked state after the drag threshold is crossed.</summary>
    public bool SetUndockedFromDrag()
    {
        if (IsUndocked || !_settings.AllowFlyoutUndock) return false;

        IsUndocked = true;
        _stateChanged?.Invoke(FlyoutDockStateChange.UndockedFromDrag);
        return true;
    }

    /// <summary>Redocks a snapped drag or persists its floating position.</summary>
    public FlyoutDockStateChange? CommitDragPosition()
    {
        if (!_settings.AllowFlyoutUndock || DragHelper.IsCurrentlySnapped)
        {
            bool redocked = Redock();
            return redocked ? FlyoutDockStateChange.Redocked : null;
        }

        if (!IsUndocked) return null;

        PixelPoint position = _currentPosition();
        _settings.FlyoutUndocked = true;
        _settings.FlyoutHasSavedPosition = true;
        _settings.FlyoutLeft = position.X;
        _settings.FlyoutTop = position.Y;
        _settings.Save();
        _stateChanged?.Invoke(FlyoutDockStateChange.PositionSaved);
        return FlyoutDockStateChange.PositionSaved;
    }

    /// <summary>Redocks an undocked flyout after undocking is disabled.</summary>
    public bool RedockIfUndockingDisabled() =>
        !_settings.AllowFlyoutUndock && Redock();
}
