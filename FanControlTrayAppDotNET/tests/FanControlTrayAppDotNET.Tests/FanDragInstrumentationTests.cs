using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.UI.Flyout;
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

        recorder.RecordMovement(new Point(x: 0, y: 10), (point, interpolated) => Capture(point, interpolated));
        recorder.RecordMovement(new Point(x: 0, y: 13), (point, interpolated) => Capture(point, interpolated));
        recorder.End("complete");

        string[] frameLines = [.. recorder.Lines.Where(line => line.Contains("\"type\":\"frame\""))];
        Assert.Equal(expected: 4, frameLines.Length);
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
        recorder.RecordMovement(new Point(x: 0, y: 10), (point, interpolated) => Capture(point, interpolated));

        bool recorded = recorder.RecordAnnotation(Key.I, new Point(x: 0, y: 10),
            point => Capture(point, interpolated: false, stage: "key"));
        recorder.End("cancel");

        Assert.True(recorded);
        string keyLine = Assert.Single(recorder.Lines, line => line.Contains("\"type\":\"key\""));
        Assert.Contains(expectedSubstring: "\"key\":\"i\"", keyLine);
        Assert.Contains(expectedSubstring: "\"expectation\":\"IndexIntoGroup\"", keyLine);
        Assert.DoesNotContain(expectedSubstring: "timestamp", string.Join(separator: '\n', recorder.Lines),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FrameTraceIncludesDebugMarkers()
    {
        using TempTraceDirectory traceDirectory = new();
        FanDragInstrumentation recorder = new(traceDirectory.PathProvider);
        recorder.Begin(Start());

        recorder.RecordMovement(new Point(x: 0, y: 10), (point, interpolated) => Capture(point, interpolated));
        recorder.End("complete");

        string frameLine = Assert.Single(recorder.Lines, line => line.Contains("\"type\":\"frame\""));
        Assert.Contains(expectedSubstring: "\"debugMarkers\"", frameLine);
        Assert.Contains(expectedSubstring: "\"placement\"", frameLine);
    }

    private static void AssertExpectation(Key key, FanDragExpectedIndexing expected)
    {
        Assert.True(FanDragInstrumentation.TryResolveExpectation(key, out FanDragExpectedIndexing actual));
        Assert.Equal(expected, actual);
    }

    private static FanDragInstrumentationStart Start() =>
        new(SourceKind: "fan", SourceName: "Fan A", SourceCell: "Fan A", SourceTopLevelIndex: 0, SourceSlotHeight: 88,
            SourceFanSlotHeight: 36);

    private static FanDragInstrumentationCapture Capture(Point point, bool interpolated, string stage = "move")
    {
        Fan fan = new() { FansName = "Fan A", DataSourceKey = "Fan A" };
        FanFlyoutCell cell = new(groupSettings: null, [fan]);
        Border visual = new();
        FanDragSlot slot = new(cell, visual, Top: 0, Height: 80, SlotHeight: 88, GroupInsertionTop: 0,
            GroupDropBottom: 80);
        FanDragSnapshot snapshot = new(
            [slot],
            [],
            fan,
            cell,
            visual,
            DragSourceTopLevelIndex: 0,
            DragSourceSlotHeight: 88,
            DragSourceFanSlotHeight: 36,
            DragPlacementSourceHeight: 80,
            DragPointerOffsetRatio: 0.5);
        FanDragBounds bounds = new(point.Y - 40, point.Y + 40, Height: 80, point.Y, point.Y, MovingDown: true);
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
                    Index: 0,
                    Kind: "fan",
                    Name: "Fan A",
                    Top: 0,
                    VisualTop: 0,
                    Height: 80,
                    SlotHeight: 88,
                    RenderOffsetY: 0,
                    GroupInsertionTop: 0,
                    GroupDropBottom: 80,
                    IsDragSource: true)
            ],
            [],
            [.. debugMarkers.Select(marker => new FanDragInstrumentationDebugMarker(marker.Y, marker.Placement))],
            new FanDragInstrumentationGhost(Left: 0, point.Y - 40, Width: 350, Height: 80, Opacity: 1),
            GroupPreview: null);
    }

    private sealed class TempTraceDirectory : IDisposable
    {
        public TempTraceDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), path2: "fan-drag-trace-tests",
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
