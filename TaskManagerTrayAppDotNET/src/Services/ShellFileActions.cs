using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Invokes Windows shell actions for file-system targets.</summary>
internal static class ShellFileActions
{
    private const uint COMInitializeMultithreaded = 0x0;
    private const uint COMInitializeDisableOLE1DDE = 0x4;
    private const int RPCEChangedMode = unchecked((int)0x80010106);

    /// <summary>Opens the containing folder and selects an existing file-system item.</summary>
    public static bool TryOpenContainingFolderAndSelect(string? path, out string errorMessage)
    {
        if (!TryNormalizeExistingPath(path, out string normalizedPath, out errorMessage)) return false;

        int initializationResult = NativeMethods.CoInitializeEx(
            IntPtr.Zero,
            COMInitializeMultithreaded | COMInitializeDisableOLE1DDE);
        bool shouldUninitialize = initializationResult >= 0;
        if (initializationResult < 0 && initializationResult != RPCEChangedMode)
        {
            errorMessage = DescribeShellError("COM initialization failed", initializationResult);
            return false;
        }

        try
        {
            int parseResult = NativeMethods.SHParseDisplayName(
                normalizedPath,
                IntPtr.Zero,
                out IntPtr itemIDList,
                attributesIn: 0,
                out _);
            if (parseResult < 0)
            {
                errorMessage = DescribeShellError("The selected item could not be resolved", parseResult);
                return false;
            }

            if (itemIDList == IntPtr.Zero)
            {
                errorMessage = "The Windows Shell did not return an identifier for the selected item.";
                return false;
            }

            try
            {
                // With no child array, the absolute PIDL identifies the item to select
                int openResult = NativeMethods.SHOpenFolderAndSelectItems(
                    itemIDList,
                    itemCount: 0,
                    itemIDLists: IntPtr.Zero,
                    flags: 0);
                if (openResult >= 0)
                {
                    errorMessage = string.Empty;
                    return true;
                }

                errorMessage = DescribeShellError("The containing folder could not be opened", openResult);
                return false;
            }
            finally
            {
                Marshal.FreeCoTaskMem(itemIDList);
            }
        }
        finally
        {
            if (shouldUninitialize) NativeMethods.CoUninitialize();
        }
    }

    /// <summary>Shows the shell Properties sheet for an existing file or directory.</summary>
    public static bool TryShowProperties(string? path, out string errorMessage)
    {
        if (!TryNormalizeExistingPath(path, out string normalizedPath, out errorMessage)) return false;

        return ExplorerProcessLauncher.TryShellExecute(
            normalizedPath,
            arguments: null,
            workingDirectory: Path.GetDirectoryName(normalizedPath),
            verb: "properties",
            out _,
            out errorMessage);
    }

    private static bool TryNormalizeExistingPath(
        string? path,
        out string normalizedPath,
        out string errorMessage)
    {
        normalizedPath = string.Empty;
        if (!OperatingSystem.IsWindows())
        {
            errorMessage = "Shell file actions are only available on Windows.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "The selected item does not have a resolved file-system target.";
            return false;
        }

        normalizedPath = Path.GetFullPath(path);
        if (File.Exists(normalizedPath) || Directory.Exists(normalizedPath))
        {
            errorMessage = string.Empty;
            return true;
        }

        errorMessage = $"'{normalizedPath}' no longer exists.";
        return false;
    }

    private static string DescribeShellError(string operation, int result)
    {
        const int hResultCodeMask = 0x0000FFFF;
        const int hResultFacilityMask = 0x1FFF0000;
        const int hResultFacilityWin32 = 0x00070000;

        string detail = (result & hResultFacilityMask) == hResultFacilityWin32
            ? new Win32Exception(result & hResultCodeMask).Message
            : $"HRESULT 0x{unchecked((uint)result):X8}";
        return $"{operation}: {detail}.";
    }

    private static class NativeMethods
    {
        [DllImport("ole32.dll", ExactSpelling = true)]
        public static extern int CoInitializeEx(IntPtr reserved, uint concurrencyModel);

        [DllImport("ole32.dll", ExactSpelling = true)]
        public static extern void CoUninitialize();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = true)]
        public static extern int SHParseDisplayName(
            string name,
            IntPtr bindingContext,
            out IntPtr itemIDList,
            uint attributesIn,
            out uint attributesOut);

        [DllImport("shell32.dll", ExactSpelling = true, PreserveSig = true)]
        public static extern int SHOpenFolderAndSelectItems(
            IntPtr folderItemIDList,
            uint itemCount,
            IntPtr itemIDLists,
            uint flags);
    }
}
