using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TrayAppDotNETInstaller;

/// <summary>
/// Stand-ins for the .NET conveniences the .NET Framework does not carry. Every member mirrors the modern
/// API's behaviour exactly so call sites read the same as they did before the port.
/// </summary>
internal static class FrameworkCompatibility
{
    private const int HashStreamBufferSize = 128 * 1024;
    private const string HexadecimalDigits = "0123456789ABCDEF";
    private const string LowercaseHexadecimalDigits = "0123456789abcdef";
    private const char ArgumentSeparator = ' ';
    private const char ArgumentQuote = '"';
    private const char ArgumentEscape = '\\';
    private const string TaskKillExecutableName = "taskkill.exe";
    private const int TaskKillWaitMs = 5000;

    private static readonly Lazy<string?> ProcessPathValue = new(ResolveProcessPath);
    private static readonly Lazy<int> ProcessIDValue = new(ResolveProcessID);

    /// <summary>The full path of the running executable, matching Environment.ProcessPath.</summary>
    public static string? ProcessPath => ProcessPathValue.Value;

    /// <summary>The current process identifier, matching Environment.ProcessId.</summary>
    public static int ProcessID => ProcessIDValue.Value;

    /// <summary>Formats bytes as uppercase hexadecimal, matching Convert.ToHexString.</summary>
    public static string ToHexString(byte[] bytes)
    {
        ThrowIfNull(bytes, nameof(bytes));

        char[] characters = new char[bytes.Length * 2];
        for (int index = 0; index < bytes.Length; index++)
        {
            characters[index * 2] = HexadecimalDigits[bytes[index] >> 4];
            characters[(index * 2) + 1] = HexadecimalDigits[bytes[index] & 0x0F];
        }

        return new string(characters);
    }

    /// <summary>Formats bytes as lowercase hexadecimal, matching Convert.ToHexStringLower.</summary>
    public static string ToHexStringLower(byte[] bytes)
    {
        ThrowIfNull(bytes, nameof(bytes));

        return ToHexStringLower(bytes, offset: 0, bytes.Length);
    }

    /// <summary>Formats a byte range as lowercase hexadecimal, standing in for Convert.ToHexStringLower over a span.</summary>
    public static string ToHexStringLower(byte[] bytes, int offset, int length)
    {
        ThrowIfNull(bytes, nameof(bytes));
        ThrowIfRangeIsOutside(bytes.Length, offset, length, nameof(bytes));

        char[] characters = new char[length * 2];
        for (int index = 0; index < length; index++)
        {
            byte value = bytes[offset + index];
            characters[index * 2] = LowercaseHexadecimalDigits[value >> 4];
            characters[(index * 2) + 1] = LowercaseHexadecimalDigits[value & 0x0F];
        }

        return new string(characters);
    }

    /// <summary>Parses uppercase or lowercase hexadecimal, matching Convert.FromHexString.</summary>
    public static byte[] FromHexString(string hexadecimal)
    {
        ThrowIfNull(hexadecimal, nameof(hexadecimal));
        if (hexadecimal.Length % 2 != 0)
            throw new FormatException("A hexadecimal string must contain an even number of digits.");

        byte[] bytes = new byte[hexadecimal.Length / 2];
        for (int index = 0; index < bytes.Length; index++)
        {
            int high = ParseHexadecimalDigit(hexadecimal[index * 2]);
            int low = ParseHexadecimalDigit(hexadecimal[(index * 2) + 1]);
            bytes[index] = (byte)((high << 4) | low);
        }

        return bytes;
    }

    /// <summary>Hashes a whole stream from its current position, matching SHA256.HashData.</summary>
    public static byte[] ComputeSHA256(Stream stream)
    {
        ThrowIfNull(stream, nameof(stream));

        using SHA256 algorithm = SHA256.Create();
        return algorithm.ComputeHash(stream);
    }

