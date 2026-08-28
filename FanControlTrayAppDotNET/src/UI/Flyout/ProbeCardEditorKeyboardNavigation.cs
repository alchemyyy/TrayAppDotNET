namespace FanControlTrayAppDotNET.UI.Flyout;

/// <summary>
/// Direction used by Probe Card Editor spatial keyboard navigation.
/// </summary>
internal enum ProbeCardEditorNavigationDirection
{
    Left,
    Right,
    Up,
    Down
}

/// <summary>
/// Center point of one keyboard navigation target.
/// </summary>
internal readonly record struct ProbeCardEditorNavigationPoint(double X, double Y);

/// <summary>
/// Pure navigation calculations used by the Probe Card Editor.
/// </summary>
internal static class ProbeCardEditorKeyboardNavigation
{
    private const double DirectionEpsilon = 0.5;
    private const double PerpendicularDistanceWeight = 4.0;

    /// <summary>
    /// Finds the nearest target in the requested spatial direction.
    /// </summary>
    public static int FindDirectionalTarget(
        IReadOnlyList<ProbeCardEditorNavigationPoint> points,
        int currentIndex,
        ProbeCardEditorNavigationDirection direction)
    {
        if (currentIndex < 0 || currentIndex >= points.Count) return -1;

        ProbeCardEditorNavigationPoint current = points[currentIndex];
        int bestIndex = -1;
        double bestScore = double.MaxValue;
        for (int targetIndex = 0; targetIndex < points.Count; targetIndex++)
        {
            if (targetIndex == currentIndex) continue;

            ProbeCardEditorNavigationPoint candidate = points[targetIndex];
            double horizontalDistance = candidate.X - current.X;
            double verticalDistance = candidate.Y - current.Y;
            double primaryDistance = direction switch
            {
                ProbeCardEditorNavigationDirection.Left => -horizontalDistance,
                ProbeCardEditorNavigationDirection.Right => horizontalDistance,
                ProbeCardEditorNavigationDirection.Up => -verticalDistance,
                ProbeCardEditorNavigationDirection.Down => verticalDistance,
                _ => -1
            };
            if (primaryDistance <= DirectionEpsilon) continue;

            double perpendicularDistance = direction is ProbeCardEditorNavigationDirection.Left
                or ProbeCardEditorNavigationDirection.Right
                ? Math.Abs(verticalDistance)
                : Math.Abs(horizontalDistance);
            double score = primaryDistance + perpendicularDistance * PerpendicularDistanceWeight;
            if (score >= bestScore) continue;

            bestIndex = targetIndex;
            bestScore = score;
        }

        return bestIndex;
    }

    /// <summary>
    /// Wraps an index by the requested offset.
    /// </summary>
    public static int WrapIndex(int currentIndex, int offset, int count)
    {
        if (count <= 0) return -1;
        int normalizedIndex = currentIndex < 0 || currentIndex >= count ? 0 : currentIndex;
        return (normalizedIndex + offset % count + count) % count;
    }
}
