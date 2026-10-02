using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.UI.Curves;
using FanControlTrayAppDotNET.UI.Flyout;
using FanControlTrayAppDotNET.UI.Settings;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class FanUIProfileTests
{
    [Fact]
    public void ProbeEditorShowsOneRowOfNamedProfileCheckboxesAndPreventsOrphans() => RunUI(() =>
    {
        AppSettings settings = new();
        settings.EnsureFanProfileCount(FanProfile.SlotCount);
        settings.FanProfiles[1].Name = "Quiet";
        settings.FanProfiles[2].Name = "Gaming";
        ProbeCard card = new() { Name = "Sensors", DisplayProfileMask = 1 };
        int changes = 0;
        ProbeDataSelectorWindow window = new(card, settings, _ => changes++);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            CheckBox[] checkBoxes = window.GetVisualDescendants().OfType<CheckBox>().ToArray();
            Assert.Equal(3, checkBoxes.Length);
            Assert.Equal(["Profile 1", "Quiet", "Gaming"],
                checkBoxes.Select(checkBox => Assert.IsType<TextBlock>(checkBox.Content).Text));

            TextBlock label = window.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text == "Profiles to display probe card on: ");
            Point firstPosition = checkBoxes[0].TranslatePoint(default, window)!.Value;
            foreach (CheckBox checkBox in checkBoxes)
            {
                Point position = checkBox.TranslatePoint(default, window)!.Value;
                Assert.Equal(firstPosition.Y, position.Y);
                Assert.True(position.X + checkBox.Bounds.Width <= window.ClientSize.Width);
            }
            Assert.True(label.TranslatePoint(default, window)!.Value.X < firstPosition.X);

            // Opening focuses the first checkbox; Space toggles it and the control map moves focus on Right
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.True(checkBoxes[0].IsChecked);
            Assert.Equal(0, changes);
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.Equal(3, card.DisplayProfileMask);
            checkBoxes[0].IsChecked = false;
            Assert.Equal(2, card.DisplayProfileMask);
            checkBoxes[1].IsChecked = false;
            Assert.True(checkBoxes[1].IsChecked);
            Assert.Equal(2, changes);

            settings.FanProfiles[1].Name = "Night";
            settings.RaiseChanged();
            Dispatcher.UIThread.RunJobs();
            CheckBox[] refreshed = window.GetVisualDescendants().OfType<CheckBox>().ToArray();
            Assert.Equal("Night", Assert.IsType<TextBlock>(refreshed[1].Content).Text);
            Assert.True(refreshed[1].IsChecked);
            checkBoxes[2].IsChecked = true;
            Assert.Equal(2, card.DisplayProfileMask);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void ProbeEditorCtrlDownMovesNicknameRuleAndKeepsFocusOnIt() => RunUI(() =>
    {
        AppSettings settings = new();
        settings.EnsureFanProfileCount(FanProfile.SlotCount);
        DeviceNicknameRule first = new() { TargetRegex = "First" };
        DeviceNicknameRule second = new() { TargetRegex = "Second" };
        settings.DeviceNicknameRules.AddRange([first, second]);
        ProbeDataSelectorWindow window = new(new ProbeCard { DisplayProfileMask = 1 }, settings, static _ => { });
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(NicknameRuleDeleteButton(window, first).Focus(NavigationMethod.Tab));

            // A control map command of the rule scope, dispatched from any control inside the rule
            window.KeyPress(Key.Down, RawInputModifiers.Control, PhysicalKey.ArrowDown, null);
            window.KeyRelease(Key.Down, RawInputModifiers.Control, PhysicalKey.ArrowDown, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(second, settings.DeviceNicknameRules[0]);
            Assert.Same(first, settings.DeviceNicknameRules[1]);
            Assert.Same(NicknameRuleDeleteButton(window, first), window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void ProbeEditorTabWalksTheFocusedRuleThenLeavesThePageForTheOpenTab() => RunUI(() =>
    {
        AppSettings settings = new();
        settings.EnsureFanProfileCount(FanProfile.SlotCount);
        DeviceNicknameRule rule = new() { TargetRegex = "Only" };
        settings.DeviceNicknameRules.Add(rule);
        ProbeDataSelectorWindow window = new(new ProbeCard { DisplayProfileMask = 1 }, settings, static _ => { });
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            TextBox[] ruleBoxes = window.GetVisualDescendants()
                .OfType<Border>()
                .Single(card => ReferenceEquals(card.Tag, rule))
                .GetVisualDescendants()
                .OfType<TextBox>()
                .ToArray();
            Assert.True(ruleBoxes[0].Focus(NavigationMethod.Tab));

            // The rule is a Local group inside the Once page: Tab walks it, then the page hands Tab to the strip
            PressTab(window);
            Assert.Same(ruleBoxes[1], window.FocusManager?.GetFocusedElement());
            PressTab(window);
            Assert.Same(NicknameRuleDeleteButton(window, rule), window.FocusManager?.GetFocusedElement());
            PressTab(window);
            Assert.Same(ProbeEditorTabHeader(window, "Home"), window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void ProbeEditorEnterOnTabHeaderOpensThatTabAndKeepsFocusOnIt() => RunUI(() =>
    {
        AppSettings settings = new();
        settings.EnsureFanProfileCount(FanProfile.SlotCount);
        ProbeDataSelectorWindow window = new(new ProbeCard { DisplayProfileMask = 1 }, settings, static _ => { });
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(ProbeEditorTabHeader(window, "Power").Focus(NavigationMethod.Tab));

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();

            // The Home page and its profile checkboxes are gone
            Assert.Empty(window.GetVisualDescendants().OfType<CheckBox>());
            Assert.Same(ProbeEditorTabHeader(window, "Power"), window.FocusManager?.GetFocusedElement());
        }
        finally
        {
            window.Close();
        }
    });

#if DEBUG
    [Fact]
    public void ProbeEditorLeavesNoInteractiveControlOutOfTheControlMap() => RunUI(() =>
    {
        const string liveProbeKey = "Test_Board.Temperatures.Live_Probe";
        DataSource.Register(new DataSource
        {
            DataSourceKey = liveProbeKey, DataSourceType = DataSourceTypeEnum.Temperature, IsLiveHardwareSensor = true
        });
        AppSettings settings = new();
        settings.EnsureFanProfileCount(FanProfile.SlotCount);
        settings.DeviceNicknameRules.Add(new DeviceNicknameRule { TargetRegex = "Device" });
        settings.ProbeNicknameRules.Add(new DeviceNicknameRule { TargetRegex = "Probe" });
        ProbeCardProbe liveProbe = new() { DataSourceKey = liveProbeKey };
        ProbeCard card = new()
        {
            DisplayProfileMask = 1,
            Probes = [liveProbe, new ProbeCardProbe { DataSourceKey = "Test_Board.Temperatures.Missing_Probe" }]
        };
        ProbeDataSelectorWindow window = new(card, settings, static _ => { });
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(UnmappedControlNames(window));

            // The gear, a selected card's only SettingsButton, opens the transform editor on both pages
            SettingsButton gear = window.GetVisualDescendants()
                .OfType<Border>()
                .Single(selectedCard => ReferenceEquals(selectedCard.Tag, liveProbe))
                .GetVisualDescendants()
                .OfType<SettingsButton>()
                .Single();
            Assert.True(gear.Focus(NavigationMethod.Tab));
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBox>(), box => box.Tag is ProbeCardProbe);
            Assert.Empty(UnmappedControlNames(window));

            Assert.True(ProbeEditorTabHeader(window, "Temperatures").Focus(NavigationMethod.Tab));
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBox>(), box => box.Tag is ProbeCardProbe);
            Assert.Empty(UnmappedControlNames(window));
        }
        finally
        {
            window.Close();
            DataSource.Unregister(liveProbeKey);
        }
    });

    [Fact]
    public void FanWindowsLeaveNoInteractiveControlOutOfTheControlMap() => RunUI(() =>
    {
        Fan fan = new() { DataSourceKey = "Fan_1", FansName = "Fan #1" };
        Curve curve = new() { CurveName = "Test_Curve" };
        AppSettings settings = new()
        {
            Fans = [fan.CloneForPersistence()], ProbeCards = [new ProbeCard { Name = "Sensors", DisplayProfileMask = 1 }]
        };
        settings.EnsureFanProfileCount(FanProfile.SlotCount);

        AssertEveryInteractiveControlIsMapped(
            new FanFlyoutWindow(lhmService: null, settings, static _ => { }),
            static window => window.Close());
        AssertEveryInteractiveControlIsMapped(new FanPropertiesWindow(fan, settings), static window => window.ForceClose());
        try
        {
            AssertEveryInteractiveControlIsMapped(
                new FanCurveEditorWindow(fan, curve, settings),
                static window => window.Close());
        }
        finally
        {
            Curve.Unregister(curve.CurveName);
        }

        AssertEveryInteractiveControlIsMapped(
            new FanSettingsWindow(settings, static (_, _, _) => { }),
            static window => window.Close());
    });

    private static void AssertEveryInteractiveControlIsMapped<TWindow>(TWindow window, Action<TWindow> close)
        where TWindow : Window
    {
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(UnmappedControlNames(window));
        }
        finally
        {
            close(window);
        }
    }

    /// <summary>
    /// Returns the interactive controls the Debug drift check reports as unmapped once layout has bound every tagged
    /// control. The check counts an inherited hand cursor as interactive, so it also reports the content of a mapped
    /// card that is a drag handle; such content is checked here below the point where the drift check stops.
    /// </summary>
    private static List<string> UnmappedControlNames(Window window)
    {
        window.UpdateLayout();
        List<string> names = [];
        foreach (Control control in ControlMapDriftCheck.FindUnmappedControls(window))
        {
            if (IsInteractiveItself(control))
            {
                names.Add($"{control.GetType().Name} '{control.Name}'");
                continue;
            }

            foreach (Visual descendant in control.GetVisualDescendants())
            {
                if (descendant is not Control inner || !IsInteractiveItself(inner)) continue;
                if (ControlMapBinding.GetNode(inner) == null)
                    names.Add($"{inner.GetType().Name} '{inner.Name}'");
            }
        }

        return names;
    }

    // A tab stop, or a control that sets the hand cursor itself rather than inheriting it
    private static bool IsInteractiveItself(Control control) =>
        control is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true }
        && (control.Focusable && KeyboardNavigation.GetIsTabStop(control)
            || control.IsSet(InputElement.CursorProperty) && ReferenceEquals(control.Cursor, TrayAppDotNETCursors.Hand));
#endif

    [Fact]
    public void LongProfileNamesStayInOneRowAboveSelectedProbes() => RunUI(() =>
    {
        AppSettings settings = new();
        settings.EnsureFanProfileCount(FanProfile.SlotCount);
        foreach (FanProfile profile in settings.FanProfiles)
            profile.Name = new string('X', 200);
        ProbeCard card = new() { DisplayProfileMask = 1 };
        ProbeDataSelectorWindow window = new(card, settings, static _ => { });
        try
        {
            window.Show();
            window.UpdateLayout();
            CheckBox[] checkBoxes = window.GetVisualDescendants().OfType<CheckBox>().ToArray();
            double rowTop = checkBoxes[0].TranslatePoint(default, window)!.Value.Y;
            foreach (CheckBox checkBox in checkBoxes)
            {
                Point position = checkBox.TranslatePoint(default, window)!.Value;
                Assert.Equal(rowTop, position.Y);
                Assert.True(position.X + checkBox.Bounds.Width <= window.ClientSize.Width);
                TextBlock label = Assert.IsType<TextBlock>(checkBox.Content);
                Assert.True(label.Bounds.Width < checkBox.Bounds.Width);
            }
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void OpenFanPropertiesReloadNamesAndGroupsAfterProfileSwitch() => RunUI(() =>
    {
        Fan fan = new() { DataSourceKey = "Fan_1", FansName = "Fan #1", UserDefinedName = "Front", Group = "Radiators" };
        AppSettings settings = new()
        {
            Fans = [fan.CloneForPersistence()], FanGroups = [new FanGroup { Name = "Radiators" }]
        };
        FanPropertiesWindow window = new(fan, settings);
        try
        {
            window.Show();
            TextBox nameBox = Field<TextBox>(window, "_nameBox");
            SettingsComboBox groupBox = Field<SettingsComboBox>(window, "_groupCombo");
            Assert.Equal("Front", nameBox.Text);

            settings.SelectFanProfile(1, [fan]);
            settings.RaiseChanged();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(string.Empty, nameBox.Text);
            Assert.DoesNotContain(groupBox.Items, item => item.Tag as string == "Radiators");

            settings.SelectFanProfile(0, [fan]);
            settings.RaiseChanged();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Front", nameBox.Text);
            Assert.Equal("Radiators", groupBox.SelectedItem?.Tag);
        }
        finally
        {
            window.ForceClose();
        }
    });

    [Fact]
    public void FlyoutFiltersProbeCardsButReorderingDoesNotDeleteHiddenCards() => RunUI(() =>
    {
        ProbeCard visible = new() { Name = "Visible card", DisplayProfileMask = 1 };
        ProbeCard hidden = new() { Name = "Hidden card", DisplayProfileMask = 2 };
        AppSettings settings = new() { ProbeCards = [visible, hidden] };
        FanFlyoutWindow window = new(null, settings, static _ => { });
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            List<FanFlyoutWindow.FlyoutTopLevelItem> order =
                Invoke<List<FanFlyoutWindow.FlyoutTopLevelItem>>(window, "BuildTopLevelOrder");
            Assert.Same(visible, Assert.Single(order).ProbeCard);
            Invoke<object?>(window, "ApplyTopLevelDisplayOrder", order);
            Assert.Contains(hidden, Field<List<ProbeCard>>(window, "_probeCards"));
            Assert.Contains(hidden, settings.ProbeCards);

            settings.SelectFanProfile(1, []);
            settings.RaiseChanged();
            Dispatcher.UIThread.RunJobs();
            order = Invoke<List<FanFlyoutWindow.FlyoutTopLevelItem>>(window, "BuildTopLevelOrder");
            Assert.Same(hidden, Assert.Single(order).ProbeCard);
            Assert.Contains(visible, Field<List<ProbeCard>>(window, "_probeCards"));
        }
        finally
        {
            window.Close();
        }
    });

    private static void PressTab(Window window)
    {
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Dispatcher.UIThread.RunJobs();
    }

    // Nickname rule cards carry their rule in Tag; the delete button is the card's only SettingsButton
    private static SettingsButton NicknameRuleDeleteButton(Window window, DeviceNicknameRule rule) =>
        window.GetVisualDescendants()
            .OfType<Border>()
            .Single(card => ReferenceEquals(card.Tag, rule))
            .GetVisualDescendants()
            .OfType<SettingsButton>()
            .Single();

    private static Border ProbeEditorTabHeader(Window window, string label) =>
        window.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Child is TextBlock text && text.Text == label);

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;

    private static T Invoke<T>(object target, string name, params object[] arguments) =>
        (T)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments)!;

    private static void RunUI(Action test)
    {
        using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        session.Dispatch(test, timeout.Token).GetAwaiter().GetResult();
    }

    public sealed class TestApplication : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }

    public static class TestAppBuilder
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}
