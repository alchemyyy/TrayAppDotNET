using Avalonia.Input;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class TaskManagerGridShortcutsTests
{
    [Theory]
    [InlineData(KeyModifiers.Control, TaskManagerGridShortcutAction.Zoom)]
    [InlineData(KeyModifiers.Shift, TaskManagerGridShortcutAction.Stretch)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, TaskManagerGridShortcutAction.ResetZoom)]
    [InlineData(KeyModifiers.Shift | KeyModifiers.Alt, TaskManagerGridShortcutAction.ResetStretch)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, TaskManagerGridShortcutAction.Stretch)]
    [InlineData(KeyModifiers.None, TaskManagerGridShortcutAction.None)]
    [InlineData(KeyModifiers.Alt, TaskManagerGridShortcutAction.None)]
    public void MouseWheelResolvesTheRegisteredAction(KeyModifiers modifiers, TaskManagerGridShortcutAction expected) =>
        Assert.Equal(expected, TaskManagerGridShortcuts.Resolve(TaskManagerGridShortcutTrigger.MouseWheel, modifiers));

    [Theory]
    [InlineData(KeyModifiers.Control, TaskManagerGridShortcutAction.ResetZoom)]
    [InlineData(KeyModifiers.Shift, TaskManagerGridShortcutAction.ResetStretch)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, TaskManagerGridShortcutAction.SetZoomBaseline)]
    [InlineData(KeyModifiers.Shift | KeyModifiers.Alt, TaskManagerGridShortcutAction.SetStretchBaseline)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, TaskManagerGridShortcutAction.ResetStretch)]
    [InlineData(KeyModifiers.None, TaskManagerGridShortcutAction.None)]
    public void MiddleClickResolvesTheRegisteredAction(
        KeyModifiers modifiers,
        TaskManagerGridShortcutAction expected) =>
        Assert.Equal(expected, TaskManagerGridShortcuts.Resolve(TaskManagerGridShortcutTrigger.MiddleClick, modifiers));

    [Theory]
    [InlineData(Key.Delete, KeyModifiers.None, TaskManagerGridShortcutAction.EndTask)]
    [InlineData(Key.Delete, KeyModifiers.Shift, TaskManagerGridShortcutAction.EndTask)]
    [InlineData(Key.Back, KeyModifiers.None, TaskManagerGridShortcutAction.None)]
    [InlineData(Key.None, KeyModifiers.None, TaskManagerGridShortcutAction.None)]
    public void KeysResolveTheRegisteredAction(
        Key key,
        KeyModifiers modifiers,
        TaskManagerGridShortcutAction expected) =>
        Assert.Equal(expected, TaskManagerGridShortcuts.ResolveKey(key, modifiers));

    [Fact]
    public void EveryActionHasAShortcutAUniqueTitleAndADescription()
    {
        HashSet<string> titles = [];
        foreach (TaskManagerGridShortcutAction action in Enum.GetValues<TaskManagerGridShortcutAction>())
        {
            if (action == TaskManagerGridShortcutAction.None) continue;

            Assert.Contains(TaskManagerGridShortcuts.All, shortcut => shortcut.Action == action);
            Assert.True(titles.Add(TaskManagerGridShortcuts.GetTitle(action)));
            Assert.NotEmpty(TaskManagerGridShortcuts.GetDescription(action));
        }
    }

    [Fact]
    public void EveryRegisteredShortcutResolvesToItsOwnAction()
    {
        foreach (TaskManagerGridShortcut shortcut in TaskManagerGridShortcuts.All)
        {
            TaskManagerGridShortcutAction resolved = shortcut.Trigger == TaskManagerGridShortcutTrigger.Key
                ? TaskManagerGridShortcuts.ResolveKey(shortcut.Key, shortcut.Modifiers)
                : TaskManagerGridShortcuts.Resolve(shortcut.Trigger, shortcut.Modifiers);

            Assert.Equal(shortcut.Action, resolved);
        }
    }

    [Fact]
    public void GesturesFormatModifiersBeforeTheirTrigger()
    {
        Assert.Equal(
            expected: "Ctrl + Alt + Middle click",
            TaskManagerGridShortcuts.FormatGesture(new TaskManagerGridShortcut(
                TaskManagerGridShortcutAction.SetZoomBaseline,
                TaskManagerGridShortcutTrigger.MiddleClick,
                KeyModifiers.Alt | KeyModifiers.Control)));
        Assert.Equal(
            expected: "Shift + Mouse wheel",
            TaskManagerGridShortcuts.FormatGesture(new TaskManagerGridShortcut(
                TaskManagerGridShortcutAction.Stretch,
                TaskManagerGridShortcutTrigger.MouseWheel,
                KeyModifiers.Shift)));
        Assert.Equal(
            expected: "Delete",
            TaskManagerGridShortcuts.FormatGesture(new TaskManagerGridShortcut(
                TaskManagerGridShortcutAction.EndTask,
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
