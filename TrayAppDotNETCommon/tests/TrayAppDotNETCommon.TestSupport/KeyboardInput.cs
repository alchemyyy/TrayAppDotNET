using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

namespace TrayAppDotNETCommon.TestSupport;

/// <summary>Sends keys to a headless window the way a keyboard does.</summary>
public static class KeyboardInput
{
    /// <summary>
    /// Presses and releases <paramref name="key"/>, then runs the jobs the key posted. Space carries its key symbol, as
    /// on the desktop. A key that closes its window, such as Escape or Left in a submenu, leaves no window to release
    /// the key in, so the release is sent only while the window is still shown.
    /// </summary>
    public static void PressKey(
        Window window,
        Key key,
        PhysicalKey physicalKey,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        string? keySymbol = key == Key.Space ? " " : null;
        window.KeyPress(key, modifiers, physicalKey, keySymbol);
        if (window.IsVisible)
            window.KeyRelease(key, modifiers, physicalKey, keySymbol);
        Dispatcher.UIThread.RunJobs();
    }
}
