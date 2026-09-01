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

        Assert.False(PawnIoDriverInstaller.HasExpectedInstaller(missingPath));
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

            Assert.False(PawnIoDriverInstaller.HasExpectedInstaller(temporaryPath));
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
