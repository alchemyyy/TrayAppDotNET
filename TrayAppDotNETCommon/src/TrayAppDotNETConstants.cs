namespace TrayAppDotNETCommon;

/// <summary>
/// Identity and environment values every TrayAppDotNET app shares. The .NET Framework installer factory keeps its own
/// copies because it does not reference this project.
/// </summary>
public static class TrayAppDotNETConstants
{
    /// <summary>Folder under %LOCALAPPDATA% (and %ProgramFiles%) that holds every app's data and installs.</summary>
    public const string SharedRootFolderName = "TrayAppDotNET";

    public const string Publisher = "alchemyyy";
    public const string RepositoryOwner = "alchemyyy";
    public const string RepositoryName = "TrayAppDotNET";
    public const string HelpLink = "https://github.com/" + RepositoryOwner + "/" + RepositoryName;

    /// <summary>A truthy value starts the app without its crash watcher process.</summary>
    public const string NoWatcherEnvironmentVariable = "TrayAppDotNET_NO_WATCHER";

    /// <summary>Window class of the primary taskbar.</summary>
    public const string TaskbarWindowClassName = "Shell_TrayWnd";
}
