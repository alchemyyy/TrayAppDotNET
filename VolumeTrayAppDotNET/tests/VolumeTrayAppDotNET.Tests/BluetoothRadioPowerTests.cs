using Avalonia.Input;
using VolumeTrayAppDotNET.Audio;
using VolumeTrayAppDotNET.Interop;
using VolumeTrayAppDotNET.Models;
using VolumeTrayAppDotNET.UI.Flyout;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class BluetoothRadioPowerTests
{
    [Fact]
    public void BluetoothHeaderButtonSettingsUseExpectedDefaults()
    {
        AppSettings settings = new();

        Assert.True(settings.ShowBluetoothRadioButtonInFlyoutHeader);
        Assert.Equal(
            BluetoothRadioButtonClickGesture.ControlLeftClick,
            settings.FlyoutBluetoothRadioButtonClickGesture);
    }

    [Fact]
    public void BluetoothHeaderButtonSettingsRoundTripThroughSettingsXml()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"VolumeTrayAppDotNET.BluetoothRadioPowerTests.{Guid.NewGuid():N}.xml");

        try
        {
            AppSettings settings = new();
            settings.OnTrayXmlDeserializing();
            settings.ShowBluetoothRadioButtonInFlyoutHeader = false;
            settings.FlyoutBluetoothRadioButtonClickGesture = BluetoothRadioButtonClickGesture.AltLeftClick;
            settings.OnTrayXmlDeserialized();
            settings.Save(path);

            AppSettings loaded = AppSettings.LoadOrDefault(path);

            Assert.False(loaded.ShowBluetoothRadioButtonInFlyoutHeader);
            Assert.Equal(
                BluetoothRadioButtonClickGesture.AltLeftClick,
                loaded.FlyoutBluetoothRadioButtonClickGesture);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData(BluetoothRadioButtonClickGesture.LeftClick, KeyModifiers.None, true)]
    [InlineData(BluetoothRadioButtonClickGesture.ControlLeftClick, KeyModifiers.Control, true)]
    [InlineData(BluetoothRadioButtonClickGesture.AltLeftClick, KeyModifiers.Alt, true)]
    [InlineData(BluetoothRadioButtonClickGesture.ShiftLeftClick, KeyModifiers.Shift, true)]
    [InlineData(BluetoothRadioButtonClickGesture.LeftClick, KeyModifiers.Control, false)]
    [InlineData(BluetoothRadioButtonClickGesture.ControlLeftClick, KeyModifiers.None, false)]
    [InlineData(
        BluetoothRadioButtonClickGesture.ControlLeftClick,
        KeyModifiers.Control | KeyModifiers.Shift,
        false)]
    public void BluetoothHeaderButtonRequiresTheConfiguredGesture(
        BluetoothRadioButtonClickGesture gesture,
        KeyModifiers modifiers,
        bool expected)
    {
        bool actual = VolumeFlyoutWindow.IsBluetoothRadioToggleGesture(gesture, modifiers);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolveStateUsesOnWhenAnyBluetoothRadioIsOn()
    {
        DeviceRadioState[] states =
        [
            DeviceRadioState.SoftwareRadioOff,
            DeviceRadioState.RadioOn
        ];

        Assert.Equal(BluetoothRadioPowerState.On, BluetoothRadioPower.ResolveState(states));
    }

    [Theory]
    [InlineData((int)DeviceRadioState.SoftwareRadioOff)]
    [InlineData((int)DeviceRadioState.HardwareRadioOff)]
    [InlineData((int)DeviceRadioState.SoftwareAndHardwareRadioOff)]
    [InlineData((int)DeviceRadioState.HardwareRadioOffUncontrollable)]
    public void ResolveStateTreatsEveryOffVariantAsOff(int rawState)
    {
        DeviceRadioState state = (DeviceRadioState)rawState;
        Assert.Equal(BluetoothRadioPowerState.Off, BluetoothRadioPower.ResolveState([state]));
    }

    [Fact]
    public void ResolveStateUsesUnavailableWithoutAValidRadio()
    {
        Assert.Equal(
            BluetoothRadioPowerState.Unavailable,
            BluetoothRadioPower.ResolveState([DeviceRadioState.Invalid]));
    }

    [Theory]
    [InlineData((int)DeviceRadioState.RadioOn, false, true)]
    [InlineData((int)DeviceRadioState.RadioOn, true, false)]
    [InlineData((int)DeviceRadioState.SoftwareRadioOff, true, true)]
    [InlineData((int)DeviceRadioState.SoftwareRadioOff, false, false)]
    [InlineData((int)DeviceRadioState.HardwareRadioOff, false, true)]
    [InlineData((int)DeviceRadioState.HardwareRadioOff, true, false)]
    [InlineData((int)DeviceRadioState.SoftwareAndHardwareRadioOff, true, true)]
    [InlineData((int)DeviceRadioState.HardwareRadioOnUncontrollable, false, false)]
    [InlineData((int)DeviceRadioState.HardwareRadioOffUncontrollable, true, false)]
    public void NeedsStateChangeRespectsSoftwareAndHardwareState(
        int rawState,
        bool isEnabled,
        bool expected)
    {
        DeviceRadioState state = (DeviceRadioState)rawState;
        Assert.Equal(expected, BluetoothRadioPower.NeedsStateChange(state, isEnabled));
    }
}
