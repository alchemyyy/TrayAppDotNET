using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Tray;

/// <summary>Describes an action button shown while an editable menu entry is hovered.</summary>
public sealed record TrayEditableMenuEntryButton(Action Click)
{
    public string? Text { get; init; }
    public Glyph? Glyph { get; init; }
    public string? ToolTip { get; init; }
    public bool DismissMenuOnClick { get; init; } = true;
    public double Size { get; init; } = 24;
    public double FontSize { get; init; } = 12;
    public Thickness Padding { get; init; }
}

/// <summary>Describes in-place editing for an editable menu entry's primary text.</summary>
public sealed record TrayEditableMenuInlineTextEdit(Func<string, string> Commit);

/// <summary>Describes a menu entry with secondary text and optional hover actions.</summary>
public sealed record TrayEditableMenuEntry(string Text, Action Click)
{
    public string? SecondaryText { get; init; }
    public FontWeight SecondaryTextFontWeight { get; init; } = FontWeight.Normal;
    public double SecondaryTextOpacity { get; init; } = 0.68;
    public double PrimaryTextMaximumWidth { get; init; } = double.PositiveInfinity;
    public double TextColumnSpacing { get; init; }
    public double LeadingContentSpacing { get; init; }
    public TrayEditableMenuEntryButton? LeadingButton { get; init; }
    public TrayEditableMenuEntryButton? TrailingButton { get; init; }
    public TrayEditableMenuInlineTextEdit? InlineTextEdit { get; init; }
    public Action<bool>? HoverChanged { get; init; }
    public bool IsEnabled { get; init; } = true;
}

/// <summary>Configures editable-menu behavior layered over the common tray-menu shell.</summary>
public sealed class TrayEditableMenuWindowOptions : TrayMenuWindowOptions
{
    public SettingsPaletteColor? ItemHoverColor { get; init; }
    public double DisabledItemOpacity { get; init; } = 0.68;
    public bool KeepOpenWhenOwnerActivated { get; init; }
}

/// <summary>Tray-menu variant with secondary labels, hover actions, and in-place text editing.</summary>
public sealed class TrayEditableMenuWindow : TrayMenuWindow
{
    private readonly TrayEditableMenuWindowOptions _options;
    private EditableMenuItemControl? _hoveredItem;
    private EditableMenuItemControl? _inlineEditingItem;
    private bool _suppressPendingDeactivationDismissal;
    private bool _consumeNextPointerRelease;
    private bool _closed;

    public TrayEditableMenuWindow(
        IReadOnlyList<TrayEditableMenuEntry> entries,
        TrayEditableMenuWindowOptions options)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        StackPanel items = new();
        UIResourceScope contentResources = new($"{GetType().Name}.Content");
        foreach (TrayEditableMenuEntry entry in entries)
        {
            EditableMenuItemControl item = contentResources.Own(new EditableMenuItemControl(
                entry,
                options,
                OnItemInvoked,
                OnItemHoverChanged,
                OnEntryButtonInvoked,
                OnInlineEditStateChanged));
            items.Children.Add(item);
        }

