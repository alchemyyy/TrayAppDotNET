using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.LogicalTree;
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

        Border parent = scope.Assign(new Border(), parentName: "SettingsRoot");
        TextBlock child = scope.Assign(new TextBlock(), parent);

        Assert.Equal(expected: "Window", window.Name);
        Assert.Equal(expected: "Border_SettingsRoot_0001", parent.Name);
        Assert.Equal(expected: "TextBlock_Border0001_0002", child.Name);
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

        Assert.Equal(expected: "DeviceList", root.Name);
        Assert.Equal(expected: "Border_DeviceList_0001", card.Name);
        Assert.Equal(expected: "TextBlock_Border0001_0002", value.Name);
    });

    [Fact]
    public void StyledLogicalSubtreeAssignmentAvoidsFirstChanceNameExceptions() => AvaloniaTestHost.Run(() =>
    {
        Window window = new();
        ControlNameScope scope = ControlNameScope.For(window);
        StackPanel root = new();
        Border child = new();
        root.Children.Add(child);
        window.Content = root;
        Assert.True(((ILogical)root).IsAttachedToLogicalTree);

        int namingExceptionCount = 0;
        EventHandler<FirstChanceExceptionEventArgs> handler = (sender, eventArgs) =>
        {
            if (eventArgs.Exception is InvalidOperationException
                {
                    Message: "Cannot set Name : styled element already styled."
                })
                Interlocked.Increment(ref namingExceptionCount);
        };

        AppDomain.CurrentDomain.FirstChanceException += handler;
        try
        {
            scope.AssignLogicalSubtree(root, window);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= handler;
        }

        Assert.Equal(expected: 0, namingExceptionCount);
        Assert.Null(root.Name);
        Assert.Null(child.Name);
        Assert.True(ControlNameScope.TryGetDetails(root, out ControlNameDetails? rootDetails));
        Assert.Equal(expected: "StackPanel_Window_0001", rootDetails!.Name);
        Assert.Equal(expected: ControlNameOrigin.VisualFallback, rootDetails.Origin);
        Assert.True(ControlNameScope.TryGetDetails(child, out ControlNameDetails? childDetails));
        Assert.Equal(expected: "Border_StackPanel0001_0002", childDetails!.Name);
        Assert.Equal(expected: ControlNameOrigin.VisualFallback, childDetails.Origin);
    });

    [Fact]
    public void TransientGeneratedNamesAreNotRetainedByScope() => AvaloniaTestHost.Run(() =>
    {
        Window window = new();
        ControlNameScope scope = ControlNameScope.For(window);
        WeakReference<string> generatedName = CreateTransientGeneratedName(scope);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(generatedName.TryGetTarget(out _));
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<string> CreateTransientGeneratedName(ControlNameScope scope)
    {
        Border control = scope.Assign(new Border(), parentName: "TransientControl");
        return new WeakReference<string>(control.Name!);
    }
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
