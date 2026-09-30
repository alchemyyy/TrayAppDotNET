using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class SemanticProcessGroupRootsTests
{
    private const string UserSID = "S-1-5-21-1000";
    private const string BrowserPath = @"C:\Apps\Browser\browser.exe";
    private const string PackageFullName = "Example.Package_1.0_x64__publisher";
    private const string ApplicationUserModelID = "Example.Package!App";

    [Fact]
    public void WindowedChildRepresentativeResolvesToItsGroupRoot()
    {
        ProcessGroupingFacts root = Facts(10, 100, executableName: "browser.exe", executablePath: BrowserPath);
        ProcessGroupingFacts window = Facts(
            11,
            200,
            parentProcessID: 10,
            executableName: "browser.exe",
            executablePath: BrowserPath,
            windowState: ProcessIndependentWindowState.Qualifying);

        SemanticProcessForest forest = SemanticProcessTreeBuilder.Build([window, root]);
        SemanticProcessGroup group = Assert.Single(forest.Groups);

        Assert.Equal(window.InstanceKey, group.RepresentativeInstanceKey);
        Assert.Equal(root.InstanceKey, SemanticProcessGroupRoots.ResolveRootInstanceKey(forest, group));
    }

    [Fact]
    public void OtherRootsAttachBeneathTheRepresentativeRoot()
    {
        ProcessGroupingFacts application = Facts(
            10,
            100,
            executableName: "app.exe",
            packageFullName: PackageFullName,
            applicationUserModelID: ApplicationUserModelID,
            windowState: ProcessIndependentWindowState.Qualifying);
        ProcessGroupingFacts background = Facts(
            20,
            200,
            executableName: "background.exe",
            packageFullName: PackageFullName,
            applicationUserModelID: ApplicationUserModelID);

        SemanticProcessForest forest = SemanticProcessTreeBuilder.Build([background, application]);
        SemanticProcessGroup group = Assert.Single(forest.Groups);
        ProcessInstanceKey rootInstanceKey = SemanticProcessGroupRoots.ResolveRootInstanceKey(forest, group);

        Assert.Equal(expected: 2, group.RootInstanceKeys.Length);
        Assert.Equal(application.InstanceKey, rootInstanceKey);
        Assert.Null(SemanticProcessGroupRoots.ResolveParentInstanceKey(
            FindNode(forest, application.InstanceKey),
            rootInstanceKey));
        Assert.Equal(
            rootInstanceKey,
            SemanticProcessGroupRoots.ResolveParentInstanceKey(
                FindNode(forest, background.InstanceKey),
                rootInstanceKey));
    }

    [Fact]
    public void SemanticParentSurvivesEitherGroupHeading()
    {
        ProcessGroupingFacts root = Facts(10, 100, executableName: "browser.exe", executablePath: BrowserPath);
        ProcessGroupingFacts child = Facts(
            11,
            200,
            parentProcessID: 10,
            executableName: "browser.exe",
            executablePath: BrowserPath);
        ProcessInstanceKey syntheticInstanceKey = new(
            SemanticProcessSections.FirstGroupSyntheticProcessID,
            CreationTimeTicks: 0);

        SemanticProcessForest forest = SemanticProcessTreeBuilder.Build([root, child]);
        SemanticProcessNode rootNode = FindNode(forest, root.InstanceKey);
        SemanticProcessNode childNode = FindNode(forest, child.InstanceKey);

        Assert.Equal(root.InstanceKey, SemanticProcessGroupRoots.ResolveParentInstanceKey(childNode, root.InstanceKey));
        Assert.Equal(
            root.InstanceKey,
            SemanticProcessGroupRoots.ResolveParentInstanceKey(childNode, syntheticInstanceKey));
        Assert.Equal(
            syntheticInstanceKey,
            SemanticProcessGroupRoots.ResolveParentInstanceKey(rootNode, syntheticInstanceKey));
        Assert.Null(SemanticProcessGroupRoots.ResolveParentInstanceKey(rootNode, groupHeadingInstanceKey: null));
    }

    [Fact]
    public void DescendantsCoverEveryLevelBeneathEachMember()
    {
        int[][] descendants = SemanticProcessGroupRoots.CollectDescendants([-1, 0, 0, 2, 3]);

        Assert.Equal([1, 2, 3, 4], descendants[0]);
        Assert.Empty(descendants[1]);
        Assert.Equal([3, 4], descendants[2]);
        Assert.Equal([4], descendants[3]);
        Assert.Empty(descendants[4]);
    }

    [Fact]
    public void DescendantWalkStopsOnMalformedParents()
    {
        int[][] cyclic = SemanticProcessGroupRoots.CollectDescendants([1, 0]);
        int[][] outOfRange = SemanticProcessGroupRoots.CollectDescendants([-1, 7]);

        Assert.Equal([1], cyclic[0]);
        Assert.Equal([0], cyclic[1]);
        Assert.All(outOfRange, static memberDescendants => Assert.Empty(memberDescendants));
    }

    private static SemanticProcessNode FindNode(
        SemanticProcessForest forest,
        ProcessInstanceKey instanceKey)
    {
        Assert.True(forest.TryGetNode(instanceKey, out SemanticProcessNode? node));
        return Assert.IsType<SemanticProcessNode>(node);
    }

    private static ProcessGroupingFacts Facts(
        int processID,
        long creationTime,
        int parentProcessID = -1,
        string executableName = "process.exe",
        string? executablePath = @"C:\Apps\process.exe",
        string? packageFullName = "",
        string? applicationUserModelID = null,
        ProcessIndependentWindowState windowState = ProcessIndependentWindowState.None) =>
        new(
            new ProcessInstanceKey(processID, creationTime),
            IsCreationTimeKnown: true,
            parentProcessID,
            executableName,
            executablePath,
            UserSID,
            SessionID: 1,
            packageFullName,
            applicationUserModelID,
            IsApplicationUserModelIDAmbiguous: false,
            windowState,
            IsCritical: false,
            IsProtected: false);
}
