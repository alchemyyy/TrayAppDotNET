using TrayAppDotNETCommon.Serialization;
using VolumeTrayAppDotNET.Models;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class AppSettingsSaveRequestTests
{
    // The XML reader runs the same constructor before it suppresses notifications, so this covers every load too
    [Fact]
    public void LoadingSchedulesNoSave() =>
        WithLoadedSettings((settings, path) =>
        {
            DateTime written = File.GetLastWriteTimeUtc(path);

            settings.Store!.Flush();

            Assert.Equal(written, File.GetLastWriteTimeUtc(path));
        });

    [Fact]
    public void EditAfterLoadSchedulesSave() =>
        WithLoadedSettings((settings, path) =>
        {
            bool edited = !settings.ShowRecordingDevicesInFlyout;

            settings.ShowRecordingDevicesInFlyout = edited;
            settings.Store!.Flush();

            Assert.Equal(edited, TrayXmlSerializer.ReadFile<AppSettings>(path).ShowRecordingDevicesInFlyout);
        });

    private static void WithLoadedSettings(Action<AppSettings, string> test)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"volume-settings-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.xml");
        try
        {
            AppSettings settings = AppSettings.LoadOrDefault(path);
            settings.Store!.Flush();
            test(settings, path);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
