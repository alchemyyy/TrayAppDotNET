using TrayAppDotNETCommon.UI.Tray;
using TrayAppDotNETCommon.Visuals;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class TrayInputTests
{
    [Fact]
    public void WheelDeltasAccumulateIntoWholeNotches()
    {
        TrayWheelNotchAccumulator accumulator = new();

        Assert.Equal(expected: 1, accumulator.Add(120));
        Assert.Equal(expected: 2, accumulator.Add(240));
        Assert.Equal(expected: 0, accumulator.Add(60));
        Assert.Equal(expected: 0, accumulator.Add(40));
        Assert.Equal(expected: 1, accumulator.Add(40));
        Assert.Equal(expected: 0, accumulator.Add(0));
    }

    [Fact]
    public void TurningTheWheelBackDropsTheRemainder()
    {
        TrayWheelNotchAccumulator accumulator = new();

        Assert.Equal(expected: 0, accumulator.Add(100));
        Assert.Equal(expected: -1, accumulator.Add(-120));
        Assert.Equal(expected: 0, accumulator.Add(-60));
        Assert.Equal(expected: -1, accumulator.Add(-60));

        accumulator.Add(-100);
        accumulator.Reset();
        Assert.Equal(expected: 0, accumulator.Add(-20));
    }

    [Fact]
    public void TooltipTextThatFitsIsReturnedUnchanged()
    {
        string text = new('x', TrayTooltipText.MaxLength);

        Assert.Same(text, TrayTooltipText.Truncate(text));
    }

    [Fact]
    public void LongTooltipTextIsCutAfterItsLastCompleteLine()
    {
        string text = "CPU: 12%\nMemory: 40%\nDisk: 3%";

        Assert.Equal(expected: "CPU: 12%\nMemory: 40%\u2026", TrayTooltipText.Truncate(text, maxLength: 25));
    }

    [Fact]
    public void ALongFirstLineIsCutAtTheLastCharacterThatFits() =>
        Assert.Equal(expected: "abcd\u2026", TrayTooltipText.Truncate("abcdefgh", maxLength: 5));

    [Fact]
    public void TooltipTextIsNeverCutInsideASurrogatePair() =>
        Assert.Equal(expected: "abc\u2026", TrayTooltipText.Truncate("abc\U0001F600def", maxLength: 5));

    [Fact]
    public void TruncatedTooltipTextFitsTheShellBuffer()
    {
        string text = string.Join('\n', Enumerable.Range(0, 40).Select(static i => $"Adapter {i}: connected"));

        string truncated = TrayTooltipText.Truncate(text);

        Assert.True(truncated.Length <= TrayTooltipText.MaxLength);
        Assert.EndsWith(TrayTooltipText.Ellipsis.ToString(), truncated);
    }

    [Theory]
    // The user was working in the window: the click closes it
    [InlineData(true, false, true, true, TrayToggleAction.Hide)]
    [InlineData(true, false, true, false, TrayToggleAction.Hide)]
    // Visible behind another window, such as a floating flyout: it comes forward where it is, without docking again
    [InlineData(true, false, false, true, TrayToggleAction.Activate)]
    [InlineData(true, false, false, false, TrayToggleAction.Activate)]
    [InlineData(true, true, true, true, TrayToggleAction.Show)]
    [InlineData(true, true, false, false, TrayToggleAction.Show)]
    // This click's own press hid it: the click must not reopen it
    [InlineData(false, false, true, true, TrayToggleAction.None)]
    // An earlier taskbar click hid it, or something else had the foreground: the click opens it
    [InlineData(false, false, true, false, TrayToggleAction.Show)]
    [InlineData(false, false, false, true, TrayToggleAction.Show)]
    [InlineData(false, false, false, false, TrayToggleAction.Show)]
    public void TrayClickTogglesFromTheForegroundBeforeTheTaskbar(
        bool isVisible,
        bool isMinimized,
        bool wasForegroundBeforeTaskbar,
        bool taskbarActivatedByThisClick,
        TrayToggleAction expected) =>
        Assert.Equal(
            expected,
            TrayWindowToggle.Decide(isVisible, isMinimized, wasForegroundBeforeTaskbar, taskbarActivatedByThisClick));

    [Theory]
    [InlineData(0u, 500u, true)]
    [InlineData(250u, 500u, true)]
    [InlineData(500u, 500u, true)]
    [InlineData(501u, 500u, false)]
    [InlineData(60_000u, 500u, false)]
    [InlineData(600u, 900u, true)]
    public void OnlyARecentTaskbarActivationIsThePressOfThisClick(
        uint millisecondsSinceTaskbarActivation,
        uint doubleClickTime,
        bool expected) =>
        Assert.Equal(expected, TrayWindowToggle.IsPressOfThisClick(millisecondsSinceTaskbarActivation, doubleClickTime));

    [Fact]
    public void NoTaskbarActivationSinceStartIsNotThePressOfThisClick() =>
        Assert.False(TrayWindowToggle.IsPressOfThisClick(millisecondsSinceTaskbarActivation: null, doubleClickTime: 500));

    [Theory]
    [InlineData(false, false, ModifiedAction.Default)]
    [InlineData(true, false, ModifiedAction.CtrlAction)]
    [InlineData(false, true, ModifiedAction.AltAction)]
    [InlineData(true, true, ModifiedAction.CtrlAction)]
    public void ModifiedClicksPickTheirAction(bool isCtrlDown, bool isAltDown, ModifiedAction expected) =>
        Assert.Equal(
            expected,
            TrayClickModifiers.Resolve(
                isCtrlDown,
                isAltDown,
                ModifiedAction.CtrlAction,
                ModifiedAction.AltAction,
                ModifiedAction.Default,
                ModifiedAction.Nothing));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AModifierSetToNothingFallsBackToTheNormalClick(bool isCtrlDown, bool isAltDown) =>
        Assert.Equal(
            ModifiedAction.Default,
            TrayClickModifiers.Resolve(
                isCtrlDown,
                isAltDown,
                ModifiedAction.Nothing,
                ModifiedAction.Nothing,
                ModifiedAction.Default,
                ModifiedAction.Nothing));

    [Fact]
    public void SmallChevronsAreTheFluentUpAndDownGlyphs() => AvaloniaTestHost.Run(() =>
    {
        Glyph up = GlyphCatalog.CHEVRON_UP_SMALL;
        Glyph down = GlyphCatalog.CHEVRON_DOWN_SMALL;

        Assert.Equal(expected: "\uE96D", up.Text);
        Assert.Equal(expected: 1d, up.TranslateY);
        Assert.Equal(expected: "\uE96E", down.Text);
        Assert.Equal(expected: -1d, down.TranslateY);
    });

    public enum ModifiedAction
    {
        Nothing,
        Default,
        CtrlAction,
        AltAction
    }
}
