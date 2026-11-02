using Avalonia;
using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Tray;

namespace TrayAppDotNETCommon.UI;

/// <summary>
/// Places a tray flyout and its companion windows in physical pixels. Every DIP metric comes from
/// <c>FlyoutTrayPlacement.axaml</c> and is scaled with the flyout's render scaling, so margins keep their size on every
/// monitor.
/// </summary>
public sealed class FlyoutTrayPlacement(Window window)
{
    private readonly Window _window = window ?? throw new ArgumentNullException(nameof(window));

    /// <summary>Gets or sets the tray icon the docked flyout centers on.</summary>
    public TrayAppDotNETShellTrayIcon? TrayIcon { get; set; }

    private static FlyoutTrayPlacementResources.FlyoutPlacementAxamlProperties Metrics =>
        FlyoutTrayPlacementResources.Current.AxamlFlyoutPlacement;

    private static PixelRect FallbackWorkArea => new(
        Metrics.FallbackWorkAreaX,
        Metrics.FallbackWorkAreaY,
        Metrics.FallbackWorkAreaWidth,
        Metrics.FallbackWorkAreaHeight);

    private double Scaling => _window.RenderScaling > 0 ? _window.RenderScaling : 1;

    /// <summary>Gets the edge padding in pixels for the flyout's current monitor.</summary>
    public int EdgePaddingPixels => ToPixels(Metrics.EdgePadding, Scaling);

    /// <summary>Gets the work area of the monitor holding the tray icon, or the flyout when no icon is known.</summary>
    public PixelRect ResolveWorkArea()
    {
        PixelPoint anchor = TryGetIconRect(out PixelRect iconRect) ? iconRect.Center : _window.Position;
        return TrayWorkArea.Resolve(_window.Screens, anchor, FallbackWorkArea);
    }

    /// <summary>Gets the flyout's current size in pixels.</summary>
    public PixelSize CurrentPixelSize() => MeasurePixelSize(_window);

    /// <summary>Centers the flyout on its tray icon and clamps it inside that monitor's work area.</summary>
    public PixelPoint ResolveDockedPosition()
    {
        PixelRect? iconRect = TryGetIconRect(out PixelRect resolvedIconRect) ? resolvedIconRect : null;
        PixelPoint anchor = iconRect?.Center ?? _window.Position;
        PixelRect workArea = TrayWorkArea.Resolve(_window.Screens, anchor, FallbackWorkArea);
        return TrayPopupPositioning.ResolveDockedPosition(workArea, CurrentPixelSize(), iconRect, EdgePaddingPixels);
    }

    /// <summary>Clamps a saved undocked position inside the monitor that contains it.</summary>
    public PixelPoint ResolveSavedPosition(PixelPoint savedPosition) =>
        TrayPopupPositioning.ClampToSavedMonitor(
            _window.Screens,
            ResolveWorkArea(),
            CurrentPixelSize(),
            savedPosition,
            EdgePaddingPixels);

    /// <summary>Gets the distance in pixels inside which a dragged flyout snaps back to its docked position.</summary>
    public int ResolveSnapTolerance()
    {
        PixelRect workArea = ResolveWorkArea();
        return Math.Max(
            Metrics.PixelMinSize,
            (int)Math.Round(Math.Min(workArea.Width, workArea.Height) * Metrics.SnapTolerancePercent));
    }

    /// <summary>
    /// Gets the largest logical height that fits the work area with edge padding on both sides, less an extra
    /// reserve, and never below the minimum flyout height.
    /// </summary>
    public double ResolveMaxHeight(double extraReserve = 0) =>
        ResolveMaxHeight(ResolveWorkArea(), Scaling, extraReserve);

    internal static double ResolveMaxHeight(PixelRect workArea, double scaling, double extraReserve) =>
        Math.Max(
            Metrics.WorkAreaMinHeight,
            workArea.Height / scaling - 2 * Metrics.EdgePadding - Math.Max(val1: 0, extraReserve));

