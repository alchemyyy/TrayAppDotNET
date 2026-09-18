using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace TrayAppDotNETInstaller.Services;

/// <summary>
/// One ICONDIRENTRY read from an ICO file. The first seven fields, twelve bytes in all, are laid out
/// exactly as in the GRPICONDIRENTRY the RT_GROUP_ICON resource stores.
/// </summary>
internal readonly record struct IconDirectoryEntry(
    byte Width,
    byte Height,
    byte ColorCount,
    byte Reserved,
    ushort Planes,
    ushort BitCount,
    uint BytesInResource,
    uint ImageOffset);

/// <summary>The integer RT_ICON and RT_GROUP_ICON IDs an executable already carries, and their language.</summary>
internal sealed class ExistingIconResources
{
    public List<ushort> IconIDs { get; } = [];

    public List<ushort> GroupIconIDs { get; } = [];

    public List<ushort> Languages { get; } = [];
}

/// <summary>
/// Replaces the shell icon of a stamped installer copy through the Win32 resource update APIs.
/// Stamping is cosmetic: a failure leaves the factory's suite icon in place and never fails a build.
/// </summary>
internal static class IconStamper
{
    private const string Kernel32 = "kernel32.dll";

    // Win32 resource types from WinUser.h, both referenced by integer ID
    private const int ResourceTypeIcon = 3;
    private const int ResourceTypeGroupIcon = 14;

    // ICO container layout: ICONDIR is six bytes, then one sixteen-byte ICONDIRENTRY per image.
    // The group resource repeats the header but shrinks each entry to fourteen bytes, because the
    // trailing uint32 file offset is replaced by a uint16 RT_ICON resource ID
    private const int IconDirectoryHeaderSize = 6;
    private const int FileDirectoryEntrySize = 16;
    private const int GroupDirectoryEntrySize = 14;
    private const int CommonDirectoryEntrySize = 12;
    private const ushort IconDirectoryReserved = 0;
    private const ushort IconDirectoryTypeIcon = 1;

    // RT_ICON IDs run 1..Count; zero is not a usable MAKEINTRESOURCE value
    private const ushort FirstIconResourceID = 1;

    // A managed executable built by the SDK publishes its icon group as IDI_APPLICATION at the neutral
    // language, so this is only a fallback for an image that carries no group at all
    private const ushort DefaultGroupIconID = 32512;
    private const ushort NeutralLanguage = 0;

    private const uint LoadLibraryAsDataFile = 0x00000002;
    private const int ContinueEnumeration = 1;
    private const int StopEnumeration = 0;

    /// <summary>
    /// Writes the icon images in <paramref name="iconBytes"/> over the executable's icon resources.
    /// Must run before any payload is appended, because updating resources rewrites the image and would
    /// discard trailing data. Returns false after logging when the icon could not be replaced.
    /// </summary>
    public static bool TryStamp(string executablePath, byte[] iconBytes, Action<string> log)
    {
        if (log == null)
        {
            InstallerLog.Write("IconStamper: no log callback was supplied");
            return false;
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            log("IconStamper: no executable path was supplied");
            return false;
        }

        if (iconBytes == null || iconBytes.Length == 0)
        {
            log($"IconStamper: no icon bytes were supplied for {executablePath}");
            return false;
        }

        // Stamping is cosmetic, so nothing here may escape into the factory
        try
        {
            return Stamp(executablePath, iconBytes, log);
        }
        catch (Exception exception)
        {
            log($"IconStamper: stamping {executablePath} failed: {exception}");
            return false;
        }
    }

