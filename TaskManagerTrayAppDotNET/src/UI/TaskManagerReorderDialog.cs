using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Hosts a Task Manager reorder list in an owned modal window.</summary>
internal abstract class TaskManagerReorderDialog<TItem> : Window, IDisposable
    where TItem : class
{
    private readonly List<TItem> _items;
    private readonly Func<List<TItem>> _createDefaultItems;
    private readonly Action<IReadOnlyList<TItem>> _itemsChanged;
    private readonly TaskManagerReorderList<TItem> _reorderList;
    private readonly SettingsVerticalScrollViewport? _scrollViewport;
    private readonly TextBox? _searchBox;
    private readonly Grid _titleBar;
    private readonly TrayAppDotNETCaptionCloseButton _closeButton;
    private readonly SettingsButton _resetButton;
    private readonly SettingsButton _cancelButton;
    private readonly SettingsButton _doneButton;
    private readonly double _workAreaMargin;
    private readonly double _searchWidthRatio;
    private int _resetConfirmationPending;
    private int _disposed;

    /// <summary>Gets the palette shared by the dialog and any owned prompts.</summary>
    protected SettingsPalette Palette { get; }

    /// <summary>Gets whether owned surfaces should use rounded corners.</summary>
    protected bool RoundedCornersEnabled { get; }

#if DEBUG
    /// <summary>Replaces cached scrollbar menu options without rebuilding the dialog.</summary>
    internal void SetScrollBarContextMenuOptions(ContextMenuWindowOptions contextMenuOptions)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(contextMenuOptions);
        _scrollViewport?.SetContextMenuOptions(contextMenuOptions);
    }
