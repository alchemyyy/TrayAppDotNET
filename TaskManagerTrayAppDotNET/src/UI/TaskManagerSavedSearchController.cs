using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.UI.ControlMapping;
using TaskManagerGlyphCatalog = TaskManagerTrayAppDotNET.Visuals.GlyphCatalog;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>
/// Lays out a page search box with its Clear and Save actions and hosts the saved-search menu anchored
/// directly beneath the box.
/// </summary>
internal sealed class TaskManagerSavedSearchController : IDisposable
{
    private static string SaveSearchTip => LocalizationManager.Instance[nameof(AppStrings.SavedSearch_Save)];
    private static string SearchAlreadySavedTip => LocalizationManager.Instance[nameof(AppStrings.SavedSearch_AlreadySaved)];
    private static string ClearSearchTip => LocalizationManager.Instance[nameof(AppStrings.SavedSearch_Clear)];

    private readonly TextBox _textBox;
    private readonly SettingsPalette _palette;
    private readonly bool _enableRoundedCorners;
    private readonly ITrayAppDotNETTrayMenuSettings _trayMenuSettings;
    private readonly Action<IReadOnlyList<SavedSearch>> _savedSearchesChanged;
    private readonly Func<SavedSearch, Task<bool>> _confirmRegexDeletion;
    private readonly bool _leftAligned;
    private readonly InsetGlyphButton _clearButton;
    private readonly InsetGlyphButton _saveButton;
    private readonly Grid _searchControls;
    private readonly TranslateTransform _searchControlsTransform = new();
    private double _actionSpacing;
    private IBrush _invalidTint;
    private string? _queryError;
    private List<SavedSearch> _savedSearches;
    private EditableContextMenuWindow? _menuWindow;
    private Window? _menuOwner;
    private bool _menuRepositionPending;
    private bool _deleteConfirmationPending;
#if DEBUG
    private bool _hotReloadAttached;
#endif
    private bool _disposed;

    public TaskManagerSavedSearchController(
        TextBox textBox,
        ControlMapNodeID clearNode,
        ControlMapNodeID saveNode,
        IReadOnlyList<SavedSearch> savedSearches,
        SettingsPalette palette,
        TaskManagerWindowResources windowResources,
        bool leftAligned,
        bool enableRoundedCorners,
        ITrayAppDotNETTrayMenuSettings trayMenuSettings,
        Action<IReadOnlyList<SavedSearch>> savedSearchesChanged,
        Func<SavedSearch, Task<bool>> confirmRegexDeletion)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(savedSearches);
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(windowResources);
        ArgumentNullException.ThrowIfNull(trayMenuSettings);
        ArgumentNullException.ThrowIfNull(savedSearchesChanged);
        ArgumentNullException.ThrowIfNull(confirmRegexDeletion);

        _textBox = textBox;
        _palette = palette;
        _enableRoundedCorners = enableRoundedCorners;
        _trayMenuSettings = trayMenuSettings;
        _savedSearchesChanged = savedSearchesChanged;
        _confirmRegexDeletion = confirmRegexDeletion;
        _leftAligned = leftAligned;
        _actionSpacing = windowResources.AxamlTaskManagerDetails.SearchActionSpacing;
        _invalidTint = TrayAppDotNETSettingsUI.Brush(windowResources.AxamlTaskManagerDetails.SearchInvalidTintColor);
        _savedSearches = SavedSearchCollection.Normalize(savedSearches);

        double actionButtonSize = textBox.Height;
        double actionGlyphFontSize =
            windowResources.AxamlTaskManagerDetails.SearchActionGlyphFontSize;
        _clearButton = new InsetGlyphButton(
            TaskManagerGlyphCatalog.CLOSE,
            palette,
            actionButtonSize,
            actionGlyphFontSize * windowResources.AxamlTaskManagerDetails.SearchClearGlyphScale,
            windowResources.AxamlTaskManagerDetails.SearchActionVisualInset,
            windowResources.AxamlTaskManagerDetails.SearchActionVisualCornerRadius,
            windowResources.AxamlTaskManagerDetails.SearchActionButtonPadding,
            windowResources.AxamlTaskManagerDetails.SearchActionGlyphOpacity,
            windowResources.AxamlTaskManagerDetails.SearchActionDisabledOpacity) { IsVisible = HasQuery() };
        _clearButton.MapTo(clearNode);
        _clearButton.Click += OnClearClick;
        TrayAppDotNETToolTip.SetTip(_clearButton, ClearSearchTip);
        TrayAppDotNETToolTip.SuppressWhileEngaged(_clearButton);

