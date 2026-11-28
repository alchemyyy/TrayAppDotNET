using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.UI.ControlMapping;

namespace TaskManagerTrayAppDotNET.UI;

internal readonly record struct SearchColumnToken(
    int OpeningBraceIndex,
    int ClosingBraceIndex,
    string Fragment);

/// <summary>One completion row; <paramref name="Column"/> is a catalog column ID.</summary>
internal readonly record struct SearchColumnSuggestion(
    int Column,
    string ColumnName,
    string DisplayText);

/// <summary>Finds, ranks, and completes column tokens independently of the popup UI.</summary>
internal static class SearchAutocompleteLogic
{
    /// <summary>Finds the brace-delimited column token containing the keyboard caret.</summary>
    public static bool TryGetColumnToken(
        string? text,
        int caretIndex,
        out SearchColumnToken token)
    {
        string queryText = text ?? string.Empty;
        int boundedCaretIndex = Math.Clamp(caretIndex, min: 0, queryText.Length);
        int openingBraceIndex = -1;
        for (int characterIndex = boundedCaretIndex - 1; characterIndex >= 0; characterIndex--)
        {
            switch (queryText[characterIndex])
            {
                case '}':
                    token = default;
                    return false;
                case '{':
                    openingBraceIndex = characterIndex;
                    characterIndex = -1;
                    break;
            }
        }

        if (openingBraceIndex < 0 || IsInsideQuotedValue(queryText, openingBraceIndex))
        {
            token = default;
            return false;
        }

        int closingBraceIndex = queryText.IndexOf(value: '}', openingBraceIndex + 1);
        if (closingBraceIndex >= 0 && boundedCaretIndex > closingBraceIndex)
        {
            token = default;
            return false;
        }

        string fragment = queryText[(openingBraceIndex + 1)..boundedCaretIndex].Trim();
        token = new SearchColumnToken(openingBraceIndex, closingBraceIndex, fragment);
        return true;
    }

    /// <summary>Ranks catalog columns using their title, alternate name, and optional nickname.</summary>
    public static SearchColumnSuggestion[] RankSuggestions(
        string fragment,
        SearchColumnCatalog catalog,
        int maximumSuggestionCount)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (maximumSuggestionCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSuggestionCount));

        List<RankedSuggestion> rankedSuggestions = [];
        IReadOnlyList<SearchColumn> columns = catalog.Columns;
        for (int catalogIndex = 0; catalogIndex < columns.Count; catalogIndex++)
        {
            SearchColumn column = columns[catalogIndex];
            string nickname = column.Nickname;
            string searchText = string.Concat(
                column.Title,
                " ",
                column.AlternateName,
                " ",
                nickname);
            SearchMatch combinedMatch = SearchMatcher.Score(searchText, fragment);
            SearchMatch titleMatch = SearchMatcher.Score(column.Title, fragment);
            SearchMatch alternateMatch = SearchMatcher.Score(column.AlternateName, fragment);
            SearchMatch nicknameMatch = SearchMatcher.Score(nickname, fragment);
            int score = Math.Max(
                Math.Max(combinedMatch.Score, titleMatch.Score),
                Math.Max(alternateMatch.Score, nicknameMatch.Score));
            if (score == int.MinValue) continue;

            string displayText = nickname.Length == 0
                ? column.Title
                : string.Concat(column.Title, str1: " (", nickname, str3: ")");
            SearchColumnSuggestion suggestion = new(
                column.ID,
                column.Title,
                displayText);
            rankedSuggestions.Add(new RankedSuggestion(suggestion, score, catalogIndex));
        }

        rankedSuggestions.Sort(static (left, right) =>
        {
            int scoreComparison = right.Score.CompareTo(left.Score);
            return scoreComparison != 0 ? scoreComparison : left.CatalogIndex.CompareTo(right.CatalogIndex);
        });

        int resultCount = Math.Min(maximumSuggestionCount, rankedSuggestions.Count);
        SearchColumnSuggestion[] result = new SearchColumnSuggestion[resultCount];
        for (int resultIndex = 0; resultIndex < resultCount; resultIndex++)
            result[resultIndex] = rankedSuggestions[resultIndex].Suggestion;
        return result;
    }

    /// <summary>Replaces the active token and returns a caret immediately after its closing brace.</summary>
    public static bool TryComplete(
        string? text,
        int caretIndex,
        SearchColumnSuggestion suggestion,
        out string completedText,
        out int completedCaretIndex)
    {
        string queryText = text ?? string.Empty;
        if (!TryGetColumnToken(queryText, caretIndex, out SearchColumnToken token))
        {
            completedText = queryText;
            completedCaretIndex = Math.Clamp(caretIndex, min: 0, queryText.Length);
            return false;
        }

        int replacementEndIndex = token.ClosingBraceIndex >= 0
            ? token.ClosingBraceIndex + 1
            : FindIncompleteTokenEnd(queryText, Math.Clamp(caretIndex, min: 0, queryText.Length));
        StringBuilder builder = new(
            queryText.Length + suggestion.ColumnName.Length - (replacementEndIndex - token.OpeningBraceIndex));
        builder.Append(queryText, startIndex: 0, token.OpeningBraceIndex);
        builder.Append('{');
        builder.Append(suggestion.ColumnName);
        builder.Append('}');
        builder.Append(queryText, replacementEndIndex, queryText.Length - replacementEndIndex);
        completedText = builder.ToString();
        completedCaretIndex = token.OpeningBraceIndex + suggestion.ColumnName.Length + 2;
        return true;
    }

    private static bool IsInsideQuotedValue(string text, int characterIndex)
    {
        char activeQuote = '\0';
        bool escaped = false;
        for (int index = 0; index < characterIndex; index++)
        {
            char current = text[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (activeQuote != '\0' && current == '\\')
            {
                escaped = true;
                continue;
            }

            if (current is not ('\'' or '"')) continue;
            if (activeQuote == '\0')
                activeQuote = current;
            else if (activeQuote == current)
                activeQuote = '\0';
        }

        return activeQuote != '\0';
    }

    private static int FindIncompleteTokenEnd(string text, int caretIndex)
    {
        int characterIndex = caretIndex;
        while (characterIndex < text.Length)
        {
            char current = text[characterIndex];
            if (current is '=' or '<' or '>' or '!' or '&' or '|' or '(' or ')' or '{' or '}')
                break;
            characterIndex++;
        }

        return characterIndex;
    }

    private readonly record struct RankedSuggestion(
        SearchColumnSuggestion Suggestion,
        int Score,
        int CatalogIndex);
}

