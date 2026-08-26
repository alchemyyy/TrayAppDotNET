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
        int totalSampleCount = TaskManagerTrayIcon.HistoryCapacity + 2;
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
        Assert.Equal(2, averageInput.Values[0]);
        Assert.Equal(totalSampleCount - 1, averageInput.Values[^1]);
        Assert.Equal(52, highestCoreInput.Values[0]);
        Assert.Equal(50 + totalSampleCount - 1, highestCoreInput.Values[^1]);
    }

    [Theory]
    [InlineData(TrayGraphStyle.Current)]
    [InlineData(TrayGraphStyle.Marquee)]
    public void RenderedPNGIsSquareOpaqueAndUsesTheFullCanvas(TrayGraphStyle style)
    {
        const int iconSize = 16;
        TaskManagerTrayIconRenderInput input = new(style, [10, 30, 20, 60]);

        byte[] imageBytes = TaskManagerTrayIcon.RenderPng(iconSize, input);
        using SKBitmap bitmap = SKBitmap.Decode(imageBytes)
                                ?? throw new InvalidOperationException("Tray icon PNG could not be decoded.");

        Assert.Equal(iconSize, bitmap.Width);
        Assert.Equal(iconSize, bitmap.Height);
        Assert.Equal(byte.MaxValue, bitmap.GetPixel(0, 0).Alpha);
        Assert.Equal(byte.MaxValue, bitmap.GetPixel(iconSize - 1, iconSize - 1).Alpha);
        Assert.True(bitmap.GetPixel(6, 13).Green > bitmap.GetPixel(6, 2).Green);
    }

    [Fact]
    public void CurrentAndMarqueeStylesProduceDifferentGraphs()
    {
        const int iconSize = 32;
        double[] values = [10, 80, 20, 60];

        byte[] currentImageBytes = TaskManagerTrayIcon.RenderPng(
            iconSize,
            new TaskManagerTrayIconRenderInput(TrayGraphStyle.Current, values));
        byte[] marqueeImageBytes = TaskManagerTrayIcon.RenderPng(
            iconSize,
            new TaskManagerTrayIconRenderInput(TrayGraphStyle.Marquee, values));

        Assert.False(currentImageBytes.SequenceEqual(marqueeImageBytes));
    }
}