        _saveButton = new InsetGlyphButton(
            TaskManagerGlyphCatalog.SAVE,
            palette,
            actionButtonSize,
            actionGlyphFontSize,
            windowResources.AxamlTaskManagerDetails.SearchActionVisualInset,
            windowResources.AxamlTaskManagerDetails.SearchActionVisualCornerRadius,
            windowResources.AxamlTaskManagerDetails.SearchActionButtonPadding,
            windowResources.AxamlTaskManagerDetails.SearchActionGlyphOpacity,
            windowResources.AxamlTaskManagerDetails.SearchActionDisabledOpacity) { IsVisible = HasQuery() };
        _saveButton.MapTo(saveNode);
        _saveButton.Click += OnSaveClick;
        TrayAppDotNETToolTip.SetTip(_saveButton, SaveSearchTip);
        TrayAppDotNETToolTip.SuppressWhileEngaged(_saveButton);
        UpdateSaveButtonState();

        _searchControls = new Grid
        {
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = _searchControlsTransform,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        _searchControls.Children.Add(_clearButton);
        Grid.SetColumn(_saveButton, value: 1);
        _searchControls.Children.Add(_saveButton);
        Grid.SetColumn(_textBox, value: 2);
        _searchControls.Children.Add(_textBox);
        ApplyActionSpacing();
        UpdateSearchControlsPosition();

        _textBox.TextChanged += OnTextChanged;
        _textBox.AddHandler(
            InputElement.PointerPressedEvent,
            OnPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _textBox.KeyDown += OnKeyDown;
        _textBox.LostFocus += OnTextBoxLostFocus;
    }

    /// <summary>Gets Clear, Save and the search box, shifted so the box keeps its place while actions show.</summary>
    public Control SearchControls => _searchControls;

    public Control SaveButton => _saveButton;

    /// <summary>Gets the search box and visible leading action widths for restored-window drag avoidance.</summary>
    public bool TryGetSearchDragRegionPixelWidths(
        out int searchWidth,
        out int leadingActionWidth)
    {
        searchWidth = 0;
        leadingActionWidth = 0;
        TopLevel? topLevel = TopLevel.GetTopLevel(_textBox);
        if (!_textBox.IsEffectivelyVisible
            || _textBox.Bounds.Width <= 0
            || topLevel == null)
            return false;

        PixelPoint screenLeft = _textBox.PointToScreen(default);
        PixelPoint screenRight = _textBox.PointToScreen(new Point(_textBox.Bounds.Width, y: 0));
        int searchLeft = Math.Min(screenLeft.X, screenRight.X);
        searchWidth = Math.Abs(screenRight.X - screenLeft.X);
        if (searchWidth <= 0) return false;

        if (!_clearButton.IsEffectivelyVisible) return true;
        if (_clearButton.Bounds.Width <= 0
            || TopLevel.GetTopLevel(_clearButton) == null)
        {
            leadingActionWidth = (int)Math.Ceiling(
                GetLeadingSearchActionWidth() * topLevel.RenderScaling);
            return true;
        }

        PixelPoint actionLeft = _clearButton.PointToScreen(default);
        PixelPoint actionRight = _clearButton.PointToScreen(new Point(_clearButton.Bounds.Width, y: 0));
        int actionLeftX = Math.Min(actionLeft.X, actionRight.X);
        leadingActionWidth = Math.Max(val1: 0, searchLeft - actionLeftX);
        return true;
    }

#if DEBUG
    /// <summary>Attaches process-wide reload notifications after the owning page is fully built.</summary>
    internal void AttachAXAMLHotReload()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hotReloadAttached) return;

