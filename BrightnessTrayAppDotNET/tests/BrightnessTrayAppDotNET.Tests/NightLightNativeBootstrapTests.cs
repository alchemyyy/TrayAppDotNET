using BrightnessTrayAppDotNET.Interop.NightLight;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class NightLightNativeBootstrapTests
{
    private static readonly Guid TestPDBGuid = Guid.ParseExact(
        "00112233445566778899aabbccddeeff",
        format: "N");

    [Fact]
    public void InitializationCommandUsesExactVersionOneWireFormat()
    {
        NightLightNativeBootstrapDescriptor descriptor = CreateDescriptor();

        string command = descriptor.ToInitializationCommand();

        Assert.Equal(
            "INIT\t1\t00112233445566778899aabbccddeeff\t7\t80000\t1ABCD\t2BCDE\t3CDEF\t4DEF0\t5EF01",
            command);
        Assert.All(command, character => Assert.InRange(character, low: (char)0, high: (char)0x7F));
    }

    [Fact]
    public void InitializationCommandRoundTripsDescriptor()
    {
        NightLightNativeBootstrapDescriptor expected = CreateDescriptor();

        bool parsed = NightLightHelperProtocol.TryParseInitialization(
            expected.ToInitializationCommand(),
            out NightLightNativeBootstrapDescriptor actual);

        Assert.True(parsed);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("INIT\t2\t00112233445566778899aabbccddeeff\t7\t80000\t1\t2\t3\t4\t5")]
    [InlineData("INIT\t1\t{00112233-4455-6677-8899-aabbccddeeff}\t7\t80000\t1\t2\t3\t4\t5")]
    [InlineData("INIT\t1\t00112233445566778899aabbccddeeff\t0\t80000\t1\t2\t3\t4\t5")]
    [InlineData("INIT\t1\t00112233445566778899aabbccddeeff\t7\t80000\t0\t2\t3\t4\t5")]
    [InlineData("INIT\t1\t00112233445566778899aabbccddeeff\t7\t80000\t80000\t2\t3\t4\t5")]
    [InlineData("INIT\t1\t00112233445566778899aabbccddeeff\t7\t80000\t1\t2\t3\t4\t5\textra")]
    [InlineData("INIT\t1\t00112233445566778899aabbccddeeff\t7\t80000\t1\t2\t3\t4\t\u00E9")]
    public void InvalidInitializationCommandsAreRejected(string command)
    {
        Assert.False(NightLightHelperProtocol.TryParseInitialization(command, out _));
    }

    [Fact]
    public void DescriptorRejectsOutOfImageRVAs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NightLightNativeBootstrapDescriptor(
            TestPDBGuid,
            PDBAge: 1,
            imageSize: 0x1000,
            initializeRVA: 0x1000,
            sInstanceRVA: 2,
            setTargetColorTemperatureRVA: 3,
            setPreviewColorTemperatureChangesRVA: 4,
            setBlueLightActiveRVA: 5));
    }

    [Fact]
    public void DefaultDescriptorCannotBeSerialized()
    {
        NightLightNativeBootstrapDescriptor descriptor = default;

        Assert.Throws<ArgumentException>(descriptor.ToInitializationCommand);
    }

    [Fact]
    public void DescriptorImageIdentityIncludesGuidAgeAndImageSize()
    {
        NightLightNativeBootstrapDescriptor descriptor = CreateDescriptor();

        Assert.True(descriptor.HasImageIdentity(TestPDBGuid, PDBAge: 7, imageSize: 0x80000));
        Assert.False(descriptor.HasImageIdentity(Guid.NewGuid(), PDBAge: 7, imageSize: 0x80000));
        Assert.False(descriptor.HasImageIdentity(TestPDBGuid, PDBAge: 8, imageSize: 0x80000));
        Assert.False(descriptor.HasImageIdentity(TestPDBGuid, PDBAge: 7, imageSize: 0x81000));
    }

    [Fact]
    public void ResolverCreatesDescriptorOnlyWithEveryRequiredSymbol()
    {
        PDBImageIdentity identity = new(
            PDBName: "settingshandlers_display.pdb",
            PDBGuid: TestPDBGuid,
            PDBAge: 7,
            ImageSize: 0x80000,
            PreferredImageBase: 0x180000000,
            FileVersion: "1.2.3.4",
            FileSize: 1234);
        Dictionary<string, int> rvas = CreateRVAs();

        Assert.True(NightLightNativeBootstrapResolver.TryCreateDescriptor(identity, rvas, out _));

        rvas.Remove(NightLightNativeBootstrapResolver.SetBlueLightActiveSymbol);
        Assert.False(NightLightNativeBootstrapResolver.TryCreateDescriptor(identity, rvas, out _));
    }

    [Fact]
    public void PEIdentityReaderReturnsStableCodeViewIdentityWithoutLoadingForExecution()
    {
        string assemblyPath = typeof(NightLightNativeBootstrapDescriptor).Assembly.Location;

        Assert.True(PDBSymbolResolver.TryReadImageIdentity(assemblyPath, out PDBImageIdentity first));
        Assert.True(PDBSymbolResolver.TryReadImageIdentity(assemblyPath, out PDBImageIdentity second));
        Assert.Equal(first, second);
        Assert.NotEqual(Guid.Empty, first.PDBGuid);
        Assert.True(first.PDBAge > 0);
        Assert.True(first.ImageSize > 0);
        Assert.True(first.PreferredImageBase > 0);
    }

    [Fact]
    public void CommandSerializersValidateTheirArguments()
    {
        Assert.Equal("SET\t73", NightLightHelperProtocol.SerializeSetStrength(percent: 73));
        Assert.Equal("ACTIVE\t0", NightLightHelperProtocol.SerializeSetEnabled(enabled: false, enableStrength: null));
        Assert.Equal("ACTIVE\t1\t73", NightLightHelperProtocol.SerializeSetEnabled(enabled: true, enableStrength: 73));
        Assert.Throws<ArgumentOutOfRangeException>(() => NightLightHelperProtocol.SerializeSetStrength(percent: 101));
        Assert.Throws<ArgumentException>(() =>
            NightLightHelperProtocol.SerializeSetEnabled(enabled: false, enableStrength: 50));
    }

    private static NightLightNativeBootstrapDescriptor CreateDescriptor() => new(
        TestPDBGuid,
        PDBAge: 7,
        imageSize: 0x80000,
        initializeRVA: 0x1ABCD,
        sInstanceRVA: 0x2BCDE,
        setTargetColorTemperatureRVA: 0x3CDEF,
        setPreviewColorTemperatureChangesRVA: 0x4DEF0,
        setBlueLightActiveRVA: 0x5EF01);

    private static Dictionary<string, int> CreateRVAs() => new()
    {
        [NightLightNativeBootstrapResolver.InitializeSymbol] = 0x1ABCD,
        [NightLightNativeBootstrapResolver.SInstanceSymbol] = 0x2BCDE,
        [NightLightNativeBootstrapResolver.SetTargetColorTemperatureSymbol] = 0x3CDEF,
        [NightLightNativeBootstrapResolver.SetPreviewColorTemperatureChangesSymbol] = 0x4DEF0,
        [NightLightNativeBootstrapResolver.SetBlueLightActiveSymbol] = 0x5EF01
    };
}
