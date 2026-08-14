using VolumeTrayAppDotNET.Audio;
using VolumeTrayAppDotNET.Interop;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class BluetoothRadioPowerTests
{
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
