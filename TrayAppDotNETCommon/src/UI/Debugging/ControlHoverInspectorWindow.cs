#if DEBUG
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
    private readonly SolidColorBrush _backgroundBrush;
    private readonly SolidColorBrush _borderBrush;
    private readonly SolidColorBrush _foregroundBrush;
    private readonly SolidColorBrush _secondaryForegroundBrush;
    private readonly SolidColorBrush _statusBrush;
    private readonly SolidColorBrush _headerBackgroundBrush;
    private bool _isFrozen;

    internal string? StatusText => _statusText.Text;

    public ControlHoverInspectorWindow()
    {
        _backgroundBrush = new(ResolveColor(BackgroundResourceName));
        _borderBrush = new(ResolveColor(BorderResourceName));
        _foregroundBrush = new(ResolveColor(ForegroundResourceName));
        _secondaryForegroundBrush = new(ResolveColor(SecondaryForegroundResourceName));
        _statusBrush = new(ResolveColor(LiveResourceName));
        _headerBackgroundBrush = new(ResolveColor(HeaderBackgroundResourceName));

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
            Background = _headerBackgroundBrush,
            BorderBrush = _borderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 9),
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
            Margin = new Thickness(7, 5, 7, 7),
            ItemTemplate = new FuncTreeDataTemplate<ControlHoverInspectorNode>(
                static (node, _) => new ControlHoverInspectorTreeRow(node),
                static node => node.Children)
        };
        Style treeItemStyle = new(selector => selector.OfType<TreeViewItem>());
        treeItemStyle.Setters.Add(new Setter(TreeViewItem.FontSizeProperty, TreeFontSize));
        treeItemStyle.Setters.Add(new Setter(TreeViewItem.FontWeightProperty, FontWeight.Normal));
        treeItemStyle.Setters.Add(new Setter(TreeViewItem.MarginProperty, new Thickness(0)));
        treeItemStyle.Setters.Add(new Setter(TreeViewItem.MinHeightProperty, TreeRowHeight));
        treeItemStyle.Setters.Add(new Setter(TreeViewItem.PaddingProperty, new Thickness(0)));
        _treeView.Styles.Add(treeItemStyle);
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
        _treeView.ItemsSource = snapshot.Roots;
    }

    public void ShowNoControl()
    {
        _targetText.Text = "No control is currently under the pointer";
        _treeView.ItemsSource = new ControlHoverInspectorNode[]
        {
            new("Move the pointer over an Avalonia window to capture a control")
        };
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

    protected override void OnClosed(EventArgs eventArgs)
    {
        Opened -= OnOpened;
        AppThemeHotReload.ResourcesReloaded -= OnAppThemeResourcesReloaded;
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
