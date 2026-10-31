using Avalonia.Threading;
using TrayAppDotNETCommon.Services;
using VolumeTrayAppDotNET.Audio;
using VolumeTrayAppDotNET.Interop;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class ComProxyGateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RetireWaitsForACallAlreadyInFlight()
    {
        ComProxyGate<object> gate = new();
        gate.Publish(new object());
        using ManualResetEventSlim entered = new();
        using ManualResetEventSlim release = new();

        Task call = Task.Run(() => gate.TryInvoke(0, (_, _) =>
        {
            entered.Set();
            release.Wait();
            return true;
        }, out _));
        Assert.True(entered.Wait(Timeout));

        Task retire = Task.Run(() => gate.Retire());
        Assert.False(await FinishesWithin(retire, TimeSpan.FromMilliseconds(200)));

        release.Set();
        await Task.WhenAll(call, retire).WaitAsync(Timeout);
    }

    [Fact]
    public void NoCallReachesRetiredProxies()
    {
        ComProxyGate<object> gate = new();
        object proxies = new();
        gate.Publish(proxies);

        Assert.Same(proxies, gate.Retire());
        Assert.False(gate.TryInvoke(0, static (_, _) => throw new InvalidOperationException("called after retire"), out int _));
        Assert.Null(gate.Current);
    }

    [Fact]
    public async Task SessionDisposeWaitsForAMeterReadAlreadyInFlight()
    {
        BlockingMeterSessionControl control = new();
        using AsyncThrottler<string> throttler = new(0);
        AudioSession session = new(control, Dispatcher.UIThread, throttler);

        Task sampling = Task.Run(() => session.UpdatePeakValueBackground(unified: false, biasMultiplier: 0));
        Assert.True(control.MeterEntered.Wait(Timeout));

        Task disposing = Task.Run(session.Dispose);
        Assert.False(await FinishesWithin(disposing, TimeSpan.FromMilliseconds(200)));

        control.ReleaseMeter.Set();
        await Task.WhenAll(sampling, disposing).WaitAsync(Timeout);
        Assert.Equal(expected: 1, control.MeterCalls);
    }

    private static async Task<bool> FinishesWithin(Task task, TimeSpan delay) =>
        await Task.WhenAny(task, Task.Delay(delay)) == task;

    private sealed class BlockingMeterSessionControl :
        IAudioSessionControl,
        IAudioSessionControl2,
        ISimpleAudioVolume,
        IAudioMeterInformation
    {
        private int _meterCalls;

        public ManualResetEventSlim MeterEntered { get; } = new();
        public ManualResetEventSlim ReleaseMeter { get; } = new();
        public int MeterCalls => Volatile.Read(ref _meterCalls);

        public void GetState(out AudioSessionState state) => state = AudioSessionState.Active;

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

        public void GetSessionIdentifier(out string sessionIdentifier) => sessionIdentifier = "blocking-session";

        public void GetSessionInstanceIdentifier(out string? sessionInstanceIdentifier) =>
            sessionInstanceIdentifier = "blocking-session-instance";

        // PID 0 is never process-watched, so the test stays free of real processes
        public void GetProcessId(out uint processID) => processID = 0;

        public int IsSystemSoundsSession() => 1;

        public void SetDuckingPreference(bool optOut)
        {
        }

        public void SetMasterVolume(float level, ref Guid eventContext)
        {
        }

        public void GetMasterVolume(out float level) => level = 1f;

        public void SetMute(bool isMuted, ref Guid eventContext)
        {
        }

        public void GetMute(out bool isMuted) => isMuted = false;

        public void GetPeakValue(out float peakValue) => peakValue = 0.5f;

        public void GetMeteringChannelCount(out uint channelCount)
        {
            Interlocked.Increment(ref _meterCalls);
            MeterEntered.Set();
            ReleaseMeter.Wait();
            channelCount = 1;
        }

        public int GetChannelsPeakValues(uint channelCount, IntPtr peakValues) => 0;

        public void QueryHardwareSupport(out uint hardwareSupportMask) => hardwareSupportMask = 0;
    }
}
