using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.Visuals;
using TaskManagerGlyphCatalog = TaskManagerTrayAppDotNET.Visuals.GlyphCatalog;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Hosts the saved-search menu anchored directly beneath the Processes search box.</summary>
internal sealed class ProcessSavedSearchController : IDisposable
{
    private readonly TextBox _textBox;
    private readonly SettingsPalette _palette;
    private readonly bool _enableRoundedCorners;
    private readonly ITrayAppDotNETTrayMenuSettings _trayMenuSettings;
    private readonly Action<IReadOnlyList<ProcessSavedSearch>> _savedSearchesChanged;
    private readonly Func<ProcessSavedSearch, Task<bool>> _confirmRegexDeletion;
    private readonly InsetGlyphButton _clearButton;
    private readonly InsetGlyphButton _saveButton;
    private List<ProcessSavedSearch> _savedSearches;
    private EditableContextMenuWindow? _menuWindow;
    private Window? _menuOwner;
    private bool _deleteConfirmationPending;
    private bool _disposed;

    public ProcessSavedSearchController(
        TextBox textBox,
        IReadOnlyList<ProcessSavedSearch> savedSearches,
        SettingsPalette palette,
        TaskManagerWindowResources windowResources,
        bool enableRoundedCorners,
        ITrayAppDotNETTrayMenuSettings trayMenuSettings,
        Action<IReadOnlyList<ProcessSavedSearch>> savedSearchesChanged,
        Func<ProcessSavedSearch, Task<bool>> confirmRegexDeletion)
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
        _savedSearches = ProcessSavedSearchCollection.Normalize(savedSearches);

        double actionButtonSize = textBox.Height;
        _clearButton = new InsetGlyphButton(
            TaskManagerGlyphCatalog.CLOSE,
            palette,
            actionButtonSize,
            windowResources.AxamlTaskManagerDetails.SearchActionGlyphFontSize,
            windowResources.AxamlTaskManagerDetails.SearchActionVisualInset,
            windowResources.AxamlTaskManagerDetails.SearchActionVisualCornerRadius,
            windowResources.AxamlTaskManagerDetails.SearchActionButtonPadding,
            windowResources.AxamlTaskManagerDetails.SearchClearGlyphOpacity)
        {
            IsVisible = HasQuery()
        };
        _clearButton.Click += OnClearClick;
        TrayAppDotNETToolTip.SetTip(_clearButton, "Clear search");
        TrayAppDotNETToolTip.SuppressWhileEngaged(_clearButton);

        _saveButton = new InsetGlyphButton(
            TaskManagerGlyphCatalog.SAVE,
            palette,
            actionButtonSize,
            windowResources.AxamlTaskManagerDetails.SearchActionGlyphFontSize,
            windowResources.AxamlTaskManagerDetails.SearchActionVisualInset,
            windowResources.AxamlTaskManagerDetails.SearchActionVisualCornerRadius,
            windowResources.AxamlTaskManagerDetails.SearchActionButtonPadding)
        {
            IsVisible = HasQuery()
        };
        _saveButton.Click += OnSaveClick;
        TrayAppDotNETToolTip.SetTip(_saveButton, "Save search");
        TrayAppDotNETToolTip.SuppressWhileEngaged(_saveButton);

        _textBox.TextChanged += OnTextChanged;
        _textBox.AddHandler(
            InputElement.PointerPressedEvent,
            OnPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        _textBox.KeyDown += OnKeyDown;
        _textBox.LostFocus += OnTextBoxLostFocus;
    }

    public Control ClearButton => _clearButton;

    public Control SaveButton => _saveButton;

    private bool HasQuery() => !string.IsNullOrWhiteSpace(_textBox.Text);

    private void OnTextChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        bool hasQuery = HasQuery();
        _clearButton.IsVisible = hasQuery;
        _saveButton.IsVisible = hasQuery;
        if (hasQuery) Close();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (_disposed
            || !eventArgs.GetCurrentPoint(_textBox).Properties.IsLeftButtonPressed
            || HasQuery())
        {
            return;
        }

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
            TaskManagerContextMenuWindow.CreateSavedSearchMenu(
                BuildEntries(),
                _palette,
                _enableRoundedCorners,
                _trayMenuSettings);
        double configuredWidth = double.IsFinite(_textBox.Width) ? _textBox.Width : 1;
        double menuWidth = _textBox.Bounds.Width > 0
            ? _textBox.Bounds.Width
            : Math.Max(1, configuredWidth);
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

