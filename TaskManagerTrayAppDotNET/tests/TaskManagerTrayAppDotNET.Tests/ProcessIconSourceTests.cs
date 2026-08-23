using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessIconSourceTests
{
    [Fact]
    public void DefaultSourceIsUnavailable()
    {
        ProcessIconSource source = default;

        Assert.False(source.IsAvailable);
    }

    [Theory]
    [InlineData("C:\\Apps\\Example.exe", null)]
    [InlineData(null, "Example.Package_123!App")]
    public void EitherShellIdentityMakesSourceAvailable(string? executablePath, string? applicationUserModelID)
    {
        ProcessIconSource source = new(executablePath, applicationUserModelID);

        Assert.True(source.IsAvailable);
    }

    [Fact]
    public void CacheComparerIgnoresWindowsIdentityCasing()
    {
        ProcessIconSource left = new(
            "C:\\Apps\\Example.exe",
            "Example.Package_123!App");
        ProcessIconSource right = new(
            "c:\\apps\\EXAMPLE.EXE",
            "example.package_123!APP");

        bool areEqual = ProcessIconSourceComparer.Instance.Equals(left, right);
        int leftHash = ProcessIconSourceComparer.Instance.GetHashCode(left);
        int rightHash = ProcessIconSourceComparer.Instance.GetHashCode(right);

        Assert.True(areEqual);
        Assert.Equal(leftHash, rightHash);
    }

    [Fact]
    public void CacheComparerKeepsDistinctExecutablePathsSeparate()
    {
        ProcessIconSource left = new("C:\\Apps\\One\\Example.exe", null);
        ProcessIconSource right = new("C:\\Apps\\Two\\Example.exe", null);

        Assert.False(ProcessIconSourceComparer.Instance.Equals(left, right));
    }
}
