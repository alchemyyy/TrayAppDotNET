using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TrayAppDotNETCommon.Utils;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Controls;

internal static class SearchableListBoxLayout
{
    private static readonly ControlAXAMLResources R = new(
        "avares://TrayAppDotNETCommon/UI/Controls/SearchableListBox.axaml",
        "SearchableListBox");

    public static double Width => R.Double("Width");
    public static double ListHeight => R.Double("ListHeight");
    public static double SearchBoxHeight => R.Double("SearchBoxHeight");
    public static double SearchBoxFontSize => R.Double("SearchBoxFontSize");
    public static Thickness SearchBoxBorderThickness => R.Thickness("SearchBoxBorderThickness");
    public static Thickness SearchBoxPadding => R.Thickness("SearchBoxPadding");
    public static double ClearButtonWidth => R.Double("ClearButtonWidth");
    public static double ClearButtonHeight => R.Double("ClearButtonHeight");
    public static Thickness ClearButtonPadding => R.Thickness("ClearButtonPadding");
    public static double ClearButtonFontSize => R.Double("ClearButtonFontSize");
    public static double ItemFontSize => R.Double("ItemFontSize");
    public static Thickness ItemPadding => R.Thickness("ItemPadding");
    public static Thickness ItemMargin => R.Thickness("ItemMargin");
    public static CornerRadius ItemRadius => R.CornerRadius("ItemRadius");
    public static Thickness ScrollHostPadding => R.Thickness("ScrollHostPadding");
    public static Thickness ListBorderThickness => R.Thickness("ListBorderThickness");
    public static CornerRadius ListRadius => R.CornerRadius("ListRadius");
    public static double EmptyOpacity => R.Double("EmptyOpacity");
}

/// <summary>
/// Searchable settings list that commits selection by double-click or Enter.
/// </summary>
public sealed class SettingsSearchableListBox : Grid
{
    private const string DefaultPlaceholderText = "Search";
    private const string NoResultsText = "No results";

    private readonly SettingsPalette _palette;
    private readonly SettingsSearchableListBoxItemCollection _items;
    private readonly Grid _searchRow;
    private readonly TextBox _searchBox;
    private readonly SettingsButton _clearButton;
    private readonly StackPanel _itemsPanel;
    private readonly SettingsScrollHost _scrollHost;
    private readonly Border _listBorder;
    private List<SettingsSearchableListBoxItem> _visibleItems = [];
    private SettingsSearchableListBoxItem? _selectedItem;
    private SettingsSearchableListBoxItem? _activeItem;
    private double _itemFontSize = SearchableListBoxLayout.ItemFontSize;
    private Thickness _itemPadding = SearchableListBoxLayout.ItemPadding;
    private Thickness _itemMargin = SearchableListBoxLayout.ItemMargin;
    private CornerRadius _itemCornerRadius = SearchableListBoxLayout.ItemRadius;

