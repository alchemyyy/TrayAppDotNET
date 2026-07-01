using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using BrightnessTrayAppDotNET.UI.Flyout;
using BrightnessTrayAppDotNET.Utils;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private EnvironmentalCurve ResolveDisplayCurve(EnvironmentalCurve stored, DateTime target)
    {
        if (!stored.FollowTheSun) return stored;

        double toLatitude = _settings.EnvironmentalLatitude;
        double toLongitude = _settings.EnvironmentalLongitude;
        if (!IsValidCoordinate(toLatitude, toLongitude)) return stored;

        if (string.IsNullOrEmpty(stored.LastSunShiftDate)
            || !DateTime.TryParseExact(
                stored.LastSunShiftDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime fromDate))
            return stored;

        double fromLatitude;
        double fromLongitude;
        if (IsValidCoordinate(stored.LastSunShiftLatitude, stored.LastSunShiftLongitude))
        {
            fromLatitude = stored.LastSunShiftLatitude;
            fromLongitude = stored.LastSunShiftLongitude;
        }
        else
        {
            fromLatitude = toLatitude;
            fromLongitude = toLongitude;
        }

        bool toUseDST = stored.UseDaylightSavings;
        bool fromUseDST = stored.LastSunShiftUseDaylightSavings;
        SunAnchor from = new(fromDate, fromLatitude, fromLongitude, fromUseDST);
        SunAnchor to = new(target, toLatitude, toLongitude, toUseDST);

        if (fromDate.Date == target
            && fromLatitude == toLatitude
            && fromLongitude == toLongitude
            && fromUseDST == toUseDST)
            return stored;

        return SunShifter.BuildPreview(stored, from, to);
    }

    private void OnEnvironmentalPreviewSweepStateChanged(bool running)
    {
        _environmentalCurveEditor?.SetPreviewSweepRunning(running);
        _previewSweepButton?.Text = running
            ? L("Settings_Environmental_PreviewSweep_Cancel_Button", "Cancel")
            : L("Settings_Environmental_PreviewSweep_Active_Button", "Preview next 24 hours");

        UpdatePreviewSweepEnabled();
        ApplyEnvironmentalPreviewHardwareState(_environmentalSunOverlayDate);
    }

    private void OnEnvironmentalPreviewSweepProgress(double t) =>
        _environmentalCurveEditor?.SetPreviewSweepCursor(t);

    private void OnEnvironmentalCurveEngagedStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(BrightnessFlyoutWindow.IsBrightnessCurveEnabled))
            and not (nameof(BrightnessFlyoutWindow.IsNightLightCurveEnabled)))
            return;

        UpdatePreviewSweepEnabled();
    }

    private void UpdatePreviewSweepEnabled()
    {
        if (_previewSweepButton == null) return;

        bool engaged = AppServices.BrightnessFlyout?.IsBrightnessCurveEnabled == true
                       || AppServices.BrightnessFlyout?.IsNightLightCurveEnabled == true;
        _previewSweepButton.IsEnabled = engaged;
        if (!engaged)
            _previewSweepButton.Text =
                L("Settings_Environmental_PreviewSweep_Idle_Button", "Live preview next 24 hours");
        else if (_previewSweepButton.Text != L("Settings_Environmental_PreviewSweep_Cancel_Button", "Cancel"))
            _previewSweepButton.Text = L("Settings_Environmental_PreviewSweep_Active_Button", "Preview next 24 hours");
    }

    /// <summary>
    /// Toggles the environmental 24-hour sweep against the currently previewed date.
    /// </summary>
    private void ToggleEnvironmentalPreviewSweep()
    {
        BrightnessFlyoutWindow? flyout = AppServices.BrightnessFlyout;
        if (flyout == null) return;

        CommitEnvironmentalSunOverlayDate();
        if (_environmentalSunOverlayDate.Date == DateTime.Today)
        {
            flyout.TogglePreviewSweep();
            return;
        }

        EnvironmentalCurve? previewCurve = BuildEnvironmentalPreviewCurve(ResolvePreviewTarget());
        if (previewCurve == null)
        {
            flyout.TogglePreviewSweep();
            return;
        }

        flyout.TogglePreviewSweep(previewCurve, previewCurve);
    }

    /// <summary>
    /// Applies the current preview date's transformed curve to the displays.
    /// </summary>
    private void ApplyEnvironmentalPreviewHardwareState(DateTime previewDate)
    {
        BrightnessFlyoutWindow? flyout = AppServices.BrightnessFlyout;
        if (flyout == null) return;

        if (previewDate.Date == DateTime.Today)
        {
            flyout.ClearPreviewDateCurve();
            return;
        }

        EnvironmentalCurve? previewCurve = BuildEnvironmentalPreviewCurve(previewDate.Date);
        if (previewCurve == null)
        {
            flyout.ClearPreviewDateCurve();
            return;
        }

        flyout.ApplyPreviewDateCurve(previewCurve, previewCurve);
    }

    /// <summary>
    /// Clears any display hardware state owned by the current preview date.
    /// </summary>
    private static void ClearEnvironmentalPreviewHardwareState() =>
        AppServices.BrightnessFlyout?.ClearPreviewDateCurve();

    /// <summary>
    /// Builds a frozen curve snapshot matching a settings page preview date.
    /// </summary>
    private EnvironmentalCurve? BuildEnvironmentalPreviewCurve(DateTime target)
    {
        EnvironmentalCurve? stored = SelectedEnvironmentalCurve();
        if (stored == null) return null;

        EnvironmentalCurve display = ResolveDisplayCurve(stored, target);
        (double start, double end) = ResolveDisplayDisabledPeriod(stored, target);

        EnvironmentalCurve preview = new()
        {
            Brightness = CloneEnvironmentalCurvePoints(display.Brightness),
            NightLight = CloneEnvironmentalCurvePoints(display.NightLight),
            BrightnessOffset = CloneEnvironmentalCurvePoints(display.BrightnessOffset),
            NightLightOffset = CloneEnvironmentalCurvePoints(display.NightLightOffset),
            BrightnessOffsetMin = display.BrightnessOffsetMin,
            BrightnessOffsetMax = display.BrightnessOffsetMax,
            NightLightOffsetMin = display.NightLightOffsetMin,
            NightLightOffsetMax = display.NightLightOffsetMax,
            FollowTheSun = display.FollowTheSun,
            BrightnessAnchor = display.BrightnessAnchor,
            UseDaylightSavings = display.UseDaylightSavings,
            DisabledPeriodEnabled = stored.DisabledPeriodEnabled,
            DisabledPeriodStart = start,
            DisabledPeriodEnd = end,
            DisabledPeriodFollowTheSun = stored.DisabledPeriodFollowTheSun,
            DisabledPeriodAnchor = stored.DisabledPeriodAnchor,
        };
        preview.EnsureNormalized();
        return preview;
    }

    /// <summary>
    /// Clones environmental curve points for a preview sweep snapshot.
    /// </summary>
    private static List<EnvironmentalCurvePoint> CloneEnvironmentalCurvePoints(List<EnvironmentalCurvePoint> source)
    {
        List<EnvironmentalCurvePoint> clone = new(source.Count);
        foreach (EnvironmentalCurvePoint point in source)
            clone.Add(new EnvironmentalCurvePoint { Time = point.Time, Value = point.Value });
        return clone;
    }

    private void OnEnvironmentalExitPreviewRequested() =>
        ApplyEnvironmentalSunOverlayDate(DateTime.Today);

    /// <summary>
    /// Steps the preview date when the focused date box receives mouse-wheel input.
    /// </summary>
    private void OnEnvironmentalSunOverlayDateBoxPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_sunOverlayDateBox?.IsKeyboardFocusWithin != true) return;

        int direction = e.Delta.Y > 0 ? -1 : 1;
        StepEnvironmentalSunOverlayDate(e.KeyModifiers.HasFlag(KeyModifiers.Control)
            ? d => d.AddMonths(direction)
            : d => d.AddDays(direction));
        e.Handled = true;
    }

    /// <summary>
    /// Opens or closes the environmental preview date calendar popup.
    /// </summary>
    private void ToggleEnvironmentalSunOverlayCalendar()
    {
        if (_sunOverlayDatePopup == null) return;

        if (_sunOverlayDatePopup.IsOpen)
        {
            _sunOverlayDatePopup.IsOpen = false;
            _sunOverlayDateBox?.Focus();
            return;
        }

        CommitEnvironmentalSunOverlayDate();
        SyncEnvironmentalSunOverlayCalendar(_environmentalSunOverlayDate);
        _sunOverlayDatePopup.IsOpen = true;
        _sunOverlayCalendar?.Focus();
    }

    /// <summary>
    /// Applies popup calendar selections to the environmental preview date.
    /// </summary>
    private void OnEnvironmentalSunOverlayCalendarSelectedDatesChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSunOverlayCalendarEvents) return;
        if (_sunOverlayCalendar?.SelectedDate is not DateTime selected) return;

        ApplyEnvironmentalSunOverlayDate(selected);
    }

    /// <summary>
    /// Closes the popup after a day is clicked in the environmental preview calendar.
    /// </summary>
    private void OnEnvironmentalSunOverlayCalendarPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!IsEnvironmentalSunOverlayCalendarDayButtonSource(e.Source)) return;

        if (_sunOverlayCalendar?.SelectedDate is DateTime selected)
            ApplyEnvironmentalSunOverlayDate(selected);

        CloseEnvironmentalSunOverlayCalendar();
    }

    /// <summary>
    /// Handles keyboard close and commit commands inside the preview calendar.
    /// </summary>
    private void OnEnvironmentalSunOverlayCalendarKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseEnvironmentalSunOverlayCalendar();
                e.Handled = true;
                break;
            case Key.Enter:
                if (_sunOverlayCalendar?.SelectedDate is DateTime selected)
                    ApplyEnvironmentalSunOverlayDate(selected);
                CloseEnvironmentalSunOverlayCalendar();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Closes the environmental preview date calendar and returns focus to the date field.
    /// </summary>
    private void CloseEnvironmentalSunOverlayCalendar()
    {
        if (_sunOverlayDatePopup != null)
            _sunOverlayDatePopup.IsOpen = false;
        _sunOverlayDateBox?.Focus();
    }

    /// <summary>
    /// Detaches and releases the environmental preview date calendar popup.
    /// </summary>
    private void ReleaseEnvironmentalSunOverlayCalendar()
    {
        if (_sunOverlayDatePopup != null)
            _sunOverlayDatePopup.IsOpen = false;

        if (_sunOverlayCalendar != null)
        {
            _sunOverlayCalendar.SelectedDatesChanged -= OnEnvironmentalSunOverlayCalendarSelectedDatesChanged;
            _sunOverlayCalendar.PointerReleased -= OnEnvironmentalSunOverlayCalendarPointerReleased;
            _sunOverlayCalendar.KeyDown -= OnEnvironmentalSunOverlayCalendarKeyDown;
        }

        _sunOverlayCalendar = null;
        _sunOverlayDatePopup = null;
        _suppressSunOverlayCalendarEvents = false;
    }

    /// <summary>
    /// Returns whether a pointer event came from a calendar day button.
    /// </summary>
    private static bool IsEnvironmentalSunOverlayCalendarDayButtonSource(object? source)
    {
        if (source is CalendarDayButton) return true;
        if (source is not Visual visual) return false;

        foreach (Visual ancestor in visual.GetVisualAncestors())
        {
            if (ancestor is CalendarDayButton) return true;
        }

        return false;
    }

    private void ResetEnvironmentalSunOverlayDate()
    {
        _environmentalSunOverlayDate = DateTime.Today;
        SyncEnvironmentalSunOverlayDateBox(_environmentalSunOverlayDate);
        _environmentalCurveEditor?.SetSunOverlayDate(null);
        ApplyEnvironmentalPreviewState(DateTime.Today);
    }

    private void ApplyEnvironmentalSunOverlayDate(DateTime date)
    {
        DateTime newDate = date.Date;
        _environmentalSunOverlayDate = newDate;
        SyncEnvironmentalSunOverlayDateBox(newDate);
        _environmentalCurveEditor?.SetSunOverlayDate(newDate == DateTime.Today ? null : newDate);
        ApplyEnvironmentalPreviewState(newDate);
    }

    private void CommitEnvironmentalSunOverlayDate()
    {
        if (_suppressEnvironmentalEvents || _sunOverlayDateBox == null) return;

        if (TryParseSunOverlayDate(_sunOverlayDateBox.Text ?? string.Empty, out DateTime parsed))
            ApplyEnvironmentalSunOverlayDate(parsed);
        else
            SyncEnvironmentalSunOverlayDateBox(_environmentalSunOverlayDate);
    }

    private void StepEnvironmentalSunOverlayDate(Func<DateTime, DateTime> step)
    {
        DateTime current = _sunOverlayDateBox != null &&
                           TryParseSunOverlayDate(_sunOverlayDateBox.Text ?? string.Empty, out DateTime parsed)
            ? parsed
            : _environmentalSunOverlayDate;

        DateTime next;
        try { next = step(current); }
        catch (ArgumentOutOfRangeException) { next = current; }

        ApplyEnvironmentalSunOverlayDate(next);
    }

    private void SyncEnvironmentalSunOverlayDateBox(DateTime date)
    {
        if (_sunOverlayDateBox == null) return;
        _suppressEnvironmentalEvents = true;
        try
        {
            _sunOverlayDateBox.Text = FormatSunOverlayDate(date);
            SyncEnvironmentalSunOverlayCalendar(date);
        }
        finally
        {
            _suppressEnvironmentalEvents = false;
        }
    }

    /// <summary>
    /// Synchronizes the popup calendar to the currently active environmental preview date.
    /// </summary>
    private void SyncEnvironmentalSunOverlayCalendar(DateTime date)
    {
        if (_sunOverlayCalendar == null) return;

        DateTime normalized = date.Date;
        _suppressSunOverlayCalendarEvents = true;
        try
        {
            _sunOverlayCalendar.DisplayDate = normalized;
            _sunOverlayCalendar.SelectedDate = normalized;
        }
        finally
        {
            _suppressSunOverlayCalendarEvents = false;
        }
    }

    private DateTime ResolvePreviewTarget() =>
        _environmentalSunOverlayDate.Date != DateTime.Today ? _environmentalSunOverlayDate.Date : DateTime.Today;
}
