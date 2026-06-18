using System.Runtime.InteropServices;

namespace VolumeTrayAppDotNET.Audio.Interop;

internal static class Advapi32
{
    // HKEY_CURRENT_USER. Stable Win32 predefined pseudo-handle (top bit set).
    public static readonly IntPtr HKEY_CURRENT_USER = new(unchecked((int)0x80000001));

    // Access rights for RegOpenKeyEx. KEY_NOTIFY is the only right RegNotifyChangeKeyValue needs;
    // KEY_QUERY_VALUE is folded in defensively so the same handle could also serve direct reads.
    public const int KEY_NOTIFY = 0x0010;
    public const int KEY_QUERY_VALUE = 0x0001;

    // RegNotifyChangeKeyValue filter. REG_NOTIFY_CHANGE_LAST_SET fires on any value write under
    // the watched key - subkey / name / attribute changes don't concern us.
    public const uint REG_NOTIFY_CHANGE_LAST_SET = 0x4;

    public const int ERROR_SUCCESS = 0;

    public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    public const uint TOKEN_QUERY = 0x0008;
    public const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    public const string SE_TAKE_OWNERSHIP_NAME = "SeTakeOwnershipPrivilege";

    // Pack = 4 is load-bearing. Win32's TOKEN_PRIVILEGES is naturally packed (no padding between
    // PrivilegeCount and the LUID). Default .NET layout would 8-byte-align the long Luid after the
    // uint PrivilegeCount, causing AdjustTokenPrivileges to no-op with ERROR_NOT_ALL_ASSIGNED.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public long Luid;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    public static extern int RegOpenKeyExW(
        IntPtr hKey,
        string lpSubKey,
        uint ulOptions,
        int samDesired,
        out IntPtr phkResult);

    [DllImport("advapi32.dll", SetLastError = false)]
    public static extern int RegCloseKey(IntPtr hKey);

    [DllImport("advapi32.dll", SetLastError = false)]
    public static extern int RegNotifyChangeKeyValue(
        IntPtr hKey,
        [MarshalAs(UnmanagedType.Bool)] bool bWatchSubtree,
        uint dwNotifyFilter,
        IntPtr hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool fAsynchronous);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LookupPrivilegeValueW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LookupPrivilegeValueW(
        string? lpSystemName,
        string lpName,
        out long lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);
}
