using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using TrayAppDotNETCommon.UI.Controls;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private void OnEnvironmentalCurveVisibilityChanged(object? sender, bool _)
    {
        if (_suppressEnvironmentalEvents) return;
        if (_showBrightnessCurveToggle == null || _showNightLightCurveToggle == null) return;

        if (!_showBrightnessCurveToggle.IsChecked && !_showNightLightCurveToggle.IsChecked)
        {
            _suppressEnvironmentalEvents = true;
            try
            {
                if (sender is SettingsToggle toggle) toggle.IsChecked = true;
            }
            finally
            {
                _suppressEnvironmentalEvents = false;
            }
        }

        _settings.EnvironmentalShowBrightnessCurve = _showBrightnessCurveToggle.IsChecked;
        _settings.EnvironmentalShowNightLightCurve = _showNightLightCurveToggle.IsChecked;
        Save();
        ApplyEnvironmentalCurveVisibility();
    }

    private void ApplyEnvironmentalCurveVisibility()
    {
        bool showBrightness = _showBrightnessCurveToggle?.IsChecked == true;
        bool showNightLight = _showNightLightCurveToggle?.IsChecked == true;
        _environmentalCurveEditor?.SetVisibility(showBrightness, showNightLight);

        _brightnessLegendItem?.IsVisible = showBrightness;
        _nightLightLegendItem?.IsVisible = showNightLight;
        _legendPanel?.IsVisible = true;
        UpdatePreviewSweepEnabled();
    }

    private void ApplyEnvironmentalDisabledPeriodFieldVisibility(bool enabled)
    {
        _disabledPeriodFieldsRow?.IsVisible = enabled;
        _disabledPeriodFollowTheSunRow?.IsVisible = enabled;
    }

    private void CommitEnvironmentalDisabledPeriodTime(bool isStart)
    {
        if (_suppressEnvironmentalEvents) return;
        EnvironmentalCurve? curve = SelectedEnvironmentalCurve();
        if (curve == null) return;

        TextBox? box = isStart ? _disabledPeriodStartBox : _disabledPeriodEndBox;
        if (box == null) return;

        if (!TryParseDisabledPeriodTime(box.Text ?? string.Empty, out double t))
        {
            (double start, double end) = ResolveDisplayDisabledPeriod(curve, ResolvePreviewTarget());
            box.Text = FormatDisabledPeriodTime(isStart ? start : end);
            return;
        }

        if (isStart) curve.DisabledPeriodStart = t;
        else curve.DisabledPeriodEnd = t;

        if (curve.DisabledPeriodFollowTheSun)
            StampDisabledPeriodSunShiftAnchor(curve);

        _profileManager?.Save();
        ApplyEnvironmentalPreviewState(_environmentalSunOverlayDate);
        NotifyRuntimeCurveChanged();
    }

    private void OnDisabledPeriodTimeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitEnvironmentalDisabledPeriodTime(ReferenceEquals(sender, _disabledPeriodStartBox));
        e.Handled = true;
    }

    private void OnEnvironmentalDisabledPeriodChanged(double start, double end)
    {
        if (_suppressEnvironmentalEvents) return;
        EnvironmentalCurve? curve = SelectedEnvironmentalCurve();
        if (curve == null) return;

        curve.DisabledPeriodStart = start;
        curve.DisabledPeriodEnd = end;
        if (curve.DisabledPeriodFollowTheSun)
            StampDisabledPeriodSunShiftAnchor(curve);

        ApplyEnvironmentalPreviewState(_environmentalSunOverlayDate);
        NotifyRuntimeCurveChanged();
        ScheduleDebouncedCurveSave();
    }

    private async Task ResetEnvironmentalCurvesAsync()
    {
        long pageGeneration = _environmentalPageGeneration;
        EnvironmentalCurve? curve = SelectedEnvironmentalCurve();
        if (curve == null) return;

        bool ok = await ConfirmAsync(
            L("Settings_Environmental_ResetCurves_ConfirmTitle", "Reset curves?"),
            L("Settings_Environmental_ResetCurves_ConfirmMessage",
                "This resets the visible curve mode for the selected profile."),
            L("Settings_Environmental_ResetCurves_ConfirmButton", "Reset"),
            L("Common_Cancel", "Cancel"));
        if (!ok || !IsCurrentEnvironmentalPage(pageGeneration)) return;

        if (_settings.EnvironmentalOffsetMode)
        {
            curve.BrightnessOffset = EnvironmentalCurve.CreateDefaultOffset();
            curve.NightLightOffset = EnvironmentalCurve.CreateDefaultOffset();
            curve.BrightnessOffsetMin = 0.0;
            curve.BrightnessOffsetMax = 100.0;
            curve.NightLightOffsetMin = 0.0;
            curve.NightLightOffsetMax = 100.0;
        }
        else
        {
            curve.Brightness = EnvironmentalCurve.CreateDefaultBrightness();
            curve.NightLight = EnvironmentalCurve.CreateDefaultNightLight();
        }

        _profileManager?.Save();
        ApplyEnvironmentalPreviewState(_environmentalSunOverlayDate);
        NotifyRuntimeCurveChanged();
    }

    private void OnEnvironmentalCurveChanged()
    {
        EnvironmentalCurve? stored = SelectedEnvironmentalCurve();
        if (stored == null) return;

        if (_environmentalCurveDisplay is { } display && !ReferenceEquals(display, stored))
        {
            stored.Brightness = display.Brightness;
            stored.NightLight = display.NightLight;
            stored.BrightnessOffset = display.BrightnessOffset;
            stored.NightLightOffset = display.NightLightOffset;
            stored.BrightnessOffsetMin = display.BrightnessOffsetMin;
            stored.BrightnessOffsetMax = display.BrightnessOffsetMax;
            stored.NightLightOffsetMin = display.NightLightOffsetMin;
            stored.NightLightOffsetMax = display.NightLightOffsetMax;
            StampSunShiftAnchor(stored);
        }

        ScheduleDebouncedCurveSave();
        NotifyRuntimeCurveChanged();
    }

    private void NotifyRuntimeCurveChanged()
    {
        if (_environmentalCurveRuntimeNotifyQueued) return;
        _environmentalCurveRuntimeNotifyQueued = true;
        long pageGeneration = _environmentalPageGeneration;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!IsCurrentEnvironmentalPage(pageGeneration)) return;
                _environmentalCurveRuntimeNotifyQueued = false;
                AppServices.BrightnessFlyout?.RequestCurveReevaluation();
            },
            DispatcherPriority.Background);
    }

    private void ScheduleDebouncedCurveSave()
    {
        if (_profileManager == null) return;

        StopCurveSaveDebounceTimer(flushPendingSave: false);
        DispatcherTimer timer = new()
        {
            Interval = TimeSpan.FromMilliseconds(TimeConstants.EnvironmentalCurveSaveDebounceMs)
        };
        timer.Tick += OnCurveSaveDebounceTimerTick;
        _curveSaveDebounceTimer = timer;
        try { timer.Start(); }
        catch
        {
            StopCurveSaveDebounceTimer(flushPendingSave: false);
            throw;
        }
    }

    private void FlushDebouncedCurveSave()
    {
        if (_curveSaveDebounceTimer is not { IsEnabled: true }) return;

        try { _curveSaveDebounceTimer.Stop(); }
        catch
        {
            StopCurveSaveDebounceTimer(flushPendingSave: false);
            throw;
        }
        _profileManager?.Save();
    }

    private void OnCurveSaveDebounceTimerTick(object? sender, EventArgs e)
    {
        // Each arm owns a distinct timer so a queued tick cannot stop its replacement
        if (!ReferenceEquals(sender, _curveSaveDebounceTimer)) return;
        FlushDebouncedCurveSave();
    }

    private void StopCurveSaveDebounceTimer(bool flushPendingSave)
    {
        DispatcherTimer? timer = _curveSaveDebounceTimer;
        _curveSaveDebounceTimer = null;
        if (timer == null) return;

        bool hadPendingSave = timer.IsEnabled;
        try { timer.Stop(); }
        catch (Exception exception)
        {
            WPFLog.Log($"Brightness environmental save timer stop failed: {exception.Message}");
        }
        timer.Tick -= OnCurveSaveDebounceTimerTick;
        if (flushPendingSave && hadPendingSave) _profileManager?.Save();
    }
}
