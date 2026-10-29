using VolumeTrayAppDotNET.Audio;
using Xunit;

namespace VolumeTrayAppDotNET.Tests;

public sealed class AppVolumeScaleTests
{
    // FlyoutSlider ignores Value writes closer than this, so a smaller round-trip error never moves the thumb
    private const double SliderValueResolution = 0.001;
    private const int PercentSteps = 10_000;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EndpointsAreSilenceAndFullVolume(bool isLogarithmic)
    {
        Assert.Equal(0f, AppVolumeScale.LevelFromSliderPercent(0, isLogarithmic));
        Assert.Equal(1f, AppVolumeScale.LevelFromSliderPercent(100, isLogarithmic));
        Assert.Equal(0.0, AppVolumeScale.SliderPercentFromLevel(0f, isLogarithmic));
        Assert.Equal(100.0, AppVolumeScale.SliderPercentFromLevel(1f, isLogarithmic));
    }

    [Theory]
    [InlineData(-25.0, 0f)]
    [InlineData(250.0, 1f)]
    public void OutOfRangePercentsClampToTheEndpoints(double percent, float expected)
    {
        Assert.Equal(expected, AppVolumeScale.LevelFromSliderPercent(percent, isLogarithmic: true));
    }

    [Fact]
    public void LogarithmicLevelRisesStrictlyWithSliderPosition()
    {
        float previous = AppVolumeScale.LevelFromSliderPercent(0, isLogarithmic: true);
        for (int step = 1; step <= PercentSteps; step++)
        {
            float level = AppVolumeScale.LevelFromSliderPercent(step * 100.0 / PercentSteps, isLogarithmic: true);
            Assert.True(level > previous, $"Level stopped rising at {step * 100.0 / PercentSteps}%");
            previous = level;
        }
    }

    [Fact]
    public void LogarithmicPositionRisesStrictlyWithLevel()
    {
        double previous = AppVolumeScale.SliderPercentFromLevel(0f, isLogarithmic: true);
        for (int step = 1; step <= PercentSteps; step++)
        {
            double percent = AppVolumeScale.SliderPercentFromLevel(step / (float)PercentSteps, isLogarithmic: true);
            Assert.True(percent > previous, $"Position stopped rising at level {step / (float)PercentSteps}");
            previous = percent;
        }
    }

    [Fact]
    public void WindowsEchoOfASetLevelLeavesTheSliderWhereItWas()
    {
        for (int step = 0; step <= PercentSteps; step++)
        {
            double percent = step * 100.0 / PercentSteps;
            float level = AppVolumeScale.LevelFromSliderPercent(percent, isLogarithmic: true);
            double echoed = AppVolumeScale.SliderPercentFromLevel(level, isLogarithmic: true);

            Assert.True(Math.Abs(echoed - percent) < SliderValueResolution / 2, $"{percent}% came back as {echoed}%");
        }
    }

    [Fact]
    public void RepositioningFromALevelNeverTriggersAnotherWrite()
    {
        for (int step = 0; step <= PercentSteps; step++)
        {
            float level = step / (float)PercentSteps;
            double percent = AppVolumeScale.SliderPercentFromLevel(level, isLogarithmic: true);
            float written = AppVolumeScale.LevelFromSliderPercent(percent, isLogarithmic: true);

            Assert.True(
                Math.Abs(written - level) < AudioSession.VolumeEqualityEpsilon,
                $"Level {level} drifted to {written}");
        }
    }

    [Fact]
    public void EqualStepsAboveTheFadeChangeLoudnessByEqualDecibels()
    {
        double firstPercent = Math.Ceiling(AppVolumeScale.KneePosition * 100.0);
        for (double percent = firstPercent; percent + 10.0 <= 100.0; percent += 1.0)
        {
            float lower = AppVolumeScale.LevelFromSliderPercent(percent, isLogarithmic: true);
            float upper = AppVolumeScale.LevelFromSliderPercent(percent + 10.0, isLogarithmic: true);
            double decibels = 20.0 * Math.Log10(upper / (double)lower);

            Assert.Equal(AppVolumeScale.DynamicRangeDecibels / 10.0, decibels, precision: 3);
        }
    }

