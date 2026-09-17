using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.UI.Flyout;
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

            // The custom window-level Space handler must reach the checkbox, not a probe action
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
