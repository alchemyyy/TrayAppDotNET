using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using TrayAppDotNETCommon.UI.Settings;

namespace TrayAppDotNETCommon.UI.Controls;

internal static class SettingsCardsLayout
{
    private static readonly Lazy<CardsResources> Resources = new(static () => new CardsResources());

    private static CardsResources AXAMLResources => Resources.Value;

    public static double NumberBoxWidth => AXAMLResources.AxamlSettingsCards.NumberBoxWidth;
    public static Thickness RightControlMargin => AXAMLResources.AxamlSettingsCards.RightControlMargin;
    public static Thickness CardPadding => AXAMLResources.AxamlSettingsCards.CardPadding;
    public static Thickness CardMargin => AXAMLResources.AxamlSettingsCards.CardMargin;
    public static double DisabledOpacity => AXAMLResources.AxamlSettingsCards.DisabledOpacity;
}

public static class TrayAppDotNETSettingsCards
{
    /// <summary>Marks a custom settings card so stitched search results can filter it independently.</summary>
    public static Border RegisterSearchCard(Border card, params string[] searchKeywords)
    {
        ArgumentNullException.ThrowIfNull(card);
        SettingsSearchMetadata.Mark(card, SettingsSearchRole.Card);
        return SettingsSearchMetadata.AddSearchKeywords(card, searchKeywords);
    }

    public static StackPanel PageStack(string title, SettingsPalette palette)
    {
        StackPanel stack = new() { Background = TrayAppDotNETSettingsUI.Brush(palette.Background) };
        stack.Children.Add(TrayAppDotNETSettingsUI.SectionHeader(title, palette));
        return stack;
    }

    public static SettingsButton Button(string text, SettingsPalette palette, CornerRadius cornerRadius)
    {
        SettingsButton button = TrayAppDotNETSettingsUI.Button(text, palette);
        button.CornerRadius = cornerRadius;
        return button;
    }

    public static Border BoolCard(
        string title,
        string description,
        bool value,
        Action<bool> set,
        SettingsPalette palette,
        CornerRadius cardRadius,
        Action save,
        Action? afterSave = null,
        IReadOnlyList<string>? searchKeywords = null)
    {
        SettingsToggle toggle = TrayAppDotNETSettingsUI.Toggle(palette, value, (_, enabled) =>
        {
            set(enabled);
            save();
            afterSave?.Invoke();
        });
        return Card(title, description, toggle, palette, cardRadius, searchKeywords);
    }

    public static Border IntCard(
        string title,
        string description,
        int value,
        int min,
        int max,
        Action<int> set,
        SettingsPalette palette,
        CornerRadius cardRadius,
        Action save,
        string suffix = "",
        IReadOnlyList<string>? searchKeywords = null)
    {
        SettingsNumberBox input = TrayAppDotNETSettingsUI.NumberBox(
            palette,
            value,
            min,
            max,
            SettingsCardsLayout.NumberBoxWidth,
            suffix);
        input.ValueChanged += (_, e) =>
        {
            if (!e.NewValue.HasValue) return;
            set((int)e.NewValue.Value);
            save();
        };
        return Card(title, description, input, palette, cardRadius, searchKeywords);
    }

    public static Border ComboCard(
        string title,
        string description,
        IReadOnlyList<(string Tag, string Text)> items,
        string selectedTag,
        Action<string> set,
        SettingsPalette palette,
        CornerRadius cardRadius,
        Action save,
        Action? afterSave = null,
        bool autoSizeToText = false,
        SettingsComboBoxAutoSizeMode autoSizeMode = SettingsComboBoxAutoSizeMode.LongestItem,
        IReadOnlyList<string>? searchKeywords = null)
    {
        SettingsComboBox combo = TrayAppDotNETSettingsUI.ComboBox(
            palette,
            autoSizeToText: autoSizeToText,
            autoSizeMode: autoSizeMode);
        foreach ((string tag, string text) in items)
            combo.Items.Add(TrayAppDotNETSettingsUI.ComboItem(tag, text, palette));
        TrayAppDotNETSettingsUI.SelectComboByTag(combo, selectedTag);
        combo.SelectionChanged += (_, _) =>
        {
            string? tag = TrayAppDotNETSettingsUI.SelectedTag(combo);
            if (string.IsNullOrEmpty(tag)) return;
            set(tag);
            save();
            afterSave?.Invoke();
        };
        return Card(title, description, combo, palette, cardRadius, searchKeywords);
    }

    public static Border Card(
        string title,
        string description,
        Control? rightControl,
        SettingsPalette palette,
        CornerRadius cardRadius,
        IReadOnlyList<string>? searchKeywords = null)
    {
        StackPanel text = new()
        {
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        text.Children.Add(TrayAppDotNETSettingsUI.TitleText(title, palette));
        if (!string.IsNullOrEmpty(description))
            text.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(description, palette));

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 0 });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.Children.Add(text);

        if (rightControl != null)
        {
            rightControl.VerticalAlignment = VerticalAlignment.Center;
            rightControl.Margin = SettingsCardsLayout.RightControlMargin;
            Grid.SetColumn(rightControl, 1);
            grid.Children.Add(rightControl);
        }

        Border card = RawCard(grid, palette, cardRadius);
        return SettingsSearchMetadata.MarkCard(card, title, searchKeywords);
    }

    public static Border RawCard(
        Control content,
        SettingsPalette palette,
        CornerRadius cardRadius,
        IReadOnlyList<string>? searchKeywords = null)
    {
        Border card = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.CardBackground),
            CornerRadius = cardRadius,
            Padding = SettingsCardsLayout.CardPadding,
            Margin = SettingsCardsLayout.CardMargin,
            Child = content
        };
        TrayAppDotNETSettingsUI.ApplyDisabledOpacity(card, SettingsCardsLayout.DisabledOpacity);
        SettingsSearchMetadata.Mark(card, SettingsSearchRole.Card);
        return SettingsSearchMetadata.AddSearchKeywords(card, searchKeywords);
    }

    public static Border MutableCard(
        string title,
        string description,
        Control? rightControl,
        SettingsPalette palette,
        CornerRadius cardRadius,
        out TextBlock descriptionText,
        IReadOnlyList<string>? searchKeywords = null)
    {
        StackPanel text = new()
        {
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        text.Children.Add(TrayAppDotNETSettingsUI.TitleText(title, palette));
        descriptionText = TrayAppDotNETSettingsUI.DescriptionText(description, palette);
        descriptionText.IsVisible = !string.IsNullOrEmpty(description);
        text.Children.Add(descriptionText);

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star) { MinWidth = 0 });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.Children.Add(text);
        if (rightControl != null)
        {
            rightControl.VerticalAlignment = VerticalAlignment.Center;
            rightControl.Margin = SettingsCardsLayout.RightControlMargin;
            Grid.SetColumn(rightControl, 1);
            grid.Children.Add(rightControl);
        }

        Border card = RawCard(grid, palette, cardRadius);
        return SettingsSearchMetadata.MarkCard(card, title, searchKeywords);
    }
}
