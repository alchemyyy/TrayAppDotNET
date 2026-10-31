using Avalonia.Threading;
using TrayAppDotNETCommon.UI.ContextMenus;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Tray;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class TrayMenuControllerTests
{
    private const string TestMapName = "TrayMenuControllerTests";

    private static readonly ControlMapNodeID Surface = new(TestMapName, "TrayMenu");
    private static readonly ControlMapNodeID Exit = new(TestMapName, "TrayMenu.Exit");

    [Fact]
    public void InvalidatingAHiddenWarmMenuReplacesItAtOnce() => RunWithController(async (controller, menus) =>
    {
        await controller.PrimeAsync();
        TrayMenuWindow first = Assert.IsType<TrayMenuWindow>(controller.Current);
        Assert.False(first.IsVisible);

        controller.Invalidate();
        Assert.Null(controller.Current);

        await WaitUntilAsync(() => controller.Current is { } current && !ReferenceEquals(current, first));
        Assert.Equal(expected: 2, menus.Count);
    });

    [Fact]
    public void InvalidatingAnOpenMenuReplacesItOnceItIsDismissed() => RunWithController(async (controller, menus) =>
    {
        await controller.PrimeAsync();
        TrayMenuWindow first = Assert.IsType<TrayMenuWindow>(controller.Current);
        first.Show();

        controller.Invalidate();
        Assert.Same(first, controller.Current);
        Assert.True(first.IsVisible);

        first.DismissForWarmCache();
        await WaitUntilAsync(() => controller.Current is { } current && !ReferenceEquals(current, first));
        Assert.Equal(expected: 2, menus.Count);
    });

    [Fact]
    public void ASettingsChangeReplacesTheWarmMenu() => RunWithController(async (controller, menus, settings) =>
    {
        await controller.PrimeAsync();
        TrayMenuWindow first = Assert.IsType<TrayMenuWindow>(controller.Current);

        settings.RaiseChanged();
        await WaitUntilAsync(() => controller.Current is { } current && !ReferenceEquals(current, first));
        Assert.Equal(expected: 2, menus.Count);
    });

    private static void RunWithController(Func<TrayMenuController, List<TrayMenuWindow>, Task> test) =>
        RunWithController((controller, menus, _) => test(controller, menus));

    private static void RunWithController(
        Func<TrayMenuController, List<TrayMenuWindow>, AppShellLifecycleTests.HostTestSettings, Task> test) =>
        AvaloniaTestHost.RunAsync(async () =>
        {
            ControlMapCatalog.Register(new TrayMenuTestMap());
            try
            {
                AppShellLifecycleTests.HostTestSettings settings = new();
                List<TrayMenuWindow> menus = [];
                using TrayMenuController controller = new(
                    () =>
                    {
                        TrayMenuWindow menu = new(
                            new ContextMenuEntryBuilder(),
                            TrayMenuWindowOptions.Create(
                                settings,
                                theme: null,
                                Surface,
                                settingsEntry: null,
                                new TrayMenuStandardEntry("Exit", static () => { }, Exit)));
                        menus.Add(menu);
                        return menu;
                    },
                    static () => true,
                    settings,
                    theme: null,
                    static _ => { });
                await test(controller, menus, settings);
            }
            finally
            {
                ControlMapCatalog.Unregister(TestMapName);
            }
        });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The tray menu was not replaced.");
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle);
            await Task.Delay(millisecondsDelay: 5);
        }
    }

    // An app map whose tray menu surface instantiates the common ContextMenu template, with the standard Exit row
    private sealed class TrayMenuTestMap : UI.ControlMapping.ControlMap
    {
        public TrayMenuTestMap() : base(TestMapName)
        {
            Surface surface = new() { ID = "TrayMenu", Kind = SurfaceKind.Popup, Template = "ContextMenu" };
            surface.Children.Add(new Leaf { ID = "Exit", Kind = LeafKind.ListItem });
            Children.Add(surface);
        }
    }
}
