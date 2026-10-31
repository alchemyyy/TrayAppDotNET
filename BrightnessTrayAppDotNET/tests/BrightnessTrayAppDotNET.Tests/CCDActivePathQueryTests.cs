using BrightnessTrayAppDotNET.Interop.DDCCI;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class CCDActivePathQueryTests
{
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorAccessDenied = 5;

    [Fact]
    public void TopologyRaceIsRetriedWithFreshSizes()
    {
        ScriptedQuery query = new(sizes: [2, 3], results: [ErrorInsufficientBuffer, 0]);

        bool ok = CCD.TryQueryActivePaths(query, caller: "test", out CCD.DISPLAYCONFIG_PATH_INFO[] paths);

        Assert.True(ok);
        Assert.Equal(expected: 3, paths.Length);
        Assert.Equal(expected: 2, query.QueryCalls);
    }

    [Fact]
    public void PersistentRaceGivesUpAfterThreeAttempts()
    {
        ScriptedQuery query = new(
            sizes: [1, 1, 1, 1],
            results: [ErrorInsufficientBuffer, ErrorInsufficientBuffer, ErrorInsufficientBuffer, 0]);

        bool ok = CCD.TryQueryActivePaths(query, caller: "test", out CCD.DISPLAYCONFIG_PATH_INFO[] paths);

        Assert.False(ok);
        Assert.Empty(paths);
        Assert.Equal(expected: 3, query.QueryCalls);
    }

    [Fact]
    public void OtherFailuresAreNotRetried()
    {
        ScriptedQuery query = new(sizes: [1, 1], results: [ErrorAccessDenied, 0]);

        bool ok = CCD.TryQueryActivePaths(query, caller: "test", out _);

        Assert.False(ok);
        Assert.Equal(expected: 1, query.QueryCalls);
    }

    [Fact]
    public void FewerPathsThanSizedAreTrimmed()
    {
        ScriptedQuery query = new(sizes: [4], results: [0], returnedPathCount: 2);

        bool ok = CCD.TryQueryActivePaths(query, caller: "test", out CCD.DISPLAYCONFIG_PATH_INFO[] paths);

        Assert.True(ok);
        Assert.Equal(expected: 2, paths.Length);
    }

    [Fact]
    public void NoActivePathIsASuccessfulEmptyResult()
    {
        ScriptedQuery query = new(sizes: [0], results: []);

        bool ok = CCD.TryQueryActivePaths(query, caller: "test", out CCD.DISPLAYCONFIG_PATH_INFO[] paths);

        Assert.True(ok);
        Assert.Empty(paths);
        Assert.Equal(expected: 0, query.QueryCalls);
    }

    private sealed class ScriptedQuery(uint[] sizes, int[] results, uint? returnedPathCount = null)
        : CCD.IActivePathQuery
    {
        private int _sizeCalls;

        public int QueryCalls { get; private set; }

        public int GetBufferSizes(out uint pathCount, out uint modeCount)
        {
            pathCount = sizes[_sizeCalls++];
            modeCount = pathCount;
            return 0;
        }

        public int Query(
            ref uint pathCount,
            CCD.DISPLAYCONFIG_PATH_INFO[] paths,
            ref uint modeCount,
            CCD.DISPLAYCONFIG_MODE_INFO_BLOB[] modes)
        {
            int result = results[QueryCalls++];
            if (result == 0 && returnedPathCount is { } count) pathCount = count;
            return result;
        }
    }
}
