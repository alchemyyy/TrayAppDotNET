using SkiaSharp;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI.Tray;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class TaskManagerTrayIconTests
{
    [Fact]
    public void HistoryKeepsNewestSamplesAndAllDataSources()
    {
        using TaskManagerTrayIcon renderer = new();
        const int totalSampleCount = TaskManagerTrayIcon.HistoryCapacity + 2;
        for (int sampleIndex = 0; sampleIndex < totalSampleCount; sampleIndex++)
        {
            renderer.AddSample(new SystemPerformanceSample(
                sampleIndex,
                50 + sampleIndex,
                80 + sampleIndex));
        }

        TaskManagerTrayIconRenderInput averageInput = renderer.CreateRenderInput(
            TrayGraphStyle.Marquee,
            TrayGraphDataSource.CPUAverage);
        TaskManagerTrayIconRenderInput highestCoreInput = renderer.CreateRenderInput(
            TrayGraphStyle.Marquee,
            TrayGraphDataSource.CPUHighestCore);

        Assert.Equal(TaskManagerTrayIcon.HistoryCapacity, averageInput.Values.Length);
        Assert.Equal(TrayGraphDataSource.CPUAverage, averageInput.DataSource);
        Assert.Equal(TrayGraphDataSource.CPUHighestCore, highestCoreInput.DataSource);
        Assert.Equal(expected: 2, averageInput.Values[0]);
        Assert.Equal(totalSampleCount - 1, averageInput.Values[^1]);
        Assert.Equal(expected: 52, highestCoreInput.Values[0]);
        Assert.Equal(50 + totalSampleCount - 1, highestCoreInput.Values[^1]);
    }

    [Theory]
    [InlineData(TrayGraphStyle.Current)]
    [InlineData(TrayGraphStyle.Marquee)]
    public void RenderedPNGHasRoundedCornersAndUsesTheInterior(TrayGraphStyle style)
    {
        const int iconSize = 16;
        TaskManagerTrayIconRenderInput input = new(
            style,
            TrayGraphDataSource.CPUAverage,
            [10, 30, 20, 60]);

        byte[] imageBytes = TaskManagerTrayIcon.RenderPng(iconSize, input);
        using SKBitmap bitmap = SKBitmap.Decode(imageBytes)
                                ?? throw new InvalidOperationException("Tray icon PNG could not be decoded.");

        Assert.Equal(iconSize, bitmap.Width);
        Assert.Equal(iconSize, bitmap.Height);
        Assert.True(bitmap.GetPixel(x: 0, y: 0).Alpha < byte.MaxValue);
        Assert.True(bitmap.GetPixel(iconSize - 1, iconSize - 1).Alpha < byte.MaxValue);
        Assert.Equal(byte.MaxValue, bitmap.GetPixel(x: 8, y: 8).Alpha);
        Assert.True(bitmap.GetPixel(x: 6, y: 13).Green > bitmap.GetPixel(x: 6, y: 2).Green);
    }

    [Fact]
    public void CurrentAndMarqueeStylesProduceDifferentGraphs()
    {
        const int iconSize = 32;
        double[] values = [10, 80, 20, 60];

        byte[] currentImageBytes = TaskManagerTrayIcon.RenderPng(
            iconSize,
            new TaskManagerTrayIconRenderInput(
                TrayGraphStyle.Current,
                TrayGraphDataSource.CPUAverage,
                values));
        byte[] marqueeImageBytes = TaskManagerTrayIcon.RenderPng(
            iconSize,
            new TaskManagerTrayIconRenderInput(
                TrayGraphStyle.Marquee,
                TrayGraphDataSource.CPUAverage,
                values));

        Assert.False(currentImageBytes.SequenceEqual(marqueeImageBytes));
    }

    [Fact]
    public void DataSourceSelectsTheMatchingPerformanceGraphAccent()
    {
        const int iconSize = 32;
        double[] values = [10, 80, 20, 60];

        byte[] cpuImageBytes = TaskManagerTrayIcon.RenderPng(
            iconSize,
            new TaskManagerTrayIconRenderInput(
                TrayGraphStyle.Marquee,
                TrayGraphDataSource.CPUAverage,
                values));
        byte[] memoryImageBytes = TaskManagerTrayIcon.RenderPng(
            iconSize,
            new TaskManagerTrayIconRenderInput(
                TrayGraphStyle.Marquee,
                TrayGraphDataSource.Memory,
                values));

        Assert.False(cpuImageBytes.SequenceEqual(memoryImageBytes));
    }
}
