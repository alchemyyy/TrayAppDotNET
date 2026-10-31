using Avalonia;
using Avalonia.Threading;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.UI.WarmWindows;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Tray;

/// <summary>
/// Owns an app's tray menu: the warm slot that keeps it ready, showing it, and rebuilding it when what it shows went
/// stale. A warm menu captures its colors, corners, font size and app state when it is built, so every settings
/// change, every theme change and every app signal passed to <see cref="Invalidate"/> drops it. A hidden menu is
/// dropped at once; an open menu stays open and is dropped when it is dismissed.
/// </summary>
/// <remarks>Create, use and dispose it on the UI thread.</remarks>
public sealed class TrayMenuController : IDisposable
{
    private readonly Func<TrayMenuWindow> _createMenu;
    private readonly Func<bool> _isKeepWarmEnabled;
    private readonly AppSettingsCommon? _settings;
    private readonly AppTheme? _theme;
    private readonly TrayAppDotNETWarmWindowSlot<TrayMenuWindow> _slot;
    private bool _invalidateOnDismiss;
    private bool _reprimeQueued;
    private bool _disposed;

    /// <param name="createMenu">Builds a menu from the current settings, theme and app state.</param>
    /// <param name="isKeepWarmEnabled">Whether a dismissed menu is kept ready for the next right click.</param>
    /// <param name="settings">Raises <see cref="AppSettingsCommon.Changed"/> for every setting the menu reads.</param>
    /// <param name="theme">Raises <see cref="AppTheme.ThemeChanged"/> when Windows switches light and dark.</param>
    /// <param name="log">Logs warm-slot failures.</param>
    public TrayMenuController(
        Func<TrayMenuWindow> createMenu,
        Func<bool> isKeepWarmEnabled,
        AppSettingsCommon? settings,
        AppTheme? theme,
        Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(createMenu);
        ArgumentNullException.ThrowIfNull(isKeepWarmEnabled);
        ArgumentNullException.ThrowIfNull(log);

        _createMenu = CreateTrackedMenu;
        _isKeepWarmEnabled = isKeepWarmEnabled;
        _settings = settings;
        _theme = theme;
        _slot = new TrayAppDotNETWarmWindowSlot<TrayMenuWindow>(
            isKeepWarmEnabled,
            ex => log($"Tray menu keep-warm: {ex.Message}"));
        if (_settings != null) _settings.Changed += OnSettingsOrThemeChanged;
        if (_theme != null) _theme.ThemeChanged += OnThemeChanged;
        return;

        TrayMenuWindow CreateTrackedMenu()
        {
            TrayMenuWindow menu = createMenu();
            menu.WarmDismissed += OnMenuDismissed;
            return menu;
        }
    }

    /// <summary>The menu that is open or kept warm, if any.</summary>
    public TrayMenuWindow? Current => _slot.Cached;

    public void Show(TrayAppDotNETShellTrayIcon trayIcon, PixelPoint cursorPoint, TrayAppDotNETContextMenuPosition placement)
    {
        if (_disposed) return;

        TrayMenuWindow? current = _slot.Cached;
        if (current is { IsVisible: true }) current.DismissForWarmCache();

        // An invalidation that waited for the open menu applies before it would be shown again
        if (_invalidateOnDismiss)
        {
            _invalidateOnDismiss = false;
            _slot.EvictNow();
        }

        _slot.TakeOrCreate(_createMenu).ShowAt(trayIcon, cursorPoint, placement);
    }

    /// <summary>Builds the menu ahead of the first right click when keeping it warm is enabled.</summary>
    public async Task PrimeAsync()
    {
        if (_disposed || !_isKeepWarmEnabled()) return;
        await _slot.PrimeAsync(_createMenu);
    }

    /// <summary>Primes or schedules eviction after the keep-warm setting changed.</summary>
    public void ApplyKeepWarmPolicy()
    {
        if (_disposed) return;
        if (_slot.Cached == null && !_isKeepWarmEnabled()) return;

        _slot.ApplyKeepWarmPolicy(_createMenu);
    }

    /// <summary>
    /// Drops the menu because what it shows changed. A hidden menu goes at once and a warm one is built again at idle;
    /// an open menu stays usable and goes when it is dismissed.
    /// </summary>
    public void Invalidate()
    {
        if (_disposed) return;

        TrayMenuWindow? current = _slot.Cached;
        if (current == null) return;

        if (current.IsVisible)
        {
            _invalidateOnDismiss = true;
            return;
        }

        _invalidateOnDismiss = false;
        _slot.EvictNow();
        QueueReprime();
    }

    private void OnMenuDismissed(object? sender, EventArgs e)
    {
        if (!_invalidateOnDismiss || !ReferenceEquals(sender, _slot.Cached)) return;

        // The menu raises this from inside its own dismissal, so it is closed on the next turn
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !_invalidateOnDismiss) return;
            if (_slot.Cached is { IsVisible: true }) return;

            _invalidateOnDismiss = false;
            _slot.EvictNow();
            QueueReprime();
        });
    }

    private void QueueReprime()
    {
        if (_reprimeQueued || !_isKeepWarmEnabled()) return;

        _reprimeQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _reprimeQueued = false;
            ApplyKeepWarmPolicy();
        }, DispatcherPriority.ApplicationIdle);
    }

    private void OnSettingsOrThemeChanged() => Dispatcher.UIThread.Post(Invalidate);

    private void OnThemeChanged(bool isLightTheme) => OnSettingsOrThemeChanged();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_settings != null) _settings.Changed -= OnSettingsOrThemeChanged;
        if (_theme != null) _theme.ThemeChanged -= OnThemeChanged;
        _slot.Dispose();
    }
}
