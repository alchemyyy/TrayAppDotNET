using Avalonia;

namespace TrayAppDotNETCommon.UI.ControlMapping;

/// <summary>Direction of an arrow key in spatial navigation.</summary>
public enum SpatialDirection : byte
{
    Left,
    Right,
    Up,
    Down
}

/// <summary>Geometry for Arrows="Spatial" scopes: the nearest target in the pressed direction.</summary>
public static class ControlMapSpatialNavigation
{
    // A candidate must lie at least this far along the direction to count as being in it
    private const double DirectionEpsilon = 0.5;

    // Sideways distance costs this much more than forward distance, so targets in line win over diagonal ones
    private const double PerpendicularDistanceWeight = 4.0;

    /// <summary>
    /// Returns the index of the point nearest to the current one in the given direction, or -1 when none lies that
    /// way. The score is the forward distance plus the weighted sideways distance.
    /// </summary>
    public static int FindDirectionalTarget(IReadOnlyList<Point> centers, int currentIndex, SpatialDirection direction)
    {
        if (currentIndex < 0 || currentIndex >= centers.Count) return -1;

        Point current = centers[currentIndex];
        int bestIndex = -1;
        double bestScore = double.MaxValue;
        for (int targetIndex = 0; targetIndex < centers.Count; targetIndex++)
        {
            if (targetIndex == currentIndex) continue;

            Point candidate = centers[targetIndex];
            double horizontalDistance = candidate.X - current.X;
            double verticalDistance = candidate.Y - current.Y;
            double forwardDistance = direction switch
            {
                SpatialDirection.Left => -horizontalDistance,
                SpatialDirection.Right => horizontalDistance,
                SpatialDirection.Up => -verticalDistance,
                SpatialDirection.Down => verticalDistance,
                _ => -1
            };
            if (forwardDistance <= DirectionEpsilon) continue;

            double sidewaysDistance = direction is SpatialDirection.Left or SpatialDirection.Right
                ? Math.Abs(verticalDistance)
                : Math.Abs(horizontalDistance);
            double score = forwardDistance + sidewaysDistance * PerpendicularDistanceWeight;
            if (score >= bestScore) continue;

            bestIndex = targetIndex;
            bestScore = score;
        }

        return bestIndex;
    }
}