    /// <summary>
    /// Stacks companion windows beside the flyout, <paramref name="rowsPerColumn"/> per column, on the left when the
    /// left side has room and on the right otherwise. Every window is then clamped inside the work area.
    /// </summary>
    public void PlaceCompanionsBeside(IReadOnlyList<Window> companions, int rowsPerColumn)
    {
        ArgumentNullException.ThrowIfNull(companions);
        if (companions.Count == 0) return;

        PixelSize[] sizes = new PixelSize[companions.Count];
        for (int index = 0; index < companions.Count; index++)
            sizes[index] = MeasurePixelSize(companions[index]);

        PixelRect flyout = new(_window.Position, CurrentPixelSize());
        PixelPoint[] positions = ResolveCompanionPositions(
            flyout,
            ResolveWorkArea(),
            sizes,
            rowsPerColumn,
            ToPixels(Metrics.CompanionGap, Scaling),
            EdgePaddingPixels);
        for (int index = 0; index < companions.Count; index++)
            companions[index].Position = positions[index];
    }

    /// <summary>Resolves companion positions in pixels; see <see cref="PlaceCompanionsBeside"/>.</summary>
    internal static PixelPoint[] ResolveCompanionPositions(
        PixelRect flyout,
        PixelRect workArea,
        IReadOnlyList<PixelSize> sizes,
        int rowsPerColumn,
        int gap,
        int edgePadding)
    {
        int count = sizes.Count;
        PixelPoint[] positions = new PixelPoint[count];
        if (count == 0) return positions;

        int rows = Math.Max(val1: 1, Math.Min(rowsPerColumn, count));
        int columns = (count + rows - 1) / rows;
        int cellWidth = 1;
        int cellHeight = 1;
        foreach (PixelSize size in sizes)
        {
            cellWidth = Math.Max(cellWidth, size.Width);
            cellHeight = Math.Max(cellHeight, size.Height);
        }

        int stackWidth = columns * (cellWidth + gap);
        int stackHeight = rows * cellHeight + (rows - 1) * gap;
        int leftRoom = flyout.X - (workArea.X + edgePadding);
        int rightRoom = workArea.Right - edgePadding - flyout.Right;
        bool placeLeft = leftRoom >= stackWidth || (rightRoom < stackWidth && leftRoom >= rightRoom);

        // The stack's bottom lines up with the flyout's bottom when the stack is taller than the flyout
        int stackTop = Math.Min(flyout.Y, flyout.Bottom - stackHeight);
        for (int index = 0; index < count; index++)
        {
            int column = index / rows;
            int row = index % rows;
            int left = placeLeft
                ? flyout.X - (column + 1) * (cellWidth + gap)
                : flyout.Right + gap + column * (cellWidth + gap);
            int top = stackTop + row * (cellHeight + gap);
            positions[index] = TrayPopupPositioning.ClampToWorkArea(
                workArea,
                sizes[index],
                new PixelPoint(left, top),
                edgePadding);
        }

        return positions;
    }

    private bool TryGetIconRect(out PixelRect iconRect)
    {
        iconRect = default;
        return TrayIcon?.TryGetIconRect(out iconRect) == true;
    }

    private static PixelSize MeasurePixelSize(Window window)
    {
        double scaling = window.RenderScaling > 0 ? window.RenderScaling : 1;
        double width = Math.Max(window.Bounds.Width, FiniteOrZero(window.Width));
        double height = Math.Max(window.Bounds.Height, Math.Max(FiniteOrZero(window.Height), window.MinHeight));
        int minimum = Metrics.PixelMinSize;
        return new PixelSize(
            Math.Max(minimum, (int)Math.Ceiling(width * scaling)),
            Math.Max(minimum, (int)Math.Ceiling(height * scaling)));
    }

    private static double FiniteOrZero(double value) => double.IsFinite(value) ? value : 0;

    private static int ToPixels(double logical, double scaling) =>
        Math.Max(val1: 0, (int)Math.Round(logical * scaling));
}
