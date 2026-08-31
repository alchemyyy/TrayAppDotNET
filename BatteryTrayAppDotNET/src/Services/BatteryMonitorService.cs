using System.Runtime.InteropServices;
using Windows.Devices.Power;
using Avalonia.Threading;

namespace BatteryTrayAppDotNET.Services;

public sealed class BatteryMonitorService : IDisposable
{
    private const int PollIntervalMs = 5_000;
    private const byte BatteryFlagCharging = 0x08;
    private const byte BatteryFlagNoSystemBattery = 0x80;
    private const byte BatteryFlagUnknown = 0xFF;
    private const byte BatteryLifePercentUnknown = 0xFF;
    private const uint BatteryLifeTimeUnknown = 0xFFFFFFFF;

    private readonly SemaphoreSlim _pollGate = new(initialCount: 1, maxCount: 1);
    private readonly Lock _lifetimeGate = new();
    private CancellationTokenSource? _pollingCancellationToken;
    private Task? _pollTask;
    private Task? _forceRefreshTask;
    private bool _disposed;

    public BatterySnapshot Snapshot { get; private set; } = BatterySnapshot.Unknown;

    public event Action? StateChanged;

    public void Start()
    {
        lock (_lifetimeGate)
        {
            if (_disposed) return;
            if (_pollingCancellationToken != null) return;

            _pollingCancellationToken = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollLoopAsync(_pollingCancellationToken.Token));
        }

