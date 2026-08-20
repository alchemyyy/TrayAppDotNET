using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;

namespace BrightnessTrayAppDotNET.Tests;

internal static class AvaloniaTestHost
{
    public static void Run(Action test)
    {
        using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        session.Dispatch(test, CancellationToken.None).GetAwaiter().GetResult();
    }

    public sealed class TestApplication : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }

    public static class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder
            .Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
