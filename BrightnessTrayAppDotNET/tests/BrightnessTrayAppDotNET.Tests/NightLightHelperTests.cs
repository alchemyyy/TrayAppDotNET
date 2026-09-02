using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using BrightnessTrayAppDotNET.Interop.NightLight;
using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.Services;
using BrightnessTrayAppDotNET.UI.Flyout;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class NightLightHelperTests
{
    [Fact]
    public void ProviderCapabilityProbeDoesNotStartNativeHelper()
    {
        Assert.False(NightLightHelperClient.HasStartedInitialization);

        AppSettings settings = new();
        NightLightProvider.Initialize(settings);
        _ = NightLightProvider.IsSupported();

        Assert.False(NightLightHelperClient.HasStartedInitialization);
    }

    [Fact]
    public void LatestQueueReplacesPendingIntermediateValue()
    {
        NightLightLatestStrengthQueue queue = new();

        bool replacedFirst = queue.Store(10);
        bool replacedSecond = queue.Store(80);
        bool tookValue = queue.TryTake(out int value);

        Assert.False(replacedFirst);
        Assert.True(replacedSecond);
        Assert.True(tookValue);
        Assert.Equal(expected: 80, value);
        Assert.False(queue.TryTake(out int ignoredValue));
        Assert.Equal(expected: 0, ignoredValue);
    }

    [Fact]
    public void FailedValueDoesNotReplaceNewerPendingValue()
    {
        NightLightLatestStrengthQueue queue = new();
        queue.Store(20);
        Assert.True(queue.TryTake(out int inFlightValue));

        queue.Store(90);
        bool restored = queue.RestoreIfEmpty(inFlightValue);

        Assert.False(restored);
        Assert.True(queue.TryTake(out int pendingValue));
        Assert.Equal(expected: 90, pendingValue);
    }

    [Fact]
    public void FailedValueIsRestoredWhenNoReplacementExists()
    {
        NightLightLatestStrengthQueue queue = new();
        queue.Store(35);
        Assert.True(queue.TryTake(out int inFlightValue));

        bool restored = queue.RestoreIfEmpty(inFlightValue);

        Assert.True(restored);
        Assert.True(queue.TryTake(out int pendingValue));
        Assert.Equal(expected: 35, pendingValue);
    }

    [Fact]
    public void RecyclePolicyWarmsBeforeHardOperationLimit()
    {
        const int warmupThreshold = Constants.NightLightHelperRecycleOperationCount -
                                    Constants.NightLightHelperWarmupLeadOperationCount;

        Assert.False(NightLightHelperClient.ShouldStartWarmup(warmupThreshold - 1));
        Assert.True(NightLightHelperClient.ShouldStartWarmup(warmupThreshold));
        Assert.False(NightLightHelperClient.ShouldRecycle(
            Constants.NightLightHelperRecycleOperationCount - 1));
        Assert.True(NightLightHelperClient.ShouldRecycle(
            Constants.NightLightHelperRecycleOperationCount));
    }

    [Theory]
    [InlineData("UNSUPPORTED\tinvalid-init", true, "invalid-init")]
    [InlineData("UNSUPPORTED", false, "")]
    [InlineData("UNSUPPORTED\t", false, "")]
    [InlineData("UNSUPPORTED\treason\textra", false, "")]
    [InlineData("FAIL", false, "")]
    public void UnsupportedResponseRequiresOneReasonToken(
        string response,
        bool expected,
        string expectedReason)
    {
        bool parsed = NightLightHelperProtocol.TryParseUnsupportedResponse(response, out string reason);

        Assert.Equal(expected, parsed);
        Assert.Equal(expectedReason, reason);
    }

    [Fact]
    public void ActiveCommandSerializerRejectsInvalidCombinations()
    {
        Assert.Throws<ArgumentException>(
            () => NightLightHelperProtocol.SerializeSetEnabled(enabled: false, enableStrength: 50));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NightLightHelperProtocol.SerializeSetEnabled(enabled: true, enableStrength: 101));
    }

    [Fact]
    public async Task NativeSidecarRejectsMalformedInitialization()
    {
        string helperPath = Path.Combine(AppContext.BaseDirectory, Constants.NativeHelpersFileName);
        Assert.True(File.Exists(helperPath), $"Native helper is missing at '{helperPath}'.");

        string pipeName = "BrightnessTrayAppNightLightTest_" + Guid.NewGuid().ToString("N");
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
        startInfo.ArgumentList.Add(NightLightHelperProtocol.ServerArg);
        startInfo.ArgumentList.Add(NightLightHelperProtocol.ParentProcessIDArg);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(NightLightHelperProtocol.PipeNameArg);
        startInfo.ArgumentList.Add(pipeName);

        using Process process = Process.Start(startInfo)
                                ?? throw new InvalidOperationException("Native helper process did not start.");
        try
        {
            await pipe.WaitForConnectionAsync().WaitAsync(TimeSpan.FromSeconds(5));
            using StreamReader reader = new(
                pipe,
                NightLightHelperProtocol.PipeEncoding,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            using StreamWriter writer = new(
                pipe,
                NightLightHelperProtocol.PipeEncoding,
                bufferSize: 1024,
                leaveOpen: true)
            {
                AutoFlush = true
            };

            await writer.WriteLineAsync("INIT\tinvalid");
            string? response = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("UNSUPPORTED\tinvalid-init", response);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void ProfileAutosaveWaitsForSliderGestureCompletion(
        bool autosaveEnabled,
        bool isAnySliderDragging,
        bool expected)
    {
        bool actual = BrightnessFlyoutWindow.CanAutosaveProfile(
            autosaveEnabled,
            isAnySliderDragging);

        Assert.Equal(expected, actual);
    }
}
