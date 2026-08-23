using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class AcceleratorPerformanceSamplerTests
{
    [Fact]
    public void ParsesEngineCounterIdentityWithoutSplittingTheName()
    {
        const string name = "pid_12220_luid_0x00000001_0x0000C85A_phys_2_eng_5_engtype_Copy";

        bool parsed = AcceleratorCounterInstanceParser.TryParseEngine(
            name,
            out AcceleratorCounterInstance instance);

        Assert.True(parsed);
        Assert.Equal(12_220, instance.ProcessID);
        Assert.Equal(0x000000010000C85AUL, instance.AdapterLUID);
        Assert.Equal(2, instance.PhysicalAdapterIndex);
        Assert.Equal(5, instance.EngineIndex);
        Assert.Equal("Copy", name[instance.EngineTypeStart..]);
    }

    [Fact]
    public void ParsesProcessMemoryCounterIdentity()
    {
        const string name = "pid_1116_luid_0x00000000_0x0000D3EC_phys_0";

        bool parsed = AcceleratorCounterInstanceParser.TryParseMemory(
            name,
            out AcceleratorCounterInstance instance);

        Assert.True(parsed);
        Assert.Equal(1_116, instance.ProcessID);
        Assert.Equal(0xD3ECUL, instance.AdapterLUID);
        Assert.Equal(0, instance.PhysicalAdapterIndex);
        Assert.Equal(-1, instance.EngineIndex);
        Assert.Equal(-1, instance.EngineTypeStart);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pid_bad_luid_0x0_0x1_phys_0")]
    [InlineData("pid_12_luid_0x0_0x1_phys_0_eng_0")]
    public void RejectsMalformedEngineCounterIdentity(string name)
    {
        Assert.False(AcceleratorCounterInstanceParser.TryParseEngine(name, out _));
    }
}
