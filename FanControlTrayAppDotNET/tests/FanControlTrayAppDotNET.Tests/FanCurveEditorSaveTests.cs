using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FanControlTrayAppDotNET.Models;
using FanControlTrayAppDotNET.UI.Curves;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class FanCurveEditorSaveTests
{
    [Fact]
    public void DraggingAPointSavesOnceOnRelease() =>
        AvaloniaTestHost.RunWithFluentTheme(DragAndCountSaves, TimeSpan.FromSeconds(30));

    private static void DragAndCountSaves()
    {
        AppSettings settings = new();
        Curve curve = new()
        {
            CurveName = "Drag Save Test",
            SmoothingFactor = 0,
            CurveNodes = [new CurveNode(x: 0, y: 20), new CurveNode(x: 100, y: 80)]
        };
        Fan fan = new() { DataSourceKey = "Test_Board.Control.Fan_#1", MaxRPM = 3000 };
        FanCurveEditorWindow window = new(fan, curve, settings);
        int saves = 0;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            FanCurveEditor editor = window.GetVisualDescendants().OfType<FanCurveEditor>().Single();
            Point center = editor.TranslatePoint(
                new Point(editor.Bounds.Width / 2, editor.Bounds.Height / 2),
                window) ?? throw new InvalidOperationException("The curve graph is not laid out.");
            int versionBefore = curve.Version;
            settings.Changed += () => saves++;

            // The press adds a point and drags it; moves only redraw
            window.MouseDown(center, MouseButton.Left);
            for (int step = 1; step <= 5; step++)
                window.MouseMove(center + new Point(x: 0, y: -4 * step));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expected: 0, saves);
            Assert.True(curve.Version > versionBefore);

            window.MouseUp(center + new Point(x: 0, y: -20), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(expected: 1, saves);
            Assert.Equal(expected: 3, curve.CurveNodes.Count);
        }
        finally
        {
            window.Close();
        }
    }
}
