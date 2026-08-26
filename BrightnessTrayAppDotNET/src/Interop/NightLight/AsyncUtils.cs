using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace BrightnessTrayAppDotNET.Interop.NightLight;

public static class AsyncUtils
{
    private const int RegNotifyChangeLastSet = 0x4;

    public static Task<bool> WaitOneAsync(WaitHandle handle, int timeoutMs)
    {
        ArgumentNullException.ThrowIfNull(handle);

        WaitRegistrationState state = new();
        RegisteredWaitHandle registeredWaitHandle = ThreadPool.RegisterWaitForSingleObject(
            waitObject: handle,
            callBack: static (callbackState, timedOut) =>
            {
                WaitRegistrationState registrationState = (WaitRegistrationState)callbackState!;
                registrationState.Complete(!timedOut);
            },
            state: state,
            millisecondsTimeOutInterval: timeoutMs,
            executeOnlyOnce: true);
        state.SetRegistration(registeredWaitHandle);
        return state.Task;
    }

    private sealed class WaitRegistrationState
    {
        private readonly TaskCompletionSource<bool> _taskCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private RegisteredWaitHandle? _registeredWaitHandle;
        private int _completed;

        public Task<bool> Task => _taskCompletionSource.Task;

        public void SetRegistration(RegisteredWaitHandle registeredWaitHandle)
        {
            if (Volatile.Read(ref _completed) != 0)
            {
                registeredWaitHandle.Unregister(null);
                return;
            }

            RegisteredWaitHandle? previous =
                Interlocked.CompareExchange(ref _registeredWaitHandle, registeredWaitHandle, null);
            if (previous != null)
            {
                registeredWaitHandle.Unregister(null);
                return;
            }

            // The wait can fire before RegisterWaitForSingleObject returns its registration handle.
            if (Volatile.Read(ref _completed) != 0)
                Unregister();
        }

        public void Complete(bool signaled)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0) return;

            Unregister();
            _taskCompletionSource.TrySetResult(signaled);
        }

        private void Unregister()
        {
            RegisteredWaitHandle? registeredWaitHandle =
                Interlocked.Exchange(ref _registeredWaitHandle, null);
            registeredWaitHandle?.Unregister(null);
        }
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
                TADNLog.Log(
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
                    TADNLog.Log(
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
                    TADNLog.Log(
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
