using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class ApplicationInstanceCoordinatorTests
{
    [Fact]
    public void ApplicationMutexAllowsOnlyOneOwningThread()
    {
        SingleInstanceIdentity identity = new(
            "TrayAppDotNETApplicationInstanceTest",
            Guid.NewGuid().ToString("D"));
        ApplicationInstanceCoordinator first = Assert.IsType<ApplicationInstanceCoordinator>(
            ApplicationInstanceCoordinator.TryAcquire(identity, timeoutMs: 100));

        try
        {
            bool acquiredWhileHeld = AcquireOnDedicatedThread(identity, timeoutMs: 50);
            Assert.False(acquiredWhileHeld);
        }
        finally
        {
            first.Dispose();
        }

        bool acquiredAfterRelease = AcquireOnDedicatedThread(identity, timeoutMs: 1_000);
        Assert.True(acquiredAfterRelease);
    }

    private static bool AcquireOnDedicatedThread(SingleInstanceIdentity identity, int timeoutMs)
    {
        bool acquired = false;
        Thread thread = new(() => acquired = TryAcquireAndRelease(identity, timeoutMs));
        thread.Start();
        thread.Join();
        return acquired;
    }

    private static bool TryAcquireAndRelease(SingleInstanceIdentity identity, int timeoutMs)
    {
        ApplicationInstanceCoordinator? coordinator =
            ApplicationInstanceCoordinator.TryAcquire(identity, timeoutMs);
        if (coordinator == null) return false;

        coordinator.Dispose();
        return true;
    }
}
