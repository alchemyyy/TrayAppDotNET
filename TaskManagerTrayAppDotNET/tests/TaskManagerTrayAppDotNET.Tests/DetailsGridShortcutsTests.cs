using Avalonia.Input;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class DetailsGridShortcutsTests
{
    [Theory]
    [InlineData(KeyModifiers.Control, DetailsGridShortcutAction.Zoom)]
    [InlineData(KeyModifiers.Shift, DetailsGridShortcutAction.Stretch)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, DetailsGridShortcutAction.ResetZoom)]
    [InlineData(KeyModifiers.Shift | KeyModifiers.Alt, DetailsGridShortcutAction.ResetStretch)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, DetailsGridShortcutAction.Stretch)]
    [InlineData(KeyModifiers.None, DetailsGridShortcutAction.None)]
    [InlineData(KeyModifiers.Alt, DetailsGridShortcutAction.None)]
    public void MouseWheelResolvesTheRegisteredAction(KeyModifiers modifiers, DetailsGridShortcutAction expected) =>
        Assert.Equal(expected, DetailsGridShortcuts.Resolve(DetailsGridShortcutTrigger.MouseWheel, modifiers));

    [Theory]
    [InlineData(KeyModifiers.Control, DetailsGridShortcutAction.ResetZoom)]
    [InlineData(KeyModifiers.Shift, DetailsGridShortcutAction.ResetStretch)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Alt, DetailsGridShortcutAction.SetZoomBaseline)]
    [InlineData(KeyModifiers.Shift | KeyModifiers.Alt, DetailsGridShortcutAction.SetStretchBaseline)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, DetailsGridShortcutAction.ResetStretch)]
    [InlineData(KeyModifiers.None, DetailsGridShortcutAction.None)]
    public void MiddleClickResolvesTheRegisteredAction(KeyModifiers modifiers, DetailsGridShortcutAction expected) =>
        Assert.Equal(expected, DetailsGridShortcuts.Resolve(DetailsGridShortcutTrigger.MiddleClick, modifiers));

    [Theory]
    [InlineData(Key.Delete, KeyModifiers.None, DetailsGridShortcutAction.EndTask)]
    [InlineData(Key.Delete, KeyModifiers.Shift, DetailsGridShortcutAction.EndTask)]
    [InlineData(Key.Back, KeyModifiers.None, DetailsGridShortcutAction.None)]
    [InlineData(Key.None, KeyModifiers.None, DetailsGridShortcutAction.None)]
    public void KeysResolveTheRegisteredAction(Key key, KeyModifiers modifiers, DetailsGridShortcutAction expected) =>
        Assert.Equal(expected, DetailsGridShortcuts.ResolveKey(key, modifiers));

    [Fact]
    public void EveryActionHasAShortcutAUniqueTitleAndADescription()
    {
        HashSet<string> titles = [];
        foreach (DetailsGridShortcutAction action in Enum.GetValues<DetailsGridShortcutAction>())
        {
            if (action == DetailsGridShortcutAction.None) continue;

            Assert.Contains(DetailsGridShortcuts.All, shortcut => shortcut.Action == action);
            Assert.True(titles.Add(DetailsGridShortcuts.GetTitle(action)));
            Assert.NotEmpty(DetailsGridShortcuts.GetDescription(action));
        }
    }

    [Fact]
    public void EveryRegisteredShortcutResolvesToItsOwnAction()
    {
        foreach (DetailsGridShortcut shortcut in DetailsGridShortcuts.All)
        {
            DetailsGridShortcutAction resolved = shortcut.Trigger == DetailsGridShortcutTrigger.Key
                ? DetailsGridShortcuts.ResolveKey(shortcut.Key, shortcut.Modifiers)
                : DetailsGridShortcuts.Resolve(shortcut.Trigger, shortcut.Modifiers);

            Assert.Equal(shortcut.Action, resolved);
        }
    }

    [Fact]
    public void GesturesFormatModifiersBeforeTheirTrigger()
    {
        Assert.Equal(
            expected: "Ctrl + Alt + Middle click",
            DetailsGridShortcuts.FormatGesture(new DetailsGridShortcut(
                DetailsGridShortcutAction.SetZoomBaseline,
                DetailsGridShortcutTrigger.MiddleClick,
                KeyModifiers.Alt | KeyModifiers.Control)));
        Assert.Equal(
            expected: "Shift + Mouse wheel",
            DetailsGridShortcuts.FormatGesture(new DetailsGridShortcut(
                DetailsGridShortcutAction.Stretch,
                DetailsGridShortcutTrigger.MouseWheel,
                KeyModifiers.Shift)));
        Assert.Equal(
            expected: "Delete",
            DetailsGridShortcuts.FormatGesture(new DetailsGridShortcut(
                DetailsGridShortcutAction.EndTask,
                DetailsGridShortcutTrigger.Key,
                KeyModifiers.None,
                Key.Delete)));
    }

    [Theory]
    [InlineData(DetailsGridShortcutAction.ResetZoom, true)]
    [InlineData(DetailsGridShortcutAction.SetStretchBaseline, true)]
    [InlineData(DetailsGridShortcutAction.Zoom, false)]
    [InlineData(DetailsGridShortcutAction.EndTask, false)]
    public void OnlyResetsAndBaselinesWaitForHeaderInteractions(DetailsGridShortcutAction action, bool expected) =>
        Assert.Equal(expected, DetailsGridShortcuts.IsZoomReset(action));
}
