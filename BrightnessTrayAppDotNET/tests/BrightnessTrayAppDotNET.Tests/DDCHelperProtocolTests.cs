using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using BrightnessTrayAppDotNET.DDCCI;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class DDCHelperProtocolTests
{
    [Fact]
    public void BuildCommandPreservesMonitorIdentityAndArguments()
    {
        DDCMonitor monitor = new()
        {
            DeviceID = @"MONITOR\ABC123\instance",
            EDIDSerial = "serial-123",
            Name = @"\\.\DISPLAY7",
            DisplayInstancePath = @"DISPLAY\ABC123\instance"
        };

        string command = DDCHelperProtocol.BuildCommand("SETVCP", monitor, "10", "73");
        string[] fields = command.Split('\t');

        Assert.Equal(7, fields.Length);
        Assert.Equal("SETVCP", fields[0]);
        Assert.Equal(monitor.DeviceID, DDCHelperProtocol.DecodeField(fields[1]));
        Assert.Equal(monitor.EDIDSerial, DDCHelperProtocol.DecodeField(fields[2]));
        Assert.Equal(monitor.Name, DDCHelperProtocol.DecodeField(fields[3]));
        Assert.Equal(monitor.DisplayInstancePath, DDCHelperProtocol.DecodeField(fields[4]));
        Assert.Equal("10", fields[5]);
        Assert.Equal("73", fields[6]);
    }

    [Fact]
    public void FieldEncodingRoundTripsUnicode()
    {
        const string value = "Display \u03a9 \u4eae\u5ea6";

        string encoded = DDCHelperProtocol.EncodeField(value);

        Assert.Equal(value, DDCHelperProtocol.DecodeField(encoded));
    }

    [Theory]
    [InlineData("READY\t1", true)]
    [InlineData("READY\t2", false)]
    [InlineData("OK", false)]
    [InlineData(null, false)]
    public void ReadyResponseRequiresExactProtocolVersion(string? response, bool expected)
    {
        Assert.Equal(expected, DDCHelperProtocol.IsReadyResponse(response));
    }

    [Fact]
    public async Task NativeSidecarCompletesProtocolHandshake()
    {
        string helperPath = Path.Combine(AppContext.BaseDirectory, Constants.NativeHelpersFileName);
        Assert.True(File.Exists(helperPath), $"Native helper is missing at '{helperPath}'.");

        string pipeName = "BrightnessTrayAppDDCTest_" + Guid.NewGuid().ToString("N");
        await using NamedPipeServerStream pipe = new(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        ProcessStartInfo startInfo = new()
        {
            FileName = helperPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add(DDCHelperProtocol.ServerArgument);
        startInfo.ArgumentList.Add(DDCHelperProtocol.ParentProcessIDArgument);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(DDCHelperProtocol.PipeNameArgument);
        startInfo.ArgumentList.Add(pipeName);

        using Process process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("Native helper process did not start.");
        try
        {
            await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(5));
            using StreamReader reader = new(
                pipe,
                DDCHelperProtocol.PipeEncoding,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            using StreamWriter writer = new(
                pipe,
                DDCHelperProtocol.PipeEncoding,
                bufferSize: 1024,
                leaveOpen: true)
            {
                AutoFlush = true
            };

            string? ready = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(DDCHelperProtocol.ReadyResponse, ready);

            await writer.WriteLineAsync(DDCHelperProtocol.PingCommand);
            string? pong = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(DDCHelperProtocol.PingResponse, pong);

            await writer.WriteLineAsync(DDCHelperProtocol.ExitCommand);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }
}
