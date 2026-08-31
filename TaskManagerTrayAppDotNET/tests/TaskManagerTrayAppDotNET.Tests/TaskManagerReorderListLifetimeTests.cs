#if DEBUG
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
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
    public async Task FailedInitialRowBuildDoesNotSubscribeToStaticHotReloadEvents()
    {
        using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                int reorderSubscriberCount = GetStaticEventSubscriberCount(
                    typeof(TaskManagerReorderResources),
                    nameof(TaskManagerReorderResources.ResourcesReloaded));
                int glyphSubscriberCount = GetStaticEventSubscriberCount(
                    typeof(GlyphCatalogHotReload),
                    nameof(GlyphCatalogHotReload.ResourcesReloaded));
                List<ReorderItem> items = [new ReorderItem("Item")];

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                    _ = new TaskManagerReorderList<ReorderItem>(
                        items,
                        static item => item.Name,
                        static item => throw new InvalidOperationException("expected builder failure"),
                        CreatePalette(),
                        enableRoundedCorners: true));

                Assert.Equal("expected builder failure", exception.Message);
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
            },
            CancellationToken.None);
    }

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

    private static SettingsPalette CreatePalette() => new(
        Colors.Black,
        Colors.White,
        Colors.Gray,
        Colors.DarkGray,
        Colors.DimGray,
        Colors.Black,
        Colors.DarkGray,
        Colors.LightGray,
        Colors.Gray,
        Colors.Blue,
        Colors.Blue,
        Colors.White,
        Colors.DarkBlue,
        Colors.Blue,
        Colors.DarkBlue,
        Colors.Blue,
        Colors.Gray,
        Colors.White,
        Colors.Red,
        Colors.DarkRed,
        Colors.White);

    private sealed class ReorderItem(string name)
    {
        public string Name { get; } = name;
    }

    private sealed class TestApplication : Application
    {
    }

    private static class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder
            .Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
#endif