    private IReadOnlyList<EditableContextMenuEntry> BuildEntries()
    {
        List<EditableContextMenuEntry> entries = [];
        if (_savedSearches.Count == 0)
        {
            entries.Add(new EditableContextMenuEntry("No saved searches", static () => { })
            {
                IsEnabled = false
            });
            return entries;
        }

        TaskManagerContextMenuResources resources = TaskManagerContextMenuResources.Current;
        for (int searchIndex = 0; searchIndex < _savedSearches.Count; searchIndex++)
        {
            ProcessSavedSearch savedSearch = _savedSearches[searchIndex];
            int capturedSearchIndex = searchIndex;
            EditableContextMenuEntryButton renameButton = new(static () => { })
            {
                Glyph = TaskManagerGlyphCatalog.MORE,
                ToolTip = "Rename saved search",
                DismissMenuOnClick = false,
                Size = resources.AxamlTaskManagerContextMenu.SavedSearchButtonSize,
                FontSize = resources.AxamlTaskManagerContextMenu.SavedSearchButtonGlyphFontSize,
                Padding = resources.AxamlTaskManagerContextMenu.SavedSearchButtonPadding
            };
            EditableContextMenuEntryButton deleteButton = new(() => RequestDeleteSavedSearch(capturedSearchIndex))
            {
                Text = "x",
                ToolTip = "Delete saved search",
                Size = resources.AxamlTaskManagerContextMenu.SavedSearchButtonSize,
                FontSize = resources.AxamlTaskManagerContextMenu.SavedSearchDeleteButtonFontSize,
                Padding = resources.AxamlTaskManagerContextMenu.SavedSearchButtonPadding
            };
            entries.Add(new EditableContextMenuEntry(savedSearch.Name, () => RunSavedSearch(savedSearch.Query))
            {
                SecondaryText = savedSearch.Query,
                SecondaryTextFontWeight = FontWeight.Light,
                SecondaryTextOpacity = resources.AxamlTaskManagerContextMenu.SavedSearchQueryOpacity,
                PrimaryTextMaximumWidth =
                    resources.AxamlTaskManagerContextMenu.SavedSearchNameMaximumWidth,
                TextColumnSpacing = resources.AxamlTaskManagerContextMenu.SavedSearchColumnSpacing,
                LeadingContentSpacing =
                    resources.AxamlTaskManagerContextMenu.SavedSearchLeadingButtonTextSpacing,
                LeadingButton = renameButton,
                TrailingButton = deleteButton,
                InlineTextEdit = new EditableContextMenuInlineTextEdit(
                    name => RenameSavedSearch(capturedSearchIndex, name))
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
    }

    private void DetachMenuOwner()
    {
        Window? owner = _menuOwner;
        if (owner == null) return;

        _menuOwner = null;
        owner.RemoveHandler(InputElement.PointerPressedEvent, OnMenuOwnerPointerPressed);
        owner.Deactivated -= OnMenuOwnerDeactivated;
    }

    private void OnMenuOwnerPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        EditableContextMenuWindow? menuWindow = _menuWindow;
        if (menuWindow?.CommitInlineTextEdit(keepOpenAcrossPendingDeactivation: true) == true)
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
            TopLevel.GetTopLevel(_textBox)?.FocusManager?.Focus(null);
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
            {
                return;
            }

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
            {
                return;
            }

            Close();
        }, DispatcherPriority.Input);
    }

