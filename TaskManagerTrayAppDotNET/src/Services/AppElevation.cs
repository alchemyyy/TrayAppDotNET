using System.Security.Principal;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Reports whether this process is running with a full administrator token.</summary>
internal static class AppElevation
{
    private static readonly Lazy<bool> Elevated = new(DetectElevated);

    /// <summary>True when the process holds an elevated (full administrator) token, not a filtered one.</summary>
    public static bool IsElevated => Elevated.Value;

    private static bool DetectElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception exception)
        {
            TADNLog.Log($"AppElevation.DetectElevated: {exception.Message}");
            return false;
        }
    }
}
