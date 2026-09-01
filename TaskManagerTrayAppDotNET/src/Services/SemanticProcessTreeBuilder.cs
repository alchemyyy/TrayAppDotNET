namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Builds deterministic semantic application groups without presentation sort or filter state.</summary>
internal static class SemanticProcessTreeBuilder
{
    public static SemanticProcessForest Build(
        IReadOnlyList<ProcessGroupingFacts> snapshot,
        SemanticProcessTreeState? previousState = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        SemanticProcessTreeState retainedState = previousState ?? new SemanticProcessTreeState();
        int processCount = snapshot.Count;
        if (processCount == 0)
        {
            return new SemanticProcessForest(
                [],
                new Dictionary<ProcessInstanceKey, SemanticProcessNode>(),
                new SemanticProcessTreeState());
        }

        ProcessGroupingFacts[] facts = new ProcessGroupingFacts[processCount];
        for (int processIndex = 0; processIndex < processCount; processIndex++)
            facts[processIndex] = Normalize(snapshot[processIndex]);

        int[] orderedIndexes = CreateOrderedIndexes(facts);
        Dictionary<ProcessInstanceKey, int> indexByInstance = new(processCount);
        Dictionary<int, int> indexByProcessID = new(processCount);
        for (int orderedIndex = 0; orderedIndex < orderedIndexes.Length; orderedIndex++)
        {
            int processIndex = orderedIndexes[orderedIndex];
            ProcessGroupingFacts process = facts[processIndex];
            indexByInstance.TryAdd(process.InstanceKey, processIndex);
            indexByProcessID.TryAdd(process.InstanceKey.ProcessID, processIndex);
        }

        int[] liveParentIndexes = BuildValidatedParentIndexes(facts, indexByProcessID);
        bool[] isInfrastructure = new bool[processCount];
        SemanticProcessGroupKey?[] groupKeys = new SemanticProcessGroupKey?[processCount];
        for (int orderedIndex = 0; orderedIndex < orderedIndexes.Length; orderedIndex++)
        {
            int processIndex = orderedIndexes[orderedIndex];
            bool isolated = SemanticProcessInfrastructurePolicy.IsIsolatedInfrastructure(facts[processIndex]);
            isInfrastructure[processIndex] = isolated;
            if (isolated)
                groupKeys[processIndex] = SemanticProcessGroupKey.Infrastructure(facts[processIndex].InstanceKey);
        }

        SeedExplicitOwnerGroups(
            facts,
            orderedIndexes,
            indexByInstance,
            isInfrastructure,
            groupKeys);
        SeedPackagedApplicationGroups(
            facts,
            orderedIndexes,
            liveParentIndexes,
            isInfrastructure,
            groupKeys);
        PropagateAncestryGroups(
            facts,
            orderedIndexes,
            liveParentIndexes,
            isInfrastructure,
            retainedState,
            groupKeys);
        ConvertFreshSingleProcessRoots(facts, retainedState, groupKeys);

        SemanticProcessNode[] nodes = BuildNodes(
            facts,
            orderedIndexes,
            indexByInstance,
            liveParentIndexes,
            isInfrastructure,
            groupKeys);
        SemanticProcessGroup[] groups = MaterializeGroups(
            nodes,
            indexByInstance,
            orderedIndexes);
        Dictionary<ProcessInstanceKey, SemanticProcessNode> nodesByInstance = new(processCount);
        Dictionary<ProcessInstanceKey, SemanticRetainedProcessState> nextRetainedProcesses = new(processCount);
        for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
        {
            SemanticProcessNode node = nodes[nodeIndex];
            nodesByInstance.TryAdd(node.Facts.InstanceKey, node);
            if (!node.Facts.IsCreationTimeKnown) continue;
            nextRetainedProcesses[node.Facts.InstanceKey] = new SemanticRetainedProcessState(
                node.GroupKey,
                TryGetSecurityScope(node.Facts, out SemanticSecurityScopeKey securityScope)
                    ? securityScope
                    : null,
                node.Facts.PackageFullName,
                ResolveApplicationUserModelID(node.Facts));
        }

        return new SemanticProcessForest(
            groups,
            nodesByInstance,
            new SemanticProcessTreeState(nextRetainedProcesses));
    }

