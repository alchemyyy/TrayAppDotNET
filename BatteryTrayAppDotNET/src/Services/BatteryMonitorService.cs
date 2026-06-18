using System.Runtime.InteropServices;
using Avalonia.Threading;
using BatteryTrayAppDotNET.Models;
using Windows.Devices.Power;

namespace BatteryTrayAppDotNET.Services;

public sealed class BatteryMonitorService : IDisposable
{
    private const int PollIntervalMs = 5_000;
    private const byte BatteryFlagCharging = 0x08;
    private const byte BatteryFlagNoSystemBattery = 0x80;
    private const byte BatteryFlagUnknown = 0xFF;
    private const byte BatteryLifePercentUnknown = 0xFF;
    private const uint BatteryLifeTimeUnknown = 0xFFFFFFFF;

    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private CancellationTokenSource? _pollingCancellationToken;
    private Task? _pollTask;
    private bool _disposed;

    public BatterySnapshot Snapshot { get; private set; } = BatterySnapshot.Unknown;

    public event Action? StateChanged;

    public void Start()
    {
        if (_pollingCancellationToken != null) return;

        _pollingCancellationToken = new CancellationTokenSource();
        _pollTask = Task.Run(() => PollLoopAsync(_pollingCancellationToken.Token));
        ForceRefresh();
    }

    public void ForceRefresh()
    {
        _ = PollOnceAsync(CancellationToken.None);
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
        if (!await _pollGate.WaitAsync(0, token)) return;

        try
        {
            BatterySnapshot snapshot = await Task.Run(CreateSnapshot, token);
            if (_disposed) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Snapshot = snapshot;
                StateChanged?.Invoke();
            });
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
            0,
            100);

        float? chargeRate = null;
        float? dischargeRate = null;
        if (report.ChargeRateWatts is { } rate)
        {
            if (rate > 0) chargeRate = rate;
            else if (rate < 0) dischargeRate = Math.Abs(rate);
        }

        return new BatterySnapshot(
            BatteryPresent: batteryPresent,
            ChargePercentage: chargePercentage,
            IsOnExternalPower: isOnExternalPower,
            IsCharging: isCharging,
            IsFullyCharged: isFullyCharged,
            ChargeRateWatts: chargeRate,
            DischargeRateWatts: isOnExternalPower ? null : dischargeRate,
            DesignedCapacityMilliwattHours: report.DesignedCapacityMilliwattHours,
            FullChargeCapacityMilliwattHours: report.FullChargeCapacityMilliwattHours,
            RemainingCapacityMilliwattHours: report.RemainingCapacityMilliwattHours,
            WindowsEstimatedTimeRemaining: powerStatus.EstimatedTimeRemaining);
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
                BatteryPresent: !string.Equals(status, "NotPresent", StringComparison.Ordinal),
                IsOnExternalPower: string.Equals(status, "Charging", StringComparison.Ordinal)
                    || string.Equals(status, "Idle", StringComparison.Ordinal),
                IsCharging: string.Equals(status, "Charging", StringComparison.Ordinal),
                ChargePercentage: percent,
                ChargeRateWatts: MilliwattsToWatts(report.ChargeRateInMilliwatts),
                DesignedCapacityMilliwattHours: report.DesignCapacityInMilliwattHours,
                FullChargeCapacityMilliwattHours: report.FullChargeCapacityInMilliwattHours,
                RemainingCapacityMilliwattHours: report.RemainingCapacityInMilliwattHours);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"BatteryMonitorService.GetBatteryReportSnapshot: {ex.Message}");
            return BatteryReportSnapshot.Unknown;
        }
    }

    private static float? MilliwattsToWatts(int? milliwatts) =>
        milliwatts.HasValue ? milliwatts.Value / 1000f : null;

    private static PowerStatus GetWindowsPowerStatus()
    {
        try
        {
            if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS status)) return PowerStatus.Unknown;

            bool? isOnExternalPower = status.ACLineStatus switch
            {
                0 => false,
                1 => true,
                _ => null,
            };

            bool batteryPresent = status.BatteryFlag is not BatteryFlagNoSystemBattery and not BatteryFlagUnknown;
            int? chargePercentage = status.BatteryLifePercent == BatteryLifePercentUnknown
                ? null
                : status.BatteryLifePercent;
            TimeSpan? estimate = status.BatteryLifeTime == BatteryLifeTimeUnknown
                ? null
                : TimeSpan.FromSeconds(status.BatteryLifeTime);

            return new PowerStatus(
                BatteryPresent: batteryPresent,
                IsOnExternalPower: isOnExternalPower,
                IsCharging: (status.BatteryFlag & BatteryFlagCharging) != 0,
                ChargePercentage: chargePercentage,
                EstimatedTimeRemaining: estimate);
        }
        catch
        {
            return PowerStatus.Unknown;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_pollingCancellationToken != null)
        {
            _pollingCancellationToken.Cancel();
            try { _pollTask?.Wait(2_000); }
            catch { }
            _pollingCancellationToken.Dispose();
        }

        _pollGate.Dispose();
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
        TimeSpan? EstimatedTimeRemaining)
    {
        public static PowerStatus Unknown { get; } = new(
            BatteryPresent: false,
            IsOnExternalPower: null,
            IsCharging: false,
            ChargePercentage: null,
            EstimatedTimeRemaining: null);
    }
}
