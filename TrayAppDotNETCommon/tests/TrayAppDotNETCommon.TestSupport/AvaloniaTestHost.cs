using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Xunit;

namespace TrayAppDotNETCommon.TestSupport;

/// <summary>
/// Runs test code on a headless Avalonia dispatcher. Every call gets its own application instance
/// (<see cref="AvaloniaTestIsolationLevel.PerTest"/>), so objects a test creates belong to that test (see
/// .agents/HEADLESS_TEST_LIMITATIONS.md). A timeout fails a hung asynchronous test instead of blocking the run.
/// </summary>
/// <remarks>
/// The host keeps one session per theme for the life of the test process instead of one per test. Disposing an
/// Avalonia 12.1.1 <see cref="HeadlessUnitTestSession"/> races its own dispatch loop: the loop can take from the
/// queue after <c>Dispose</c> completed it (InvalidOperationException), and a session whose loop started before
/// <c>StartNew</c> stored the loop task has none to wait for (NullReferenceException). Either failure surfaced at the
/// end of a test that had passed, at random.
/// </remarks>
public static class AvaloniaTestHost
{
    private static readonly Lazy<HeadlessUnitTestSession> PlainSession =
        new(static () => HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder), AvaloniaTestIsolationLevel.PerTest));

    private static readonly Lazy<HeadlessUnitTestSession> FluentSession =
        new(static () => HeadlessUnitTestSession.StartNew(typeof(FluentTestAppBuilder), AvaloniaTestIsolationLevel.PerTest));

    /// <summary>Runs <paramref name="test"/> without a theme, the cheapest host for controls tadn draws itself.</summary>
    public static void Run(Action test, TimeSpan? timeout = null) =>
        Dispatch(PlainSession.Value, test, timeout);

    /// <summary>Runs an asynchronous test without a theme and waits for it; awaiting lets render jobs run.</summary>
    public static void RunAsync(Func<Task> test, TimeSpan? timeout = null) =>
        Dispatch(PlainSession.Value, test, timeout);

    /// <summary>
    /// Runs a test under the Fluent theme the apps use. Themed windows get their template, whose layer manager hosts
    /// popups in an overlay, since the headless platform has no popup windows.
    /// </summary>
    public static void RunWithFluentTheme(Action test, TimeSpan? timeout = null) =>
        Dispatch(FluentSession.Value, test, timeout);

    /// <summary>Runs an asynchronous test under the Fluent theme and waits for it.</summary>
    public static void RunWithFluentThemeAsync(Func<Task> test, TimeSpan? timeout = null) =>
        Dispatch(FluentSession.Value, test, timeout);

    /// <summary>
    /// Shows a window activated. The headless platform raises Activated from a posted job, and a key sent before it
    /// runs can meet a focus manager that has not settled on the window yet; running the job first makes key tests
    /// deterministic.
    /// </summary>
    public static void ShowActivated(Window window)
    {
        window.Show();
        window.UpdateLayout();
        window.Activate();
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsActive);
    }

    /// <summary>Focuses a control for keyboard input and fails here, not at the first key, when it cannot take focus.</summary>
    public static void FocusForKeys(InputElement element, NavigationMethod method = NavigationMethod.Unspecified)
    {
        Assert.True(element.Focus(method));
        Dispatcher.UIThread.RunJobs();
        Assert.True(element.IsKeyboardFocusWithin);
    }

    private static void Dispatch(HeadlessUnitTestSession session, Action test, TimeSpan? timeout)
    {
        using CancellationTokenSource? cancellation = CreateCancellation(timeout);
        session.Dispatch(test, cancellation?.Token ?? CancellationToken.None).GetAwaiter().GetResult();
    }

    private static void Dispatch(HeadlessUnitTestSession session, Func<Task> test, TimeSpan? timeout)
    {
        using CancellationTokenSource? cancellation = CreateCancellation(timeout);
        session.Dispatch(
            async () =>
            {
                await test();
                return true;
            },
            cancellation?.Token ?? CancellationToken.None).GetAwaiter().GetResult();
    }

    private static CancellationTokenSource? CreateCancellation(TimeSpan? timeout) =>
        timeout is { } limit ? new CancellationTokenSource(limit) : null;

    private sealed class TestApplication : Application;

    private static class TestAppBuilder
    {
        // Called through reflection by HeadlessUnitTestSession.StartNew
        public static AppBuilder BuildAvaloniaApp() => AppBuilder
            .Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }

    private sealed class FluentTestApplication : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }

    private static class FluentTestAppBuilder
    {
        // Called through reflection by HeadlessUnitTestSession.StartNew
        public static AppBuilder BuildAvaloniaApp() => AppBuilder
            .Configure<FluentTestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
