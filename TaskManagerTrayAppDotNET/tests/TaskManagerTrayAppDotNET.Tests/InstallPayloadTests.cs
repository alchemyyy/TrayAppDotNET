using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class InstallPayloadTests
{
    [Fact]
    public void StandaloneKillHelperIsARequiredManagedBuildInstallFile()
    {
        Assert.Contains(
            AppServices.Installation.Payload.RequiredFiles,
            file => string.Equals(file.Name, Constants.KillHelperFileName, StringComparison.Ordinal));
    }
}
