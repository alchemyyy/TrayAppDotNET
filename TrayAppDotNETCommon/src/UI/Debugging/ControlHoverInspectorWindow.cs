#if DEBUG
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace TrayAppDotNETCommon.UI.Debugging;

/// <summary>Displays the current debug hover target without activating or owning the inspected window.</summary>
internal sealed class ControlHoverInspectorWindow : Window
{
    private const double InspectorWidth = 640;
    private const double InspectorHeight = 720;
    private const double MinimumInspectorWidth = 420;
    private const double MinimumInspectorHeight = 360;
    private const double TreeFontSize = 11;
    private const double TreeRowHeight = 17;
    private const int WorkAreaMarginPixels = 12;
    private const int FallbackWorkAreaWidthPixels = 1920;
    private const int FallbackWorkAreaHeightPixels = 1080;

    private static readonly Color BackgroundColor = Color.FromRgb(28, 28, 30);
    private static readonly Color BorderColor = Color.FromRgb(92, 92, 96);
    private static readonly Color ForegroundColor = Color.FromRgb(235, 235, 240);
    private static readonly Color SecondaryForegroundColor = Color.FromRgb(185, 185, 192);
    private static readonly Color LiveColor = Color.FromRgb(105, 210, 140);
    private static readonly Color FrozenColor = Color.FromRgb(255, 194, 92);
    private static readonly Color HeaderBackgroundColor = Color.FromRgb(38, 38, 42);

    private readonly ControlNameScope _controlNames;
    private readonly TextBlock _statusText;
    private readonly TextBlock _targetText;
    private readonly TreeView _treeView;

    internal string? StatusText => _statusText.Text;

    public ControlHoverInspectorWindow()
    {
        _controlNames = ControlNameScope.For(this);
        Title = "Avalonia Hover Inspector";
        Width = InspectorWidth;
        Height = InspectorHeight;
        MinWidth = MinimumInspectorWidth;
        MinHeight = MinimumInspectorHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowDecorations = WindowDecorations.Full;
        ShowActivated = false;
        ShowInTaskbar = false;
        Focusable = true;
        Topmost = true;
        CanResize = true;
        Background = new SolidColorBrush(BackgroundColor);
        Opacity = 0.97;

        _statusText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            FontWeight = FontWeight.Normal
        };

        _targetText = new TextBlock
        {
            Foreground = new SolidColorBrush(SecondaryForegroundColor),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        StackPanel header = new()
        {
            Spacing = 3,
            Children =
            {
                _statusText,
                _targetText
            }
        };

        Border headerBorder = new()
        {
            Background = new SolidColorBrush(HeaderBackgroundColor),
            BorderBrush = new SolidColorBrush(BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 9),
            Child = header
        };
        DockPanel.SetDock(headerBorder, Dock.Top);

        _treeView = new TreeView
        {
            Background = new SolidColorBrush(BackgroundColor),
            Foreground = new SolidColorBrush(ForegroundColor),
            FontFamily = new FontFamily("Consolas"),
            FontSize = TreeFontSize,
            FontWeight = FontWeight.Normal,
            Margin = new Thickness(7, 5, 7, 7)
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_treeView, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_treeView, ScrollBarVisibility.Auto);

        DockPanel contentPanel = new()
        {
            LastChildFill = true,
            Children =
            {
                headerBorder,
                _treeView
            }
        };

        Border root = new()
        {
            Background = new SolidColorBrush(BackgroundColor),
            BorderBrush = new SolidColorBrush(BorderColor),
            BorderThickness = new Thickness(1),
            Child = contentPanel
        };
        _controlNames.AssignLogicalSubtree(root, this);
        Content = root;

        SetFrozen(false);
        ShowNoControl();

        Opened += OnOpened;
    }

    public void ShowSnapshot(ControlHoverInspectorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _targetText.Text = snapshot.TargetLabel;
        _treeView.Items.Clear();
        foreach (ControlHoverInspectorNode root in snapshot.Roots)
        {
            TreeViewItem rootItem = BuildTreeItem(root);
            _controlNames.AssignLogicalSubtree(rootItem, _treeView);
            _treeView.Items.Add(rootItem);
        }
    }

    public void ShowNoControl()
    {
        _targetText.Text = "No control is currently under the pointer";
        _treeView.Items.Clear();
        TreeViewItem instruction = new()
        {
            Header = "Move the pointer over an Avalonia window to capture a control"
        };
        _controlNames.Assign(instruction, _treeView);
        _treeView.Items.Add(instruction);
    }

    public void SetFrozen(bool isFrozen)
    {
        string state = isFrozen ? "FROZEN" : "LIVE";
        _statusText.Text = $"{state} | {ControlHoverInspectorShortcut.Hint}";
        _statusText.Foreground = new SolidColorBrush(isFrozen ? FrozenColor : LiveColor);
        Title = isFrozen ? "Avalonia Hover Inspector [FROZEN]" : "Avalonia Hover Inspector";
    }

    private static TreeViewItem BuildTreeItem(ControlHoverInspectorNode node)
    {
        TreeViewItem rootItem = CreateTreeItem(node);
        Stack<(ControlHoverInspectorNode Node, TreeViewItem Item)> pending = [];
        pending.Push((node, rootItem));

        while (pending.Count > 0)
        {
            (ControlHoverInspectorNode currentNode, TreeViewItem currentItem) = pending.Pop();
            List<(ControlHoverInspectorNode Node, TreeViewItem Item)> children = [];
            foreach (ControlHoverInspectorNode childNode in currentNode.Children)
            {
                TreeViewItem childItem = CreateTreeItem(childNode);
                currentItem.Items.Add(childItem);
                children.Add((childNode, childItem));
            }

            for (int index = children.Count - 1; index >= 0; index--)
                pending.Push(children[index]);
        }

        return rootItem;
    }

    private static TreeViewItem CreateTreeItem(ControlHoverInspectorNode node) => new()
    {
        Header = node.Text,
        IsExpanded = node.IsExpanded,
        FontSize = TreeFontSize,
        FontWeight = FontWeight.Normal,
        Margin = new Thickness(0),
        MinHeight = TreeRowHeight,
        Padding = new Thickness(0)
    };

    private void OnOpened(object? sender, EventArgs eventArgs)
    {
        PixelRect workArea = Screens.Primary?.WorkingArea
                             ?? new PixelRect(0, 0, FallbackWorkAreaWidthPixels, FallbackWorkAreaHeightPixels);
        double renderScaling = Math.Max(RenderScaling, 1);
        int inspectorWidthPixels = (int)Math.Ceiling(InspectorWidth * renderScaling);
        int inspectorHeightPixels = (int)Math.Ceiling(InspectorHeight * renderScaling);
        int horizontalPosition = Math.Max(
            workArea.X + WorkAreaMarginPixels,
            workArea.Right - inspectorWidthPixels - WorkAreaMarginPixels);
        int verticalPosition = Math.Max(
            workArea.Y + WorkAreaMarginPixels,
            workArea.Bottom - inspectorHeightPixels - WorkAreaMarginPixels);
        Position = new PixelPoint(horizontalPosition, verticalPosition);
    }
}
#endif