    public SettingsSearchableListBox(SettingsPalette palette)
    {
        _palette = palette;
        _items = new SettingsSearchableListBoxItemCollection(this);
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
            SelectionForegroundBrush = TrayAppDotNETSettingsUI.Brush(palette.Foreground),
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
        _clearButton.Click += (_, _) => ClearSearch();

        _searchRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        _searchRow.Children.Add(_searchBox);
        Grid.SetColumn(_clearButton, 1);
        _searchRow.Children.Add(_clearButton);
        Children.Add(_searchRow);

        _itemsPanel = new StackPanel();
        _scrollHost = TrayAppDotNETSettingsUI.ScrollHost(_itemsPanel, palette, SearchableListBoxLayout.ScrollHostPadding);
        _scrollHost.Height = SearchableListBoxLayout.ListHeight;
        _scrollHost.VerticalAlignment = VerticalAlignment.Stretch;

        _listBorder = new Border
        {
            Height = SearchableListBoxLayout.ListHeight,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = TrayAppDotNETSettingsUI.Brush(palette.ControlBackground),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            BorderThickness = SearchableListBoxLayout.ListBorderThickness,
            CornerRadius = SearchableListBoxLayout.ListRadius,
            ClipToBounds = true,
            Child = _scrollHost,
        };
        Grid.SetRow(_listBorder, 1);
        Children.Add(_listBorder);

        _searchBox.TextChanged += (_, _) =>
        {
            RebuildItems();
            _scrollHost.SetVerticalOffset(0);
            UpdateClearButton();
        };
        KeyDown += OnKeyboardNavigation;
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

            double height = Math.Max(1, value);
            _listBorder.Height = height;
            _listBorder.VerticalAlignment = VerticalAlignment.Top;
            _scrollHost.Height = height;
            _scrollHost.VerticalAlignment = VerticalAlignment.Top;
        }
    }

    public double SearchBoxHeight
    {
        get => _searchBox.Height;
        set => _searchBox.Height = Math.Max(1, value);
    }

    public Thickness SearchBoxPadding
    {
        get => _searchBox.Padding;
        set => _searchBox.Padding = value;
    }

    public double ClearButtonWidth
    {
        get => _clearButton.Width;
        set => _clearButton.Width = Math.Max(1, value);
    }

    public double ClearButtonHeight
    {
        get => _clearButton.Height;
        set
        {
            double height = Math.Max(1, value);
            _clearButton.Height = height;
            _clearButton.MinHeight = height;
        }
    }

    public double ClearButtonFontSize
    {
        get => _clearButton.Label.FontSize;
        set => _clearButton.Label.FontSize = Math.Max(1, value);
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
        get => _itemsPanel.Margin;
        set => _itemsPanel.Margin = value;
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
            _itemFontSize = Math.Max(1, value);
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
    internal void OnItemAdded(SettingsSearchableListBoxItem item)
    {
        RebuildItems();
    }

    /// <summary>
    /// Handles an item removal.
    /// </summary>
    internal void OnItemRemoved(SettingsSearchableListBoxItem item)
    {
        if (ReferenceEquals(_selectedItem, item))
            SetSelectedItem(null, raiseChanged: true);

        if (ReferenceEquals(_activeItem, item))
            _activeItem = null;

        RebuildItems();
    }

    /// <summary>
    /// Handles collection reset.
    /// </summary>
    internal void OnItemsReset()
    {
        SetSelectedItem(null, raiseChanged: true);
        _activeItem = null;
        RebuildItems();
    }

    /// <summary>
    /// Marks an item as active without committing selection.
    /// </summary>
    internal void ActivateItem(SettingsSearchableListBoxItem item)
    {
        _activeItem = item;
        RebuildItems();
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
    private void RebuildItems()
    {
        _itemsPanel.Children.Clear();
        _visibleItems = SearchMatcher.FilterAndRank(
            _items,
            _searchBox.Text,
            static item => item.SearchText);
        EnsureActiveItemVisible();

        if (_visibleItems.Count == 0)
        {
            TextBlock empty = TrayAppDotNETSettingsUI.Text(NoResultsText, _palette, _itemFontSize);
            empty.Opacity = SearchableListBoxLayout.EmptyOpacity;
            Border emptyHost = new()
            {
                Background = Brushes.Transparent,
                Padding = _itemPadding,
                Margin = _itemMargin,
                Child = empty,
            };
            _itemsPanel.Children.Add(emptyHost);
            return;
        }

        foreach (SettingsSearchableListBoxItem item in _visibleItems)
        {
            SettingsSearchableListBoxItemRow row = new(
                item,
                this,
                _palette,
                _itemFontSize,
                _itemPadding,
                _itemMargin,
                _itemCornerRadius);
            row.IsSelected = ReferenceEquals(item, _selectedItem);
            row.IsActive = ReferenceEquals(item, _activeItem);
            _itemsPanel.Children.Add(row);
        }
    }

    /// <summary>
    /// Keeps keyboard selection on a visible row.
    /// </summary>
    private void EnsureActiveItemVisible()
    {
        if (_visibleItems.Count == 0)
        {
            _activeItem = null;
            return;
        }

        if (_activeItem != null && _visibleItems.Contains(_activeItem)) return;
        if (_selectedItem != null && _visibleItems.Contains(_selectedItem))
        {
            _activeItem = _selectedItem;
            return;
        }

        _activeItem = _visibleItems[0];
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

        int nextIndex = Math.Clamp(index + delta, 0, _visibleItems.Count - 1);
        _activeItem = _visibleItems[nextIndex];
        RebuildItems();
        QueueScrollActiveItemIntoView();
    }

    /// <summary>
    /// Sets selected item state and raises selection changes.
    /// </summary>
    private void SetSelectedItem(SettingsSearchableListBoxItem? item, bool raiseChanged)
    {
        if (ReferenceEquals(_selectedItem, item)) return;
        _selectedItem = item;
        if (item != null)
            _activeItem = item;

        RebuildItems();
        QueueScrollActiveItemIntoView();
        if (raiseChanged)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Enables the clear button only when a query exists.
    /// </summary>
    private void UpdateClearButton()
    {
        _clearButton.IsEnabled = !string.IsNullOrEmpty(_searchBox.Text);
    }

    /// <summary>
    /// Scrolls after layout has measured rebuilt rows.
    /// </summary>
    private void QueueScrollActiveItemIntoView() =>
        Dispatcher.UIThread.Post(ScrollActiveItemIntoView, DispatcherPriority.Loaded);

    /// <summary>
    /// Scrolls the keyboard-active item into the visible list area.
    /// </summary>
    private void ScrollActiveItemIntoView()
    {
        if (_activeItem == null) return;

        int index = _visibleItems.IndexOf(_activeItem);
        if (index < 0 || index >= _itemsPanel.Children.Count) return;
        if (_itemsPanel.Children[index] is not Control row) return;

        Point? rowPoint = row.TranslatePoint(new Point(0, 0), _itemsPanel);
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
}

/// <summary>
/// Item model for a settings searchable list.
/// </summary>
public sealed class SettingsSearchableListBoxItem
{
    private readonly Func<Control>? _contentFactory;

    public SettingsSearchableListBoxItem(
        object tag,
        string text,
        string searchText = "",
        Func<Control>? contentFactory = null)
    {
        Tag = tag;
        Text = text;
        SearchText = string.IsNullOrWhiteSpace(searchText) ? text : searchText;
        _contentFactory = contentFactory;
    }

    public object Tag { get; }

    public string Text { get; }

    public string SearchText { get; }

    /// <summary>
    /// Builds display content for this list item.
    /// </summary>
    public Control CreateContent(SettingsPalette palette, double fontSize)
    {
        if (_contentFactory != null) return _contentFactory();

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
        owner.OnItemAdded(item);
    }

    /// <summary>
    /// Replaces a searchable-list item.
    /// </summary>
    protected override void SetItem(int index, SettingsSearchableListBoxItem item)
    {
        SettingsSearchableListBoxItem old = this[index];
        owner.OnItemRemoved(old);
        base.SetItem(index, item);
        owner.OnItemAdded(item);
    }

    /// <summary>
    /// Removes a searchable-list item.
    /// </summary>
    protected override void RemoveItem(int index)
    {
        SettingsSearchableListBoxItem old = this[index];
        base.RemoveItem(index);
        owner.OnItemRemoved(old);
    }

    /// <summary>
    /// Clears all searchable-list items.
    /// </summary>
    protected override void ClearItems()
    {
        foreach (SettingsSearchableListBoxItem item in this)
            owner.OnItemRemoved(item);
        base.ClearItems();
        owner.OnItemsReset();
    }
}

/// <summary>
/// Visual row for one searchable-list item.
/// </summary>
internal sealed class SettingsSearchableListBoxItemRow : Border
{
    private readonly SettingsSearchableListBoxItem _item;
    private readonly SettingsSearchableListBox _owner;
    private readonly SettingsPalette _palette;
    private readonly Control _content;
    private bool _isPointerOver;
    private bool _isSelected;
    private bool _isActive;

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
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;
        Child = _content;

        PointerEntered += (_, _) =>
        {
            _isPointerOver = true;
            UpdateVisual();
        };
        PointerExited += (_, _) =>
        {
            _isPointerOver = false;
            UpdateVisual();
        };
        PointerPressed += (_, e) =>
        {
            if (!IsEnabled) return;
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

            if (e.ClickCount >= 2)
                _owner.CommitItem(_item);
            else
                _owner.ActivateItem(_item);

            e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key is not Key.Enter) return;
            _owner.CommitItem(_item);
            e.Handled = true;
        };

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
            label.Foreground = TrayAppDotNETSettingsUI.Brush(highlighted
                ? ContrastForeground(background)
                : _palette.Foreground);
    }

    /// <summary>
    /// Picks readable text for user-configurable highlight colors.
    /// </summary>
    private static Color ContrastForeground(Color background)
    {
        double red = background.R / 255.0;
        double green = background.G / 255.0;
        double blue = background.B / 255.0;
        double luminance = (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
        return luminance > 0.55 ? Colors.Black : Colors.White;
    }
}
