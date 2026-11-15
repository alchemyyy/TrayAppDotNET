using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Controls;

namespace TrayAppDotNETCommon.UI.Settings;

public sealed class TrayAppDotNETGeneralSettingsSectionOptions
{
    public required SettingsPalette Palette { get; init; }
    public required CornerRadius CardRadius { get; init; }
    public required Func<string, string> L { get; init; }
    public required Action Save { get; init; }
    public required Func<string, string, string, string?, Task<bool>> ConfirmAsync { get; init; }
    public required Func<string, string, Task> ShowMessage { get; init; }
    public required Func<bool> GetRunOnStartup { get; init; }
    public required Action<bool> SetRunOnStartup { get; init; }
    public required Func<string> GetLanguage { get; init; }
    public required Action<string> SetLanguage { get; init; }

    /// <summary>Culture names of the translations the app ships beyond the neutral English resources</summary>
    public IReadOnlyList<string> AvailableLanguages { get; init; } = [];

    /// <summary>Supplying both start-minimized delegates adds that sub-option to the Run on startup card.</summary>
    public Func<bool>? GetStartMinimized { get; init; }

    public Action<bool>? SetStartMinimized { get; init; }

    public required Func<string?> GetCurrentStartupShortcutTarget { get; init; }
}

public sealed class TrayAppDotNETGeneralSettingsSection
{
    private readonly TrayAppDotNETGeneralSettingsSectionOptions _options;
    private TextBlock? _startupDescription;

    public TrayAppDotNETGeneralSettingsSection(TrayAppDotNETGeneralSettingsSectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// Builds the Language card: Auto, then the neutral language and every translation the app ships. The culture is
    /// read once at startup, so a change offers the same restart the rendering backend card does.
    /// </summary>
    public Border BuildLanguageCard()
    {
        List<(string Tag, string Text)> items =
        [
            (LocalizationManager.AutoLanguage, L(nameof(CommonStrings.Settings_General_Language_Auto)))
        ];
        items.AddRange(LanguageOptions(_options.AvailableLanguages));

        // A saved language whose translation is no longer shipped shows as Auto without rewriting the setting
        string selectedLanguage = _options.GetLanguage();
        if (!items.Exists(item => string.Equals(item.Tag, selectedLanguage, StringComparison.Ordinal)))
            selectedLanguage = LocalizationManager.AutoLanguage;

        return TrayAppDotNETSettingsCards.ComboCard(
            L(nameof(CommonStrings.Settings_General_Language_Title)),
            L(nameof(CommonStrings.Settings_General_Language_Description)),
            items,
            selectedLanguage,
            _options.SetLanguage,
            _options.Palette,
            _options.CardRadius,
            _options.Save,
            afterSave: OfferLanguageRestart,
            autoSizeToText: true,
            searchKeywords: [L(nameof(CommonStrings.Settings_General_Language_SearchKeywords))],
            node: ControlMap.LanguageCard.Language);
    }

    private void OfferLanguageRestart() =>
        _ = OfferLanguageRestartAsync();

    private async Task OfferLanguageRestartAsync()
    {
        try
        {
            await TrayAppDotNETRestartPrompt.PromptAsync(
                _options.ConfirmAsync,
                _options.ShowMessage,
                _options.L,
                L(nameof(CommonStrings.Settings_General_LanguageRestart_Title)),
                L(nameof(CommonStrings.Settings_General_LanguageRestart_Message)));
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Language restart prompt failed: {exception}");
        }
    }

    /// <summary>
    /// Pairs the neutral language and each translation with its native display name, sorted by that name.
    /// Names .NET does not recognize are logged and skipped.
    /// </summary>
    internal static List<(string Tag, string Text)> LanguageOptions(IReadOnlyList<string> translations)
    {
        List<(string Tag, string Text)> options = [];
        HashSet<string> seenNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string cultureName in translations.Prepend(LocalizationManager.NeutralLanguage))
        {
            CultureInfo culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(cultureName);
            }
            catch (CultureNotFoundException exception)
            {
                TADNLog.Log($"Language: skipping unknown translation culture '{cultureName}': {exception.Message}");
                continue;
            }

            if (!seenNames.Add(culture.Name)) continue;

            // Native names can start lowercase ("français"); pickers show them capitalized
            string nativeName = culture.NativeName;
            string text = culture.TextInfo.ToUpper(nativeName[..1]) + nativeName[1..];
            options.Add((culture.Name, text));
        }

