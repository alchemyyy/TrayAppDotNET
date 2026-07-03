using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TrayAppDotNETCommon.UI.Controls;

internal sealed class ControlAXAMLResources
{
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

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Control layout resources are static AXAML dictionaries embedded in this assembly.")]
    private static ResourceDictionary Load(string resourceUri)
    {
        Uri uri = new(resourceUri, UriKind.Absolute);
        object? loaded = AvaloniaXamlLoader.Load(uri);
        return loaded as ResourceDictionary ??
               throw new InvalidOperationException($"AXAML resource '{resourceUri}' is not a ResourceDictionary.");
    }

    private InvalidOperationException InvalidType(string name, string expectedType) =>
        new($"Resource '{_prefix}{name}' is not a {expectedType}.");
}
