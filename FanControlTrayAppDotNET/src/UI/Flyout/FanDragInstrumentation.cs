using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Input;

namespace FanControlTrayAppDotNET.UI;

internal sealed class FanDragInstrumentation
{
    public const string LatestFileName = "fan-drag-trace-latest.jsonl";
    public const string HistoryFileName = "fan-drag-trace-history.jsonl";

    private static int s_nextSessionId;

    private readonly Func<string> _directoryProvider;
    private readonly Action<string>? _log;
    private readonly List<string> _lines = [];

    private Point? _lastMovementPoint;
    private int _sessionId;
    private int _nextFrameIndex;
    private int _keyCount;

    public FanDragInstrumentation(Func<string>? directoryProvider = null, Action<string>? log = null)
    {
        _directoryProvider = directoryProvider ?? DefaultTraceDirectory;
        _log = log;
    }

    public bool IsActive { get; private set; }

    public string? LatestTracePath { get; private set; }

    public IReadOnlyList<string> Lines => _lines;

    public static string DefaultTraceDirectory()
    {
        string directory = Path.Combine(Program.AppLocalAppDataDirectory, "drag-traces");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public void Begin(FanDragInstrumentationStart start)
    {
        if (IsActive) End("restart");

        _lines.Clear();
        _lastMovementPoint = null;
        _sessionId = Interlocked.Increment(ref s_nextSessionId);
        _nextFrameIndex = 0;
        _keyCount = 0;
        IsActive = true;
        _lines.Add(WriteBegin(start));
    }

    public void RecordMovement(
        Point current,
        Func<Point, bool, FanDragInstrumentationCapture> captureFactory)
    {
        if (!IsActive) return;

        if (_lastMovementPoint is { } previous)
        {
            int previousY = (int)Math.Round(previous.Y, MidpointRounding.AwayFromZero);
            int currentY = (int)Math.Round(current.Y, MidpointRounding.AwayFromZero);
            int step = currentY > previousY ? 1 : -1;

            for (int y = previousY + step; previousY != currentY && y != currentY; y += step)
            {
                double ratio = Math.Abs(current.Y - previous.Y) < 0.001
                    ? 1.0
                    : (y - previous.Y) / (current.Y - previous.Y);
                double x = previous.X + (current.X - previous.X) * ratio;
                AddFrame(captureFactory(new Point(x, y), true));
            }
        }

        AddFrame(captureFactory(current, false));
        _lastMovementPoint = current;
    }

    public bool RecordAnnotation(
        Key key,
        Point current,
        Func<Point, FanDragInstrumentationCapture> captureFactory)
    {
        if (!IsActive || !TryResolveExpectation(key, out FanDragExpectedIndexing expectation)) return false;

        FanDragInstrumentationCapture capture = captureFactory(current);
        FanDragInstrumentationFrame frame = AddFrame(capture);
        _lines.Add(WriteKey(frame.Index, key, expectation, capture.Evaluation));
        _keyCount++;
        Flush(appendHistory: false);
        return true;
    }

    public void End(string reason)
    {
        if (!IsActive) return;

        IsActive = false;
        _lines.Add(WriteEnd(reason));
        Flush(appendHistory: true);
    }

    public static bool TryResolveExpectation(Key key, out FanDragExpectedIndexing expectation)
    {
        switch (key)
        {
            case Key.A:
                expectation = FanDragExpectedIndexing.IndexAbove;
                return true;
            case Key.B:
                expectation = FanDragExpectedIndexing.IndexBelow;
                return true;
            case Key.I:
                expectation = FanDragExpectedIndexing.IndexIntoGroup;
                return true;
            case Key.O:
                expectation = FanDragExpectedIndexing.IndexOutOfGroup;
                return true;
            case Key.X:
                expectation = FanDragExpectedIndexing.NoIndexChange;
                return true;
            default:
                expectation = FanDragExpectedIndexing.NoIndexChange;
                return false;
        }
    }

    private FanDragInstrumentationFrame AddFrame(FanDragInstrumentationCapture capture)
    {
        FanDragInstrumentationFrame frame = new(_nextFrameIndex++, capture);
        _lines.Add(WriteFrame(frame));
        return frame;
    }

    private void Flush(bool appendHistory)
    {
        if (_lines.Count == 0) return;

        try
        {
            string directory = _directoryProvider();
            Directory.CreateDirectory(directory);

            LatestTracePath = Path.Combine(directory, LatestFileName);
            File.WriteAllLines(LatestTracePath, _lines, Encoding.UTF8);
            if (appendHistory)
                File.AppendAllLines(Path.Combine(directory, HistoryFileName), _lines, Encoding.UTF8);
            _log?.Invoke($"Fan drag instrumentation wrote {LatestTracePath}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Fan drag instrumentation flush failed: {ex.Message}");
        }
    }

    private string WriteBegin(FanDragInstrumentationStart start)
    {
        StringBuilder builder = new();
        builder.Append("{\"type\":\"begin\",\"session\":");
        builder.Append(_sessionId.ToString(CultureInfo.InvariantCulture));
        AppendName(builder, "sourceKind", start.SourceKind);
        AppendName(builder, "sourceName", start.SourceName);
        AppendName(builder, "sourceCell", start.SourceCell);
        builder.Append(",\"sourceTopLevelIndex\":");
        builder.Append(start.SourceTopLevelIndex.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"sourceSlotHeight\":");
        AppendNumber(builder, start.SourceSlotHeight);
        builder.Append(",\"sourceFanSlotHeight\":");
        AppendNumber(builder, start.SourceFanSlotHeight);
        builder.Append('}');
        return builder.ToString();
    }

    private string WriteFrame(FanDragInstrumentationFrame frame)
    {
        FanDragInstrumentationCapture capture = frame.Capture;
        StringBuilder builder = new();
        builder.Append("{\"type\":\"frame\",\"session\":");
        builder.Append(_sessionId.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"frame\":");
        builder.Append(frame.Index.ToString(CultureInfo.InvariantCulture));
        AppendName(builder, "stage", capture.Stage);
        builder.Append(",\"interpolated\":");
        builder.Append(capture.Interpolated ? "true" : "false");
        builder.Append(",\"pointer\":{\"x\":");
        AppendNumber(builder, capture.Pointer.X);
        builder.Append(",\"y\":");
        AppendNumber(builder, capture.Pointer.Y);
        builder.Append('}');
        AppendBounds(builder, capture.Evaluation.Bounds);
        AppendPlacement(builder, "placement", capture.Evaluation.Placement);
        AppendPreview(builder, capture.Evaluation.Preview);
        AppendPlacement(builder, "activePlacement", capture.ActivePlacement);
        AppendName(builder, "ghostStyle", capture.GhostStyle.ToString());
        AppendSnapshot(builder, capture.Evaluation.Snapshot);
        AppendSlots(builder, capture.Slots);
        AppendFanSlots(builder, capture.FanSlots);
        AppendDebugMarkers(builder, capture.DebugMarkers);
        AppendGhost(builder, capture.Ghost);
        AppendGroupPreview(builder, capture.GroupPreview);
        builder.Append('}');
        return builder.ToString();
    }

    private string WriteKey(
        int frameIndex,
        Key key,
        FanDragExpectedIndexing expectation,
        FanDragEvaluation evaluation)
    {
        StringBuilder builder = new();
        builder.Append("{\"type\":\"key\",\"session\":");
        builder.Append(_sessionId.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"frame\":");
        builder.Append(frameIndex.ToString(CultureInfo.InvariantCulture));
        AppendName(builder, "key", KeyLabel(key));
        AppendName(builder, "expectation", expectation.ToString());
        AppendPlacement(builder, "placement", evaluation.Placement);
        AppendPreview(builder, evaluation.Preview);
        builder.Append('}');
        return builder.ToString();
    }

    private string WriteEnd(string reason)
    {
        StringBuilder builder = new();
        builder.Append("{\"type\":\"end\",\"session\":");
        builder.Append(_sessionId.ToString(CultureInfo.InvariantCulture));
        AppendName(builder, "reason", reason);
        builder.Append(",\"frames\":");
        builder.Append(_nextFrameIndex.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"keys\":");
        builder.Append(_keyCount.ToString(CultureInfo.InvariantCulture));
        builder.Append('}');
        return builder.ToString();
    }

    private static string KeyLabel(Key key) => key switch
    {
        Key.A => "a",
        Key.B => "b",
        Key.I => "i",
        Key.O => "o",
        Key.X => "x",
        _ => key.ToString(),
    };

    private static void AppendSnapshot(StringBuilder builder, FanDragSnapshot snapshot)
    {
        builder.Append(",\"snapshot\":{\"draggedFan\":");
        AppendString(builder, snapshot.DraggedFan?.DisplayName);
        builder.Append(",\"dragSourceCell\":");
        AppendString(builder, CellLabel(snapshot.DragSourceCell));
        builder.Append(",\"dragSourceTopLevelIndex\":");
        builder.Append(snapshot.DragSourceTopLevelIndex.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"dragSourceSlotHeight\":");
        AppendNumber(builder, snapshot.DragSourceSlotHeight);
        builder.Append(",\"dragSourceFanSlotHeight\":");
        AppendNumber(builder, snapshot.DragSourceFanSlotHeight);
        builder.Append(",\"dragPlacementSourceHeight\":");
        AppendNumber(builder, snapshot.DragPlacementSourceHeight);
        builder.Append(",\"dragPointerOffsetRatio\":");
        AppendNumber(builder, snapshot.DragPointerOffsetRatio);
        builder.Append('}');
    }

    private static void AppendBounds(StringBuilder builder, FanDragBounds bounds)
    {
        builder.Append(",\"bounds\":{\"top\":");
        AppendNumber(builder, bounds.Top);
        builder.Append(",\"bottom\":");
        AppendNumber(builder, bounds.Bottom);
        builder.Append(",\"height\":");
        AppendNumber(builder, bounds.Height);
        builder.Append(",\"midpoint\":");
        AppendNumber(builder, bounds.Midpoint);
        builder.Append(",\"pointerY\":");
        AppendNumber(builder, bounds.PointerY);
        builder.Append(",\"movingDown\":");
        builder.Append(bounds.MovingDown ? "true" : "false");
        builder.Append('}');
    }

    private static void AppendPlacement(StringBuilder builder, string property, FanDragPlacement placement)
    {
        builder.Append(',');
        AppendString(builder, property);
        builder.Append(":{\"kind\":");
        AppendString(builder, placement.Kind.ToString());
        builder.Append(",\"topLevelIndex\":");
        builder.Append(placement.TopLevelIndex.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"group\":");
        AppendString(builder, CellLabel(placement.GroupCell));
        builder.Append(",\"groupFanIndex\":");
        builder.Append(placement.GroupFanIndex.ToString(CultureInfo.InvariantCulture));
        builder.Append('}');
    }

    private static void AppendPreview(StringBuilder builder, FanDragPreviewPlan preview)
    {
        builder.Append(",\"preview\":{\"topLevelOffsets\":[");
        for (int i = 0; i < preview.TopLevelOffsets.Count; i++)
        {
            if (i > 0) builder.Append(',');
            FanDragSlotOffset offset = preview.TopLevelOffsets[i];
            builder.Append("{\"index\":");
            builder.Append(offset.Index.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"offset\":");
            AppendNumber(builder, offset.Offset);
            builder.Append('}');
        }

        builder.Append("],\"groupDropPreviewCell\":");
        AppendString(builder, CellLabel(preview.GroupDropPreviewCell));
        builder.Append(",\"groupDropPreviewFanIndex\":");
        builder.Append(preview.GroupDropPreviewFanIndex.ToString(CultureInfo.InvariantCulture));
        builder.Append('}');
    }

    private static void AppendSlots(StringBuilder builder, IReadOnlyList<FanDragInstrumentationSlot> slots)
    {
        builder.Append(",\"slots\":[");
        for (int i = 0; i < slots.Count; i++)
        {
            if (i > 0) builder.Append(',');
            FanDragInstrumentationSlot slot = slots[i];
            builder.Append("{\"index\":");
            builder.Append(slot.Index.ToString(CultureInfo.InvariantCulture));
            AppendName(builder, "kind", slot.Kind);
            AppendName(builder, "name", slot.Name);
            builder.Append(",\"top\":");
            AppendNumber(builder, slot.Top);
            builder.Append(",\"visualTop\":");
            AppendNumber(builder, slot.VisualTop);
            builder.Append(",\"height\":");
            AppendNumber(builder, slot.Height);
            builder.Append(",\"slotHeight\":");
            AppendNumber(builder, slot.SlotHeight);
            builder.Append(",\"renderOffsetY\":");
            AppendNumber(builder, slot.RenderOffsetY);
            builder.Append(",\"groupInsertionTop\":");
            AppendNumber(builder, slot.GroupInsertionTop);
            builder.Append(",\"groupDropBottom\":");
            AppendNumber(builder, slot.GroupDropBottom);
            builder.Append(",\"isDragSource\":");
            builder.Append(slot.IsDragSource ? "true" : "false");
            builder.Append('}');
        }

        builder.Append(']');
    }

    private static void AppendFanSlots(StringBuilder builder, IReadOnlyList<FanDragInstrumentationFanSlot> slots)
    {
        builder.Append(",\"fanSlots\":[");
        for (int i = 0; i < slots.Count; i++)
        {
            if (i > 0) builder.Append(',');
            FanDragInstrumentationFanSlot slot = slots[i];
            builder.Append("{\"group\":");
            AppendString(builder, slot.GroupName);
            builder.Append(",\"fan\":");
            AppendString(builder, slot.FanName);
            builder.Append(",\"fanIndex\":");
            builder.Append(slot.FanIndex.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"top\":");
            AppendNumber(builder, slot.Top);
            builder.Append(",\"visualTop\":");
            AppendNumber(builder, slot.VisualTop);
            builder.Append(",\"height\":");
            AppendNumber(builder, slot.Height);
            builder.Append(",\"renderOffsetY\":");
            AppendNumber(builder, slot.RenderOffsetY);
            builder.Append(",\"isDraggedFan\":");
            builder.Append(slot.IsDraggedFan ? "true" : "false");
            builder.Append('}');
        }

        builder.Append(']');
    }

    private static void AppendDebugMarkers(StringBuilder builder, IReadOnlyList<FanDragInstrumentationDebugMarker> markers)
    {
        builder.Append(",\"debugMarkers\":[");
        for (int i = 0; i < markers.Count; i++)
        {
            if (i > 0) builder.Append(',');
            FanDragInstrumentationDebugMarker marker = markers[i];
            builder.Append("{\"y\":");
            AppendNumber(builder, marker.Y);
            AppendPlacement(builder, "placement", marker.Placement);
            builder.Append('}');
        }

        builder.Append(']');
    }

    private static void AppendGhost(StringBuilder builder, FanDragInstrumentationGhost? ghost)
    {
        builder.Append(",\"ghost\":");
        if (ghost == null)
        {
            builder.Append("null");
            return;
        }

        builder.Append("{\"left\":");
        AppendNumber(builder, ghost.Left);
        builder.Append(",\"top\":");
        AppendNumber(builder, ghost.Top);
        builder.Append(",\"width\":");
        AppendNumber(builder, ghost.Width);
        builder.Append(",\"height\":");
        AppendNumber(builder, ghost.Height);
        builder.Append(",\"opacity\":");
        AppendNumber(builder, ghost.Opacity);
        builder.Append('}');
    }

    private static void AppendGroupPreview(StringBuilder builder, FanDragInstrumentationGroupPreview? preview)
    {
        builder.Append(",\"groupDropPreview\":");
        if (preview == null)
        {
            builder.Append("null");
            return;
        }

        builder.Append("{\"group\":");
        AppendString(builder, preview.GroupName);
        builder.Append(",\"childIndex\":");
        builder.Append(preview.ChildIndex.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"top\":");
        AppendNumber(builder, preview.Top);
        builder.Append(",\"height\":");
        AppendNumber(builder, preview.Height);
        builder.Append(",\"extent\":");
        AppendNumber(builder, preview.Extent);
        builder.Append('}');
    }

    private static void AppendName(StringBuilder builder, string property, string? value)
    {
        builder.Append(',');
        AppendString(builder, property);
        builder.Append(':');
        AppendString(builder, value);
    }

    private static void AppendNumber(StringBuilder builder, double value)
    {
        if (double.IsFinite(value))
            builder.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
        else
            builder.Append('0');
    }

    private static void AppendString(StringBuilder builder, string? value)
    {
        if (value == null)
        {
            builder.Append("null");
            return;
        }

        builder.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(c))
                    {
                        builder.Append("\\u");
                        builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                        builder.Append(c);

                    break;
            }
        }

