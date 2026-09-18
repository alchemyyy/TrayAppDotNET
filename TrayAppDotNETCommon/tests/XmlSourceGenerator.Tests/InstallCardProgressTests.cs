using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Install;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Settings;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class InstallCardProgressTests
{
    private const string ExecutablePath = @"C:\TrayAppDotNET\TestTrayAppDotNET.exe";
    private const int CurrentBuildNumber = 7;
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    [Fact]
    public void InstallShowsStageUpdatesThenHidesTheRowOnSuccess() => AvaloniaTestHost.RunAsync(async () =>
    {
        using ManualResetEventSlim releaseInstall = new(initialState: false);
        int installCallCount = 0;
        InstallCardHarness harness = InstallCardHarness.Create(progress =>
        {
            Interlocked.Increment(ref installCallCount);
            progress?.Report(TrayAppDotNETInstallProgress.At(percent: 25, message: "Copying files"));
            progress?.Report(TrayAppDotNETInstallProgress.At(percent: 60, message: "Creating shortcuts"));
            releaseInstall.Wait(WaitTimeout);
            return new TrayAppDotNETInstallResult(Success: true);
        });

        Assert.False(harness.ProgressRow.IsVisible);
        Assert.Same(TrayAppDotNETSettingsUI.Brush(harness.Palette.Accent), harness.ProgressBar.Foreground);
        Assert.Same(TrayAppDotNETSettingsUI.Brush(harness.Palette.ControlBackground), harness.ProgressBar.Background);

        harness.ClickInstall();

        Assert.True(harness.ProgressRow.IsVisible);
        Assert.True(harness.ProgressBar.IsVisible);
        Assert.False(harness.InstallButton.IsEnabled);
        Assert.False(harness.UninstallButton.IsEnabled);

        await WaitUntilAsync(() => harness.ProgressStatus.Text == "Creating shortcuts");

        Assert.Equal(expected: 60, harness.ProgressBar.Value);
        Assert.True(harness.ProgressRow.IsVisible);

        // A disabled button ignores the second click while the install is still running
        harness.ClickInstall();
        releaseInstall.Set();
        await WaitUntilAsync(() => !harness.ProgressRow.IsVisible);

        Assert.Equal(expected: 1, installCallCount);
        Assert.True(harness.InstallButton.IsEnabled);
        Assert.True(harness.UninstallButton.IsEnabled);
        Assert.Equal(string.Empty, harness.ProgressStatus.Text);
        Assert.Equal(expected: 1, harness.RefreshCount);
        Assert.Empty(harness.ShownMessages);
    });

    [Fact]
    public void InstallFailureKeepsTheMessageVisibleAndShowsTheDialog() => AvaloniaTestHost.RunAsync(async () =>
    {
        InstallCardHarness harness = InstallCardHarness.Create(progress =>
        {
            progress?.Report(TrayAppDotNETInstallProgress.At(percent: 40, message: "Copying files"));
            return new TrayAppDotNETInstallResult(Success: false, ErrorMessage: "Access denied");
        });

        harness.ClickInstall();
        await WaitUntilAsync(() => harness.InstallButton.IsEnabled);

        Assert.True(harness.ProgressRow.IsVisible);
        Assert.False(harness.ProgressBar.IsVisible);
        Assert.Equal("Access denied", harness.ProgressStatus.Text);
        Assert.True(harness.UninstallButton.IsEnabled);
        (string Title, string Message) shown = Assert.Single(harness.ShownMessages);
        Assert.Equal(nameof(CommonStrings.Settings_General_InstallFailed_Title), shown.Title);
        Assert.Equal("Access denied", shown.Message);
        Assert.Equal(expected: 1, harness.RefreshCount);

        // The next operation clears the stale failure text and brings the bar back
        harness.ClickInstall();

        Assert.True(harness.ProgressBar.IsVisible);
        Assert.Equal(string.Empty, harness.ProgressStatus.Text);
        await WaitUntilAsync(() => harness.InstallButton.IsEnabled);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UninstallStaysActiveUntilTheTerminalUpdateArrives(bool failTerminal) =>
        AvaloniaTestHost.RunAsync(async () =>
        {
            IProgress<TrayAppDotNETInstallProgress>? capturedProgress = null;
            InstallCardHarness harness = InstallCardHarness.Create(
                static _ => new TrayAppDotNETInstallResult(Success: true),
                (_, progress) =>
                {
                    capturedProgress = progress;
                    return Task.CompletedTask;
                },
                TrayAppDotNETInstallStatus.InstalledUpToDate);

            harness.ClickUninstall();

            Assert.NotNull(capturedProgress);
            Assert.True(harness.ProgressRow.IsVisible);
            Assert.False(harness.InstallButton.IsEnabled);
            Assert.False(harness.UninstallButton.IsEnabled);

            capturedProgress.Report(TrayAppDotNETInstallProgress.At(percent: 45, message: "Removing files"));
            await WaitUntilAsync(() => harness.ProgressStatus.Text == "Removing files");

            Assert.Equal(expected: 45, harness.ProgressBar.Value);
            Assert.False(harness.UninstallButton.IsEnabled);
            Assert.Equal(expected: 0, harness.RefreshCount);

            TrayAppDotNETInstallProgress terminalUpdate = failTerminal
                ? TrayAppDotNETInstallProgress.Failed("Helper exited early")
                : TrayAppDotNETInstallProgress.At(percent: 100, message: "Removed");
            capturedProgress.Report(terminalUpdate);
            await WaitUntilAsync(() => harness.UninstallButton.IsEnabled);

            Assert.True(harness.InstallButton.IsEnabled);
            Assert.False(harness.ProgressBar.IsVisible);
            Assert.Equal(failTerminal, harness.ProgressRow.IsVisible);
            Assert.Equal(failTerminal ? "Helper exited early" : string.Empty, harness.ProgressStatus.Text);
            Assert.Equal(expected: 1, harness.RefreshCount);
        });

    [Fact]
    public void UninstallWithoutAnyUpdateRestoresTheCardAfterTheSilenceTimeout() =>
        AvaloniaTestHost.RunAsync(async () =>
        {
            InstallCardHarness harness = InstallCardHarness.Create(
                static _ => new TrayAppDotNETInstallResult(Success: true),
                static (_, _) => Task.CompletedTask,
                TrayAppDotNETInstallStatus.InstalledUpToDate);

            harness.ClickUninstall();

            Assert.True(harness.ProgressRow.IsVisible);
            Assert.False(harness.UninstallButton.IsEnabled);

            await WaitUntilAsync(() => harness.UninstallButton.IsEnabled);

            Assert.False(harness.ProgressRow.IsVisible);
            Assert.True(harness.InstallButton.IsEnabled);
            Assert.Equal(expected: 1, harness.RefreshCount);
        });

    /// <summary>Yields to the dispatcher until the condition holds or the wait times out.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        long deadline = Environment.TickCount64 + (long)WaitTimeout.TotalMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("The install card did not reach the expected state in time.");

            await Task.Delay(PollInterval);
        }
    }

    private sealed class InstallCardHarness
    {
        private readonly List<(string Title, string Message)> _shownMessages = [];
        private readonly TrayAppDotNETInstallStatus _status;
        private int _refreshCount;

        private InstallCardHarness(TrayAppDotNETInstallStatus status, SettingsPalette palette)
        {
            _status = status;
            Palette = palette;
        }

        public SettingsPalette Palette { get; }
        public SettingsButton InstallButton { get; private set; } = null!;
        public SettingsButton UninstallButton { get; private set; } = null!;
        public ProgressBar ProgressBar { get; private set; } = null!;
        public StackPanel ProgressRow { get; private set; } = null!;
        public TextBlock ProgressStatus { get; private set; } = null!;
        public IReadOnlyList<(string Title, string Message)> ShownMessages => _shownMessages;
        public int RefreshCount => _refreshCount;

        /// <summary>Builds one install card whose install and uninstall delegates are supplied by the test.</summary>
        public static InstallCardHarness Create(
            Func<IProgress<TrayAppDotNETInstallProgress>?, TrayAppDotNETInstallResult> install,
            Func<Action, IProgress<TrayAppDotNETInstallProgress>?, Task>? uninstallAsync = null,
            TrayAppDotNETInstallStatus status = TrayAppDotNETInstallStatus.NotInstalled)
        {
            InstallCardHarness harness = new(status, CreatePalette());
            TrayAppDotNETGeneralSettingsSection section = new(new TrayAppDotNETGeneralSettingsSectionOptions
            {
                Palette = harness.Palette,
                ButtonRadius = new CornerRadius(4),
                CardRadius = new CornerRadius(8),
                L = static key => key,
                Save = static () => { },
                // Only the install confirmation is accepted; the restart prompt after success is declined
                ConfirmAsync = static (title, _, _, _) => Task.FromResult(
                    title is nameof(CommonStrings.Settings_General_InstallConfirm_Title)
                        or nameof(CommonStrings.Settings_General_InstallSystemWideConfirm_Title)),
                ShowMessage = (title, message) =>
                {
                    harness._shownMessages.Add((title, message));
                    return Task.CompletedTask;
                },
                GetRunOnStartup = static () => false,
                SetRunOnStartup = static _ => { },
                GetCurrentStartupShortcutTarget = static () => null,
                RetargetStartupShortcut = () => harness._refreshCount++,
                DetectInstallations = () =>
                [
                    new TrayAppDotNETInstallationInfo(
                        InstallScope.LocalAppData,
                        ExecutablePath,
                        harness._status,
                        CurrentBuildNumber)
                ],
                CurrentBuildNumber = CurrentBuildNumber,
                Shutdown = static () => { }
            });

            StackPanel stack = new();
            section.AddInstallationSection(
                stack,
                [
                    new TrayAppDotNETInstallCardOptions
                    {
                        Scope = InstallScope.LocalAppData,
                        Title = "Local user",
                        ExecutablePath = ExecutablePath,
                        Elevated = false,
                        Install = install,
                        UninstallAsync = uninstallAsync ?? (static (_, _) => Task.CompletedTask)
                    }
                ]);

            Border card = Assert.Single(stack.Children.OfType<Border>());
            SettingsButton[] buttons = card.GetVisualDescendants().OfType<SettingsButton>().ToArray();
            Assert.Equal(expected: 2, buttons.Length);
            harness.InstallButton = buttons[0];
            harness.UninstallButton = buttons[1];
            harness.ProgressBar = Assert.Single(card.GetVisualDescendants().OfType<ProgressBar>());
            harness.ProgressRow = Assert.IsType<StackPanel>(harness.ProgressBar.GetVisualParent());
            harness.ProgressStatus = Assert.Single(harness.ProgressRow.Children.OfType<TextBlock>());
            return harness;
        }

        public void ClickInstall() => Click(InstallButton);

        public void ClickUninstall() => Click(UninstallButton);

        private static void Click(SettingsButton button) =>
            button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

        private static SettingsPalette CreatePalette() =>
            new(
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
    }
}
