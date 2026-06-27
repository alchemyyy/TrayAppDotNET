using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace BrightnessTrayAppDotNET.Interop.NightLight;

public static class AsyncUtils
{
    private const int RegNotifyChangeLastSet = 0x4;

    public static Task<bool> WaitOneAsync(WaitHandle handle, int timeoutMs)
    {
        TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RegisteredWaitHandle? rwh = null;
        _ = ThreadPool.RegisterWaitForSingleObject(
            waitObject: handle,
            (_, timedOut) =>
            {
                rwh?.Unregister(null);
                tcs.TrySetResult(!timedOut);
            },
            state: null,
            millisecondsTimeOutInterval: timeoutMs,
            executeOnlyOnce: true);
        return tcs.Task;
    }

    /// <summary>
    /// Arms a one-shot <c>RegNotifyChangeKeyValue</c> on <paramref name="registryKeyPath"/>, runs
    /// <paramref name="call"/>, then asynchronously awaits the event signal. Returns when the write reaches disk
    /// (RegNotify fires) or when <paramref name="saveNotifyTimeoutMs"/> elapses, whichever comes first.
    /// Falls back to <paramref name="fallbackDwellMs"/> if the key is missing or notify registration fails.
    ///
    /// Confirms the caller's own SetValue reached disk; does NOT confirm any downstream broker has propagated
    /// the change. <paramref name="callerName"/> is prefixed onto log messages so failures can be attributed
    /// to the right call site.
    /// </summary>
    public static async Task IssueWithSaveNotifyAsync(
        string registryKeyPath,
        Action call,
        int saveNotifyTimeoutMs,
        int fallbackDwellMs,
        string callerName)
    {
        RegistryKey? key = null;
        EventWaitHandle? eventWaitHandle = null;
        bool armed = false;
        try
        {
            key = Registry.CurrentUser.OpenSubKey(registryKeyPath, writable: false);
            if (key is null)
            {
                WPFLog.Log(
                    $"{callerName}.IssueWithSaveNotifyAsync: key '{registryKeyPath}' missing;"
                    + " falling back to fixed dwell.");
            }
            else
            {
                eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
                int rc = RegNotifyChangeKeyValue(
                    key.Handle,
                    bWatchSubtree: false,
                    dwNotifyFilter: RegNotifyChangeLastSet,
                    hEvent: eventWaitHandle.SafeWaitHandle,
                    fAsynchronous: true);
                if (rc == 0)
                    armed = true;
                else
                {
                    WPFLog.Log(
                        $"{callerName}.IssueWithSaveNotifyAsync: RegNotifyChangeKeyValue rc={rc};"
                        + " falling back to fixed dwell.");
                }
            }

            call();

            if (armed && eventWaitHandle is not null)
            {
                bool signaled = await WaitOneAsync(eventWaitHandle, saveNotifyTimeoutMs).ConfigureAwait(false);
                if (!signaled)
                {
                    WPFLog.Log(
                        $"{callerName}.IssueWithSaveNotifyAsync: timeout {saveNotifyTimeoutMs}ms"
                        + " - registry write did not fire RegNotify.");
                }
            }
            else
                await Task.Delay(fallbackDwellMs).ConfigureAwait(false);
        }
        finally
        {
            eventWaitHandle?.Dispose();
            key?.Dispose();
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegNotifyChangeKeyValue(
        SafeRegistryHandle hKey,
        [MarshalAs(UnmanagedType.Bool)] bool bWatchSubtree,
        int dwNotifyFilter,
        SafeWaitHandle hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous);
}
