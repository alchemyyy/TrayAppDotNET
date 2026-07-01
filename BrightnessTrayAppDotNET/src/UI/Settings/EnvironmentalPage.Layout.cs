using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using BrightnessTrayAppDotNET.UI.Settings.Environmental;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private StackPanel BuildEnvironmentalLegend(SettingsPalette p)
    {
        _legendPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Spacing = 6,
        };

        EnvironmentalCurveEditorPalette editorPalette = BuildEnvironmentalEditorPalette(p);
        _brightnessLegendItem = LegendItem(L("Settings_Environmental_Legend_Brightness", "Brightness"),
            editorPalette.BrightnessCurve, p, vertical: false);
        _nightLightLegendItem = LegendItem(L("Settings_Environmental_Legend_NightLight", "Night light"),
            editorPalette.NightLightCurve, p, vertical: false);
        _currentTimeLegendItem = LegendItem(L("Settings_Environmental_Legend_TimeNow", "Now"),
            editorPalette.CurrentTime, p, vertical: true);
        _legendPanel.Children.Add(_brightnessLegendItem);
        _legendPanel.Children.Add(_nightLightLegendItem);
        _legendPanel.Children.Add(_currentTimeLegendItem);
        return _legendPanel;
    }

    private static StackPanel LegendItem(string text, Color color, SettingsPalette p, bool vertical)
    {
        Border swatch = new()
        {
            Width = vertical ? 2 : 14,
            Height = vertical ? 14 : 3,
            Background = TrayAppDotNETSettingsUI.Brush(color),
            CornerRadius = new CornerRadius(vertical ? 1 : 1.5),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0),
        };
        TextBlock label = TrayAppDotNETSettingsUI.Text(text, p, 11);
        label.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { swatch, label }, };
    }

    private StackPanel BuildEnvironmentalProfileRow(SettingsPalette p)
    {
        _environmentalProfileCombo = TrayAppDotNETSettingsUI.ComboBox(p, 140, autoSizeToText: true);
        _environmentalProfileCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressEnvironmentalEvents) return;
            if (_environmentalProfileCombo.SelectedItem?.Tag is not int index) return;
            _environmentalProfileIndex = index;
            LoadEnvironmentalCurveForSelectedProfile();
        };

        TextBlock label = TrayAppDotNETSettingsUI.TitleText(L("Settings_Environmental_Profile_Label", "Profile"), p);
        label.FontWeight = FontWeight.SemiBold;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 8, 0);

        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Children = { label, _environmentalProfileCombo },
        };
    }

    private StackPanel BuildEnvironmentalModeRows(SettingsPalette p)
    {
        StackPanel panel = new();
        _offsetModeToggle = AddToggleRow(panel, p, L("Settings_Environmental_OffsetMode_Title", "Offset mode"),
            _settings.EnvironmentalOffsetMode, (_, enabled) =>
            {
                if (_suppressEnvironmentalEvents) return;
                _settings.EnvironmentalOffsetMode = enabled;
                Save();
                _environmentalCurveEditor?.SetOffsetMode(enabled);
            });
        _followTheSunToggle = AddToggleRow(panel, p, L("Settings_Environmental_FollowTheSun_Title", "Follow the sun"),
            true, (_, enabled) =>
            {
                if (_suppressEnvironmentalEvents) return;
                EnvironmentalCurve? curve = SelectedEnvironmentalCurve();
                if (curve == null) return;
                curve.FollowTheSun = enabled;
                if (enabled) StampSunShiftAnchor(curve);
                _profileManager?.Save();
                ApplyEnvironmentalPreviewState(_environmentalSunOverlayDate);
                NotifyRuntimeCurveChanged();
            });
        _useDaylightSavingsToggle = AddToggleRow(panel, p,
            L("Settings_Environmental_UseDaylightSavings_Title", "Use DST"), true, (_, enabled) =>
            {
                if (_suppressEnvironmentalEvents) return;
                EnvironmentalCurve? curve = SelectedEnvironmentalCurve();
                if (curve == null) return;
                curve.UseDaylightSavings = enabled;
                _profileManager?.Save();
                _environmentalCurveEditor?.SetUseDaylightSavings(enabled);
                ApplyEnvironmentalPreviewState(_environmentalSunOverlayDate);
                NotifyRuntimeCurveChanged();
            });

        return panel;
    }

    private StackPanel BuildEnvironmentalDisabledPeriodRows(SettingsPalette p)
    {
        StackPanel panel = new();
        _disabledPeriodToggle = AddToggleRow(panel, p,
            L("Settings_Environmental_DisabledPeriod_Title", "Disabled period"), false, (_, enabled) =>
            {
                if (_suppressEnvironmentalEvents) return;
                EnvironmentalCurve? curve = SelectedEnvironmentalCurve();
                if (curve == null) return;
                curve.DisabledPeriodEnabled = enabled;
                _profileManager?.Save();
                ApplyEnvironmentalDisabledPeriodFieldVisibility(enabled);
                ApplyEnvironmentalPreviewState(_environmentalSunOverlayDate);
                NotifyRuntimeCurveChanged();
            });

        _disabledPeriodFollowTheSunRow = AddToggleRow(panel, p,
            L("Settings_Environmental_DisabledPeriodFollowTheSun_Title", "Disabled period follows sun"), false,
            (_, enabled) =>
            {
                if (_suppressEnvironmentalEvents) return;
                EnvironmentalCurve? curve = SelectedEnvironmentalCurve();
                if (curve == null) return;
                curve.DisabledPeriodFollowTheSun = enabled;
                if (enabled) StampDisabledPeriodSunShiftAnchor(curve);
                _profileManager?.Save();
                ApplyEnvironmentalPreviewState(_environmentalSunOverlayDate);
                NotifyRuntimeCurveChanged();
            }, out _disabledPeriodFollowTheSunToggle, indent: 8);

        StackPanel fields = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 6), };
        _disabledPeriodStartBox = TimeBox(p);
        _disabledPeriodEndBox = TimeBox(p);
        _disabledPeriodStartBox.Width = 64;
        _disabledPeriodEndBox.Width = 64;
        fields.Children.Add(InlineLabel(L("Settings_Environmental_DisabledPeriod_Start_Label", "Start"), p,
            new Thickness(0, 0, 4, 0)));
        fields.Children.Add(_disabledPeriodStartBox);
        fields.Children.Add(InlineLabel(L("Settings_Environmental_DisabledPeriod_End_Label", "End"), p,
            new Thickness(10, 0, 4, 0)));
        fields.Children.Add(_disabledPeriodEndBox);
        _disabledPeriodFieldsRow = fields;
        panel.Children.Add(fields);

        _disabledPeriodStartBox.LostFocus += (_, _) => CommitEnvironmentalDisabledPeriodTime(isStart: true);
        _disabledPeriodEndBox.LostFocus += (_, _) => CommitEnvironmentalDisabledPeriodTime(isStart: false);
        _disabledPeriodStartBox.KeyDown += OnDisabledPeriodTimeKeyDown;
        _disabledPeriodEndBox.KeyDown += OnDisabledPeriodTimeKeyDown;

        return panel;
    }

    private Grid BuildEnvironmentalVisibilityRows(SettingsPalette p)
    {
        StackPanel panel = new() { Margin = new Thickness(24, 12, 0, 0), VerticalAlignment = VerticalAlignment.Top, };
        _showBrightnessCurveToggle = AddToggleRow(panel, p,
            L("Settings_Environmental_ShowBrightnessCurve_Title", "Brightness curve"),
            _settings.EnvironmentalShowBrightnessCurve, OnEnvironmentalCurveVisibilityChanged);
        _showNightLightCurveToggle = AddToggleRow(panel, p,
            L("Settings_Environmental_ShowNightLightCurve_Title", "Night-light curve"),
            _settings.EnvironmentalShowNightLightCurve, OnEnvironmentalCurveVisibilityChanged);
        _showSunOverlayToggle = AddToggleRow(panel, p, L("Settings_Environmental_ShowSunOverlay_Title", "Sun overlay"),
            _settings.EnvironmentalShowSunOverlay, (_, enabled) =>
            {
                if (_suppressEnvironmentalEvents) return;
                _settings.EnvironmentalShowSunOverlay = enabled;
                Save();
                _environmentalCurveEditor?.SetShowSunOverlay(enabled);
            });
        _showCursorReadoutToggle = AddToggleRow(panel, p,
            L("Settings_Environmental_ShowCursorReadout_Title", "Cursor readout"),
            _settings.EnvironmentalShowCursorReadout, (_, enabled) =>
            {
                if (_suppressEnvironmentalEvents) return;
                _settings.EnvironmentalShowCursorReadout = enabled;
                Save();
                _environmentalCurveEditor?.SetShowCursorReadout(enabled);
            });
        return new Grid { Children = { panel } };
    }

    private Grid BuildEnvironmentalPreviewControls(SettingsPalette p)
    {
        Grid row = new() { Margin = new Thickness(0, 0, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        Control visibility = BuildEnvironmentalVisibilityRows(p);
        Grid.SetColumn(visibility, 0);
        row.Children.Add(visibility);

        StackPanel right = new() { VerticalAlignment = VerticalAlignment.Top, };
        Grid.SetColumn(right, 1);
        row.Children.Add(right);

        right.Children.Add(BuildEnvironmentalDateRow(p));

        _previewSweepButton = Button(L("Settings_Environmental_PreviewSweep_Idle_Button", "Live preview next 24 hours"),
            p);
        _previewSweepButton.Width = 217;
        _previewSweepButton.HorizontalAlignment = HorizontalAlignment.Right;
        _previewSweepButton.Margin = new Thickness(0, -2, 0, 4);
        ApplyEnvironmentalButtonFont(_previewSweepButton);
        _previewSweepButton.Click += (_, _) => ToggleEnvironmentalPreviewSweep();
        TrayAppDotNETToolTip.SetTip(
            _previewSweepButton,
            L("Settings_Environmental_PreviewSweep_ToolTip", "Animate the full day's curve over 10 seconds."));
        right.Children.Add(_previewSweepButton);

        right.Children.Add(BuildEnvironmentalSmoothnessCard(p));
        return row;
    }

    private Grid BuildEnvironmentalDateRow(SettingsPalette p)
    {
        Grid grid = new() { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        TextBlock title =
            TrayAppDotNETSettingsUI.TitleText(L("Settings_Environmental_PreviewDate_Label", "Preview date"), p);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.HorizontalAlignment = HorizontalAlignment.Right;
        title.Margin = new Thickness(0, 0, 8, 0);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        grid.Children.Add(title);

        Grid dateControl = new() { Width = 122, Height = 32, };
        dateControl.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(94)));
        dateControl.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(28)));

        _sunOverlayDateBox = TrayAppDotNETSettingsUI.TextBox(p, 94, FormatSunOverlayDate(DateTime.Today));
        _sunOverlayDateBox.Padding = new Thickness(8, 0, 0, 0);
        _sunOverlayDateBox.TextAlignment = TextAlignment.Left;
        _sunOverlayDateBox.LostFocus += (_, _) => CommitEnvironmentalSunOverlayDate();
        _sunOverlayDateBox.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Up:
                    StepEnvironmentalSunOverlayDate(e.KeyModifiers.HasFlag(KeyModifiers.Control)
                        ? d => d.AddMonths(1)
                        : d => d.AddDays(1));
                    e.Handled = true;
                    break;
                case Key.Down:
                    StepEnvironmentalSunOverlayDate(e.KeyModifiers.HasFlag(KeyModifiers.Control)
                        ? d => d.AddMonths(-1)
                        : d => d.AddDays(-1));
                    e.Handled = true;
                    break;
                case Key.Enter:
                    CommitEnvironmentalSunOverlayDate();
                    e.Handled = true;
                    break;
            }
        };
        _sunOverlayDateBox.AddHandler(
            InputElement.PointerWheelChangedEvent,
            OnEnvironmentalSunOverlayDateBoxPointerWheelChanged,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        dateControl.Children.Add(_sunOverlayDateBox);

        SettingsButton calendarButton = Button(GlyphCatalog.CALENDAR, p);
        calendarButton.Width = 28;
        calendarButton.Height = 32;
        calendarButton.MinHeight = 32;
        calendarButton.Padding = new Thickness(0);
        calendarButton.Label.FontFamily = TrayAppDotNETSettingsUI.IconFont;
        calendarButton.Label.FontSize = 13;
        calendarButton.Click += (_, _) => ToggleEnvironmentalSunOverlayCalendar();
        TrayAppDotNETToolTip.SetTip(
            calendarButton,
            L("Settings_Environmental_PickDate_ToolTip",
                "Pick a preview date."));
        Grid.SetColumn(calendarButton, 1);
        dateControl.Children.Add(calendarButton);

        _sunOverlayCalendar = BuildEnvironmentalSunOverlayCalendar();
        _sunOverlayDatePopup = BuildEnvironmentalSunOverlayDatePopup(p, calendarButton, _sunOverlayCalendar);
        dateControl.Children.Add(_sunOverlayDatePopup);

        Grid.SetColumn(dateControl, 1);
        grid.Children.Add(dateControl);
        return grid;
    }

    /// <summary>
    /// Builds the calendar used by the environmental preview date popup.
    /// </summary>
    private Calendar BuildEnvironmentalSunOverlayCalendar()
    {
        Calendar calendar = new()
        {
            DisplayDate = _environmentalSunOverlayDate,
            FirstDayOfWeek = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek,
            IsTodayHighlighted = true,
            SelectedDate = _environmentalSunOverlayDate,
            SelectionMode = CalendarSelectionMode.SingleDate,
        };
        calendar.SelectedDatesChanged += OnEnvironmentalSunOverlayCalendarSelectedDatesChanged;
        calendar.PointerReleased += OnEnvironmentalSunOverlayCalendarPointerReleased;
        calendar.KeyDown += OnEnvironmentalSunOverlayCalendarKeyDown;
        return calendar;
    }

    /// <summary>
    /// Builds the popup shell that anchors the preview date calendar to its button.
    /// </summary>
    private static Popup BuildEnvironmentalSunOverlayDatePopup(SettingsPalette p, Control target, Calendar calendar)
    {
        Border popupBorder = new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(p.Background),
            BorderBrush = TrayAppDotNETSettingsUI.Brush(p.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6),
            Margin = new Thickness(8),
            Child = calendar,
        };

        return new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.BottomEdgeAlignedRight,
            VerticalOffset = 4,
            IsLightDismissEnabled = true,
            Child = popupBorder,
        };
    }

    private Border BuildEnvironmentalSmoothnessCard(SettingsPalette p)
    {
        SettingsNumberBox smoothness = TrayAppDotNETSettingsUI.NumberBox(
            p,
            _settings.EnvironmentalCurveSmoothness,
            CurveSmoothnessMin,
            CurveSmoothnessMax,
            width: 80);
        smoothness.HandleMouseWheelWhenMouseOver = true;
        smoothness.ValueChanged += (_, e) =>
        {
            if (!e.NewValue.HasValue) return;
            int value = Math.Clamp((int)Math.Round(e.NewValue.Value), CurveSmoothnessMin, CurveSmoothnessMax);
            _settings.EnvironmentalCurveSmoothness = value;
            Save();
            _environmentalCurveEditor?.SetSmoothness(value / 100.0);
            NotifyRuntimeCurveChanged();
        };

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock title =
            TrayAppDotNETSettingsUI.TitleText(L("Settings_Environmental_Smoothness_Title", "Smoothness"), p);
        title.FontWeight = FontWeight.SemiBold;
        title.Margin = new Thickness(0, -2, 0, 0);
        grid.Children.Add(title);

        smoothness.Margin = new Thickness(0, 0, 4, 0);
        Grid.SetColumn(smoothness, 1);
        grid.Children.Add(smoothness);

        StackPanel description = new();
        description.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            L("Settings_Environmental_Smoothness_DescriptionLine1", "Blend between"),
            p,
            new Thickness(0, 2, 0, 0)));
        description.Children.Add(TrayAppDotNETSettingsUI.DescriptionText(
            L("Settings_Environmental_Smoothness_DescriptionLine2", "linear(0) and interpolated(100)"),
            p,
            new Thickness(0, 0, 0, 0)));
        Grid.SetRow(description, 1);
        Grid.SetColumnSpan(description, 2);
        grid.Children.Add(description);

        return CompactEnvironmentalCard(grid, p, new Thickness(14, 8), new Thickness(16, 0, 0, 0));
    }

    private Border BuildEnvironmentalLocationCard(SettingsPalette p)
    {
        Grid panel = new();
        panel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        panel.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(12)));
        panel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        panel.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(12)));
        panel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        TextBlock title =
            TrayAppDotNETSettingsUI.TitleText(L("Settings_Environmental_GeoLocation_Title", "Geo location"), p);
        title.FontWeight = FontWeight.SemiBold;
        title.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(title);

        TextBlock description = TrayAppDotNETSettingsUI.DescriptionText(
            L("Settings_Environmental_GeoLocation_Description",
                "Used for sun-position overlays and follow-the-sun shifting."),
            p,
            new Thickness(0, 0, 0, 8));
        description.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(description, 2);
        Grid.SetColumnSpan(description, 3);
        panel.Children.Add(description);

        _latitudeBox =
            TrayAppDotNETSettingsUI.TextBox(p, double.NaN, FormatCoordinate(_settings.EnvironmentalLatitude));
        _longitudeBox =
            TrayAppDotNETSettingsUI.TextBox(p, double.NaN, FormatCoordinate(_settings.EnvironmentalLongitude));
        SettingsButton approximate = Button(L("Settings_Environmental_ApproxFromIP_Button", "Approx. from IP"), p);
        SettingsButton map = Button(L("Settings_Environmental_PickOnMap_Button", "Pick on map"), p);
        approximate.MinWidth = 130;
        map.MinWidth = 130;
        ApplyEnvironmentalButtonFont(approximate);
        ApplyEnvironmentalButtonFont(map);
        approximate.HorizontalAlignment = HorizontalAlignment.Stretch;
        map.HorizontalAlignment = HorizontalAlignment.Stretch;
        approximate.Margin = new Thickness(0, 0, 0, 4);
        approximate.Click += async (_, _) => await ApproximateEnvironmentalLocationFromIPAsync(approximate);
        map.Click += (_, _) => OpenEnvironmentalMapPicker();
        StackPanel buttons = new() { VerticalAlignment = VerticalAlignment.Bottom, Children = { approximate, map }, };
        Grid.SetRow(buttons, 1);
        Grid.SetColumn(buttons, 0);
        panel.Children.Add(buttons);

        Control latitude = LabeledBox(L("Settings_Environmental_Latitude_Label", "Latitude"), _latitudeBox, p);
        Grid.SetRow(latitude, 1);
        Grid.SetColumn(latitude, 2);
        panel.Children.Add(latitude);
        Control longitude = LabeledBox(L("Settings_Environmental_Longitude_Label", "Longitude"), _longitudeBox, p);
        Grid.SetRow(longitude, 1);
        Grid.SetColumn(longitude, 4);
        panel.Children.Add(longitude);

        _latitudeBox.LostFocus += (_, _) => CommitEnvironmentalCoordinates();
        _longitudeBox.LostFocus += (_, _) => CommitEnvironmentalCoordinates();
        _latitudeBox.KeyDown += OnEnvironmentalCoordinateKeyDown;
        _longitudeBox.KeyDown += OnEnvironmentalCoordinateKeyDown;

        return CompactEnvironmentalCard(panel, p, new Thickness(14, 10), new Thickness(0, 2, 0, 0));
    }

    private SettingsButton BuildEnvironmentalResetButton(SettingsPalette p)
    {
        SettingsButton reset = Button(L("Settings_Environmental_ResetCurves_Button", "Reset curves"), p);
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        reset.Margin = new Thickness(0, 4, 0, 0);
        ApplyEnvironmentalButtonFont(reset);
        reset.Click += async (_, _) => await ResetEnvironmentalCurvesAsync();
        return reset;
    }

    private static void ApplyEnvironmentalButtonFont(SettingsButton button) =>
        button.Label.FontSize = 12.0;

    private static StackPanel AddToggleRow(
        StackPanel panel,
        SettingsPalette p,
        string label,
        bool value,
        EventHandler<bool> changed,
        out SettingsToggle toggle,
        double indent = 0.0)
    {
        toggle = TrayAppDotNETSettingsUI.Toggle(p, value, changed);
        TextBlock text = TrayAppDotNETSettingsUI.TitleText(label, p);
        text.VerticalAlignment = VerticalAlignment.Center;
        StackPanel row = TrayAppDotNETSettingsUI.Horizontal(toggle, text);
        row.Margin = new Thickness(indent, 0, 0, 6);
        toggle.Margin = new Thickness(0, 0, 8, 0);
        panel.Children.Add(row);
        return row;
    }

    private static SettingsToggle AddToggleRow(
        StackPanel panel,
        SettingsPalette p,
        string label,
        bool value,
        EventHandler<bool> changed)
    {
        AddToggleRow(panel, p, label, value, changed, out SettingsToggle toggle);
        return toggle;
    }

    private static TextBox TimeBox(SettingsPalette p)
    {
        TextBox box = TrayAppDotNETSettingsUI.TextBox(p, double.NaN);
        box.HorizontalAlignment = HorizontalAlignment.Stretch;
        box.TextAlignment = TextAlignment.Center;
        return box;
    }

    private static StackPanel LabeledBox(string label, Control box, SettingsPalette p)
    {
        StackPanel panel = new();
        TextBlock title = TrayAppDotNETSettingsUI.TitleText(label, p);
        title.Margin = new Thickness(0, 6, 0, 4);
        panel.Children.Add(title);
        panel.Children.Add(box);
        return panel;
    }

    private static TextBlock InlineLabel(string label, SettingsPalette p, Thickness margin)
    {
        TextBlock text = TrayAppDotNETSettingsUI.TitleText(label, p);
        text.VerticalAlignment = VerticalAlignment.Center;
        text.Margin = margin;
        return text;
    }

    private Border CompactEnvironmentalCard(Control content, SettingsPalette p, Thickness padding, Thickness margin) =>
        new()
        {
            Background = TrayAppDotNETSettingsUI.Brush(p.CardBackground),
            CornerRadius = RadiusLarge,
            Padding = padding,
            Margin = margin,
            Child = content,
        };
}