        TaskManagerContextMenuResources.ResourcesReloaded += OnAXAMLResourcesReloaded;
        _hotReloadAttached = true;
    }

    /// <summary>Applies search-action AXAML metrics without replacing the current query.</summary>
    internal void ApplyAXAMLResources(TaskManagerWindowResources windowResources)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(windowResources);

        double actionButtonSize = _textBox.Height;
        double actionGlyphFontSize =
            windowResources.AxamlTaskManagerDetails.SearchActionGlyphFontSize;
        _clearButton.ApplyAXAMLResources(
            TaskManagerGlyphCatalog.CLOSE,
            actionButtonSize,
            actionGlyphFontSize * windowResources.AxamlTaskManagerDetails.SearchClearGlyphScale,
            windowResources.AxamlTaskManagerDetails.SearchActionVisualInset,
            windowResources.AxamlTaskManagerDetails.SearchActionVisualCornerRadius,
            windowResources.AxamlTaskManagerDetails.SearchActionButtonPadding,
            windowResources.AxamlTaskManagerDetails.SearchActionGlyphOpacity,
            windowResources.AxamlTaskManagerDetails.SearchActionDisabledOpacity);
        _saveButton.ApplyAXAMLResources(
            TaskManagerGlyphCatalog.SAVE,
            actionButtonSize,
            actionGlyphFontSize,
            windowResources.AxamlTaskManagerDetails.SearchActionVisualInset,
            windowResources.AxamlTaskManagerDetails.SearchActionVisualCornerRadius,
            windowResources.AxamlTaskManagerDetails.SearchActionButtonPadding,
            windowResources.AxamlTaskManagerDetails.SearchActionGlyphOpacity,
            windowResources.AxamlTaskManagerDetails.SearchActionDisabledOpacity);
        _actionSpacing = windowResources.AxamlTaskManagerDetails.SearchActionSpacing;
        _invalidTint = TrayAppDotNETSettingsUI.Brush(windowResources.AxamlTaskManagerDetails.SearchInvalidTintColor);
        ApplyQueryErrorVisual();
        ApplyActionSpacing();
        UpdateSearchControlsPosition();
    }
