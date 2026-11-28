using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI.Performance;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

// Shows and drives a window, so it must not start a headless session while other tests start theirs
[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class PerformanceDeviceCardWindowTestCollection
{
    public const string CollectionName = "Performance device card window tests";
}

[Collection(PerformanceDeviceCardWindowTestCollection.CollectionName)]
public sealed class PerformanceDeviceCardWindowTests
{
    [Fact]
    public void EnterCompletesAValueNameWithoutLeavingTheFormatBox() =>
        AvaloniaTestHost.RunWithFluentTheme(() =>
        {
            PerformanceDevicePresentation device = PerformanceDevicePresentationFactory.Create(PerformanceSnapshot.Empty)
                .Single(static candidate => candidate.Kind == PerformanceDeviceKind.CPU);
            List<PerformanceDeviceCardCustomization> published = [];
            PerformanceDeviceCardWindow window = new(
                device,
                new PerformanceDeviceCardCustomization { DeviceID = device.DeviceID },
                TestPalettes.Default(),
                enableRoundedCorners: true,
                (card, _) => published.Add(card));
            AvaloniaTestHost.ShowActivated(window);

            TextBox line = window.GetVisualDescendants().OfType<TextBox>()
                .Single(box => box.Text == "{utilization}[  {speed}]");
            AvaloniaTestHost.FocusForKeys(line);
            line.Text = string.Empty;
            line.CaretIndex = 0;
            window.KeyTextInput("{hand");
            Dispatcher.UIThread.RunJobs();
            Popup popup = window.GetLogicalDescendants().OfType<Popup>().First(p => p.PlacementTarget == line);
            Assert.True(popup.IsOpen);

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, keySymbol: null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, keySymbol: null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("{handles}", line.Text);
            Assert.False(popup.IsOpen);
            Assert.True(line.IsKeyboardFocusWithin);
            Assert.Equal("{handles}", published[^1].FirstLineFormat);
            window.Close();
        });
}
