using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

internal static class AvaloniaTestHost
{
    public static void Run(Action test)
    {
        using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        session.Dispatch(test, CancellationToken.None).GetAwaiter().GetResult();
    }

    public static void RunAsync(Func<Task> test)
    {
        using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        session.Dispatch(
            async () =>
            {
                await test();
                return true;
            },
            CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Runs a test under the Fluent theme the apps use. Themed windows get their template, whose layer manager hosts
    /// popups in an overlay, since the headless platform has no popup windows.
    /// </summary>
    public static void RunWithFluentTheme(Action test)
    {
        using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(FluentTestAppBuilder));
        session.Dispatch(test, CancellationToken.None).GetAwaiter().GetResult();
    }

    public sealed class TestApplication : Application;

    public static class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder
            .Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }

    public sealed class FluentTestApplication : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }

    public static class FluentTestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder
            .Configure<FluentTestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
