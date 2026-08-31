using System.Runtime.InteropServices;
using Avalonia.Threading;
using TrayAppDotNETCommon.Services;
using VolumeTrayAppDotNET.Audio;
using VolumeTrayAppDotNET.Interop;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class AudioSessionReconciliationTests
{
    private const int AudioClientDeviceInvalidated = unchecked((int)0x88890004);

    [Fact]
    public void ReconcileRefreshesStateAndVolumeFromCurrentControl()
    {
        FakeAudioSessionControl control = new() { CurrentState = AudioSessionState.Active, CurrentVolume = 1f };
        using AsyncThrottler<string> throttler = new(0);
        using AudioSession session = new(control, Dispatcher.UIThread, throttler);

        control.CurrentState = AudioSessionState.Inactive;
        control.CurrentVolume = 0.77f;
        control.CurrentMute = true;

        AudioSessionReconciliationResult result = session.ReconcileWithCoreAudio();

        Assert.Equal(AudioSessionReconciliationResult.Current, result);
        Assert.Equal(AudioSessionState.Inactive, session.State);
        Assert.Equal(expected: 0.77f, session.Volume);
        Assert.True(session.IsMuted);
    }

    [Fact]
    public void ReconcileRetiresControlInvalidatedByDefaultDeviceSwitch()
    {
        FakeAudioSessionControl control = new() { CurrentState = AudioSessionState.Active, CurrentVolume = 1f };
        using AsyncThrottler<string> throttler = new(0);
        using AudioSession session = new(control, Dispatcher.UIThread, throttler);
        control.IsDeviceInvalidated = true;

        AudioSessionReconciliationResult result = session.ReconcileWithCoreAudio();

        Assert.Equal(AudioSessionReconciliationResult.DeviceInvalidated, result);
        Assert.True(session.IsDisconnected);
    }

    [Fact]
    public void ReconcileReportsAlreadyExpiredControl()
    {
        FakeAudioSessionControl control = new()
        {
            CurrentState = AudioSessionState.Inactive,
            CurrentVolume = 0.5f
        };
        using AsyncThrottler<string> throttler = new(0);
        using AudioSession session = new(control, Dispatcher.UIThread, throttler);
        control.CurrentState = AudioSessionState.Expired;

        AudioSessionReconciliationResult result = session.ReconcileWithCoreAudio();

        Assert.Equal(AudioSessionReconciliationResult.Expired, result);
        Assert.Equal(AudioSessionState.Expired, session.State);
    }

    [Fact]
    public async Task VolumeThrottleReportsDeferredWriteFailure()
    {
        using AsyncThrottler<string> throttler = new(0);
        VolumeThrottle volumeThrottle = new(throttler, key: "test-session");
        InvalidOperationException expectedException = new("Write failed");
        TaskCompletionSource<Exception> failureSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        volumeThrottle.Write(
            value: 0.5f,
            (_, _) => throw expectedException,
            exception => failureSource.TrySetResult(exception));

        Exception actualException = await failureSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(expectedException, actualException);
    }

    private sealed class FakeAudioSessionControl :
        IAudioSessionControl,
        IAudioSessionControl2,
        ISimpleAudioVolume,
        IAudioMeterInformation
    {
        public AudioSessionState CurrentState { get; set; }
        public float CurrentVolume { get; set; }
        public bool CurrentMute { get; set; }
        public bool IsDeviceInvalidated { get; set; }

        public void GetState(out AudioSessionState state)
        {
            ThrowIfDeviceInvalidated();
            state = CurrentState;
        }

        public void GetDisplayName(out string displayName) => displayName = "Test session";

        public void SetDisplayName(string displayName, ref Guid eventContext)
        {
        }

        public void GetIconPath(out string iconPath) => iconPath = string.Empty;

        public void SetIconPath(string iconPath, ref Guid eventContext)
        {
        }

        public void GetGroupingParam(out Guid groupingParameter) => groupingParameter = Guid.Empty;

        public void SetGroupingParam(ref Guid groupingParameter, ref Guid eventContext)
        {
        }

        public void RegisterAudioSessionNotification(IAudioSessionEvents notifications)
        {
        }

        public void UnregisterAudioSessionNotification(IAudioSessionEvents notifications)
        {
        }

        public void GetSessionIdentifier(out string sessionIdentifier) => sessionIdentifier = "test-session";

        public void GetSessionInstanceIdentifier(out string? sessionInstanceIdentifier) =>
            sessionInstanceIdentifier = "test-session-instance";

        public void GetProcessId(out uint processID)
        {
            ThrowIfDeviceInvalidated();
            processID = uint.MaxValue;
        }

        public int IsSystemSoundsSession() => 1;

        public void SetDuckingPreference(bool optOut)
        {
        }

        public void SetMasterVolume(float level, ref Guid eventContext) => CurrentVolume = level;

        public void GetMasterVolume(out float level)
        {
            ThrowIfDeviceInvalidated();
            level = CurrentVolume;
        }

        public void SetMute(bool isMuted, ref Guid eventContext) => CurrentMute = isMuted;

        public void GetMute(out bool isMuted)
        {
            ThrowIfDeviceInvalidated();
            isMuted = CurrentMute;
        }

        public void GetPeakValue(out float peakValue) => peakValue = 0f;

        public void GetMeteringChannelCount(out uint channelCount) => channelCount = 0;

        public int GetChannelsPeakValues(uint channelCount, IntPtr peakValues) => 0;

        public void QueryHardwareSupport(out uint hardwareSupportMask) => hardwareSupportMask = 0;

        private void ThrowIfDeviceInvalidated()
        {
            if (IsDeviceInvalidated)
                throw new COMException(message: "The audio endpoint was invalidated.", AudioClientDeviceInvalidated);
        }
    }
}
