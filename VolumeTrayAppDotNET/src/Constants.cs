namespace VolumeTrayAppDotNET;

internal static class Constants
{
    public const string ApplicationName = "VolumeTrayAppDotNET";
    public const string SharedRootFolderName = "TrayAppDotNET";
    public const string NoWatcherEnvironmentVariable = "TrayAppDotNET_NO_WATCHER";
    public const string Publisher = "alchemyyy";
    public const string HelpLink = "https://github.com/alchemyyy/TrayAppDotNET";
    public const string AppBaseURI = "avares://" + ApplicationName + "/";
    public const string AppIconFileName = "app.ico";
    public const string AppIconRelativePath = "Assets/" + AppIconFileName;
    public const string AppIconResourceUri = AppBaseURI + AppIconRelativePath;
    public const string AppGUID = "1ac1ef49-bb6a-4a21-8480-24766db1f35e";
    public const string TrayIconGUID = "1ac1ef49-bb6a-4a21-8480-24766db1f35e";
    public const string TaskbarWindowClassName = "Shell_TrayWnd";
    public const string MonitorDeviceInterfaceGUID = "e6f07b5f-ee97-4a90-b076-33f57bf4eaa7";
    public const string MonitorSetupClassGUID = "4d36e96e-e325-11ce-bfc1-08002be10318";
}
