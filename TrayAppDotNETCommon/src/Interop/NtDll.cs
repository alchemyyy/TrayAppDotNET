using System.Runtime.InteropServices;

namespace TrayAppDotNETCommon.Interop;

/// <summary>ntdll.dll interop shared by every app: system and process information queries.</summary>
public static unsafe class NtDll
{
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    /// <summary>PROCESSINFOCLASS ProcessCommandLineInformation: needs only PROCESS_QUERY_LIMITED_INFORMATION.</summary>
    public const int ProcessCommandLineInformation = 60;

    /// <summary>
    /// One signature for every information class: callers pass a pointer to their own buffer or structure and
    /// read the required length only when they need it (pass null otherwise).
    /// </summary>
    [DllImport("ntdll.dll")]
    public static extern int NtQuerySystemInformation(
        int systemInformationClass,
        void* systemInformation,
        uint systemInformationLength,
        uint* returnLength);

    [DllImport("ntdll.dll")]
    public static extern int NtQuerySystemInformationEx(
        int systemInformationClass,
        void* inputBuffer,
        uint inputBufferLength,
        void* systemInformation,
        uint systemInformationLength,
        uint* returnLength);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        void* processInformation,
        int processInformationLength,
        int* returnLength);

    [StructLayout(LayoutKind.Sequential)]
    public struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }
}