#endif

    private bool HasQuery() => !string.IsNullOrWhiteSpace(_textBox.Text);

    /// <summary>Tints Clear red and explains the error in its tooltip while the query is invalid.</summary>
    /// <param name="errorMessage">The query's error, or <see langword="null"/> when it is valid or empty.</param>
    public void SetQueryError(string? errorMessage)
    {
        if (_disposed || string.Equals(_queryError, errorMessage, StringComparison.Ordinal)) return;

        _queryError = errorMessage;
        ApplyQueryErrorVisual();
    }

    private void ApplyQueryErrorVisual()
    {
        _clearButton.SetTint(_queryError == null ? null : _invalidTint);
        TrayAppDotNETToolTip.SetTip(
            _clearButton,
            _queryError == null
                ? ClearSearchTip
                : string.Format(
                    CultureInfo.CurrentCulture,
                    LocalizationManager.Instance[nameof(AppStrings.SavedSearch_ClearInvalidFormat)],
                    _queryError));
    }

    private void ApplyActionSpacing() =>
        _saveButton.Margin = new Thickness(left: 0, top: 0, _actionSpacing, bottom: 0);

    /// <summary>Shifts the controls left by the visible actions so the box itself does not move.</summary>
    private void UpdateSearchControlsPosition()
    {
        if (!HasQuery())
        {
            _searchControlsTransform.X = 0;
            return;
        }

        double leadingActionWidth = GetLeadingSearchActionWidth();
        _searchControlsTransform.X = _leftAligned
            ? -leadingActionWidth
            : -leadingActionWidth / 2;
    }

    private double GetLeadingSearchActionWidth() =>
        _clearButton.Width + _saveButton.Width + _actionSpacing;

    /// <summary>Disables Save and explains why while the current query is already saved.</summary>
    private void UpdateSaveButtonState()
    {
        bool canSave = !SavedSearchCollection.ContainsQuery(_savedSearches, _textBox.Text);
        if (_saveButton.IsEnabled == canSave) return;

        _saveButton.IsEnabled = canSave;
        TrayAppDotNETToolTip.SetTip(_saveButton, canSave ? SaveSearchTip : SearchAlreadySavedTip);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        bool hasQuery = HasQuery();
        _clearButton.IsVisible = hasQuery;
        _saveButton.IsVisible = hasQuery;
        UpdateSaveButtonState();
        UpdateSearchControlsPosition();
        if (hasQuery) Close();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (_disposed
            || !eventArgs.GetCurrentPoint(_textBox).Properties.IsLeftButtonPressed
            || HasQuery())
            return;

        _textBox.Focus();
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && !HasQuery()) Open();
        }, DispatcherPriority.Input);
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Escape || _menuWindow == null) return;

        Close();
        eventArgs.Handled = true;
    }

    private void Open()
    {
        Close();
        if (TopLevel.GetTopLevel(_textBox) is not Window owner) return;

        EditableContextMenuWindow menuWindow =
            TaskManagerSavedSearchMenu.Create(
                BuildEntries(),
                _palette,
                _enableRoundedCorners,
                _trayMenuSettings);
        double configuredWidth = double.IsFinite(_textBox.Width) ? _textBox.Width : 1;
        double menuWidth = _textBox.Bounds.Width > 0
            ? _textBox.Bounds.Width
            : Math.Max(val1: 1, configuredWidth);
        menuWindow.SizeToContent = SizeToContent.Height;
        menuWindow.Width = menuWidth;
        menuWindow.MinWidth = menuWidth;
        menuWindow.MaxWidth = menuWidth;
        menuWindow.MaxHeight =
            TaskManagerContextMenuResources.Current.AxamlTaskManagerContextMenu.SavedSearchMaximumHeight;
        menuWindow.Closed += OnMenuClosed;
        _menuWindow = menuWindow;
        AttachMenuOwner(owner);

        try
        {
            menuWindow.ShowOver(_textBox, _textBox, owner);
        }
        catch (Exception exception)
        {
            menuWindow.Closed -= OnMenuClosed;
            _menuWindow = null;
            DetachMenuOwner();
            try
            {
                menuWindow.Close();
            }
            catch (Exception closeException)
            {
                TADNLog.Log($"Saved-search menu cleanup failed: {closeException}");
            }

            TADNLog.Log($"Saved-search menu failed to open: {exception}");
        }
    }

    private List<EditableContextMenuEntry> BuildEntries()
    {
        List<EditableContextMenuEntry> entries = [];
        if (_savedSearches.Count == 0)
        {
            entries.Add(new EditableContextMenuEntry(Text: LocalizationManager.Instance[nameof(AppStrings.SavedSearch_None)], static () => { })
                { IsEnabled = false });
            return entries;
        }

        TaskManagerContextMenuResources resources = TaskManagerContextMenuResources.Current;
        for (int searchIndex = 0; searchIndex < _savedSearches.Count; searchIndex++)
        {
            SavedSearch savedSearch = _savedSearches[searchIndex];
            int capturedSearchIndex = searchIndex;
            EditableContextMenuEntryButton renameButton = new(static () => { })
            {
                Glyph = TaskManagerGlyphCatalog.MORE,
                ToolTip = LocalizationManager.Instance[nameof(AppStrings.SavedSearch_Rename)],
                DismissMenuOnClick = false,
                Size = resources.AxamlTaskManagerContextMenu.SavedSearchButtonSize,
                FontSize = resources.AxamlTaskManagerContextMenu.SavedSearchButtonGlyphFontSize,
                Padding = resources.AxamlTaskManagerContextMenu.SavedSearchButtonPadding
            };
            EditableContextMenuEntryButton deleteButton = new(() => RequestDeleteSavedSearch(capturedSearchIndex))
            {
                Glyph = TaskManagerGlyphCatalog.CLOSE,
                ToolTip = LocalizationManager.Instance[nameof(AppStrings.SavedSearch_Delete)],
                DismissMenuOnClick = false,
                Size = resources.AxamlTaskManagerContextMenu.SavedSearchButtonSize,
                FontSize = resources.AxamlTaskManagerContextMenu.SavedSearchDeleteButtonFontSize,
                Padding = resources.AxamlTaskManagerContextMenu.SavedSearchButtonPadding
            };
            entries.Add(new EditableContextMenuEntry(savedSearch.Name, () => RunSavedSearch(savedSearch.Query))
            {
                SecondaryText = savedSearch.Query,
                SecondaryTextFontWeight =
                    (FontWeight)resources.AxamlTaskManagerContextMenu.SavedSearchQueryFontWeight,
                SecondaryTextOpacity = resources.AxamlTaskManagerContextMenu.SavedSearchQueryOpacity,
                PrimaryTextMaximumWidth =
                    resources.AxamlTaskManagerContextMenu.SavedSearchNameMaximumWidth,
                TextColumnSpacing = resources.AxamlTaskManagerContextMenu.SavedSearchColumnSpacing,
                LeadingContentSpacing =
                    resources.AxamlTaskManagerContextMenu.SavedSearchLeadingButtonTextSpacing,
                LeadingButton = renameButton,
                TrailingButton = deleteButton,
                InlineTextEdit =
                    new EditableContextMenuInlineTextEdit(name => RenameSavedSearch(capturedSearchIndex, name)),
                Node = ControlMap.SavedSearchMenu.SavedSearch.ID
            });
        }

        return entries;
    }

    private void Close()
    {
        EditableContextMenuWindow? menuWindow = _menuWindow;
        _menuWindow = null;
        DetachMenuOwner();
        if (menuWindow == null) return;

        menuWindow.Closed -= OnMenuClosed;
        menuWindow.Close();
    }

