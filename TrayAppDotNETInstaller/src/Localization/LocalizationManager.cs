using System.Globalization;

namespace TrayAppDotNETInstaller.Localization;

/// <summary>
/// Resolves installer strings for the active culture. Mirrors the shared TrayAppDotNET manager's surface
/// (an indexer over resource keys with the key itself as the fallback) without referencing the shared library.
/// </summary>
public sealed class LocalizationManager
{
    public static LocalizationManager Instance { get; } = new();

    private CultureInfo _currentCulture = CultureInfo.CurrentUICulture;

    private LocalizationManager() { }

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            FrameworkCompatibility.ThrowIfNull(value, nameof(value));
            if (Equals(_currentCulture, value)) return;

            _currentCulture = value;
            Strings.Culture = value;
        }
    }

    public string this[string key] => GetString(key);

    /// <summary>Returns the localized text for a resource key, or the key when no text exists.</summary>
    public string GetString(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        string? resolved = Strings.ResourceManager.GetString(key, _currentCulture);
        return string.IsNullOrWhiteSpace(resolved) ? key : resolved;
    }
}