    private static ProcessGroupingFacts Normalize(ProcessGroupingFacts facts) => facts with
    {
        ExecutableName = facts.ExecutableName ?? string.Empty,
        ExecutablePath = NormalizeOptionalPath(facts.ExecutablePath),
        UserSID = facts.UserSID,
        PackageFullName = NormalizeOptionalIdentity(facts.PackageFullName),
        ApplicationUserModelID = NormalizeOptionalIdentity(facts.ApplicationUserModelID)
    };

    private static string? NormalizeOptionalPath(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception exception) when (exception is ArgumentException
                                               or NotSupportedException
                                               or PathTooLongException)
        {
            return value;
        }
    }

    private static string? NormalizeOptionalIdentity(string? value) =>
        value is { Length: > 0 } ? value.ToUpperInvariant() : value;

    private static int[] CreateOrderedIndexes(ProcessGroupingFacts[] facts)
    {
        int[] indexes = new int[facts.Length];
        for (int processIndex = 0; processIndex < indexes.Length; processIndex++)
            indexes[processIndex] = processIndex;
        Array.Sort(indexes, (leftIndex, rightIndex) => CompareFacts(facts[leftIndex], facts[rightIndex]));
        return indexes;
    }

    private static int CompareFacts(ProcessGroupingFacts left, ProcessGroupingFacts right)
    {
        if (left.IsCreationTimeKnown != right.IsCreationTimeKnown)
            return left.IsCreationTimeKnown ? -1 : 1;
        if (left.IsCreationTimeKnown)
        {
            int creationComparison = left.InstanceKey.CreationTimeTicks.CompareTo(
                right.InstanceKey.CreationTimeTicks);
            if (creationComparison != 0) return creationComparison;
        }

        int processIDComparison = left.InstanceKey.ProcessID.CompareTo(right.InstanceKey.ProcessID);
        return processIDComparison != 0
            ? processIDComparison
            : left.InstanceKey.CreationTimeTicks.CompareTo(right.InstanceKey.CreationTimeTicks);
    }

    private static int[] BuildValidatedParentIndexes(
        ProcessGroupingFacts[] facts,
        Dictionary<int, int> indexByProcessID)
    {
        int[] parentIndexes = new int[facts.Length];
        Array.Fill(parentIndexes, value: -1);
        for (int childIndex = 0; childIndex < facts.Length; childIndex++)
        {
            ProcessGroupingFacts child = facts[childIndex];
            if (!child.IsCreationTimeKnown
                || child.ParentProcessID < 0
                || child.ParentProcessID == child.InstanceKey.ProcessID
                || !indexByProcessID.TryGetValue(child.ParentProcessID, out int parentIndex))
                continue;

            ProcessGroupingFacts parent = facts[parentIndex];
            if (!parent.IsCreationTimeKnown
                || parent.InstanceKey == child.InstanceKey
                || parent.InstanceKey.CreationTimeTicks >= child.InstanceKey.CreationTimeTicks)
                continue;
            parentIndexes[childIndex] = parentIndex;
        }

        // Strict creation ordering already prevents a cycle; retain an explicit malformed-data guard
        for (int childIndex = 0; childIndex < parentIndexes.Length; childIndex++)
        {
            int currentIndex = parentIndexes[childIndex];
            int visitedCount = 0;
            while (currentIndex >= 0 && visitedCount <= parentIndexes.Length)
            {
                if (currentIndex == childIndex)
                {
                    parentIndexes[childIndex] = -1;
                    break;
                }

                currentIndex = parentIndexes[currentIndex];
                visitedCount++;
            }

            if (visitedCount > parentIndexes.Length)
                parentIndexes[childIndex] = -1;
        }

        return parentIndexes;
    }

    private static void SeedExplicitOwnerGroups(
        ProcessGroupingFacts[] facts,
        int[] orderedIndexes,
        Dictionary<ProcessInstanceKey, int> indexByInstance,
        bool[] isInfrastructure,
        SemanticProcessGroupKey?[] groupKeys)
    {
        for (int orderedIndex = 0; orderedIndex < orderedIndexes.Length; orderedIndex++)
        {
            int hostIndex = orderedIndexes[orderedIndex];
            ProcessGroupingFacts host = facts[hostIndex];
            if (isInfrastructure[hostIndex]
                || host.ExplicitOwnerInstanceKey is not { } ownerInstanceKey
                || !indexByInstance.TryGetValue(ownerInstanceKey, out int ownerIndex)
                || isInfrastructure[ownerIndex])
                continue;

            ProcessGroupingFacts owner = facts[ownerIndex];
            if (!owner.IsCreationTimeKnown
                || !host.IsCreationTimeKnown
                || owner.InstanceKey.CreationTimeTicks >= host.InstanceKey.CreationTimeTicks
                || HaveKnownSecurityScopeConflict(host, owner))
                continue;

            SemanticSecurityScopeKey securityScope = TryGetSecurityScope(owner, out SemanticSecurityScopeKey knownScope)
                ? knownScope
                : default;
            SemanticProcessGroupKey ownerKey = groupKeys[ownerIndex]
                                                       ?? SemanticProcessGroupKey.ExplicitOwner(
                                                           securityScope,
                                                           owner.InstanceKey);
            if (ownerKey.Kind != SemanticProcessGroupKind.ExplicitOwner)
                ownerKey = SemanticProcessGroupKey.ExplicitOwner(securityScope, owner.InstanceKey);
            groupKeys[ownerIndex] = ownerKey;
            groupKeys[hostIndex] = ownerKey;
        }
    }

    private static void SeedPackagedApplicationGroups(
        ProcessGroupingFacts[] facts,
        int[] orderedIndexes,
        int[] liveParentIndexes,
        bool[] isInfrastructure,
        SemanticProcessGroupKey?[] groupKeys)
    {
        Dictionary<PackageCohortKey, List<int>> cohorts = [];
        for (int orderedIndex = 0; orderedIndex < orderedIndexes.Length; orderedIndex++)
        {
            int processIndex = orderedIndexes[orderedIndex];
            ProcessGroupingFacts process = facts[processIndex];
            if (isInfrastructure[processIndex]
                || groupKeys[processIndex].HasValue
                || process.PackageFullName is not { Length: > 0 } packageFullName
                || !TryGetSecurityScope(process, out SemanticSecurityScopeKey securityScope))
                continue;

            PackageCohortKey cohortKey = new(
                securityScope,
                packageFullName);
            if (!cohorts.TryGetValue(cohortKey, out List<int>? cohort))
            {
                cohort = [];
                cohorts.Add(cohortKey, cohort);
            }

            cohort.Add(processIndex);
        }

        foreach (KeyValuePair<PackageCohortKey, List<int>> pair in cohorts)
        {
            PackageCohortKey cohortKey = pair.Key;
            List<int> cohort = pair.Value;
            HashSet<string> applicationSplits = new(StringComparer.OrdinalIgnoreCase);
            for (int memberIndex = 0; memberIndex < cohort.Count; memberIndex++)
            {
                string? applicationID = ResolveApplicationUserModelID(facts[cohort[memberIndex]]);
                if (applicationID is { Length: > 0 }) applicationSplits.Add(applicationID);
            }

            if (applicationSplits.Count <= 1)
            {
                string applicationSplit = applicationSplits.Count == 0
                    ? string.Empty
                    : applicationSplits.First();
                SemanticProcessGroupKey key = SemanticProcessGroupKey.PackagedApplication(
                    cohortKey.SecurityScope,
                    cohortKey.PackageFullName,
                    applicationSplit);
                for (int memberIndex = 0; memberIndex < cohort.Count; memberIndex++)
                    groupKeys[cohort[memberIndex]] = key;
                continue;
            }

            for (int memberIndex = 0; memberIndex < cohort.Count; memberIndex++)
            {
                int processIndex = cohort[memberIndex];
                string? applicationID = ResolveApplicationUserModelID(facts[processIndex]);
                if (applicationID is not { Length: > 0 }) continue;
                groupKeys[processIndex] = SemanticProcessGroupKey.PackagedApplication(
                    cohortKey.SecurityScope,
                    cohortKey.PackageFullName,
                    applicationID);
            }

            for (int memberIndex = 0; memberIndex < cohort.Count; memberIndex++)
            {
                int processIndex = cohort[memberIndex];
                if (groupKeys[processIndex].HasValue) continue;

                int ancestorIndex = liveParentIndexes[processIndex];
                while (ancestorIndex >= 0)
                {
                    ProcessGroupingFacts ancestor = facts[ancestorIndex];
                    if (!string.Equals(
                            ancestor.PackageFullName,
                            cohortKey.PackageFullName,
                            StringComparison.OrdinalIgnoreCase)
                        || !TryGetSecurityScope(ancestor, out SemanticSecurityScopeKey ancestorScope)
                        || ancestorScope != cohortKey.SecurityScope)
                        break;

                    if (groupKeys[ancestorIndex] is { } ancestorKey
                        && ancestorKey.Kind == SemanticProcessGroupKind.PackagedApplication
                        && ancestorKey.ApplicationSplit.Length > 0)
                    {
                        groupKeys[processIndex] = ancestorKey;
                        break;
                    }

                    ancestorIndex = liveParentIndexes[ancestorIndex];
                }
            }
        }
    }

    private static void PropagateAncestryGroups(
        ProcessGroupingFacts[] facts,
        int[] orderedIndexes,
        int[] liveParentIndexes,
        bool[] isInfrastructure,
        SemanticProcessTreeState previousState,
        SemanticProcessGroupKey?[] groupKeys)
    {
        for (int orderedIndex = 0; orderedIndex < orderedIndexes.Length; orderedIndex++)
        {
            int processIndex = orderedIndexes[orderedIndex];
            if (groupKeys[processIndex].HasValue) continue;

            int parentIndex = liveParentIndexes[processIndex];
            if (parentIndex >= 0
                && groupKeys[parentIndex] is { } parentGroupKey
                && CanInheritGroup(
                    facts[processIndex],
                    facts[parentIndex],
                    isInfrastructure[processIndex],
                    isInfrastructure[parentIndex]))
            {
                groupKeys[processIndex] = parentGroupKey;
                continue;
            }

            ProcessGroupingFacts process = facts[processIndex];
            if (process.IsCreationTimeKnown
                && previousState.Processes.TryGetValue(
                    process.InstanceKey,
                    out SemanticRetainedProcessState retainedProcess)
                && CanRetainGroup(process, retainedProcess, isInfrastructure[processIndex]))
            {
                groupKeys[processIndex] = retainedProcess.GroupKey;
                continue;
            }

            groupKeys[processIndex] = SemanticProcessGroupKey.AncestryRoot(process.InstanceKey);
        }
    }

    private static void ConvertFreshSingleProcessRoots(
        ProcessGroupingFacts[] facts,
        SemanticProcessTreeState previousState,
        SemanticProcessGroupKey?[] groupKeys)
    {
        Dictionary<SemanticProcessGroupKey, int> memberCounts = [];
        for (int processIndex = 0; processIndex < groupKeys.Length; processIndex++)
        {
            SemanticProcessGroupKey key = groupKeys[processIndex]
                                          ?? throw new InvalidOperationException(
                                              "Semantic grouping left a process unassigned.");
            memberCounts.TryGetValue(key, out int memberCount);
            memberCounts[key] = memberCount + 1;
        }

        for (int processIndex = 0; processIndex < groupKeys.Length; processIndex++)
        {
            SemanticProcessGroupKey key = groupKeys[processIndex]!.Value;
            if (key.Kind != SemanticProcessGroupKind.AncestryRoot
                || key.AnchorInstanceKey != facts[processIndex].InstanceKey
                || memberCounts[key] != 1)
                continue;
            if (previousState.Processes.TryGetValue(
                    facts[processIndex].InstanceKey,
                    out SemanticRetainedProcessState retainedProcess)
                && retainedProcess.GroupKey == key)
                continue;
            groupKeys[processIndex] = SemanticProcessGroupKey.Singleton(facts[processIndex].InstanceKey);
        }
    }

    private static SemanticProcessNode[] BuildNodes(
        ProcessGroupingFacts[] facts,
        int[] orderedIndexes,
        Dictionary<ProcessInstanceKey, int> indexByInstance,
        int[] liveParentIndexes,
        bool[] isInfrastructure,
        SemanticProcessGroupKey?[] groupKeys)
    {
        SemanticProcessNode[] nodes = new SemanticProcessNode[facts.Length];
        for (int orderedIndex = 0; orderedIndex < orderedIndexes.Length; orderedIndex++)
        {
            int processIndex = orderedIndexes[orderedIndex];
            ProcessGroupingFacts process = facts[processIndex];
            SemanticProcessGroupKey groupKey = groupKeys[processIndex]
                                               ?? throw new InvalidOperationException(
                                                   "Semantic grouping left a process unassigned.");
            ProcessInstanceKey? parentInstanceKey = null;
            SemanticProcessParentReason parentReason = SemanticProcessParentReason.NoParent;

            if (process.ExplicitOwnerInstanceKey is { } ownerInstanceKey
                && indexByInstance.TryGetValue(ownerInstanceKey, out int ownerIndex)
                && groupKeys[ownerIndex] == groupKey
                && !isInfrastructure[ownerIndex]
                && !isInfrastructure[processIndex])
            {
                parentInstanceKey = ownerInstanceKey;
                parentReason = SemanticProcessParentReason.ExplicitOwnership;
            }
            else
            {
                int parentIndex = liveParentIndexes[processIndex];
                if (parentIndex >= 0
                    && groupKeys[parentIndex] == groupKey
                    && CanInheritGroup(
                        process,
                        facts[parentIndex],
                        isInfrastructure[processIndex],
                        isInfrastructure[parentIndex]))
                {
                    parentInstanceKey = facts[parentIndex].InstanceKey;
                    parentReason = SemanticProcessParentReason.DirectAncestry;
                }
                else if (groupKey.Kind == SemanticProcessGroupKind.AncestryRoot
                         && groupKey.AnchorInstanceKey != process.InstanceKey)
                    parentReason = SemanticProcessParentReason.RetainedAncestry;
            }

            nodes[processIndex] = new SemanticProcessNode(
                process,
                groupKey,
                parentInstanceKey,
                parentReason);
        }

        return nodes;
    }

    private static SemanticProcessGroup[] MaterializeGroups(
        SemanticProcessNode[] nodes,
        Dictionary<ProcessInstanceKey, int> indexByInstance,
        int[] orderedIndexes)
    {
        Dictionary<SemanticProcessGroupKey, List<SemanticProcessNode>> nodesByGroup = [];
        List<SemanticProcessGroupKey> groupOrder = [];
        int[] descendantCounts = new int[nodes.Length];
        Array.Fill(descendantCounts, value: 1);
        for (int orderedIndex = orderedIndexes.Length - 1; orderedIndex >= 0; orderedIndex--)
        {
            int nodeIndex = orderedIndexes[orderedIndex];
            SemanticProcessNode node = nodes[nodeIndex];
            if (node.ParentInstanceKey is not { } parentInstanceKey
                || !indexByInstance.TryGetValue(parentInstanceKey, out int parentIndex))
                continue;
            descendantCounts[parentIndex] = SaturatingAdd(
                descendantCounts[parentIndex],
                descendantCounts[nodeIndex]);
        }

        for (int orderedIndex = 0; orderedIndex < orderedIndexes.Length; orderedIndex++)
        {
            int nodeIndex = orderedIndexes[orderedIndex];
            SemanticProcessNode node = nodes[nodeIndex];
            if (!nodesByGroup.TryGetValue(node.GroupKey, out List<SemanticProcessNode>? groupNodes))
            {
                groupNodes = [];
                nodesByGroup.Add(node.GroupKey, groupNodes);
                groupOrder.Add(node.GroupKey);
            }

            groupNodes.Add(node);
        }

        SemanticProcessGroup[] groups = new SemanticProcessGroup[groupOrder.Count];
        for (int groupIndex = 0; groupIndex < groupOrder.Count; groupIndex++)
        {
            SemanticProcessGroupKey groupKey = groupOrder[groupIndex];
            SemanticProcessNode[] groupNodes = [.. nodesByGroup[groupKey]];
            List<ProcessInstanceKey> roots = [];
            for (int nodeIndex = 0; nodeIndex < groupNodes.Length; nodeIndex++)
            {
                if (!groupNodes[nodeIndex].ParentInstanceKey.HasValue)
                    roots.Add(groupNodes[nodeIndex].Facts.InstanceKey);
            }

            ProcessInstanceKey representative = ChooseRepresentative(
                groupKey,
                groupNodes,
                descendantCounts,
                indexByInstance);
            groups[groupIndex] = new SemanticProcessGroup
            {
                Key = groupKey,
                Nodes = groupNodes,
                RootInstanceKeys = [.. roots],
                RepresentativeInstanceKey = representative,
                Classification = ClassifyGroup(groupNodes)
            };
        }

        return groups;
    }

    private static ProcessInstanceKey ChooseRepresentative(
        SemanticProcessGroupKey groupKey,
        SemanticProcessNode[] nodes,
        int[] descendantCounts,
        Dictionary<ProcessInstanceKey, int> indexByInstance)
    {
        if (groupKey.Kind == SemanticProcessGroupKind.ExplicitOwner)
        {
            for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
            {
                if (nodes[nodeIndex].Facts.InstanceKey == groupKey.AnchorInstanceKey)
                    return groupKey.AnchorInstanceKey;
            }
        }

        int representativeIndex = FindOldestCandidate(nodes, static node =>
            node.Facts.IndependentWindowState == ProcessIndependentWindowState.Qualifying);
        if (representativeIndex >= 0) return nodes[representativeIndex].Facts.InstanceKey;

        int bestRootIndex = -1;
        int bestDescendantCount = -1;
        for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
        {
            SemanticProcessNode node = nodes[nodeIndex];
            if (node.ParentInstanceKey.HasValue
                || !indexByInstance.TryGetValue(node.Facts.InstanceKey, out int globalIndex))
                continue;
            int descendantCount = descendantCounts[globalIndex];
            if (descendantCount > bestDescendantCount
                || descendantCount == bestDescendantCount
                && IsPreferredRepresentative(node, nodes[bestRootIndex]))
            {
                bestRootIndex = nodeIndex;
                bestDescendantCount = descendantCount;
            }
        }

        if (bestRootIndex >= 0) return nodes[bestRootIndex].Facts.InstanceKey;

        representativeIndex = FindOldestCandidate(nodes, static node =>
            !SemanticProcessInfrastructurePolicy.IsBrokerOrHost(node.Facts.ExecutableName));
        return nodes[representativeIndex >= 0 ? representativeIndex : 0].Facts.InstanceKey;
    }

    private static int FindOldestCandidate(
        SemanticProcessNode[] nodes,
        Func<SemanticProcessNode, bool> predicate)
    {
        int bestIndex = -1;
        for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
        {
            if (!predicate(nodes[nodeIndex])) continue;
            if (bestIndex < 0 || CompareFacts(nodes[nodeIndex].Facts, nodes[bestIndex].Facts) < 0)
                bestIndex = nodeIndex;
        }

        return bestIndex;
    }

    private static bool IsPreferredRepresentative(
        SemanticProcessNode candidate,
        SemanticProcessNode current)
    {
        bool candidateIsHost = SemanticProcessInfrastructurePolicy.IsBrokerOrHost(
            candidate.Facts.ExecutableName);
        bool currentIsHost = SemanticProcessInfrastructurePolicy.IsBrokerOrHost(
            current.Facts.ExecutableName);
        if (candidateIsHost != currentIsHost) return !candidateIsHost;
        return CompareFacts(candidate.Facts, current.Facts) < 0;
    }

    private static SemanticProcessGroupClassification ClassifyGroup(SemanticProcessNode[] nodes)
    {
        for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
        {
            if (SemanticProcessInfrastructurePolicy.IsIsolatedInfrastructure(nodes[nodeIndex].Facts))
                return SemanticProcessGroupClassification.Windows;
        }

        for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
        {
            if (nodes[nodeIndex].Facts.IndependentWindowState == ProcessIndependentWindowState.Qualifying)
                return SemanticProcessGroupClassification.App;
        }

        return SemanticProcessGroupClassification.Background;
    }

    private static bool CanInheritGroup(
        ProcessGroupingFacts child,
        ProcessGroupingFacts parent,
        bool childIsInfrastructure,
        bool parentIsInfrastructure)
    {
        if (childIsInfrastructure || parentIsInfrastructure) return false;
        if (HaveKnownSecurityScopeConflict(child, parent)) return false;
        if (HaveApplicationIdentityConflict(child, parent)) return false;
        if (HaveMatchingApplicationIdentity(child, parent)) return true;
        if (HaveMatchingExecutableIdentity(child, parent)) return true;
        return child.IndependentWindowState switch
        {
            ProcessIndependentWindowState.Unknown => false,
            ProcessIndependentWindowState.Qualifying => false,
            ProcessIndependentWindowState.None => true,
            _ => false
        };
    }

    private static bool HaveKnownSecurityScopeConflict(
        ProcessGroupingFacts left,
        ProcessGroupingFacts right) =>
        TryGetSecurityScope(left, out SemanticSecurityScopeKey leftScope)
        && TryGetSecurityScope(right, out SemanticSecurityScopeKey rightScope)
        && leftScope != rightScope;

    private static bool HaveApplicationIdentityConflict(
        ProcessGroupingFacts left,
        ProcessGroupingFacts right)
    {
        if (left.PackageFullName == null || right.PackageFullName == null) return false;
        if (!string.Equals(
                left.PackageFullName,
                right.PackageFullName,
                StringComparison.OrdinalIgnoreCase))
            return true;
        if (left.PackageFullName.Length == 0) return false;

        string? leftApplicationID = ResolveApplicationUserModelID(left);
        string? rightApplicationID = ResolveApplicationUserModelID(right);
        return leftApplicationID is { Length: > 0 }
               && rightApplicationID is { Length: > 0 }
               && !string.Equals(
                   leftApplicationID,
                   rightApplicationID,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool HaveMatchingApplicationIdentity(
        ProcessGroupingFacts left,
        ProcessGroupingFacts right) =>
        left.PackageFullName is { Length: > 0 }
        && right.PackageFullName is { Length: > 0 }
        && string.Equals(
            left.PackageFullName,
            right.PackageFullName,
            StringComparison.OrdinalIgnoreCase)
        && !HaveApplicationIdentityConflict(left, right);

    private static bool HaveMatchingExecutableIdentity(
        ProcessGroupingFacts left,
        ProcessGroupingFacts right)
    {
        if (left.ExecutablePath is { Length: > 0 } leftPath
            && right.ExecutablePath is { Length: > 0 } rightPath)
            return string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
        return left.ExecutableName.Length > 0
               && right.ExecutableName.Length > 0
               && string.Equals(
                   left.ExecutableName,
                   right.ExecutableName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanRetainGroup(
        ProcessGroupingFacts process,
        SemanticRetainedProcessState retainedProcess,
        bool isInfrastructure)
    {
        SemanticProcessGroupKey groupKey = retainedProcess.GroupKey;
        if (groupKey.Kind == SemanticProcessGroupKind.Infrastructure)
            return isInfrastructure && groupKey.AnchorInstanceKey == process.InstanceKey;
        if (isInfrastructure) return false;

        if (retainedProcess.SecurityScope is { } previousScope
            && TryGetSecurityScope(process, out SemanticSecurityScopeKey currentScope)
            && previousScope != currentScope)
            return false;
        if (retainedProcess.PackageFullName != null
            && process.PackageFullName != null
            && !string.Equals(
                retainedProcess.PackageFullName,
                process.PackageFullName,
                StringComparison.OrdinalIgnoreCase))
            return false;

        string? currentApplicationID = ResolveApplicationUserModelID(process);
        return retainedProcess.ApplicationUserModelID == null
               || currentApplicationID == null
               || string.Equals(
                   retainedProcess.ApplicationUserModelID,
                   currentApplicationID,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetSecurityScope(
        ProcessGroupingFacts process,
        out SemanticSecurityScopeKey securityScope)
    {
        if (process.UserSID is not { Length: > 0 } userSID || process.SessionID < 0)
        {
            securityScope = default;
            return false;
        }

        securityScope = new SemanticSecurityScopeKey(userSID, process.SessionID);
        return true;
    }

    private static string? ResolveApplicationUserModelID(ProcessGroupingFacts process) =>
        process.IsApplicationUserModelIDAmbiguous
            ? null
            : process.ApplicationUserModelID;

    private static int SaturatingAdd(int left, int right) =>
        right > int.MaxValue - left ? int.MaxValue : left + right;

    private readonly record struct PackageCohortKey(
        SemanticSecurityScopeKey SecurityScope,
        string PackageFullName);
}
