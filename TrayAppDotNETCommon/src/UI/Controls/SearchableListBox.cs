using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TrayAppDotNETCommon.Utils;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Controls;

internal static class SearchableListBoxLayout
{
    private static SearchableListBoxResources AXAMLResources => SearchableListBoxResources.Current;

    public static double Width => AXAMLResources.AxamlSearchableListBox.Width;
    public static double ListHeight => AXAMLResources.AxamlSearchableListBox.ListHeight;
    public static double SearchBoxHeight => AXAMLResources.AxamlSearchableListBox.SearchBoxHeight;
    public static double SearchBoxFontSize => AXAMLResources.AxamlSearchableListBox.SearchBoxFontSize;

    public static Thickness SearchBoxBorderThickness =>
        AXAMLResources.AxamlSearchableListBox.SearchBoxBorderThickness;

    public static Thickness SearchBoxPadding => AXAMLResources.AxamlSearchableListBox.SearchBoxPadding;
    public static double ClearButtonWidth => AXAMLResources.AxamlSearchableListBox.ClearButtonWidth;
    public static double ClearButtonHeight => AXAMLResources.AxamlSearchableListBox.ClearButtonHeight;
    public static Thickness ClearButtonPadding => AXAMLResources.AxamlSearchableListBox.ClearButtonPadding;
    public static double ClearButtonFontSize => AXAMLResources.AxamlSearchableListBox.ClearButtonFontSize;
    public static double ItemFontSize => AXAMLResources.AxamlSearchableListBox.ItemFontSize;
    public static Thickness ItemPadding => AXAMLResources.AxamlSearchableListBox.ItemPadding;
    public static Thickness ItemMargin => AXAMLResources.AxamlSearchableListBox.ItemMargin;
    public static CornerRadius ItemCornerRadius => AXAMLResources.AxamlSearchableListBox.ItemCornerRadius;
    public static Thickness ScrollHostPadding => AXAMLResources.AxamlSearchableListBox.ScrollHostPadding;
    public static Thickness ListBorderThickness => AXAMLResources.AxamlSearchableListBox.ListBorderThickness;
    public static CornerRadius ListCornerRadius => AXAMLResources.AxamlSearchableListBox.ListCornerRadius;
    public static double EmptyOpacity => AXAMLResources.AxamlSearchableListBox.EmptyOpacity;
}

/// <summary>
/// Searchable settings list that commits selection by double-click or Enter.
/// </summary>
public sealed class SettingsSearchableListBox : Grid, IDisposable
{
    private const string DefaultPlaceholderText = "Search";
    private const string NoResultsText = "No results";

    private readonly SettingsPalette _palette;
    private readonly SettingsSearchableListBoxItemCollection _items;
    private readonly Grid _searchRow;
    private readonly TextBox _searchBox;
    private readonly SettingsButton _clearButton;
    private StackPanel _itemsPanel;
    private readonly SettingsScrollHost _scrollHost;
    private readonly Border _listBorder;
    private UIResourceScope _rowResources;
    private List<SettingsSearchableListBoxItem> _visibleItems = [];
    private SettingsSearchableListBoxItem? _selectedItem;
    private SettingsSearchableListBoxItem? _activeItem;
    private double _itemFontSize = SearchableListBoxLayout.ItemFontSize;
    private Thickness _itemPadding = SearchableListBoxLayout.ItemPadding;
    private Thickness _itemMargin = SearchableListBoxLayout.ItemMargin;
    private CornerRadius _itemCornerRadius = SearchableListBoxLayout.ItemCornerRadius;
    private Thickness _listContentMargin;
    private long _rowGenerationID;
    private int _disposed;

