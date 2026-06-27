using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.UI;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class FanDragInstrumentationTests
{
    [Fact]
    public void AnnotationKeysMapToRequestedIndexingExpectations()
    {
        AssertExpectation(Key.A, FanDragExpectedIndexing.IndexAbove);
        AssertExpectation(Key.B, FanDragExpectedIndexing.IndexBelow);
        AssertExpectation(Key.I, FanDragExpectedIndexing.IndexIntoGroup);
        AssertExpectation(Key.O, FanDragExpectedIndexing.IndexOutOfGroup);
        AssertExpectation(Key.X, FanDragExpectedIndexing.NoIndexChange);
        Assert.False(FanDragInstrumentation.TryResolveExpectation(Key.C, out _));
    }

    [Fact]
    public void MovementTraceRecordsSkippedPointerPixelsAsInterpolatedFrames()
    {
        using TempTraceDirectory traceDirectory = new();
        FanDragInstrumentation recorder = new(traceDirectory.PathProvider);
        recorder.Begin(Start());

        recorder.RecordMovement(new Point(0, 10), (point, interpolated) => Capture(point, interpolated));
        recorder.RecordMovement(new Point(0, 13), (point, interpolated) => Capture(point, interpolated));
        recorder.End("complete");

        string[] frameLines = [.. recorder.Lines.Where(line => line.Contains("\"type\":\"frame\""))];
        Assert.Equal(4, frameLines.Length);
        Assert.Contains(frameLines, line => line.Contains("\"y\":11") && line.Contains("\"interpolated\":true"));
        Assert.Contains(frameLines, line => line.Contains("\"y\":12") && line.Contains("\"interpolated\":true"));
        Assert.Contains(frameLines, line => line.Contains("\"y\":13") && line.Contains("\"interpolated\":false"));
        Assert.True(File.Exists(Path.Combine(traceDirectory.Path, FanDragInstrumentation.LatestFileName)));
    }

    [Fact]
    public void AnnotationTraceRecordsTheCurrentFrameWithoutTimestampFields()
    {
        using TempTraceDirectory traceDirectory = new();
        FanDragInstrumentation recorder = new(traceDirectory.PathProvider);
        recorder.Begin(Start());
        recorder.RecordMovement(new Point(0, 10), (point, interpolated) => Capture(point, interpolated));

        bool recorded = recorder.RecordAnnotation(Key.I, new Point(0, 10),
            point => Capture(point, interpolated: false, stage: "key"));
        recorder.End("cancel");

        Assert.True(recorded);
        string keyLine = Assert.Single(recorder.Lines, line => line.Contains("\"type\":\"key\""));
        Assert.Contains("\"key\":\"i\"", keyLine);
        Assert.Contains("\"expectation\":\"IndexIntoGroup\"", keyLine);
        Assert.DoesNotContain("timestamp", string.Join('\n', recorder.Lines), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FrameTraceIncludesDebugMarkers()
    {
        using TempTraceDirectory traceDirectory = new();
        FanDragInstrumentation recorder = new(traceDirectory.PathProvider);
        recorder.Begin(Start());

        recorder.RecordMovement(new Point(0, 10), (point, interpolated) => Capture(point, interpolated));
        recorder.End("complete");

        string frameLine = Assert.Single(recorder.Lines, line => line.Contains("\"type\":\"frame\""));
        Assert.Contains("\"debugMarkers\"", frameLine);
        Assert.Contains("\"placement\"", frameLine);
    }

    private static void AssertExpectation(Key key, FanDragExpectedIndexing expected)
    {
        Assert.True(FanDragInstrumentation.TryResolveExpectation(key, out FanDragExpectedIndexing actual));
        Assert.Equal(expected, actual);
    }

    private static FanDragInstrumentationStart Start() =>
        new("fan", "Fan A", "Fan A", 0, 88, 36);

    private static FanDragInstrumentationCapture Capture(Point point, bool interpolated, string stage = "move")
    {
        Fan fan = new()
        {
            FansName = "Fan A",
            DataSourceKey = "Fan A",
        };
        FanFlyoutCell cell = new(null, [fan]);
        Border visual = new();
        FanDragSlot slot = new(cell, visual, 0, 80, 88, 0, 80);
        FanDragSnapshot snapshot = new(
            [slot],
            [],
            fan,
            cell,
            visual,
            0,
            88,
            36,
            80,
            0.5);
        FanDragBounds bounds = new(point.Y - 40, point.Y + 40, 80, point.Y, point.Y, true);
        FanDragEvaluation evaluation = FanDragEngine.Evaluate(snapshot, bounds);
        IReadOnlyList<FanDragDebugMarker> debugMarkers = FanDragEngine.CalculateDebugMarkers(snapshot, bounds);

        return new FanDragInstrumentationCapture(
            point,
            stage,
            interpolated,
            evaluation,
            evaluation.Placement,
            FanDragGhostStyle.TopLevelFan,
            [
                new FanDragInstrumentationSlot(
                    0,
                    "fan",
                    "Fan A",
                    0,
                    0,
                    80,
                    88,
                    0,
                    0,
                    80,
                    true),
            ],
            [],
            [.. debugMarkers.Select(marker => new FanDragInstrumentationDebugMarker(marker.Y, marker.Placement))],
            new FanDragInstrumentationGhost(0, point.Y - 40, 350, 80, 1),
            null);
    }

    private sealed class TempTraceDirectory : IDisposable
    {
        public TempTraceDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fan-drag-trace-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string PathProvider() => Path;

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // best-effort cleanup for test temp files
            }
        }
    }
}
