using System.Globalization;
using System.Runtime.CompilerServices;
using TrayAppDotNETCommon.Localization;
using AppStrings = TaskManagerTrayAppDotNET.Localization.Strings;

namespace TaskManagerTrayAppDotNET.Tests;

/// <summary>
/// Loads the app's neutral resources before any test runs, as the app host does at startup. Without this,
/// LocalizationManager returns key names, so formatted UI strings would lose their placeholders and tests could not
/// tell a correct string from a wrong one.
/// </summary>
internal static class TestLocalization
{
    [ModuleInitializer]
    internal static void Initialize() =>
        LocalizationManager.Instance.Initialize(
            AppStrings.ResourceManager,
            static culture => AppStrings.Culture = culture,
            CultureInfo.InvariantCulture);

    /// <summary>The resource string for <paramref name="key"/>; fails the test if the key is missing.</summary>
    internal static string L(string key) =>
        LocalizationManager.Instance.TryGetString(key, out string value)
            ? value
            : throw new InvalidOperationException($"Resource string '{key}' is missing.");

    /// <summary>Formats the resource string for <paramref name="key"/> the way the UI does.</summary>
    internal static string F(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, L(key), arguments);
}
