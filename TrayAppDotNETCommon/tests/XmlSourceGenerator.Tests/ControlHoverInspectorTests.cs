#if DEBUG
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using TrayAppDotNETCommon.UI.Debugging;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class ControlHoverInspectorTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("YES")]
    [InlineData(" on ")]
    [InlineData("enabled")]
    public void ExplicitEnvironmentValuesEnableInspector(string value) =>
        Assert.True(ControlHoverInspectorActivation.IsEnabled(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("NO")]
    [InlineData(" off ")]
    public void MissingOrFalseEnvironmentValuesDisableInspector(string? value) =>
        Assert.False(ControlHoverInspectorActivation.IsEnabled(value));

    [Fact]
    public void InspectorRemainsVisibleWithoutSourceWindows() => AvaloniaTestHost.Run(() =>
    {
        using ControlHoverInspectorSession inspectorSession = new();
        Window sourceWindow = new();

        Assert.True(inspectorSession.IsInspectorVisible);
        sourceWindow.Show();
        Assert.True(inspectorSession.IsInspectorVisible);

        sourceWindow.Hide();
        Assert.True(inspectorSession.IsInspectorVisible);

        sourceWindow.Close();
    });

    [Fact]
    public void FreezeToggleRetainsAVisibleShortcutReminder() => AvaloniaTestHost.Run(() =>
    {
        using ControlHoverInspectorSession inspectorSession = new();

        Assert.False(inspectorSession.IsFrozen);
        Assert.Contains(ControlHoverInspectorShortcut.Hint, inspectorSession.InspectorStatusText);

        inspectorSession.ToggleFrozen();
        Assert.True(inspectorSession.IsFrozen);
        Assert.Contains("FROZEN", inspectorSession.InspectorStatusText);
        Assert.Contains(ControlHoverInspectorShortcut.Hint, inspectorSession.InspectorStatusText);

        inspectorSession.ToggleFrozen();
        Assert.False(inspectorSession.IsFrozen);
        Assert.Contains("LIVE", inspectorSession.InspectorStatusText);
    });

    [Fact]
    public void FreezeShortcutRequiresExactModifiers()
    {
        KeyModifiers freezeModifiers = KeyModifiers.Control | KeyModifiers.Alt;

        Assert.True(ControlHoverInspectorShortcut.IsFreezeToggle(Key.Q, freezeModifiers));
        Assert.False(ControlHoverInspectorShortcut.IsFreezeToggle(Key.Q, KeyModifiers.Control));
        Assert.False(ControlHoverInspectorShortcut.IsFreezeToggle(
            Key.Q,
            freezeModifiers | KeyModifiers.Shift));
        Assert.False(ControlHoverInspectorShortcut.IsFreezeToggle(Key.W, freezeModifiers));
    }

    [Fact]
    public void SnapshotIncludesEffectivePropertiesAndLayoutAncestry() => AvaloniaTestHost.Run(() =>
    {
        TextBlock valueText = new()
        {
            Name = "ValueText",
            Text = "42",
            Margin = new Thickness(4),
            Width = 120,
            Opacity = 0.75
        };
        valueText.Classes.Add("reading");

        Border card = new()
        {
            Name = "DeviceCard",
            Padding = new Thickness(8),
            Child = valueText
        };
        Window window = new()
        {
            Title = "Inspector test",
            Content = card
        };

        window.Show();
        ControlHoverInspectorSnapshot snapshot = ControlHoverInspectorSnapshotBuilder.Build(window, valueText);
        List<string> rows = Flatten(snapshot.Roots);

        Assert.Equal("TextBlock#ValueText.reading", snapshot.TargetLabel);
        Assert.Contains(rows, row => row.Contains("Target: TextBlock#ValueText.reading", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Window: Avalonia.Controls.Window", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Title: Inspector test", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Layout ancestry", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Border#DeviceCard", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Bounds in parent", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Transform to top level", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Render scaling", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Effective Avalonia properties", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Margin = L=4", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Width = 120 [LocalValue]", StringComparison.Ordinal));

        window.Close();
    });

    private static List<string> Flatten(IReadOnlyList<ControlHoverInspectorNode> roots)
    {
        List<string> rows = [];
        Stack<ControlHoverInspectorNode> pending = [];
        for (int index = roots.Count - 1; index >= 0; index--)
            pending.Push(roots[index]);

        while (pending.Count > 0)
        {
            ControlHoverInspectorNode node = pending.Pop();
            rows.Add(node.Text);
            for (int index = node.Children.Count - 1; index >= 0; index--)
                pending.Push(node.Children[index]);
        }

        return rows;
    }
}
#endif