/// <summary>Shows a focus-preserving TADN column-completion menu beneath a search box.</summary>
internal sealed class SearchAutocompleteController : IDisposable
{
    private readonly TextBox _textBox;
    private readonly SettingsPalette _palette;
    private readonly bool _enableRoundedCorners;
    private readonly StackPanel _itemsPanel;
    private readonly Border _popupBorder;
    private readonly List<Border> _itemBorders = [];
    private readonly ControlMapNodeID _suggestionNode;
    private SearchColumnCatalog _catalog;
    private SearchColumnSuggestion[] _suggestions = [];
    private int _maximumSuggestionCount = 1;
    private int _selectedIndex;
    private bool _updateScheduled;
    private bool _suppressUpdate;
    private bool _completedOnEnterDown;
#if DEBUG
    private bool _hotReloadAttached;
#endif
    private bool _disposed;

    public SearchAutocompleteController(
        TextBox textBox,
        SearchColumnCatalog catalog,
        ControlMapNodeID suggestionNode,
        SettingsPalette palette,
        bool enableRoundedCorners)
    {
        ArgumentNullException.ThrowIfNull(textBox);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(palette);

        _textBox = textBox;
        _catalog = catalog;
        _suggestionNode = suggestionNode;
        _palette = palette;
        _enableRoundedCorners = enableRoundedCorners;
        _itemsPanel = new StackPanel();

        _popupBorder = new Border
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(palette.Border),
            Child = _itemsPanel
        };
        Popup = new Popup
        {
            PlacementTarget = textBox,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = false,
            Focusable = false,
            Child = _popupBorder
        };
        ApplyAXAMLResources();

        _textBox.TextChanged += OnTextChanged;
        _textBox.KeyDown += OnKeyDown;
        _textBox.AddHandler(InputElement.KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
        _textBox.PointerReleased += OnPointerReleased;
        _textBox.PropertyChanged += OnTextBoxPropertyChanged;
        _textBox.LostFocus += OnLostFocus;
    }

    public Popup Popup { get; }

#if DEBUG
    /// <summary>Attaches process-wide reload notifications after the owning page is fully built.</summary>
    internal void AttachAXAMLHotReload()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hotReloadAttached) return;

        TaskManagerContextMenuResources.ResourcesReloaded += OnAXAMLResourcesReloaded;
        _hotReloadAttached = true;
    }
