using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.Controls.Curves;
using TrayAppDotNETCommon.UI.Drag;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

/// <summary>Covers the shared flyout building blocks the four flyouts moved onto.</summary>
public sealed class FlyoutPrimitivesTests
{
    [Theory]
    [InlineData(0, 2, 0, 35, 0)]
    [InlineData(0, 2, 1, 35, -35)]
    [InlineData(0, 2, 2, 35, -35)]
    [InlineData(0, 2, 3, 35, 0)]
    [InlineData(2, 0, 0, 35, 35)]
    [InlineData(2, 0, 1, 35, 35)]
    [InlineData(2, 0, 2, 35, 0)]
    [InlineData(2, 0, 3, 35, 0)]
    [InlineData(1, 1, 0, 35, 0)]
    public void ReorderPreviewMovesOnlyDisplacedSiblings(
        int sourceIndex,
        int targetIndex,
        int rowIndex,
        double shift,
        double expectedOffset) =>
        Assert.Equal(expectedOffset, ReorderMath.ResolvePreviewOffset(rowIndex, sourceIndex, targetIndex, shift));

    [Theory]
    [InlineData(5, 0)]
    [InlineData(15, 1)]
    [InlineData(35, 2)]
    [InlineData(90, 3)]
    public void ReorderTargetCountsTheOtherRowsAboveTheDraggedMidpoint(double draggedMidpoint, int expected) =>
        Assert.Equal(expected, ReorderMath.ResolveTargetIndex([10, 30, 50], draggedMidpoint));

    [Fact]
    public void ReorderMoveShiftsTheItemsBetween()
    {
        List<string> items = ["a", "b", "c", "d"];

        ReorderMath.Move(items, sourceIndex: 0, targetIndex: 2);

        Assert.Equal(["b", "c", "a", "d"], items);
    }

    [Fact]
    public void WheelStepsAccumulateFractionsAndIgnoreHorizontalInput()
    {
        WheelStepAccumulator steps = new();

        Assert.Equal(expected: 0, steps.Add(0));
        Assert.Equal(expected: 0, steps.Add(0.4));
        Assert.Equal(expected: 0, steps.Add(0.4));
        Assert.Equal(expected: 1, steps.Add(0.4));
        Assert.Equal(expected: 0, steps.Add(-0.6));
        Assert.Equal(expected: -1, steps.Add(-0.6));
        Assert.Equal(expected: 3, steps.Add(3));
    }

    [Fact]
    public void CompanionsStackBesideTheFlyoutOnTheSideWithRoom()
    {
        PixelRect workArea = new(x: 0, y: 0, width: 1920, height: 1040);
        PixelRect flyout = new(x: 1500, y: 500, width: 400, height: 500);
        PixelSize[] sizes = [new(300, 200), new(300, 200), new(300, 200)];

        PixelPoint[] positions = FlyoutTrayPlacement.ResolveCompanionPositions(
            flyout,
            workArea,
            sizes,
            rowsPerColumn: 2,
            gap: 10,
            edgePadding: 8);

        // Two rows in the first column, the third in a second column further out; all left of the flyout
        Assert.Equal(new PixelPoint(1190, 500), positions[0]);
        Assert.Equal(new PixelPoint(1190, 710), positions[1]);
        Assert.Equal(new PixelPoint(880, 500), positions[2]);
    }

    [Fact]
    public void CompanionsGoRightWhenTheLeftHasNoRoom()
    {
        PixelRect workArea = new(x: 0, y: 0, width: 1920, height: 1040);
        PixelRect flyout = new(x: 20, y: 100, width: 400, height: 500);

        PixelPoint[] positions = FlyoutTrayPlacement.ResolveCompanionPositions(
            flyout,
            workArea,
            [new PixelSize(300, 200)],
            rowsPerColumn: 2,
            gap: 10,
            edgePadding: 8);

        Assert.Equal(new PixelPoint(430, 100), positions[0]);
    }

    [Theory]
    [InlineData(MonotonicEndTangent.Secant)]
    [InlineData(MonotonicEndTangent.ThreePoint)]
    public void SmoothedSamplingStaysMonotonicThroughTheNodes(MonotonicEndTangent rule)
    {
        double[] xs = [0, 20, 45, 70, 100];
        double[] ys = [10, 12, 60, 61, 95];
        double[] tangents = new double[xs.Length];
        MonotonicCurveSampler.ComputeMonotonicTangents(xs, ys, tangents, rule);

        double previous = double.NegativeInfinity;
        for (double x = 0; x <= 100; x += 0.5)
        {
            double y = MonotonicCurveSampler.Sample(xs, ys, tangents, x, smoothness: 1.0);
            Assert.True(y >= previous - 1e-9);
            previous = y;
        }

        for (int i = 0; i < xs.Length; i++)
            Assert.Equal(ys[i], MonotonicCurveSampler.Sample(xs, ys, tangents, xs[i], smoothness: 1.0), precision: 9);
    }

    [Fact]
    public void UnsmoothedSamplingIsLinear()
    {
        double[] xs = [0, 50, 100];
        double[] ys = [0, 80, 100];
        double[] tangents = new double[xs.Length];

        Assert.Equal(expected: 40, MonotonicCurveSampler.Sample(xs, ys, tangents, x: 25, smoothness: 0), precision: 9);
        Assert.Equal(expected: 90, MonotonicCurveSampler.InterpolateLinear(xs, ys, x: 75), precision: 9);
    }

