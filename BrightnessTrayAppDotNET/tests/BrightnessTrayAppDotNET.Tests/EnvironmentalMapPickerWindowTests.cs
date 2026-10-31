using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.UI.Settings.Environmental;
using TrayAppDotNETCommon.UI;
using Xunit;
using BrightnessAppTheme = BrightnessTrayAppDotNET.Visuals.AppTheme;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class EnvironmentalMapPickerWindowTests
{
    [Theory]
    [InlineData(WindowCloseReason.Undefined)]
    [InlineData(WindowCloseReason.WindowClosing)]
    public void UserCloseKeepsReusablePickerAlive(WindowCloseReason closeReason)
    {
        bool cancel = EnvironmentalMapPickerWindow.ShouldCancelCloseForReuse(
            isRetiring: false,
            closeReason);

        Assert.True(cancel);
    }

    [Theory]
    [InlineData(WindowCloseReason.OwnerWindowClosing)]
    [InlineData(WindowCloseReason.ApplicationShutdown)]
    [InlineData(WindowCloseReason.OSShutdown)]
    public void OwnerAndApplicationCloseCanRetirePicker(WindowCloseReason closeReason)
    {
        bool cancel = EnvironmentalMapPickerWindow.ShouldCancelCloseForReuse(
            isRetiring: false,
            closeReason);

        Assert.False(cancel);
    }

    [Fact]
    public void ExplicitPageRetirementCanClosePicker()
    {
        bool cancel = EnvironmentalMapPickerWindow.ShouldCancelCloseForReuse(
            isRetiring: true,
            WindowCloseReason.WindowClosing);

        Assert.False(cancel);
    }

    [Fact]
    public void FineWheelDeltasAddUpToAZoomStepAndHorizontalOnesDoNot() => AvaloniaTestHost.RunWithFluentTheme(() =>
    {
        AppSettings settings = new();
        BrightnessAppTheme theme = BrightnessAppTheme.Default;
        EnvironmentalMapPickerCanvas map = new(
            AppSettingsPalette.Create(theme, settings, isLight: false),
            theme.EnvironmentalMapPin.For(false));
        Window window = new() { Width = 400, Height = 300, Content = map };
        try
        {
            window.Show();
            window.UpdateLayout();
            Avalonia.Point center = new(x: 200, y: 150);

            // The first render fits the map to the viewport; zoom is measured from there
            window.MouseMove(center);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            double initialScale = map.ViewportScale;

            window.MouseWheel(center, new Avalonia.Vector(x: 1, y: 0));
            Assert.Equal(initialScale, map.ViewportScale);

            // A precision touchpad reports fractions of a notch; two halves make one zoom step
            window.MouseWheel(center, new Avalonia.Vector(x: 0, y: 0.5));
            Assert.Equal(initialScale, map.ViewportScale);
            window.MouseWheel(center, new Avalonia.Vector(x: 0, y: 0.5));
            Assert.True(map.ViewportScale > initialScale);
        }
        finally
        {
            window.Close();
            map.Dispose();
        }
    });

    [Fact]
    public void EveryShowStartsFromTheCoordinateItIsGiven() => AvaloniaTestHost.RunWithFluentTheme(() =>
    {
        AppSettings settings = new();
        BrightnessAppTheme theme = BrightnessAppTheme.Default;
        Window owner = new();
        EnvironmentalMapPickerWindow picker = new(
            AppSettingsPalette.Create(theme, settings, isLight: false),
            theme,
            settings,
            isLight: false);
        try
        {
            owner.Show();
            picker.ShowAt(owner, latitude: 48.2, longitude: 16.4);
            Assert.Equal(new GeoCoordinate(48.2, 16.4), picker.SelectedCoordinate);

            // An aborted session hides the reusable picker; the next show must not keep its pin
            picker.Hide();
            picker.ShowAt(owner, latitude: -33.9, longitude: 151.2);
            Assert.Equal(new GeoCoordinate(-33.9, 151.2), picker.SelectedCoordinate);
        }
        finally
        {
            picker.CloseForPageRetirement();
            owner.Close();
        }
    });

    [Theory]
    [InlineData(KeyModifiers.None, EnvironmentalCurveEditor.KeyboardTimeStepFine)]
    [InlineData(KeyModifiers.Control, EnvironmentalCurveEditor.KeyboardTimeStepCoarse)]
    [InlineData(KeyModifiers.Shift, EnvironmentalCurveEditor.KeyboardTimeStepOneMinute)]
    public void CurveKeysMoveAPointByWholeTimeSteps(KeyModifiers modifiers, double expectedTimeStep)
    {
        (double timeStep, _) = EnvironmentalCurveEditor.ResolveNudgeSteps(modifiers, offsetMode: false);

        Assert.Equal(expectedTimeStep, timeStep);
        Assert.Equal(expected: 15, EnvironmentalCurveEditor.KeyboardTimeStepFine * 24 * 60, precision: 9);
        Assert.Equal(expected: 60, EnvironmentalCurveEditor.KeyboardTimeStepCoarse * 24 * 60, precision: 9);
    }
}
