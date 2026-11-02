using TrayAppDotNETCommon.Serialization;
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
            TrayXmlSerializer.WriteFile(path, settings);

            DeviceSettings loaded = DeviceSettings.LoadOrDefault(path);

            Assert.Equal(expected: "My Bluetooth Headphones",
                loaded.Find("bluetooth-endpoint-id")?.CustomFriendlyName);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WriteFileDoesNotReuseLockedLegacyTemporaryFile()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"VolumeTrayAppDotNET.DeviceSettingsTests.{Guid.NewGuid():N}.xml");
        string legacyTemporaryPath = path + ".tmp";

        try
        {
            DeviceSettings settings = new();
            settings.GetOrCreate("locked-temporary-file").CustomFriendlyName = "Headphones";

            using (new FileStream(legacyTemporaryPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                TrayXmlSerializer.WriteFile(path, settings);

            DeviceSettings loaded = DeviceSettings.LoadOrDefault(path);
            Assert.Equal(expected: "Headphones", loaded.Find("locked-temporary-file")?.CustomFriendlyName);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(legacyTemporaryPath)) File.Delete(legacyTemporaryPath);
        }
    }

    [Fact]
    public async Task ConcurrentWritesProduceAReadableFile()
    {
        const int writerCount = 32;
        string path = Path.Combine(
            Path.GetTempPath(),
            $"VolumeTrayAppDotNET.DeviceSettingsTests.{Guid.NewGuid():N}.xml");
        using ManualResetEventSlim start = new(false);
        Task[] writers = new Task[writerCount];

        try
        {
            for (int writerIndex = 0; writerIndex < writers.Length; writerIndex++)
            {
                int capturedWriterIndex = writerIndex;
                writers[writerIndex] = Task.Run(() =>
                {
                    DeviceSettings settings = new();
                    settings.GetOrCreate($"device-{capturedWriterIndex}").CustomFriendlyName =
                        $"Device {capturedWriterIndex}";
                    start.Wait();
                    TrayXmlSerializer.WriteFile(path, settings);
                });
            }

            start.Set();
            await Task.WhenAll(writers);

            DeviceSettings loaded = DeviceSettings.LoadOrDefault(path);
            Assert.Single(loaded.Devices);
            Assert.NotNull(loaded.Devices[0].CustomFriendlyName);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, $"{Path.GetFileName(path)}.*.tmp"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
        }
    }
}