#endif

    protected TaskManagerReorderDialog(
        string title,
        string description,
        List<TItem> items,
        Func<TItem, string> getSearchText,
        Func<TItem, Action, Control> buildPrimaryContent,
        Func<List<TItem>> createDefaultItems,
        Action<IReadOnlyList<TItem>> itemsChanged,
        SettingsPalette palette,
        bool enableRoundedCorners,
        TaskManagerWindowResources resources,
        Color background,
        double width,
        double height,
        double minimumHeight,
        bool showSearch,
        string searchPlaceholder,
        SettingsScrollBarStyle? scrollBarStyle = null,
        ContextMenuWindowOptions? scrollBarContextMenuOptions = null,
        Action<TItem>? activateItem = null,
        Control? headerTrailingControl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(getSearchText);
        ArgumentNullException.ThrowIfNull(buildPrimaryContent);
        ArgumentNullException.ThrowIfNull(createDefaultItems);
        ArgumentNullException.ThrowIfNull(itemsChanged);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(resources);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (minimumHeight <= 0 || minimumHeight > height)
            throw new ArgumentOutOfRangeException(nameof(minimumHeight));
        if (scrollBarStyle.HasValue != (scrollBarContextMenuOptions != null))
        {
            throw new ArgumentException(
                "A scrollbar style and context-menu options must be supplied together.",
                nameof(scrollBarStyle));
        }

        _items = items;
        _createDefaultItems = createDefaultItems;
        _itemsChanged = itemsChanged;
        Palette = palette;
        RoundedCornersEnabled = enableRoundedCorners;
        _workAreaMargin = resources.AxamlTaskManagerReorderDialog.WorkAreaMargin;
        _searchWidthRatio = resources.AxamlTaskManagerReorderDialog.SearchWidthRatio;
        if (_searchWidthRatio <= 0 || _searchWidthRatio > 1)
            throw new InvalidOperationException("The reorder-dialog search width ratio must be in (0, 1].");

        Title = title;
        Width = width;
        Height = height;
        MinWidth = width;
        MaxWidth = width;
        MinHeight = minimumHeight;
        MaxHeight = height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowDecorations = WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;
        FontFamily = TrayAppDotNETSettingsUI.UIFont;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];

        _reorderList = new TaskManagerReorderList<TItem>(
            _items,
            getSearchText,
            item => buildPrimaryContent(item, OnItemChanged),
            palette,
            enableRoundedCorners,
            activateItem);
        _reorderList.ItemsChanged += OnItemsChanged;
        _reorderList.OrderPreviewChanged += OnOrderPreviewChanged;

        _searchBox = showSearch
            ? TrayAppDotNETSettingsUI.SearchTextBox(
                palette,
                width * _searchWidthRatio)
            : null;
        if (_searchBox != null)
        {
            _searchBox.PlaceholderText = searchPlaceholder;
            _searchBox.HorizontalAlignment = HorizontalAlignment.Center;
            _searchBox.Margin = resources.AxamlTaskManagerReorderDialog.SearchMargin;
            _searchBox.TextChanged += OnSearchTextChanged;
        }

        _closeButton = new TrayAppDotNETCaptionCloseButton(palette);
        _closeButton.Click += OnCancelClick;
        TrayAppDotNETToolTip.SetTip(_closeButton, "Close");
        TrayAppDotNETToolTip.SuppressWhileEngaged(_closeButton);
        _titleBar = BuildTitleBar(title, palette, resources, _closeButton);
        _titleBar.PointerPressed += OnTitleBarPointerPressed;

        _resetButton = TrayAppDotNETSettingsUI.Button("Reset", palette);
        _resetButton.Click += OnResetClick;
        _cancelButton = TrayAppDotNETSettingsUI.Button("Cancel", palette);
        _cancelButton.Click += OnCancelClick;
        _doneButton = TrayAppDotNETSettingsUI.Button("Done", palette);
        _doneButton.Click += OnDoneClick;

        Control listHost;
        if (scrollBarStyle is { } style)
        {
            _scrollViewport = new SettingsVerticalScrollViewport(
                _reorderList,
                resources.AxamlTaskManagerReorderDialog.ListPadding,
                background,
                style,
                scrollBarContextMenuOptions!);
            _reorderList.AttachScrollViewport(_scrollViewport);
            listHost = _scrollViewport;
        }
        else
        {
            listHost = new Border
            {
                Background = TrayAppDotNETSettingsUI.Brush(background),
                Margin = resources.AxamlTaskManagerReorderDialog.UnscrolledListMargin,
                Child = _reorderList
            };
        }

        Grid content = new()
        {
            RowDefinitions =
            {
                new RowDefinition(new GridLength(_closeButton.Height)),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };
        content.Children.Add(_titleBar);

        Border header = BuildHeader(
            description,
            palette,
            background,
            resources,
            _searchBox,
            headerTrailingControl);
        Grid.SetRow(header, 1);
        content.Children.Add(header);

        Grid.SetRow(listHost, 2);
        content.Children.Add(listHost);

        Border footer = BuildFooter(
            palette,
            background,
            resources,
            _resetButton,
            _cancelButton,
            _doneButton);
        Grid.SetRow(footer, 3);
        content.Children.Add(footer);

        Content = new FlyoutFrame(
            content,
            background,
            palette.Border,
            enableRoundedCorners);

        KeyDown += OnWindowKeyDown;
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private static Grid BuildTitleBar(
        string title,
        SettingsPalette palette,
        TaskManagerWindowResources resources,
        TrayAppDotNETCaptionCloseButton closeButton)
    {
        Grid titleBar = new()
        {
            Background = Brushes.Transparent,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        TextBlock titleText = TrayAppDotNETSettingsUI.Text(
            title,
            palette,
            resources.AxamlTaskManagerReorderDialog.TitleFontSize,
            (FontWeight)resources.AxamlTaskManagerReorderDialog.TitleFontWeight);
        titleText.VerticalAlignment = VerticalAlignment.Center;
        titleText.Margin = resources.AxamlTaskManagerReorderDialog.TitleMargin;
        titleBar.Children.Add(titleText);
        Grid.SetColumn(closeButton, 1);
        titleBar.Children.Add(closeButton);
        return titleBar;
    }

    private static Border BuildHeader(
        string description,
        SettingsPalette palette,
        Color background,
        TaskManagerWindowResources resources,
        TextBox? searchBox,
        Control? trailingControl)
    {
        StackPanel content = new();
        if (searchBox != null) content.Children.Add(searchBox);

        TextBlock descriptionText = TrayAppDotNETSettingsUI.DescriptionText(description, palette);
        descriptionText.TextWrapping = TextWrapping.Wrap;
        descriptionText.VerticalAlignment = VerticalAlignment.Center;
        Grid descriptionRow = new()
        {
            ColumnSpacing = resources.AxamlTaskManagerReorderDialog.DescriptionOptionSpacing,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        descriptionRow.Children.Add(descriptionText);
        if (trailingControl != null)
        {
            trailingControl.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trailingControl, 1);
            descriptionRow.Children.Add(trailingControl);
        }
        content.Children.Add(descriptionRow);
        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(background),
            Padding = resources.AxamlTaskManagerReorderDialog.HeaderPadding,
            Child = content
        };
    }

    private static Border BuildFooter(
        SettingsPalette palette,
        Color background,
        TaskManagerWindowResources resources,
        SettingsButton resetButton,
        SettingsButton cancelButton,
        SettingsButton doneButton)
    {
        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = resources.AxamlTaskManagerReorderDialog.FooterButtonSpacing,
            Children = { resetButton, cancelButton, doneButton }
        };
        return new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            BorderThickness = resources.AxamlTaskManagerReorderDialog.FooterBorderThickness,
            Padding = resources.AxamlTaskManagerReorderDialog.FooterPadding,
            Child = buttons
        };
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        _reorderList.SetFilter(_searchBox?.Text);
        _scrollViewport?.SetVerticalOffset(0);
    }

    private async void OnResetClick(object? sender, EventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (Interlocked.Exchange(ref _resetConfirmationPending, 1) != 0) return;

        bool confirmed;
        try
        {
            confirmed = await ConfirmResetAsync();
        }
        catch (Exception exception)
        {
            TADNLog.Log($"{GetType().Name} reset confirmation failed: {exception}");
            return;
        }
        finally
        {
            Volatile.Write(ref _resetConfirmationPending, 0);
        }

        if (!confirmed || Volatile.Read(ref _disposed) != 0) return;

        List<TItem> defaultItems = _createDefaultItems();
        _items.Clear();
        _items.AddRange(defaultItems);
        _reorderList.Refresh();
        PublishItemsChanged();
    }

    /// <summary>Confirms a reset when the specialized reorder dialog requires it.</summary>
    protected virtual Task<bool> ConfirmResetAsync() => Task.FromResult(true);

    /// <summary>Applies an additional item filter without changing the caller-owned list.</summary>
    protected void SetItemFilter(Func<TItem, bool>? includeItem)
    {
        _reorderList.SetItemFilter(includeItem);
        _scrollViewport?.SetVerticalOffset(0);
    }

    private void OnCancelClick(object? sender, EventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) == 0) Close();
    }

    private void OnDoneClick(object? sender, EventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        Close();
    }

    private void OnItemsChanged()
    {
        if (Volatile.Read(ref _disposed) == 0) PublishItemsChanged();
    }

    private void OnOrderPreviewChanged(IReadOnlyList<TItem> items)
    {
        if (Volatile.Read(ref _disposed) == 0) _itemsChanged(items);
    }

    private void OnItemChanged()
    {
        if (Volatile.Read(ref _disposed) != 0) return;

        _reorderList.Refresh();
        PublishItemsChanged();
    }

    private void PublishItemsChanged() => _itemsChanged([.. _items]);

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (!eventArgs.GetCurrentPoint(_titleBar).Properties.IsLeftButtonPressed) return;
        if (_closeButton.IsPointerOver) return;

        BeginMoveDrag(eventArgs);
        eventArgs.Handled = true;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposed) != 0 || eventArgs.Key != Key.Escape) return;

        Close();
        eventArgs.Handled = true;
    }

    private void OnOpened(object? sender, EventArgs eventArgs)
    {
        PixelRect? workingArea = Screens.ScreenFromWindow(this)?.WorkingArea;
        if (workingArea is not PixelRect availableArea) return;

        double renderScaling = RenderScaling > 0 ? RenderScaling : 1;
        double availableWidth = Math.Max(1, availableArea.Width / renderScaling - _workAreaMargin);
        double availableHeight = Math.Max(1, availableArea.Height / renderScaling - _workAreaMargin);
        double nextWidth = Math.Min(MaxWidth, availableWidth);
        double nextHeight = Math.Min(MaxHeight, availableHeight);
        MinWidth = Math.Min(MinWidth, nextWidth);
        MinHeight = Math.Min(MinHeight, nextHeight);
        Width = nextWidth;
        Height = nextHeight;
        if (_searchBox != null) _searchBox.Width = nextWidth * _searchWidthRatio;

        int windowWidthPixels = Math.Max(1, (int)Math.Ceiling(Width * renderScaling));
        int windowHeightPixels = Math.Max(1, (int)Math.Ceiling(Height * renderScaling));
        int maximumX = Math.Max(availableArea.X, availableArea.X + availableArea.Width - windowWidthPixels);
        int maximumY = Math.Max(availableArea.Y, availableArea.Y + availableArea.Height - windowHeightPixels);
        Position = new PixelPoint(
            Math.Clamp(Position.X, availableArea.X, maximumX),
            Math.Clamp(Position.Y, availableArea.Y, maximumY));
    }

    private void OnClosed(object? sender, EventArgs eventArgs) => Dispose();

    /// <summary>Releases the reorder list, scrollbar, and window event handlers.</summary>
    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        _reorderList.CancelActiveDrag();
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        Closed -= OnClosed;
        Opened -= OnOpened;
        KeyDown -= OnWindowKeyDown;
        _titleBar.PointerPressed -= OnTitleBarPointerPressed;
        _closeButton.Click -= OnCancelClick;
        _resetButton.Click -= OnResetClick;
        _cancelButton.Click -= OnCancelClick;
        _doneButton.Click -= OnDoneClick;
        _reorderList.ItemsChanged -= OnItemsChanged;
        _reorderList.OrderPreviewChanged -= OnOrderPreviewChanged;
        if (_searchBox != null) _searchBox.TextChanged -= OnSearchTextChanged;
        _reorderList.Dispose();
        _scrollViewport?.Dispose();
        Content = null;
    }
}
