using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Settings;
using TrayAppDotNETCommon.Visuals;
using TrayAppDotNETCommon.Localization;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class SettingsWindowSharedBehaviorTests
{
    [Fact]
    public void MessageShowsOneButtonAndEscapeClosesIt() => AvaloniaTestHost.Run(() =>
    {
        SharedBehaviorWindow window = new(new AppTheme());
        try
        {
            AvaloniaTestHost.ShowActivated(window);
            Task message = window.ShowTestMessage();

            string ok = LocalizationManager.Instance[nameof(CommonStrings.SettingsWindow_ConfirmOverlay_OK)];
            List<SettingsButton> overlayButtons = [.. window.GetVisualDescendants()
                .OfType<SettingsButton>()
                .Where(button => button.IsEffectivelyVisible && (button.Text == ok || button.Text.Length == 0))];
            Assert.Single(overlayButtons);
            Assert.False(message.IsCompleted);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, keySymbol: null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, keySymbol: null);

            Assert.True(message.IsCompleted);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void ConfirmAfterCloseCompletesWithFalse() => AvaloniaTestHost.Run(() =>
    {
        SharedBehaviorWindow window = new(new AppTheme());
        window.Show();
        window.Close();

        Task<bool> confirm = window.ConfirmAsync("Title", "Message", "Install", "Cancel");

        Assert.True(confirm.IsCompletedSuccessfully);
        Assert.False(confirm.Result);
    });

    [Fact]
    public void OpenWindowFollowsTheWindowsLightDarkSwitch() => AvaloniaTestHost.Run(() =>
    {
        AppTheme theme = new();
        SharedBehaviorWindow window = new(theme);
        try
        {
            window.Show();
            string before = window.CurrentBackground;

            theme.ApplySystemLightTheme(!theme.IsLightTheme);
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(before, window.CurrentBackground);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void LightDarkSwitchWaitsUntilTheFocusedEditorLetsGo() => AvaloniaTestHost.RunAsync(async () =>
    {
        AppTheme theme = new();
        SharedBehaviorWindow window = new(theme);
        try
        {
            AvaloniaTestHost.ShowActivated(window);
            TextBox editor = window.Editor;
            AvaloniaTestHost.FocusForKeys(editor);
            window.KeyTextInput("half");
            string before = window.CurrentBackground;

            theme.ApplySystemLightTheme(!theme.IsLightTheme);
            Dispatcher.UIThread.RunJobs();

            // The editor, its text and its caret survive while it has focus
            Assert.True(window.IsThemeRebuildDeferred);
            Assert.Equal(before, window.CurrentBackground);
            Assert.Same(editor, window.Editor);
            Assert.Same(editor, window.FocusManager?.GetFocusedElement());
            Assert.Equal(expected: "half", editor.Text);

            Assert.True(window.LeaveButton.Focus());
            await PumpUntilAsync(() => !window.IsThemeRebuildDeferred);

            Assert.NotEqual(before, window.CurrentBackground);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void LightDarkSwitchWaitsForAnOpenConfirmation() => AvaloniaTestHost.RunAsync(async () =>
    {
        AppTheme theme = new();
        SharedBehaviorWindow window = new(theme);
        try
        {
            AvaloniaTestHost.ShowActivated(window);
            Task message = window.ShowTestMessage();
            string before = window.CurrentBackground;

            theme.ApplySystemLightTheme(!theme.IsLightTheme);
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.IsThemeRebuildDeferred);
            Assert.Equal(before, window.CurrentBackground);
            Assert.False(message.IsCompleted);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, keySymbol: null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, keySymbol: null);
            await PumpUntilAsync(() => !window.IsThemeRebuildDeferred);

            Assert.True(message.IsCompleted);
            Assert.NotEqual(before, window.CurrentBackground);
        }
        finally
        {
            window.Close();
        }
    });

    // The deferred rebuild retries on a dispatcher timer, which only fires while the test yields to the dispatcher
    private static async Task PumpUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the deferred rebuild did not run");
            await Task.Delay(millisecondsDelay: 20);
        }
    }

    [Fact]
    public void SearchUsesTheVisibilityThePageSetsWhileResultsAreShown() => AvaloniaTestHost.Run(() =>
    {
        // A card a parent toggle hides, as the submenu delay card is while the system delay is used
        TextBlock title = new() { Text = "Submenu delay" };
        Border card = TrayAppDotNETSettingsCards.RegisterSearchCard(new Border { Child = title, IsVisible = false });
        StackPanel page = new();
        page.Children.Add(card);
        SettingsSearchView view = new(new TextBlock(), "{0}", [new SettingsSearchPageSource("Page", page)]);
        Assert.Empty(view.ReadDocuments());

        // The page shows the card while results are on screen; the next query must see it
        card.IsVisible = true;
        IReadOnlyList<SettingsSearchDocument> documents = view.ReadDocuments();
        view.ApplyMatches([Assert.Single(documents).ID], "submenu");

        Assert.True(card.IsVisible);
    });

    private sealed class SharedBehaviorWindow : SettingsWindowCommon<SharedPage>
    {
        private readonly AppTheme _theme;

        public SharedBehaviorWindow(AppTheme theme)
        {
            _theme = theme;
            ConfigureSettingsWindow(title: "Test", icon: null);
            InitializeSettingsShell();
        }

        public string CurrentBackground => Palette.Background.Value.ToString();

        /// <summary>The text box on the page the shell built last.</summary>
        public TextBox Editor { get; private set; } = null!;

        /// <summary>A button after the editor on that page.</summary>
        public Button LeaveButton { get; private set; } = null!;

        protected override AppTheme SettingsTheme => _theme;
        protected override SharedPage DefaultPageKey => SharedPage.Only;
        protected override string HeaderText => "Test";

        public Task ShowTestMessage() => ShowMessage("Title", "Message");

        protected override IReadOnlyList<SettingsPageDescriptor<SharedPage>> CreatePageDescriptors() =>
        [
            new(SharedPage.Only, Label: "Only", BuildPage)
        ];

        protected override void Save()
        {
        }

        private StackPanel BuildPage()
        {
            Editor = new TextBox();
            LeaveButton = new Button { Content = "Leave" };
            StackPanel page = new();
            page.Children.Add(new TextBlock { Text = "page" });
            page.Children.Add(Editor);
            page.Children.Add(LeaveButton);
            return page;
        }
    }

    private enum SharedPage
    {
        Only
    }
}
