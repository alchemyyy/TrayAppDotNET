namespace TaskManagerTrayAppDotNET;

internal static class Constants
{
    public const string ApplicationName = "TaskManagerTrayAppDotNET";
    public const string DisplayName = "Task Manager";
    public const string AppBaseURI = "avares://" + ApplicationName + "/";
    public const string AppIconResourceUri = AppBaseURI + "Assets/" + AppIconLoader.DefaultFileName;

    /// <summary>The app icon frame the main window draws in its title bar and sidebar.</summary>
    public const int ApplicationBitmapSize = 256;
    public const string KillHelperFileName = "TaskManagerTrayAppDotNET.KillHelper.exe";
    public const string KillHelperModeArgument = "--kill-helper";
    public const string AppGUID = "4ae62894-9d4e-44fb-97d7-622d90f594ae";
    public const string TrayIconGUID = "25683ae6-ff6c-4d7f-8115-59181532f7f5";
}
