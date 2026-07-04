using System.Globalization;
using Avalonia;
using Avalonia.Controls;

namespace TrayAppDotNETCommon.UI.Controls;

internal sealed class ControlAXAMLResources
{
    private const string CardsResourcesUri = "avares://TrayAppDotNETCommon/UI/Controls/Cards.axaml";
    private const string ColorPickerWindowResourcesUri =
        "avares://TrayAppDotNETCommon/UI/Controls/ColorPickerWindow.axaml";
    private const string DialogChromeResourcesUri = "avares://TrayAppDotNETCommon/UI/Controls/DialogChrome.axaml";
    private const string FlyoutCardsResourcesUri = "avares://TrayAppDotNETCommon/UI/Controls/FlyoutCards.axaml";
    private const string FlyoutSliderResourcesUri = "avares://TrayAppDotNETCommon/UI/Controls/FlyoutSlider.axaml";
    private const string SearchableListBoxResourcesUri =
        "avares://TrayAppDotNETCommon/UI/Controls/SearchableListBox.axaml";
    private const string SettingsUIResourcesUri = "avares://TrayAppDotNETCommon/UI/Controls/SettingsUI.axaml";
    private const string UpdateConfirmationWindowResourcesUri =
        "avares://TrayAppDotNETCommon/UI/Controls/UpdateConfirmationWindow.axaml";

    private readonly string _prefix;
    private readonly Lazy<ResourceDictionary> _resources;

    public ControlAXAMLResources(string resourceUri, string prefix)
    {
        _prefix = prefix.EndsWith('.') ? prefix : prefix + ".";
        _resources = new Lazy<ResourceDictionary>(() => Load(resourceUri));
    }

    /// <summary>
    /// Reads a double resource.
    /// </summary>
    public double Double(string name) =>
        Resource(name) switch
        {
            double value => value,
            int value => value,
            string value => double.Parse(value, CultureInfo.InvariantCulture),
            object value => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        };

    /// <summary>
    /// Reads an integer resource.
    /// </summary>
    public int Int(string name) => (int)Math.Round(Double(name));

    /// <summary>
    /// Reads a thickness resource.
    /// </summary>
    public Thickness Thickness(string name) =>
        Resource(name) is Thickness value
            ? value
            : throw InvalidType(name, nameof(Thickness));

    /// <summary>
    /// Reads a corner-radius resource.
    /// </summary>
    public CornerRadius CornerRadius(string name) =>
        Resource(name) is CornerRadius value
            ? value
            : throw InvalidType(name, nameof(CornerRadius));

    private object Resource(string name)
    {
        string key = _prefix + name;
        object? value = _resources.Value[key];
        return value ?? throw new InvalidOperationException($"Missing resource '{key}'.");
    }

    private static ResourceDictionary Load(string resourceUri)
    {
        return resourceUri switch
        {
            CardsResourcesUri => new CardsResources(),
            ColorPickerWindowResourcesUri => new ColorPickerWindowResources(),
            DialogChromeResourcesUri => new DialogChromeResources(),
            FlyoutCardsResourcesUri => new FlyoutCardsResources(),
            FlyoutSliderResourcesUri => new FlyoutSliderResources(),
            SearchableListBoxResourcesUri => new SearchableListBoxResources(),
            SettingsUIResourcesUri => new SettingsUIResources(),
            UpdateConfirmationWindowResourcesUri => new UpdateConfirmationWindowResources(),
            _ => throw new InvalidOperationException($"Unknown AXAML resource dictionary '{resourceUri}'."),
        };
    }

    private InvalidOperationException InvalidType(string name, string expectedType) =>
        new($"Resource '{_prefix}{name}' is not a {expectedType}.");
}
