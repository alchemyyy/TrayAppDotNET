using Avalonia.Input;
using VolumeTrayAppDotNET.Audio;
using VolumeTrayAppDotNET.Interop;
using VolumeTrayAppDotNET.Models;
using VolumeTrayAppDotNET.UI.Flyout;
using TrayAppDotNETCommon.Serialization;
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
            TrayXmlSerializer.WriteFile(path, settings);

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
    [InlineData(BluetoothRadioButtonClickGesture.LeftClick, KeyModifiers.Control, false)]
    [InlineData(BluetoothRadioButtonClickGesture.LeftClick, KeyModifiers.Alt, false)]
    [InlineData(BluetoothRadioButtonClickGesture.LeftClick, KeyModifiers.Shift, false)]
    [InlineData(BluetoothRadioButtonClickGesture.ControlLeftClick, KeyModifiers.None, false)]
    [InlineData(BluetoothRadioButtonClickGesture.ControlLeftClick, KeyModifiers.Control, true)]
    [InlineData(BluetoothRadioButtonClickGesture.ControlLeftClick, KeyModifiers.Alt, false)]
    [InlineData(BluetoothRadioButtonClickGesture.ControlLeftClick, KeyModifiers.Shift, false)]
    [InlineData(BluetoothRadioButtonClickGesture.AltLeftClick, KeyModifiers.None, false)]
    [InlineData(BluetoothRadioButtonClickGesture.AltLeftClick, KeyModifiers.Control, false)]
    [InlineData(BluetoothRadioButtonClickGesture.AltLeftClick, KeyModifiers.Alt, true)]
    [InlineData(BluetoothRadioButtonClickGesture.AltLeftClick, KeyModifiers.Shift, false)]
    [InlineData(BluetoothRadioButtonClickGesture.ShiftLeftClick, KeyModifiers.None, false)]
    [InlineData(BluetoothRadioButtonClickGesture.ShiftLeftClick, KeyModifiers.Control, false)]
    [InlineData(BluetoothRadioButtonClickGesture.ShiftLeftClick, KeyModifiers.Alt, false)]
    [InlineData(BluetoothRadioButtonClickGesture.ShiftLeftClick, KeyModifiers.Shift, true)]
    public void BluetoothHeaderButtonTogglesOnPowerGestureAndOpensQuickSettingsOtherwise(
        BluetoothRadioButtonClickGesture powerGesture,
        KeyModifiers modifiers,
        bool expectsToggle)
    {
        VolumeFlyoutWindow.BluetoothRadioButtonAction expected = expectsToggle
            ? VolumeFlyoutWindow.BluetoothRadioButtonAction.ToggleRadio
            : VolumeFlyoutWindow.BluetoothRadioButtonAction.OpenQuickSettings;

        VolumeFlyoutWindow.BluetoothRadioButtonAction actual =
            VolumeFlyoutWindow.ResolveBluetoothRadioButtonAction(powerGesture, modifiers, canToggleRadio: true);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(BluetoothRadioButtonClickGesture.LeftClick, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(BluetoothRadioButtonClickGesture.ControlLeftClick, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(BluetoothRadioButtonClickGesture.AltLeftClick, KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData(BluetoothRadioButtonClickGesture.ShiftLeftClick, KeyModifiers.Shift | KeyModifiers.Alt)]
    public void BluetoothHeaderButtonOpensQuickSettingsWhenExtraModifiersAreHeld(
        BluetoothRadioButtonClickGesture powerGesture,
        KeyModifiers modifiers)
    {
        Assert.Equal(
            VolumeFlyoutWindow.BluetoothRadioButtonAction.OpenQuickSettings,
            VolumeFlyoutWindow.ResolveBluetoothRadioButtonAction(powerGesture, modifiers, canToggleRadio: true));
    }

    [Theory]
    [InlineData(BluetoothRadioButtonClickGesture.LeftClick)]
    [InlineData(BluetoothRadioButtonClickGesture.ControlLeftClick)]
    [InlineData(BluetoothRadioButtonClickGesture.AltLeftClick)]
    [InlineData(BluetoothRadioButtonClickGesture.ShiftLeftClick)]
    public void BluetoothHeaderButtonPowerGestureDoesNothingWhileToggleIsUnavailable(
        BluetoothRadioButtonClickGesture powerGesture)
    {
        KeyModifiers powerModifiers = VolumeFlyoutWindow.BluetoothRadioButtonClickGestureModifiers(powerGesture);
        KeyModifiers quickSettingsModifiers = VolumeFlyoutWindow.BluetoothRadioButtonClickGestureModifiers(
            VolumeFlyoutWindow.BluetoothQuickSettingsClickGesture(powerGesture));

        Assert.Equal(
            VolumeFlyoutWindow.BluetoothRadioButtonAction.None,
            VolumeFlyoutWindow.ResolveBluetoothRadioButtonAction(powerGesture, powerModifiers, canToggleRadio: false));
        Assert.Equal(
            VolumeFlyoutWindow.BluetoothRadioButtonAction.OpenQuickSettings,
            VolumeFlyoutWindow.ResolveBluetoothRadioButtonAction(
                powerGesture,
                quickSettingsModifiers,
                canToggleRadio: false));
    }

    [Theory]
    [InlineData(BluetoothRadioButtonClickGesture.LeftClick, BluetoothRadioButtonClickGesture.ControlLeftClick)]
    [InlineData(BluetoothRadioButtonClickGesture.ControlLeftClick, BluetoothRadioButtonClickGesture.LeftClick)]
    [InlineData(BluetoothRadioButtonClickGesture.AltLeftClick, BluetoothRadioButtonClickGesture.LeftClick)]
    [InlineData(BluetoothRadioButtonClickGesture.ShiftLeftClick, BluetoothRadioButtonClickGesture.LeftClick)]
    public void BluetoothHeaderButtonTooltipNamesAClickThatOpensQuickSettings(
        BluetoothRadioButtonClickGesture powerGesture,
        BluetoothRadioButtonClickGesture expectedQuickSettingsGesture)
    {
        BluetoothRadioButtonClickGesture quickSettingsGesture =
            VolumeFlyoutWindow.BluetoothQuickSettingsClickGesture(powerGesture);
        KeyModifiers quickSettingsModifiers =
            VolumeFlyoutWindow.BluetoothRadioButtonClickGestureModifiers(quickSettingsGesture);

        Assert.Equal(expectedQuickSettingsGesture, quickSettingsGesture);
        Assert.Equal(
            VolumeFlyoutWindow.BluetoothRadioButtonAction.OpenQuickSettings,
            VolumeFlyoutWindow.ResolveBluetoothRadioButtonAction(
                powerGesture,
                quickSettingsModifiers,
                canToggleRadio: true));
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
