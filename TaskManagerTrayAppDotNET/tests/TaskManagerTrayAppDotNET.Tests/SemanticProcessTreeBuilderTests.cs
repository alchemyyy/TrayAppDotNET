using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class SemanticProcessTreeBuilderTests
{
    private const string UserSID = "S-1-5-21-1000";

    [Fact]
    public void UnrelatedProcessesWithTheSameImageRemainSeparate()
    {
        ProcessGroupingFacts first = Facts(
            processID: 10,
            creationTime: 100,
            executableName: "dotnet.exe",
            executablePath: @"C:\Program Files\dotnet\dotnet.exe");
        ProcessGroupingFacts second = Facts(
            processID: 20,
            creationTime: 200,
            executableName: "dotnet.exe",
            executablePath: @"C:\Program Files\dotnet\dotnet.exe");

        SemanticProcessForest forest = SemanticProcessTreeBuilder.Build([second, first]);

        Assert.Equal(expected: 2, forest.Groups.Length);
        Assert.All(forest.Groups, group => Assert.Single(group.Nodes));
    }

    [Fact]
    public void SameImageParentAndChildShareAnAncestryGroup()
    {
        ProcessGroupingFacts parent = Facts(
            processID: 10,
            creationTime: 100,
            executableName: "browser.exe",
            executablePath: @"C:\Apps\Browser\browser.exe");
        ProcessGroupingFacts child = Facts(
            processID: 11,
            creationTime: 200,
            parentProcessID: parent.InstanceKey.ProcessID,
            executableName: "browser.exe",
            executablePath: @"C:\Apps\Browser\browser.exe",
            windowState: ProcessIndependentWindowState.Qualifying);

        SemanticProcessForest forest = SemanticProcessTreeBuilder.Build([child, parent]);
        SemanticProcessGroup group = Assert.Single(forest.Groups);
        SemanticProcessNode childNode = FindNode(forest, child.InstanceKey);

        Assert.Equal(expected: 2, group.Nodes.Length);
        Assert.Equal(parent.InstanceKey, childNode.ParentInstanceKey);
        Assert.Equal(SemanticProcessParentReason.DirectAncestry, childNode.ParentReason);
    }

    [Fact]
    public void DifferentImageHeadlessChildInheritsItsParentGroup()
    {
        ProcessGroupingFacts parent = Facts(
            10,
            100,
            executableName: "editor.exe",
            executablePath: @"C:\Apps\Editor\editor.exe");
        ProcessGroupingFacts child = Facts(
            11,
            200,
            parentProcessID: 10,
            executableName: "compiler.exe",
            executablePath: @"C:\Apps\Editor\compiler.exe",
            windowState: ProcessIndependentWindowState.None);

        SemanticProcessForest forest = SemanticProcessTreeBuilder.Build([parent, child]);

        Assert.Single(forest.Groups);
        Assert.Equal(parent.InstanceKey, FindNode(forest, child.InstanceKey).ParentInstanceKey);
    }

    [Fact]
    public void DifferentImageChildWithIndependentWindowBecomesANewRoot()
    {
        ProcessGroupingFacts parent = Facts(
            10,
            100,
            executableName: "terminal.exe",
            executablePath: @"C:\Apps\Terminal\terminal.exe");
        ProcessGroupingFacts child = Facts(
            11,
            200,
            parentProcessID: 10,
            executableName: "editor.exe",
            executablePath: @"C:\Apps\Editor\editor.exe",
            windowState: ProcessIndependentWindowState.Qualifying);

        SemanticProcessForest forest = SemanticProcessTreeBuilder.Build([parent, child]);

        Assert.Equal(expected: 2, forest.Groups.Length);
        Assert.Null(FindNode(forest, child.InstanceKey).ParentInstanceKey);
    }

    [Fact]
    public void StrongApplicationIdentityOverridesIndependentWindowBoundary()
    {
        ProcessGroupingFacts parent = Facts(
            10,
            100,
            executableName: "app.exe",
            executablePath: @"C:\Apps\Example\app.exe",
            packageFullName: "Example.Package_1.0_x64__publisher",
            applicationUserModelID: "Example.Package!App");
        ProcessGroupingFacts child = Facts(
            11,
            200,
            parentProcessID: 10,
            executableName: "helper.exe",
            executablePath: @"C:\Apps\Example\helper.exe",
            packageFullName: "Example.Package_1.0_x64__publisher",
            applicationUserModelID: "Example.Package!App",
            windowState: ProcessIndependentWindowState.Qualifying);

        SemanticProcessForest forest = SemanticProcessTreeBuilder.Build([parent, child]);

        Assert.Single(forest.Groups);
        Assert.Equal(parent.InstanceKey, FindNode(forest, child.InstanceKey).ParentInstanceKey);
    }

    [Fact]
    public void PackagedApplicationWithSeveralRootsCreatesOneMultiRootGroup()
    {
        ProcessGroupingFacts first = Facts(
            10,
            100,
            packageFullName: "Example.Package_1.0_x64__publisher",
            applicationUserModelID: "Example.Package!App",
            windowState: ProcessIndependentWindowState.Qualifying);
        ProcessGroupingFacts second = Facts(
            20,
            200,
            executableName: "background.exe",
            packageFullName: "Example.Package_1.0_x64__publisher",
            applicationUserModelID: "Example.Package!App");

        SemanticProcessGroup group = Assert.Single(
            SemanticProcessTreeBuilder.Build([second, first]).Groups);

        Assert.Equal(SemanticProcessGroupKind.PackagedApplication, group.Key.Kind);
        Assert.Equal(expected: 2, group.RootInstanceKeys.Length);
    }

    [Fact]
    public void ConflictingPackageApplicationsSplitAndDoNotGuessForAmbiguousRoot()
    {
        const string package = "Suite.Package_1.0_x64__publisher";
        ProcessGroupingFacts first = Facts(
            10,
            100,
            packageFullName: package,
            applicationUserModelID: "Suite.Package!First");
        ProcessGroupingFacts second = Facts(
            20,
            200,
            packageFullName: package,
            applicationUserModelID: "Suite.Package!Second");
        ProcessGroupingFacts ambiguous = Facts(
            30,
            300,
            packageFullName: package,
            applicationUserModelID: null,
            isApplicationUserModelIDAmbiguous: true);

        SemanticProcessForest forest = SemanticProcessTreeBuilder.Build([ambiguous, second, first]);

        Assert.Equal(expected: 3, forest.Groups.Length);
        Assert.Equal(
            SemanticProcessGroupKind.Singleton,
            FindNode(forest, ambiguous.InstanceKey).GroupKey.Kind);
    }

    [Fact]
    public void PackageMemberWithoutApplicationIDInheritsNearestPackageAncestorSplit()
    {
        const string package = "Suite.Package_1.0_x64__publisher";
        ProcessGroupingFacts firstApp = Facts(
            10,
            100,
            packageFullName: package,
            applicationUserModelID: "Suite.Package!First");
        ProcessGroupingFacts secondApp = Facts(
            20,
            110,
            packageFullName: package,
            applicationUserModelID: "Suite.Package!Second");
        ProcessGroupingFacts helper = Facts(
            11,
            200,
            parentProcessID: 10,
            executableName: "helper.exe",
            packageFullName: package,
            applicationUserModelID: null);

        SemanticProcessForest forest = SemanticProcessTreeBuilder.Build([helper, secondApp, firstApp]);

        Assert.Equal(
            FindNode(forest, firstApp.InstanceKey).GroupKey,
            FindNode(forest, helper.InstanceKey).GroupKey);
        Assert.NotEqual(
            FindNode(forest, secondApp.InstanceKey).GroupKey,
            FindNode(forest, helper.InstanceKey).GroupKey);
    }

    [Fact]
    public void ExplorerLaunchBoundaryDoesNotAbsorbApplication()
    {
        string explorerPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "explorer.exe");
        ProcessGroupingFacts explorer = Facts(
            10,
            100,
            executableName: "explorer.exe",
            executablePath: explorerPath);
        ProcessGroupingFacts application = Facts(
            11,
            200,
            parentProcessID: 10,
            executableName: "application.exe",
            windowState: ProcessIndependentWindowState.None);

        SemanticProcessForest forest = SemanticProcessTreeBuilder.Build([application, explorer]);

        Assert.Equal(expected: 2, forest.Groups.Length);
        Assert.Equal(
            SemanticProcessGroupKind.Infrastructure,
            FindNode(forest, explorer.InstanceKey).GroupKey.Kind);
        Assert.Null(FindNode(forest, application.InstanceKey).ParentInstanceKey);
    }

    [Fact]
    public void RuntimeBrokerIsNotPreferredOverVisiblePackagedApplication()
    {
        const string package = "Example.Package_1.0_x64__publisher";
        ProcessGroupingFacts broker = Facts(
            10,
            100,
            executableName: "RuntimeBroker.exe",
            packageFullName: package,
            applicationUserModelID: "Example.Package!App");
        ProcessGroupingFacts application = Facts(
            20,
            200,
            executableName: "Example.exe",
            packageFullName: package,
            applicationUserModelID: "Example.Package!App",
            windowState: ProcessIndependentWindowState.Qualifying);

        SemanticProcessGroup group = Assert.Single(
            SemanticProcessTreeBuilder.Build([broker, application]).Groups);

        Assert.Equal(application.InstanceKey, group.RepresentativeInstanceKey);
        Assert.Equal(SemanticProcessGroupClassification.App, group.Classification);
    }

    [Fact]
    public void ParentPIDPointingToNewerProcessIsRejected()
    {
        ProcessGroupingFacts child = Facts(11, 100, parentProcessID: 10);
        ProcessGroupingFacts newerParent = Facts(10, 200);

        SemanticProcessForest forest = SemanticProcessTreeBuilder.Build([newerParent, child]);

        Assert.Equal(expected: 2, forest.Groups.Length);
        Assert.Null(FindNode(forest, child.InstanceKey).ParentInstanceKey);
    }

    [Fact]
    public void VerifiedGroupSurvivesParentExitWithoutRenderingDeadParent()
    {
        ProcessGroupingFacts parent = Facts(
            10,
            100,
            executableName: "editor.exe",
            executablePath: @"C:\Apps\Editor\editor.exe");
        ProcessGroupingFacts child = Facts(
            11,
            200,
            parentProcessID: 10,
            executableName: "compiler.exe",
            executablePath: @"C:\Apps\Editor\compiler.exe");
        SemanticProcessForest firstForest = SemanticProcessTreeBuilder.Build([parent, child]);

        SemanticProcessForest secondForest = SemanticProcessTreeBuilder.Build(
            [child],
            firstForest.RetainedState);
        SemanticProcessNode survivingChild = FindNode(secondForest, child.InstanceKey);

        Assert.Single(secondForest.Groups);
        Assert.Single(secondForest.Groups[0].Nodes);
        Assert.Equal(FindNode(firstForest, child.InstanceKey).GroupKey, survivingChild.GroupKey);
        Assert.Null(survivingChild.ParentInstanceKey);
        Assert.Equal(SemanticProcessParentReason.RetainedAncestry, survivingChild.ParentReason);
    }

    [Fact]
    public void ChildFirstSeenAfterUnknownParentExitIsSingleton()
    {
        ProcessGroupingFacts child = Facts(11, 200, parentProcessID: 10);

        SemanticProcessNode node = FindNode(
            SemanticProcessTreeBuilder.Build([child]),
            child.InstanceKey);

        Assert.Equal(SemanticProcessGroupKind.Singleton, node.GroupKey.Kind);
        Assert.Null(node.ParentInstanceKey);
    }

    [Fact]
    public void SamePackageInDifferentSessionsRemainsSeparate()
    {
        const string package = "Example.Package_1.0_x64__publisher";
        ProcessGroupingFacts first = Facts(10, 100, packageFullName: package, sessionID: 1);
        ProcessGroupingFacts second = Facts(20, 200, packageFullName: package, sessionID: 2);

        SemanticProcessForest forest = SemanticProcessTreeBuilder.Build([first, second]);

        Assert.Equal(expected: 2, forest.Groups.Length);
    }

    [Fact]
    public void PIDReuseCannotRetainPriorGroupMembership()
    {
        ProcessGroupingFacts parent = Facts(10, 100);
        ProcessGroupingFacts original = Facts(11, 200, parentProcessID: 10);
        SemanticProcessForest firstForest = SemanticProcessTreeBuilder.Build([parent, original]);
        ProcessGroupingFacts reused = Facts(11, 300, parentProcessID: -1);

        SemanticProcessNode reusedNode = FindNode(
            SemanticProcessTreeBuilder.Build([reused], firstForest.RetainedState),
            reused.InstanceKey);

        Assert.Equal(SemanticProcessGroupKind.Singleton, reusedNode.GroupKey.Kind);
        Assert.NotEqual(FindNode(firstForest, original.InstanceKey).GroupKey, reusedNode.GroupKey);
    }

    [Fact]
    public void UnknownWindowFactPrefersExtraRootForDifferentImages()
    {
        ProcessGroupingFacts parent = Facts(10, 100, executableName: "parent.exe");
        ProcessGroupingFacts child = Facts(
            11,
            200,
            parentProcessID: 10,
            executableName: "unknown.exe",
            executablePath: null,
            windowState: ProcessIndependentWindowState.Unknown);

        SemanticProcessForest forest = SemanticProcessTreeBuilder.Build([parent, child]);

        Assert.Equal(expected: 2, forest.Groups.Length);
        Assert.Null(FindNode(forest, child.InstanceKey).ParentInstanceKey);
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
        string? userSID = UserSID,
        int sessionID = 1,
        string? packageFullName = "",
        string? applicationUserModelID = null,
        bool isApplicationUserModelIDAmbiguous = false,
        ProcessIndependentWindowState windowState = ProcessIndependentWindowState.None,
        bool isCriticalOrProtected = false,
        bool isCreationTimeKnown = true) =>
        new(
            new ProcessInstanceKey(processID, creationTime),
            isCreationTimeKnown,
            parentProcessID,
            executableName,
            executablePath,
            userSID,
            sessionID,
            packageFullName,
            applicationUserModelID,
            isApplicationUserModelIDAmbiguous,
            windowState,
            isCriticalOrProtected);
}
