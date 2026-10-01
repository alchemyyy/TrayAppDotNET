using TaskManagerTrayAppDotNET.Models;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessTableColumnCatalogTests
{
    [Theory]
    [InlineData(ProcessTableColumnKind.Name, false)]
    [InlineData(ProcessTableColumnKind.CommandLine, false)]
    [InlineData(ProcessTableColumnKind.ProcessID, true)]
    [InlineData(ProcessTableColumnKind.Disk, true)]
    [InlineData(ProcessTableColumnKind.Network, true)]
    [InlineData(ProcessTableColumnKind.CPU, true)]
    [InlineData(ProcessTableColumnKind.PrivateMemory, true)]
    public void DefaultSortDirectionFollowsColumnAlignment(
        ProcessTableColumnKind column,
        bool expectedDescending)
    {
        Assert.Equal(
            expectedDescending,
            ProcessTableColumnCatalog.SortsDescendingByDefault(column));
    }
}
