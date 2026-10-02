using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using TrayAppDotNETCommon.UI.ControlMapping;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class ControlMapRuntimeTests
{
    private const string MapName = "RuntimeTestControlMap";
    private const string WideVariant = "Wide";

    private static readonly ControlMapNodeID MainID = ID("Main");
    private static readonly ControlMapNodeID HeaderID = ID("Shell.Header");
    private static readonly ControlMapNodeID SettingsID = ID("Shell.Header.Settings");
    private static readonly ControlMapNodeID ContentID = ID("Shell.Content");
    private static readonly ControlMapNodeID DismissID = ID("Shell.Dismiss");
    private static readonly ControlMapNodeID UndockID = ID("Main.Undock");
    private static readonly ControlMapNodeID PageID = ID("Main.Page");
    private static readonly ControlMapNodeID FirstAID = ID("Main.Page.First.A");
    private static readonly ControlMapNodeID FirstBID = ID("Main.Page.First.B");
    private static readonly ControlMapNodeID SecondID = ID("Main.Page.Second");
    private static readonly ControlMapNodeID ItemID = ID("Main.Page.Item");
    private static readonly ControlMapNodeID ItemNameID = ID("Main.Page.Item.Name");
    private static readonly ControlMapNodeID ItemRemoveID = ID("Main.Page.Item.Remove");
    private static readonly ControlMapNodeID TextColorID = ID("Main.Page.TextColor");
    private static readonly ControlMapNodeID BackgroundColorID = ID("Main.Page.BackgroundColor");
    private static readonly ControlMapNodeID SaveID = ID("Main.Page.Save");
    private static readonly ControlMapNodeID JumpID = ID("Main.Page.Jump");
    private static readonly ControlMapNodeID ToolbarID = ID("Main.Page.Toolbar");
    private static readonly ControlMapNodeID ToolbarRunID = ID("Main.Page.Toolbar.Run");
    private static readonly ControlMapNodeID ToolbarStopID = ID("Main.Page.Toolbar.Stop");
    private static readonly ControlMapNodeID CardPickID = ID("Card.Pick");
    private static readonly ControlMapNodeID CardResetID = ID("Card.Reset");

    [Fact]
    public void TabIndexesFollowMapOrderThroughTemplatesSlotsAndLogicalScopes() => RunWithMap(view =>
    {
        // Within Page: First (logical) 0, A 1, B 2, Second 3, Item 4, TextColor 7, BackgroundColor 10
        Assert.Equal(expected: 1, KeyboardNavigation.GetTabIndex(view.FirstA));
        Assert.Equal(expected: 2, KeyboardNavigation.GetTabIndex(view.FirstB));
        Assert.Equal(expected: 3, KeyboardNavigation.GetTabIndex(view.Second));
        Assert.Equal(expected: 4, KeyboardNavigation.GetTabIndex(view.ItemOne));
        Assert.Equal(expected: 4, KeyboardNavigation.GetTabIndex(view.ItemTwo));
        Assert.Equal(expected: 7, KeyboardNavigation.GetTabIndex(view.TextColorCard));
        Assert.Equal(expected: 10, KeyboardNavigation.GetTabIndex(view.BackgroundColorCard));

        // Repeated instances and template instances are their own local groups
        Assert.Equal(expected: 0, KeyboardNavigation.GetTabIndex(view.ItemOneName));
        Assert.Equal(expected: 1, KeyboardNavigation.GetTabIndex(view.ItemOneRemove));
        Assert.Equal(expected: 0, KeyboardNavigation.GetTabIndex(view.TextColorPick));
        Assert.Equal(expected: 1, KeyboardNavigation.GetTabIndex(view.TextColorReset));
        Assert.Equal(KeyboardNavigationMode.Local, KeyboardNavigation.GetTabNavigation(view.ItemOne));

        // The instance's slot content lands where the template's named slot sits
        Assert.Equal(expected: 0, KeyboardNavigation.GetTabIndex(view.Settings));
        Assert.Equal(expected: 1, KeyboardNavigation.GetTabIndex(view.Undock));
        Assert.Equal(KeyboardNavigationMode.Once, KeyboardNavigation.GetTabNavigation(view.Header));
        Assert.Equal(KeyboardNavigationMode.Cycle, KeyboardNavigation.GetTabNavigation(view.Window));
    });

    [Fact]
    public void BorderLeavesBecomeFocusableTabStopsWithAFocusRing() => RunWithMap(view =>
    {
        Assert.True(view.FirstA.Focusable);
        Assert.True(KeyboardNavigation.GetIsTabStop(view.FirstA));
        Assert.NotNull(view.FirstA.FocusAdorner);
    });

    [Fact]
    public void TemplateNodesResolvePerInstance() => RunWithMap(view =>
    {
        ExpandedNode? textColorPick = ControlMapNavigator.NodeOf(view.TextColorPick);
        ExpandedNode? backgroundColorPick = ControlMapNavigator.NodeOf(view.BackgroundColorPick);

        Assert.NotNull(textColorPick);
        Assert.NotNull(backgroundColorPick);
        Assert.NotSame(textColorPick, backgroundColorPick);
        Assert.Same(ControlMapNavigator.NodeOf(view.TextColorCard), textColorPick.Parent);
    });

    [Fact]
    public void VariantReordersChildrenAndRemovesUnlisted() => RunWithMap(view =>
    {
        view.Page.MapVariant(WideVariant);

        // Wide orders Second then First, and lists neither Item nor the cards
        Assert.Equal(expected: 0, KeyboardNavigation.GetTabIndex(view.Second));
        Assert.Equal(expected: 2, KeyboardNavigation.GetTabIndex(view.FirstA));
        Assert.False(KeyboardNavigation.GetIsTabStop(view.ItemOne));

        view.Page.MapVariant(WideVariant, isActive: false);
        Assert.Equal(expected: 3, KeyboardNavigation.GetTabIndex(view.Second));
        Assert.True(KeyboardNavigation.GetIsTabStop(view.ItemOne));
    });

    [Fact]
    public void EnterActivatesAFocusedLeafThroughItsActivation() => RunWithMap(view =>
    {
        int activations = 0;
        KeyModifiers lastModifiers = KeyModifiers.None;
        view.Settings.MapActivation(activation =>
        {
            activations++;
            lastModifiers = activation.KeyModifiers;
        });

        Assert.True(PressKey(view.Settings, Key.Enter));
        Assert.True(PressKey(view.Settings, Key.Space, KeyModifiers.Control));
        Assert.Equal(expected: 2, activations);
        Assert.Equal(KeyModifiers.Control, lastModifiers);
    });

    [Fact]
    public void AcceleratorsRunCommandsFromTheirKeyScope() => RunWithMap(view =>
    {
        int dismissals = 0;
        int saves = 0;
        view.Window.MapCommand(DismissID, () => dismissals++);
        view.Page.MapCommand(SaveID, () => saves++);

        Assert.True(PressKey(view.FirstA, Key.Escape));
        Assert.True(PressKey(view.FirstA, Key.S, KeyModifiers.Control));

        // Save is scoped to Page, so the header does not see it
        Assert.False(PressKey(view.Settings, Key.S, KeyModifiers.Control));
        Assert.Equal(expected: 1, dismissals);
        Assert.Equal(expected: 1, saves);
    });

    [Fact]
    public void FocusedControlKeysWinOverAccelerators() => RunWithMap(view =>
    {
        int dismissals = 0;
        view.Window.MapCommand(DismissID, () => dismissals++);
        view.ItemOneName.KeyDown += static (_, eventArgs) =>
        {
            if (eventArgs.Key == Key.Escape)
                eventArgs.Handled = true;
        };

        Assert.True(PressKey(view.ItemOneName, Key.Escape));
        Assert.Equal(expected: 0, dismissals);
    });

    [Fact]
    public void KeysThatTypeIntoAFocusedTextBoxSkipAccelerators() => RunWithMap(view =>
    {
        int jumps = 0;
        int saves = 0;
        view.Page.MapCommand(JumpID, () => jumps++);
        view.Page.MapCommand(SaveID, () => saves++);

        // The box types J, so the J accelerator waits; Ctrl+S types nothing and still saves
        Assert.False(PressKey(view.ItemOneName, Key.J));
        Assert.True(PressKey(view.ItemOneName, Key.S, KeyModifiers.Control));

        // Outside a text box the same accelerator runs
        Assert.True(PressKey(view.FirstA, Key.J));
        Assert.Equal(expected: 1, jumps);
        Assert.Equal(expected: 1, saves);
    });

    [Fact]
    public void ArrowsMoveFocusWithinAnArrowScope() => RunWithMap(view =>
    {
        Assert.True(view.Settings.Focus());

        Assert.True(PressKey(view.Settings, Key.Right));
        Assert.True(view.Undock.IsFocused);

        // The edge stops the movement and leaves the key unhandled
        Assert.False(PressKey(view.Undock, Key.Right));
        Assert.True(PressKey(view.Undock, Key.Left));
        Assert.True(view.Settings.IsFocused);
    });

    [Fact]
    public void ArrangedScopeKeepsTheOrderCodeAddsChildrenIn() => RunWithMap(view =>
    {
        // Stop is added before Run, the reverse of document order
        Assert.Equal(KeyboardNavigation.GetTabIndex(view.ToolbarStop), KeyboardNavigation.GetTabIndex(view.ToolbarRun));
        Assert.True(view.ToolbarStop.Focus());

        Assert.True(PressKey(view.ToolbarStop, Key.Right));
        Assert.True(view.ToolbarRun.IsFocused);
    });

    [Fact]
    public void DriftCheckFindsInteractiveControlsWithoutANode() => RunWithMap(view =>
    {
        Border stray = new() { Focusable = true, Width = 10, Height = 10 };
        view.Page.Children.Add(stray);

        List<Control> unmapped = ControlMapDriftCheck.FindUnmappedControls(view.Window);

        Assert.Contains(stray, unmapped);
        Assert.DoesNotContain(view.FirstA, unmapped);
        Assert.DoesNotContain(view.ItemOneName, unmapped);
    });

    [Fact]
    public void SpatialNavigationPicksTheNearestTargetInLine()
    {
        List<Avalonia.Point> centers =
        [
            new(x: 0, y: 0), new(x: 100, y: 5), new(x: 50, y: 60), new(x: 0, y: 100)
        ];

        Assert.Equal(expected: 1, ControlMapSpatialNavigation.FindDirectionalTarget(centers, 0, SpatialDirection.Right));
        Assert.Equal(expected: 3, ControlMapSpatialNavigation.FindDirectionalTarget(centers, 0, SpatialDirection.Down));
        Assert.Equal(expected: -1, ControlMapSpatialNavigation.FindDirectionalTarget(centers, 0, SpatialDirection.Left));
    }

    private static ControlMapNodeID ID(string path) => new(MapName, path);

    private static bool PressKey(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        KeyEventArgs eventArgs = new()
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = target
        };
        target.RaiseEvent(eventArgs);
        return eventArgs.Handled;
    }

    private static void RunWithMap(Action<TestView> test) => AvaloniaTestHost.Run(() =>
    {
        ControlMapCatalog.Register(CreateMap());
        try
        {
            TestView view = new();
            view.Window.Show();
            test(view);
            view.Window.Close();
        }
        finally
        {
            ControlMapCatalog.Unregister(MapName);
        }
    });

    private static ControlMap CreateMap()
    {
        TestControlMap map = new();
        map.Children.Add(Container(new Template { ID = "Card" },
            new Leaf { ID = "Pick", Kind = LeafKind.Button },
            new Leaf { ID = "Reset", Kind = LeafKind.Button }));
        map.Children.Add(Container(new Template { ID = "Shell" },
            Container(new Scope { ID = "Header", Tab = KeyboardNavigationMode.Once, Arrows = ArrowNavigation.Horizontal },
                new Leaf { ID = "Settings", Kind = LeafKind.Button },
                new Slot { ID = "Actions" }),
            Container(new Scope { ID = "Content" }, new Slot()),
            new Leaf { ID = "Dismiss", Kind = LeafKind.Command, Keys = "Escape" }));
        map.Children.Add(Container(new Surface { ID = "Main", Template = "Shell" },
            new Leaf { ID = "Undock", Kind = LeafKind.Button, Slot = "Actions" },
            Container(new Scope { ID = "Page" },
                new Variant { ID = WideVariant, Order = "Second First" },
                Container(new Scope { ID = "First" },
                    new Leaf { ID = "A", Kind = LeafKind.Button },
                    new Leaf { ID = "B", Kind = LeafKind.Button }),
                new Leaf { ID = "Second", Kind = LeafKind.Button },
                Container(new Scope { ID = "Item", IsRepeated = true },
                    new Leaf { ID = "Name", Kind = LeafKind.Text },
                    new Leaf { ID = "Remove", Kind = LeafKind.Button }),
                new Scope { ID = "TextColor", Template = "Card" },
                new Scope { ID = "BackgroundColor", Template = "Card" },
                new Leaf { ID = "Save", Kind = LeafKind.Command, Keys = "Ctrl+S" },
                Container(new Scope
                    {
                        ID = "Toolbar",
                        Tab = KeyboardNavigationMode.Once,
                        Arrows = ArrowNavigation.Horizontal,
                        IsArranged = true
                    },
                    new Leaf { ID = "Run", Kind = LeafKind.Button },
                    new Leaf { ID = "Stop", Kind = LeafKind.Button }),
                new Leaf { ID = "Jump", Kind = LeafKind.Command, Keys = "J" })));
        return map;
    }

    private static ControlMapContainer Container(ControlMapContainer container, params ControlMapNode[] children)
    {
        foreach (ControlMapNode child in children)
            container.Children.Add(child);

        return container;
    }

    private sealed class TestControlMap() : ControlMap(MapName);

    // A window whose controls carry the test map's ids, built the way code-built UI tags controls
    private sealed class TestView
    {
        public readonly Window Window = new Window().MapTo(MainID);
        public readonly StackPanel Header = new StackPanel { Orientation = Orientation.Horizontal }.MapTo(HeaderID);
        public readonly Border Settings = Button().MapTo(SettingsID);
        public readonly Border Undock = Button().MapTo(UndockID);
        public readonly StackPanel Page = new StackPanel().MapTo(PageID);
        public readonly Border FirstA = Button().MapTo(FirstAID);
        public readonly Border FirstB = Button().MapTo(FirstBID);
        public readonly Border Second = Button().MapTo(SecondID);
        public readonly StackPanel ItemOne = new StackPanel().MapTo(ItemID);
        public readonly TextBox ItemOneName = new TextBox().MapTo(ItemNameID);
        public readonly Border ItemOneRemove = Button().MapTo(ItemRemoveID);
        public readonly StackPanel ItemTwo = new StackPanel().MapTo(ItemID);
        public readonly StackPanel TextColorCard = new StackPanel().MapTo(TextColorID);
        public readonly Border TextColorPick = Button().MapTo(CardPickID);
        public readonly Border TextColorReset = Button().MapTo(CardResetID);
        public readonly StackPanel BackgroundColorCard = new StackPanel().MapTo(BackgroundColorID);
        public readonly Border BackgroundColorPick = Button().MapTo(CardPickID);
        public readonly StackPanel Toolbar = new StackPanel { Orientation = Orientation.Horizontal }.MapTo(ToolbarID);
        public readonly Border ToolbarRun = Button().MapTo(ToolbarRunID);
        public readonly Border ToolbarStop = Button().MapTo(ToolbarStopID);

        public TestView()
        {
            Header.Children.Add(Settings);
            Header.Children.Add(Undock);
            ItemOne.Children.Add(ItemOneName);
            ItemOne.Children.Add(ItemOneRemove);
            ItemTwo.Children.Add(new TextBox().MapTo(ItemNameID));
            TextColorCard.Children.Add(TextColorPick);
            TextColorCard.Children.Add(TextColorReset);
            BackgroundColorCard.Children.Add(BackgroundColorPick);
            Page.Children.Add(FirstA);
            Page.Children.Add(FirstB);
            Page.Children.Add(Second);
            Page.Children.Add(ItemOne);
            Page.Children.Add(ItemTwo);
            Page.Children.Add(TextColorCard);
            Page.Children.Add(BackgroundColorCard);
            Toolbar.Children.Add(ToolbarStop);
            Toolbar.Children.Add(ToolbarRun);
            Page.Children.Add(Toolbar);

            StackPanel content = new StackPanel().MapTo(ContentID);
            content.Children.Add(Page);
            StackPanel root = new();
            root.Children.Add(Header);
            root.Children.Add(content);
            Window.Content = root;
        }

        private static Border Button() => new() { Width = 20, Height = 20 };
    }
}