#endif

    /// <summary>Refreshes column names and nicknames after the page's columns change.</summary>
    public void SetCatalog(SearchColumnCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
        ScheduleUpdate();
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        if (!_suppressUpdate) ScheduleUpdate();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs) => ScheduleUpdate();

    private void OnTextBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.Property == TextBox.CaretIndexProperty) ScheduleUpdate();
    }

    private void OnLostFocus(object? sender, RoutedEventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && !_textBox.IsKeyboardFocusWithin) Close();
        });
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (_disposed) return;

        switch (eventArgs.Key)
        {
            case Key.Down when Popup.IsOpen && _suggestions.Length > 0:
                _selectedIndex = Math.Min(_selectedIndex + 1, _suggestions.Length - 1);
                UpdateItemVisuals();
                eventArgs.Handled = true;
                break;
            case Key.Up when Popup.IsOpen && _suggestions.Length > 0:
                _selectedIndex = Math.Max(_selectedIndex - 1, val2: 0);
                UpdateItemVisuals();
                eventArgs.Handled = true;
                break;
            case Key.Enter or Key.Tab when Popup.IsOpen && _suggestions.Length > 0:
                CompleteSelectedSuggestion();
                _completedOnEnterDown = eventArgs.Key == Key.Enter;
                eventArgs.Handled = true;
                break;
            case Key.Escape when Popup.IsOpen:
                Close();
                eventArgs.Handled = true;
                break;
        }
    }

    // Runs before the box's own key-up handlers, so an Enter that completed a token does not also commit the box
    private void OnPreviewKeyUp(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Enter || !_completedOnEnterDown) return;

        _completedOnEnterDown = false;
        eventArgs.Handled = true;
    }

    private void ScheduleUpdate()
    {
        if (_disposed || _suppressUpdate || _updateScheduled) return;

        _updateScheduled = true;
        Dispatcher.UIThread.Post(UpdateSuggestions, DispatcherPriority.Input);
    }

    private void UpdateSuggestions()
    {
        _updateScheduled = false;
        if (_disposed || !_textBox.IsKeyboardFocusWithin)
        {
            Close();
            return;
        }

        string queryText = _textBox.Text ?? string.Empty;
        if (!SearchAutocompleteLogic.TryGetColumnToken(
                queryText,
                _textBox.CaretIndex,
                out SearchColumnToken token))
        {
            Close();
            return;
        }

        SearchColumnSuggestion[] nextSuggestions =
            SearchAutocompleteLogic.RankSuggestions(
                token.Fragment,
                _catalog,
                _maximumSuggestionCount);
        if (nextSuggestions.Length == 0)
        {
            Close();
            return;
        }

        int? previouslySelectedColumn =
            (uint)_selectedIndex < (uint)_suggestions.Length
                ? _suggestions[_selectedIndex].Column
                : null;
        _suggestions = nextSuggestions;
        _selectedIndex = 0;
        if (previouslySelectedColumn.HasValue)
        {
            for (int suggestionIndex = 0;
                 suggestionIndex < _suggestions.Length;
                 suggestionIndex++)
            {
                if (_suggestions[suggestionIndex].Column != previouslySelectedColumn.Value) continue;
                _selectedIndex = suggestionIndex;
                break;
            }
        }

        RebuildItems();
        PositionPopupAtCaret();
        Popup.IsOpen = true;
    }

    private void RebuildItems()
    {
        _itemsPanel.Children.Clear();
        _itemBorders.Clear();
        TaskManagerContextMenuResources resources = TaskManagerContextMenuResources.Current;
        for (int suggestionIndex = 0; suggestionIndex < _suggestions.Length; suggestionIndex++)
        {
            SearchColumnSuggestion suggestion = _suggestions[suggestionIndex];
            TextBlock label = TrayAppDotNETSettingsUI.Text(
                suggestion.DisplayText,
                _palette,
                resources.AxamlTaskManagerContextMenu.AutocompleteFontSize);
            label.FontWeight = (FontWeight)resources.AxamlTaskManagerContextMenu.FontWeight;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.IsHitTestVisible = false;

            Border itemBorder = new()
            {
                Height = resources.AxamlTaskManagerContextMenu.AutocompleteItemHeight,
                Padding = resources.AxamlTaskManagerContextMenu.AutocompleteItemPadding,
                CornerRadius = _enableRoundedCorners
                    ? resources.AxamlTaskManagerContextMenu.AutocompleteItemCornerRadius
                    : default,
                Background = Brushes.Transparent,
                Focusable = false,
                Tag = suggestion,
                Child = label
            };
            itemBorder.PointerEntered += OnSuggestionPointerEntered;
            itemBorder.PointerPressed += OnSuggestionPointerPressed;
            itemBorder.MapTo(_suggestionNode);
            itemBorder.MapActivation(_ => CompleteSuggestion(itemBorder));
            _itemBorders.Add(itemBorder);
            _itemsPanel.Children.Add(itemBorder);
        }

        UpdateItemVisuals();
    }

