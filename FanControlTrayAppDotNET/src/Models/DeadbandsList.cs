using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace FanControlTrayAppDotNET.Models;

// One closed interval of disallowed fan speeds. Inclusive on both ends. Units depend on the
// containing DeadbandsList's RPMMode (duty cycle % when false, RPM when true).
public class DeadbandRange
{
    [XmlAttribute]
    public int Lower { get; set; }

    [XmlAttribute]
    public int Upper { get; set; }

    public DeadbandRange() { }

    public DeadbandRange(int lower, int upper)
    {
        Lower = lower;
        Upper = upper;
    }
}

// User-defined "forbidden zone" for a fan's operating speed, stored with the fan settings and assigned by name.
// No control path applies the bands yet: curve and manual writes ignore them.
// Common use case: a pump that resonates between 600-900 RPM, or a fan that whines at 30-40% PWM.
//
// Lists are reusable across fans: assign by name through the static DeadbandsLists registry.
public class DeadbandsList : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public static readonly Dictionary<string, DeadbandsList> DeadbandsLists =
        new(StringComparer.OrdinalIgnoreCase);

    [XmlAttribute]
    public string Name
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
        }
    } = string.Empty;

    // When true, Bands are interpreted as RPM ranges. When false, duty cycle %.
    [XmlAttribute]
    public bool RPMMode { get; set; }

    [XmlArray("Bands")]
    [XmlArrayItem("Band")]
    public List<DeadbandRange> Bands { get; set; } = [];

    public static void Register(DeadbandsList list)
    {
        if (string.IsNullOrEmpty(list.Name)) return;
        DeadbandsLists[list.Name] = list;
    }

    public static void Unregister(string name) => DeadbandsLists.Remove(name);

    public static DeadbandsList? Find(string? name) =>
        string.IsNullOrEmpty(name) ? null : DeadbandsLists.GetValueOrDefault(name);

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