    [Fact]
    public void SliderIgnoresHorizontalWheelAndStepsOnAccumulatedFineInput() => AvaloniaTestHost.Run(() =>
    {
        FlyoutSlider slider = new() { Width = 200, Height = 30, Minimum = 0, Maximum = 100, Value = 50, WheelStep = 5 };
        Window window = new() { Width = 200, Height = 30, Content = slider };
        try
        {
            window.Show();
            window.UpdateLayout();
            Point center = new(x: 100, y: 15);

            window.MouseWheel(center, new Vector(x: 1, y: 0));
            Assert.Equal(expected: 50, slider.Value);

            window.MouseWheel(center, new Vector(x: 0, y: 0.5));
            Assert.Equal(expected: 50, slider.Value);
            window.MouseWheel(center, new Vector(x: 0, y: 0.5));
            Assert.Equal(expected: 55, slider.Value);
        }
        finally
        {
            window.Close();
            slider.Dispose();
        }
    });

    [Fact]
    public void SliderStaysAdjustingUntilTheOuterGestureEnds() => AvaloniaTestHost.Run(() =>
    {
        FlyoutSlider slider = new() { Width = 200, Height = 30, Minimum = 0, Maximum = 100, Value = 50, WheelStep = 5 };
        Window window = new() { Width = 200, Height = 30, Content = slider };
        try
        {
            window.Show();
            window.UpdateLayout();
            Point center = new(x: 100, y: 15);

            window.MouseDown(center, MouseButton.Left);
            Assert.True(slider.IsUserAdjusting);

            // A wheel step inside the drag must not end the drag's adjustment
            window.MouseWheel(center, new Vector(x: 0, y: 1));
            Assert.True(slider.IsUserAdjusting);

            window.MouseUp(center, MouseButton.Left);
            Assert.False(slider.IsUserAdjusting);
        }
        finally
        {
            window.Close();
            slider.Dispose();
        }
    });

    [Fact]
    public void ReorderGestureCommitsOnReleaseAndRestoresRowsOnCaptureLoss() => AvaloniaTestHost.Run(() =>
    {
        StackPanel list = new() { Width = 100 };
        for (int index = 0; index < 3; index++)
            list.Children.Add(new Border { Height = 20, Background = Brushes.Gray });

        List<(int Source, int Target)> commits = [];
        IPointer? pressedPointer = null;
        using VerticalReorderGesture gesture = new(new VerticalReorderGestureOptions
        {
            List = list,
            Threshold = 4,
            Commit = (source, target) => commits.Add((source, target))
        });
        foreach (Control row in list.Children)
        {
            row.PointerPressed += (_, e) =>
            {
                pressedPointer = e.Pointer;
                gesture.TryBegin(row, e);
            };
        }

        Window window = new()
        {
            Width = 100,
            Height = 60,
            Content = list,
            VerticalContentAlignment = VerticalAlignment.Top
        };
        try
        {
            window.Show();
            window.UpdateLayout();

            // A drag lost to another window puts the rows back and reorders nothing
            window.MouseDown(new Point(x: 50, y: 10), MouseButton.Left);
            window.MouseMove(new Point(x: 50, y: 45));
            Assert.True(gesture.IsDragging);
            Assert.NotNull(list.Children[0].RenderTransform);
            Control dragged = list.Children[0];
            dragged.RaiseEvent(new PointerCaptureLostEventArgs(dragged, pressedPointer!));
            Assert.False(gesture.IsActive);
            Assert.Empty(commits);
            Assert.All(list.Children, child => Assert.Null(child.RenderTransform));
            window.MouseUp(new Point(x: 50, y: 45), MouseButton.Left);

            // A released drag commits once
            window.MouseDown(new Point(x: 50, y: 10), MouseButton.Left);
            window.MouseMove(new Point(x: 50, y: 58));
            window.MouseUp(new Point(x: 50, y: 58), MouseButton.Left);
            Assert.Equal([(0, 2)], commits);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void ReplacingSearchListItemsKeepsAListedSelectionAndDropsAMissingOne() => AvaloniaTestHost.Run(() =>
    {
        SettingsSearchableListBox list = new(TestPalettes.Default());
        int selectionChanges = 0;
        try
        {
            SettingsSearchableListBoxItem kept = new(tag: "kept", text: "Kept");
            list.ReplaceItems([new SettingsSearchableListBoxItem(tag: "a", text: "A"), kept]);
            list.SelectedItem = kept;
            list.SelectionChanged += (_, _) => selectionChanges++;

            list.ReplaceItems([kept, new SettingsSearchableListBoxItem(tag: "b", text: "B")]);
            Assert.Same(kept, list.SelectedItem);
            Assert.Equal(expected: 0, selectionChanges);
            Assert.Equal(expected: 2, list.Items.Count);

            list.ReplaceItems([new SettingsSearchableListBoxItem(tag: "c", text: "C")]);
            Assert.Null(list.SelectedItem);
            Assert.Equal(expected: 1, selectionChanges);
        }
        finally
        {
            list.Dispose();
        }
    });

    [Fact]
    public void DialogTitleWithoutTextFollowsTheWindowTitle() => AvaloniaTestHost.Run(() =>
    {
        Window window = new() { Title = "First" };
        using UIResourceScope resources = new(nameof(DialogTitleWithoutTextFollowsTheWindowTitle));
        Grid bar = TrayAppDotNETDialogChrome.TitleBar(
            new TrayAppDotNETDialogTitleBarOptions
            {
                Window = window,
                Palette = TestPalettes.Default(),
                CloseTooltip = "Close",
                Close = window.Close
            },
            resources);
        TextBlock title = Assert.IsType<TextBlock>(bar.Children[0]);

        Assert.Equal("First", title.Text);
        window.Title = "Second";
        Assert.Equal("Second", title.Text);
        Dispatcher.UIThread.RunJobs();
    });
}
