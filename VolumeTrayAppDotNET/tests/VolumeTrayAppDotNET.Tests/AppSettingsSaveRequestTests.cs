using VolumeTrayAppDotNET.Models;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class AppSettingsSaveRequestTests
{
    // The XML reader runs the same constructor before it suppresses notifications, so this covers every load too
    [Fact]
    public void ConstructionSchedulesNoSave() =>
        Assert.Equal(0, new SaveCountingSettings().SaveRequestCount);

    [Fact]
    public void EditAfterConstructionSchedulesSave()
    {
        SaveCountingSettings settings = new();

        settings.ShowRecordingDevicesInFlyout = !settings.ShowRecordingDevicesInFlyout;

        Assert.Equal(1, settings.SaveRequestCount);
    }

    private sealed class SaveCountingSettings : AppSettings
    {
        public int SaveRequestCount { get; private set; }

        protected override void RequestSave() => SaveRequestCount++;
    }
}
