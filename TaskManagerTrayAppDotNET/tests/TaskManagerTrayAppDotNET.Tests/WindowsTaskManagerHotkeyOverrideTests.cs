using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class WindowsTaskManagerHotkeyOverrideTests
{
    private const uint EscapeVirtualKey = 0x1B;

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    public void ShortcutRequiresControlAndShiftWithoutAltOrWindows(
        bool isControlDown,
        bool isShiftDown,
        bool isAltDown,
        bool isWindowsKeyDown)
    {
        using WindowsTaskManagerHotkeyOverride hotkeyOverride = new(
            static () => { },
            log: null);

        WindowsTaskManagerHotkeyDecision decision = hotkeyOverride.ProcessKeyboardEvent(
            EscapeVirtualKey,
            isKeyDown: true,
            isKeyUp: false,
            isControlDown,
            isShiftDown,
            isAltDown,
            isWindowsKeyDown);

        Assert.Equal(WindowsTaskManagerHotkeyDecision.PassThrough, decision);
    }

    [Fact]
    public void ShortcutSuppressesRepeatsAndMatchingKeyUpButActivatesOnce()
    {
        using WindowsTaskManagerHotkeyOverride hotkeyOverride = new(
            static () => { },
            log: null);

        WindowsTaskManagerHotkeyDecision initialKeyDown = hotkeyOverride.ProcessKeyboardEvent(
            EscapeVirtualKey,
            isKeyDown: true,
            isKeyUp: false,
            isControlDown: true,
            isShiftDown: true,
            isAltDown: false,
            isWindowsKeyDown: false);
        WindowsTaskManagerHotkeyDecision repeatedKeyDown = hotkeyOverride.ProcessKeyboardEvent(
            EscapeVirtualKey,
            isKeyDown: true,
            isKeyUp: false,
            isControlDown: true,
            isShiftDown: true,
            isAltDown: false,
            isWindowsKeyDown: false);
        WindowsTaskManagerHotkeyDecision keyUp = hotkeyOverride.ProcessKeyboardEvent(
            EscapeVirtualKey,
            isKeyDown: false,
            isKeyUp: true,
            isControlDown: true,
            isShiftDown: true,
            isAltDown: false,
            isWindowsKeyDown: false);
        WindowsTaskManagerHotkeyDecision unrelatedKeyUp = hotkeyOverride.ProcessKeyboardEvent(
            EscapeVirtualKey,
            isKeyDown: false,
            isKeyUp: true,
            isControlDown: false,
            isShiftDown: false,
            isAltDown: false,
            isWindowsKeyDown: false);

        Assert.Equal(WindowsTaskManagerHotkeyDecision.SuppressAndActivate, initialKeyDown);
        Assert.Equal(WindowsTaskManagerHotkeyDecision.Suppress, repeatedKeyDown);
        Assert.Equal(WindowsTaskManagerHotkeyDecision.Suppress, keyUp);
        Assert.Equal(WindowsTaskManagerHotkeyDecision.PassThrough, unrelatedKeyUp);
    }
}
