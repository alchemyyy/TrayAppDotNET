using TrayAppDotNETCommon.Interop;

namespace TrayAppDotNETCommon.UI.Tray;

/// <summary>Resolves the Ctrl and Alt variants of a tray click, double click or wheel action.</summary>
public static class TrayClickModifiers
{
    public static bool IsCtrlDown() => IsKeyDown(User32.VK_CONTROL);

    public static bool IsAltDown() => IsKeyDown(User32.VK_MENU);

    /// <summary>
    /// Returns <paramref name="ctrl"/> while Ctrl is held and <paramref name="alt"/> while Alt is held, unless that
    /// variant is <paramref name="nothing"/>, which falls through to <paramref name="fallback"/>. Ctrl wins over Alt.
    /// </summary>
    public static TAction Resolve<TAction>(TAction ctrl, TAction alt, TAction fallback, TAction nothing)
        where TAction : struct, Enum =>
        Resolve(IsCtrlDown(), IsAltDown(), ctrl, alt, fallback, nothing);

    internal static TAction Resolve<TAction>(
        bool isCtrlDown,
        bool isAltDown,
        TAction ctrl,
        TAction alt,
        TAction fallback,
        TAction nothing)
        where TAction : struct, Enum
    {
        EqualityComparer<TAction> comparer = EqualityComparer<TAction>.Default;
        if (isCtrlDown && !comparer.Equals(ctrl, nothing)) return ctrl;
        if (isAltDown && !comparer.Equals(alt, nothing)) return alt;
        return fallback;
    }

    private static bool IsKeyDown(int virtualKey) => (User32.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}
