using System.Globalization;
using Avalonia.Threading;
using TrayAppDotNETCommon.UI.Controls;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private void SeedEnvironmentalPage()
    {
        if (_environmentalCurveEditor == null) return;

        _suppressEnvironmentalEvents = true;
        try
        {
            _showBrightnessCurveToggle!.IsChecked = _settings.EnvironmentalShowBrightnessCurve;
            _showNightLightCurveToggle!.IsChecked = _settings.EnvironmentalShowNightLightCurve;
            _offsetModeToggle!.IsChecked = _settings.EnvironmentalOffsetMode;
            _showCursorReadoutToggle!.IsChecked = _settings.EnvironmentalShowCursorReadout;
            _showSunOverlayToggle!.IsChecked = _settings.EnvironmentalShowSunOverlay;
            _latitudeBox!.Text = FormatCoordinate(_settings.EnvironmentalLatitude);
            _longitudeBox!.Text = FormatCoordinate(_settings.EnvironmentalLongitude);
            PopulateEnvironmentalProfileCombo();
            ResetEnvironmentalSunOverlayDate();
            ApplyEnvironmentalCurveVisibility();
            UpdatePreviewSweepEnabled();
        }
        finally
        {
            _suppressEnvironmentalEvents = false;
        }

        _environmentalCurveEditor.SetOffsetMode(_settings.EnvironmentalOffsetMode);
        _environmentalCurveEditor.SetShowCursorReadout(_settings.EnvironmentalShowCursorReadout);
        _environmentalCurveEditor.SetShowSunOverlay(_settings.EnvironmentalShowSunOverlay);
        _environmentalCurveEditor.SetGeoLocation(_settings.EnvironmentalLatitude, _settings.EnvironmentalLongitude);
        _environmentalCurveEditor.SetSmoothness(_settings.EnvironmentalCurveSmoothness / 100.0);
        LoadEnvironmentalCurveForSelectedProfile();
    }

    private void AttachEnvironmentalEvents()
    {
        if (_environmentalEventsAttached) return;

        if (_profileManager != null)
            _profileManager.ProfilesListChanged += OnEnvironmentalProfilesListChanged;

        if (_brightnessRangeProvider != null)
        {
            _brightnessRangeProvider.LiveBrightnessRangeChanged += OnLiveBrightnessRangeChanged;
            _brightnessRangeProvider.EmitCurrent();
        }

        _environmentalFlyout = AppServices.BrightnessFlyout;
        if (_environmentalFlyout != null)
        {
            _environmentalFlyout.PropertyChanged += OnEnvironmentalCurveEngagedStateChanged;
            _environmentalFlyout.PreviewSweepStateChanged += OnEnvironmentalPreviewSweepStateChanged;
            _environmentalFlyout.PreviewSweepProgress += OnEnvironmentalPreviewSweepProgress;
        }

        if (_environmentalCurveEditor != null)
        {
            _environmentalCurveEditor.CurveChanged += OnEnvironmentalCurveChanged;
            _environmentalCurveEditor.ExitPreviewModeRequested += OnEnvironmentalExitPreviewRequested;
            _environmentalCurveEditor.DisabledPeriodChanged += OnEnvironmentalDisabledPeriodChanged;
        }

        WireEnvironmentalColorCallbacks();

        _environmentalEventsAttached = true;
    }

    private void StopEnvironmentalPageSession()
    {
        FlushDebouncedCurveSave();

        if (!_environmentalEventsAttached) return;

        if (_profileManager != null)
            _profileManager.ProfilesListChanged -= OnEnvironmentalProfilesListChanged;

        if (_brightnessRangeProvider != null)
            _brightnessRangeProvider.LiveBrightnessRangeChanged -= OnLiveBrightnessRangeChanged;

        if (_environmentalFlyout != null)
        {
            _environmentalFlyout.PropertyChanged -= OnEnvironmentalCurveEngagedStateChanged;
            _environmentalFlyout.PreviewSweepStateChanged -= OnEnvironmentalPreviewSweepStateChanged;
            _environmentalFlyout.PreviewSweepProgress -= OnEnvironmentalPreviewSweepProgress;
            _environmentalFlyout = null;
        }

        if (_environmentalCurveEditor != null)
        {
            _environmentalCurveEditor.CurveChanged -= OnEnvironmentalCurveChanged;
            _environmentalCurveEditor.ExitPreviewModeRequested -= OnEnvironmentalExitPreviewRequested;
            _environmentalCurveEditor.DisabledPeriodChanged -= OnEnvironmentalDisabledPeriodChanged;
            _environmentalCurveEditor.SetPreviewSweepRunning(false);
        }

        UnwireEnvironmentalColorCallbacks();
        _environmentalEventsAttached = false;
    }

    private void OnEnvironmentalProfilesListChanged()
    {
        if (_environmentalProfileCombo == null) return;

        int previous = _environmentalProfileIndex;
        _suppressEnvironmentalEvents = true;
        try
        {
            PopulateEnvironmentalProfileCombo();
            if (previous >= 0 && previous < _environmentalProfileCombo.Items.Count)
            {
                _environmentalProfileIndex = previous;
                _environmentalProfileCombo.SelectedIndex = previous;
            }
        }
        finally
        {
            _suppressEnvironmentalEvents = false;
        }
    }

    private void OnLiveBrightnessRangeChanged(double? min, double? max) =>
        Dispatcher.UIThread.Post(() => _environmentalCurveEditor?.SetActiveBrightnessRange(min, max));

    private void PopulateEnvironmentalProfileCombo()
    {
        if (_environmentalProfileCombo == null) return;

        _environmentalProfileCombo.Items.Clear();
        if (_profileManager == null)
        {
            _environmentalProfileCombo.IsEnabled = false;
            _environmentalProfileIndex = -1;
            return;
        }

        for (int i = 0; i < _profileManager.Profiles.Profiles.Count; i++)
        {
            string label = string.IsNullOrWhiteSpace(_profileManager.GetName(i))
                ? string.Format(CultureInfo.CurrentCulture,
                    L("Settings_Environmental_Profile_Default_Format", "Profile {0}"), i + 1)
                : string.Format(CultureInfo.CurrentCulture,
                    L("Settings_Environmental_Profile_Named_Format", "{0} ({1})"), _profileManager.GetName(i), i + 1);
            _environmentalProfileCombo.Items.Add(new SettingsComboBoxItem(i, label, Palette));
        }

        _environmentalProfileIndex = _profileManager.SelectedIndex;
        if (_environmentalProfileIndex < 0 || _environmentalProfileIndex >= _environmentalProfileCombo.Items.Count)
            _environmentalProfileIndex = _environmentalProfileCombo.Items.Count > 0 ? 0 : -1;

        if (_environmentalProfileIndex >= 0)
            _environmentalProfileCombo.SelectedIndex = _environmentalProfileIndex;
    }

    private EnvironmentalCurve? SelectedEnvironmentalCurve()
    {
        if (_profileManager == null) return null;
        if (_environmentalProfileIndex < 0 ||
            _environmentalProfileIndex >= _profileManager.Profiles.Profiles.Count) return null;
        return _profileManager.Profiles.Profiles[_environmentalProfileIndex].EnvironmentalCurve;
    }

    private void LoadEnvironmentalCurveForSelectedProfile()
    {
        EnvironmentalCurve? curve = SelectedEnvironmentalCurve();
        if (curve == null || _environmentalCurveEditor == null) return;

        curve.EnsureNormalized();
        BootstrapSunShiftAnchor(curve);
        BootstrapDisabledPeriodSunShiftAnchor(curve);

        _suppressEnvironmentalEvents = true;
        try
        {
            _followTheSunToggle!.IsChecked = curve.FollowTheSun;
            _useDaylightSavingsToggle!.IsChecked = curve.UseDaylightSavings;
            _disabledPeriodToggle!.IsChecked = curve.DisabledPeriodEnabled;
            _disabledPeriodFollowTheSunToggle!.IsChecked = curve.DisabledPeriodFollowTheSun;
            ApplyEnvironmentalDisabledPeriodFieldVisibility(curve.DisabledPeriodEnabled);
        }
        finally
        {
            _suppressEnvironmentalEvents = false;
        }

        _environmentalCurveEditor.SetUseDaylightSavings(curve.UseDaylightSavings);
        ApplyEnvironmentalPreviewState(_environmentalSunOverlayDate);
    }

    private void ApplyEnvironmentalPreviewState(DateTime previewDate)
    {
        if (_environmentalCurveEditor == null) return;

        EnvironmentalCurve? stored = SelectedEnvironmentalCurve();
        if (stored == null) return;

        stored.EnsureNormalized();
        bool inPreview = previewDate.Date != DateTime.Today;
        DateTime target = inPreview ? previewDate.Date : DateTime.Today;
        _environmentalCurveEditor.SetPreviewMode(inPreview);
        _previewSweepButton?.IsVisible = !inPreview;
        _environmentalCurveDisplay = ResolveDisplayCurve(stored, target);
        _environmentalCurveEditor.SetCurves(_environmentalCurveDisplay);

        (double start, double end) = ResolveDisplayDisabledPeriod(stored, target);
        _environmentalCurveEditor.SetDisabledPeriod(stored.DisabledPeriodEnabled, start, end);

        _suppressEnvironmentalEvents = true;
        try
        {
            _disabledPeriodStartBox?.Text = FormatDisabledPeriodTime(start);
            _disabledPeriodEndBox?.Text = FormatDisabledPeriodTime(end);
        }
        finally
        {
            _suppressEnvironmentalEvents = false;
        }
    }
}
