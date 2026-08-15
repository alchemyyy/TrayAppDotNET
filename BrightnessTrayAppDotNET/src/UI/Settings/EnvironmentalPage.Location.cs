using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using BrightnessTrayAppDotNET.UI.Settings.Environmental;
using TrayAppDotNETCommon.UI.Controls;

namespace BrightnessTrayAppDotNET.UI.Settings;

public sealed partial class BrightnessSettingsWindow
{
    private void OnEnvironmentalCoordinateKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        CommitEnvironmentalCoordinates();
        e.Handled = true;
    }

    private void CommitEnvironmentalCoordinates()
    {
        if (_suppressEnvironmentalEvents) return;
        if (_latitudeBox == null || _longitudeBox == null) return;

        bool changed = false;
        if (TryParseCoordinate(_latitudeBox.Text, out double latitude))
        {
            double clamped = Math.Clamp(latitude, -90.0, 90.0);
            if (Math.Abs(_settings.EnvironmentalLatitude - clamped) > 1e-9)
            {
                _settings.EnvironmentalLatitude = clamped;
                changed = true;
            }

            _latitudeBox.Text = FormatCoordinate(clamped);
        }
        else
            _latitudeBox.Text = FormatCoordinate(_settings.EnvironmentalLatitude);

        if (TryParseCoordinate(_longitudeBox.Text, out double longitude))
        {
            double clamped = Math.Clamp(longitude, -180.0, 180.0);
            if (Math.Abs(_settings.EnvironmentalLongitude - clamped) > 1e-9)
            {
                _settings.EnvironmentalLongitude = clamped;
                changed = true;
            }

            _longitudeBox.Text = FormatCoordinate(clamped);
        }
        else
            _longitudeBox.Text = FormatCoordinate(_settings.EnvironmentalLongitude);

        if (!changed) return;

        Save();
        _environmentalCurveEditor?.SetGeoLocation(_settings.EnvironmentalLatitude, _settings.EnvironmentalLongitude);
        ApplyEnvironmentalPreviewState(_environmentalSunOverlayDate);
        NotifyRuntimeCurveChanged();
    }

    private async Task ApproximateEnvironmentalLocationFromIPAsync(SettingsButton button)
    {
        long pageGeneration = _environmentalPageGeneration;
        CancellationToken cancellationToken = _environmentalPageResources?.CancellationToken ??
                                              new CancellationToken(canceled: true);
        if (!IsCurrentEnvironmentalPage(pageGeneration)) return;

        button.IsEnabled = false;
        string original = button.Text;
        button.Text = L(nameof(AppStrings.Settings_Environmental_ApproxFromIP_Locating), "Locating...");
        try
        {
            using HttpResponseMessage response = await EnvironmentalHttpClient
                .GetAsync("https://am.i.mullvad.net/json", cancellationToken)
                .ConfigureAwait(true);
            if (!IsCurrentEnvironmentalPage(pageGeneration)) return;
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
            if (!IsCurrentEnvironmentalPage(pageGeneration)) return;

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("latitude", out JsonElement latitudeElement)) return;
            if (!root.TryGetProperty("longitude", out JsonElement longitudeElement)) return;
            if (!latitudeElement.TryGetDouble(out double latitude)) return;
            if (!longitudeElement.TryGetDouble(out double longitude)) return;

            ApplyEnvironmentalCoordinates(latitude, longitude);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            WPFLog.Log("BrightnessSettingsWindow.ApproximateEnvironmentalLocationFromIPAsync: page retired");
        }
        catch (Exception ex)
        {
            WPFLog.Log($"BrightnessSettingsWindow.ApproximateEnvironmentalLocationFromIPAsync: {ex.Message}");
        }
        finally
        {
            if (IsCurrentEnvironmentalPage(pageGeneration))
            {
                button.Text = original;
                button.IsEnabled = true;
            }
        }
    }

    private void OpenEnvironmentalMapPicker()
    {
        if (_environmentalPageResources is not { IsDisposed: false } pageResources) return;

        if (_environmentalMapPicker == null)
        {
            EnvironmentalMapPickerWindow picker = new(
                _settings.EnvironmentalLatitude,
                _settings.EnvironmentalLongitude,
                Palette,
                AppServices.Theme ?? AppTheme.Default,
                _settings,
                ResolveEffectiveIsLight()) { WindowStartupLocation = WindowStartupLocation.CenterOwner };
            picker.Applied += ApplyEnvironmentalCoordinates;
            picker.Closed += OnEnvironmentalMapPickerClosed;
            pageResources.Add(() =>
            {
                picker.Applied -= ApplyEnvironmentalCoordinates;
                picker.Closed -= OnEnvironmentalMapPickerClosed;
                picker.CloseForPageRetirement();
                if (ReferenceEquals(_environmentalMapPicker, picker)) _environmentalMapPicker = null;
            });
            _environmentalMapPicker = picker;
        }

        if (!_environmentalMapPicker.IsVisible)
            _environmentalMapPicker.Show(this);
        else
            _environmentalMapPicker.Activate();
    }

    private void CloseEnvironmentalMapPicker()
    {
        EnvironmentalMapPickerWindow? picker = _environmentalMapPicker;
        _environmentalMapPicker = null;
        if (picker == null) return;

        picker.Applied -= ApplyEnvironmentalCoordinates;
        picker.Closed -= OnEnvironmentalMapPickerClosed;
        try { picker.CloseForPageRetirement(); }
        catch (Exception exception)
        {
            WPFLog.Log($"BrightnessSettingsWindow.CloseEnvironmentalMapPicker: {exception.Message}");
        }
    }

    private void OnEnvironmentalMapPickerClosed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(_environmentalMapPicker, sender)) _environmentalMapPicker = null;
    }

    private void ApplyEnvironmentalCoordinates(double latitude, double longitude)
    {
        _settings.EnvironmentalLatitude = Math.Clamp(latitude, -90.0, 90.0);
        _settings.EnvironmentalLongitude = Math.Clamp(longitude, -180.0, 180.0);
        _latitudeBox?.Text = FormatCoordinate(_settings.EnvironmentalLatitude);
        _longitudeBox?.Text = FormatCoordinate(_settings.EnvironmentalLongitude);
        Save();
        _environmentalCurveEditor?.SetGeoLocation(_settings.EnvironmentalLatitude, _settings.EnvironmentalLongitude);
        ApplyEnvironmentalPreviewState(_environmentalSunOverlayDate);
        NotifyRuntimeCurveChanged();
    }
}
