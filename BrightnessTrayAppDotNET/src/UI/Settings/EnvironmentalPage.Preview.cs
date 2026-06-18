using System.ComponentModel;
using System.Globalization;
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

    private void OnEnvironmentalExitPreviewRequested() =>
        ApplyEnvironmentalSunOverlayDate(DateTime.Today);

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
        }
        finally
        {
            _suppressEnvironmentalEvents = false;
        }
    }

    private DateTime ResolvePreviewTarget() =>
        _environmentalSunOverlayDate.Date != DateTime.Today ? _environmentalSunOverlayDate.Date : DateTime.Today;
}