    private static bool Stamp(string executablePath, byte[] iconBytes, Action<string> log)
    {
        if (!TryParseIconDirectory(iconBytes, out IconDirectoryEntry[]? entries, out string? parseFailure))
        {
            log($"IconStamper: {executablePath} keeps its icon because the replacement is unusable: {parseFailure}");
            return false;
        }

        string fullExecutablePath = Path.GetFullPath(executablePath);
        ExistingIconResources existing = ReadExistingIconResources(fullExecutablePath, log);
        ushort language = existing.Languages.Count > 0 ? existing.Languages[0] : NeutralLanguage;
        ushort groupIconID = LowestResourceID(existing.GroupIconIDs, DefaultGroupIconID);
        byte[] groupDirectory = BuildGroupDirectory(entries);

        // The existing manifest and version resources must survive, so nothing is bulk-deleted here
        IntPtr updateHandle = BeginUpdateResourceW(fullExecutablePath, deleteExistingResources: false);
        if (updateHandle == IntPtr.Zero)
        {
            log($"IconStamper: BeginUpdateResourceW failed for {fullExecutablePath} with Win32 error {Marshal.GetLastWin32Error()}");
            return false;
        }

        // UpdateResourceW reads through the raw pointers, so both buffers stay pinned until the update closes
        GCHandle iconPin = GCHandle.Alloc(iconBytes, GCHandleType.Pinned);
        GCHandle groupPin = GCHandle.Alloc(groupDirectory, GCHandleType.Pinned);
        try
        {
            if (!DeleteStaleResources(updateHandle, existing, entries.Length, groupIconID, language, log))
                return DiscardUpdate(updateHandle, log);

            IntPtr iconBase = iconPin.AddrOfPinnedObject();
            for (int index = 0; index < entries.Length; index++)
            {
                IconDirectoryEntry entry = entries[index];
                ushort iconID = (ushort)(FirstIconResourceID + index);
                IntPtr imagePointer = IntPtr.Add(iconBase, (int)entry.ImageOffset);
                if (UpdateResourceW(
                        updateHandle,
                        new IntPtr(ResourceTypeIcon),
                        new IntPtr(iconID),
                        language,
                        imagePointer,
                        entry.BytesInResource))
                {
                    continue;
                }

                log($"IconStamper: UpdateResourceW failed for RT_ICON #{iconID} with Win32 error {Marshal.GetLastWin32Error()}");
                return DiscardUpdate(updateHandle, log);
            }

            if (!UpdateResourceW(
                    updateHandle,
                    new IntPtr(ResourceTypeGroupIcon),
                    new IntPtr(groupIconID),
                    language,
                    groupPin.AddrOfPinnedObject(),
                    (uint)groupDirectory.Length))
            {
                log($"IconStamper: UpdateResourceW failed for RT_GROUP_ICON #{groupIconID} with Win32 error {Marshal.GetLastWin32Error()}");
                return DiscardUpdate(updateHandle, log);
            }

            if (!EndUpdateResourceW(updateHandle, discard: false))
            {
                log($"IconStamper: EndUpdateResourceW failed for {fullExecutablePath} with Win32 error {Marshal.GetLastWin32Error()}");
                return false;
            }
        }
        finally
        {
            iconPin.Free();
            groupPin.Free();
        }

        log($"IconStamper: wrote {entries.Length} image(s) to {fullExecutablePath} as RT_GROUP_ICON #{groupIconID} at language 0x{language:X4}");
        return true;
    }

    /// <summary>
    /// Removes icon IDs the replacement set does not cover. A shorter icon set would otherwise leave
    /// orphaned images behind, and a second group would compete for the lowest ID Explorer picks.
    /// </summary>
    private static bool DeleteStaleResources(
        IntPtr updateHandle,
        ExistingIconResources existing,
        int imageCount,
        ushort groupIconID,
        ushort language,
        Action<string> log)
    {
        foreach (ushort iconID in existing.IconIDs)
        {
            if (iconID >= FirstIconResourceID && iconID <= imageCount) continue;
            if (!DeleteResource(updateHandle, ResourceTypeIcon, iconID, language, log)) return false;
        }

        foreach (ushort existingGroupID in existing.GroupIconIDs)
        {
            if (existingGroupID == groupIconID) continue;
            if (!DeleteResource(updateHandle, ResourceTypeGroupIcon, existingGroupID, language, log)) return false;
        }

        return true;
    }

    /// <summary>Deletes one resource; UpdateResourceW treats a null pointer with zero length as a removal.</summary>
    private static bool DeleteResource(
        IntPtr updateHandle,
        int resourceType,
        ushort resourceID,
        ushort language,
        Action<string> log)
    {
        if (UpdateResourceW(updateHandle, new IntPtr(resourceType), new IntPtr(resourceID), language, IntPtr.Zero, 0))
            return true;

        log($"IconStamper: deleting resource type {resourceType} #{resourceID} failed with Win32 error {Marshal.GetLastWin32Error()}");
        return false;
    }