#if DEBUG
    /// <summary>Closes transient saved-search content so its next open uses reloaded metrics.</summary>
    private void OnAXAMLResourcesReloaded()
    {
        if (!_disposed) Close();
    }
#endif

    private void OnMenuClosed(object? sender, EventArgs eventArgs)
    {
        if (sender is not EditableContextMenuWindow menuWindow) return;

        menuWindow.Closed -= OnMenuClosed;
        if (ReferenceEquals(_menuWindow, menuWindow))
        {
            _menuWindow = null;
            DetachMenuOwner();
        }
    }

    private void AttachMenuOwner(Window owner)
    {
        DetachMenuOwner();
        _menuOwner = owner;
        owner.AddHandler(
            InputElement.PointerPressedEvent,
            OnMenuOwnerPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        owner.Deactivated += OnMenuOwnerDeactivated;
        owner.PositionChanged += OnMenuOwnerGeometryChanged;
        owner.Resized += OnMenuOwnerResized;
        _textBox.LayoutUpdated += OnSearchBoxLayoutUpdated;
    }

    private void DetachMenuOwner()
    {
        Window? owner = _menuOwner;
        if (owner == null) return;

        _menuOwner = null;
        owner.RemoveHandler(InputElement.PointerPressedEvent, OnMenuOwnerPointerPressed);
        owner.Deactivated -= OnMenuOwnerDeactivated;
        owner.PositionChanged -= OnMenuOwnerGeometryChanged;
        owner.Resized -= OnMenuOwnerResized;
        _textBox.LayoutUpdated -= OnSearchBoxLayoutUpdated;
        _menuRepositionPending = false;
    }

    private void OnMenuOwnerGeometryChanged(object? sender, PixelPointEventArgs eventArgs) =>
        ScheduleMenuReposition();

    private void OnMenuOwnerResized(object? sender, WindowResizedEventArgs eventArgs) =>
        ScheduleMenuReposition();

    private void OnSearchBoxLayoutUpdated(object? sender, EventArgs eventArgs) =>
        ScheduleMenuReposition();

    private void ScheduleMenuReposition()
    {
        EditableContextMenuWindow? menuWindow = _menuWindow;
        Window? owner = _menuOwner;
        if (_disposed || _menuRepositionPending || menuWindow == null || owner == null) return;

        _menuRepositionPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _menuRepositionPending = false;
            if (_disposed
                || !ReferenceEquals(_menuWindow, menuWindow)
                || !ReferenceEquals(_menuOwner, owner)
                || !menuWindow.IsVisible
                || TopLevel.GetTopLevel(_textBox) is not Window currentOwner
                || !ReferenceEquals(currentOwner, owner))
                return;

            double menuWidth = _textBox.Bounds.Width;
            if (double.IsFinite(menuWidth) && menuWidth > 0)
            {
                menuWindow.Width = menuWidth;
                menuWindow.MinWidth = menuWidth;
                menuWindow.MaxWidth = menuWidth;
            }

            menuWindow.RepositionOver(_textBox, _textBox, owner);
        }, DispatcherPriority.Render);
    }

    private void OnMenuOwnerPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        EditableContextMenuWindow? menuWindow = _menuWindow;
        if (menuWindow?.CommitInlineTextEdit(true) == true)
        {
            eventArgs.Handled = true;
            return;
        }

        bool pressedInsideSearchBox = IsInsideSearchBox(eventArgs.Source);
        Close();
        if (pressedInsideSearchBox) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !_textBox.IsKeyboardFocusWithin) return;

            _textBox.ClearSelection();
            TopLevel.GetTopLevel(_textBox)?.FocusManager.Focus(null);
        }, DispatcherPriority.Input);
    }

    private bool IsInsideSearchBox(object? source)
    {
        Visual? current = source as Visual;
        while (current != null)
        {
            if (ReferenceEquals(current, _textBox)) return true;
            current = current.GetVisualParent();
        }

        return false;
    }

    private void OnMenuOwnerDeactivated(object? sender, EventArgs eventArgs)
    {
        Window? owner = _menuOwner;
        EditableContextMenuWindow? menuWindow = _menuWindow;
        if (owner == null || menuWindow == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed
                || !ReferenceEquals(_menuOwner, owner)
                || !ReferenceEquals(_menuWindow, menuWindow)
                || owner.IsActive
                || menuWindow.IsActive)
                return;

            Close();
        }, DispatcherPriority.Input);
    }

    private void OnTextBoxLostFocus(object? sender, RoutedEventArgs eventArgs)
    {
        EditableContextMenuWindow? menuWindow = _menuWindow;
        if (menuWindow == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed
                || !ReferenceEquals(_menuWindow, menuWindow)
                || _textBox.IsKeyboardFocusWithin
                || menuWindow.IsActive)
                return;

            Close();
        }, DispatcherPriority.Input);
    }

    private void OnSaveClick(object? sender, EventArgs eventArgs)
    {
        if (_disposed) return;

        List<SavedSearch> updated = SavedSearchCollection.Add(
            _savedSearches,
            _textBox.Text);
        if (updated.Count == _savedSearches.Count) return;

        Persist(updated);
    }

    private void OnClearClick(object? sender, EventArgs eventArgs)
    {
        if (_disposed) return;

        Close();
        _textBox.Text = string.Empty;
        _textBox.CaretIndex = 0;
        _textBox.SelectionStart = 0;
        _textBox.SelectionEnd = 0;
        _textBox.Focus();
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && !HasQuery() && _textBox.IsKeyboardFocusWithin) Open();
        }, DispatcherPriority.Input);
    }

    private void RunSavedSearch(string query)
    {
        if (_disposed) return;

        _textBox.Text = query;
        int caretIndex = query.Length;
        _textBox.CaretIndex = caretIndex;
        _textBox.SelectionStart = caretIndex;
        _textBox.SelectionEnd = caretIndex;
        _textBox.Focus();
    }

    private void RequestDeleteSavedSearch(int searchIndex)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed) _ = DeleteSavedSearchAsync(searchIndex);
        }, DispatcherPriority.Input);
    }

    private async Task DeleteSavedSearchAsync(int searchIndex)
    {
        if (_disposed || (uint)searchIndex >= (uint)_savedSearches.Count) return;

        SavedSearch savedSearch = _savedSearches[searchIndex];
        if (SavedSearchCollection.UsesRegularExpression(savedSearch.Query))
        {
            if (_deleteConfirmationPending) return;

            _deleteConfirmationPending = true;
            try
            {
                bool confirmed = await _confirmRegexDeletion(savedSearch);
                if (_disposed || !confirmed) return;
            }
            catch (Exception exception)
            {
                TADNLog.Log($"Saved-search deletion confirmation failed: {exception}");
                return;
            }
            finally
            {
                _deleteConfirmationPending = false;
            }
        }

        if ((uint)searchIndex >= (uint)_savedSearches.Count) return;

        List<SavedSearch> updated = [.. _savedSearches];
        updated.RemoveAt(searchIndex);
        Persist(updated);
        _menuWindow?.ReplaceEntries(BuildEntries());
    }

    private string RenameSavedSearch(int searchIndex, string name)
    {
        if (_disposed || (uint)searchIndex >= (uint)_savedSearches.Count)
            return name.Trim();

        string previousName = _savedSearches[searchIndex].Name;
        List<SavedSearch> updated = SavedSearchCollection.Rename(
            _savedSearches,
            searchIndex,
            name);
        if (SavedSearchCollection.AreEquivalent(updated, _savedSearches))
            return previousName;

        Persist(updated);
        return (uint)searchIndex < (uint)_savedSearches.Count
            ? _savedSearches[searchIndex].Name
            : previousName;
    }

    private void Persist(IReadOnlyList<SavedSearch> searches)
    {
        List<SavedSearch> normalized = SavedSearchCollection.Normalize(searches);
        try
        {
            _savedSearchesChanged(normalized);
            _savedSearches = normalized;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Saved-search settings update failed: {exception}");
        }

        UpdateSaveButtonState();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _textBox.TextChanged -= OnTextChanged;
        _textBox.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        _textBox.KeyDown -= OnKeyDown;
        _textBox.LostFocus -= OnTextBoxLostFocus;
#if DEBUG
        if (_hotReloadAttached)
        {
            TaskManagerContextMenuResources.ResourcesReloaded -= OnAXAMLResourcesReloaded;
            _hotReloadAttached = false;
        }
#endif
        _clearButton.Click -= OnClearClick;
        _saveButton.Click -= OnSaveClick;
        _clearButton.Dispose();
        _saveButton.Dispose();
        Close();
    }
}
