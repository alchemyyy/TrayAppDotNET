namespace TrayAppDotNETCommon.UI.Tray;

/// <summary>An app's tray icon drawing: a value-equal render input from its state, and the render itself.</summary>
public interface ITrayIconRenderer<TInput> : IDisposable
    where TInput : notnull
{
    /// <summary>Builds the render input of the current state on the UI thread. Equal state gives an equal input.</summary>
    TInput CreateRenderInput();

    /// <summary>Renders <paramref name="input"/> at <paramref name="iconSize"/> pixels on the render thread.</summary>
    NativeIcon RenderIcon(TInput input, int iconSize);
}

public sealed class TrayIconPresenterOptions
{
    /// <summary>
    /// Skips a render whose input and icon size equal the last requested ones. Off for icons that change with every
    /// refresh, such as Task Manager's live graph.
    /// </summary>
    public bool Dedupe { get; init; } = true;

    /// <summary>The taskbar's small icon size; part of the dedupe key, so a scale change re-renders.</summary>
    public Func<int> IconSize { get; init; } = TrayAppDotNETTrayIconMetrics.GetTaskbarSmallIconSize;

    public Action<string> Log { get; init; } = TADNLog.Log;
}

/// <summary>
/// Owns what an app shows in the notification area: the tooltip text, the render requests for its icon, and the
/// fallback app icon. Tooltip text is set only here, at once; a finished render replaces only the icon, so it never
/// brings back the tooltip of the state it was rendered for. Renders run off the UI thread with the latest request
/// winning, and an input equal to the last requested one, at the same icon size, is not rendered again.
/// </summary>
/// <remarks>
/// The presenter owns <paramref name="renderer"/> and disposes it; the tray icon stays the caller's. A display or DPI
/// change raises <see cref="TrayAppDotNETShellTrayIcon.RefreshNeeded"/>, and the refresh that follows renders again
/// only when the taskbar icon size changed.
/// </remarks>
public sealed class TrayIconPresenter<TInput>(
    TrayAppDotNETShellTrayIcon trayIcon,
    ITrayIconRenderer<TInput>? renderer,
    Func<NativeIcon?> fallbackIcon,
    TrayIconPresenterOptions? options = null) : IDisposable
    where TInput : notnull
{
    private readonly TrayIconPresenterOptions _options = options ?? new TrayIconPresenterOptions();
    private readonly TrayIconRenderQueue _renderQueue = new(options?.Log ?? TADNLog.Log);
    private RenderRequest? _lastRequested;
    private bool _fallbackApplied;
    private bool _iconApplied;
    private bool _disposed;

    /// <summary>The renderer, or null when it could not be created and the app icon stands in.</summary>
    public ITrayIconRenderer<TInput>? Renderer => renderer;

    /// <summary>Sets the tooltip and requests the icon for the renderer's current state.</summary>
    public void Refresh(string tooltip)
    {
        if (_disposed) return;

        trayIcon.SetTooltip(tooltip);
        if (renderer == null)
        {
            ApplyFallbackOnce();
            return;
        }

        RenderRequest request = new(renderer.CreateRenderInput(), _options.IconSize());
        if (_options.Dedupe && _lastRequested is { } last && last.Equals(request)) return;

        _lastRequested = request;

        // The shell adds the icon only once it has one, so the app icon stands in until the first render lands
        if (!_iconApplied) ApplyFallbackOnce();

        RenderAttempt attempt = new(request);
        _renderQueue.Request(() => Render(renderer, attempt), icon => Apply(icon, attempt));
    }

    /// <summary>Makes the next refresh render even when its input equals the last one.</summary>
    public void Invalidate() => _lastRequested = null;

    private NativeIcon? Render(ITrayIconRenderer<TInput> iconRenderer, RenderAttempt attempt)
    {
        try
        {
            return iconRenderer.RenderIcon(attempt.Request.Input, attempt.Request.IconSize);
        }
        catch (Exception ex)
        {
            _options.Log($"TrayIconPresenter.Render: {ex.Message}");
            attempt.UsedFallback = true;
            return fallbackIcon();
        }
    }

    private void Apply(NativeIcon icon, RenderAttempt attempt)
    {
        if (_disposed)
        {
            icon.Dispose();
            return;
        }

        trayIcon.SetOwnedIcon(icon);
        _iconApplied = true;
        _fallbackApplied = attempt.UsedFallback;

        // A failed render must not satisfy the dedupe, or the same state would keep the fallback icon
        if (attempt.UsedFallback && _lastRequested is { } last && last.Equals(attempt.Request))
            _lastRequested = null;
    }

    private void ApplyFallbackOnce()
    {
        if (_fallbackApplied) return;

        NativeIcon? icon = fallbackIcon();
        if (icon == null) return;

        trayIcon.SetOwnedIcon(icon);
        _iconApplied = true;
        _fallbackApplied = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _renderQueue.Dispose();
        renderer?.Dispose();
    }

    private readonly record struct RenderRequest(TInput Input, int IconSize);

    // Written on the render thread before the queue posts the result to the UI thread, which reads it
    private sealed class RenderAttempt(RenderRequest request)
    {
        public RenderRequest Request { get; } = request;
        public bool UsedFallback { get; set; }
    }
}
