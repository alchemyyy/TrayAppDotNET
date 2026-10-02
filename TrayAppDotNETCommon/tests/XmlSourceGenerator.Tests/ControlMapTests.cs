using System.Reflection;
using TrayAppDotNETCommon.UI.ControlMapping;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class ControlMapTests
{
    // Template ids that app maps reference; CONTROL_MAP_PLAYBOOK.md lists them under Shared Templates
    private static readonly string[] SharedTemplateIDs =
    [
        "SettingsShell", "GeneralSection", "RenderingSection", "AboutPage", "UpdateConfirmation", "ColorPicker",
        "Installer", "Uninstaller", "ContextMenu", "EditableContextMenu", "SearchableListBox"
    ];

    // Templates whose instances supply children; each needs the unnamed slot those children default to
    private static readonly string[] SlottedTemplateIDs = ["SettingsShell", "ContextMenu", "EditableContextMenu"];

    // Named slots app maps route into
    private static readonly (string TemplateID, string SlotID)[] NamedSlots =
    [
        ("SettingsShell", "NavigationActions"),
        ("SettingsShell", "FooterActions")
    ];

    [Fact]
    public void EveryGeneratedIDResolvesToTheNodeAtItsPath()
    {
        UI.ControlMap map = new();
        List<ControlMapNodeID> ids = CollectGeneratedIDs(typeof(UI.ControlMap));

        Assert.NotEmpty(ids);
        foreach (ControlMapNodeID id in ids)
        {
            ControlMapNode? node = map.Find(id);
            Assert.True(node != null, $"{id} does not resolve in the compiled map");
            Assert.Equal(id.Path, node!.Path);
        }

        // The generator and the XAML compiler read one file, so each node has exactly one generated id
        Assert.Equal(ids.Count, map.Nodes.Count);
    }

    [Fact]
    public void IDsFromAnotherMapDoNotResolve()
    {
        UI.ControlMap map = new();
        // App maps share the class name, so only the namespace in the map name tells them apart
        ControlMapNodeID foreignID = new(Map: "BatteryTrayAppDotNET.UI.ControlMap", UI.ControlMap.SettingsShell.ID.Path);

        Assert.Null(map.Find(foreignID));
        Assert.NotNull(map.Find(UI.ControlMap.SettingsShell.ID));
    }

    [Fact]
    public void SiblingIndexesFollowDocumentOrder()
    {
        UI.ControlMap map = new();

        foreach (ControlMapNode node in map.Nodes.Values)
        {
            List<ControlMapNode> siblings = node.Parent?.Children ?? map.Children;
            Assert.Same(node, siblings[node.Index]);
        }
    }

    [Fact]
    public void SharedTemplatesExistWithTheSlotsAppMapsUse()
    {
        UI.ControlMap map = new();

        foreach (string templateID in SharedTemplateIDs)
            Assert.True(map.FindTemplate(templateID) != null, $"Template '{templateID}' is missing");

        foreach (string templateID in SlottedTemplateIDs)
            Assert.Equal(expected: 1, CountSlots(map.FindTemplate(templateID)!, slotID: string.Empty));

        foreach ((string templateID, string slotID) in NamedSlots)
            Assert.Equal(expected: 1, CountSlots(map.FindTemplate(templateID)!, slotID));
    }

    [Fact]
    public void EveryGestureParsesAsAnAvaloniaKeyGesture()
    {
        UI.ControlMap map = new();

        foreach (ControlMapNode node in map.Nodes.Values)
        {
            if (node is not Leaf leaf) continue;

            Assert.Equal(CountGestures(leaf.Keys), leaf.KeyGestures.Count);
            Assert.Equal(CountGestures(leaf.FocusKeys), leaf.FocusKeyGestures.Count);
        }
    }

    // Walks the generated nested classes iteratively and returns every static node id field
    private static List<ControlMapNodeID> CollectGeneratedIDs(Type mapType)
    {
        List<ControlMapNodeID> ids = [];
        Stack<Type> pending = new();
        pending.Push(mapType);
        while (pending.Count > 0)
        {
            Type type = pending.Pop();
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType == typeof(ControlMapNodeID))
                    ids.Add((ControlMapNodeID)field.GetValue(obj: null)!);
            }

            foreach (Type nestedType in type.GetNestedTypes(BindingFlags.Public))
                pending.Push(nestedType);
        }

        return ids;
    }

    private static int CountSlots(Template template, string slotID)
    {
        int slotCount = 0;
        Stack<ControlMapNode> pending = new();
        pending.Push(template);
        while (pending.Count > 0)
        {
            ControlMapNode node = pending.Pop();
            if (node is Slot && string.Equals(node.ID, slotID, StringComparison.Ordinal)) slotCount++;
            if (node is not ControlMapContainer container) continue;

            foreach (ControlMapNode child in container.Children)
                pending.Push(child);
        }

        return slotCount;
    }

    private static int CountGestures(string? gestures) =>
        gestures?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length ?? 0;
}
