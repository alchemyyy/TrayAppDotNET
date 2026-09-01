using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

internal readonly record struct ProcessWindowGroupingFacts(
    ProcessIndependentWindowState IndependentWindowState,
    string? ApplicationUserModelID,
    bool IsApplicationUserModelIDAmbiguous);

/// <summary>Captures qualifying top-level windows once per semantic process snapshot.</summary>
internal sealed class ProcessWindowGroupingFactsCollector
{
    private const uint GetWindowOwner = 4;
    private const int WindowExtendedStyle = -20;
    private const long WindowStyleToolWindow = 0x00000080L;
    private const uint DWMWindowAttributeCloaked = 14;
    private const ushort VariantTypeStringPointer = 31;

    private static readonly Guid PropertyStoreInterfaceID = new("886D8EEB-8CF2-4446-8D02-CD88A634FC89");

    private static readonly PROPERTYKEY ApplicationUserModelIDPropertyKey = new()
    {
        FormatID = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        PropertyID = 5
    };

    private readonly NativeMethods.EnumWindowsCallback _enumerateWindowCallback;
    private readonly Dictionary<int, MutableProcessWindowFacts> _factsByProcessID = new(256);
    private bool _captureSucceeded;

    public ProcessWindowGroupingFactsCollector()
    {
        _enumerateWindowCallback = OnEnumerateWindow;
    }

    /// <summary>Replaces all prior window facts with one complete desktop enumeration.</summary>
    public void Capture()
    {
        _factsByProcessID.Clear();
        _captureSucceeded = NativeMethods.EnumWindows(_enumerateWindowCallback, IntPtr.Zero);
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
        _ = NativeMethods.GetWindowThreadProcessId(windowHandle, out uint nativeProcessID);
        if (nativeProcessID > int.MaxValue) return true;
        int processID = (int)nativeProcessID;

        WindowQualification qualification = QualifyWindow(windowHandle);
        if (qualification == WindowQualification.NotQualifying) return true;

        if (!_factsByProcessID.TryGetValue(processID, out MutableProcessWindowFacts? facts))
        {
            facts = new MutableProcessWindowFacts();
            _factsByProcessID.Add(processID, facts);
        }

        if (qualification == WindowQualification.Unknown)
        {
            facts.HadClassificationFailure = true;
            return true;
        }

        facts.HasQualifyingWindow = true;
        string? applicationUserModelID = TryReadWindowApplicationUserModelID(windowHandle);
        if (applicationUserModelID is not { Length: > 0 }) return true;
        facts.ApplicationUserModelIDs ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        facts.ApplicationUserModelIDs.Add(applicationUserModelID);
        return true;
    }

    private static WindowQualification QualifyWindow(IntPtr windowHandle)
    {
        if (!NativeMethods.IsWindow(windowHandle)
            || !NativeMethods.IsWindowVisible(windowHandle)
            || NativeMethods.GetWindow(windowHandle, GetWindowOwner) != IntPtr.Zero
            || NativeMethods.GetWindowTextLength(windowHandle) <= 0)
            return WindowQualification.NotQualifying;

        long extendedStyle = NativeMethods.GetWindowLongPtr(
            windowHandle,
            WindowExtendedStyle).ToInt64();
        if ((extendedStyle & WindowStyleToolWindow) != 0)
            return WindowQualification.NotQualifying;

        int result = NativeMethods.DwmGetWindowAttribute(
            windowHandle,
            DWMWindowAttributeCloaked,
            out int isCloaked,
            sizeof(int));
        if (result < 0) return WindowQualification.Unknown;
        return isCloaked == 0
            ? WindowQualification.Qualifying
            : WindowQualification.NotQualifying;
    }

    private static unsafe string? TryReadWindowApplicationUserModelID(IntPtr windowHandle)
    {
        Guid interfaceID = PropertyStoreInterfaceID;
        int result = NativeMethods.SHGetPropertyStoreForWindow(
            windowHandle,
            ref interfaceID,
            out IntPtr propertyStore);
        if (result < 0 || propertyStore == IntPtr.Zero) return null;

        try
        {
            void** virtualTable = *(void***)propertyStore;
            delegate* unmanaged[Stdcall]<IntPtr, PROPERTYKEY*, PROPVARIANT*, int> getValue =
                (delegate* unmanaged[Stdcall]<IntPtr, PROPERTYKEY*, PROPVARIANT*, int>)virtualTable[5];
            PROPERTYKEY propertyKey = ApplicationUserModelIDPropertyKey;
            PROPVARIANT propertyValue = default;
            try
            {
                result = getValue(propertyStore, &propertyKey, &propertyValue);
                return result >= 0
                       && propertyValue.VariantType == VariantTypeStringPointer
                       && propertyValue.PointerValue != IntPtr.Zero
                    ? Marshal.PtrToStringUni(propertyValue.PointerValue)
                    : null;
            }
            finally
            {
                _ = NativeMethods.PropVariantClear(ref propertyValue);
            }
        }
        finally
        {
            ReleasePropertyStore(propertyStore);
        }
    }

    private static unsafe void ReleasePropertyStore(IntPtr propertyStore)
    {
        void** virtualTable = *(void***)propertyStore;
        delegate* unmanaged[Stdcall]<IntPtr, uint> release =
            (delegate* unmanaged[Stdcall]<IntPtr, uint>)virtualTable[2];
        _ = release(propertyStore);
    }

    private enum WindowQualification : byte
    {
        NotQualifying,
        Qualifying,
        Unknown
    }

    private sealed class MutableProcessWindowFacts
    {
        public HashSet<string>? ApplicationUserModelIDs;
        public bool HasQualifyingWindow;
        public bool HadClassificationFailure;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid FormatID;
        public uint PropertyID;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)]
        public ushort VariantType;

        [FieldOffset(8)]
        public IntPtr PointerValue;
    }

    private static class NativeMethods
    {
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processID);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr windowHandle);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

        [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr windowHandle);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(
            IntPtr windowHandle,
            uint attribute,
            out int attributeValue,
            int attributeSize);

        [DllImport("shell32.dll")]
        public static extern int SHGetPropertyStoreForWindow(
            IntPtr windowHandle,
            ref Guid interfaceID,
            out IntPtr propertyStore);

        [DllImport("ole32.dll")]
        public static extern int PropVariantClear(ref PROPVARIANT propertyValue);
    }
}
