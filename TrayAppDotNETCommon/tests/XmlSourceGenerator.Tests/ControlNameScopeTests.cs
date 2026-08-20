using Avalonia.Controls;
using TrayAppDotNETCommon.UI;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class ControlNameScopeTests
{
#if DEBUG
    [Fact]
    public void AssignedNamesUseParentIdentityAndTopLevelMonotonicIndex() => AvaloniaTestHost.Run(() =>
    {
        Window window = new();
        ControlNameScope scope = ControlNameScope.For(window);

        Border parent = scope.Assign(new Border(), "SettingsRoot");
        TextBlock child = scope.Assign(new TextBlock(), parent);

        Assert.Equal("Window", window.Name);
        Assert.Equal("Border_SettingsRoot_0001", parent.Name);
        Assert.Equal("TextBlock_Border0001_0002", child.Name);
    });

    [Fact]
    public void LogicalSubtreeAssignmentPreservesExplicitNames() => AvaloniaTestHost.Run(() =>
    {
        Window window = new();
        ControlNameScope scope = ControlNameScope.For(window);
        StackPanel root = new() { Name = "DeviceList" };
        Border card = new();
        TextBlock value = new();
        card.Child = value;
        root.Children.Add(card);

        scope.AssignLogicalSubtree(root, window);

        Assert.Equal("DeviceList", root.Name);
        Assert.Equal("Border_DeviceList_0001", card.Name);
        Assert.Equal("TextBlock_Border0001_0002", value.Name);
    });
#else
    [Fact]
    public void NamingIsDisabledInReleaseBuilds() => AvaloniaTestHost.Run(() =>
    {
        Window window = new();
        ControlNameScope scope = ControlNameScope.For(window);
        Border parent = scope.Assign(new Border(), "SettingsRoot");
        TextBlock child = new();
        parent.Child = child;

        scope.AssignLogicalSubtree(parent, window);

        Assert.Null(window.Name);
        Assert.Null(parent.Name);
        Assert.Null(child.Name);
    });
#endif
}
