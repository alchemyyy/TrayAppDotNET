using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Styling;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Models;

namespace TrayAppDotNETCommon.UI;

public static class TrayAppDotNETAnimationPolicy
{
    private static readonly Style DisabledToolTipTransitionsStyle = new(static selector => selector.OfType<ToolTip>())
    {
        Setters = { new Setter(Animatable.TransitionsProperty, new Transitions()), },
    };

    public static TrayAppDotNETAnimationMode Mode { get; private set; } = TrayAppDotNETAnimationMode.System;

    public static bool AnimationsEnabled => ResolveAnimationsEnabled(Mode);

    public static void Apply(Application application, TrayAppDotNETAnimationMode mode)
    {
        Mode = mode;
        bool disableAnimations = !ResolveAnimationsEnabled(mode);
        bool styleInstalled = application.Styles.Contains(DisabledToolTipTransitionsStyle);

        if (disableAnimations && !styleInstalled)
            application.Styles.Add(DisabledToolTipTransitionsStyle);
        else if (!disableAnimations && styleInstalled) application.Styles.Remove(DisabledToolTipTransitionsStyle);
    }

    public static bool ResolveAnimationsEnabled(TrayAppDotNETAnimationMode mode) => mode switch
    {
        TrayAppDotNETAnimationMode.Disabled => false,
        TrayAppDotNETAnimationMode.Enabled => true,
        _ => SystemAnimationsEnabled(),
    };

    private static bool SystemAnimationsEnabled()
    {
        try
        {
            return !User32.SystemParametersInfo(User32.SPI_GETCLIENTAREAANIMATION, 0, out int enabled, 0) ||
                   enabled != 0;
        }
        catch
        {
            return true;
        }
    }
}
