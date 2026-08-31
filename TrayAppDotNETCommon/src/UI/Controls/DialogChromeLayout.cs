using Avalonia;

namespace TrayAppDotNETCommon.UI.Controls;

/// <summary>Provides typed access to shared installer and uninstaller layout resources.</summary>
internal static class TrayAppDotNETDialogChromeLayout
{
    private static DialogChromeResources AXAMLResources => DialogChromeResources.Current;

    public static double InstallerWindowWidth => AXAMLResources.AxamlDialogChrome.InstallerWindowWidth;
    public static double InstallerWindowHeight => AXAMLResources.AxamlDialogChrome.InstallerWindowHeight;
    public static double InstallerWindowMinWidth => AXAMLResources.AxamlDialogChrome.InstallerWindowMinWidth;
    public static double InstallerWindowMinHeight => AXAMLResources.AxamlDialogChrome.InstallerWindowMinHeight;
    public static double UninstallerWindowWidth => AXAMLResources.AxamlDialogChrome.UninstallerWindowWidth;
    public static double UninstallerWindowHeight => AXAMLResources.AxamlDialogChrome.UninstallerWindowHeight;
    public static double UninstallerWindowMinWidth => AXAMLResources.AxamlDialogChrome.UninstallerWindowMinWidth;
    public static double UninstallerWindowMinHeight => AXAMLResources.AxamlDialogChrome.UninstallerWindowMinHeight;
    public static double TitleBarHeight => AXAMLResources.AxamlDialogChrome.TitleBarHeight;
    public static double TitleFontSize => AXAMLResources.AxamlDialogChrome.TitleFontSize;
    public static Thickness RootBorderThickness => AXAMLResources.AxamlDialogChrome.RootBorderThickness;
    public static CornerRadius RootCornerRadius => AXAMLResources.AxamlDialogChrome.RootCornerRadius;
    public static CornerRadius CardCornerRadius => AXAMLResources.AxamlDialogChrome.CardCornerRadius;
    public static CornerRadius ZeroCornerRadius => AXAMLResources.AxamlDialogChrome.ZeroCornerRadius;
    public static Thickness TitleMargin => AXAMLResources.AxamlDialogChrome.TitleMargin;
    public static Thickness BodyMargin => AXAMLResources.AxamlDialogChrome.BodyMargin;
    public static Thickness DescriptionMargin => AXAMLResources.AxamlDialogChrome.DescriptionMargin;
    public static Thickness OptionRadioMargin => AXAMLResources.AxamlDialogChrome.OptionRadioMargin;
    public static Thickness OptionCardPadding => AXAMLResources.AxamlDialogChrome.OptionCardPadding;
    public static Thickness OptionCardMargin => AXAMLResources.AxamlDialogChrome.OptionCardMargin;

    public static Thickness InstallerLocationTitleMargin =>
        AXAMLResources.AxamlDialogChrome.InstallerLocationTitleMargin;

    public static Thickness InstallerLocationButtonsMargin =>
        AXAMLResources.AxamlDialogChrome.InstallerLocationButtonsMargin;

    public static Thickness InstallerLocationButtonPadding =>
        AXAMLResources.AxamlDialogChrome.InstallerLocationButtonPadding;

    public static Thickness InstallerLocationButtonBorderThickness =>
        AXAMLResources.AxamlDialogChrome.InstallerLocationButtonBorderThickness;

    public static Thickness InstallerLocationButtonGap => AXAMLResources.AxamlDialogChrome.InstallerLocationButtonGap;
    public static Thickness InstallerPathPadding => AXAMLResources.AxamlDialogChrome.InstallerPathPadding;
    public static Thickness InstallerPathMargin => AXAMLResources.AxamlDialogChrome.InstallerPathMargin;

    public static Thickness InstallerPathBorderThickness =>
        AXAMLResources.AxamlDialogChrome.InstallerPathBorderThickness;

    public static Thickness InstallerShortcutMargin => AXAMLResources.AxamlDialogChrome.InstallerShortcutMargin;
    public static Thickness InstallerShortcutPadding => AXAMLResources.AxamlDialogChrome.InstallerShortcutPadding;
    public static double InstallerPathFontSize => AXAMLResources.AxamlDialogChrome.InstallerPathFontSize;
    public static Thickness ActionButtonPadding => AXAMLResources.AxamlDialogChrome.ActionButtonPadding;
    public static Thickness CancelButtonMargin => AXAMLResources.AxamlDialogChrome.CancelButtonMargin;
    public static Thickness ActionButtonsMargin => AXAMLResources.AxamlDialogChrome.ActionButtonsMargin;
}
