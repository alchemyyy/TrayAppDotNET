using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessTreeDescendantsTests
{
    private const int RootProcessID = 100;
    private const long RootCreated = 1_000;

    [Fact]
    public void ValidChainIsCollectedParentsFirst()
    {
        List<ProcessTreeNode> descendants = ProcessTreeDescendants.Collect(
            RootProcessID,
            RootCreated,
            [
                new(ProcessID: 300, ParentProcessID: 200, CreationTimeFileTime: 3_000),
                new(ProcessID: 200, ParentProcessID: RootProcessID, CreationTimeFileTime: 2_000),
                new(ProcessID: 400, ParentProcessID: 300, CreationTimeFileTime: 4_000)
            ]);

        Assert.Equal([200, 300, 400], descendants.Select(static node => node.ProcessID));
    }

    [Fact]
    public void RecycledPIDDropsTheOrphansBelowIt()
    {
        // PID 200 was a launcher that exited; its orphan 300 still records parent 200. A later descendant of the
        // root received the recycled PID 200, created after the orphan, so the orphan edge fails with its subtree.
        List<ProcessTreeNode> descendants = ProcessTreeDescendants.Collect(
            RootProcessID,
            RootCreated,
            [
                new(ProcessID: 200, ParentProcessID: RootProcessID, CreationTimeFileTime: 5_000),
                new(ProcessID: 300, ParentProcessID: 200, CreationTimeFileTime: 2_000),
                new(ProcessID: 310, ParentProcessID: 300, CreationTimeFileTime: 6_000),
                new(ProcessID: 210, ParentProcessID: 200, CreationTimeFileTime: 7_000)
            ]);

        Assert.Equal([200, 210], descendants.Select(static node => node.ProcessID));
    }

    [Fact]
    public void ChildOlderThanTheRootIsNotPartOfTheTree()
    {
        List<ProcessTreeNode> descendants = ProcessTreeDescendants.Collect(
            RootProcessID,
            RootCreated,
            [
                new(ProcessID: 200, ParentProcessID: RootProcessID, CreationTimeFileTime: 500),
                new(ProcessID: 300, ParentProcessID: 200, CreationTimeFileTime: 3_000)
            ]);

        Assert.Empty(descendants);
    }

    [Fact]
    public void UnknownCreationTimesFailTheEdge()
    {
        Assert.Empty(ProcessTreeDescendants.Collect(
            RootProcessID,
            rootCreationTimeFileTime: 0,
            [new(ProcessID: 200, ParentProcessID: RootProcessID, CreationTimeFileTime: 2_000)]));
        Assert.Empty(ProcessTreeDescendants.Collect(
            RootProcessID,
            RootCreated,
            [new(ProcessID: 200, ParentProcessID: RootProcessID, CreationTimeFileTime: 0)]));
    }

    [Fact]
    public void EqualCreationTimesAreNotAValidEdge() =>
        Assert.False(ProcessTreeDescendants.IsValidEdge(parentCreationTimeFileTime: 10, childCreationTimeFileTime: 10));
}
