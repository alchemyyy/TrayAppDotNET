using System.Globalization;

namespace TaskManagerTrayAppDotNET.UI.Performance;

/// <summary>Resource lookups shared by the Performance page views.</summary>
internal static class PerformanceText
{
    /// <summary>The placeholder shown for a value Windows did not report.</summary>
    internal static string Unavailable => L(nameof(AppStrings.Perf_Unavailable));

    internal static string L(string key) => LocalizationManager.Instance[key];

    internal static string F(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, L(key), arguments);
}
