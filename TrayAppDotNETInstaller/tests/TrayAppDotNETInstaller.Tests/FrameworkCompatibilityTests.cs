using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

/// <summary>
/// Covers the stand-ins whose behaviour is not already exercised through the services that use them.
/// AddArgument replaces ProcessStartInfo.ArgumentList, so every process the installer starts depends on its
/// quoting; the round trip is checked against the same parser the child processes use.
/// </summary>
public sealed class FrameworkCompatibilityTests
{
    private const string FakeExecutableName = "x.exe";

    [Theory]
    [InlineData("simple")]
    [InlineData("--desktop-shortcut")]
    [InlineData(@"C:\Program Files\TrayAppDotNET\VolumeTrayAppDotNET.exe")]
    [InlineData(@"C:\a trailing\separator\")]
    [InlineData("has \"quotes\" inside")]
    [InlineData(@"back\\slashes\\\""and a quote")]
    [InlineData("")]
    public void AddArgument_RoundTripsThroughCommandLineToArgvW(string argument)
    {
        ProcessStartInfo startInfo = new() { FileName = FakeExecutableName };

        startInfo.AddArgument("first");
        startInfo.AddArgument(argument);
        startInfo.AddArgument("last");

        // The executable name leads the line because CommandLineToArgvW parses argv[0] by its own rules
        string[] parsed = ParseCommandLine($"{FakeExecutableName} {startInfo.Arguments}");

        Assert.Equal([FakeExecutableName, "first", argument, "last"], parsed);
    }

    [Fact]
    public void AddArgument_LeavesUnquotedArgumentsAlone()
    {
        ProcessStartInfo startInfo = new() { FileName = FakeExecutableName };

        startInfo.AddArgument("--mode");
        startInfo.AddArgument("system");

        Assert.Equal("--mode system", startInfo.Arguments);
    }

    [Theory]
    [InlineData("A,B,C", new[] { "A", "B", "C" })]
    [InlineData(" A , B ", new[] { "A", "B" })]
    [InlineData(",", new string[0])]
    [InlineData("", new string[0])]
    public void SplitTrimmed_DropsEmptyEntriesAndTrimsTheRest(string value, string[] expected)
    {
        Assert.Equal(expected, FrameworkCompatibility.SplitTrimmed(value, separator: ','));
    }

    [Fact]
    public void LittleEndianAccessors_RoundTripEveryWidth()
    {
        byte[] buffer = new byte[16];

        FrameworkCompatibility.WriteUInt16LittleEndian(buffer, offset: 0, ushort.MaxValue - 1);
        FrameworkCompatibility.WriteUInt32LittleEndian(buffer, offset: 2, uint.MaxValue - 2);
        FrameworkCompatibility.WriteInt64LittleEndian(buffer, offset: 6, long.MinValue + 3);

        Assert.Equal(ushort.MaxValue - 1, FrameworkCompatibility.ReadUInt16LittleEndian(buffer, offset: 0));
        Assert.Equal(uint.MaxValue - 2, FrameworkCompatibility.ReadUInt32LittleEndian(buffer, offset: 2));
        Assert.Equal(long.MinValue + 3, FrameworkCompatibility.ReadInt64LittleEndian(buffer, offset: 6));
        // Little-endian means the low byte lands first
        Assert.Equal(0xFE, buffer[0]);
        Assert.Equal(0xFF, buffer[1]);
    }

    /// <summary>Parses a command line the way CreateProcess children do, so the quoting is checked for real.</summary>
    private static string[] ParseCommandLine(string commandLine)
    {
        IntPtr block = CommandLineToArgvW(commandLine, out int argumentCount);
        Assert.NotEqual(IntPtr.Zero, block);
        try
        {
            string[] arguments = new string[argumentCount];
            for (int index = 0; index < argumentCount; index++)
            {
                IntPtr argumentPointer = Marshal.ReadIntPtr(block, index * IntPtr.Size);
                arguments[index] = Marshal.PtrToStringUni(argumentPointer) ?? string.Empty;
            }

            return arguments;
        }
        finally
        {
            LocalFree(block);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
