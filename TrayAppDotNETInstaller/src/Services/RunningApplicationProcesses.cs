using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TrayAppDotNETInstaller.Services;

/// <summary>Finds the TrayAppDotNET apps running from one install folder.</summary>
internal static class RunningApplicationProcesses
{
    private const string AppExecutableSuffix = "TrayAppDotNET.exe";
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int MaximumPathLength = 32_768;

    /// <summary>The distinct exe paths of the app processes whose image sits directly in <paramref name="directory"/>.</summary>
    public static IReadOnlyList<string> ExecutablesUnder(string directory)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        HashSet<string> executables = new(StringComparer.OrdinalIgnoreCase);
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                string? image = TryGetImagePath(process.Id);
                if (image == null || !image.EndsWith(AppExecutableSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(Path.GetDirectoryName(image), root, StringComparison.OrdinalIgnoreCase)) continue;

                executables.Add(image);
            }
        }

        return [.. executables];
    }

    private static string? TryGetImagePath(int processId)
    {
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, inheritHandle: false, processId);
        if (handle == IntPtr.Zero) return null;

        try
        {
            StringBuilder buffer = new(MaximumPathLength);
            int length = buffer.Capacity;
            return QueryFullProcessImageName(handle, flags: 0, buffer, ref length) ? buffer.ToString(0, length) : null;
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    // DllImport stands in for LibraryImport; the .NET Framework has no source-generated interop
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder exeName, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
