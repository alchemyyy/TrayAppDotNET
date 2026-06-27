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
        button.IsEnabled = false;
        string original = button.Text;
        button.Text = L("Settings_Environmental_ApproxFromIP_Locating", "Locating...");
        try
        {
            using HttpResponseMessage response = await EnvironmentalHttpClient
                .GetAsync("https://am.i.mullvad.net/json")
                .ConfigureAwait(true);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("latitude", out JsonElement latitudeElement)) return;
            if (!root.TryGetProperty("longitude", out JsonElement longitudeElement)) return;
            if (!latitudeElement.TryGetDouble(out double latitude)) return;
            if (!longitudeElement.TryGetDouble(out double longitude)) return;

            ApplyEnvironmentalCoordinates(latitude, longitude);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"BrightnessSettingsWindow.ApproximateEnvironmentalLocationFromIPAsync: {ex.Message}");
        }
        finally
        {
            button.Text = original;
            button.IsEnabled = true;
        }
    }

    private void OpenEnvironmentalMapPicker()
    {
        EnvironmentalMapPickerWindow picker = new(
            _settings.EnvironmentalLatitude,
            _settings.EnvironmentalLongitude,
            Palette,
            AppServices.Theme ?? AppTheme.Default,
            _settings,
            ResolveEffectiveIsLight()) { WindowStartupLocation = WindowStartupLocation.CenterOwner, };
        picker.Applied += ApplyEnvironmentalCoordinates;
        picker.Show(this);
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
