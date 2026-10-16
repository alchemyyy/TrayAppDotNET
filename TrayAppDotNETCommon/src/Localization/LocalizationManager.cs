using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace TrayAppDotNETCommon.Localization;

/// <summary>
/// Resolves app-specific resources before shared resources and exposes culture changes to UI binding.
/// The host app supplies its generated resource manager once at startup.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    /// <summary>Language setting value that follows the Windows display language</summary>
    public const string AutoLanguage = "Auto";

    /// <summary>Culture of the neutral resx files, which every app ships</summary>
    public const string NeutralLanguage = "en";

    /// <summary>Windows display language, captured before a Language setting replaces the thread cultures</summary>
    public static CultureInfo SystemUICulture { get; } = CultureInfo.CurrentUICulture;

    public static LocalizationManager Instance { get; } = new();

    /// <summary>
    /// Maps a Language setting value to the culture to apply. Auto, a blank value, and a language without a shipped
    /// translation resolve to the Windows display language.
    /// </summary>
    public static CultureInfo ResolveCulture(
        string? language,
        IReadOnlyList<string> availableLanguages,
        Action<string>? log = null)
    {
        string trimmedLanguage = language?.Trim() ?? string.Empty;
        if (trimmedLanguage.Length == 0 ||
            string.Equals(trimmedLanguage, AutoLanguage, StringComparison.OrdinalIgnoreCase))
            return SystemUICulture;

        bool isShipped = string.Equals(trimmedLanguage, NeutralLanguage, StringComparison.OrdinalIgnoreCase) ||
                         availableLanguages.Contains(trimmedLanguage, StringComparer.OrdinalIgnoreCase);
        if (!isShipped)
        {
            log?.Invoke($"LocalizationManager: no shipped translation for '{trimmedLanguage}'; using {AutoLanguage}");
            return SystemUICulture;
        }

        try
        {
            return CultureInfo.GetCultureInfo(trimmedLanguage);
        }
        catch (CultureNotFoundException exception)
        {
            log?.Invoke(
                $"LocalizationManager: '{trimmedLanguage}' is not a known culture; using {AutoLanguage}: {exception.Message}");
            return SystemUICulture;
        }
    }

    private ResourceManager? _applicationResourceManager;
    private Action<CultureInfo>? _applyApplicationStringsCulture;
    private CultureInfo _currentCulture = CultureInfo.CurrentUICulture;

    private LocalizationManager() { }

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (Equals(_currentCulture, value)) return;

            ApplyCulture(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CultureChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsInitialized => _applicationResourceManager != null;

    public string this[string key] => GetString(key);

    public string GetString(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        // Application resources intentionally override shared defaults
        string? resolved = _applicationResourceManager?.GetString(key, _currentCulture)
                           ?? CommonStrings.ResourceManager.GetString(key, _currentCulture);
        return string.IsNullOrWhiteSpace(resolved) ? key : resolved;
    }

    public bool TryGetString(string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrEmpty(key)) return false;

        string? resolved = _applicationResourceManager?.GetString(key, _currentCulture)
                           ?? CommonStrings.ResourceManager.GetString(key, _currentCulture);
        if (string.IsNullOrWhiteSpace(resolved)) return false;

        value = resolved;
        return true;
    }

    public void Initialize(
        ResourceManager resourceManager,
        Action<CultureInfo>? applyGeneratedStringsCulture = null,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(resourceManager);

        _applicationResourceManager = resourceManager;
        _applyApplicationStringsCulture = applyGeneratedStringsCulture;

        CultureInfo target = culture ?? CultureInfo.CurrentUICulture;
        ApplyCulture(target);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyCulture(CultureInfo culture)
    {
        _currentCulture = culture;
        _applyApplicationStringsCulture?.Invoke(culture);
        CommonStrings.Culture = culture;

        // Only the UI language follows the setting; number and date formats keep the Windows regional format,
        // which Windows also treats separately from the display language
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
