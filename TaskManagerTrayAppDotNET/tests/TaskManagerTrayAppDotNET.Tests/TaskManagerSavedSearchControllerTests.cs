using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class TaskManagerSavedSearchControllerTestCollection
{
    public const string CollectionName = "Task Manager saved-search controller tests";
}

[Collection(TaskManagerSavedSearchControllerTestCollection.CollectionName)]
public sealed class TaskManagerSavedSearchControllerTests
{
    private const double SearchBoxHeight = 32;
    private const double EnabledOpacity = 1;
    private const double WindowWidth = 400;
    private const double WindowHeight = 200;
    private const string SavedQuery = "chrome";

    [Fact]
    public void SaveIsDisabledWhileTheQueryIsAlreadySaved() => AvaloniaTestHost.RunWithFluentTheme(static () =>
    {
        TaskManagerWindowResources resources = new();
        TextBox searchBox = new() { Height = SearchBoxHeight };
        List<IReadOnlyList<SavedSearch>> published = [];
        using TaskManagerSavedSearchController controller = CreateController(
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
        IReadOnlyList<SavedSearch> saved = Assert.Single(published);
        Assert.Equal([SavedQuery, "firefox"], saved.Select(static search => search.Query));
        Assert.False(saveButton.IsEnabled);

        PressEnter(saveButton);

        Assert.Single(published);
    });

    [Fact]
    public void InvalidQueryTintsClearAndExplainsTheErrorInItsTooltip() => AvaloniaTestHost.RunWithFluentTheme(static () =>
    {
        TaskManagerWindowResources resources = new();
        TextBox searchBox = new() { Height = SearchBoxHeight };
        using TaskManagerSavedSearchController controller = CreateController(
            searchBox,
            resources,
            static _ => { });
        Control clearButton = ((Grid)controller.SearchControls).Children[0];
        Border clearSurface = Assert.IsType<Border>(((Border)clearButton).Child);
        const string errorMessage = "Invalid regular expression: Unterminated [] set.";

        controller.SetQueryError(errorMessage);

        Assert.Equal(
            resources.AxamlTaskManagerDetails.SearchInvalidTintColor,
            Assert.IsAssignableFrom<ISolidColorBrush>(clearSurface.Background).Color);
        Assert.Contains(errorMessage, Assert.IsType<string>(ToolTip.GetTip(clearButton)), StringComparison.Ordinal);

        controller.SetQueryError(errorMessage: null);

        Assert.Equal(
            Colors.Transparent,
            Assert.IsAssignableFrom<ISolidColorBrush>(clearSurface.Background).Color);
        Assert.Equal(expected: "Clear search", ToolTip.GetTip(clearButton));
    });

    [Fact]
    public void DisabledSaveButtonStillBlocksAHeaderWindowDrag() => AvaloniaTestHost.RunWithFluentTheme(static () =>
    {
        TextBox searchBox = new() { Height = SearchBoxHeight };
        using TaskManagerSavedSearchController controller = CreateController(
            searchBox,
            new TaskManagerWindowResources(),
            static _ => { });
        Control saveButton = controller.SaveButton;
        Control searchControls = controller.SearchControls;
        searchControls.HorizontalAlignment = HorizontalAlignment.Center;
        Grid header = new() { Background = Brushes.Black, Children = { searchControls } };
        Window window = new() { Width = WindowWidth, Height = WindowHeight, Content = header };

        try
        {
            window.Show();
            SetSearchText(searchBox, SavedQuery);
            window.UpdateLayout();
            // Hit testing reads the composed scene, so commit a frame rather than rely on one an earlier test left
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
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
    });

    private static TaskManagerSavedSearchController CreateController(
        TextBox searchBox,
        TaskManagerWindowResources resources,
        Action<IReadOnlyList<SavedSearch>> savedSearchesChanged) =>
        new(
            searchBox,
            ControlMap.Main.ProcessesPage.Search.Clear,
            ControlMap.Main.ProcessesPage.Search.Save,
            [new SavedSearch { Name = "Browsers", Query = SavedQuery }],
            TestPalettes.Default(),
            resources,
            leftAligned: false,
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
}
