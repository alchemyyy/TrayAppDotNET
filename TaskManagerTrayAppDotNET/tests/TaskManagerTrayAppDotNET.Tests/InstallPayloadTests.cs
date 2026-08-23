using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class InstallPayloadTests
{
    [Fact]
    public void NativeKillHelperIsARequiredInstallFile()
    {
        Assert.Contains(
            AppServices.Installation.Payload.RequiredFiles,
            file => string.Equals(file.Name, Constants.KillHelperFileName, StringComparison.Ordinal));
    }
}
