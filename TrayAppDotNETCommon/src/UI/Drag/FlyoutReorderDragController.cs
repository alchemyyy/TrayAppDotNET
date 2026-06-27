using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Controls;

namespace TrayAppDotNETCommon.UI.Drag;

public readonly record struct FlyoutDragTarget<TItem>(
    TItem Item,
    Control Root,
    Control Content,
    Border? SlotCover,
    double Left,
    double Top,
    double Height,
    string? GroupKey = null);

public sealed class FlyoutReorderDragController<TItem>
    where TItem : class
{
    private readonly Control _coordinateRoot;
    private readonly Canvas _overlay;
    private readonly Func<TItem, Control> _createGhostContent;
    private Border? _ghost;
    private Border? _activeSlotCover;
    private TItem? _draggedItem;
    private double _pointerOffsetY;

    public FlyoutReorderDragController(
        Control coordinateRoot,
        Canvas overlay,
        Func<TItem, Control> createGhostContent)
    {
        _coordinateRoot = coordinateRoot;
        _overlay = overlay;
        _createGhostContent = createGhostContent;
        _overlay.IsHitTestVisible = false;
    }

    public bool IsDragging => _draggedItem != null;

    public TItem? DraggedItem => _draggedItem;

    public void Begin(
        TItem item,
        FlyoutDragTarget<TItem> source,
        double pointerOffsetY,
        Color shadowColor,
        double opacity = 0.84)
    {
        End();

        _draggedItem = item;
        _pointerOffsetY = pointerOffsetY;

        Control content = _createGhostContent(item);
        content.Width = Math.Max(1, source.Content.Bounds.Width);
        content.Height = Math.Max(1, source.Content.Bounds.Height);
        content.IsHitTestVisible = false;

        _ghost = new Border
        {
            Width = Math.Max(1, source.Content.Bounds.Width),
            Height = Math.Max(1, source.Content.Bounds.Height),
            Opacity = opacity,
            IsHitTestVisible = false,
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 2, Blur = 18, Color = shadowColor, }),
            Child = content,
        };

        Canvas.SetLeft(_ghost, source.Left);
        Canvas.SetTop(_ghost, source.Top);
        _overlay.Children.Add(_ghost);

        _activeSlotCover = source.SlotCover;
        if (_activeSlotCover != null)
            _activeSlotCover.IsVisible = true;
    }

    public void Move(Point pointerInRoot)
    {
        if (_ghost == null) return;
        Canvas.SetTop(_ghost, pointerInRoot.Y - _pointerOffsetY);
    }

    public void End()
    {
        if (_ghost != null)
        {
            _overlay.Children.Remove(_ghost);
            _ghost = null;
        }

        if (_activeSlotCover != null)
        {
            _activeSlotCover.IsVisible = false;
            _activeSlotCover = null;
        }

        _draggedItem = null;
    }

    public FlyoutDragTarget<TItem>[] SnapshotTargets(
        IEnumerable<(TItem Item, Control Root, Control Content, Border? SlotCover, string? GroupKey)> items)
    {
        List<FlyoutDragTarget<TItem>> targets = [];
        foreach ((TItem item, Control root, Control content, Border? slotCover, string? groupKey) in items)
        {
            Point topLeft = content.TranslatePoint(new Point(0, 0), _coordinateRoot) ?? new Point();
            targets.Add(new FlyoutDragTarget<TItem>(
                item,
                root,
                content,
                slotCover,
                topLeft.X,
                topLeft.Y,
                Math.Max(content.Bounds.Height, 1),
                groupKey));
        }

        return [.. targets];
    }

    public static int CalculateInsertionIndex(
        IReadOnlyList<FlyoutDragTarget<TItem>> targets,
        double pointerY,
        Predicate<FlyoutDragTarget<TItem>>? include = null)
    {
        int insertion = 0;
        int count = 0;
        foreach (FlyoutDragTarget<TItem> target in targets)
        {
            if (include != null && !include(target)) continue;
            count++;
            if (pointerY > target.Top + target.Height / 2.0) insertion++;
            else break;
        }

        return Math.Clamp(insertion, 0, count);
    }

    public static bool IsInteractiveDragSource(Visual? source) =>
        TrayAppDotNETFlyoutUI.IsInteractiveDragSource(source);
}