    public SettingsSearchableListBox(SettingsPalette palette)
    {
        _palette = palette;
        _items = new SettingsSearchableListBoxItemCollection(this);
        _rowResources = new UIResourceScope($"{nameof(SettingsSearchableListBox)}.Rows");
        Width = SearchableListBoxLayout.Width;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Focusable = true;
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Star));

        _searchBox = new TextBox
        {
            Height = SearchableListBoxLayout.SearchBoxHeight,
            FontFamily = TrayAppDotNETSettingsUI.UIFont,
            FontSize = SearchableListBoxLayout.SearchBoxFontSize,
            Background = TrayAppDotNETSettingsUI.Brush(palette.ControlBackground),
            Foreground = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            BorderBrush = Brushes.Transparent,
            BorderThickness = SearchableListBoxLayout.SearchBoxBorderThickness,
            Padding = SearchableListBoxLayout.SearchBoxPadding,
            VerticalContentAlignment = VerticalAlignment.Center,
            PlaceholderText = DefaultPlaceholderText,
            CaretBrush = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
            SelectionBrush = TrayAppDotNETSettingsUI.Brush(AppTheme.ResolveTextSelectionHighlight(palette.Accent)),
            SelectionForegroundBrush = TrayAppDotNETSettingsUI.Brush(palette.Foreground)
        };
        TrayAppDotNETSettingsUI.ApplyTextBoxResources(
            _searchBox,
            palette,
            TrayAppDotNETSettingsUI.Brush(palette.ControlBackground),
            TrayAppDotNETSettingsUI.Brush(palette.Hover),
            TrayAppDotNETSettingsUI.Brush(palette.TextBoxFocused));
        _searchBox.KeyDown += OnKeyboardNavigation;

        _clearButton = TrayAppDotNETSettingsUI.Button(GlyphCatalog.CHROME_CLOSE, palette);
        _clearButton.Width = SearchableListBoxLayout.ClearButtonWidth;
        _clearButton.Height = SearchableListBoxLayout.ClearButtonHeight;
        _clearButton.MinHeight = SearchableListBoxLayout.ClearButtonHeight;
        _clearButton.Padding = SearchableListBoxLayout.ClearButtonPadding;
        _clearButton.Label.FontFamily = TrayAppDotNETSettingsUI.IconFont;
        _clearButton.Label.FontSize = SearchableListBoxLayout.ClearButtonFontSize;
        _clearButton.Click += OnClearButtonClick;

        _searchRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        _searchRow.Children.Add(_searchBox);
        SetColumn(_clearButton, value: 1);
        _searchRow.Children.Add(_clearButton);
        Children.Add(_searchRow);

        _itemsPanel = new StackPanel();
        _scrollHost =
            TrayAppDotNETSettingsUI.ScrollHost(_itemsPanel, palette, SearchableListBoxLayout.ScrollHostPadding);
        _scrollHost.Height = SearchableListBoxLayout.ListHeight;
        _scrollHost.VerticalAlignment = VerticalAlignment.Stretch;

        _listBorder = new Border
        {
            Height = SearchableListBoxLayout.ListHeight,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = TrayAppDotNETSettingsUI.Brush(palette.ControlBackground),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            BorderThickness = SearchableListBoxLayout.ListBorderThickness,
            CornerRadius = SearchableListBoxLayout.ListCornerRadius,
            ClipToBounds = true,
            Child = _scrollHost
        };
        SetRow(_listBorder, value: 1);
        Children.Add(_listBorder);

        _searchBox.TextChanged += OnSearchTextChanged;
        KeyDown += OnKeyboardNavigation;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        UpdateClearButton();
        RebuildItems();
    }

    public event EventHandler? SelectionChanged;

    public SettingsSearchableListBoxItemCollection Items => _items;

    public SettingsSearchableListBoxItem? SelectedItem
    {
        get => _selectedItem;
        set => SetSelectedItem(value, raiseChanged: true);
    }

    public string SearchText
    {
        get => _searchBox.Text ?? string.Empty;
        set => _searchBox.Text = value;
    }

    public string PlaceholderText
    {
        get => _searchBox.PlaceholderText ?? string.Empty;
        set => _searchBox.PlaceholderText = value;
    }

    public double ListHeight
    {
        get => _listBorder.Height;
        set
        {
            if (double.IsNaN(value))
            {
                _listBorder.Height = double.NaN;
                _listBorder.VerticalAlignment = VerticalAlignment.Stretch;
                _scrollHost.Height = double.NaN;
                _scrollHost.VerticalAlignment = VerticalAlignment.Stretch;
                return;
            }

            double height = Math.Max(val1: 1, value);
            _listBorder.Height = height;
            _listBorder.VerticalAlignment = VerticalAlignment.Top;
            _scrollHost.Height = height;
            _scrollHost.VerticalAlignment = VerticalAlignment.Top;
        }
    }

    public double SearchBoxHeight
    {
        get => _searchBox.Height;
        set => _searchBox.Height = Math.Max(val1: 1, value);
    }

    public Thickness SearchBoxPadding
    {
        get => _searchBox.Padding;
        set => _searchBox.Padding = value;
    }

    public double ClearButtonWidth
    {
        get => _clearButton.Width;
        set => _clearButton.Width = Math.Max(val1: 1, value);
    }

    public double ClearButtonHeight
    {
        get => _clearButton.Height;
        set
        {
            double height = Math.Max(val1: 1, value);
            _clearButton.Height = height;
            _clearButton.MinHeight = height;
        }
    }

    public double ClearButtonFontSize
    {
        get => _clearButton.Label.FontSize;
        set => _clearButton.Label.FontSize = Math.Max(val1: 1, value);
    }

    public Thickness ClearButtonMargin
    {
        get => _clearButton.Margin;
        set => _clearButton.Margin = value;
    }

    public Thickness SearchRowMargin
    {
        get => _searchRow.Margin;
        set => _searchRow.Margin = value;
    }

    public Thickness ListBorderThickness
    {
        get => _listBorder.BorderThickness;
        set => _listBorder.BorderThickness = value;
    }

    public CornerRadius ListCornerRadius
    {
        get => _listBorder.CornerRadius;
        set => _listBorder.CornerRadius = value;
    }

    public Thickness ListContentMargin
    {
        get => _listContentMargin;
        set
        {
            _listContentMargin = value;
            _itemsPanel.Margin = value;
        }
    }

    public Thickness ItemPadding
    {
        get => _itemPadding;
        set
        {
            _itemPadding = value;
            RebuildItems();
        }
    }

    public Thickness ItemMargin
    {
        get => _itemMargin;
        set
        {
            _itemMargin = value;
            RebuildItems();
        }
    }

    public CornerRadius ItemCornerRadius
    {
        get => _itemCornerRadius;
        set
        {
            _itemCornerRadius = value;
            RebuildItems();
        }
    }

    public double ItemFontSize
    {
        get => _itemFontSize;
        set
        {
            _itemFontSize = Math.Max(val1: 1, value);
            RebuildItems();
        }
    }

    /// <summary>
    /// Clears the query and returns focus to the search box.
    /// </summary>
    public void ClearSearch()
    {
        _searchBox.Text = string.Empty;
        _searchBox.Focus();
    }

    /// <summary>
    /// Moves keyboard focus to the search box.
    /// </summary>
    public void FocusSearch() => _searchBox.Focus();

    /// <summary>
    /// Handles a newly added item.
    /// </summary>
    internal void OnItemAdded(SettingsSearchableListBoxItem item) => RebuildItems();

    /// <summary>
    /// Handles an item removal.
    /// </summary>
    internal bool OnItemRemoved(SettingsSearchableListBoxItem item)
    {
        bool selectionChanged = ReferenceEquals(_selectedItem, item);
        SettingsSearchableListBoxItem? candidateSelectedItem = selectionChanged ? null : _selectedItem;
        SettingsSearchableListBoxItem? candidateActiveItem = ReferenceEquals(_activeItem, item) ? null : _activeItem;
        RebuildItems(candidateSelectedItem, candidateActiveItem);
        return selectionChanged;
    }

    /// <summary>
    /// Handles an item replacement with one visual generation change.
    /// </summary>
    internal bool OnItemReplaced(SettingsSearchableListBoxItem oldItem)
    {
        bool selectionChanged = ReferenceEquals(_selectedItem, oldItem);
        SettingsSearchableListBoxItem? candidateSelectedItem = selectionChanged ? null : _selectedItem;
        SettingsSearchableListBoxItem? candidateActiveItem = ReferenceEquals(_activeItem, oldItem) ? null : _activeItem;
        RebuildItems(candidateSelectedItem, candidateActiveItem);
        return selectionChanged;
    }

    /// <summary>
    /// Handles collection reset with one visual generation change.
    /// </summary>
    internal bool OnItemsCleared(IReadOnlyList<SettingsSearchableListBoxItem> removedItems)
    {
        bool selectionChanged = _selectedItem != null && removedItems.Contains(_selectedItem);
        RebuildItems(selectedItem: null, activeItem: null);
        return selectionChanged;
    }

    internal void RaiseSelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Marks an item as active without committing selection.
    /// </summary>
    internal void ActivateItem(SettingsSearchableListBoxItem item)
    {
        RebuildItems(_selectedItem, item);
        Focus();
    }

    /// <summary>
    /// Commits an item as the selected item.
    /// </summary>
    internal void CommitItem(SettingsSearchableListBoxItem item)
    {
        SetSelectedItem(item, raiseChanged: true);
        _activeItem = item;
    }

    /// <summary>
    /// Rebuilds visible item rows from the current query.
    /// </summary>
    private void RebuildItems() => RebuildItems(_selectedItem, _activeItem);

    private void RebuildItems(
        SettingsSearchableListBoxItem? selectedItem,
        SettingsSearchableListBoxItem? activeItem)
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        List<SettingsSearchableListBoxItem> candidateVisibleItems = SearchMatcher.FilterAndRank(
            _items,
            _searchBox.Text,
            static item => item.SearchText);
        SettingsSearchableListBoxItem? candidateActiveItem = ActiveItemFor(
            candidateVisibleItems,
            selectedItem,
            activeItem);
        StackPanel candidatePanel = new() { Margin = _listContentMargin };
        UIResourceScope candidateResources = new($"{nameof(SettingsSearchableListBox)}.Rows");
        candidateResources.Add(candidatePanel.Children.Clear);

        try
        {
            if (candidateVisibleItems.Count == 0)
            {
                TextBlock empty = TrayAppDotNETSettingsUI.Text(NoResultsText, _palette, _itemFontSize);
                empty.Opacity = SearchableListBoxLayout.EmptyOpacity;
                Border emptyHost = new()
                {
                    Background = Brushes.Transparent, Padding = _itemPadding, Margin = _itemMargin, Child = empty
                };
                candidatePanel.Children.Add(emptyHost);
            }
            else
            {
                foreach (SettingsSearchableListBoxItem item in candidateVisibleItems)
                {
                    SettingsSearchableListBoxItemRow row = candidateResources.Own(new SettingsSearchableListBoxItemRow(
                        item,
                        this,
                        _palette,
                        _itemFontSize,
                        _itemPadding,
                        _itemMargin,
                        _itemCornerRadius));
                    row.IsSelected = ReferenceEquals(item, selectedItem);
                    row.IsActive = ReferenceEquals(item, candidateActiveItem);
                    candidatePanel.Children.Add(row);
                }
            }
        }
        catch
        {
            candidateResources.Dispose();
            throw;
        }

        UIResourceScope previousResources = _rowResources;
        try
        {
            _scrollHost.SetContent(candidatePanel);
            _itemsPanel = candidatePanel;
            _rowResources = candidateResources;
            _visibleItems = candidateVisibleItems;
            _selectedItem = selectedItem;
            _activeItem = candidateActiveItem;
            _rowGenerationID++;
        }
        catch
        {
            candidateResources.Dispose();
            throw;
        }

        previousResources.Dispose();
    }

    /// <summary>
    /// Keeps keyboard selection on a visible row.
    /// </summary>
    private static SettingsSearchableListBoxItem? ActiveItemFor(
        List<SettingsSearchableListBoxItem> visibleItems,
        SettingsSearchableListBoxItem? selectedItem,
        SettingsSearchableListBoxItem? activeItem)
    {
        if (visibleItems.Count == 0) return null;

        if (activeItem != null && visibleItems.Contains(activeItem)) return activeItem;
        if (selectedItem != null && visibleItems.Contains(selectedItem)) return selectedItem;

        return visibleItems[0];
    }

    /// <summary>
    /// Handles list and search keyboard selection.
    /// </summary>
    private void OnKeyboardNavigation(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveActiveItem(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveActiveItem(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                if (_activeItem != null)
                {
                    CommitItem(_activeItem);
                    e.Handled = true;
                }

                break;
        }
    }

    /// <summary>
    /// Moves the active row by a signed delta.
    /// </summary>
    private void MoveActiveItem(int delta)
    {
        if (_visibleItems.Count == 0) return;

        int index = _activeItem == null ? -1 : _visibleItems.IndexOf(_activeItem);
        if (index < 0)
            index = delta >= 0 ? -1 : 0;

        int nextIndex = Math.Clamp(index + delta, min: 0, _visibleItems.Count - 1);
        SettingsSearchableListBoxItem activeItem = _visibleItems[nextIndex];
        RebuildItems(_selectedItem, activeItem);
        QueueScrollActiveItemIntoView();
    }

    /// <summary>
    /// Sets selected item state and raises selection changes.
    /// </summary>
    private void SetSelectedItem(SettingsSearchableListBoxItem? item, bool raiseChanged)
    {
        if (ReferenceEquals(_selectedItem, item)) return;
        RebuildItems(item, item ?? _activeItem);
        QueueScrollActiveItemIntoView();
        if (raiseChanged)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Enables the clear button only when a query exists.
    /// </summary>
    private void UpdateClearButton() => _clearButton.IsEnabled = !string.IsNullOrEmpty(_searchBox.Text);

    /// <summary>
    /// Scrolls after layout has measured rebuilt rows.
    /// </summary>
    private void QueueScrollActiveItemIntoView()
    {
        long expectedGenerationID = _rowGenerationID;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                if (_rowGenerationID != expectedGenerationID) return;
                ScrollActiveItemIntoView();
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Scrolls the keyboard-active item into the visible list area.
    /// </summary>
    private void ScrollActiveItemIntoView()
    {
        if (_activeItem == null) return;

        int index = _visibleItems.IndexOf(_activeItem);
        if (index < 0 || index >= _itemsPanel.Children.Count) return;
        if (_itemsPanel.Children[index] is not { } row) return;

        Point? rowPoint = row.TranslatePoint(new Point(x: 0, y: 0), _itemsPanel);
        if (!rowPoint.HasValue) return;

        double viewportHeight = _scrollHost.ViewportHeight;
        if (viewportHeight <= 0) return;

        double rowTop = rowPoint.Value.Y;
        double rowBottom = rowTop + row.Bounds.Height;
        double visibleTop = _scrollHost.VerticalOffset;
        double visibleBottom = visibleTop + viewportHeight;

        if (rowTop < visibleTop)
        {
            _scrollHost.SetVerticalOffset(rowTop);
            return;
        }

        if (rowBottom <= visibleBottom) return;
        _scrollHost.SetVerticalOffset(rowBottom - viewportHeight);
    }

    /// <summary>Releases generated rows, queued work, and event handlers.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, value: 1) != 0) return;

        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        KeyDown -= OnKeyboardNavigation;
        _searchBox.KeyDown -= OnKeyboardNavigation;
        _searchBox.TextChanged -= OnSearchTextChanged;
        _clearButton.Click -= OnClearButtonClick;

        _rowResources.Dispose();
        _scrollHost.Dispose();
        _itemsPanel = new StackPanel();
        _items.ClearWithoutNotification();
        _visibleItems.Clear();
        _selectedItem = null;
        _activeItem = null;
        SelectionChanged = null;
    }

    private void OnClearButtonClick(object? sender, EventArgs e) => ClearSearch();

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        RebuildItems();
        _scrollHost.SetVerticalOffset(0);
        UpdateClearButton();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => _rowGenerationID++;
}

/// <summary>
/// Item model for a settings searchable list.
/// </summary>
public sealed class SettingsSearchableListBoxItem(
    object tag,
    string text,
    string searchText = "",
    Func<Control>? contentFactory = null)
{
    public object Tag { get; } = tag;

    public string Text { get; } = text;

    public string SearchText { get; } = string.IsNullOrWhiteSpace(searchText) ? text : searchText;

    /// <summary>
    /// Builds display content for this list item.
    /// </summary>
    public Control CreateContent(SettingsPalette palette, double fontSize)
    {
        if (contentFactory != null) return contentFactory();

        TextBlock label = TrayAppDotNETSettingsUI.Text(Text, palette, fontSize);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.TextWrapping = TextWrapping.NoWrap;
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }
}

/// <summary>
/// Item collection that notifies the owning searchable list.
/// </summary>
public sealed class SettingsSearchableListBoxItemCollection(SettingsSearchableListBox owner)
    : Collection<SettingsSearchableListBoxItem>
{
    /// <summary>
    /// Inserts a searchable-list item.
    /// </summary>
    protected override void InsertItem(int index, SettingsSearchableListBoxItem item)
    {
        base.InsertItem(index, item);
        try
        {
            owner.OnItemAdded(item);
        }
        catch
        {
            base.RemoveItem(index);
            throw;
        }
    }

    /// <summary>
    /// Replaces a searchable-list item.
    /// </summary>
    protected override void SetItem(int index, SettingsSearchableListBoxItem item)
    {
        SettingsSearchableListBoxItem old = this[index];
        base.SetItem(index, item);
        bool selectionChanged;
        try
        {
            selectionChanged = owner.OnItemReplaced(old);
        }
        catch
        {
            base.SetItem(index, old);
            throw;
        }

        if (selectionChanged)
            owner.RaiseSelectionChanged();
    }

    /// <summary>
    /// Removes a searchable-list item.
    /// </summary>
    protected override void RemoveItem(int index)
    {
        SettingsSearchableListBoxItem old = this[index];
        base.RemoveItem(index);
        bool selectionChanged;
        try
        {
            selectionChanged = owner.OnItemRemoved(old);
        }
        catch
        {
            base.InsertItem(index, old);
            throw;
        }

        if (selectionChanged)
            owner.RaiseSelectionChanged();
    }

    /// <summary>
    /// Clears all searchable-list items.
    /// </summary>
    protected override void ClearItems()
    {
        List<SettingsSearchableListBoxItem> removedItems = [.. this];
        base.ClearItems();
        bool selectionChanged;
        try
        {
            selectionChanged = owner.OnItemsCleared(removedItems);
        }
        catch
        {
            foreach (SettingsSearchableListBoxItem item in removedItems)
                base.InsertItem(Count, item);
            throw;
        }

        if (selectionChanged)
            owner.RaiseSelectionChanged();
    }

    internal void ClearWithoutNotification() => base.ClearItems();
}

/// <summary>
/// Visual row for one searchable-list item.
/// </summary>
internal sealed class SettingsSearchableListBoxItemRow : Border, IDisposable
{
    private readonly SettingsSearchableListBoxItem _item;
    private readonly SettingsSearchableListBox _owner;
    private readonly SettingsPalette _palette;
    private Control? _content;
    private bool _isPointerOver;
    private bool _isSelected;
    private bool _isActive;
    private int _disposed;

    public SettingsSearchableListBoxItemRow(
        SettingsSearchableListBoxItem item,
        SettingsSearchableListBox owner,
        SettingsPalette palette,
        double fontSize,
        Thickness padding,
        Thickness margin,
        CornerRadius cornerRadius)
    {
        _item = item;
        _owner = owner;
        _palette = palette;
        _content = item.CreateContent(palette, fontSize);
        _content.IsHitTestVisible = false;

        Background = Brushes.Transparent;
        Padding = padding;
        Margin = margin;
        CornerRadius = cornerRadius;
        Cursor = TrayAppDotNETCursors.Hand;
        Focusable = true;
        Child = _content;

        PointerEntered += OnPointerEntered;
        PointerExited += OnPointerExited;
        PointerPressed += OnPointerPressed;
        KeyDown += OnKeyDown;

        UpdateVisual();
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            UpdateVisual();
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            UpdateVisual();
        }
    }

    /// <summary>
    /// Applies selected, active, and hover visuals.
    /// </summary>
    private void UpdateVisual()
    {
        bool highlighted = _isSelected || _isActive || _isPointerOver;
        Color background = _isSelected
            ? _palette.SearchListItemSelected
            : _isActive || _isPointerOver
                ? _palette.SearchListItemHover
                : Colors.Transparent;
        Background = TrayAppDotNETSettingsUI.Brush(background);
        if (_content is TextBlock label)
        {
            label.Foreground = TrayAppDotNETSettingsUI.Brush(highlighted
                ? ContrastForeground(background)
                : _palette.Foreground);
        }
    }

    /// <summary>
    /// Picks readable text for user-configurable highlight colors.
    /// </summary>
    private static Color ContrastForeground(Color background)
    {
        double red = background.R / 255.0;
        double green = background.G / 255.0;
        double blue = background.B / 255.0;
        double luminance = 0.2126 * red + 0.7152 * green + 0.0722 * blue;
        return luminance > 0.55 ? Colors.Black : Colors.White;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, value: 1) != 0) return;

        PointerEntered -= OnPointerEntered;
        PointerExited -= OnPointerExited;
        PointerPressed -= OnPointerPressed;
        KeyDown -= OnKeyDown;
        Child = null;
        Control? content = Interlocked.Exchange(ref _content, value: null);
        if (content is IDisposable disposable)
            disposable.Dispose();
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        _isPointerOver = true;
        UpdateVisual();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _isPointerOver = false;
        UpdateVisual();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsEnabled) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount >= 2)
            _owner.CommitItem(_item);
        else
            _owner.ActivateItem(_item);

        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter) return;
        _owner.CommitItem(_item);
        e.Handled = true;
    }
}
