using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Settings;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class UpdateSkipFlowTests : IDisposable
{
    private const string ApplicationName = "TestTrayApp";
    private const string FocusVisiblePseudoClass = ":focus-visible";
    private const int CurrentBuild = 100;
    private const int OfferedVersion = 200;
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    private readonly TempDirectory _temp = new();

    // Created by the test or the code under test, so a test can also start from a missing folder
    private string TestDirectory => _temp.Combine("data");

    [Fact]
    public void SkipAsksForConfirmationAndUndoSkipOffersTheReleaseAgain() => AvaloniaTestHost.RunAsync(async () =>
    {
        using AboutPageHarness harness = await AboutPageHarness.CreateAsync(TestDirectory);
        Assert.True(harness.SkipButton.IsVisible);
        Assert.False(harness.UndoSkipButton.IsVisible);

        Click(harness.SkipButton);
        TrayAppDotNETConfirmationWindow confirmation = await WaitForPromptAsync(harness.Window);
        Click(FindButton(confirmation, nameof(CommonStrings.SkipDialog_Confirm)));
        await WaitUntilAsync(() => harness.UndoSkipButton.IsVisible, "Undo skip shows after the skip");

        Assert.Equal(OfferedVersion, harness.Settings.SkippedUpdateVersion);
        Assert.Null(harness.Service.AvailableUpdate);
        Assert.False(harness.SkipButton.IsVisible);
        Assert.True(harness.UndoSkipButton.IsEnabled);
        Assert.True(harness.CheckButton.IsEnabled);
        Assert.Empty(harness.Window.OwnedWindows);

        Click(harness.UndoSkipButton);
        await WaitUntilAsync(() => harness.SkipButton.IsVisible, "Skip shows again after the undo");

        Assert.Equal(expected: 0, harness.Settings.SkippedUpdateVersion);
        Assert.Equal(OfferedVersion, harness.Service.AvailableUpdate?.Version);
        Assert.False(harness.UndoSkipButton.IsVisible);
        Assert.True(harness.SkipButton.IsEnabled);
    });

    [Fact]
    public void CancellingTheSkipConfirmationKeepsTheReleaseOffered() => AvaloniaTestHost.RunAsync(async () =>
    {
        using AboutPageHarness harness = await AboutPageHarness.CreateAsync(TestDirectory);

        Click(harness.SkipButton);
        TrayAppDotNETConfirmationWindow confirmation = await WaitForPromptAsync(harness.Window);
        Click(FindButton(confirmation, nameof(CommonStrings.SettingsWindow_ConfirmOverlay_Cancel)));
        await WaitUntilAsync(() => harness.SkipButton.IsEnabled, "Skip is enabled after the cancelled confirmation");

        Assert.Equal(expected: 0, harness.Settings.SkippedUpdateVersion);
        Assert.Equal(OfferedVersion, harness.Service.AvailableUpdate?.Version);
        Assert.False(harness.UndoSkipButton.IsVisible);
        Assert.Empty(harness.Window.OwnedWindows);
    });

    // NOTE: headless windows activate asynchronously, and a late activation focuses the remembered control without a
    // navigation method, so tests that pass through a prompt assert where focus lands but not its focus ring
    [Fact]
    public void SkippingFromTheKeyboardMovesFocusToUndoSkip() => AvaloniaTestHost.RunAsync(async () =>
    {
        using AboutPageHarness harness = await AboutPageHarness.CreateAsync(TestDirectory);
        await harness.ActivateAsync();
        Assert.True(harness.SkipButton.Focus(NavigationMethod.Tab));

        Click(harness.SkipButton);
        TrayAppDotNETConfirmationWindow confirmation = await WaitForPromptAsync(harness.Window);
        Click(FindButton(confirmation, nameof(CommonStrings.SkipDialog_Confirm)));

        await WaitUntilAsync(() => harness.UndoSkipButton.IsFocused, "Undo skip takes focus from the hidden Skip");
    });

    [Fact]
    public void UndoingASkipFromTheKeyboardMovesFocusToSkip() => AvaloniaTestHost.RunAsync(async () =>
    {
        using AboutPageHarness harness = await AboutPageHarness.CreateAsync(TestDirectory);
        await harness.Service.SkipReleaseAsync(harness.Service.AvailableUpdate!);
        await WaitUntilAsync(() => harness.UndoSkipButton.IsVisible, "Undo skip shows for the skipped release");
        await harness.ActivateAsync();
        Assert.True(harness.UndoSkipButton.Focus(NavigationMethod.Tab));

        Click(harness.UndoSkipButton);
        await WaitUntilAsync(() => harness.SkipButton.IsFocused, "Skip takes focus from the hidden Undo skip");

        Assert.Contains(FocusVisiblePseudoClass, harness.SkipButton.Classes);
    });

    [Fact]
    public void DecliningTheSkipFromTheUpdatePromptReturnsToTheUpdatePrompt() => AvaloniaTestHost.RunAsync(async () =>
    {
        using AboutPageHarness harness = await AboutPageHarness.CreateAsync(TestDirectory);
        await harness.ActivateAsync();
        Assert.True(harness.InstallButton.Focus(NavigationMethod.Tab));

        Click(harness.InstallButton);
        TrayAppDotNETConfirmationWindow updatePrompt = await WaitForPromptAsync(harness.Window);
        Click(FindButton(updatePrompt, nameof(CommonStrings.UpdateDialog_SkipRelease)));
        TrayAppDotNETConfirmationWindow confirmation = await WaitForPromptAsync(harness.Window, updatePrompt);
        Click(FindButton(confirmation, nameof(CommonStrings.SettingsWindow_ConfirmOverlay_Cancel)));
        TrayAppDotNETConfirmationWindow reopenedPrompt = await WaitForPromptAsync(harness.Window, confirmation);

        Assert.NotSame(updatePrompt, reopenedPrompt);
        Assert.Equal(expected: 0, harness.Settings.SkippedUpdateVersion);
        Click(FindButton(reopenedPrompt, nameof(CommonStrings.UpdateDialog_Close)));
        await WaitUntilAsync(() => harness.InstallButton.IsFocused, "Install takes focus back after its prompt");

        Assert.Empty(harness.Window.OwnedWindows);
        Assert.True(harness.InstallButton.IsEnabled);
        Assert.Equal(OfferedVersion, harness.Service.AvailableUpdate?.Version);
        Assert.True(harness.SkipButton.IsVisible);
    });

    public void Dispose() => _temp.Dispose();

    private static void Click(SettingsButton button) =>
        button.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

    private static SettingsButton FindButton(Visual root, string text) =>
        Assert.Single(root.GetVisualDescendants().OfType<SettingsButton>(), button => button.Text == text);

    // Waits for the visible prompt the owner opened after the previous one closed
    private static async Task<TrayAppDotNETConfirmationWindow> WaitForPromptAsync(
        Window owner,
        Window? previousPrompt = null)
    {
        TrayAppDotNETConfirmationWindow? prompt = null;
        await WaitUntilAsync(
            () =>
            {
                prompt = owner.OwnedWindows
                    .OfType<TrayAppDotNETConfirmationWindow>()
                    .SingleOrDefault(window => window.IsVisible && !ReferenceEquals(window, previousPrompt));
                return prompt != null;
            },
            "a new prompt opens");
        return prompt!;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string expectation)
    {
        long deadline = Environment.TickCount64 + (long)WaitTimeout.TotalMilliseconds;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException($"Timed out waiting until {expectation}.");

            await Task.Delay(PollInterval);
        }
    }

    private sealed class AboutPageHarness : IDisposable
    {
        private AboutPageHarness(
            Window window,
            TrayAppDotNETAboutPage page,
            TestUpdateSettings settings,
            UpdateCheckService service)
        {
            Window = window;
            Page = page;
            Settings = settings;
            Service = service;
        }

        public Window Window { get; }
        public TrayAppDotNETAboutPage Page { get; }
        public TestUpdateSettings Settings { get; }
        public UpdateCheckService Service { get; }
        public SettingsButton CheckButton { get; private set; } = null!;
        public SettingsButton SkipButton { get; private set; } = null!;
        public SettingsButton UndoSkipButton { get; private set; } = null!;
        public SettingsButton InstallButton { get; private set; } = null!;

        /// <summary>Shows the About page in a window, with the update service offering one newer release.</summary>
        public static async Task<AboutPageHarness> CreateAsync(string cacheDirectory)
        {
            TestUpdateSettings settings = new();
            UpdateCheckService service = new(
                new UpdateCheckOptions
                {
                    VersionsManifestUrl = new Uri("https://updates.test/versions.xml"),
                    VersionsManifestCachePath = Path.Combine(
                        cacheDirectory,
                        GitHubReleaseUrls.VersionsManifestFileName),
                    RepositoryOwner = "test-owner",
                    RepositoryName = "test-repository",
                    ApplicationName = ApplicationName,
                    CurrentBuild = CurrentBuild,
                    UserAgent = ApplicationName + "-Updater",
                    StagingDirectory = () => cacheDirectory,
                    IsEnabled = () => settings.CheckForUpdatesEnabled,
                    PollInterval = () => TimeSpan.FromMilliseconds(settings.UpdateCheckIntervalMs),
                    GetSkippedUpdateVersion = () => settings.SkippedUpdateVersion,
                    PersistSkippedUpdateVersion = version => settings.SkippedUpdateVersion = version,
                    InvokeOnUIThread = TrayAppDotNETAvalonia.InvokeOnUIThreadAsync
                },
                new EndpointOnlyMessageHandler());
            UpdateInfo? offered = await service.CheckNowAsync();
            Assert.Equal(OfferedVersion, offered?.Version);

            Window window = new() { Width = 900, Height = 900 };
            TrayAppDotNETAboutPage page = new(new TrayAppDotNETAboutPageOptions
            {
                Palette = TestPalettes.Default(),
                ButtonRadius = new CornerRadius(4),
                CardRadius = new CornerRadius(8),
                L = static key => key,
                Save = static () => { },
                ApplicationName = ApplicationName,
                Tagline = string.Empty,
                BuildNumber = CurrentBuild,
                CommitHash = string.Empty,
                Publisher = string.Empty,
                HelpLink = "https://example.test/",
                SettingsFolderPath = cacheDirectory,
                UpdateSettings = settings,
                UpdateService = () => service,
                PromptOwner = () => window,
                Shutdown = static () => { }
            });
            window.Content = page.Build();
            window.Show();

            AboutPageHarness harness = new(window, page, settings, service)
            {
                CheckButton = FindButton(window, nameof(CommonStrings.Settings_About_CheckForUpdates_Button)),
                SkipButton = FindButton(window, nameof(CommonStrings.Settings_About_SkipUpdate_Button)),
                UndoSkipButton = FindButton(window, nameof(CommonStrings.Settings_About_UndoSkip_Button)),
                InstallButton = FindButton(window, nameof(CommonStrings.Settings_About_InstallUpdate_Available))
            };
            return harness;
        }

        public async Task ActivateAsync()
        {
            Window.Activate();
            await WaitUntilAsync(() => Window.IsActive, "the window activates");
        }

        public void Dispose()
        {
            Window.Close();
            Page.Dispose();
            Service.Dispose();
        }
    }

    private sealed class TestUpdateSettings : ITrayAppDotNETUpdateSettings
    {
        public bool CheckForUpdatesEnabled { get; set; } = true;
        public bool ShowUpdateNotificationsEnabled { get; set; }
        public bool ShowUpdateButtonInFlyout { get; set; } = true;
        public int UpdateCheckIntervalMs { get; set; } = 3_600_000;
        public int SkippedUpdateVersion { get; set; }
    }

    // Answers only the version endpoint, so the backdate card's previous release lookup simply fails
    private sealed class EndpointOnlyMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.RequestUri?.Host != "updates.test")
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request });
            }

            string manifest = $"""
                               <?xml version="1.0" encoding="utf-8"?>
                               <versions>
                                 <release tag="TrayAppDotNET_{OfferedVersion}" name="Release {OfferedVersion}" />
                                 <artifacts>
                                   <artifact profile="release" kind="app" appId="{ApplicationName}"
                                             version="{OfferedVersion}"
                                             fileName="{ApplicationName}_{OfferedVersion}.zip" size="0" />
                                 </artifacts>
                               </versions>
                               """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(manifest, Encoding.UTF8, mediaType: "application/xml"),
                RequestMessage = request
            });
        }
    }
}
