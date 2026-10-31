using FanControlTrayAppDotNET.Services;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class PawnIoDriverInstallerTests
{
    private const long ExpectedInstallerLength = 3_225_016;

    /// <summary>
    /// Verifies a missing installer cannot be selected for elevation and execution.
    /// </summary>
    [Fact]
    public void RejectsMissingInstaller()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"missing-pawnio-{Guid.NewGuid():N}.exe");

        Assert.Null(PawnIoDriverInstaller.TryOpenVerified(missingPath));
    }

    /// <summary>
    /// Verifies matching the publisher asset length alone is insufficient.
    /// </summary>
    [Fact]
    public void RejectsInstallerWithWrongSHA256()
    {
        string temporaryPath = Path.Combine(
            Path.GetTempPath(),
            $"invalid-pawnio-{Guid.NewGuid():N}.exe");

        try
        {
            using (FileStream output = new(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                output.SetLength(ExpectedInstallerLength);
            }

            Assert.Null(PawnIoDriverInstaller.TryOpenVerified(temporaryPath));
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    /// <summary>The download cache lives under the shared TrayAppDotNET root that Fan's uninstall cleans.</summary>
    [Fact]
    public void DownloadCacheLivesUnderTheSharedRoot()
    {
        string cache = PawnIoDriverInstaller.DownloadCacheDirectory;

        Assert.StartsWith(
            TrayAppDotNETCommon.Services.Install.TrayAppDotNETAppInstallServices.LocalAppDataRoot
            + Path.DirectorySeparatorChar,
            cache,
            StringComparison.OrdinalIgnoreCase);
    }
}
