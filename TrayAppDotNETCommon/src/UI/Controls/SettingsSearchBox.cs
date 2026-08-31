using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Controls;

/// <summary>Compact search input used by the settings navigation sidebar.</summary>
public sealed class SettingsSearchBox : Grid, IDisposable
{
    private readonly TextBox _textBox;
    private readonly SettingsButton _clearButton;
    private int _disposed;

    public SettingsSearchBox(SettingsPalette palette, string placeholderText)
    {
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        HorizontalAlignment = HorizontalAlignment.Stretch;

        _textBox = TrayAppDotNETSettingsUI.SearchTextBox(palette, double.NaN);
        _textBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        _textBox.PlaceholderText = placeholderText;
        _textBox.PropertyChanged += OnTextBoxPropertyChanged;
        _textBox.KeyDown += OnKeyDown;
        Children.Add(_textBox);

        _clearButton = new SettingsButton(
            GlyphCatalog.CHROME_CLOSE,
            palette,
            palette.ControlBackgroundDeep,
            palette.HoverDeep,
            palette.PressedDeep)
        {
            Width = SearchableListBoxLayout.ClearButtonWidth,
            Height = SearchableListBoxLayout.ClearButtonHeight,
            MinHeight = SearchableListBoxLayout.ClearButtonHeight,
            Padding = SearchableListBoxLayout.ClearButtonPadding,
            Label = { FontSize = SearchableListBoxLayout.ClearButtonFontSize }
        };
        _clearButton.Click += OnClearButtonClick;
        SetColumn(_clearButton, value: 1);
        Children.Add(_clearButton);

        UpdateClearButton();
    }

    public event EventHandler? SearchTextChanged;

    public string SearchText
    {
        get => _textBox.Text ?? string.Empty;
        set => _textBox.Text = value;
    }

    public void Clear() => _textBox.Text = string.Empty;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, value: 1) != 0) return;

        _clearButton.Click -= OnClearButtonClick;
        _textBox.KeyDown -= OnKeyDown;
        _textBox.PropertyChanged -= OnTextBoxPropertyChanged;
        SearchTextChanged = null;
    }

    private void OnTextBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.Property != TextBox.TextProperty) return;

        UpdateClearButton();
        SearchTextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Escape || string.IsNullOrEmpty(SearchText)) return;

        Clear();
        eventArgs.Handled = true;
    }

    private void OnClearButtonClick(object? sender, EventArgs eventArgs) => Clear();

    private void UpdateClearButton() => _clearButton.IsVisible = !string.IsNullOrEmpty(SearchText);
}
