using FanControlTrayAppDotNET.Models;

namespace FanControlTrayAppDotNET.UI.Flyout;

/// <summary>Stages group mappings created while an unpublished flyout generation is built.</summary>
internal sealed class FanGroupVisualBuildStage
{
    private readonly Dictionary<string, FanGroup> _stagedGroups = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GroupMapSnapshot> _publicationSnapshots = [];
    private bool _isPublished;

    public int Count => _stagedGroups.Count;

    public FanGroup Resolve(
        string groupName,
        IReadOnlyDictionary<string, FanGroup> activeGroups,
        Func<string, FanGroup?> registeredLookup,
        int defaultDisplayOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
        ArgumentNullException.ThrowIfNull(activeGroups);
        ArgumentNullException.ThrowIfNull(registeredLookup);

        if (activeGroups.TryGetValue(groupName, out FanGroup? activeGroup))
            return activeGroup;
        if (_stagedGroups.TryGetValue(groupName, out FanGroup? stagedGroup))
            return stagedGroup;

        FanGroup? registeredGroup = registeredLookup(groupName);
        FanGroup group = registeredGroup ?? FanGroup.CreateUnregistered(groupName);
        if (registeredGroup == null)
            group.DisplayOrder = defaultDisplayOrder;
        _stagedGroups.Add(groupName, group);
        return group;
    }

    /// <summary>Publishes all staged mappings and restores both maps if any publication step fails.</summary>
    public void Publish(
        Dictionary<string, FanGroup> activeGroups,
        Dictionary<string, FanGroup> registry,
        Action<int, string, FanGroup>? afterGroupPublished = null)
    {
        ArgumentNullException.ThrowIfNull(activeGroups);
        ArgumentNullException.ThrowIfNull(registry);
        if (_isPublished)
            throw new InvalidOperationException("The staged fan groups are already published.");

        _publicationSnapshots.Clear();
        int publishedCount = 0;
        try
        {
            foreach ((string groupName, FanGroup group) in _stagedGroups)
            {
                bool hadActiveGroup = activeGroups.TryGetValue(groupName, out FanGroup? previousActiveGroup);
                bool hadRegisteredGroup = registry.TryGetValue(groupName, out FanGroup? previousRegisteredGroup);
                _publicationSnapshots.Add(new GroupMapSnapshot(
                    groupName,
                    hadActiveGroup,
                    previousActiveGroup,
                    hadRegisteredGroup,
                    previousRegisteredGroup));

                activeGroups[groupName] = group;
                registry[groupName] = group;
                afterGroupPublished?.Invoke(publishedCount, groupName, group);
                publishedCount++;
            }

            _isPublished = true;
        }
        catch
        {
            RestorePublication(activeGroups, registry);
            _publicationSnapshots.Clear();
            throw;
        }
    }

    /// <summary>Rolls a successful publication back while the previous visual generation is still available.</summary>
    public void Rollback(
        Dictionary<string, FanGroup> activeGroups,
        Dictionary<string, FanGroup> registry)
    {
        if (!_isPublished) return;
        RestorePublication(activeGroups, registry);
        _publicationSnapshots.Clear();
        _isPublished = false;
    }

    /// <summary>Finalizes publication and releases staging-only references.</summary>
    public void CompletePublication()
    {
        if (!_isPublished) return;

        _publicationSnapshots.Clear();
        _stagedGroups.Clear();
        _isPublished = false;
    }

    /// <summary>Releases an unpublished candidate without disturbing successfully published maps.</summary>
    public void ReleaseUnpublished()
    {
        if (_isPublished) return;
        _publicationSnapshots.Clear();
        _stagedGroups.Clear();
    }

    private void RestorePublication(
        Dictionary<string, FanGroup> activeGroups,
        Dictionary<string, FanGroup> registry)
    {
        for (int index = _publicationSnapshots.Count - 1; index >= 0; index--)
        {
            GroupMapSnapshot snapshot = _publicationSnapshots[index];
            RestoreMapEntry(
                activeGroups,
                snapshot.GroupName,
                snapshot.HadActiveGroup,
                snapshot.PreviousActiveGroup);
            RestoreMapEntry(
                registry,
                snapshot.GroupName,
                snapshot.HadRegisteredGroup,
                snapshot.PreviousRegisteredGroup);
        }
    }

    private static void RestoreMapEntry(
        Dictionary<string, FanGroup> groups,
        string groupName,
        bool hadPreviousGroup,
        FanGroup? previousGroup)
    {
        if (hadPreviousGroup && previousGroup != null)
        {
            groups[groupName] = previousGroup;
            return;
        }

        groups.Remove(groupName);
    }

    private sealed record GroupMapSnapshot(
        string GroupName,
        bool HadActiveGroup,
        FanGroup? PreviousActiveGroup,
        bool HadRegisteredGroup,
        FanGroup? PreviousRegisteredGroup);
}
