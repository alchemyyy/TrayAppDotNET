using Avalonia.Threading;
using SkiaSharp;
using TrayAppDotNETCommon.UI.Tray;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class TrayIconPresenterTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void AppIconStandsInUntilTheFirstRenderLands() => RunWithPresenter(async context =>
    {
        context.Renderer.Gate.Reset();
        context.Presenter.Refresh("Battery 80%");

        Assert.Equal(expected: "Battery 80%", context.TrayIcon.TooltipText);
        Assert.Equal(expected: 1, context.FallbackCount);
        Assert.Same(context.LastFallback, context.TrayIcon.CurrentIcon);

        Assert.True(await context.Renderer.Started.WaitAsync(Timeout));
        context.Renderer.Gate.Set();
        await WaitUntilAsync(() => context.Renderer.InputOf(context.TrayIcon.CurrentIcon) == "A");
        Assert.Equal(expected: 1, context.FallbackCount);
    });

    [Fact]
    public void AStateThatReturnsWhileItsOldRenderRunsIsRenderedAgain() => RunWithPresenter(async context =>
    {
        context.Renderer.Gate.Reset();
        context.Presenter.Refresh("A");
        Assert.True(await context.Renderer.Started.WaitAsync(Timeout));

        // B replaces the running render of A, so A coming back must be requested again rather than deduplicated
        context.Renderer.State = "B";
        context.Presenter.Refresh("B");
        context.Renderer.State = "A";
        context.Presenter.Refresh("A");
        context.Renderer.Gate.Set();

        await WaitUntilAsync(() => context.Renderer.InputOf(context.TrayIcon.CurrentIcon) == "A");
        await SettleAsync();
        Assert.Equal(expected: "A", context.Renderer.InputOf(context.TrayIcon.CurrentIcon));
        Assert.Equal(expected: "A", context.Renderer.LastInput);
    });

    [Fact]
    public void AFinishedRenderKeepsTheNewestTooltip() => RunWithPresenter(async context =>
    {
        context.Renderer.Gate.Reset();
        context.Presenter.Refresh("Old tooltip");
        Assert.True(await context.Renderer.Started.WaitAsync(Timeout));

        // Same render input, new text: the tooltip changes at once and the icon is not rendered again
        context.Presenter.Refresh("New tooltip");
        context.Renderer.Gate.Set();

        await WaitUntilAsync(() => context.Renderer.InputOf(context.TrayIcon.CurrentIcon) == "A");
        await SettleAsync();
        Assert.Equal(expected: "New tooltip", context.TrayIcon.TooltipText);
        Assert.Equal(expected: 1, context.Renderer.CallCount);
    });

    [Fact]
    public void AnEqualInputAtTheSameIconSizeIsNotRenderedAgain() => RunWithPresenter(async context =>
    {
        context.Presenter.Refresh("A");
        await WaitUntilAsync(() => context.Renderer.InputOf(context.TrayIcon.CurrentIcon) == "A");

        context.Presenter.Refresh("A");
        await SettleAsync();
        Assert.Equal(expected: 1, context.Renderer.CallCount);

        context.IconSize = 24;
        context.Presenter.Refresh("A");
        await WaitUntilAsync(() => context.Renderer.CallCount == 2);
        Assert.Equal(expected: 24, context.Renderer.LastIconSize);
    });

    [Fact]
    public void WithoutDedupeEveryRefreshRenders() => RunWithPresenter(async context =>
    {
        context.Presenter.Refresh("A");
        await WaitUntilAsync(() => context.Renderer.InputOf(context.TrayIcon.CurrentIcon) == "A");
        context.Presenter.Refresh("A");
        await WaitUntilAsync(() => context.Renderer.CallCount == 2);
    }, dedupe: false);

    [Fact]
    public void AFailedRenderShowsTheAppIconAndDoesNotSatisfyTheDedupe() => RunWithPresenter(async context =>
    {
        context.Renderer.State = GatedRenderer.FailingState;
        context.Presenter.Refresh("A");
        await WaitUntilAsync(() => context.FallbackCount == 2);
        await SettleAsync();
        Assert.Same(context.LastFallback, context.TrayIcon.CurrentIcon);

        context.Presenter.Refresh("A");
        await WaitUntilAsync(() => context.Renderer.CallCount == 2);
    });

    [Fact]
    public void WithoutARendererTheAppIconIsAppliedOnce() => AvaloniaTestHost.Run(() =>
    {
        int fallbackCount = 0;
        using TrayAppDotNETShellTrayIcon trayIcon = CreateTrayIcon();
        using TrayIconPresenter<string> presenter = new(
            trayIcon,
            renderer: null,
            () =>
            {
                fallbackCount++;
                return CreateIcon(16);
            });

        presenter.Refresh("First");
        presenter.Refresh("Second");

        Assert.Equal(expected: 1, fallbackCount);
        Assert.NotNull(trayIcon.CurrentIcon);
        Assert.Equal(expected: "Second", trayIcon.TooltipText);
    });

    private static void RunWithPresenter(Func<PresenterContext, Task> test, bool dedupe = true) =>
        AvaloniaTestHost.RunAsync(async () =>
        {
            using TrayAppDotNETShellTrayIcon trayIcon = CreateTrayIcon();
            GatedRenderer renderer = new();
            PresenterContext context = new(trayIcon, renderer);
            using TrayIconPresenter<string> presenter = new(
                trayIcon,
                renderer,
                () =>
                {
                    NativeIcon icon = CreateIcon(16);
                    context.AddFallback(icon);
                    return icon;
                },
                new TrayIconPresenterOptions
                {
                    Dedupe = dedupe,
                    IconSize = () => context.IconSize,
                    Log = static _ => { }
                });
            context.Presenter = presenter;
            try
            {
                await test(context);
            }
            finally
            {
                renderer.Gate.Set();
            }
        });

    // Never made visible, so the shell is not asked to show it
    private static TrayAppDotNETShellTrayIcon CreateTrayIcon() =>
        new(Guid.NewGuid().ToString(), nameof(TrayIconPresenterTests));

    private static NativeIcon CreateIcon(int size) =>
        TrayIconRasterizer.Render(size, state: 0, static (canvas, _, _) => canvas.Clear(SKColors.Red));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The tray icon did not reach the expected state.");
            await Task.Delay(millisecondsDelay: 5);
            Dispatcher.UIThread.RunJobs();
        }
    }

    // Lets a render that should not happen get the chance to land
    private static async Task SettleAsync()
    {
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(millisecondsDelay: 10);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed class PresenterContext(TrayAppDotNETShellTrayIcon trayIcon, GatedRenderer renderer)
    {
        public TrayAppDotNETShellTrayIcon TrayIcon { get; } = trayIcon;
        public GatedRenderer Renderer { get; } = renderer;
        private readonly Lock _lock = new();
        private int _fallbackCount;
        private NativeIcon? _lastFallback;

        // The presenter asks for the app icon on the UI thread and, after a failed render, on the render thread
        public int FallbackCount
        {
            get
            {
                lock (_lock) return _fallbackCount;
            }
        }

        public NativeIcon? LastFallback
        {
            get
            {
                lock (_lock) return _lastFallback;
            }
        }

        public TrayIconPresenter<string> Presenter { get; set; } = null!;
        public int IconSize { get; set; } = 16;

        public void AddFallback(NativeIcon icon)
        {
            lock (_lock)
            {
                _fallbackCount++;
                _lastFallback = icon;
            }
        }
    }

    // Renders its State; the gate holds a render on the render thread so a test can act while it runs
    private sealed class GatedRenderer : ITrayIconRenderer<string>
    {
        public const string FailingState = "Fails";

        private readonly Lock _lock = new();
        private readonly Dictionary<IntPtr, string> _renderedInputs = [];
        private int _callCount;
        private string? _lastInput;
        private int _lastIconSize;

        public string State { get; set; } = "A";
        public ManualResetEventSlim Gate { get; } = new(initialState: true);
        public SemaphoreSlim Started { get; } = new(initialCount: 0);

        public int CallCount
        {
            get
            {
                lock (_lock) return _callCount;
            }
        }

        public string? LastInput
        {
            get
            {
                lock (_lock) return _lastInput;
            }
        }

        public int LastIconSize
        {
            get
            {
                lock (_lock) return _lastIconSize;
            }
        }

        public string CreateRenderInput() => State;

        public NativeIcon RenderIcon(string input, int iconSize)
        {
            lock (_lock)
            {
                _callCount++;
                _lastInput = input;
                _lastIconSize = iconSize;
            }

            Started.Release();
            Gate.Wait(Timeout);
            if (input == FailingState) throw new InvalidOperationException("Render failed.");

            NativeIcon icon = CreateIcon(iconSize);
            lock (_lock) _renderedInputs[icon.Handle] = input;
            return icon;
        }

        public string? InputOf(NativeIcon? icon)
        {
            if (icon == null) return null;
            lock (_lock) return _renderedInputs.GetValueOrDefault(icon.Handle);
        }

        public void Dispose() => Gate.Set();
    }
}
