namespace TaskManagerTrayAppDotNET;

internal static class Constants
{
    public const string ApplicationName = "TaskManagerTrayAppDotNET";
    public const string DisplayName = "Task Manager";
    public const string SharedRootFolderName = "TrayAppDotNET";
    public const string Publisher = "alchemyyy";
    public const string HelpLink = "https://github.com/alchemyyy/TrayAppDotNET";
    public const string AppBaseURI = "avares://" + ApplicationName + "/";
    public const string AppIconFileName = "app.ico";
    public const string AppIconRelativePath = "Assets/" + AppIconFileName;
    public const string AppIconResourceUri = AppBaseURI + AppIconRelativePath;
    public const string AppGUID = "4ae62894-9d4e-44fb-97d7-622d90f594ae";
    public const string TrayIconGUID = "25683ae6-ff6c-4d7f-8115-59181532f7f5";
}
