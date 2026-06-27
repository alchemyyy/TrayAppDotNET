using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace TrayAppDotNETCommon.UI.Controls;

public static class TrayAppDotNETSettingsCards
{
    public static StackPanel PageStack(string title, SettingsPalette palette)
    {
        StackPanel stack = new() { Background = TrayAppDotNETSettingsUI.Brush(palette.Background), };
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
        Action? afterSave = null)
    {
        SettingsToggle toggle = TrayAppDotNETSettingsUI.Toggle(palette, value, (_, enabled) =>
        {
            set(enabled);
            save();
            afterSave?.Invoke();
        });
        return Card(title, description, toggle, palette, cardRadius);
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
        string suffix = "")
    {
        SettingsNumberBox input = TrayAppDotNETSettingsUI.NumberBox(palette, value, min, max, 100, suffix);
        input.ValueChanged += (_, e) =>
        {
            if (!e.NewValue.HasValue) return;
            set((int)e.NewValue.Value);
            save();
        };
        return Card(title, description, input, palette, cardRadius);
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
        SettingsComboBoxAutoSizeMode autoSizeMode = SettingsComboBoxAutoSizeMode.LongestItem)
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
        return Card(title, description, combo, palette, cardRadius);
    }

    public static Border Card(
        string title,
        string description,
        Control? rightControl,
        SettingsPalette palette,
        CornerRadius cardRadius)
    {
        StackPanel text = new()
        {
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
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
            rightControl.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(rightControl, 1);
            grid.Children.Add(rightControl);
        }

        return RawCard(grid, palette, cardRadius);
    }

    public static Border RawCard(Control content, SettingsPalette palette, CornerRadius cardRadius)
    {
        Border card = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(palette.CardBackground),
            CornerRadius = cardRadius,
            Padding = new Thickness(16, 12),
            Margin = new Thickness(0, 0, 0, 6),
            Child = content,
        };
        TrayAppDotNETSettingsUI.ApplyDisabledOpacity(card, 0.45);
        return card;
    }

    public static Border MutableCard(
        string title,
        string description,
        Control? rightControl,
        SettingsPalette palette,
        CornerRadius cardRadius,
        out TextBlock descriptionText)
    {
        StackPanel text = new()
        {
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
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
            rightControl.Margin = new Thickness(16, 0, 0, 0);
            Grid.SetColumn(rightControl, 1);
            grid.Children.Add(rightControl);
        }

        return RawCard(grid, palette, cardRadius);
    }
}
