using System.Runtime.InteropServices;

namespace TaskManagerTrayAppDotNET.Services;

/// <summary>Reads every page file's size and use from <c>SystemPageFileInformationEx</c>.</summary>
/// <remarks>
/// One small kernel query per sample that needs no administrator rights. Sizes come back in pages. Paths are reused
/// between samples so an unchanged page file set allocates only the result array.
/// </remarks>
internal sealed unsafe class PageFileReader
{
    private const int SystemPageFileInformationEx = 144;
    private const int InitialBufferSize = 512;
    private const int MaximumBufferSize = 64 * 1024;

    private byte[] _buffer = new byte[InitialBufferSize];
    private PageFileSnapshot[] _previous = [];

    /// <summary>Returns the current page files in kernel order, or none when the query fails.</summary>
    public PageFileSnapshot[] Read()
    {
        while (true)
        {
            int status;
            uint returnLength;
            fixed (byte* buffer = _buffer)
            {
                status = NtDll.NtQuerySystemInformation(
                    SystemPageFileInformationEx,
                    buffer,
                    (uint)_buffer.Length,
                    &returnLength);
                if (status >= 0) return _previous = Parse(buffer, (int)Math.Min(returnLength, (uint)_buffer.Length));
            }

            if (status != NtDll.StatusInfoLengthMismatch || _buffer.Length >= MaximumBufferSize) return _previous = [];
            _buffer = new byte[Math.Min(MaximumBufferSize, Math.Max(_buffer.Length * 2, (int)returnLength))];
        }
    }

    private PageFileSnapshot[] Parse(byte* buffer, int length)
    {
        if (length < sizeof(SYSTEM_PAGEFILE_INFORMATION_EX)) return [];

        ulong pageSize = (ulong)Environment.SystemPageSize;
        List<PageFileSnapshot> pageFiles = [];
        int offset = 0;
        while (offset + sizeof(SYSTEM_PAGEFILE_INFORMATION_EX) <= length)
        {
            SYSTEM_PAGEFILE_INFORMATION_EX* entry = (SYSTEM_PAGEFILE_INFORMATION_EX*)(buffer + offset);
            ReadOnlySpan<char> nativePath = entry->PageFileName.Buffer == IntPtr.Zero
                ? []
                : new ReadOnlySpan<char>((char*)entry->PageFileName.Buffer, entry->PageFileName.Length / 2);
            string path = ReusePath(nativePath);
            pageFiles.Add(new PageFileSnapshot(
                path,
                GetDriveName(path),
                entry->TotalInUse * pageSize,
                entry->TotalSize * pageSize,
                entry->PeakUsage * pageSize));
            if (entry->NextEntryOffset == 0) break;
            offset += (int)entry->NextEntryOffset;
        }

        return [.. pageFiles];
    }

    private string ReusePath(ReadOnlySpan<char> nativePath)
    {
        ReadOnlySpan<char> path = nativePath.StartsWith(@"\??\") ? nativePath[4..] : nativePath;
        for (int index = 0; index < _previous.Length; index++)
        {
            if (path.SequenceEqual(_previous[index].Path)) return _previous[index].Path;
        }

        return new string(path);
    }

    /// <summary>Returns the drive letter a page file path starts with, or the whole path without one.</summary>
    internal static string GetDriveName(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'
            ? char.ToUpperInvariant(path[0]).ToString()
            : path;

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_PAGEFILE_INFORMATION_EX
    {
        public uint NextEntryOffset;
        public uint TotalSize;
        public uint TotalInUse;
        public uint PeakUsage;
        public UNICODE_STRING PageFileName;
        public uint MinimumSize;
        public uint MaximumSize;
    }
}
