namespace TaskManagerTrayAppDotNET.Models;

internal enum SemanticProcessGroupKind : byte
{
    Infrastructure,
    ExplicitOwner,
    PackagedApplication,
    AncestryRoot,
    Singleton
}

internal enum SemanticProcessParentReason : byte
{
    NoParent,
    DirectAncestry,
    RetainedAncestry,
    ExplicitOwnership
}

internal enum SemanticProcessGroupClassification : byte
{
    Windows,
    App,
    Background
}

/// <summary>Known user and session boundary for identity-based application grouping.</summary>
internal readonly record struct SemanticSecurityScopeKey(string UserSID, int SessionID);

/// <summary>Tagged identity that cannot collide across semantic grouping domains.</summary>
internal readonly record struct SemanticProcessGroupKey(
    SemanticProcessGroupKind Kind,
    ProcessInstanceKey AnchorInstanceKey,
    SemanticSecurityScopeKey SecurityScope,
    string PackageFullName,
    string ApplicationSplit)
{
    public static SemanticProcessGroupKey Infrastructure(ProcessInstanceKey instanceKey) =>
        new(
            SemanticProcessGroupKind.Infrastructure,
            instanceKey,
            default,
            string.Empty,
            string.Empty);

    public static SemanticProcessGroupKey ExplicitOwner(
        SemanticSecurityScopeKey securityScope,
        ProcessInstanceKey ownerInstanceKey) =>
        new(
            SemanticProcessGroupKind.ExplicitOwner,
            ownerInstanceKey,
            securityScope,
            string.Empty,
            string.Empty);

    public static SemanticProcessGroupKey PackagedApplication(
        SemanticSecurityScopeKey securityScope,
        string packageFullName,
        string applicationSplit) =>
        new(
            SemanticProcessGroupKind.PackagedApplication,
            default,
            securityScope,
            packageFullName,
            applicationSplit);

    public static SemanticProcessGroupKey AncestryRoot(ProcessInstanceKey instanceKey) =>
        new(
            SemanticProcessGroupKind.AncestryRoot,
            instanceKey,
            default,
            string.Empty,
            string.Empty);

    public static SemanticProcessGroupKey Singleton(ProcessInstanceKey instanceKey) =>
        new(
            SemanticProcessGroupKind.Singleton,
            instanceKey,
            default,
            string.Empty,
            string.Empty);
}

/// <summary>One process node with separate semantic membership and logical parentage.</summary>
internal sealed record SemanticProcessNode(
    ProcessGroupingFacts Facts,
    SemanticProcessGroupKey GroupKey,
    ProcessInstanceKey? ParentInstanceKey,
    SemanticProcessParentReason ParentReason);

/// <summary>One materialized semantic group and its deterministic display representative.</summary>
internal sealed class SemanticProcessGroup
{
    public required SemanticProcessGroupKey Key { get; init; }
    public required SemanticProcessNode[] Nodes { get; init; }
    public required ProcessInstanceKey[] RootInstanceKeys { get; init; }
    public required ProcessInstanceKey RepresentativeInstanceKey { get; init; }
    public required SemanticProcessGroupClassification Classification { get; init; }
}

/// <summary>Retained evidence used only when the same exact process instance survives.</summary>
internal readonly record struct SemanticRetainedProcessState(
    SemanticProcessGroupKey GroupKey,
    SemanticSecurityScopeKey? SecurityScope,
    string? PackageFullName,
    string? ApplicationUserModelID);

/// <summary>Previous semantic membership keyed by PID plus creation time.</summary>
internal sealed class SemanticProcessTreeState
{
    private readonly Dictionary<ProcessInstanceKey, SemanticRetainedProcessState> _processes;

    public SemanticProcessTreeState()
    {
        _processes = [];
    }

    internal SemanticProcessTreeState(
        Dictionary<ProcessInstanceKey, SemanticRetainedProcessState> processes)
    {
        _processes = processes;
    }

    internal IReadOnlyDictionary<ProcessInstanceKey, SemanticRetainedProcessState> Processes =>
        _processes;
}

/// <summary>Immutable semantic groups plus exact-instance lookup and next retained state.</summary>
internal sealed class SemanticProcessForest
{
    private readonly Dictionary<ProcessInstanceKey, SemanticProcessNode> _nodesByInstance;

    public SemanticProcessForest(
        SemanticProcessGroup[] groups,
        Dictionary<ProcessInstanceKey, SemanticProcessNode> nodesByInstance,
        SemanticProcessTreeState retainedState)
    {
        Groups = groups;
        _nodesByInstance = nodesByInstance;
        RetainedState = retainedState;
    }

    public SemanticProcessGroup[] Groups { get; }
    public SemanticProcessTreeState RetainedState { get; }

    public bool TryGetNode(
        ProcessInstanceKey instanceKey,
        out SemanticProcessNode? node) =>
        _nodesByInstance.TryGetValue(instanceKey, out node);
}
