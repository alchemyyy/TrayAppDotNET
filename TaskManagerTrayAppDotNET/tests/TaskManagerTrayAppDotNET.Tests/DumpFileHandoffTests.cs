using System.IO.Pipes;
using Microsoft.Win32.SafeHandles;
using TaskManagerTrayAppDotNET.Services;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class DumpFileHandoffTests : IDisposable
{
    // GetCurrentProcess pseudo-handle: lets the test take handles from itself as the broker takes them from the UI
    private static readonly IntPtr ThisProcess = new(-1);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "TaskManagerTrayAppDotNET.Tests",
        nameof(DumpFileHandoffTests) + "-" + Guid.NewGuid().ToString("N"));

    public DumpFileHandoffTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(0UL, false)]
    [InlineData(4UL, true)]
    [InlineData(0x1A4UL, true)]
    [InlineData(6UL, false)]                   // not a multiple of four
    [InlineData(0x8000_0000UL, false)]         // beyond any handle table
    [InlineData(ulong.MaxValue, false)]
    public void OnlyPlausibleHandleValuesAreAccepted(ulong value, bool expected) =>
        Assert.Equal(expected, DumpFileHandoff.IsPlausibleHandleValue(value));

    [Theory]
    [InlineData(DumpFileHandoff.FileTypeDisk, true, 0x20u, 1u, 0UL, true)]          // empty archive file
    [InlineData(0x0003u, true, 0x20u, 1u, 0UL, false)]                             // a pipe
    [InlineData(0x0002u, true, 0x20u, 1u, 0UL, false)]                             // a character device
    [InlineData(DumpFileHandoff.FileTypeDisk, false, 0u, 0u, 0UL, false)]          // a volume: no file information
    [InlineData(DumpFileHandoff.FileTypeDisk, true, 0x10u, 1u, 0UL, false)]        // a directory
    [InlineData(DumpFileHandoff.FileTypeDisk, true, 0x420u, 1u, 0UL, false)]       // a reparse point
    [InlineData(DumpFileHandoff.FileTypeDisk, true, 0x20u, 0u, 0UL, false)]        // deleted, no links left
    [InlineData(DumpFileHandoff.FileTypeDisk, true, 0x20u, 1u, 1UL, false)]        // existing data
    public void OnlyAnEmptyRegularFileIsAcceptedAsADumpTarget(
        uint fileType,
        bool hasFileInformation,
        uint attributes,
        uint linkCount,
        ulong length,
        bool expected)
    {
        bool accepted = DumpFileHandoff.IsAcceptableDumpFile(
            fileType,
            hasFileInformation,
            attributes,
            linkCount,
            length,
            out string reason);

        Assert.Equal(expected, accepted);
        Assert.Equal(expected, reason.Length == 0);
    }

    [Fact]
    public void TakesAnEmptyFileHandleFromTheOwningProcess()
    {
        using SafeFileHandle file = CreateFile("empty.dmp");

        using SafeFileHandle? taken = DumpFileHandoff.TryTake(
            ThisProcess,
            (ulong)file.DangerousGetHandle(),
            out string errorMessage);

        Assert.NotNull(taken);
        Assert.Empty(errorMessage);
    }

    [Fact]
    public void RefusesAFileThatAlreadyHasData()
    {
        using SafeFileHandle file = CreateFile("used.dmp");
        RandomAccess.Write(file, "data"u8, fileOffset: 0);

        using SafeFileHandle? taken = DumpFileHandoff.TryTake(
            ThisProcess,
            (ulong)file.DangerousGetHandle(),
            out string errorMessage);

        Assert.Null(taken);
        Assert.NotEmpty(errorMessage);
    }

    [Fact]
    public void RefusesAPipeHandle()
    {
        using AnonymousPipeServerStream pipe = new(PipeDirection.Out);

        using SafeFileHandle? taken = DumpFileHandoff.TryTake(
            ThisProcess,
            (ulong)pipe.SafePipeHandle.DangerousGetHandle(),
            out string errorMessage);

        Assert.Null(taken);
        Assert.NotEmpty(errorMessage);
    }

    [Fact]
    public void RefusesWithoutTheUIProcess()
    {
        using SafeFileHandle file = CreateFile("orphan.dmp");

        Assert.Null(DumpFileHandoff.TryTake(IntPtr.Zero, (ulong)file.DangerousGetHandle(), out _));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of the test's own temp folder
        }
    }

    private SafeFileHandle CreateFile(string name) =>
        File.OpenHandle(Path.Combine(_directory, name), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
}
