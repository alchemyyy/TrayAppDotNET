#if DEBUG
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Debugging;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class ControlHoverInspectorTests
{
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
    public void ActivationShortcutRequiresExactModifiers()
    {
        KeyModifiers activationModifiers = KeyModifiers.Control | KeyModifiers.Alt;

        Assert.True(ControlHoverInspectorShortcut.IsActivationToggle(Key.D, activationModifiers));
        Assert.False(ControlHoverInspectorShortcut.IsActivationToggle(Key.D, KeyModifiers.Control));
        Assert.False(ControlHoverInspectorShortcut.IsActivationToggle(
            Key.D,
            activationModifiers | KeyModifiers.Shift));
        Assert.False(ControlHoverInspectorShortcut.IsActivationToggle(Key.Q, activationModifiers));
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
        Grid.SetRow(valueText, 2);

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
        Assert.Contains(rows, row => row.Contains("Visual ancestry", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Border#DeviceCard", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("bounds=", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Render scaling", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Effective Avalonia properties", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Margin = L=4", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Width = 120 [LocalValue]", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.Contains("Row = 2 [LocalValue]", StringComparison.Ordinal));

        window.Close();
    });

    [Fact]
    public void CaptureQueueCoalescesAHoverBurstToTheLatestTarget() => AvaloniaTestHost.Run(() =>
    {
        List<Action> scheduledCaptures = [];
        IInputElement? capturedElement = null;
        ControlHoverInspectorCaptureQueue captureQueue = new(
            callback => scheduledCaptures.Add(callback),
            (_, hitElement) => capturedElement = hitElement);
        Window topLevel = new();
        TextBlock? latestTarget = null;

        for (int index = 0; index < 10_000; index++)
        {
            latestTarget = new TextBlock { Text = index.ToString() };
            captureQueue.Enqueue(topLevel, latestTarget);
        }

        Assert.True(captureQueue.HasPendingCapture);
        Assert.Single(scheduledCaptures);

        scheduledCaptures[0]();

        Assert.False(captureQueue.HasPendingCapture);
        Assert.Same(latestTarget, capturedElement);
    });

    [Fact]
    public void InspectorWindowReusesOneBoundedRootCollection() => AvaloniaTestHost.Run(() =>
    {
        ControlHoverInspectorWindow inspectorWindow = new();
        inspectorWindow.Show();
        object? rootItemsSource = inspectorWindow.RootItemsSource;

        for (int index = 0; index < 2_000; index++)
        {
            ControlHoverInspectorSnapshot snapshot = new(
                $"Target {index}",
                [new ControlHoverInspectorNode($"Snapshot {index}")]);
            inspectorWindow.ShowSnapshot(snapshot);
        }

        Assert.Same(rootItemsSource, inspectorWindow.RootItemsSource);
        Assert.Equal(1, inspectorWindow.DisplayedRootCount);
        inspectorWindow.Close();
    });

    [Fact]
    public void InspectorWindowReleasesReplacedSnapshotGraphs() => AvaloniaTestHost.Run(() =>
    {
        ControlHoverInspectorWindow inspectorWindow = new();
        inspectorWindow.Show();
        WeakReference<ControlHoverInspectorNode> firstSnapshotRoot = ShowTransientSnapshot(inspectorWindow, 0);

        for (int index = 1; index <= 100; index++)
            _ = ShowTransientSnapshot(inspectorWindow, index);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(firstSnapshotRoot.TryGetTarget(out _));
        inspectorWindow.Close();
    });

    [Fact]
    public void RepeatedRealCapturesReleaseEarlierSnapshotGraphs() => AvaloniaTestHost.Run(() =>
    {
        List<TextBlock> targets = [];
        StackPanel content = new();
        for (int targetIndex = 0; targetIndex < 24; targetIndex++)
        {
            TextBlock target = new()
            {
                Name = $"Target{targetIndex}",
                Text = targetIndex.ToString(),
                Margin = new Thickness(targetIndex % 4)
            };
            targets.Add(target);
            content.Children.Add(target);
        }

        Window sourceWindow = new() { Content = content };
        ControlHoverInspectorWindow inspectorWindow = new();
        sourceWindow.Show();
        inspectorWindow.Show();

        WeakReference<ControlHoverInspectorNode> firstSnapshotRoot =
            CaptureRealSnapshot(inspectorWindow, sourceWindow, targets[0]);
        for (int captureIndex = 1; captureIndex <= 512; captureIndex++)
        {
            TextBlock target = targets[captureIndex % targets.Count];
            _ = CaptureRealSnapshot(inspectorWindow, sourceWindow, target);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(firstSnapshotRoot.TryGetTarget(out _));
        Assert.InRange(
            inspectorWindow.DisplayedRootCount,
            1,
            ControlHoverInspectorSnapshotBuilder.MaximumSnapshotNodeCount);
        inspectorWindow.Close();
        sourceWindow.Close();
    });

    [Fact]
    public void SnapshotBoundsDeepAncestry() => AvaloniaTestHost.Run(() =>
    {
        TextBlock target = new() { Name = "BoundedTarget", Text = "target" };
        Control root = target;
        for (int depth = 0; depth < ControlHoverInspectorSnapshotBuilder.MaximumVisualPathElements + 8; depth++)
        {
            Grid grid = new();
            grid.Children.Add(root);
            root = grid;
        }

        Window window = new() { Content = root };
        window.Show();

        ControlHoverInspectorSnapshot snapshot = ControlHoverInspectorSnapshotBuilder.Build(window, target);
        List<string> rows = Flatten(snapshot.Roots);

        Assert.True(
            rows.Count <= ControlHoverInspectorSnapshotBuilder.MaximumSnapshotNodeCount,
            $"Snapshot retained {rows.Count} rows.");
        Assert.Contains(rows, row => row.Contains("visual ancestry truncated", StringComparison.Ordinal));

        window.Close();
    });

    [Fact]
    public void SnapshotDoesNotTraverseAndNameUnrelatedVisualBranches() => AvaloniaTestHost.Run(() =>
    {
        TextBlock target = new() { Name = "Target", Text = "target" };
        Border unrelatedBranch = new() { Child = new TextBlock { Text = "unrelated" } };
        StackPanel content = new()
        {
            Children =
            {
                target,
                unrelatedBranch
            }
        };
        Window window = new() { Content = content };
        window.Show();

        Assert.False(ControlNameScope.TryGetDetails(unrelatedBranch, out _));

        _ = ControlHoverInspectorSnapshotBuilder.Build(window, target);

        Assert.False(ControlNameScope.TryGetDetails(unrelatedBranch, out _));
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<ControlHoverInspectorNode> ShowTransientSnapshot(
        ControlHoverInspectorWindow inspectorWindow,
        int index)
    {
        ControlHoverInspectorNode root = new($"Snapshot {index}", isExpanded: true);
        for (int childIndex = 0; childIndex < 64; childIndex++)
            root.Children.Add(new ControlHoverInspectorNode($"Child {childIndex}"));

        ControlHoverInspectorSnapshot snapshot = new($"Target {index}", [root]);
        inspectorWindow.ShowSnapshot(snapshot);
        return new WeakReference<ControlHoverInspectorNode>(root);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<ControlHoverInspectorNode> CaptureRealSnapshot(
        ControlHoverInspectorWindow inspectorWindow,
        Window sourceWindow,
        TextBlock target)
    {
        ControlHoverInspectorSnapshot snapshot =
            ControlHoverInspectorSnapshotBuilder.Build(sourceWindow, target);
        ControlHoverInspectorNode firstRoot = snapshot.Roots[0];
        inspectorWindow.ShowSnapshot(snapshot);
        return new WeakReference<ControlHoverInspectorNode>(firstRoot);
    }
}
#endif
