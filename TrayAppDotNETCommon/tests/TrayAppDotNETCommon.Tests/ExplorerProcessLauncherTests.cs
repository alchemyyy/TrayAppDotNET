using System.Diagnostics;
using TrayAppDotNETCommon.Services;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class ExplorerProcessLauncherTests
{
    // Opt-in: the test starts a process through the desktop Explorer
    private const string ExplorerLaunchSmokeTestEnvironmentVariable =
        "TRAYAPPDOTNET_RUN_EXPLORER_LAUNCH_TEST";

    [EnvironmentFact(ExplorerLaunchSmokeTestEnvironmentVariable)]
    public void ExplorerShellLaunchUsesExplorerAsTheProcessParent()
    {
        DirectoryInfo testDirectory = Directory.CreateTempSubdirectory("TmtadnParent-");
        string processName = "TmtadnProbe" + Guid.NewGuid().ToString(format: "N")[..8];
        string executablePath = Path.Combine(testDirectory.FullName, processName + ".exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, path2: "ping.exe"), executablePath);

        Process? process = null;
        try
        {
            bool launched = false;
            int launchError = 0;
            string launchErrorMessage = string.Empty;
            Thread launchThread = new(() =>
            {
                launched = ExplorerProcessLauncher.TryShellExecute(
                    executablePath,
                    arguments: "127.0.0.1 -n 30",
                    workingDirectory: testDirectory.FullName,
                    verb: "open",
                    out launchError,
                    out launchErrorMessage);
            })
            {
                IsBackground = true,
                Name = "Explorer parent smoke test launcher"
            };
            launchThread.SetApartmentState(ApartmentState.STA);
            launchThread.Start();
            Assert.True(launchThread.Join(TimeSpan.FromSeconds(10)));
            Assert.True(launched, $"{launchError}: {launchErrorMessage}");

            process = TestProcesses.WaitForProcess(processName, TimeSpan.FromSeconds(10));
            int parentProcessID = TestProcesses.ReadParentProcessID(process.Id);
            Assert.NotEqual(Environment.ProcessId, parentProcessID);
            using Process parentProcess = Process.GetProcessById(parentProcessID);
            Assert.Equal("explorer", parentProcess.ProcessName, ignoreCase: true);
        }
        finally
        {
            if (process != null)
            {
                TestProcesses.TerminateIfRunning(process);
                process.Dispose();
            }

            testDirectory.Delete(recursive: true);
        }
    }
}
