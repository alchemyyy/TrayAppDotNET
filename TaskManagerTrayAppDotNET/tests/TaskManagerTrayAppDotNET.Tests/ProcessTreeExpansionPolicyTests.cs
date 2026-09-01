using TaskManagerTrayAppDotNET.Models;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessTreeExpansionPolicyTests
{
    [Theory]
    [InlineData(ProcessTreeDefaultState.Collapsed, false, false, true)]
    [InlineData(ProcessTreeDefaultState.Collapsed, true, false, true)]
    [InlineData(ProcessTreeDefaultState.Collapsed, true, true, false)]
    [InlineData(ProcessTreeDefaultState.Expanded, false, false, false)]
    [InlineData(ProcessTreeDefaultState.Expanded, true, false, false)]
    public void StartsCollapsedAppliesSemanticSectionExemption(
        ProcessTreeDefaultState defaultState,
        bool isSemanticSection,
        bool expandSemanticSectionsByDefault,
        bool expected)
    {
        Assert.Equal(
            expected,
            ProcessTreeExpansionPolicy.StartsCollapsed(
                defaultState,
                isSemanticSection,
                expandSemanticSectionsByDefault));
    }
}