        builder.Append('"');
    }

    private static string? CellLabel(FanFlyoutCell? cell)
    {
        if (cell == null) return null;
        if (cell.HasGroupHeader) return cell.GroupName;
        return cell.Fans.Count == 1 ? cell.Fans[0].DisplayName : "<empty>";
    }
}

internal enum FanDragExpectedIndexing
{
    IndexAbove,
    IndexBelow,
    IndexIntoGroup,
    IndexOutOfGroup,
    NoIndexChange,
}

internal sealed record FanDragInstrumentationStart(
    string SourceKind,
    string SourceName,
    string? SourceCell,
    int SourceTopLevelIndex,
    double SourceSlotHeight,
    double SourceFanSlotHeight);

internal sealed record FanDragInstrumentationFrame(
    int Index,
    FanDragInstrumentationCapture Capture);

internal sealed record FanDragInstrumentationCapture(
    Point Pointer,
    string Stage,
    bool Interpolated,
    FanDragEvaluation Evaluation,
    FanDragPlacement ActivePlacement,
    FanDragGhostStyle GhostStyle,
    IReadOnlyList<FanDragInstrumentationSlot> Slots,
    IReadOnlyList<FanDragInstrumentationFanSlot> FanSlots,
    IReadOnlyList<FanDragInstrumentationDebugMarker> DebugMarkers,
    FanDragInstrumentationGhost? Ghost,
    FanDragInstrumentationGroupPreview? GroupPreview);

internal sealed record FanDragInstrumentationSlot(
    int Index,
    string Kind,
    string Name,
    double Top,
    double VisualTop,
    double Height,
    double SlotHeight,
    double RenderOffsetY,
    double GroupInsertionTop,
    double GroupDropBottom,
    bool IsDragSource);

internal sealed record FanDragInstrumentationFanSlot(
    string? GroupName,
    string FanName,
    int FanIndex,
    double Top,
    double VisualTop,
    double Height,
    double RenderOffsetY,
    bool IsDraggedFan);

internal sealed record FanDragInstrumentationDebugMarker(
    double Y,
    FanDragPlacement Placement);

internal sealed record FanDragInstrumentationGhost(
    double Left,
    double Top,
    double Width,
    double Height,
    double Opacity);

internal sealed record FanDragInstrumentationGroupPreview(
    string? GroupName,
    int ChildIndex,
    double Top,
    double Height,
    double Extent);
