using System.Runtime.InteropServices;
using TaskManagerTrayAppDotNET.Interop;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>One drive letter and the native device its DOS device name points at.</summary>
internal readonly record struct DriveDevicePath(string DriveName, string DevicePath);

/// <summary>
/// Reads a process's image path by process ID without opening the process. Task Manager's
/// TmGetPathFromProcessIdForNonAdmin does the same for a process it cannot open: it queries
/// SystemProcessIdInformation, then maps the native device path back to a drive letter.
/// </summary>
internal sealed class ProcessImagePathResolver
{
    private const int SystemProcessIdInformation = 88;
    private const ushort InitialImageNameBytes = 1_024;
    private const string NativeDevicePrefix = @"\Device\";

    private DriveDevicePath[] _drives = [];

    /// <summary>Returns the drive-letter image path, or empty when the process has none or it is on no drive.</summary>
    public string Resolve(int processID)
    {
        string nativePath = ReadNativeImagePath(processID);
        // Pseudo processes such as Registry and MemCompression report a bare name
        if (!nativePath.StartsWith(NativeDevicePrefix, StringComparison.OrdinalIgnoreCase)) return string.Empty;
        if (TryConvertToDrivePath(nativePath, _drives, out string drivePath)) return drivePath;

        // A drive mounted since the last lookup gets one refresh
        _drives = ReadDriveDevicePaths();
        return TryConvertToDrivePath(nativePath, _drives, out drivePath) ? drivePath : string.Empty;
    }

    /// <summary>Replaces the longest matching device prefix with its drive letter.</summary>
    internal static bool TryConvertToDrivePath(
        string nativePath,
        IReadOnlyList<DriveDevicePath> drives,
        out string drivePath)
    {
        int bestIndex = -1;
        for (int driveIndex = 0; driveIndex < drives.Count; driveIndex++)
        {
            string devicePath = drives[driveIndex].DevicePath;
            if (nativePath.Length <= devicePath.Length
                || nativePath[devicePath.Length] != Path.DirectorySeparatorChar
                || !nativePath.StartsWith(devicePath, StringComparison.OrdinalIgnoreCase))
                continue;
            if (bestIndex < 0 || devicePath.Length > drives[bestIndex].DevicePath.Length)
                bestIndex = driveIndex;
        }

        if (bestIndex < 0)
        {
            drivePath = string.Empty;
            return false;
        }

        drivePath = string.Concat(
            drives[bestIndex].DriveName,
            nativePath.AsSpan(drives[bestIndex].DevicePath.Length));
        return true;
    }

    private static unsafe string ReadNativeImagePath(int processID)
    {
        ushort capacity = InitialImageNameBytes;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            IntPtr buffer = Marshal.AllocHGlobal(capacity);
            try
            {
                SYSTEM_PROCESS_ID_INFORMATION information = new()
                {
                    ProcessID = processID,
                    ImageName = new NtDll.UNICODE_STRING
                    {
                        Length = 0,
                        MaximumLength = capacity,
                        Buffer = buffer
                    }
                };
                int status = NtDll.NtQuerySystemInformation(
                    SystemProcessIdInformation,
                    &information,
                    (uint)sizeof(SYSTEM_PROCESS_ID_INFORMATION),
                    returnLength: null);
                if (status >= 0)
                    return Marshal.PtrToStringUni(buffer, information.ImageName.Length / sizeof(char));

                // The kernel reports the required size in MaximumLength
                if (status != NtDll.StatusInfoLengthMismatch
                    || information.ImageName.MaximumLength <= capacity)
                    return string.Empty;
                capacity = information.ImageName.MaximumLength;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return string.Empty;
    }

    private static DriveDevicePath[] ReadDriveDevicePaths()
    {
        List<DriveDevicePath> drives = [];
        for (char driveLetter = 'A'; driveLetter <= 'Z'; driveLetter++)
        {
            string driveName = string.Concat(driveLetter.ToString(), ":");
            string[] targets = DosDevices.QueryTargets(driveName);
            if (targets.Length == 0) continue;

            // The first string of the returned list is the current mapping
            string devicePath = targets[0];
            if (devicePath.StartsWith(NativeDevicePrefix, StringComparison.OrdinalIgnoreCase))
                drives.Add(new DriveDevicePath(driveName, devicePath));
        }

        return [.. drives];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_PROCESS_ID_INFORMATION
    {
        public IntPtr ProcessID;
        public NtDll.UNICODE_STRING ImageName;
    }
}