        ForceRefresh();
    }

    public void ForceRefresh()
    {
        lock (_lifetimeGate)
        {
            if (_disposed) return;
            if (_pollingCancellationToken == null) return;
            if (_forceRefreshTask is { IsCompleted: false }) return;

            _forceRefreshTask = PollOnceAsync(_pollingCancellationToken.Token);
        }
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await PollOnceAsync(token);

            try { await Task.Delay(PollIntervalMs, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollOnceAsync(CancellationToken token)
    {
        if (_disposed) return;
        if (!await _pollGate.WaitAsync(millisecondsTimeout: 0, token)) return;

        try
        {
            BatterySnapshot snapshot = await Task.Run(CreateSnapshot, token);
            if (_disposed) return;

            await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    Snapshot = snapshot;
                    StateChanged?.Invoke();
                },
                DispatcherPriority.Normal,
                token);
        }
        catch (OperationCanceledException)
        {
            // Normal during shutdown.
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryMonitorService.PollOnceAsync: {ex}");
        }
        finally
        {
            _pollGate.Release();
        }
    }

    private static BatterySnapshot CreateSnapshot()
    {
        PowerStatus powerStatus = GetWindowsPowerStatus();
        BatteryReportSnapshot report = GetBatteryReportSnapshot();

        bool batteryPresent = report.BatteryPresent || powerStatus.BatteryPresent;
        bool isOnExternalPower = powerStatus.IsOnExternalPower
                                 ?? report.IsOnExternalPower
                                 ?? !batteryPresent;
        bool isCharging = report.IsCharging || powerStatus.IsCharging;
        bool isFullyCharged = batteryPresent
                              && isOnExternalPower
                              && !isCharging
                              && (powerStatus.ChargePercentage ?? report.ChargePercentage ?? 0) >= 100;

        if (!batteryPresent)
        {
            isOnExternalPower = true;
            isCharging = false;
            isFullyCharged = false;
        }
        else if (!isOnExternalPower)
        {
            isCharging = false;
            isFullyCharged = false;
        }

        int chargePercentage = Math.Clamp(
            powerStatus.ChargePercentage ?? report.ChargePercentage ?? (batteryPresent ? 0 : 100),
            min: 0,
            max: 100);

        float? chargeRate = null;
        float? dischargeRate = null;
        if (report.ChargeRateWatts is { } rate)
        {
            switch (rate)
            {
                case > 0:
                    chargeRate = rate;
                    break;
                case < 0:
                    dischargeRate = Math.Abs(rate);
                    break;
            }
        }

        return new BatterySnapshot(
            batteryPresent,
            chargePercentage,
            isOnExternalPower,
            isCharging,
            isFullyCharged,
            chargeRate,
            isOnExternalPower ? null : dischargeRate,
            report.DesignedCapacityMilliwattHours,
            report.FullChargeCapacityMilliwattHours,
            report.RemainingCapacityMilliwattHours,
            powerStatus.EstimatedTimeRemaining,
            powerStatus.EnergySaverEnabled);
    }

    private static BatteryReportSnapshot GetBatteryReportSnapshot()
    {
        try
        {
            BatteryReport report = Battery.AggregateBattery.GetReport();
            int? remaining = report.RemainingCapacityInMilliwattHours;
            int? full = report.FullChargeCapacityInMilliwattHours;
            int? percent = remaining.HasValue && full is > 0
                ? (int)Math.Round(remaining.Value * 100.0 / full.Value)
                : null;

            string status = report.Status.ToString();
            return new BatteryReportSnapshot(
                !string.Equals(status, b: "NotPresent", StringComparison.Ordinal),
                string.Equals(status, b: "Charging", StringComparison.Ordinal)
                || string.Equals(status, b: "Idle", StringComparison.Ordinal),
                string.Equals(status, b: "Charging", StringComparison.Ordinal),
                percent,
                MilliwattsToWatts(report.ChargeRateInMilliwatts),
                report.DesignCapacityInMilliwattHours,
                report.FullChargeCapacityInMilliwattHours,
                report.RemainingCapacityInMilliwattHours);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryMonitorService.GetBatteryReportSnapshot: {ex.Message}");
            return BatteryReportSnapshot.Unknown;
        }
    }

    private static float? MilliwattsToWatts(int? milliwatts) =>
        milliwatts / 1000f;

    private static PowerStatus GetWindowsPowerStatus()
    {
        try
        {
            if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS status)) return PowerStatus.Unknown;

            bool? isOnExternalPower = status.ACLineStatus switch
            {
                0 => false,
                1 => true,
                _ => null
            };

            bool batteryPresent = status.BatteryFlag is not BatteryFlagNoSystemBattery and not BatteryFlagUnknown;
            int? chargePercentage = status.BatteryLifePercent == BatteryLifePercentUnknown
                ? null
                : status.BatteryLifePercent;
            TimeSpan? estimate = status.BatteryLifeTime == BatteryLifeTimeUnknown
                ? null
                : TimeSpan.FromSeconds(status.BatteryLifeTime);

            return new PowerStatus(
                batteryPresent,
                isOnExternalPower,
                (status.BatteryFlag & BatteryFlagCharging) != 0,
                chargePercentage,
                estimate,
                status.SystemStatusFlag != 0);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryMonitorService.GetWindowsPowerStatus: {ex.Message}");
            return PowerStatus.Unknown;
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? pollingCancellationToken;
        Task? pollTask;
        Task? forceRefreshTask;
        lock (_lifetimeGate)
        {
            if (_disposed) return;
            _disposed = true;

            pollingCancellationToken = _pollingCancellationToken;
            pollTask = _pollTask;
            forceRefreshTask = _forceRefreshTask;
            pollingCancellationToken?.Cancel();
            _pollingCancellationToken = null;
            _pollTask = null;
            _forceRefreshTask = null;
        }

        WaitForPollTask(pollTask, nameof(_pollTask));
        if (!ReferenceEquals(forceRefreshTask, pollTask))
            WaitForPollTask(forceRefreshTask, nameof(_forceRefreshTask));
        pollingCancellationToken?.Dispose();

        _pollGate.Dispose();
    }

    private static void WaitForPollTask(Task? task, string taskName)
    {
        if (task == null) return;

        try
        {
            bool completed = task.Wait(2_000);
            if (!completed) TADNLog.Log($"BatteryMonitorService.Dispose: {taskName} did not stop before timeout");
        }
        catch (AggregateException ex)
        {
            foreach (Exception inner in ex.Flatten().InnerExceptions)
            {
                if (inner is OperationCanceledException) continue;

                TADNLog.Log($"BatteryMonitorService.Dispose: {taskName}: {inner.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown/dispose path.
        }
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    private readonly record struct BatteryReportSnapshot(
        bool BatteryPresent,
        bool? IsOnExternalPower,
        bool IsCharging,
        int? ChargePercentage,
        float? ChargeRateWatts,
        float? DesignedCapacityMilliwattHours,
        float? FullChargeCapacityMilliwattHours,
        float? RemainingCapacityMilliwattHours)
    {
        public static BatteryReportSnapshot Unknown { get; } = new(
            BatteryPresent: false,
            IsOnExternalPower: null,
            IsCharging: false,
            ChargePercentage: null,
            ChargeRateWatts: null,
            DesignedCapacityMilliwattHours: null,
            FullChargeCapacityMilliwattHours: null,
            RemainingCapacityMilliwattHours: null);
    }

    private readonly record struct PowerStatus(
        bool BatteryPresent,
        bool? IsOnExternalPower,
        bool IsCharging,
        int? ChargePercentage,
        TimeSpan? EstimatedTimeRemaining,
        bool EnergySaverEnabled)
    {
        public static PowerStatus Unknown { get; } = new(
            BatteryPresent: false,
            IsOnExternalPower: null,
            IsCharging: false,
            ChargePercentage: null,
            EstimatedTimeRemaining: null,
            EnergySaverEnabled: false);
    }
}
