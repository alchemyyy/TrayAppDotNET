using System.Runtime.InteropServices;

namespace TrayAppDotNETInstaller.UI;

/// <summary>
/// The Windows folder picker, reached through the shell's common item dialog. WPF on the .NET Framework
/// carries no folder picker of its own and the Windows Forms one is the old tree view, which would look
/// nothing like the rest of the installer.
/// </summary>
internal static class FolderPicker
{
    // CLSID_FileOpenDialog
    private static readonly Guid FileOpenDialogClassIdentifier = new("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7");
    // IID_IShellItem
    private static readonly Guid ShellItemInterfaceIdentifier = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    // FILEOPENDIALOGOPTIONS
    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint FOS_PATHMUSTEXIST = 0x00000800;

    // SIGDN_FILESYSPATH
    private const uint DisplayNameFileSystemPath = 0x80058000;

    private const int HResultOK = 0;
    // HRESULT_FROM_WIN32(ERROR_CANCELLED), which the dialog returns when the user backs out
    private const int HResultCancelled = unchecked((int)0x800704C7);

    /// <summary>
    /// Shows the picker over the given window and returns the chosen folder, or null when the user cancels
    /// or the dialog is unavailable. failureMessage is null for a plain cancel and carries the reason
    /// otherwise, so the caller can put it in front of the user.
    /// </summary>
    public static string? PickFolder(
        IntPtr ownerWindowHandle,
        string title,
        string currentDirectory,
        out string? failureMessage)
    {
        failureMessage = null;
        IFileOpenDialog? dialog = null;
        try
        {
            dialog = CreateDialog(out failureMessage);
            if (dialog == null) return null;

            uint options;
            int result = dialog.GetOptions(out options);
            if (result != HResultOK) return Failed(result, "GetOptions", out failureMessage);

            result = dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
            if (result != HResultOK) return Failed(result, "SetOptions", out failureMessage);

            result = dialog.SetTitle(title);
            if (result != HResultOK) InstallerLog.Write($"FolderPicker: SetTitle returned 0x{result:X8}");

            SeedStartFolder(dialog, currentDirectory);
            result = dialog.Show(ownerWindowHandle);
            if (result == HResultCancelled) return null;
            if (result != HResultOK) return Failed(result, "Show", out failureMessage);

            IShellItem? selectedItem;
            result = dialog.GetResult(out selectedItem);
            if (result != HResultOK || selectedItem == null) return Failed(result, "GetResult", out failureMessage);

            return ReadFileSystemPath(selectedItem, out failureMessage);
        }
        catch (Exception exception)
        {
            InstallerLog.Write("FolderPicker.PickFolder", exception);
            failureMessage = exception.Message;
            return null;
        }
        finally
        {
            if (dialog != null) Marshal.ReleaseComObject(dialog);
        }
    }

    /// <summary>Instantiates the shell dialog, reporting rather than throwing when the class is unavailable.</summary>
    private static IFileOpenDialog? CreateDialog(out string? failureMessage)
    {
        failureMessage = null;
        Type? dialogType = Type.GetTypeFromCLSID(FileOpenDialogClassIdentifier, throwOnError: false);
        if (dialogType == null)
        {
            failureMessage = "The Windows folder picker is not registered.";
            InstallerLog.Write($"FolderPicker: {failureMessage}");
            return null;
        }

        object? instance = Activator.CreateInstance(dialogType);
        if (instance is IFileOpenDialog dialog) return dialog;

        failureMessage = "The Windows folder picker could not be created.";
        InstallerLog.Write($"FolderPicker: {failureMessage}");
        if (instance != null) Marshal.ReleaseComObject(instance);
        return null;
    }

    /// <summary>Opens the dialog on the folder already in the box, when that folder exists.</summary>
    private static void SeedStartFolder(IFileOpenDialog dialog, string currentDirectory)
    {
        if (string.IsNullOrWhiteSpace(currentDirectory) || !Directory.Exists(currentDirectory)) return;

        IShellItem? startFolder = null;
        try
        {
            Guid shellItemInterfaceIdentifier = ShellItemInterfaceIdentifier;
            int result = SHCreateItemFromParsingName(
                currentDirectory,
                IntPtr.Zero,
                ref shellItemInterfaceIdentifier,
                out startFolder);
            if (result != HResultOK || startFolder == null)
            {
                InstallerLog.Write($"FolderPicker: no shell item for {currentDirectory} (0x{result:X8})");
                return;
            }

            result = dialog.SetFolder(startFolder);
            if (result != HResultOK) InstallerLog.Write($"FolderPicker: SetFolder returned 0x{result:X8}");
        }
        catch (Exception exception)
        {
            // A start folder is a convenience, so the picker still opens on the shell default
            InstallerLog.Write("FolderPicker.SeedStartFolder", exception);
        }
        finally
        {
            if (startFolder != null) Marshal.ReleaseComObject(startFolder);
        }
    }

