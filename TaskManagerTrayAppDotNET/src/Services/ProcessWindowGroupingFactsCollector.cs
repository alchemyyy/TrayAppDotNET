using System.Runtime.InteropServices;
using TaskManagerTrayAppDotNET.Interop;

namespace TaskManagerTrayAppDotNET.Services;

internal readonly record struct ProcessWindowGroupingFacts(
    ProcessIndependentWindowState IndependentWindowState,
    string? ApplicationUserModelID,
    bool IsApplicationUserModelIDAmbiguous);

/// <summary>Captures qualifying top-level windows once per semantic process snapshot.</summary>
internal sealed class ProcessWindowGroupingFactsCollector
{
    private readonly User32.EnumWindowsProc _enumerateWindowCallback;
    private readonly Dictionary<int, MutableProcessWindowFacts> _factsByProcessID = new(256);
    private bool _captureSucceeded;
    private bool _callbackFailureLogged;

    public ProcessWindowGroupingFactsCollector()
    {
        _enumerateWindowCallback = OnEnumerateWindow;
    }

    /// <summary>Replaces all prior window facts with one complete desktop enumeration.</summary>
    public void Capture()
    {
        _factsByProcessID.Clear();
        _captureSucceeded = User32.EnumWindows(_enumerateWindowCallback, IntPtr.Zero);
    }

    public ProcessWindowGroupingFacts GetFacts(int processID)
    {
        if (!_captureSucceeded)
        {
            return new ProcessWindowGroupingFacts(
                ProcessIndependentWindowState.Unknown,
                ApplicationUserModelID: null,
                IsApplicationUserModelIDAmbiguous: false);
        }

        if (!_factsByProcessID.TryGetValue(processID, out MutableProcessWindowFacts? facts))
        {
            return new ProcessWindowGroupingFacts(
                ProcessIndependentWindowState.None,
                ApplicationUserModelID: null,
                IsApplicationUserModelIDAmbiguous: false);
        }

        ProcessIndependentWindowState windowState = facts.HasQualifyingWindow
            ? ProcessIndependentWindowState.Qualifying
            : facts.HadClassificationFailure
                ? ProcessIndependentWindowState.Unknown
                : ProcessIndependentWindowState.None;
        string? applicationUserModelID = facts.ApplicationUserModelIDs?.Count == 1
            ? facts.ApplicationUserModelIDs.First()
            : null;
        return new ProcessWindowGroupingFacts(
            windowState,
            applicationUserModelID,
            facts.ApplicationUserModelIDs is { Count: > 1 });
    }

    private bool OnEnumerateWindow(IntPtr windowHandle, IntPtr parameter)
    {
        _ = parameter;
        // An exception must never unwind into user32: under Native AOT that terminates the process
        try
        {
            CollectWindow(windowHandle);
        }
        catch (Exception exception)
        {
            if (!_callbackFailureLogged)
            {
                _callbackFailureLogged = true;
                TADNLog.Log($"ProcessWindowGroupingFactsCollector: window enumeration callback failed: {exception}");
            }
        }

        return true;
    }

    private void CollectWindow(IntPtr windowHandle)
    {
        TopLevelWindowClass qualification = TopLevelWindowClassifier.Classify(windowHandle);
        if (qualification == TopLevelWindowClass.NotQualifying) return;

        // A ghost window belongs to dwm.exe; charge the hung window it covers, as the Status column does
        IntPtr hungWindowHandle = User32.HungWindowFromGhostWindow(windowHandle);
        IntPtr processWindowHandle = hungWindowHandle != IntPtr.Zero ? hungWindowHandle : windowHandle;
        _ = User32.GetWindowThreadProcessId(processWindowHandle, out uint nativeProcessID);
        if (nativeProcessID > int.MaxValue) return;
        int processID = (int)nativeProcessID;

        if (!_factsByProcessID.TryGetValue(processID, out MutableProcessWindowFacts? facts))
        {
            facts = new MutableProcessWindowFacts();
            _factsByProcessID.Add(processID, facts);
        }

        if (qualification == TopLevelWindowClass.Unknown)
        {
            facts.HadClassificationFailure = true;
            return;
        }

        facts.HasQualifyingWindow = true;
        string? applicationUserModelID = TryReadWindowApplicationUserModelID(windowHandle);
        if (applicationUserModelID is not { Length: > 0 }) return;
        facts.ApplicationUserModelIDs ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        facts.ApplicationUserModelIDs.Add(applicationUserModelID);
    }

    private static string? TryReadWindowApplicationUserModelID(IntPtr windowHandle)
    {
        Guid interfaceID = PropertyStore.InterfaceID;
        int result = NativeMethods.SHGetPropertyStoreForWindow(
            windowHandle,
            ref interfaceID,
            out IntPtr propertyStore);
        if (result < 0 || propertyStore == IntPtr.Zero) return null;

        try
        {
            return PropertyStore.TryGetString(propertyStore, PropertyStore.AppUserModelIDKey);
        }
        finally
        {
            PropertyStore.Release(propertyStore);
        }
    }

    private sealed class MutableProcessWindowFacts
    {
        public HashSet<string>? ApplicationUserModelIDs;
        public bool HasQualifyingWindow;
        public bool HadClassificationFailure;
    }

    private static class NativeMethods
    {
        [DllImport("shell32.dll")]
        public static extern int SHGetPropertyStoreForWindow(
            IntPtr windowHandle,
            ref Guid interfaceID,
            out IntPtr propertyStore);
    }
}
