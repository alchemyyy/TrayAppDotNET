using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace TrayAppDotNETCommon.UI;

public sealed class HotReloadResourceReader(Control owner, string prefix)
{
    private readonly Control _owner = owner;
    private readonly string _prefix = prefix.EndsWith('.') ? prefix : prefix + ".";

    public double Double(string name) =>
        Resource(name) switch
        {
            double value => value,
            int value => value,
            string value => double.Parse(value, CultureInfo.InvariantCulture),
            object value => Convert.ToDouble(value, CultureInfo.InvariantCulture),
        };

    public int Int(string name) => (int)Math.Round(Double(name));

    public Thickness Thickness(string name) =>
        Resource(name) is Thickness value
            ? value
            : throw InvalidType(name, nameof(Thickness));

    public CornerRadius CornerRadius(string name) =>
        Resource(name) is CornerRadius value
            ? value
            : throw InvalidType(name, nameof(CornerRadius));

    public TranslateTransform TranslateTransform(string name) =>
        Resource(name) is TranslateTransform value
            ? value
            : throw InvalidType(name, nameof(TranslateTransform));

    public TranslateTransform CloneTranslateTransform(string name)
    {
        TranslateTransform value = TranslateTransform(name);
        return new TranslateTransform(value.X, value.Y);
    }

    private object Resource(string name)
    {
        string key = _prefix + name;
        object? value = _owner.Resources[key];
        return value ?? throw new InvalidOperationException($"Missing hot-reload resource '{key}'.");
    }

    private InvalidOperationException InvalidType(string name, string expectedType) =>
        new($"Hot-reload resource '{_prefix}{name}' is not a {expectedType}.");
}
