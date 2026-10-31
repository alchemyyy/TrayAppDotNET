using System.Text;
using BrightnessTrayAppDotNET.Interop.NightLight;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class NightLightKnownRVAsTests
{
    [Fact]
    public void EntryWithAMalformedOrZeroRVAIsSkipped()
    {
        byte[] xml = Encoding.UTF8.GetBytes(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <NightLightKnownRVAs>
              <Entry Version="10.0.1" Initialize="100" SInstance="200" SetTargetColorTemperature="300" SetPreview="400" SetBlueLightActive="500" />
              <Entry Version="10.0.2" Initialize="0x265C4" SInstance="200" SetTargetColorTemperature="300" SetPreview="400" SetBlueLightActive="500" />
              <Entry Version="10.0.3" Initialize="100" SInstance="0" SetTargetColorTemperature="300" SetPreview="400" SetBlueLightActive="500" />
            </NightLightKnownRVAs>
            """);

        Dictionary<string, (int, int, int, int, int)> known = NightLightCloudStore.ParseKnownRVAs(xml);

        Assert.Equal("10.0.1", Assert.Single(known.Keys));
        Assert.Equal((100, 200, 300, 400, 500), known["10.0.1"]);
    }

    [Fact]
    public void ShippedDefaultsFillBuildsTheMirrorLacksWithoutOverridingUserEntries()
    {
        Dictionary<string, (int, int, int, int, int)> known = new(StringComparer.OrdinalIgnoreCase)
        {
            ["10.0.1"] = (1, 2, 3, 4, 5)
        };
        Dictionary<string, (int, int, int, int, int)> defaults = new()
        {
            ["10.0.1"] = (10, 20, 30, 40, 50),
            ["10.0.9"] = (11, 21, 31, 41, 51)
        };

        bool added = NightLightCloudStore.MergeMissingDefaults(known, defaults);

        Assert.True(added);
        Assert.Equal((1, 2, 3, 4, 5), known["10.0.1"]);
        Assert.Equal((11, 21, 31, 41, 51), known["10.0.9"]);
        Assert.False(NightLightCloudStore.MergeMissingDefaults(known, defaults));
    }
}
