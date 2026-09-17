using System.Text;
using FanControlTrayAppDotNET.Models;
using TrayAppDotNETCommon.Serialization;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class FanProfileTests
{
    [Fact]
    public void SwitchingProfilesSeparatesNamesGroupsAndLayout()
    {
        Fan fan = CreateFan();
        FanGroup radiators = new()
        {
            Name = "Radiators", DisplayOrder = 4, IsCollapsed = true,
            RPMMode = true, FanDisplayedValue = 1500, AssignedCurveName = "Quiet"
        };
        AppSettings settings = new() { Fans = [fan.CloneForPersistence()], FanGroups = [radiators] };

        Assert.True(settings.SelectFanProfile(1, [fan]));
        Assert.Equal(string.Empty, fan.UserDefinedName);
        Assert.Null(fan.Group);
        Assert.Equal(-1, fan.FlyoutDisplayOrder);
        Assert.Empty(settings.FanGroups);
        Assert.Null(FanGroup.Find("Radiators"));
        Assert.Equal(60, fan.ClampHigh);

        fan.UserDefinedName = "Exhaust";
        fan.Group = "Case";
        fan.FlyoutDisplayOrder = 2;
        FanGroup caseGroup = new() { Name = "Case", AssignedCurveName = "Performance" };
        settings.FanGroups.Add(caseGroup);
        Assert.True(settings.SelectFanProfile(0, [fan]));
        Assert.Equal("Front", fan.UserDefinedName);
        Assert.Equal("Radiators", fan.Group);
        Assert.Equal(3, fan.FlyoutDisplayOrder);
        FanGroup restored = Assert.Single(settings.FanGroups);
        Assert.NotSame(radiators, restored);
        Assert.Same(restored, FanGroup.Find("Radiators"));
        Assert.Null(FanGroup.Find("Case"));
        Assert.True(restored.IsCollapsed);
        Assert.True(restored.RPMMode);
        Assert.Equal(1500, restored.FanDisplayedValue);
        Assert.Equal("Quiet", restored.AssignedCurveName);

        restored.Name = "Cooling";
        fan.Group = "Cooling";
        Assert.True(settings.SelectFanProfile(1, [fan]));
        Assert.Equal("Exhaust", fan.UserDefinedName);
        Assert.Equal("Case", fan.Group);
        Assert.Equal("Performance", Assert.Single(settings.FanGroups).AssignedCurveName);
        Assert.Null(FanGroup.Find("Cooling"));
        Assert.Equal("Cooling", Assert.Single(settings.FanProfiles[0].Groups).Name);
    }

    [Fact]
    public void MigrationKeepsLegacyLayoutOnlyOnSelectedProfile()
    {
        Fan fan = CreateFan();
        ProbeCard card = new() { Name = "CPU" };
        AppSettings settings = new()
        {
            SelectedFanProfileIndex = 2,
            Fans = [fan],
            FanGroups = [new FanGroup { Name = "Radiators" }],
            ProbeCards = [card]
        };
        settings.EnsureFanProfileCount(FanProfile.SlotCount);
        settings.FanProfiles[2].Name = "Gaming";
        settings.FanProfiles[0].Fans.Add(new FanProfileEntry
        {
            DataSourceKey = fan.DataSourceKey, AssignedCurveName = "Silent", FanDisplayedValue = 30
        });

        Assert.True(settings.EnsureFanProfileLayouts());
        Assert.False(settings.EnsureFanProfileLayouts());
        Assert.Equal("Gaming", settings.FanProfiles[2].Name);
        Assert.Equal("Front", Assert.Single(settings.FanProfiles[2].Fans).UserDefinedName);
        Assert.Empty(settings.FanProfiles[0].Groups);
        Assert.Empty(settings.FanProfiles[1].Groups);
        Assert.True(card.IsVisibleOnProfile(2));
        Assert.False(card.IsVisibleOnProfile(0));
        Assert.False(card.IsVisibleOnProfile(1));

        Assert.True(settings.SelectFanProfile(0, [fan]));
        Assert.Equal(string.Empty, fan.UserDefinedName);
        Assert.Null(fan.Group);
        Assert.Empty(settings.FanGroups);
        Assert.Equal("Silent", fan.AssignedCurveName);
        Assert.Equal(30, fan.FanDisplayedValue);
        Assert.True(card.IsVisibleOnProfile(2));
        Assert.False(card.IsVisibleOnProfile(0));
    }

    [Fact]
    public void LayoutChangesPersistIndependentlyOfControlAutosave()
    {
        Fan fan = CreateFan();
        AppSettings settings = new() { Autosave = false, Fans = [fan.CloneForPersistence()] };
        settings.EnsureFanProfileLayouts();
        fan.UserDefinedName = "Renamed";
        fan.Group = "Custom";
        fan.FanDisplayedValue = 90;

        settings.SelectFanProfile(1, [fan]);
        settings.SelectFanProfile(0, [fan]);

        Assert.Equal("Renamed", fan.UserDefinedName);
        Assert.Equal("Custom", fan.Group);
        Assert.Equal(50, fan.FanDisplayedValue);
        Assert.Equal("Custom", Assert.Single(settings.FanGroups).Name);
    }

    [Fact]
    public void SwitchingUpdatesDisconnectedFansAndRetainsTheirSnapshots()
    {
        Fan offline = CreateFan();
        AppSettings settings = new()
        {
            Fans = [offline], FanGroups = [new FanGroup { Name = "Radiators" }]
        };
        settings.SelectFanProfile(1, []);
        Assert.Equal(string.Empty, Assert.Single(settings.Fans).UserDefinedName);
        Assert.Null(Assert.Single(settings.Fans).Group);

        settings.SelectFanProfile(0, []);
        Assert.Equal("Front", Assert.Single(settings.Fans).UserDefinedName);
        Assert.Equal("Radiators", Assert.Single(settings.Fans).Group);
        Assert.Equal("Front", Assert.Single(settings.FanProfiles[0].Fans).UserDefinedName);
    }

    [Fact]
    public void MissingFanEntryDoesNotInheritLayoutOrChangeSafetyLimits()
    {
        Fan fan = CreateFan();
        FanProfile profile = new();
        profile.ApplyTo([fan]);

        Assert.Equal(string.Empty, fan.UserDefinedName);
        Assert.Null(fan.Group);
        Assert.Equal(-1, fan.FlyoutDisplayOrder);
        Assert.Equal(60, fan.ClampHigh);
        Assert.Equal("Curve A", fan.AssignedCurveName);
    }

    [Fact]
    public void ProfileMatchingUsesCaseInsensitiveLastEntry()
    {
        Fan fan = CreateFan();
        FanProfile profile = new()
        {
            Fans =
            [
                new() { DataSourceKey = "fan_1", UserDefinedName = "First" },
                new() { DataSourceKey = "FAN_1", UserDefinedName = "Last" }
            ]
        };
        profile.ApplyTo([fan]);
        Assert.Equal("Last", fan.UserDefinedName);
        profile.Capture([fan], [], includeControlState: true);
        Assert.Single(profile.Fans);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    public void SelectingCurrentOrInvalidProfileDoesNotChangeLiveState(int targetIndex)
    {
        Fan fan = CreateFan();
        AppSettings settings = new();

        Assert.False(settings.SelectFanProfile(targetIndex, [fan]));
        Assert.Equal(0, settings.SelectedFanProfileIndex);
        Assert.Equal("Front", fan.UserDefinedName);
        Assert.Equal("Radiators", fan.Group);
    }

    [Fact]
    public void GeneratedXMLRoundTripRetainsIndependentProfilesAndProbeVisibility()
    {
        Fan fan = CreateFan();
        ProbeCard card = new() { Name = "CPU", DisplayProfileMask = 5 };
        AppSettings settings = new()
        {
            Fans = [fan.CloneForPersistence()],
            FanGroups = [new FanGroup { Name = "Radiators" }],
            ProbeCards = [card]
        };
        settings.EnsureFanProfileLayouts();
        settings.FanProfiles[1].Name = "Quiet";
        settings.SelectFanProfile(1, [fan]);
        fan.UserDefinedName = "Exhaust";
        fan.Group = "Case";
        settings.FanGroups.Add(new FanGroup { Name = "Case", RPMMode = true, FanDisplayedValue = 1200 });
        settings.SelectFanProfile(0, [fan]);

        using MemoryStream stream = new();
        TrayXmlSerializer.Write(stream, settings);
        string xml = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("DisplayProfileMask=\"5\"", xml);
        stream.Position = 0;
        AppSettings restored = TrayXmlSerializer.Read<AppSettings>(stream);

        Assert.True(restored.FanProfileLayoutsInitialized);
        Assert.Equal("Quiet", restored.FanProfiles[1].DisplayName(1));
        Assert.Equal("Front", Assert.Single(restored.FanProfiles[0].Fans).UserDefinedName);
        Assert.Equal("Exhaust", Assert.Single(restored.FanProfiles[1].Fans).UserDefinedName);
        Assert.Equal("Case", Assert.Single(restored.FanProfiles[1].Groups).Name);
        Assert.Equal(5, Assert.Single(restored.ProbeCards).DisplayProfileMask);
        Assert.Null(FanGroup.Find("Case"));
        restored.SelectFanProfile(1, [fan]);
        Assert.Equal("Exhaust", fan.UserDefinedName);
        Assert.Equal("Case", fan.Group);
        Assert.Equal(1200, Assert.Single(restored.FanGroups).FanDisplayedValue);
    }

    [Fact]
    public void SavingActiveLayoutSurvivesRestartBeforeAnotherProfileSwitch()
    {
        Fan fan = CreateFan();
        AppSettings settings = new() { Fans = [fan.CloneForPersistence()] };
        settings.EnsureFanProfileLayouts();
        fan.UserDefinedName = "Saved name";
        fan.Group = "Saved group";
        settings.UpsertPersistedFan(fan);
        ProbeCard card = new() { Name = "Sensors" };
        settings.ProbeCards.Add(card);
        string path = Path.GetTempFileName();
        try
        {
            settings.Save(path);
            AppSettings restored = AppSettings.LoadOrDefault(path);
            Assert.Equal("Saved name", Assert.Single(restored.FanProfiles[0].Fans).UserDefinedName);
            Assert.Equal("Saved group", Assert.Single(restored.FanProfiles[0].Groups).Name);
            Assert.True(Assert.Single(restored.ProbeCards).IsVisibleOnProfile(0));

            restored.SelectFanProfile(1, [fan]);
            restored.SelectFanProfile(0, [fan]);
            Assert.Equal("Saved name", fan.UserDefinedName);
            Assert.Equal("Saved group", fan.Group);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Fan CreateFan() => new()
    {
        DataSourceKey = "Fan_1", FansName = "Fan #1", UserDefinedName = "Front",
        Group = "Radiators", FlyoutDisplayOrder = 3, AssignedCurveName = "Curve A", ClampHigh = 60
    };
}
