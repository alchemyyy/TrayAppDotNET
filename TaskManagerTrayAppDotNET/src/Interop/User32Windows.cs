namespace TaskManagerTrayAppDotNET.Interop;

/// <summary>How a top-level window counts for Task Manager's user-facing window rules.</summary>
internal enum TopLevelWindowClass : byte
{
    NotQualifying,
    Qualifying,

    /// <summary>The DWM cloak state could not be read; each caller decides what that means for it.</summary>
    Unknown
}

/// <summary>
/// The single user-facing top-level window rule: visible, unowned, titled, not a tool window, and not cloaked.
/// </summary>
internal static class TopLevelWindowClassifier
{
    private const uint GetWindowOwner = User32.GW_OWNER;
    private const int WindowExtendedStyle = -20;
    private const long WindowStyleToolWindow = 0x00000080L;
    private const uint DwmWindowAttributeCloaked = 14;

    public static TopLevelWindowClass Classify(nint windowHandle)
    {
        if (!User32.IsWindow(windowHandle)
            || !User32.IsWindowVisible(windowHandle)
            || User32.GetWindow(windowHandle, GetWindowOwner) != IntPtr.Zero
            || User32.GetWindowTextLength(windowHandle) <= 0)
            return TopLevelWindowClass.NotQualifying;

        long extendedStyle = User32.GetWindowLongPtr(windowHandle, WindowExtendedStyle).ToInt64();
        if ((extendedStyle & WindowStyleToolWindow) != 0) return TopLevelWindowClass.NotQualifying;

        int result = DWMAPI.DwmGetWindowAttribute(windowHandle, DwmWindowAttributeCloaked, out int isCloaked, sizeof(int));
        if (result < 0) return TopLevelWindowClass.Unknown;
        return isCloaked == 0 ? TopLevelWindowClass.Qualifying : TopLevelWindowClass.NotQualifying;
    }
}