        InitializeMenuContent(items, contentResources);
        AddHandler(
            InputElement.PointerPressedEvent,
            OnWindowPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            InputElement.PointerReleasedEvent,
            OnWindowPointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    /// <summary>Commits the active entry edit without selecting an entry or closing the menu.</summary>
    public bool CommitInlineTextEdit(bool keepOpenAcrossPendingDeactivation = false)
    {
        EditableMenuItemControl? inlineEditingItem = _inlineEditingItem;
        if (inlineEditingItem == null) return false;

        if (keepOpenAcrossPendingDeactivation)
            _suppressPendingDeactivationDismissal = true;

        inlineEditingItem.CommitInlineTextEdit();
        return true;
    }

    private void OnItemInvoked(EditableMenuItemControl item, TrayEditableMenuEntry entry)
    {
        if (_closed || !entry.IsEnabled || item.IsInlineEditing) return;

        InvokeAndClose(entry.Click);
    }

    private void OnEntryButtonInvoked(TrayEditableMenuEntryButton button)
    {
        if (_closed) return;

        if (button.DismissMenuOnClick)
        {
            InvokeAndClose(button.Click);
            return;
        }

        button.Click();
    }

    private void OnItemHoverChanged(EditableMenuItemControl item, bool isHovered)
    {
        if (!isHovered)
        {
            if (ReferenceEquals(_hoveredItem, item))
                _hoveredItem = null;
            return;
        }

        EditableMenuItemControl? previousItem = _hoveredItem;
        _hoveredItem = item;
        if (previousItem != null && !ReferenceEquals(previousItem, item))
            previousItem.ClearPointerHover();
    }

    private void OnInlineEditStateChanged(EditableMenuItemControl item, bool isEditing)
    {
        if (!isEditing)
        {
            if (ReferenceEquals(_inlineEditingItem, item))
                _inlineEditingItem = null;
            return;
        }

        EditableMenuItemControl? previousItem = _inlineEditingItem;
        if (previousItem != null && !ReferenceEquals(previousItem, item))
            previousItem.CommitInlineTextEdit();

        _inlineEditingItem = item;
        Activate();
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (_closed
            || _inlineEditingItem == null
            || !eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Visual? source = eventArgs.Source as Visual;
        if (_inlineEditingItem.ContainsInlineEditor(source)) return;

        CommitInlineTextEdit();
        _consumeNextPointerRelease = true;
        eventArgs.Handled = true;
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        if (!_consumeNextPointerRelease || eventArgs.InitialPressMouseButton != MouseButton.Left) return;

        _consumeNextPointerRelease = false;
        eventArgs.Handled = true;
    }

    protected override bool ShouldDismissAfterDeactivation()
    {
        if (_suppressPendingDeactivationDismissal)
        {
            _suppressPendingDeactivationDismissal = false;
            return false;
        }

        if (_options.KeepOpenWhenOwnerActivated && Owner?.IsActive == true)
            return false;

        CommitInlineTextEdit();
        return true;
    }

    protected override void OnClosed(EventArgs eventArgs)
    {
        _closed = true;
        RemoveHandler(InputElement.PointerPressedEvent, OnWindowPointerPressed);
        RemoveHandler(InputElement.PointerReleasedEvent, OnWindowPointerReleased);
        _hoveredItem = null;
        _inlineEditingItem = null;
        base.OnClosed(eventArgs);
    }

    private sealed class EditableMenuItemControl : Border, IDisposable
    {
        private readonly TrayEditableMenuEntry _entry;
        private readonly TrayEditableMenuWindowOptions _options;
        private readonly Border _itemBorder;
        private readonly TextBlock _primaryLabel;
        private readonly SettingsButton? _leadingButton;
        private readonly SettingsButton? _trailingButton;
        private readonly TextBox? _inlineEditor;
        private readonly Action<EditableMenuItemControl, TrayEditableMenuEntry> _invoke;
        private readonly Action<EditableMenuItemControl, bool> _itemHoverChanged;
        private readonly Action<TrayEditableMenuEntryButton> _invokeButton;
        private readonly Action<EditableMenuItemControl, bool> _inlineEditStateChanged;
        private bool _isPointerOver;
        private bool _isInlineEditing;
        private bool _disposed;

        public EditableMenuItemControl(
            TrayEditableMenuEntry entry,
            TrayEditableMenuWindowOptions options,
            Action<EditableMenuItemControl, TrayEditableMenuEntry> invoke,
            Action<EditableMenuItemControl, bool> itemHoverChanged,
            Action<TrayEditableMenuEntryButton> invokeButton,
            Action<EditableMenuItemControl, bool> inlineEditStateChanged)
        {
            _entry = entry;
            _options = options;
            _invoke = invoke;
            _itemHoverChanged = itemHoverChanged;
            _invokeButton = invokeButton;
            _inlineEditStateChanged = inlineEditStateChanged;
            Background = Brushes.Transparent;
            Cursor = entry.IsEnabled ? TrayAppDotNETCursors.Hand : TrayAppDotNETCursors.Arrow;
            Focusable = entry.IsEnabled;
            Opacity = entry.IsEnabled ? 1 : Math.Clamp(options.DisabledItemOpacity, 0, 1);

            (_primaryLabel, _leadingButton, _trailingButton, _inlineEditor, Control content) =
                BuildContent(entry, options);
            _itemBorder = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = ResolveCornerRadius(options, options.ItemCornerRadius),
                Padding = options.ItemPadding,
                Margin = options.ItemMargin,
                MinHeight = double.IsFinite(options.ItemHeight) ? options.ItemHeight : 0,
                MinWidth = options.ItemMinWidth,
                Child = content
            };
            Child = _itemBorder;

            PointerEntered += OnPointerEntered;
            PointerExited += OnPointerExited;
            PointerPressed += OnPointerPressed;
            PointerReleased += OnPointerReleased;
            KeyDown += OnKeyDown;
            if (_leadingButton != null)
                _leadingButton.Click += OnLeadingButtonClick;
            if (_trailingButton != null)
                _trailingButton.Click += OnTrailingButtonClick;
            if (_inlineEditor != null)
            {
                _inlineEditor.KeyDown += OnInlineEditorKeyDown;
                _inlineEditor.LostFocus += OnInlineEditorLostFocus;
            }
        }

        public bool IsInlineEditing => _isInlineEditing;

        private static (
            TextBlock PrimaryLabel,
            SettingsButton? LeadingButton,
            SettingsButton? TrailingButton,
            TextBox? InlineEditor,
            Control Content) BuildContent(
                TrayEditableMenuEntry entry,
                TrayEditableMenuWindowOptions options)
        {
            TextBlock primaryLabel = TrayAppDotNETSettingsUI.Text(
                entry.Text,
                options.Palette,
                options.FontSize);
            primaryLabel.FontWeight = options.FontWeight;
            primaryLabel.VerticalAlignment = VerticalAlignment.Center;
            primaryLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            primaryLabel.MaxWidth = entry.PrimaryTextMaximumWidth;

            TextBlock secondaryLabel = TrayAppDotNETSettingsUI.Text(
                entry.SecondaryText ?? string.Empty,
                options.Palette,
                options.FontSize);
            secondaryLabel.FontWeight = entry.SecondaryTextFontWeight;
            secondaryLabel.VerticalAlignment = VerticalAlignment.Center;
            secondaryLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            secondaryLabel.Opacity = Math.Clamp(entry.SecondaryTextOpacity, 0, 1);
            secondaryLabel.IsVisible = !string.IsNullOrEmpty(entry.SecondaryText);

            TextBox? inlineEditor = CreateInlineEditor(entry, options);
            Grid primaryHost = new();
            primaryHost.Children.Add(primaryLabel);
            if (inlineEditor != null)
                primaryHost.Children.Add(inlineEditor);

            Grid textContent = new()
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(new GridLength(
                        string.IsNullOrEmpty(entry.SecondaryText) ? 0 : entry.TextColumnSpacing)),
                    new ColumnDefinition(GridLength.Star)
                }
            };
            Grid.SetColumn(primaryHost, 0);
            textContent.Children.Add(primaryHost);
            Grid.SetColumn(secondaryLabel, 2);
            textContent.Children.Add(secondaryLabel);

            SettingsButton? leadingButton = CreateEntryButton(entry.LeadingButton, options);
            SettingsButton? trailingButton = CreateEntryButton(entry.TrailingButton, options);
            Grid content = new()
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(leadingButton == null ? new GridLength(0) : GridLength.Auto),
                    new ColumnDefinition(new GridLength(
                        leadingButton == null ? 0 : entry.LeadingContentSpacing)),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(trailingButton == null ? new GridLength(0) : GridLength.Auto)
                }
            };

            if (leadingButton != null)
            {
                Grid.SetColumn(leadingButton, 0);
                content.Children.Add(leadingButton);
            }

            Grid.SetColumn(textContent, 2);
            content.Children.Add(textContent);
            if (trailingButton != null)
            {
                Grid.SetColumn(trailingButton, 3);
                content.Children.Add(trailingButton);
            }

            return (primaryLabel, leadingButton, trailingButton, inlineEditor, content);
        }

        private static TextBox? CreateInlineEditor(
            TrayEditableMenuEntry entry,
            TrayEditableMenuWindowOptions options)
        {
            if (entry.InlineTextEdit == null) return null;

            double editorWidth = double.IsFinite(entry.PrimaryTextMaximumWidth)
                ? Math.Max(1, entry.PrimaryTextMaximumWidth)
                : double.NaN;
            TextBox editor = TrayAppDotNETSettingsUI.SearchTextBox(
                options.Palette,
                editorWidth,
                entry.Text);
            editor.FontSize = options.FontSize;
            editor.FontWeight = options.FontWeight;
            editor.MinWidth = Math.Min(80, double.IsFinite(editorWidth) ? editorWidth : 80);
            editor.Height = double.NaN;
            editor.MinHeight = 0;
            editor.Padding = new Thickness(3, 0);
            editor.IsVisible = false;
            return editor;
        }

        private static SettingsButton? CreateEntryButton(
            TrayEditableMenuEntryButton? definition,
            TrayEditableMenuWindowOptions options)
        {
            if (definition == null) return null;

            SettingsButton button = definition.Glyph is { } glyph
                ? new SettingsButton(glyph, options.Palette, transparentBase: true)
                : new SettingsButton(definition.Text ?? string.Empty, options.Palette, transparentBase: true);
            button.Width = Math.Max(0, definition.Size);
            button.Height = Math.Max(0, definition.Size);
            button.MinHeight = Math.Max(0, definition.Size);
            button.Padding = definition.Padding;
            button.Label.FontSize = Math.Max(0, definition.FontSize);
            button.Opacity = 0;
            button.IsHitTestVisible = false;
            if (!string.IsNullOrWhiteSpace(definition.ToolTip))
                TrayAppDotNETToolTip.SetTip(button, definition.ToolTip);
            TrayAppDotNETToolTip.SuppressWhileEngaged(button);
            return button;
        }

        public void ClearPointerHover()
        {
            if (_disposed || !_isPointerOver) return;

            _isPointerOver = false;
            UpdateVisual();
            _itemHoverChanged(this, false);
            _entry.HoverChanged?.Invoke(false);
        }

        public bool ContainsInlineEditor(Visual? source)
        {
            TextBox? inlineEditor = _inlineEditor;
            if (inlineEditor == null || source == null) return false;

            Visual? current = source;
            while (current != null)
            {
                if (ReferenceEquals(current, inlineEditor)) return true;
                current = current.GetVisualParent();
            }

            return false;
        }

        public void CommitInlineTextEdit()
        {
            if (_disposed || !_isInlineEditing || _inlineEditor == null) return;

            string currentText = _inlineEditor.Text ?? string.Empty;
            string resolvedText = _primaryLabel.Text ?? string.Empty;
            try
            {
                if (_entry.InlineTextEdit is { } inlineTextEdit)
                    resolvedText = inlineTextEdit.Commit(currentText);
            }
            catch (Exception exception)
            {
                TADNLog.Log($"Editable tray-menu commit failed: {exception}");
            }

            _primaryLabel.Text = resolvedText;
            _inlineEditor.Text = resolvedText;
            EndInlineTextEdit();
        }

        private void BeginInlineTextEdit()
        {
            if (_disposed || _isInlineEditing || _inlineEditor == null) return;

            _inlineEditStateChanged(this, true);
            _isInlineEditing = true;
            _primaryLabel.IsVisible = false;
            _inlineEditor.Text = _primaryLabel.Text ?? string.Empty;
            _inlineEditor.IsVisible = true;
            SetActionButtonsVisible(false);
            _inlineEditor.Focus();
            _inlineEditor.SelectAll();
        }

        private void CancelInlineTextEdit()
        {
            if (_disposed || !_isInlineEditing || _inlineEditor == null) return;

            _inlineEditor.Text = _primaryLabel.Text ?? string.Empty;
            EndInlineTextEdit();
        }

        private void EndInlineTextEdit()
        {
            if (!_isInlineEditing || _inlineEditor == null) return;

            _isInlineEditing = false;
            _inlineEditor.IsVisible = false;
            _primaryLabel.IsVisible = true;
            SetActionButtonsVisible(_isPointerOver);
            _inlineEditStateChanged(this, false);
            Focus();
        }

        private void UpdateVisual()
        {
            SettingsPaletteColor hoverColor = _options.ItemHoverColor ?? _options.Palette.Hover;
            _itemBorder.Background = _isPointerOver
                ? TrayAppDotNETSettingsUI.Brush(hoverColor)
                : Brushes.Transparent;
            SetActionButtonsVisible(_isPointerOver && !_isInlineEditing);
        }

        private void SetActionButtonsVisible(bool isVisible)
        {
            SetActionButtonVisible(_leadingButton, isVisible);
            SetActionButtonVisible(_trailingButton, isVisible);
        }

        private static void SetActionButtonVisible(SettingsButton? button, bool isVisible)
        {
            if (button == null) return;

            button.Opacity = isVisible ? 1 : 0;
            button.IsHitTestVisible = isVisible;
        }

        private void OnPointerEntered(object? sender, PointerEventArgs eventArgs)
        {
            if (_disposed || !_entry.IsEnabled || _isPointerOver) return;

            _isPointerOver = true;
            UpdateVisual();
            _itemHoverChanged(this, true);
            _entry.HoverChanged?.Invoke(true);
        }

        private void OnPointerExited(object? sender, PointerEventArgs eventArgs)
        {
            if (_disposed
                || !_entry.IsEnabled
                || !_isPointerOver
                || TrayAppDotNETFlyoutUI.IsPointerInside(this, eventArgs))
            {
                return;
            }

            ClearPointerHover();
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
        {
            if (_disposed
                || _isInlineEditing
                || !_entry.IsEnabled
                || !eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                return;
            }

            if (!_options.InvokeOnPointerReleased)
                _invoke(this, _entry);
            eventArgs.Handled = true;
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
        {
            if (_disposed
                || _isInlineEditing
                || !_entry.IsEnabled
                || !_options.InvokeOnPointerReleased
                || eventArgs.InitialPressMouseButton != MouseButton.Left)
            {
                return;
            }

            if (_isPointerOver)
                _invoke(this, _entry);
            eventArgs.Handled = true;
        }

        private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
        {
            if (_disposed || _isInlineEditing || !_entry.IsEnabled) return;
            if (eventArgs.Key is not (Key.Enter or Key.Space)) return;

            _invoke(this, _entry);
            eventArgs.Handled = true;
        }

        private void OnInlineEditorKeyDown(object? sender, KeyEventArgs eventArgs)
        {
            switch (eventArgs.Key)
            {
                case Key.Enter:
                    CommitInlineTextEdit();
                    eventArgs.Handled = true;
                    break;
                case Key.Escape:
                    CancelInlineTextEdit();
                    eventArgs.Handled = true;
                    break;
            }
        }

        private void OnInlineEditorLostFocus(object? sender, RoutedEventArgs eventArgs)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed && _isInlineEditing && _inlineEditor?.IsKeyboardFocusWithin != true)
                    CommitInlineTextEdit();
            }, DispatcherPriority.Input);
        }

        private void OnLeadingButtonClick(object? sender, EventArgs eventArgs)
        {
            if (_entry.InlineTextEdit != null)
            {
                BeginInlineTextEdit();
                return;
            }

            if (_entry.LeadingButton is { } leadingButton)
                _invokeButton(leadingButton);
        }

        private void OnTrailingButtonClick(object? sender, EventArgs eventArgs)
        {
            if (_entry.TrailingButton is { } trailingButton)
                _invokeButton(trailingButton);
        }

        public void Dispose()
        {
            if (_disposed) return;

            if (_isInlineEditing)
                _inlineEditStateChanged(this, false);
            _disposed = true;
            if (_isPointerOver)
            {
                _itemHoverChanged(this, false);
                _entry.HoverChanged?.Invoke(false);
            }

            _isPointerOver = false;
            _isInlineEditing = false;
            PointerEntered -= OnPointerEntered;
            PointerExited -= OnPointerExited;
            PointerPressed -= OnPointerPressed;
            PointerReleased -= OnPointerReleased;
            KeyDown -= OnKeyDown;
            if (_inlineEditor != null)
            {
                _inlineEditor.KeyDown -= OnInlineEditorKeyDown;
                _inlineEditor.LostFocus -= OnInlineEditorLostFocus;
            }
            if (_leadingButton != null)
                _leadingButton.Click -= OnLeadingButtonClick;
            if (_trailingButton != null)
                _trailingButton.Click -= OnTrailingButtonClick;
            Cursor = null;
            Child = null;
        }

        private static CornerRadius ResolveCornerRadius(
            TrayEditableMenuWindowOptions options,
            CornerRadius roundedRadius) =>
            options.Rounded ? roundedRadius : new CornerRadius(0);
    }
}
