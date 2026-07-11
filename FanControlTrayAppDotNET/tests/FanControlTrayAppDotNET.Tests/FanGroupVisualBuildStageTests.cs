using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.UI.Flyout;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class FanGroupVisualBuildStageTests
{
    [Fact]
    public void ResolveKeepsNewGroupOutOfActiveAndProcessRegistries()
    {
        string groupName = $"Staged_{Guid.NewGuid():N}";
        Dictionary<string, FanGroup> activeGroups = new(StringComparer.OrdinalIgnoreCase);
        FanGroupVisualBuildStage stage = new();

        FanGroup group = stage.Resolve(groupName, activeGroups, FanGroup.Find, 7);

        Assert.Equal(groupName, group.Name);
        Assert.Equal(7, group.DisplayOrder);
        Assert.Empty(activeGroups);
        Assert.Null(FanGroup.Find(groupName));
    }

    [Fact]
    public void PublicationFailureRollsBothMapsBack()
    {
        string firstName = $"First_{Guid.NewGuid():N}";
        string secondName = $"Second_{Guid.NewGuid():N}";
        Dictionary<string, FanGroup> activeGroups = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, FanGroup> registry = new(StringComparer.OrdinalIgnoreCase);
        FanGroupVisualBuildStage stage = new();
        _ = stage.Resolve(firstName, activeGroups, registry.GetValueOrDefault, 0);
        _ = stage.Resolve(secondName, activeGroups, registry.GetValueOrDefault, 1);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            stage.Publish(
                activeGroups,
                registry,
                static (index, _, _) =>
                {
                    if (index == 0)
                        throw new InvalidOperationException("publish failure");
                }));

        Assert.Equal("publish failure", exception.Message);
        Assert.Empty(activeGroups);
        Assert.Empty(registry);

        stage.Publish(activeGroups, registry);
        Assert.Equal(2, activeGroups.Count);
        Assert.Equal(2, registry.Count);
    }

    [Fact]
    public void ExplicitRollbackRestoresPrepublicationMaps()
    {
        string groupName = $"Rollback_{Guid.NewGuid():N}";
        Dictionary<string, FanGroup> activeGroups = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, FanGroup> registry = new(StringComparer.OrdinalIgnoreCase);
        FanGroupVisualBuildStage stage = new();
        FanGroup stagedGroup = stage.Resolve(groupName, activeGroups, registry.GetValueOrDefault, 3);
        FanGroup previousActiveGroup = FanGroup.CreateUnregistered(groupName);
        FanGroup previousRegisteredGroup = FanGroup.CreateUnregistered(groupName);
        activeGroups[groupName] = previousActiveGroup;
        registry[groupName] = previousRegisteredGroup;

        stage.Publish(activeGroups, registry);
        Assert.Same(stagedGroup, activeGroups[groupName]);
        Assert.Same(stagedGroup, registry[groupName]);

        stage.Rollback(activeGroups, registry);

        Assert.Same(previousActiveGroup, activeGroups[groupName]);
        Assert.Same(previousRegisteredGroup, registry[groupName]);
    }
}