    /// <summary>Hashes a buffer, matching SHA256.HashData.</summary>
    public static byte[] ComputeSHA256(byte[] buffer)
    {
        ThrowIfNull(buffer, nameof(buffer));

        using SHA256 algorithm = SHA256.Create();
        return algorithm.ComputeHash(buffer);
    }

    /// <summary>Hashes a byte range of a stream without loading it, used for payload verification.</summary>
    public static byte[] ComputeSHA256(Stream stream, long offset, long length)
    {
        ThrowIfNull(stream, nameof(stream));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

        stream.Position = offset;
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[HashStreamBufferSize];
        long remaining = length;
        while (remaining > 0)
        {
            int request = (int)Math.Min(buffer.Length, remaining);
            int read = stream.Read(buffer, offset: 0, request);
            if (read <= 0) throw new EndOfStreamException("The payload range ended before its declared length.");

            hash.AppendData(buffer, offset: 0, read);
            remaining -= read;
        }

        return hash.GetHashAndReset();
    }

    /// <summary>Compares two hashes without an early exit, matching CryptographicOperations.FixedTimeEquals.</summary>
    public static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        ThrowIfNull(left, nameof(left));
        ThrowIfNull(right, nameof(right));
        if (left.Length != right.Length) return false;

        int difference = 0;
        for (int index = 0; index < left.Length; index++)
            difference |= left[index] ^ right[index];

