namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Defines unavailable process-table presentation and numeric ordering.</summary>
internal static class ProcessTableValuePresentation
{
    public const string UnavailableText = "N/A";

    /// <summary>Orders every invalid or negative sample below valid nonnegative values.</summary>
    public static int CompareNonnegativeDouble(double left, double right)
    {
        bool leftIsAvailable = double.IsFinite(left) && left >= 0;
        bool rightIsAvailable = double.IsFinite(right) && right >= 0;
        if (!leftIsAvailable) return rightIsAvailable ? -1 : 0;
        if (!rightIsAvailable) return 1;
        return left.CompareTo(right);
    }
}
