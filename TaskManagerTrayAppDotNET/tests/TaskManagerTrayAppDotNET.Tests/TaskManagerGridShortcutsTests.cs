using Avalonia.Input;
using TaskManagerTrayAppDotNET.UI;
using TrayAppDotNETCommon.UI.ControlMapping;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class TaskManagerGridShortcutsTests
{
    [Theory]
    [InlineData(KeyModifiers.Control, TaskManagerGridShortcutAction.Zoom)]
    [InlineData(KeyModifiers.Shift, TaskManagerGridShortcutAction.Stretch)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, TaskManagerGridShortcutAction.ResetZoom)]
    [InlineData(KeyModifiers.Shift | KeyModifiers.Alt, TaskManagerGridShortcutAction.ResetStretch)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Meta, TaskManagerGridShortcutAction.Zoom)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, TaskManagerGridShortcutAction.None)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt, TaskManagerGridShortcutAction.None)]
    [InlineData(KeyModifiers.None, TaskManagerGridShortcutAction.None)]
    [InlineData(KeyModifiers.Alt, TaskManagerGridShortcutAction.None)]
    public void MouseWheelResolvesTheMostSpecificMapGesture(
        KeyModifiers modifiers,
        TaskManagerGridShortcutAction expected) =>
        Assert.Equal(expected, TaskManagerGridShortcuts.Resolve(TaskManagerGridShortcutTrigger.MouseWheel, modifiers));

    [Theory]
    [InlineData(KeyModifiers.Control, TaskManagerGridShortcutAction.ResetZoom)]
    [InlineData(KeyModifiers.Shift, TaskManagerGridShortcutAction.ResetStretch)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, TaskManagerGridShortcutAction.SetZoomBaseline)]
    [InlineData(KeyModifiers.Shift | KeyModifiers.Alt, TaskManagerGridShortcutAction.SetStretchBaseline)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, TaskManagerGridShortcutAction.None)]
    [InlineData(KeyModifiers.None, TaskManagerGridShortcutAction.None)]
    public void MiddleClickResolvesTheMostSpecificMapGesture(
        KeyModifiers modifiers,
        TaskManagerGridShortcutAction expected) =>
        Assert.Equal(expected, TaskManagerGridShortcuts.Resolve(TaskManagerGridShortcutTrigger.MiddleClick, modifiers));

    [Fact]
    public void KeyGesturesAreLeftToTheControlMapRuntime() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TaskManagerGridShortcuts.Resolve(TaskManagerGridShortcutTrigger.Key, KeyModifiers.None));

    [Fact]
    public void EndTaskReadsDeleteFromTheProcessTableLeaf()
    {
        TaskManagerGridShortcut endTask = Assert.Single(
            TaskManagerGridShortcuts.All,
            shortcut => shortcut.Action == TaskManagerGridShortcutAction.EndTask);

        Assert.Equal(UI.ControlMap.Main.ProcessesPage.TableViewport.Table.EndTask, endTask.Node);
        Assert.Equal(TaskManagerGridShortcutTrigger.Key, endTask.Trigger);
        Assert.Equal(Key.Delete, endTask.Key);
        Assert.Equal(KeyModifiers.None, endTask.Modifiers);
    }

    [Fact]
    public void EveryMapGestureOfAShortcutLeafIsListed()
    {
        foreach (TaskManagerGridShortcutEntry entry in TaskManagerGridShortcuts.Entries)
        {
            Leaf leaf = Assert.IsType<Leaf>(ControlMapCatalog.Find(entry.Node));
            int listedCount = 0;
            foreach (TaskManagerGridShortcut shortcut in TaskManagerGridShortcuts.All)
            {
                if (shortcut.Action != entry.Action) continue;

                Assert.Equal(entry.Node, shortcut.Node);
                listedCount++;
            }

            // A gesture the tables cannot dispatch would be missing here, and from the Hotkeys page
            Assert.NotEqual(expected: 0, listedCount);
            Assert.Equal(leaf.PointerGestures.Count + leaf.KeyGestures.Count, listedCount);
        }
    }

    [Fact]
    public void EveryTaskManagerGridCommandHasAnAction()
    {
        Template grid = Assert.IsType<Template>(ControlMapCatalog.Find(UI.ControlMap.TaskManagerGrid.ID));
        foreach (ControlMapNode child in grid.Children)
        {
            if (child is not Leaf { Kind: LeafKind.Command } command) continue;

            Assert.Contains(TaskManagerGridShortcuts.Entries, entry => entry.Node == command.NodeID);
        }
    }

    [Fact]
    public void EveryActionHasAnEntryAUniqueTitleAndADescription()
    {
        HashSet<string> titles = [];
        foreach (TaskManagerGridShortcutAction action in Enum.GetValues<TaskManagerGridShortcutAction>())
        {
            if (action == TaskManagerGridShortcutAction.None) continue;

            Assert.Single(TaskManagerGridShortcuts.Entries, entry => entry.Action == action);
            Assert.True(titles.Add(TaskManagerGridShortcuts.GetTitle(action)));
            Assert.NotEmpty(TaskManagerGridShortcuts.GetDescription(action));
        }
    }

    [Fact]
    public void EveryPointerShortcutResolvesToItsOwnAction()
    {
        foreach (TaskManagerGridShortcut shortcut in TaskManagerGridShortcuts.All)
        {
            if (shortcut.Trigger == TaskManagerGridShortcutTrigger.Key) continue;

            Assert.Equal(shortcut.Action, TaskManagerGridShortcuts.Resolve(shortcut.Trigger, shortcut.Modifiers));
        }
    }

    [Fact]
    public void GesturesFormatModifiersBeforeTheirTrigger()
    {
        Assert.Equal(
            expected: "Ctrl + Alt + Middle click",
            TaskManagerGridShortcuts.FormatGesture(new TaskManagerGridShortcut(
                TaskManagerGridShortcutAction.SetZoomBaseline,
                UI.ControlMap.TaskManagerGrid.SetZoomBaseline,
                TaskManagerGridShortcutTrigger.MiddleClick,
                KeyModifiers.Alt | KeyModifiers.Control)));
        Assert.Equal(
            expected: "Shift + Mouse wheel",
            TaskManagerGridShortcuts.FormatGesture(new TaskManagerGridShortcut(
                TaskManagerGridShortcutAction.Stretch,
                UI.ControlMap.TaskManagerGrid.Stretch,
                TaskManagerGridShortcutTrigger.MouseWheel,
                KeyModifiers.Shift)));
        Assert.Equal(
            expected: "Delete",
            TaskManagerGridShortcuts.FormatGesture(new TaskManagerGridShortcut(
                TaskManagerGridShortcutAction.EndTask,
                UI.ControlMap.Main.ProcessesPage.TableViewport.Table.EndTask,
                TaskManagerGridShortcutTrigger.Key,
                KeyModifiers.None,
                Key.Delete)));
    }

    [Theory]
    [InlineData(TaskManagerGridShortcutAction.ResetZoom, true)]
    [InlineData(TaskManagerGridShortcutAction.SetStretchBaseline, true)]
    [InlineData(TaskManagerGridShortcutAction.Zoom, false)]
    [InlineData(TaskManagerGridShortcutAction.EndTask, false)]
    public void OnlyResetsAndBaselinesWaitForHeaderInteractions(TaskManagerGridShortcutAction action, bool expected) =>
        Assert.Equal(expected, TaskManagerGridShortcuts.IsZoomReset(action));
}
