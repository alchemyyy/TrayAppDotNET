using System.Runtime.CompilerServices;
using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.UI.Flyout;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class FanFlyoutCellRetentionTests
{
    [Fact]
    public void DisposeIsIdempotentAndStopsGroupNotifications()
    {
        FanGroup group = new() { Name = "Group" };
        FanFlyoutCell cell = new(group, [CreateFan("Fan")]);
        int notificationCount = 0;
        cell.PropertyChanged += (_, _) => notificationCount++;

        group.Name = "Before disposal";
        Assert.True(notificationCount > 0);

        cell.Dispose();
        cell.Dispose();
        int countAfterDisposal = notificationCount;
        group.Name = "After disposal";

        Assert.Equal(countAfterDisposal, notificationCount);
        Assert.Empty(cell.Fans);
    }

    [Fact]
    public void DisposedCellIsCollectibleWhilePublisherRemainsAlive()
    {
        FanGroup group = new() { Name = "Group" };
        WeakReference<FanFlyoutCell> reference = CreateDisposedCellReference(group);

        ForceCollection();

        Assert.False(reference.TryGetTarget(out FanFlyoutCell? retainedCell));
        Assert.Null(retainedCell);
        GC.KeepAlive(group);
    }

    [Fact]
    public void PureDragArrangementIsNotRetainedByGroupPublisher()
    {
        FanGroup group = new() { Name = "Group" };
        Fan fan = CreateFan("Fan");
        WeakReference<FanDragCellArrangement> reference = CreateArrangementReference(group, fan);

        ForceCollection();

        Assert.False(reference.TryGetTarget(out FanDragCellArrangement? retainedArrangement));
        Assert.Null(retainedArrangement);
        GC.KeepAlive(group);
        GC.KeepAlive(fan);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<FanFlyoutCell> CreateDisposedCellReference(FanGroup group)
    {
        FanFlyoutCell cell = new(group, [CreateFan("Fan")]);
        WeakReference<FanFlyoutCell> reference = new(cell);
        cell.Dispose();
        return reference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<FanDragCellArrangement> CreateArrangementReference(FanGroup group, Fan fan)
    {
        FanDragCellArrangement arrangement = new(group, [fan]);
        return new WeakReference<FanDragCellArrangement>(arrangement);
    }

    private static Fan CreateFan(string name) => new()
    {
        FansName = name,
        DataSourceKey = name
    };

    private static void ForceCollection()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }
}