#if DEBUG
    /// <summary>Reapplies popup chrome and live suggestion rows after context-menu AXAML reloads.</summary>
    private void OnAXAMLResourcesReloaded()
    {
        if (_disposed) return;

        ApplyAXAMLResources();
        UpdateSuggestions();
        if (Popup.IsOpen) PositionPopupAtCaret();
    }
#endif

    private void ApplyAXAMLResources()
    {
        TaskManagerContextMenuResources resources = TaskManagerContextMenuResources.Current;
        _popupBorder.Width = resources.AxamlTaskManagerContextMenu.AutocompleteWidth;
        _popupBorder.BorderThickness =
            resources.AxamlTaskManagerContextMenu.AutocompleteBorderThickness;
        _popupBorder.CornerRadius = _enableRoundedCorners
            ? resources.AxamlTaskManagerContextMenu.AutocompleteCornerRadius
            : default;
        _popupBorder.Padding = resources.AxamlTaskManagerContextMenu.AutocompletePadding;
        Popup.VerticalOffset = resources.AxamlTaskManagerContextMenu.AutocompleteVerticalOffset;
        _maximumSuggestionCount = Math.Max(
            val1: 1,
            resources.AxamlTaskManagerContextMenu.AutocompleteMaximumSuggestionCount);
    }

    private void PositionPopupAtCaret()
    {
        TextPresenter? presenter = _textBox.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        if (presenter == null)
        {
            Popup.HorizontalOffset = 0;
            return;
        }

        int caretIndex = Math.Clamp(_textBox.CaretIndex, min: 0, (_textBox.Text ?? string.Empty).Length);
        Rect caretRectangle = presenter.TextLayout.HitTestTextPosition(caretIndex);
        Point? caretPosition = presenter.TranslatePoint(caretRectangle.Position, _textBox);
        Popup.HorizontalOffset = Math.Max(val1: 0, caretPosition?.X ?? 0);
    }

    private void OnSuggestionPointerEntered(object? sender, PointerEventArgs eventArgs)
    {
        if (sender is not Border itemBorder) return;

        int suggestionIndex = _itemBorders.IndexOf(itemBorder);
        if (suggestionIndex < 0) return;
        _selectedIndex = suggestionIndex;
        UpdateItemVisuals();
    }

    private void OnSuggestionPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (sender is not Border itemBorder || !CompleteSuggestion(itemBorder)) return;

        eventArgs.Handled = true;
    }

    /// <summary>Completes the column token with a suggestion row's column; returns false for a stale row.</summary>
    private bool CompleteSuggestion(Border itemBorder)
    {
        int suggestionIndex = _itemBorders.IndexOf(itemBorder);
        if (suggestionIndex < 0) return false;

        _selectedIndex = suggestionIndex;
        CompleteSelectedSuggestion();
        return true;
    }

    private void UpdateItemVisuals()
    {
        for (int itemIndex = 0; itemIndex < _itemBorders.Count; itemIndex++)
        {
            _itemBorders[itemIndex].Background = itemIndex == _selectedIndex
                ? TrayAppDotNETSettingsUI.Brush(_palette.SearchListItemSelected)
                : Brushes.Transparent;
        }
    }

    private void CompleteSelectedSuggestion()
    {
        if ((uint)_selectedIndex >= (uint)_suggestions.Length) return;
        if (!SearchAutocompleteLogic.TryComplete(
                _textBox.Text,
                _textBox.CaretIndex,
                _suggestions[_selectedIndex],
                out string completedText,
                out int completedCaretIndex))
        {
            Close();
            return;
        }

        _suppressUpdate = true;
        _textBox.Text = completedText;
        _textBox.CaretIndex = completedCaretIndex;
        _textBox.SelectionStart = completedCaretIndex;
        _textBox.SelectionEnd = completedCaretIndex;
        _suppressUpdate = false;
        _textBox.Focus();
        Close();
    }

    private void Close()
    {
        Popup.IsOpen = false;
        _suggestions = [];
        _selectedIndex = 0;
        _itemsPanel.Children.Clear();
        _itemBorders.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _textBox.TextChanged -= OnTextChanged;
        _textBox.KeyDown -= OnKeyDown;
        _textBox.RemoveHandler(InputElement.KeyUpEvent, OnPreviewKeyUp);
        _textBox.PointerReleased -= OnPointerReleased;
        _textBox.PropertyChanged -= OnTextBoxPropertyChanged;
        _textBox.LostFocus -= OnLostFocus;
#if DEBUG
        if (_hotReloadAttached)
        {
            TaskManagerContextMenuResources.ResourcesReloaded -= OnAXAMLResourcesReloaded;
            _hotReloadAttached = false;
        }
#endif
        Close();
    }
}
