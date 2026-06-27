using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;

namespace FanControlTrayAppDotNET.Models;

// Stub: a Trigger fires a fan action when some condition becomes true. Conditions and actions
// will be fleshed out in a later pass; for now this gives the Fan model somewhere to hold them.
public class Trigger : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    [XmlAttribute] public string Name { get; set; } = string.Empty;

    [XmlAttribute] public bool Enabled { get; set; } = true;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
