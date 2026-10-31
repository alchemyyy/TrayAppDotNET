using TrayAppDotNETCommon.Utils;

namespace TrayAppDotNETCommon.Services.Install;

/// <summary>
/// Writes progress lines (<see cref="TrayAppDotNETInstallProgress"/>, shared with the installer as
/// Contracts/InstallProgress.cs) to the invoking console or redirected standard output.
/// </summary>
public sealed class TrayAppDotNETConsoleProgress : IProgress<TrayAppDotNETInstallProgress>
{
    public static readonly TrayAppDotNETConsoleProgress Instance = new();

    public void Report(TrayAppDotNETInstallProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);
        // Failure lines stay on standard output so consumers see the protocol in order
        _ = TrayAppDotNETConsoleOutput.TryWriteLine(value.ToLine(), error: false);
    }
}
