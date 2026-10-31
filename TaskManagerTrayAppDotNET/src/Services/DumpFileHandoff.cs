using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>
/// Moves the dump file from the unelevated UI to the elevated broker as a handle. The UI creates the file in its own
/// temp folder, where a same-user process could plant a junction; creating it there as administrator would let that
/// process redirect the write into a protected folder. So the broker never opens a path: it duplicates the UI's
/// handle out of the pinned parent process, checks that it is an empty regular file, and writes the dump into it.
/// </summary>
internal static class DumpFileHandoff
{
    /// <summary>PROCESS_DUP_HANDLE: the right the broker needs on the UI process to copy the file handle.</summary>
    internal const uint ProcessDuplicateHandle = 0x0040;

    internal const uint FileTypeDisk = 0x0001;
    internal const uint FileAttributeDirectory = 0x0010;
    internal const uint FileAttributeReparsePoint = 0x0400;

    private const uint DuplicateSameAccess = 0x0002;
    private static readonly IntPtr CurrentProcess = new(-1);

    /// <summary>
    /// Whether a request argument can be a handle value in the UI process. Handle values are multiples of four and
    /// far below 2^31; anything else is rejected before the broker touches the parent.
    /// </summary>
    internal static bool IsPlausibleHandleValue(ulong value) => value is > 0 and <= int.MaxValue && value % 4 == 0;

    /// <summary>
    /// Whether a duplicated handle is an empty regular disk file: not a pipe, console, directory, reparse point or
    /// volume, and nothing written yet, so the dump cannot overwrite existing data.
    /// </summary>
    internal static bool IsAcceptableDumpFile(
        uint fileType,
        bool hasFileInformation,
        uint attributes,
        uint linkCount,
        ulong length,
        out string reason)
    {
        if (fileType != FileTypeDisk)
            reason = "The dump target is not a disk file.";
        else if (!hasFileInformation || linkCount == 0)
            reason = "The dump target is not a regular file.";
        else if ((attributes & (FileAttributeDirectory | FileAttributeReparsePoint)) != 0)
            reason = "The dump target is a directory or a reparse point.";
        else if (length != 0)
            reason = "The dump target file is not empty.";
        else
            reason = string.Empty;

        return reason.Length == 0;
    }

    /// <summary>Copies <paramref name="handleValue"/> out of the UI process and checks that it is an empty file.</summary>
    public static SafeFileHandle? TryTake(IntPtr parentProcess, ulong handleValue, out string errorMessage)
    {
        if (parentProcess == IntPtr.Zero)
        {
            errorMessage = "The elevated helper cannot reach the Task Manager process.";
            return null;
        }

        if (!IsPlausibleHandleValue(handleValue))
        {
            errorMessage = "The dump file handle is invalid.";
            return null;
        }

        if (!DuplicateHandle(
                parentProcess,
                (IntPtr)(long)handleValue,
                CurrentProcess,
                out IntPtr duplicated,
                desiredAccess: 0,
                inheritHandle: false,
                DuplicateSameAccess))
        {
            errorMessage = new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()).Message;
            return null;
        }

        SafeFileHandle file = new(duplicated, ownsHandle: true);
        bool hasInformation = GetFileInformationByHandle(file, out BY_HANDLE_FILE_INFORMATION information);
        ulong length = ((ulong)information.FileSizeHigh << 32) | information.FileSizeLow;
        if (IsAcceptableDumpFile(
                GetFileType(file),
                hasInformation,
                information.FileAttributes,
                information.NumberOfLinks,
                length,
                out errorMessage))
            return file;

        file.Dispose();
        return null;
    }

    // FILETIME members are two DWORDs, so the struct is 4-byte packed (52 bytes)
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(
        IntPtr sourceProcess,
        IntPtr sourceHandle,
        IntPtr targetProcess,
        out IntPtr targetHandle,
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint options);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(SafeFileHandle file);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out BY_HANDLE_FILE_INFORMATION information);
}
