#if DEBUG
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.Visuals;

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

    private const string BackgroundResourceName = "DebugInspectorBackground";
    private const string BorderResourceName = "DebugInspectorBorder";
    private const string ForegroundResourceName = "DebugInspectorForeground";
    private const string SecondaryForegroundResourceName = "DebugInspectorSecondaryForeground";
    private const string LiveResourceName = "DebugInspectorLive";
    private const string FrozenResourceName = "DebugInspectorFrozen";
    private const string HeaderBackgroundResourceName = "DebugInspectorHeaderBackground";

    private readonly TextBlock _statusText;
    private readonly TextBlock _targetText;
    private readonly TreeView _treeView;
    private readonly ObservableCollection<ControlHoverInspectorNode> _treeRoots = [];
    private readonly SolidColorBrush _backgroundBrush;
    private readonly SolidColorBrush _borderBrush;
    private readonly SolidColorBrush _foregroundBrush;
    private readonly SolidColorBrush _secondaryForegroundBrush;
    private readonly SolidColorBrush _statusBrush;
    private readonly SolidColorBrush _headerBackgroundBrush;
    private bool _isFrozen;

    internal string? StatusText => _statusText.Text;

    internal object? RootItemsSource => _treeView.ItemsSource;

    internal int DisplayedRootCount => _treeRoots.Count;

    public ControlHoverInspectorWindow()
    {
        _backgroundBrush = new SolidColorBrush(ResolveColor(BackgroundResourceName));
        _borderBrush = new SolidColorBrush(ResolveColor(BorderResourceName));
        _foregroundBrush = new SolidColorBrush(ResolveColor(ForegroundResourceName));
        _secondaryForegroundBrush = new SolidColorBrush(ResolveColor(SecondaryForegroundResourceName));
        _statusBrush = new SolidColorBrush(ResolveColor(LiveResourceName));
        _headerBackgroundBrush = new SolidColorBrush(ResolveColor(HeaderBackgroundResourceName));

        ControlNameScope controlNames = ControlNameScope.For(this);
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
        Background = _backgroundBrush;
        Opacity = 0.97;

        _statusText = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            FontWeight = FontWeight.Normal,
            Foreground = _statusBrush
        };

        _targetText = new TextBlock
        {
            Foreground = _secondaryForegroundBrush,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        StackPanel header = new() { Spacing = 3, Children = { _statusText, _targetText } };

        Border headerBorder = new()
        {
            Background = _headerBackgroundBrush,
            BorderBrush = _borderBrush,
            BorderThickness = new Thickness(left: 0, top: 0, right: 0, bottom: 1),
            Padding = new Thickness(horizontal: 12, vertical: 9),
            Child = header
        };
        DockPanel.SetDock(headerBorder, Dock.Top);

        _treeView = new TreeView
        {
            Background = _backgroundBrush,
            Foreground = _foregroundBrush,
            FontFamily = new FontFamily("Consolas"),
            FontSize = TreeFontSize,
            FontWeight = FontWeight.Normal,
            Margin = new Thickness(left: 7, top: 5, right: 7, bottom: 7),
            ItemsSource = _treeRoots,
            ItemTemplate = new FuncTreeDataTemplate<ControlHoverInspectorNode>(
                static (node, _) => new ControlHoverInspectorTreeRow(node),
                static node => node.Children)
        };
        Style treeItemStyle = new(selector => selector.OfType<TreeViewItem>());
        treeItemStyle.Setters.Add(new Setter(FontSizeProperty, TreeFontSize));
        treeItemStyle.Setters.Add(new Setter(FontWeightProperty, FontWeight.Normal));
        treeItemStyle.Setters.Add(new Setter(MarginProperty, new Thickness(0)));
        treeItemStyle.Setters.Add(new Setter(MinHeightProperty, TreeRowHeight));
        treeItemStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
        _treeView.Styles.Add(treeItemStyle);
        ScrollViewer.SetHorizontalScrollBarVisibility(_treeView, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_treeView, ScrollBarVisibility.Auto);

        DockPanel contentPanel = new() { LastChildFill = true, Children = { headerBorder, _treeView } };

        Border root = new()
        {
            Background = _backgroundBrush,
            BorderBrush = _borderBrush,
            BorderThickness = new Thickness(1),
            Child = contentPanel
        };
        controlNames.AssignLogicalSubtree(root, this);
        Content = root;

        SetFrozen(false);
        ShowNoControl();

        AppThemeHotReload.ResourcesReloaded += OnAppThemeResourcesReloaded;
        Opened += OnOpened;
    }

    public void ShowSnapshot(ControlHoverInspectorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _targetText.Text = snapshot.TargetLabel;
        ReplaceRoots(snapshot.Roots);
    }

    public void ShowPendingCapture(ControlHoverInspectorCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        _targetText.Text = capture.TargetLabel;
        List<ControlHoverInspectorNode> roots =
        [
            capture.IdentityNode,
            new("Loading property and provenance data...")
        ];
        if (capture.AncestryNode != null)
            roots.Add(capture.AncestryNode);

        ReplaceRoots(roots);
    }

    public void ShowNoControl()
    {
        _targetText.Text = "No control is currently under the pointer";
        ReplaceRoots(
        [
            new ControlHoverInspectorNode("Move the pointer over an Avalonia window to capture a control")
        ]);
    }

    public void SetFrozen(bool isFrozen)
    {
        _isFrozen = isFrozen;
        string state = isFrozen ? "FROZEN" : "LIVE";
        _statusText.Text = $"{state} | {ControlHoverInspectorShortcut.Hint}";
        _statusBrush.Color = ResolveColor(isFrozen ? FrozenResourceName : LiveResourceName);
        Title = isFrozen ? "Avalonia Hover Inspector [FROZEN]" : "Avalonia Hover Inspector";
    }

    private static Color ResolveColor(string resourceName) => AppThemeColorCatalog.SingleColor(resourceName);

    private void ReplaceRoots(IReadOnlyList<ControlHoverInspectorNode> roots)
    {
        _treeRoots.Clear();
        foreach (ControlHoverInspectorNode root in roots)
            _treeRoots.Add(root);
    }

    private void OnAppThemeResourcesReloaded()
    {
        _backgroundBrush.Color = ResolveColor(BackgroundResourceName);
        _borderBrush.Color = ResolveColor(BorderResourceName);
        _foregroundBrush.Color = ResolveColor(ForegroundResourceName);
        _secondaryForegroundBrush.Color = ResolveColor(SecondaryForegroundResourceName);
        _headerBackgroundBrush.Color = ResolveColor(HeaderBackgroundResourceName);
        _statusBrush.Color = ResolveColor(_isFrozen ? FrozenResourceName : LiveResourceName);
    }

    private void OnOpened(object? sender, EventArgs eventArgs)
    {
        PixelRect workArea = Screens.Primary?.WorkingArea
                             ?? new PixelRect(x: 0, y: 0, FallbackWorkAreaWidthPixels, FallbackWorkAreaHeightPixels);
        double renderScaling = Math.Max(RenderScaling, val2: 1);
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

    protected override void OnClosed(EventArgs eventArgs)
    {
        Opened -= OnOpened;
        AppThemeHotReload.ResourcesReloaded -= OnAppThemeResourcesReloaded;
        _treeRoots.Clear();
        _treeView.ItemsSource = null;
        base.OnClosed(eventArgs);
    }

    private sealed class ControlHoverInspectorTreeRow : TextBlock
    {
        private readonly bool _isInitiallyExpanded;

        public ControlHoverInspectorTreeRow(ControlHoverInspectorNode node)
        {
            Text = node.Text;
            FontSize = TreeFontSize;
            FontWeight = FontWeight.Normal;
            Margin = new Thickness(0);
            _isInitiallyExpanded = node.IsExpanded;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
        {
            base.OnAttachedToVisualTree(eventArgs);

            for (Visual? visual = this; visual != null; visual = visual.GetVisualParent())
            {
                if (visual is not TreeViewItem treeViewItem) continue;

                treeViewItem.IsExpanded = _isInitiallyExpanded;
                break;
            }
        }
    }
}
#endif