    [Fact]
    public void FadeToSilenceMeetsTheDecibelCurveWithoutAJump()
    {
        double kneePercent = AppVolumeScale.KneePosition * 100.0;
        float below = AppVolumeScale.LevelFromSliderPercent(kneePercent - 0.001, isLogarithmic: true);
        float above = AppVolumeScale.LevelFromSliderPercent(kneePercent + 0.001, isLogarithmic: true);

        Assert.True(below < above);
        Assert.True(above - below < 1e-6f, $"Jump of {above - below} at the knee");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void WheelStepsClimbOutOfSilenceOneStepAtATime(int stepPercent)
    {
        float level = 0f;
        for (int notch = 1; notch * stepPercent <= 100; notch++)
        {
            float next = StepThroughWindows(level, stepPercent);

            Assert.True(next - level >= AudioSession.VolumeEqualityEpsilon, $"Notch {notch} was swallowed");
            Assert.Equal(notch * stepPercent, AppVolumeScale.SliderPercentFromLevel(next, isLogarithmic: true),
                precision: 3);
            level = next;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void WheelStepsDescendFromFullVolumeToSilence(int stepPercent)
    {
        float level = 1f;
        for (int notch = 1; notch * stepPercent <= 100; notch++)
        {
            float next = StepThroughWindows(level, -stepPercent);

            Assert.True(level - next >= AudioSession.VolumeEqualityEpsilon, $"Notch {notch} was swallowed");
            Assert.Equal(100 - notch * stepPercent, AppVolumeScale.SliderPercentFromLevel(next, isLogarithmic: true),
                precision: 3);
            level = next;
        }

        Assert.Equal(0f, level);
    }

    [Fact]
    public void SettingOffIsThePlainPercentConversion()
    {
        for (int step = 0; step <= PercentSteps; step++)
        {
            double percent = step * 100.0 / PercentSteps;
            float level = step / (float)PercentSteps;

            Assert.Equal(
                (float)(percent / 100.0),
                AppVolumeScale.LevelFromSliderPercent(percent, isLogarithmic: false));
            Assert.Equal(level * 100.0, AppVolumeScale.SliderPercentFromLevel(level, isLogarithmic: false));
        }
    }

    [Theory]
    [InlineData(0f, 0.5f)]
    [InlineData(0.25f, 0.5f)]
    [InlineData(1f, 0.003f)]
    [InlineData(0.8f, 0f)]
    public void SettingOffPassesMeterPeaksThrough(float peak, float level)
    {
        Assert.Equal(peak, AppVolumeScale.MeterPeakFraction(peak, level, isLogarithmic: false));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(0.5f)]
    [InlineData(0.02f)]
    public void FullScalePeakReachesTheThumb(float level)
    {
        Assert.Equal(1.0, AppVolumeScale.MeterPeakFraction(1f, level, isLogarithmic: true), precision: 5);
    }

    [Theory]
    [InlineData(0f, 0.5f)]
    [InlineData(0.5f, 0f)]
    [InlineData(float.NaN, 0.5f)]
    public void SilentPeakOrSilentLevelDrawsNoMeter(float peak, float level)
    {
        Assert.Equal(0f, AppVolumeScale.MeterPeakFraction(peak, level, isLogarithmic: true));
    }

    [Fact]
    public void MeterEndsWhereTheThumbWouldSitForTheLevelReachingTheDevice()
    {
        const float level = 0.5f;
        const float peak = 0.25f;
        double thumbPercent = AppVolumeScale.SliderPercentFromLevel(level, isLogarithmic: true);
        double expectedMeterPercent = AppVolumeScale.SliderPercentFromLevel(peak * level, isLogarithmic: true);

        float fraction = AppVolumeScale.MeterPeakFraction(peak, level, isLogarithmic: true);

        Assert.Equal(expectedMeterPercent, fraction * thumbPercent, precision: 3);
    }

    [Fact]
    public void MeterGrowsStrictlyWithPeak()
    {
        float previous = 0f;
        for (int step = 1; step <= 1000; step++)
        {
            float fraction = AppVolumeScale.MeterPeakFraction(step / 1000f, level: 0.3f, isLogarithmic: true);
            Assert.True(fraction > previous, $"Meter stopped growing at peak {step / 1000f}");
            previous = fraction;
        }
    }

    // One wheel notch on a flyout app slider: the thumb sits where the current level maps to, FlyoutSlider moves
    // it by the step, and the new position is written back as a level
    private static float StepThroughWindows(float level, int stepPercent)
    {
        double position = AppVolumeScale.SliderPercentFromLevel(level, isLogarithmic: true);
        double next = Math.Clamp(position + stepPercent, min: 0, max: 100);
        return AppVolumeScale.LevelFromSliderPercent(next, isLogarithmic: true);
    }
}