    private void OnSaveClick(object? sender, EventArgs eventArgs)
    {
        if (_disposed) return;

        List<ProcessSavedSearch> updated = ProcessSavedSearchCollection.Add(
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

        ProcessSavedSearch savedSearch = _savedSearches[searchIndex];
        bool usesRegularExpression =
            ProcessSavedSearchCollection.UsesRegularExpression(savedSearch.Query);
        if (usesRegularExpression)
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

        List<ProcessSavedSearch> updated = [.. _savedSearches];
        updated.RemoveAt(searchIndex);
        Persist(updated);
    }

    private string RenameSavedSearch(int searchIndex, string name)
    {
        if (_disposed || (uint)searchIndex >= (uint)_savedSearches.Count)
            return name.Trim();

        string previousName = _savedSearches[searchIndex].Name;
        List<ProcessSavedSearch> updated = ProcessSavedSearchCollection.Rename(
            _savedSearches,
            searchIndex,
            name);
        if (ProcessSavedSearchCollection.AreEquivalent(updated, _savedSearches))
            return previousName;

        Persist(updated);
        return (uint)searchIndex < (uint)_savedSearches.Count
            ? _savedSearches[searchIndex].Name
            : previousName;
    }

    private void Persist(IReadOnlyList<ProcessSavedSearch> searches)
    {
        List<ProcessSavedSearch> normalized = ProcessSavedSearchCollection.Normalize(searches);
        try
        {
            _savedSearchesChanged(normalized);
            _savedSearches = normalized;
        }
        catch (Exception exception)
        {
            TADNLog.Log($"Saved-search settings update failed: {exception}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _textBox.TextChanged -= OnTextChanged;
        _textBox.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        _textBox.KeyDown -= OnKeyDown;
        _textBox.LostFocus -= OnTextBoxLostFocus;
        _clearButton.Click -= OnClearClick;
        _saveButton.Click -= OnSaveClick;
        _clearButton.Dispose();
        _saveButton.Dispose();
        Close();
    }

    /// <summary>Keeps a full-size invisible hit target around a smaller visual button surface.</summary>
    internal sealed class InsetGlyphButton : Border, IDisposable
    {
        private readonly SettingsPalette _palette;
        private readonly Border _surface;
        private bool _isPointerOver;
        private bool _isPressed;
        private bool _disposed;

        public InsetGlyphButton(
            Glyph glyph,
            SettingsPalette palette,
            double hitTargetSize,
            double glyphFontSize,
            double visualInset,
            CornerRadius cornerRadius,
            Thickness visualPadding,
            double glyphOpacity = 1)
        {
            ArgumentNullException.ThrowIfNull(glyph);
            ArgumentNullException.ThrowIfNull(palette);

            _palette = palette;
            double normalizedHitTargetSize = Math.Max(0, hitTargetSize);
            double normalizedInset = Math.Clamp(
                visualInset,
                0,
                normalizedHitTargetSize / 2);
            Width = normalizedHitTargetSize;
            Height = normalizedHitTargetSize;
            MinHeight = normalizedHitTargetSize;
            Background = Brushes.Transparent;
            Cursor = TrayAppDotNETCursors.Hand;
            Focusable = true;

            TextBlock glyphText = TrayAppDotNETSettingsUI.Text(
                string.Empty,
                palette,
                Math.Max(0, glyphFontSize));
            GlyphApplicator.ApplyTo(glyphText, glyph);
            glyphText.HorizontalAlignment = HorizontalAlignment.Center;
            glyphText.VerticalAlignment = VerticalAlignment.Center;
            glyphText.IsHitTestVisible = false;
            glyphText.Opacity = Math.Clamp(glyphOpacity, 0, 1);

            _surface = new Border
            {
                Margin = new Thickness(normalizedInset),
                Padding = visualPadding,
                CornerRadius = cornerRadius,
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                Child = glyphText
            };
            Child = _surface;

            PointerEntered += OnPointerEntered;
            PointerExited += OnPointerExited;
            PointerPressed += OnPointerPressed;
            PointerReleased += OnPointerReleased;
            KeyDown += OnKeyDown;
        }

        public event EventHandler? Click;

        private void OnPointerEntered(object? sender, PointerEventArgs eventArgs)
        {
            if (_disposed) return;

            _isPointerOver = true;
            UpdateVisual();
        }

        private void OnPointerExited(object? sender, PointerEventArgs eventArgs)
        {
            if (_disposed) return;

            _isPointerOver = false;
            _isPressed = false;
            UpdateVisual();
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
        {
            if (_disposed || !IsEnabled
                || !eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            _isPressed = true;
            UpdateVisual();
            eventArgs.Handled = true;
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
        {
            if (_disposed || !IsEnabled || eventArgs.InitialPressMouseButton != MouseButton.Left)
                return;

            bool releasedInside = TrayAppDotNETFlyoutUI.IsPointerInside(this, eventArgs);
            bool clicked = _isPressed && releasedInside;
            _isPointerOver = releasedInside;
            _isPressed = false;
            UpdateVisual();
            if (!clicked) return;

            Click?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
        }

        private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
        {
            if (_disposed || !IsEnabled || eventArgs.Key is not (Key.Enter or Key.Space)) return;

            Click?.Invoke(this, EventArgs.Empty);
            eventArgs.Handled = true;
        }

        private void UpdateVisual()
        {
            _surface.Background = _isPressed
                ? TrayAppDotNETSettingsUI.Brush(_palette.Pressed)
                : _isPointerOver
                    ? TrayAppDotNETSettingsUI.Brush(_palette.Hover)
                    : Brushes.Transparent;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            PointerEntered -= OnPointerEntered;
            PointerExited -= OnPointerExited;
            PointerPressed -= OnPointerPressed;
            PointerReleased -= OnPointerReleased;
            KeyDown -= OnKeyDown;
            Click = null;
            Cursor = null;
            Child = null;
        }
    }
}