        return difference == 0;
    }

    /// <summary>Waits for a process to exit, matching Process.WaitForExitAsync.</summary>
    public static Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default)
    {
        ThrowIfNull(process, nameof(process));

        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => completion.TrySetResult(true);
        if (process.HasExited) completion.TrySetResult(true);
        if (!cancellationToken.CanBeCanceled) return completion.Task;

        CancellationTokenRegistration registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return completion.Task.ContinueWith(
            task =>
            {
                registration.Dispose();
                return task;
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default).Unwrap();
    }

    /// <summary>Matches ArgumentNullException.ThrowIfNull.</summary>
    public static void ThrowIfNull(object? argument, string parameterName)
    {
        if (argument == null) throw new ArgumentNullException(parameterName);
    }

    /// <summary>Matches ArgumentException.ThrowIfNullOrWhiteSpace.</summary>
    public static void ThrowIfNullOrWhiteSpace(string? argument, string parameterName)
    {
        if (argument == null) throw new ArgumentNullException(parameterName);
        if (string.IsNullOrWhiteSpace(argument))
            throw new ArgumentException("The value cannot be empty or whitespace.", parameterName);
    }

    /// <summary>Matches ObjectDisposedException.ThrowIf.</summary>
    public static void ThrowIfDisposed(bool disposed, object instance)
    {
        if (disposed) throw new ObjectDisposedException(instance?.GetType().FullName);
    }

    /// <summary>Matches string.Contains with a comparison, which the framework only offers for characters.</summary>
    public static bool Contains(this string text, string value, StringComparison comparison)
    {
        ThrowIfNull(text, nameof(text));
        ThrowIfNull(value, nameof(value));

        return text.IndexOf(value, comparison) >= 0;
    }

    /// <summary>
    /// Matches string.IsNullOrEmpty. The .NET Framework reference assemblies carry no nullable annotations,
    /// so the framework method never tells the compiler the value is non-null on the false branch.
    /// </summary>
    public static bool IsNullOrEmpty([NotNullWhen(false)] string? value) => string.IsNullOrEmpty(value);

    /// <summary>Matches string.IsNullOrWhiteSpace, and likewise restores the nullable flow information.</summary>
    public static bool IsNullOrWhiteSpace([NotNullWhen(false)] string? value) => string.IsNullOrWhiteSpace(value);

    /// <summary>Restricts a value to an inclusive range, matching Math.Clamp.</summary>
    public static int Clamp(int value, int min, int max)
    {
        if (min > max) throw new ArgumentException("The lower bound is above the upper bound.", nameof(min));
        if (value < min) return min;

        return value > max ? max : value;
    }

    /// <summary>Fills a buffer or throws, matching Stream.ReadExactly.</summary>
    public static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
    {
        ThrowIfNull(stream, nameof(stream));
        ThrowIfNull(buffer, nameof(buffer));
        ThrowIfRangeIsOutside(buffer.Length, offset, count, nameof(buffer));

        int filled = 0;
        while (filled < count)
        {
            int read = stream.Read(buffer, offset + filled, count - filled);
            if (read <= 0) throw new EndOfStreamException("The stream ended before the requested number of bytes was read.");

            filled += read;
        }
    }

    /// <summary>Compares two byte ranges, standing in for ReadOnlySpan&lt;byte&gt;.SequenceEqual.</summary>
    public static bool RangesEqual(byte[] left, int leftOffset, byte[] right, int rightOffset, int length)
    {
        ThrowIfNull(left, nameof(left));
        ThrowIfNull(right, nameof(right));
        ThrowIfRangeIsOutside(left.Length, leftOffset, length, nameof(left));
        ThrowIfRangeIsOutside(right.Length, rightOffset, length, nameof(right));

        for (int index = 0; index < length; index++)
        {
            if (left[leftOffset + index] != right[rightOffset + index]) return false;
        }

        return true;
    }

    // Little-endian accessors standing in for System.Buffers.Binary.BinaryPrimitives, which needs spans

    /// <summary>Reads a little-endian unsigned 16-bit value.</summary>
    public static ushort ReadUInt16LittleEndian(byte[] buffer, int offset)
    {
        ThrowIfNull(buffer, nameof(buffer));
        ThrowIfRangeIsOutside(buffer.Length, offset, sizeof(ushort), nameof(buffer));

        return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
    }

    /// <summary>Reads a little-endian unsigned 32-bit value.</summary>
    public static uint ReadUInt32LittleEndian(byte[] buffer, int offset) =>
        unchecked((uint)ReadInt32LittleEndian(buffer, offset));

    /// <summary>Reads a little-endian signed 32-bit value.</summary>
    public static int ReadInt32LittleEndian(byte[] buffer, int offset)
    {
        ThrowIfNull(buffer, nameof(buffer));
        ThrowIfRangeIsOutside(buffer.Length, offset, sizeof(int), nameof(buffer));

        return buffer[offset]
               | (buffer[offset + 1] << 8)
               | (buffer[offset + 2] << 16)
               | (buffer[offset + 3] << 24);
    }

    /// <summary>Reads a little-endian signed 64-bit value.</summary>
    public static long ReadInt64LittleEndian(byte[] buffer, int offset)
    {
        ThrowIfNull(buffer, nameof(buffer));
        ThrowIfRangeIsOutside(buffer.Length, offset, sizeof(long), nameof(buffer));

        uint low = ReadUInt32LittleEndian(buffer, offset);
        uint high = ReadUInt32LittleEndian(buffer, offset + sizeof(uint));
        return unchecked((long)(((ulong)high << 32) | low));
    }

    /// <summary>Writes a little-endian unsigned 16-bit value.</summary>
    public static void WriteUInt16LittleEndian(byte[] buffer, int offset, ushort value)
    {
        ThrowIfNull(buffer, nameof(buffer));
        ThrowIfRangeIsOutside(buffer.Length, offset, sizeof(ushort), nameof(buffer));

        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
    }

    /// <summary>Writes a little-endian unsigned 32-bit value.</summary>
    public static void WriteUInt32LittleEndian(byte[] buffer, int offset, uint value) =>
        WriteInt32LittleEndian(buffer, offset, unchecked((int)value));

    /// <summary>Writes a little-endian signed 32-bit value.</summary>
    public static void WriteInt32LittleEndian(byte[] buffer, int offset, int value)
    {
        ThrowIfNull(buffer, nameof(buffer));
        ThrowIfRangeIsOutside(buffer.Length, offset, sizeof(int), nameof(buffer));

        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    /// <summary>Writes a little-endian signed 64-bit value.</summary>
    public static void WriteInt64LittleEndian(byte[] buffer, int offset, long value)
    {
        ThrowIfNull(buffer, nameof(buffer));
        ThrowIfRangeIsOutside(buffer.Length, offset, sizeof(long), nameof(buffer));

        WriteInt32LittleEndian(buffer, offset, unchecked((int)value));
        WriteInt32LittleEndian(buffer, offset + sizeof(int), unchecked((int)(value >> 32)));
    }

    /// <summary>
    /// Appends one already-unquoted argument to ProcessStartInfo.Arguments, standing in for
    /// ProcessStartInfo.ArgumentList, which the .NET Framework does not define. The quoting follows the
    /// CommandLineToArgvW rules so a path with spaces or quotes survives the round trip.
    /// </summary>
    public static void AddArgument(this ProcessStartInfo startInfo, string argument)
    {
        ThrowIfNull(startInfo, nameof(startInfo));
        ThrowIfNull(argument, nameof(argument));

        StringBuilder builder = new(startInfo.Arguments ?? string.Empty);
        if (builder.Length != 0) builder.Append(ArgumentSeparator);

        if (argument.Length != 0 && !NeedsQuoting(argument))
        {
            builder.Append(argument);
            startInfo.Arguments = builder.ToString();
            return;
        }

        builder.Append(ArgumentQuote);
        int index = 0;
        while (index < argument.Length)
        {
            char character = argument[index];
            index++;
            if (character != ArgumentEscape)
            {
                // A quote inside the value has to reach the child as a literal quote
                if (character == ArgumentQuote) builder.Append(ArgumentEscape);

                builder.Append(character);
                continue;
            }

            // Backslashes only escape when a quote follows, so a run is doubled just before one
            int backslashCount = 1;
            while (index < argument.Length && argument[index] == ArgumentEscape)
            {
                index++;
                backslashCount++;
            }

            if (index == argument.Length)
            {
                builder.Append(ArgumentEscape, backslashCount * 2);
                continue;
            }

            if (argument[index] == ArgumentQuote)
            {
                builder.Append(ArgumentEscape, (backslashCount * 2) + 1);
                builder.Append(ArgumentQuote);
                index++;
                continue;
            }

            builder.Append(ArgumentEscape, backslashCount);
        }

        builder.Append(ArgumentQuote);
        startInfo.Arguments = builder.ToString();
    }

    /// <summary>Moves a file, matching the File.Move overload that takes an overwrite flag.</summary>
    public static void MoveFile(string sourcePath, string destinationPath, bool overwrite)
    {
        ThrowIfNullOrWhiteSpace(sourcePath, nameof(sourcePath));
        ThrowIfNullOrWhiteSpace(destinationPath, nameof(destinationPath));

        if (!overwrite)
        {
            File.Move(sourcePath, destinationPath);
            return;
        }

        // MoveFileEx with the replace flag is what File.Move(source, destination, overwrite) maps to. Deleting
        // the destination first and then moving would leave nothing behind if the move failed in between.
        if (MoveFileExW(sourcePath, destinationPath, MoveFileReplaceExisting)) return;

        throw new IOException(
            $"Could not replace {destinationPath}.",
            Marshal.GetHRForLastWin32Error());
    }

    /// <summary>
    /// Kills a process and everything it started, matching Process.Kill(entireProcessTree: true). The
    /// .NET Framework has no tree kill, so taskkill walks the tree and the direct kill is the fallback.
    /// </summary>
    public static void KillProcessTree(Process process)
    {
        ThrowIfNull(process, nameof(process));

        int processID = process.Id;
        string degradedReason;
        try
        {
            ProcessStartInfo startInfo = new()
            {
                // Resolved against System32 rather than by name: this can run elevated, and a bare name would
                // be resolved against the application directory, the current directory and PATH first
                FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    TaskKillExecutableName),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // The output streams are deliberately not redirected. taskkill /T prints a line per killed process,
            // and an undrained pipe would block it once the buffer filled, stalling the wait below
            startInfo.AddArgument("/PID");
            startInfo.AddArgument(ToInvariant(processID));
            startInfo.AddArgument("/T");
            startInfo.AddArgument("/F");
            using Process? taskKill = Process.Start(startInfo);
            if (taskKill == null)
            {
                degradedReason = "the process could not be started";
            }
            else
            {
                taskKill.WaitForExit(TaskKillWaitMs);
                if (taskKill.HasExited && taskKill.ExitCode == 0) return;

                degradedReason = taskKill.HasExited
                    ? $"it exited with code {ToInvariant(taskKill.ExitCode)}"
                    : $"it did not finish within {ToInvariant(TaskKillWaitMs)} ms";
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                              or InvalidOperationException
                                              or NotSupportedException
                                              or FileNotFoundException
                                              or ObjectDisposedException
                                              or IOException)
        {
            InstallerLog.Write($"FrameworkCompatibility: taskkill could not end the tree of PID {processID}", exception);
            degradedReason = exception.Message;
        }

        // A tree kill that quietly became a single-process kill is worth knowing about
        InstallerLog.Write(
            $"FrameworkCompatibility: taskkill did not end the tree of PID {processID} because {degradedReason}; "
            + "ending that process alone");
        try
        {
            process.Kill();
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                              or System.ComponentModel.Win32Exception
                                              or NotSupportedException)
        {
            InstallerLog.Write($"FrameworkCompatibility: PID {processID} could not be ended", exception);
        }
    }

    /// <summary>
    /// Splits on one separator, trimming each piece and dropping the empty ones. Stands in for
    /// string.Split(char, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).
    /// </summary>
    public static List<string> SplitTrimmed(string value, char separator)
    {
        List<string> pieces = [];
        if (string.IsNullOrEmpty(value)) return pieces;

        foreach (string piece in value.Split(separator))
        {
            string trimmed = piece.Trim();
            if (trimmed.Length != 0) pieces.Add(trimmed);
        }

        return pieces;
    }

    /// <summary>True when an argument carries whitespace or a quote and therefore has to be quoted.</summary>
    private static bool NeedsQuoting(string argument)
    {
        foreach (char character in argument)
        {
            if (char.IsWhiteSpace(character) || character == ArgumentQuote) return true;
        }

        return false;
    }

    private static void ThrowIfRangeIsOutside(int bufferLength, int offset, int length, string parameterName)
    {
        if (offset < 0 || length < 0 || bufferLength - offset < length)
            throw new ArgumentOutOfRangeException(parameterName, $"The range {offset}+{length} lies outside a {bufferLength}-byte buffer.");
    }

    private static int ParseHexadecimalDigit(char digit)
    {
        if (digit >= '0' && digit <= '9') return digit - '0';
        if (digit >= 'A' && digit <= 'F') return digit - 'A' + 10;
        if (digit >= 'a' && digit <= 'f') return digit - 'a' + 10;

        throw new FormatException($"'{digit}' is not a hexadecimal digit.");
    }

    private static string? ResolveProcessPath()
    {
        try
        {
            using Process current = Process.GetCurrentProcess();
            string? mainModulePath = current.MainModule?.FileName;
            if (!string.IsNullOrEmpty(mainModulePath)) return mainModulePath;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            InstallerLog.Write($"FrameworkCompatibility: the main module path was unavailable: {exception.Message}");
        }

        return System.Reflection.Assembly.GetEntryAssembly()?.Location;
    }

    private static int ResolveProcessID()
    {
        using Process current = Process.GetCurrentProcess();
        return current.Id;
    }

    /// <summary>Formats with the invariant culture, used where the modern overloads were implicit.</summary>
    public static string ToInvariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private const uint MoveFileReplaceExisting = 0x00000001;

    // DllImport stands in for LibraryImport; the .NET Framework has no source-generated interop
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(string existingFileName, string newFileName, uint flags);
}
