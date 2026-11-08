using System.Diagnostics;
using System.Security.Principal;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Services.Install;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class ScheduledTaskSmokeTests
{
    // Registering a task that runs elevated needs an elevated test run, and the probe flashes a console window
    private const string SmokeTestEnvironmentVariable = "TRAYAPPDOTNET_RUN_SCHEDULED_TASK_TEST";

    private const int OutputTimeoutMilliseconds = 15_000;

    [EnvironmentFact(SmokeTestEnvironmentVariable)]
    public void RegistersRunsAndDeletesTasksThroughTaskScheduler()
    {
        string folderName = "TrayAppDotNET.Tests-" + Guid.NewGuid().ToString("N");
        string userSid = CurrentUserSid();
        string commandProcessor = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        string outputPath = Path.Combine(Path.GetTempPath(), folderName + ".txt");

        using ComApartmentScope apartment = ComApartmentScope.Enter(Ole32.COINIT_APARTMENTTHREADED);
        Assert.True(apartment.IsUsable);
        AssertSucceeded(TaskSchedulerSession.TryConnect(out TaskSchedulerSession? connected));
        using TaskSchedulerSession session = Assert.IsType<TaskSchedulerSession>(connected);
        string securityDescriptor = TrayAppDotNETElevatedStartTask.TaskSecurityDescriptor(userSid);
        TrayAppDotNETElevatedStartTask elevatedStart = new("TaskManagerTrayAppDotNET", () => commandProcessor);
        try
        {
            // The app's own definition, read back the way Task Scheduler stores it
            string definition = elevatedStart.BuildTaskDefinition(commandProcessor, userSid);
            AssertSucceeded(session.RegisterTask(folderName, "App", definition, userSid, securityDescriptor));
            AssertSucceeded(session.TryOpenTask(folderName, "App", out ScheduledTask? appTask));
            using (appTask)
            {
                AssertSucceeded(Assert.IsType<ScheduledTask>(appTask).TryGetXml(out string? registered));
                Assert.True(TrayAppDotNETElevatedStartTask.IsUsableDefinition(registered, commandProcessor));
            }

            // A probe that records the command line Task Scheduler builds from $(Arg0)
            AssertSucceeded(session.RegisterTask(
                folderName,
                "Probe",
                ProbeDefinition(commandProcessor, outputPath, userSid),
                userSid,
                securityDescriptor));
            AssertSucceeded(session.TryOpenTask(folderName, "Probe", out ScheduledTask? probeTask));
            using (probeTask)
                AssertSucceeded(Assert.IsType<ScheduledTask>(probeTask).Run("--autostart --hidden"));
            Assert.Contains("rem --marker --autostart --hidden", WaitForOutput(outputPath), StringComparison.Ordinal);

            // Deleting by name leaves the other task, and a folder that still holds a task is kept
            AssertSucceeded(session.DeleteTasks(folderName, name => name == "App"));
            Assert.True(TaskSchedulerSession.IsNotFound(session.TryOpenTask(folderName, "App", out _)));
            Assert.True(TaskSchedulerSession.IsNotEmpty(session.DeleteFolder(folderName)));

            AssertSucceeded(session.DeleteTask(folderName, "Probe"));
            AssertSucceeded(session.DeleteFolder(folderName));
            Assert.True(TaskSchedulerSession.IsNotFound(session.DeleteFolder(folderName)));
        }
        finally
        {
            _ = session.DeleteTasks(folderName, _ => true);
            _ = session.DeleteFolder(folderName);
            File.Delete(outputPath);
        }
    }

    private static string ProbeDefinition(string commandProcessor, string outputPath, string userSid) =>
        $"""
         <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
           <Principals>
             <Principal id="Author">
               <UserId>{userSid}</UserId>
               <LogonType>InteractiveToken</LogonType>
             </Principal>
           </Principals>
           <Settings>
             <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
             <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
           </Settings>
           <Actions Context="Author">
             <Exec>
               <Command>{commandProcessor}</Command>
               <Arguments>/v:on /d /c echo !CMDCMDLINE!&gt;"{outputPath}"&amp;rem --marker $(Arg0)</Arguments>
             </Exec>
           </Actions>
         </Task>
         """;

    private static string WaitForOutput(string path)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < OutputTimeoutMilliseconds)
        {
            try
            {
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path);
                    if (text.Contains("--marker", StringComparison.Ordinal)) return text;
                }
            }
            catch (IOException)
            {
                // The probe is still writing
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException($"The probe task wrote nothing to {path}.");
    }

    private static string CurrentUserSid()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? throw new InvalidOperationException("The current account has no SID.");
    }

    private static void AssertSucceeded(int hResult) => Assert.True(hResult >= 0, $"HRESULT 0x{hResult:X8}");
}
