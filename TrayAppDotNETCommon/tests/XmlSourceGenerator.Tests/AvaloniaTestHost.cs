using Avalonia;
using Avalonia.Headless;
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

    public sealed class TestApplication : Application;

    public static class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder
            .Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
