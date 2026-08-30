namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Calculates the window offset that keeps a restored drag anchor outside the search box.</summary>
internal static class RestoredWindowDragGeometry
{
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
            ? Math.Max(0, pageContentLeft)
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
        {
            return 0;
        }

        int distanceToLeft = cursorWithinWindow - searchLeftWithinWindow;
        int distanceToRight = searchRightWithinWindow - cursorWithinWindow;
        int targetCursorWithinWindow = distanceToLeft <= distanceToRight
            ? searchLeftWithinWindow - outsideMarginPixels
            : searchRightWithinWindow + outsideMarginPixels;
        return cursorWithinWindow - targetCursorWithinWindow;
    }
}
