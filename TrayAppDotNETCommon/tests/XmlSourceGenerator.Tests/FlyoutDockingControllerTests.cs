using Avalonia;
using TrayAppDotNETCommon.UI;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class FlyoutDockingControllerTests
{
    [Fact]
    public void ConstructorRestoresOnlyCompleteUndockedState()
    {
        DockHarness restored = new() { Settings = { FlyoutUndocked = true, FlyoutHasSavedPosition = true } };

        DockHarness missingPosition = new() { Settings = { FlyoutUndocked = true } };
        missingPosition.RecreateController();

        restored.RecreateController();

        Assert.True(restored.Controller.IsUndocked);
        Assert.False(missingPosition.Controller.IsUndocked);
    }

    [Fact]
    public void ExplicitUndockPersistsStateAndRestoresResolvedPosition()
    {
        DockHarness harness = new() { Settings = { FlyoutHasSavedPosition = true, FlyoutLeft = 400, FlyoutTop = 300 } };

        bool changed = harness.Controller.UndockToSavedPosition();

        Assert.True(changed);
        Assert.True(harness.Controller.IsUndocked);
        Assert.True(harness.Settings.FlyoutUndocked);
        Assert.Equal(new PixelPoint(x: 410, y: 320), harness.Position);
        Assert.Equal(expected: 1, harness.Settings.SaveCount);
        Assert.Equal([FlyoutDockStateChange.Undocked], harness.Changes);
    }

    [Fact]
    public void DisabledClampingRestoresRawSavedPosition()
    {
        DockHarness harness = new()
        {
            Settings =
            {
                ClampUndockedFlyoutToScreen = false,
                FlyoutHasSavedPosition = true,
                FlyoutLeft = 400,
                FlyoutTop = 300
            }
        };

        harness.Controller.UndockToSavedPosition();

        Assert.Equal(new PixelPoint(x: 400, y: 300), harness.Position);
    }

    [Fact]
    public void DragUndockDefersPersistenceUntilRelease()
    {
        DockHarness harness = new();
        harness.DragHelper.BeginDrag(
            new PixelPoint(x: 0, y: 0),
            new PixelPoint(x: 200, y: 200),
            new PixelPoint(x: 0, y: 0),
            snapTolerance: 20);

        bool changed = harness.Controller.SetUndockedFromDrag();

        Assert.True(changed);
        Assert.True(harness.Controller.IsUndocked);
        Assert.False(harness.Settings.FlyoutUndocked);
        Assert.Equal(expected: 0, harness.Settings.SaveCount);

        harness.Position = new PixelPoint(x: 700, y: 500);
        FlyoutDockStateChange? committedChange = harness.Controller.CommitDragPosition();

        Assert.Equal(FlyoutDockStateChange.PositionSaved, committedChange);
        Assert.True(harness.Settings.FlyoutUndocked);
        Assert.True(harness.Settings.FlyoutHasSavedPosition);
        Assert.Equal(expected: 700, harness.Settings.FlyoutLeft);
        Assert.Equal(expected: 500, harness.Settings.FlyoutTop);
        Assert.Equal(expected: 1, harness.Settings.SaveCount);
        Assert.Equal(
            [FlyoutDockStateChange.UndockedFromDrag, FlyoutDockStateChange.PositionSaved],
            harness.Changes);
    }

    [Fact]
    public void SnappedDragRedocksWithoutSavingFloatingCoordinates()
    {
        DockHarness harness = new();
        PixelPoint dockedPosition = new(x: 100, y: 100);
        harness.Position = dockedPosition;
        harness.DragHelper.BeginDrag(
            new PixelPoint(x: 110, y: 110),
            dockedPosition,
            dockedPosition,
            snapTolerance: 20);
        harness.Controller.SetUndockedFromDrag();

        FlyoutDockStateChange? committedChange = harness.Controller.CommitDragPosition();

        Assert.Equal(FlyoutDockStateChange.Redocked, committedChange);
        Assert.False(harness.Controller.IsUndocked);
        Assert.False(harness.Settings.FlyoutUndocked);
        Assert.False(harness.Settings.FlyoutHasSavedPosition);
        Assert.Equal(expected: 1, harness.Settings.SaveCount);
        Assert.Equal(
            [FlyoutDockStateChange.UndockedFromDrag, FlyoutDockStateChange.Redocked],
            harness.Changes);
    }

    [Fact]
    public void DisabledUndockingRejectsExplicitAndDragTransitions()
    {
        DockHarness harness = new() { Settings = { AllowFlyoutUndock = false } };

        Assert.False(harness.Controller.UndockToSavedPosition());
        Assert.False(harness.Controller.SetUndockedFromDrag());
        Assert.False(harness.Controller.IsUndocked);
        Assert.Equal(expected: 0, harness.Settings.SaveCount);
        Assert.Empty(harness.Changes);
    }

    private sealed class DockHarness
    {
        public DockSettings Settings = new();
        public FlyoutWindowDragHelper DragHelper = new();
        public PixelPoint Position = new(x: 100, y: 100);
        public List<FlyoutDockStateChange> Changes = [];
        public FlyoutDockingController Controller = null!;

        public DockHarness() => RecreateController();

        public void RecreateController()
        {
            Changes.Clear();
            Controller = new FlyoutDockingController(new FlyoutDockingOptions
            {
                Settings = Settings,
                DragHelper = DragHelper,
                CurrentPosition = () => Position,
                SetPosition = position => Position = position,
                ResolveDockedPosition = static () => new PixelPoint(x: 100, y: 100),
                ResolveSavedPosition = static position => new PixelPoint(position.X + 10, position.Y + 20),
                ResolveSnapTolerance = static () => 20,
                StateChanged = Changes.Add
            });
        }
    }

    private sealed class DockSettings : IFlyoutDockSettings
    {
        public bool AllowFlyoutUndock { get; set; } = true;
        public bool RestoreFlyoutUndockedOnStartup { get; set; } = true;
        public bool ClampUndockedFlyoutToScreen { get; set; } = true;
        public bool FlyoutUndocked { get; set; }
        public bool FlyoutHasSavedPosition { get; set; }
        public double FlyoutLeft { get; set; }
        public double FlyoutTop { get; set; }
        public int SaveCount;

        public void Save() => SaveCount++;
    }
}
