using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Win32;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Models;

namespace TrayAppDotNETCommon.UI;

/// <summary>
/// Applies the animation mode. <see cref="TrayAppDotNETAnimationMode.System"/> follows the Windows "Animation
/// effects" preference, so a change to it while the app runs is applied again from one hook here.
/// </summary>
public static class TrayAppDotNETAnimationPolicy
{
    private static readonly Style DisabledToolTipTransitionsStyle = new(static selector => selector.OfType<ToolTip>())
    {
        Setters = { new Setter(Animatable.TransitionsProperty, new Transitions()) }
    };

    private static Application? _application;
    private static bool _preferenceHookInstalled;

    public static TrayAppDotNETAnimationMode Mode { get; private set; } = TrayAppDotNETAnimationMode.System;

    public static void Apply(Application application, TrayAppDotNETAnimationMode mode)
    {
        _application = application;
        Mode = mode;
        InstallPreferenceHook();
        ApplyStyles(application, ResolveAnimationsEnabled(mode));
    }

    public static bool ResolveAnimationsEnabled(TrayAppDotNETAnimationMode mode) => mode switch
    {
        TrayAppDotNETAnimationMode.Disabled => false,
        TrayAppDotNETAnimationMode.Enabled => true,
        _ => SystemAnimationsEnabled()
    };

    private static void ApplyStyles(Application application, bool animationsEnabled)
    {
        bool styleInstalled = application.Styles.Contains(DisabledToolTipTransitionsStyle);
        switch (animationsEnabled)
        {
            case false when !styleInstalled:
                application.Styles.Add(DisabledToolTipTransitionsStyle);
                break;
            case true when styleInstalled:
                application.Styles.Remove(DisabledToolTipTransitionsStyle);
                break;
        }
    }

    private static void InstallPreferenceHook()
    {
        if (_preferenceHookInstalled) return;

        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _preferenceHookInstalled = true;
        }
        catch (Exception ex)
        {
            TADNLog.Log($"TrayAppDotNETAnimationPolicy: preference hook failed: {ex.Message}");
        }
    }

    // SystemParametersInfo(SPI_SETCLIENTAREAANIMATION) broadcasts WM_SETTINGCHANGE, which SystemEvents reports with
    // a category that depends on the setting, so every preference change re-reads the system value
    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Mode != TrayAppDotNETAnimationMode.System) return;

        Dispatcher.UIThread.Post(static () =>
        {
            if (_application is { } application && Mode == TrayAppDotNETAnimationMode.System)
                ApplyStyles(application, SystemAnimationsEnabled());
        });
    }

    private static bool SystemAnimationsEnabled()
    {
        try
        {
            return !User32.SystemParametersInfo(User32.SPI_GETCLIENTAREAANIMATION, uiParam: 0, out int enabled,
                       fWinIni: 0) ||
                   enabled != 0;
        }
        catch
        {
            return true;
        }
    }
}