    /// <summary>Rolls a pending update back and always reports failure to the caller.</summary>
    private static bool DiscardUpdate(IntPtr updateHandle, Action<string> log)
    {
        if (!EndUpdateResourceW(updateHandle, discard: true))
            log($"IconStamper: discarding the resource update failed with Win32 error {Marshal.GetLastWin32Error()}");

        return false;
    }

    /// <summary>Explorer shows the numerically lowest group icon, so that is the ID stamping overwrites.</summary>
    internal static ushort LowestResourceID(IReadOnlyList<ushort> resourceIDs, ushort fallback)
    {
        if (resourceIDs.Count == 0) return fallback;

        ushort lowest = resourceIDs[0];
        foreach (ushort resourceID in resourceIDs)
        {
            if (resourceID < lowest) lowest = resourceID;
        }

        return lowest;
    }

    /// <summary>
    /// Reads the ICONDIR header and its ICONDIRENTRY table. Every image range is checked against the
    /// buffer, so a truncated or hostile ICO is reported instead of reaching UpdateResourceW.
    /// </summary>
    internal static bool TryParseIconDirectory(
        byte[] iconBytes,
        [NotNullWhen(true)] out IconDirectoryEntry[]? entries,
        [NotNullWhen(false)] out string? failureReason)
    {
        entries = null;
        int bufferLength = iconBytes?.Length ?? 0;
        if (bufferLength < IconDirectoryHeaderSize)
        {
            failureReason = $"the buffer holds {bufferLength} bytes, fewer than the {IconDirectoryHeaderSize}-byte ICONDIR header";
            return false;
        }

        // FrameworkCompatibility stands in for BinaryPrimitives, which needs spans the .NET Framework lacks
        byte[] buffer = iconBytes!;
        ushort reserved = FrameworkCompatibility.ReadUInt16LittleEndian(buffer, offset: 0);
        ushort directoryType = FrameworkCompatibility.ReadUInt16LittleEndian(buffer, offset: 2);
        ushort imageCount = FrameworkCompatibility.ReadUInt16LittleEndian(buffer, offset: 4);
        if (reserved != IconDirectoryReserved)
        {
            failureReason = $"ICONDIR.Reserved is {reserved} instead of {IconDirectoryReserved}";
            return false;
        }

        if (directoryType != IconDirectoryTypeIcon)
        {
            failureReason = $"ICONDIR.Type is {directoryType} instead of {IconDirectoryTypeIcon}";
            return false;
        }

        if (imageCount == 0)
        {
            failureReason = "ICONDIR.Count is zero";
            return false;
        }

        long tableEnd = IconDirectoryHeaderSize + ((long)FileDirectoryEntrySize * imageCount);
        if (tableEnd > bufferLength)
        {
            failureReason = $"the {imageCount}-entry directory needs {tableEnd} bytes but the buffer holds {bufferLength}";
            return false;
        }

        IconDirectoryEntry[] parsed = new IconDirectoryEntry[imageCount];
        for (int index = 0; index < imageCount; index++)
        {
            int entryOffset = IconDirectoryHeaderSize + (FileDirectoryEntrySize * index);
            uint bytesInResource = FrameworkCompatibility.ReadUInt32LittleEndian(buffer, entryOffset + 8);
            uint imageOffset = FrameworkCompatibility.ReadUInt32LittleEndian(buffer, entryOffset + CommonDirectoryEntrySize);
            if (bytesInResource == 0)
            {
                failureReason = $"image {index} declares zero bytes";
                return false;
            }

            if (imageOffset < tableEnd || (long)imageOffset + bytesInResource > bufferLength)
            {
                failureReason = $"image {index} spans {imageOffset}..{(long)imageOffset + bytesInResource} which lies outside the {bufferLength}-byte buffer";
                return false;
            }

            parsed[index] = new IconDirectoryEntry(
                buffer[entryOffset],
                buffer[entryOffset + 1],
                buffer[entryOffset + 2],
                buffer[entryOffset + 3],
                FrameworkCompatibility.ReadUInt16LittleEndian(buffer, entryOffset + 4),
                FrameworkCompatibility.ReadUInt16LittleEndian(buffer, entryOffset + 6),
                bytesInResource,
                imageOffset);
        }

        entries = parsed;
        failureReason = null;
        return true;
    }

