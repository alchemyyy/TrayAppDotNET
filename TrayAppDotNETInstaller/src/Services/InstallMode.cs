namespace TrayAppDotNETInstaller.Services;

public enum InstallMode
{
    /// <summary>Per-user install under %LocalAppData%\TrayAppDotNET.</summary>
    Local,

    /// <summary>All-users install under %ProgramFiles%\TrayAppDotNET, driven by an elevated worker.</summary>
    System,

    /// <summary>Plain extraction into a user-chosen folder.</summary>
    Portable
}
