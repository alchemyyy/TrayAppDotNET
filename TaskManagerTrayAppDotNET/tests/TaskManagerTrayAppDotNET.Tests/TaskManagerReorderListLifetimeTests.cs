#if DEBUG
using System.Reflection;
using Avalonia;
using TaskManagerTrayAppDotNET.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.Visuals;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class TaskManagerReorderListLifetimeTestCollection
{
    public const string CollectionName = "Task Manager reorder-list lifetime tests";
}

[Collection(TaskManagerReorderListLifetimeTestCollection.CollectionName)]
public sealed class TaskManagerReorderListLifetimeTests
{
    [Fact]
    public void FailedInitialRowBuildDoesNotSubscribeToStaticHotReloadEvents() => AvaloniaTestHost.Run(static () =>
    {
        int reorderSubscriberCount = GetStaticEventSubscriberCount(
            typeof(TaskManagerReorderResources),
            nameof(TaskManagerReorderResources.ResourcesReloaded));
        int glyphSubscriberCount = GetStaticEventSubscriberCount(
            typeof(GlyphCatalogHotReload),
            nameof(GlyphCatalogHotReload.ResourcesReloaded));
        List<ReorderItem> items = [new("Item")];

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            _ = new TaskManagerReorderList<ReorderItem>(
                items,
                static item => item.Name,
                static item => throw new InvalidOperationException("expected builder failure"),
                TestPalettes.Default(),
                enableRoundedCorners: true));

        Assert.Equal(expected: "expected builder failure", exception.Message);
        Assert.Equal(
            reorderSubscriberCount,
            GetStaticEventSubscriberCount(
                typeof(TaskManagerReorderResources),
                nameof(TaskManagerReorderResources.ResourcesReloaded)));
        Assert.Equal(
            glyphSubscriberCount,
            GetStaticEventSubscriberCount(
                typeof(GlyphCatalogHotReload),
                nameof(GlyphCatalogHotReload.ResourcesReloaded)));
    });

    private static int GetStaticEventSubscriberCount(Type ownerType, string eventName)
    {
        FieldInfo eventField = ownerType.GetField(
                                   eventName,
                                   BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                               ?? throw new InvalidOperationException(
                                   $"Static event backing field '{ownerType.FullName}.{eventName}' was not found.");
        Delegate? handlers = eventField.GetValue(null) as Delegate;
        return handlers?.GetInvocationList().Length ?? 0;
    }

    private sealed class ReorderItem(string name)
    {
        public string Name { get; } = name;
    }
}
#endif