    /// <summary>
    /// Lays out the RT_GROUP_ICON directory: the ICONDIR header followed by one GRPICONDIRENTRY per image.
    /// Each entry repeats the first twelve bytes of the file entry and ends with the RT_ICON resource ID
    /// instead of the four-byte file offset, so entries are fourteen bytes rather than sixteen.
    /// </summary>
    internal static byte[] BuildGroupDirectory(IReadOnlyList<IconDirectoryEntry> entries)
    {
        FrameworkCompatibility.ThrowIfNull(entries, nameof(entries));

        // FrameworkCompatibility stands in for BinaryPrimitives, which needs spans the .NET Framework lacks
        byte[] directory = new byte[IconDirectoryHeaderSize + (GroupDirectoryEntrySize * entries.Count)];
        FrameworkCompatibility.WriteUInt16LittleEndian(directory, offset: 0, IconDirectoryReserved);
        FrameworkCompatibility.WriteUInt16LittleEndian(directory, offset: 2, IconDirectoryTypeIcon);
        FrameworkCompatibility.WriteUInt16LittleEndian(directory, offset: 4, (ushort)entries.Count);

        for (int index = 0; index < entries.Count; index++)
        {
            IconDirectoryEntry entry = entries[index];
            int entryOffset = IconDirectoryHeaderSize + (GroupDirectoryEntrySize * index);
            directory[entryOffset] = entry.Width;
            directory[entryOffset + 1] = entry.Height;
            directory[entryOffset + 2] = entry.ColorCount;
            directory[entryOffset + 3] = entry.Reserved;
            FrameworkCompatibility.WriteUInt16LittleEndian(directory, entryOffset + 4, entry.Planes);
            FrameworkCompatibility.WriteUInt16LittleEndian(directory, entryOffset + 6, entry.BitCount);
            FrameworkCompatibility.WriteUInt32LittleEndian(directory, entryOffset + 8, entry.BytesInResource);
            FrameworkCompatibility.WriteUInt16LittleEndian(
                directory,
                entryOffset + CommonDirectoryEntrySize,
                (ushort)(FirstIconResourceID + index));
        }

        return directory;
    }

    /// <summary>
    /// Enumerates the icon resources an executable already carries. The image is opened as a data file and
    /// closed again before the update handle is taken, because a mapped image blocks the rewrite.
    /// An executable with no icons at all simply reports nothing, which is not a stamping failure.
    /// </summary>
    internal static ExistingIconResources ReadExistingIconResources(string executablePath, Action<string> log)
    {
        ExistingIconResources existing = new();
        IntPtr module = LoadLibraryExW(executablePath, IntPtr.Zero, LoadLibraryAsDataFile);
        if (module == IntPtr.Zero)
        {
            log($"IconStamper: LoadLibraryExW failed for {executablePath} with Win32 error {Marshal.GetLastWin32Error()}; assuming no existing icons");
            return existing;
        }

        // The .NET Framework supports neither UnmanagedCallersOnly nor unmanaged function pointers, so the
        // callbacks are ordinary delegates. Each one is held in a local and kept alive with GC.KeepAlive
        // past the last call, because the marshalled thunk dies with the delegate.
        EnumResourceNameCallback nameCallback = OnResourceNameFound;
        EnumResourceLanguageCallback languageCallback = OnResourceLanguageFound;
        GCHandle collector = GCHandle.Alloc(existing);
        try
        {
            IntPtr state = GCHandle.ToIntPtr(collector);
            EnumResourceNamesW(module, new IntPtr(ResourceTypeIcon), nameCallback, state);
            EnumResourceNamesW(module, new IntPtr(ResourceTypeGroupIcon), nameCallback, state);

            // The language of the group is the one to reuse; icons are the fallback when no group exists
            if (existing.GroupIconIDs.Count > 0)
            {
                ushort groupIconID = LowestResourceID(existing.GroupIconIDs, DefaultGroupIconID);
                EnumResourceLanguagesW(
                    module,
                    new IntPtr(ResourceTypeGroupIcon),
                    new IntPtr(groupIconID),
                    languageCallback,
                    state);
            }
            else if (existing.IconIDs.Count > 0)
            {
                ushort iconID = LowestResourceID(existing.IconIDs, FirstIconResourceID);
                EnumResourceLanguagesW(
                    module,
                    new IntPtr(ResourceTypeIcon),
                    new IntPtr(iconID),
                    languageCallback,
                    state);
            }
        }
        finally
        {
            GC.KeepAlive(nameCallback);
            GC.KeepAlive(languageCallback);
            collector.Free();
            if (!FreeLibrary(module))
                log($"IconStamper: FreeLibrary failed for {executablePath} with Win32 error {Marshal.GetLastWin32Error()}");
        }

        return existing;
    }

