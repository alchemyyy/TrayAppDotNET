using BrightnessTrayAppDotNET.Interop.NightLight;

namespace BrightnessTrayAppDotNET.Tests.NightLight;

/// <summary>
/// Manual Night Light probes against the production code paths:
///   cloudstore - exercises NightLightCloudStore.SaveSettingsKelvin.
///   chain      - band-by-band chain probe (singleton state + registry LastWriteTime).
/// It also serves as the helper process when the production code starts one.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (NightLightHelperServer.TryRun(args, out int helperExitCode))
            return helperExitCode;

        string mode = args.Length > 0 ? args[0] : string.Empty;
        if (string.Equals(mode, "cloudstore", StringComparison.OrdinalIgnoreCase)) return CloudStoreTester.Run();
        if (string.Equals(mode, "chain", StringComparison.OrdinalIgnoreCase)) return ChainProbeTester.Run();

        ConsoleLog.Error("Usage: NightLightTester cloudstore|chain");
        return 2;
    }
}
