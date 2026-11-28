using Avalonia.Media;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI.Performance;
using static TaskManagerTrayAppDotNET.Tests.TestLocalization;
using AppStrings = TaskManagerTrayAppDotNET.Localization.Strings;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class PerformanceDeviceCardFormatTests
{
    private static string Unavailable => L(nameof(AppStrings.Perf_Unavailable));

    private static readonly PerformanceCardValue[] Values =
    [
        new(Name: "utilization", Value: "42%"),
        new(Name: "speed", Value: null),
        new(Name: "temperature", Value: "61 \u00B0C")
    ];

    [Theory]
    [InlineData("{utilization}[  {speed}]", "42%")]
    [InlineData("{utilization}[  {temperature}]", "42%  61 \u00B0C")]
    [InlineData("[{speed}][busy]", "busy")]
    [InlineData("{UTILIZATION} of {description}", "42% of GPU name")]
    [InlineData("{{utilization}} [[x]]", "{utilization} [x]")]
    [InlineData("{unknown} {", "{unknown} {")]
    [InlineData("[{unknown}]", "{unknown}")]
    [InlineData("a]b[c", "a]b[c")]
    public void LineFormatInsertsValuesAndDropsOptionalTextWithUnreportedValues(string format, string expected)
    {
        string rendered = PerformanceCardLineFormat.Parse(format).Render(Values, description: "GPU name");

        Assert.Equal(expected, rendered);
    }

    [Fact]
    public void UnreportedValueOutsideOptionalTextShowsUnavailable()
    {
        string rendered = PerformanceCardLineFormat.Parse("S: {speed}").Render(Values, description: string.Empty);

        Assert.Equal(string.Concat("S: ", Unavailable), rendered);
    }

    [Fact]
    public void CustomizationReplacesTitleDescriptionLinesAndColor()
    {
        PerformanceDevicePresentation device = CreateGPU();
        PerformanceDeviceCardCustomization customization = new()
        {
            DeviceID = device.DeviceID,
            Title = "Main GPU",
            LineCount = 2,
            FirstLineFormat = "{description} ({temperature})",
            AccentColor = "#102030",
            DescriptionReplacements =
            [
                new PerformanceTextReplacementRule { MatchPattern = "(", Replacement = "ignored" },
                new PerformanceTextReplacementRule { MatchPattern = "^NVIDIA GeForce (.+)$", Replacement = "$1" }
            ]
        };

        PerformanceDeviceCardLayout layout = PerformanceDeviceCardLayout.Create(device.Kind, customization);
        PerformanceDeviceCardText card = layout.Format(device);

        Assert.Equal(expected: "Main GPU", card.Title);
        Assert.Equal(expected: "RTX 5090 (61 \u00B0C)", card.FirstLine);
        // The second line keeps the GPU's default format
        Assert.Equal(expected: "42%  61 \u00B0C", card.SecondLine);
        Assert.True(layout.CustomizesDescription);
        Assert.Equal(Color.Parse("#102030"), layout.Accent);
    }

    [Theory]
    [InlineData(0, "", "")]
    [InlineData(1, "NVIDIA GeForce RTX 5090", "")]
    public void LineCountHidesTheLinesPastIt(int lineCount, string firstLine, string secondLine)
    {
        PerformanceDevicePresentation device = CreateGPU();

        PerformanceDeviceCardText card = PerformanceDeviceCardLayout.Create(
                device.Kind,
                new PerformanceDeviceCardCustomization { DeviceID = device.DeviceID, LineCount = lineCount })
            .Format(device);

        Assert.Equal(firstLine, card.FirstLine);
        Assert.Equal(secondLine, card.SecondLine);
    }

    [Fact]
    public void CustomColorDrawsTheSecondTraceInALighterShade()
    {
        Color custom = Color.FromRgb(r: 0x20, g: 0x40, b: 0x80);

        PerformanceDeviceAccent accent = PerformanceDeviceAccent.Resolve(PerformanceDeviceKind.Disk, custom);

        Assert.Equal(custom, accent.Primary);
        Assert.True(accent.Secondary.R > custom.R && accent.Secondary.G > custom.G && accent.Secondary.B > custom.B);
    }

    [Fact]
    public void NormalizeKeepsTheLastEntryPerDeviceAndDropsDefaultsAndBadLineCounts()
    {
        List<PerformanceDeviceCardCustomization> normalized = PerformanceDeviceCardCustomizationCollection.Normalize(
        [
            new PerformanceDeviceCardCustomization { DeviceID = "gpu:0", Title = "Old" },
            new PerformanceDeviceCardCustomization { DeviceID = "gpu:0", Title = "New", LineCount = 7 },
            new PerformanceDeviceCardCustomization { DeviceID = "cpu" },
            new PerformanceDeviceCardCustomization { DeviceID = " ", Title = "No device" }
        ]);

        PerformanceDeviceCardCustomization card = Assert.Single(normalized);
        Assert.Equal(expected: "New", card.Title);
        Assert.Equal(PerformanceDeviceCardCustomization.DefaultLineCount, card.LineCount);
    }

    private static PerformanceDevicePresentation CreateGPU()
    {
        GPUPerformanceSnapshot GPU = new(
            DeviceID: "gpu:test",
            PerformanceDeviceKind.GPU,
            SortKey: 0,
            Name: "NVIDIA GeForce RTX 5090",
            AdapterLUID: 0,
            PhysicalAdapterIndex: 0,
            HasUtilizationSample: true,
            UtilizationPercent: 42,
            Engines: ReadOnlyMemory<GPUPerformanceEngineSnapshot>.Empty,
            HasDedicatedMemoryData: false,
            DedicatedMemoryBytes: 0,
            DedicatedMemoryCapacityBytes: 0,
            HasSharedMemoryData: false,
            SharedMemoryBytes: 0,
            SharedMemoryCapacityBytes: 0);
        return PerformanceDevicePresentationFactory.Create(PerformanceSnapshot.Empty with { GPUs = new[] { GPU } })
            .Single(static candidate => candidate.Kind == PerformanceDeviceKind.GPU) with
            {
                CardValues = Values
            };
    }
}