    /// <summary>EnumResourceNamesW callback; records the integer RT_ICON and RT_GROUP_ICON IDs.</summary>
    private static int OnResourceNameFound(IntPtr module, IntPtr resourceType, IntPtr resourceName, IntPtr state)
    {
        // NOTE: an exception must never unwind into kernel32, so every path here is guarded
        try
        {
            if (GCHandle.FromIntPtr(state).Target is not ExistingIconResources existing) return StopEnumeration;
            if (!TryGetIntegerResourceID(resourceName, out ushort resourceID)) return ContinueEnumeration;

            switch (resourceType.ToInt64())
            {
                case ResourceTypeIcon:
                    existing.IconIDs.Add(resourceID);
                    break;
                case ResourceTypeGroupIcon:
                    existing.GroupIconIDs.Add(resourceID);
                    break;
            }

            return ContinueEnumeration;
        }
        catch (Exception exception)
        {
            InstallerLog.Write("IconStamper.OnResourceNameFound", exception);
            return StopEnumeration;
        }
    }

    /// <summary>EnumResourceLanguagesW callback; records the language an existing icon resource uses.</summary>
    private static int OnResourceLanguageFound(
        IntPtr module,
        IntPtr resourceType,
        IntPtr resourceName,
        ushort language,
        IntPtr state)
    {
        try
        {
            if (GCHandle.FromIntPtr(state).Target is not ExistingIconResources existing) return StopEnumeration;

            existing.Languages.Add(language);
            return ContinueEnumeration;
        }
        catch (Exception exception)
        {
            InstallerLog.Write("IconStamper.OnResourceLanguageFound", exception);
            return StopEnumeration;
        }
    }

    /// <summary>
    /// Recognizes the MAKEINTRESOURCE convention: an integer ID arrives as a pointer whose value is the
    /// number itself, so anything above sixteen bits is a string name this stamper leaves alone.
    /// </summary>
    internal static bool TryGetIntegerResourceID(IntPtr resourceName, out ushort resourceID)
    {
        ulong value = (ulong)resourceName.ToInt64();
        if (value > ushort.MaxValue)
        {
            resourceID = 0;
            return false;
        }

        resourceID = (ushort)value;
        return true;
    }

    /// <summary>ENUMRESNAMEPROCW; the Winapi convention is stdcall on x86 and the default one elsewhere.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumResourceNameCallback(IntPtr module, IntPtr resourceType, IntPtr resourceName, IntPtr state);

    /// <summary>ENUMRESLANGPROCW; returning zero stops the enumeration exactly as the native contract says.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumResourceLanguageCallback(
        IntPtr module,
        IntPtr resourceType,
        IntPtr resourceName,
        ushort language,
        IntPtr state);

    // DllImport stands in for LibraryImport; the .NET Framework has no source-generated interop, so string
    // marshalling is spelled with CharSet.Unicode instead of StringMarshalling
    [DllImport(Kernel32, EntryPoint = "BeginUpdateResourceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BeginUpdateResourceW(
        string fileName,
        [MarshalAs(UnmanagedType.Bool)] bool deleteExistingResources);

    [DllImport(Kernel32, EntryPoint = "UpdateResourceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateResourceW(
        IntPtr updateHandle,
        IntPtr resourceType,
        IntPtr resourceName,
        ushort language,
        IntPtr data,
        uint dataSize);

    [DllImport(Kernel32, EntryPoint = "EndUpdateResourceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndUpdateResourceW(IntPtr updateHandle, [MarshalAs(UnmanagedType.Bool)] bool discard);

    [DllImport(Kernel32, EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr reservedFileHandle, uint flags);

    [DllImport(Kernel32, EntryPoint = "FreeLibrary", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport(Kernel32, EntryPoint = "EnumResourceNamesW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumResourceNamesW(
        IntPtr module,
        IntPtr resourceType,
        EnumResourceNameCallback callback,
        IntPtr state);

    [DllImport(Kernel32, EntryPoint = "EnumResourceLanguagesW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumResourceLanguagesW(
        IntPtr module,
        IntPtr resourceType,
        IntPtr resourceName,
        EnumResourceLanguageCallback callback,
        IntPtr state);
}
