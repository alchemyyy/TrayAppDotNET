using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace TrayAppDotNETCommon.Localization;

/// <summary>
/// Shared bridge between .resx-backed string lookups and UI binding.
/// The host app supplies its generated resource manager once at startup.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    public static LocalizationManager Instance { get; } = new();

    private ResourceManager? _resourceManager;
    private Action<CultureInfo>? _applyGeneratedStringsCulture;
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

    public bool IsInitialized => _resourceManager != null;

    public string this[string key] => GetString(key, key);

    public string GetString(string key, string fallback = "")
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        string? resolved = _resourceManager?.GetString(key, _currentCulture);
        return resolved ?? fallback;
    }

    public bool TryGetString(string key, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrEmpty(key) || _resourceManager == null) return false;

        string? resolved = _resourceManager.GetString(key, _currentCulture);
        if (resolved == null) return false;

        value = resolved;
        return true;
    }

    public void Initialize(
        ResourceManager resourceManager,
        Action<CultureInfo>? applyGeneratedStringsCulture = null,
        CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(resourceManager);

        _resourceManager = resourceManager;
        _applyGeneratedStringsCulture = applyGeneratedStringsCulture;

        CultureInfo target = culture ?? CultureInfo.CurrentUICulture;
        ApplyCulture(target);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyCulture(CultureInfo culture)
    {
        _currentCulture = culture;
        _applyGeneratedStringsCulture?.Invoke(culture);

        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
