using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using TaskManagerTrayAppDotNET.UI.ProcessesPage;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class ProcessSavedSearchControllerTestCollection
{
    public const string CollectionName = "Process saved-search controller tests";
}

[Collection(ProcessSavedSearchControllerTestCollection.CollectionName)]
public sealed class ProcessSavedSearchControllerTests
{
    private const double SearchBoxHeight = 32;
    private const double EnabledOpacity = 1;
    private const double WindowWidth = 400;
    private const double WindowHeight = 200;
    private const string SavedQuery = "chrome";

    [Fact]
    public async Task SaveIsDisabledWhileTheQueryIsAlreadySaved()
    {
        await using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                TaskManagerWindowResources resources = new();
                TextBox searchBox = new() { Height = SearchBoxHeight };
                List<IReadOnlyList<ProcessSavedSearch>> published = [];
                using ProcessSavedSearchController controller = CreateController(
                    searchBox,
                    resources,
                    published.Add);
                Control saveButton = controller.SaveButton;

                SetSearchText(searchBox, text: "  CHROME ");

                Assert.False(saveButton.IsEnabled);
                Assert.Equal(
                    resources.AxamlTaskManagerDetails.SearchActionDisabledOpacity,
                    saveButton.Opacity);
                Assert.Null(saveButton.Cursor);
                Assert.Equal(expected: "Search already saved", ToolTip.GetTip(saveButton));

                SetSearchText(searchBox, text: "firefox");

                Assert.True(saveButton.IsEnabled);
                Assert.Equal(EnabledOpacity, saveButton.Opacity);
                Assert.NotNull(saveButton.Cursor);
                Assert.Equal(expected: "Save search", ToolTip.GetTip(saveButton));

                PressEnter(saveButton);

                // Saving disables Save for the query it just stored
                IReadOnlyList<ProcessSavedSearch> saved = Assert.Single(published);
                Assert.Equal([SavedQuery, "firefox"], saved.Select(static search => search.Query));
                Assert.False(saveButton.IsEnabled);

                PressEnter(saveButton);

                Assert.Single(published);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task DisabledSaveButtonStillBlocksAHeaderWindowDrag()
    {
        await using HeadlessUnitTestSession session =
            HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(
            static () =>
            {
                TextBox searchBox = new() { Height = SearchBoxHeight };
                using ProcessSavedSearchController controller = CreateController(
                    searchBox,
                    new TaskManagerWindowResources(),
                    static _ => { });
                Control saveButton = controller.SaveButton;
                StackPanel searchControls = new()
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Top,
                    Children = { saveButton, searchBox }
                };
                Grid header = new() { Background = Brushes.Black, Children = { searchControls } };
                Window window = new() { Width = WindowWidth, Height = WindowHeight, Content = header };

                try
                {
                    window.Show();
                    SetSearchText(searchBox, SavedQuery);
                    window.UpdateLayout();
                    Point saveButtonCenter = saveButton.TranslatePoint(
                        new Point(saveButton.Bounds.Width / 2, saveButton.Bounds.Height / 2),
                        window)!.Value;

                    // Avalonia routes the press past the disabled button to the header background
                    Assert.False(saveButton.IsEnabled);
                    Assert.Same(header, window.InputHitTest(saveButtonCenter));
                    Assert.True(TaskManagerWindow.IsInteractiveHeaderControlAt(window, saveButtonCenter));
                    Assert.False(TaskManagerWindow.IsInteractiveHeaderControlAt(
                        window,
                        new Point(WindowWidth - 1, WindowHeight - 1)));
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    private static ProcessSavedSearchController CreateController(
        TextBox searchBox,
        TaskManagerWindowResources resources,
        Action<IReadOnlyList<ProcessSavedSearch>> savedSearchesChanged) =>
        new(
            searchBox,
            [new ProcessSavedSearch { Name = "Browsers", Query = SavedQuery }],
            CreatePalette(),
            resources,
            enableRoundedCorners: false,
            new AppSettings { Autosave = false },
            savedSearchesChanged,
            static _ => Task.FromResult(true));

    /// <summary>Sets the query and runs the dispatcher, since TextBox raises TextChanged asynchronously.</summary>
    private static void SetSearchText(TextBox searchBox, string text)
    {
        searchBox.Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    private static void PressEnter(Control control) =>
        control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

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

    private sealed class TestApplication : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }

    private static class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder
            .Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
