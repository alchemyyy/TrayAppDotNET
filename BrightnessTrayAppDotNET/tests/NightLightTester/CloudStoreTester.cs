using BrightnessTrayAppDotNET.Interop.NightLight;

namespace BrightnessTrayAppDotNET.Tests.NightLight;

/// <summary>
/// Drives <see cref="NightLightCloudStore.SaveSettingsKelvin"/> through a sweep so we can
/// see whether ICloudStore::Save returns S_OK or 0x80070490 for various percent values.
/// Also reads the registry strength after each step to confirm the value actually landed.
/// </summary>
internal static class CloudStoreTester
{
    private static readonly int[] Sequence = [0, 25, 50, 75, 100, 0];
    private const int StepDelayMs = 400;
    // Rapid-fire sweep: 0..100 in 1% steps, then 100..0, ~100Hz. Mimics the worst-case slider drag
    // the throttler would feed into the backend. We want to see whether SHTaskPool tag-258 dedup
    // drops intermediate writes and whether the final readback matches the last value sent.
    private const int RapidStepDelayMs = 10;

    public static int Run()
    {
        ConsoleLog.Header("NightLightCloudStore tester");
        ConsoleLog.Info($"PID={Environment.ProcessId}, apartment={Thread.CurrentThread.GetApartmentState()}");

        if (!NightLightCloudStore.IsSupported())
        {
            ConsoleLog.Error("NightLightCloudStore.IsSupported() returned false. Aborting.");
            return 1;
        }
        ConsoleLog.Ok("Backend reports IsSupported=true.");

        int initial = NightLightRegistry.GetStrength();
        bool enabled = NightLightRegistry.IsEnabled();
        ConsoleLog.Info($"Initial registry: enabled={enabled}, strength={initial}%");

        ConsoleLog.Header("Pass 1: NightLightCloudStore.SaveSettingsKelvin");
        for (int i = 0; i < Sequence.Length; i++)
        {
            int target = Sequence[i];
            ConsoleLog.Section($"CloudStore step {i + 1}/{Sequence.Length}: target={target}%");
            bool ok = NightLightCloudStore.SaveSettingsKelvin(target);
            ConsoleLog.Info($"  SaveSettingsKelvin returned {ok}");

            Thread.Sleep(StepDelayMs);
            int observed = NightLightRegistry.GetStrength();
            int delta = observed - target;
            ConsoleLog.Info($"  Registry readback {observed}% (delta {delta:+#;-#;0})");
        }

        ConsoleLog.Header("Pass 2: NightLightRegistry.SetStrength");
        for (int i = 0; i < Sequence.Length; i++)
        {
            int target = Sequence[i];
            ConsoleLog.Section($"Registry step {i + 1}/{Sequence.Length}: target={target}%");
            bool ok = NightLightRegistry.SetStrength(target);
            ConsoleLog.Info($"  SetStrength returned {ok}");

            Thread.Sleep(StepDelayMs);
            int observed = NightLightRegistry.GetStrength();
            int delta = observed - target;
            ConsoleLog.Info($"  Registry readback {observed}% (delta {delta:+#;-#;0})");
        }

        ConsoleLog.Header("Pass 3: SHTaskPool stress (rapid-fire SetTargetColorTemperature)");
        ConsoleLog.Info($"Sweeping 0..100..0 at {RapidStepDelayMs}ms cadence (~{1000 / RapidStepDelayMs}Hz).");
        int callsIssued = 0;
        for (int p = 0; p <= 100; p++)
        {
            NightLightCloudStore.SaveSettingsKelvin(p);
            callsIssued++;
            Thread.Sleep(RapidStepDelayMs);
        }
        for (int p = 100; p >= 0; p--)
        {
            NightLightCloudStore.SaveSettingsKelvin(p);
            callsIssued++;
            Thread.Sleep(RapidStepDelayMs);
        }
        ConsoleLog.Info($"Issued {callsIssued} SetTargetColorTemperature calls");

        // Wait for SHTaskPool to drain.
        Thread.Sleep(1500);
        int finalRapid = NightLightRegistry.GetStrength();
        if (finalRapid == 0)
            ConsoleLog.Ok($"Final readback {finalRapid}% matches the last requested value (0%).");
        else
            ConsoleLog.Warn($"Final readback {finalRapid}% (expected 0%). SHTaskPool may have dropped the last call.");

        ConsoleLog.Header("Pass 4: NightLightSettingsHandler.SetStrength (throttler path used in prod)");
        // The flyout calls NightLightProvider.SetStrength which calls NightLightSettingsHandler.SetStrength.
        // That goes through AsyncThrottler with a 100ms cooldown, so a quick sequence of distinct values
        // produces at most ~10 actual SHTaskPool tasks per second. Verify the final value still lands.
        int[] sliderTrajectory = [10, 25, 40, 55, 70, 85, 100, 85, 70, 55, 40, 25, 10, 0];
        foreach (int p in sliderTrajectory)
        {
            NightLightSettingsHandler.SetStrength(p);
            Thread.Sleep(15);
        }
        Thread.Sleep(1500);  // throttler + SHTaskPool drain
        int finalThrottled = NightLightRegistry.GetStrength();
        if (finalThrottled == 0)
            ConsoleLog.Ok($"Throttled trajectory final readback {finalThrottled}% matches last value (0%).");
        else
            ConsoleLog.Warn($"Throttled trajectory final readback {finalThrottled}% (expected 0%).");

        ConsoleLog.Header("Sweep complete");
        ConsoleLog.Info($"Final registry strength={NightLightRegistry.GetStrength()}%");
        return 0;
    }
}