        options.Sort((left, right) => string.Compare(left.Text, right.Text, StringComparison.CurrentCultureIgnoreCase));
        return options;
    }

    public Border BuildStartupCard()
    {
        SettingsPalette p = _options.Palette;
        bool runOnStartup = _options.GetRunOnStartup();
        CheckBox? startMinimizedCheckBox = BuildStartMinimizedCheckBox(p, runOnStartup);
        SettingsToggle startupToggle = TrayAppDotNETSettingsUI.Toggle(p, runOnStartup, (_, enabled) =>
        {
            _options.SetRunOnStartup(enabled);
            _options.Save();
            RefreshStartupDescription();
            startMinimizedCheckBox?.IsEnabled = enabled;
        }).MapTo(ControlMap.StartupCard.RunOnStartup);

        List<string> searchKeywords = [L(nameof(CommonStrings.Settings_General_RunOnStartup_SearchKeywords))];
        if (startMinimizedCheckBox != null)
            searchKeywords.Add(L(nameof(CommonStrings.Settings_General_StartMinimized_SearchKeywords)));

        Border startupCard = TrayAppDotNETSettingsCards.MutableCard(
            L(nameof(CommonStrings.Settings_General_RunOnStartup_Title)),
            RunOnStartupDescription(),
            startupToggle,
            p,
            _options.CardRadius,
            out TextBlock startupDescriptionText,
            searchKeywords,
            belowDescription: startMinimizedCheckBox);
        _startupDescription = startupDescriptionText;
        return startupCard;
    }

    /// <summary>Builds the start-minimized sub-option, which only applies while Run on startup is on.</summary>
    private CheckBox? BuildStartMinimizedCheckBox(SettingsPalette palette, bool runOnStartup)
    {
        if (_options.GetStartMinimized == null || _options.SetStartMinimized == null) return null;

        Action<bool> setStartMinimized = _options.SetStartMinimized;
        CheckBox checkBox = new CheckBox
        {
            Content = TrayAppDotNETSettingsUI.Text(L(nameof(CommonStrings.Settings_General_StartMinimized_Title)), palette),
            IsChecked = _options.GetStartMinimized(),
            IsEnabled = runOnStartup,
            Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = SettingsCardsLayout.SubOptionMargin
        }.MapTo(ControlMap.StartupCard.StartMinimized);
        TrayAppDotNETToolTip.SetTip(checkBox, L(nameof(CommonStrings.Settings_General_StartMinimized_Description)));
        checkBox.IsCheckedChanged += (_, _) =>
        {
            setStartMinimized(checkBox.IsChecked == true);
            _options.Save();
        };
        return checkBox;
    }

    private void RefreshStartupDescription() => _startupDescription?.Text = RunOnStartupDescription();

    private string RunOnStartupDescription()
    {
        string? target = _options.GetCurrentStartupShortcutTarget();
        if (string.IsNullOrEmpty(target))
            return L(nameof(CommonStrings.Settings_General_RunOnStartup_Description));

        return string.Format(
            CultureInfo.CurrentCulture,
            L(nameof(CommonStrings.Settings_General_RunOnStartup_OnDescriptionFormat)),
            L(nameof(CommonStrings.Settings_General_RunOnStartup_OnHeaderLine)),
            target);
    }

    private string L(string key) => _options.L(key);
}
