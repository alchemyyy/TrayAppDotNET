using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace TaskManagerTrayAppDotNET.UI;

/// <summary>Configures Details-column visibility and ordering.</summary>
internal sealed class ProcessColumnChooserWindow : Window
{
    private const double WindowWidth = 560;
    private const double WindowHeight = 720;
    private const double RowSpacing = 6;
    private const double ButtonSpacing = 8;
    private const double ContentPadding = 16;

    private readonly Action<List<ProcessColumnSetting>> _apply;
    private readonly List<ProcessColumnSetting> _settings;
    private readonly StackPanel _rows = new() { Spacing = RowSpacing };

    public ProcessColumnChooserWindow(
        IReadOnlyList<ProcessColumnSetting> settings,
        Action<List<ProcessColumnSetting>> apply)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(apply);

        _apply = apply;
        _settings = ProcessColumnSettings.CloneList(settings);
        Title = "Select Details columns";
        Width = WindowWidth;
        Height = WindowHeight;
        MinWidth = WindowWidth;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildContent();
        RebuildRows();
    }

    private Control BuildContent()
    {
        Grid root = new()
        {
            Margin = new Thickness(ContentPadding),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };

        TextBlock explanation = new()
        {
            Text = "Choose visible columns and arrange their left-to-right order.",
            Margin = new Thickness(0, 0, 0, ContentPadding)
        };
        root.Children.Add(explanation);

        ScrollViewer scrollViewer = new()
        {
            Content = _rows,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(scrollViewer, 1);
        root.Children.Add(scrollViewer);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = ButtonSpacing,
            Margin = new Thickness(0, ContentPadding, 0, 0)
        };
        Button resetButton = new() { Content = "Reset" };
        resetButton.Click += OnResetClick;
        Button cancelButton = new() { Content = "Cancel" };
        cancelButton.Click += OnCancelClick;
        Button applyButton = new() { Content = "Apply" };
        applyButton.Click += OnApplyClick;
        buttons.Children.Add(resetButton);
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(applyButton);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        return root;
    }

    private void RebuildRows()
    {
        _rows.Children.Clear();
        for (int settingIndex = 0; settingIndex < _settings.Count; settingIndex++)
        {
            ProcessColumnSetting setting = _settings[settingIndex];
            ProcessTableColumnDefinition definition = ProcessTableColumnCatalog.Get(setting.Column);
            Grid row = new()
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto)
                }
            };
            CheckBox visibility = new()
            {
                Content = definition.Title,
                IsChecked = setting.Visible,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = setting
            };
            visibility.IsCheckedChanged += OnVisibilityChanged;
            row.Children.Add(visibility);

            Button upButton = new()
            {
                Content = "Up",
                IsEnabled = settingIndex > 0,
                Tag = setting,
                Margin = new Thickness(ButtonSpacing, 0, 0, 0)
            };
            upButton.Click += OnMoveUpClick;
            Grid.SetColumn(upButton, 1);
            row.Children.Add(upButton);

            Button downButton = new()
            {
                Content = "Down",
                IsEnabled = settingIndex < _settings.Count - 1,
                Tag = setting,
                Margin = new Thickness(ButtonSpacing, 0, 0, 0)
            };
            downButton.Click += OnMoveDownClick;
            Grid.SetColumn(downButton, 2);
            row.Children.Add(downButton);
            _rows.Children.Add(row);
        }
    }

    private void OnVisibilityChanged(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is CheckBox { Tag: ProcessColumnSetting setting } checkBox)
            setting.Visible = checkBox.IsChecked == true;
    }

    private void OnMoveUpClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: ProcessColumnSetting setting }) return;
        int index = _settings.IndexOf(setting);
        if (index <= 0) return;

        _settings.RemoveAt(index);
        _settings.Insert(index - 1, setting);
        RebuildRows();
    }

    private void OnMoveDownClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { Tag: ProcessColumnSetting setting }) return;
        int index = _settings.IndexOf(setting);
        if (index < 0 || index >= _settings.Count - 1) return;

        _settings.RemoveAt(index);
        _settings.Insert(index + 1, setting);
        RebuildRows();
    }

    private void OnResetClick(object? sender, RoutedEventArgs eventArgs)
    {
        _settings.Clear();
        _settings.AddRange(ProcessColumnSettings.CreateDefault());
        RebuildRows();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs eventArgs) => Close();

    private void OnApplyClick(object? sender, RoutedEventArgs eventArgs)
    {
        _apply(ProcessColumnSettings.CloneList(_settings));
        Close();
    }
}
