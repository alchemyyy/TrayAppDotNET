namespace BrightnessTrayAppDotNET.Tests.NightLight;

/// <summary>
/// Direct driver for <see cref="NightLightSettingsHandlerTester"/>. Initialises the
/// SettingsHandlers_Display vtable pipeline and walks the kelvin slider through
/// 0% -> 20% -> 40% -> 80% -> 100% -> 0% with 500ms between each step. Every step
/// emits the value+IsDragging bracket and reads back the registry strength so the
/// console output shows whether the SetValue chain actually moved the slider.
/// </summary>
internal static class Program
{
    private static readonly int[] StrengthSequence = [0, 20, 40, 80, 100, 0];
    private const int StepDelayMs = 500;

    private static int Main(string[] args)
    {
        // Mode selector:
        //   "cloudstore" => exercise NightLightCloudStore.SaveSettingsKelvin.
        //   "chain"      => band-by-band chain probe (singleton state + registry LastWriteTime).
        //   anything else => the original SettingsHandler vtable sweep below.
        if (args.Length > 0 && string.Equals(args[0], "cloudstore", StringComparison.OrdinalIgnoreCase)) return CloudStoreTester.Run();
        if (args.Length > 0 && string.Equals(args[0], "chain", StringComparison.OrdinalIgnoreCase)) return ChainProbeTester.Run();

        ConsoleLog.Header("NightLightSettingsHandler tester");
        ConsoleLog.Info($"PID={Environment.ProcessId}, apartment={Thread.CurrentThread.GetApartmentState()}");

        try
        {
            if (!NightLightSettingsHandlerTester.IsSupported())
            {
                ConsoleLog.Error(
                    "Backend init reported unsupported. See trace above for the failing step. Aborting.");
                return 1;
            }

            int initialStrength = NightLightSettingsHandlerTester.GetStrengthFromRegistry();
            bool initialEnabled = NightLightSettingsHandlerTester.IsEnabledFromRegistry();
            ConsoleLog.Info($"Initial state: enabled={initialEnabled}, strength={initialStrength}%");
            if (!initialEnabled)
            {
                ConsoleLog.Warn(
                    "Night Light is OFF. Underlying SetValue calls will still be logged, "
                    + "but you won't see the screen tint change. Enable Night Light from Settings "
                    + "to observe the visual effect.");
            }

            ConsoleLog.Info(
                $"Driving sequence: {string.Join(" -> ", StrengthSequence)} "
                + $"with {StepDelayMs}ms between steps.");

            for (int i = 0; i < StrengthSequence.Length; i++)
            {
                int target = StrengthSequence[i];
                ConsoleLog.Header($"Step {i + 1}/{StrengthSequence.Length}: target = {target}%");

                bool ok = NightLightSettingsHandlerTester.DriveSetValueBracket(target);
                ConsoleLog.Info($"DriveSetValueBracket({target}) returned {ok}");

                Thread.Sleep(StepDelayMs);

                int observed = NightLightSettingsHandlerTester.GetStrengthFromRegistry();
                int delta = observed - target;
                if (delta == 0)
                    ConsoleLog.Ok($"Readback {observed}% matches target.");
                else
                {
                    ConsoleLog.Warn(
                        $"Readback {observed}% (delta = {delta:+#;-#;0} from target {target}%). "
                        + "Note: production registry-hammer path bypasses the SettingsHandler vtable; "
                        + "a non-zero delta here is consistent with the broker dropping isolated SetValue calls.");
                }
            }

            int finalStrength = NightLightSettingsHandlerTester.GetStrengthFromRegistry();
            ConsoleLog.Header("Sweep complete");
            ConsoleLog.Info($"Final strength={finalStrength}%, enabled={NightLightSettingsHandlerTester.IsEnabledFromRegistry()}");
            return 0;
        }
        catch (Exception ex)
        {
            ConsoleLog.Error($"Unhandled exception: {ex.GetType().Name}: {ex.Message}");
            ConsoleLog.Error(ex.StackTrace ?? "(no stack)");
            return 1;
        }
        finally
        {
            NightLightSettingsHandlerTester.Dispose();
        }
    }
}
