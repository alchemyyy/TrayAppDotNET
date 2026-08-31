namespace TaskManagerTrayAppDotNET.UI;

internal readonly record struct RestoredWindowDragSearchRange(int Left, int Right);

/// <summary>Calculates the window offset that keeps a restored drag anchor outside the search controls.</summary>
internal static class RestoredWindowDragGeometry
{
    /// <summary>Resolves the search box plus its visible leading action without shifting the search box itself.</summary>
    public static RestoredWindowDragSearchRange CalculateSearchRangeWithinWindow(
        int proposedWindowWidth,
        int searchWidth,
        int leadingActionWidth,
        bool alignToPageArea,
        int pageContentLeft)
    {
        if (leadingActionWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(leadingActionWidth));

        int left = CalculateSearchLeftWithinWindow(
            proposedWindowWidth,
            searchWidth,
            alignToPageArea,
            pageContentLeft);
        return new RestoredWindowDragSearchRange(
            Math.Max(val1: 0, left - leadingActionWidth),
            checked(left + searchWidth));
    }

    /// <summary>Resolves the search box's left edge for centered or page-aligned layout.</summary>
    public static int CalculateSearchLeftWithinWindow(
        int proposedWindowWidth,
        int searchWidth,
        bool alignToPageArea,
        int pageContentLeft)
    {
        if (proposedWindowWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(proposedWindowWidth));
        if (searchWidth <= 0 || searchWidth >= proposedWindowWidth)
            throw new ArgumentOutOfRangeException(nameof(searchWidth));

        return alignToPageArea
            ? Math.Max(val1: 0, pageContentLeft)
            : (proposedWindowWidth - searchWidth) / 2;
    }

    /// <summary>Returns the horizontal offset to apply to a proposed native window rectangle.</summary>
    public static int CalculateHorizontalWindowOffset(
        int cursorScreenX,
        int proposedWindowLeft,
        int searchLeftWithinWindow,
        int searchRightWithinWindow,
        int outsideMarginPixels)
    {
        if (searchRightWithinWindow <= searchLeftWithinWindow || outsideMarginPixels <= 0)
            return 0;

        int cursorWithinWindow = cursorScreenX - proposedWindowLeft;
        if (cursorWithinWindow < searchLeftWithinWindow
            || cursorWithinWindow >= searchRightWithinWindow)
            return 0;

        int distanceToLeft = cursorWithinWindow - searchLeftWithinWindow;
        int distanceToRight = searchRightWithinWindow - cursorWithinWindow;
        int targetCursorWithinWindow = distanceToLeft <= distanceToRight
            ? searchLeftWithinWindow - outsideMarginPixels
            : searchRightWithinWindow + outsideMarginPixels;
        return cursorWithinWindow - targetCursorWithinWindow;
    }
}
