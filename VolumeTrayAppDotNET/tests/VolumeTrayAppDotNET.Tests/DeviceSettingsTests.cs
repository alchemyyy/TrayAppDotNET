using VolumeTrayAppDotNET.Models;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class DeviceSettingsTests
{
    [Fact]
    public void CustomFriendlyNameRoundTripsThroughDevicesXml()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"VolumeTrayAppDotNET.DeviceSettingsTests.{Guid.NewGuid():N}.xml");

        try
        {
            DeviceSettings settings = new();
            DeviceSettingsEntry entry = settings.GetOrCreate("bluetooth-endpoint-id");
            entry.CustomFriendlyName = "My Bluetooth Headphones";
            settings.Save(path);

            DeviceSettings loaded = DeviceSettings.LoadOrDefault(path);

            Assert.Equal("My Bluetooth Headphones",
                loaded.Find("bluetooth-endpoint-id")?.CustomFriendlyName);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