    /// <summary>Reads the file system path out of the chosen shell item and frees the shell's string.</summary>
    private static string? ReadFileSystemPath(IShellItem selectedItem, out string? failureMessage)
    {
        failureMessage = null;
        IntPtr displayName = IntPtr.Zero;
        try
        {
            int result = selectedItem.GetDisplayName(DisplayNameFileSystemPath, out displayName);
            if (result != HResultOK) return Failed(result, "GetDisplayName", out failureMessage);

            return Marshal.PtrToStringUni(displayName);
        }
        finally
        {
            if (displayName != IntPtr.Zero) Marshal.FreeCoTaskMem(displayName);
            Marshal.ReleaseComObject(selectedItem);
        }
    }

    private static string? Failed(int result, string operation, out string? failureMessage)
    {
        failureMessage = $"{operation} failed with HRESULT 0x{result:X8}.";
        InstallerLog.Write($"FolderPicker: {failureMessage}");
        return null;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string path,
        IntPtr bindContext,
        ref Guid interfaceIdentifier,
        out IShellItem? shellItem);

    /// <summary>
    /// IModalWindow, IFileDialog and IFileOpenDialog flattened into one declaration. Every method must stay
    /// in vtable order even though the picker only calls a handful of them; the unused slots take the
    /// simplest signature that still occupies the right position.
    /// </summary>
    [ComImport]
    [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        // IModalWindow
        [PreserveSig]
        int Show(IntPtr ownerWindowHandle);

        // IFileDialog
        [PreserveSig]
        int SetFileTypes(uint fileTypeCount, IntPtr fileTypes);

        [PreserveSig]
        int SetFileTypeIndex(uint fileTypeIndex);

        [PreserveSig]
        int GetFileTypeIndex(out uint fileTypeIndex);

        [PreserveSig]
        int Advise(IntPtr eventSink, out uint cookie);

        [PreserveSig]
        int Unadvise(uint cookie);

        [PreserveSig]
        int SetOptions(uint options);

        [PreserveSig]
        int GetOptions(out uint options);

        [PreserveSig]
        int SetDefaultFolder(IShellItem folder);

        [PreserveSig]
        int SetFolder(IShellItem folder);

        [PreserveSig]
        int GetFolder(out IShellItem? folder);

        [PreserveSig]
        int GetCurrentSelection(out IShellItem? item);

        [PreserveSig]
        int SetFileName([MarshalAs(UnmanagedType.LPWStr)] string fileName);

        [PreserveSig]
        int GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string fileName);

        [PreserveSig]
        int SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

        [PreserveSig]
        int SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

        [PreserveSig]
        int SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

        [PreserveSig]
        int GetResult(out IShellItem? item);

        [PreserveSig]
        int AddPlace(IShellItem place, int order);

        [PreserveSig]
        int SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);

        [PreserveSig]
        int Close(int result);

        [PreserveSig]
        int SetClientGuid(ref Guid clientIdentifier);

        [PreserveSig]
        int ClearClientData();

        [PreserveSig]
        int SetFilter(IntPtr filter);

        // IFileOpenDialog
        [PreserveSig]
        int GetResults(out IntPtr items);

        [PreserveSig]
        int GetSelectedItems(out IntPtr items);
    }

    /// <summary>The shell item the dialog hands back, read only for its file system path.</summary>
    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig]
        int BindToHandler(
            IntPtr bindContext,
            ref Guid handlerIdentifier,
            ref Guid interfaceIdentifier,
            out IntPtr instance);

        [PreserveSig]
        int GetParent(out IShellItem? parent);

        [PreserveSig]
        int GetDisplayName(uint displayNameForm, out IntPtr displayName);

        [PreserveSig]
        int GetAttributes(uint mask, out uint attributes);

        [PreserveSig]
        int Compare(IShellItem other, uint hint, out int order);
    }
}
