using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Settings;

namespace NetworkTrayAppDotNET.UI;

public sealed partial class NetworkSettingsWindow
{
    private StackPanel BuildAboutPage()
    {
        TrayAppDotNETAboutPage page = new(new TrayAppDotNETAboutPageOptions
        {
            Palette = Palette,
            ButtonRadius = RadiusMedium,
            CardRadius = RadiusLarge,
            Localize = L,
            Save = Save,
            ApplicationName = Constants.ApplicationName,
            Tagline = L("Settings_About_Tagline", "A tray-based network controller."),
            BuildNumber = BuildInfo.BuildNumber,
            Publisher = Constants.Publisher,
            HelpLink = Constants.HelpLink,
        });
        return page.Build();
    }
}
