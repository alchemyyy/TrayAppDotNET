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
    public static LocalizationManager Instance { get; } = new();

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

        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
