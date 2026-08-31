using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.ContextMenus;
using TrayAppDotNETCommon.Visuals;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class ContextMenuWindowTests
{
    [Fact]
    public void TrailingGlyphMetadataAppliesFontWeightAndTransform() =>
        AvaloniaTestHost.Run(() =>
        {
            const string GlyphText = "\uE73E";
            Glyph trailingGlyph = Glyph.MDL2(
                GlyphText,
                FontWeight.Bold,
                translateX: 1);
            ContextMenuWindow menu = new(
                [
                    new ContextMenuEntry("Selected", static () => { })
                    {
                        TrailingGlyphMetadata = trailingGlyph
                    }
                ],
                new ContextMenuWindowOptions { Palette = Palette() });

            try
            {
                menu.Show();
                menu.UpdateLayout();
                TextBlock glyphText = Assert.Single(
                    menu.GetVisualDescendants().OfType<TextBlock>(),
                    textBlock => textBlock.Text == GlyphText);

                Assert.Equal(
                    TADNFontResolver.ResolveFontFamily(TADNFont.SegoeMDL2Assets),
                    glyphText.FontFamily);
                Assert.Equal(FontWeight.Bold, glyphText.FontWeight);
                Assert.IsType<TransformGroup>(glyphText.RenderTransform);
            }
            finally
            {
                menu.Close();
            }
        });

    [Fact]
    public void OverlayPositionUsesSpaceBelowTopMountedAnchor()
    {
        PixelRect containingBounds = new(100, 100, 350, 500);
        PixelRect anchorBounds = new(210, 100, 37, 38);
        PixelSize menuSize = new(180, 80);

        PixelPoint position = ContextMenuWindow.ResolveOverlayPosition(
            containingBounds,
            anchorBounds,
            menuSize);

        Assert.Equal(new PixelPoint(210, 138), position);
        Assert.Equal(
            containingBounds.Bottom - anchorBounds.Bottom,
            ContextMenuWindow.ResolveOverlayAvailableHeight(containingBounds, anchorBounds));
    }

    [Fact]
    public void OverlayPositionUsesSpaceAboveBottomMountedAnchorAndStaysInsideFlyout()
    {
        PixelRect containingBounds = new(100, 100, 350, 500);
        PixelRect anchorBounds = new(430, 562, 37, 38);
        PixelSize menuSize = new(180, 80);

        PixelPoint position = ContextMenuWindow.ResolveOverlayPosition(
            containingBounds,
            anchorBounds,
            menuSize);

        Assert.Equal(new PixelPoint(270, 482), position);
        Assert.Equal(
            anchorBounds.Y - containingBounds.Y,
            ContextMenuWindow.ResolveOverlayAvailableHeight(containingBounds, anchorBounds));
    }

    [Fact]
    public void SubmenuPositionOpensBesideOwnerWhenRightSideHasSpace()
    {
        PixelRect workArea = new(0, 0, 1000, 800);
        PixelRect ownerBounds = new(200, 300, 160, 30);
        PixelSize menuSize = new(180, 200);

        PixelPoint position = ContextMenuWindow.ResolveSubmenuPosition(
            workArea,
            ownerBounds,
            menuSize,
            edgePadding: 8);

        Assert.Equal(new PixelPoint(360, 300), position);
    }

    [Fact]
    public void SubmenuPositionFlipsLeftAndClampsToBottomEdge()
    {
        PixelRect workArea = new(0, 0, 1000, 800);
        PixelRect ownerBounds = new(900, 750, 80, 30);
        PixelSize menuSize = new(180, 200);

        PixelPoint position = ContextMenuWindow.ResolveSubmenuPosition(
            workArea,
            ownerBounds,
            menuSize,
            edgePadding: 8);

        Assert.Equal(new PixelPoint(720, 592), position);
    }

    [Fact]
    public void ScreenPointPositionClampsMenuInsideWorkArea()
    {
        PixelRect workArea = new(100, 100, 500, 400);
        PixelSize menuSize = new(180, 200);

        PixelPoint insidePosition = ContextMenuWindow.ResolveScreenPointPosition(
            workArea,
            new PixelPoint(250, 180),
            menuSize,
            edgePadding: 8);
        PixelPoint clampedPosition = ContextMenuWindow.ResolveScreenPointPosition(
            workArea,
            new PixelPoint(590, 490),
            menuSize,
            edgePadding: 8);

        Assert.Equal(new PixelPoint(250, 180), insidePosition);
        Assert.Equal(new PixelPoint(412, 292), clampedPosition);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 500)]
    [InlineData(100, 1000)]
    public void ScrollHereCentersThumbAtRequestedTrackPoint(double pointerAxis, double expectedOffset)
    {
        double offset = SettingsScrollBar.CalculateScrollHereOffset(
            pointerAxis,
            trackLength: 100,
            buttonLength: 10,
            thumbLength: 20,
            maximumOffset: 1000);

        Assert.Equal(expectedOffset, offset);
    }

    [Fact]
    public void ScrollBarContextMenuUsesOrientationSpecificCommands() =>
        AvaloniaTestHost.Run(() =>
        {
            SettingsPalette palette = Palette();
            SettingsScrollBarStyle style = new(
                TrackThickness: 20,
                IdleThumbThickness: 4,
                HoverThumbThickness: 12,
                ThumbEndMargin: 4,
                MinimumThumbLength: 24,
                TrackColor: Colors.Transparent,
                IdleThumbColor: Colors.Gray,
                HoverThumbColor: Colors.LightGray,
                DragThumbColor: Colors.White,
                ArrowColor: Colors.White,
                ShowButtonsOnHover: true);
            ContextMenuWindowOptions options = new() { Palette = palette };
            using SettingsScrollBar verticalScrollBar = new(
                Orientation.Vertical,
                style,
                TrayAppDotNETCursors.Arrow,
                options);
            using SettingsScrollBar horizontalScrollBar = new(
                Orientation.Horizontal,
                style,
                TrayAppDotNETCursors.Arrow,
                options);

            string[] verticalCommands = verticalScrollBar.BuildContextMenuEntries(pointerAxis: 50)
                .Select(entry => entry.Text)
                .ToArray();
            string[] horizontalCommands = horizontalScrollBar.BuildContextMenuEntries(pointerAxis: 50)
                .Select(entry => entry.Text)
                .ToArray();

            Assert.Equal(
                ["Scroll Here", "Top", "Bottom", "Page Up", "Page Down", "Scroll Up", "Scroll Down"],
                verticalCommands);
            Assert.Equal(
                [
                    "Scroll Here", "Left Edge", "Right Edge", "Page Left", "Page Right", "Scroll Left",
                    "Scroll Right"
                ],
                horizontalCommands);
        });

    [Fact]
    public void PointerReleaseSelectionKeepsMenuOpenThroughAction() =>
        AvaloniaTestHost.Run(() =>
        {
            bool invoked = false;
            bool wasVisibleDuringAction = false;
            ContextMenuWindow? menu = null;
            menu = new ContextMenuWindow(
                [
                    new ContextMenuEntry(
                        "Add Group Card",
                        () =>
                        {
                            invoked = true;
                            wasVisibleDuringAction = menu!.IsVisible;
                        })
                ],
                new ContextMenuWindowOptions
                {
                    Palette = Palette(),
                    InvokeOnPointerReleased = true,
                    InvokeBeforeClose = true
                });

            try
            {
                menu.Show();
                menu.UpdateLayout();
                Point itemPoint = new(menu.Bounds.Width / 2, menu.Bounds.Height / 2);
                menu.MouseMove(itemPoint, RawInputModifiers.None);
                menu.MouseDown(itemPoint, MouseButton.Left, RawInputModifiers.None);

                Assert.False(invoked);
                Assert.True(menu.IsVisible);

                menu.MouseUp(itemPoint, MouseButton.Left, RawInputModifiers.None);

                Assert.True(invoked);
                Assert.True(wasVisibleDuringAction);
                Assert.True(menu.ClosedFromSelection);
                Assert.False(menu.ClosedFromDeactivation);
                Assert.False(menu.IsVisible);
            }
            finally
            {
                if (menu.IsVisible)
                    menu.Close();
            }
        });

    [Fact]
    public void InlineActionKeepsParentEntryHoveredAcrossPointerTransitions() =>
        AvaloniaTestHost.Run(() =>
        {
            EditableContextMenuWindow menu = new(
                [
                    new EditableContextMenuEntry("Saved Search 1", static () => { })
                    {
                        SecondaryText = "{Name}=~\"browser\"",
                        TrailingButton = new EditableContextMenuEntryButton(static () => { })
                        {
                            Text = "x",
                            Size = 24,
                            FontSize = 20
                        }
                    }
                ],
                new EditableContextMenuWindowOptions
                {
                    Palette = Palette(),
                    ItemHeight = 32,
                    ItemMinWidth = 260,
                    InvokeOnPointerReleased = true
                });

            try
            {
                menu.Show();
                menu.UpdateLayout();
                SettingsButton deleteButton = menu.GetVisualDescendants()
                    .OfType<SettingsButton>()
                    .Single();
                Assert.Equal(0d, deleteButton.Opacity);
                Assert.False(deleteButton.IsHitTestVisible);

                Point entryCenter = new(menu.Bounds.Width / 2, menu.Bounds.Height / 2);
                menu.MouseMove(entryCenter, RawInputModifiers.None);
                Assert.Equal(1d, deleteButton.Opacity);
                Assert.True(deleteButton.IsHitTestVisible);

                Point? deleteCenter = deleteButton.TranslatePoint(
                    new Point(deleteButton.Bounds.Width / 2, deleteButton.Bounds.Height / 2),
                    menu);
                Assert.NotNull(deleteCenter);
                menu.MouseMove(deleteCenter.Value, RawInputModifiers.None);
                menu.MouseMove(entryCenter, RawInputModifiers.None);

                Assert.Equal(1d, deleteButton.Opacity);
                Assert.True(deleteButton.IsHitTestVisible);
            }
            finally
            {
                if (menu.IsVisible)
                    menu.Close();
            }
        });

    [Fact]
    public void NonDismissingInlineActionCanReplaceEntriesWithoutClosingMenu() =>
        AvaloniaTestHost.Run(() =>
        {
            bool invoked = false;
            EditableContextMenuWindow? menu = null;
            menu = new EditableContextMenuWindow(
                [
                    new EditableContextMenuEntry("Saved Search 1", static () => { })
                    {
                        TrailingButton = new EditableContextMenuEntryButton(() =>
                        {
                            invoked = true;
                            menu!.ReplaceEntries(
                                [new EditableContextMenuEntry("Saved Search 2", static () => { })]);
                        })
                        {
                            DismissMenuOnClick = false
                        }
                    }
                ],
                new EditableContextMenuWindowOptions
                {
                    Palette = Palette(),
                    ItemHeight = 32,
                    ItemMinWidth = 260,
                    InvokeOnPointerReleased = true
                });

            try
            {
                menu.Show();
                menu.UpdateLayout();
                Point entryCenter = new(menu.Bounds.Width / 2, menu.Bounds.Height / 2);
                menu.MouseMove(entryCenter, RawInputModifiers.None);

                SettingsButton actionButton = menu.GetVisualDescendants()
                    .OfType<SettingsButton>()
                    .Single();
                Point? actionCenter = actionButton.TranslatePoint(
                    new Point(actionButton.Bounds.Width / 2, actionButton.Bounds.Height / 2),
                    menu);
                Assert.NotNull(actionCenter);
                menu.MouseDown(actionCenter.Value, MouseButton.Left, RawInputModifiers.None);
                menu.MouseUp(actionCenter.Value, MouseButton.Left, RawInputModifiers.None);
                menu.UpdateLayout();

                Assert.True(invoked);
                Assert.True(menu.IsVisible);
                Assert.False(menu.ClosedFromSelection);
                Assert.DoesNotContain(
                    menu.GetVisualDescendants().OfType<TextBlock>(),
                    textBlock => textBlock.Text == "Saved Search 1");
                Assert.Contains(
                    menu.GetVisualDescendants().OfType<TextBlock>(),
                    textBlock => textBlock.Text == "Saved Search 2");
            }
            finally
            {
                if (menu.IsVisible)
                    menu.Close();
            }
        });

    [Fact]
    public void EnteringAnotherItemClearsPreviousInlineActionHover() =>
        AvaloniaTestHost.Run(() =>
        {
            EditableContextMenuWindow menu = new(
                [
                    Entry("Saved Search 1"),
                    Entry("Saved Search 2")
                ],
                new EditableContextMenuWindowOptions
                {
                    Palette = Palette(),
                    ItemHeight = 32,
                    ItemPadding = default,
                    ItemMargin = default,
                    RootBorderThickness = default,
                    RootPadding = default,
                    RowSpacing = 0,
                    InvokeOnPointerReleased = true
                });

            try
            {
                menu.Show();
                menu.UpdateLayout();
                SettingsButton[] actionButtons = [..
                    menu.GetVisualDescendants().OfType<SettingsButton>()];
                Assert.Equal(2, actionButtons.Length);

                menu.MouseMove(new Point(menu.Bounds.Width / 2, 16), RawInputModifiers.None);
                Assert.Equal(1d, actionButtons[0].Opacity);
                Assert.Equal(0d, actionButtons[1].Opacity);

                menu.MouseMove(new Point(menu.Bounds.Width / 2, 48), RawInputModifiers.None);
                Assert.Equal(0d, actionButtons[0].Opacity);
                Assert.Equal(1d, actionButtons[1].Opacity);
            }
            finally
            {
                if (menu.IsVisible)
                    menu.Close();
            }

            return;

            static EditableContextMenuEntry Entry(string text) =>
                new(text, static () => { })
                {
                    TrailingButton = new EditableContextMenuEntryButton(static () => { })
                };
        });

    [Fact]
    public void InlineTextEditEnterCommitsWithoutSelectingOrClosing() =>
        AvaloniaTestHost.Run(() =>
        {
            bool entryInvoked = false;
            string committedText = string.Empty;
            EditableContextMenuWindow menu = new(
                [
                    new EditableContextMenuEntry("Saved Search 1", () => entryInvoked = true)
                    {
                        LeadingButton = new EditableContextMenuEntryButton(static () => { }),
                        InlineTextEdit = new EditableContextMenuInlineTextEdit(text =>
                        {
                            committedText = text;
                            return text.Trim();
                        })
                    }
                ],
                new EditableContextMenuWindowOptions
                {
                    Palette = Palette(),
                    ItemHeight = 32,
                    ItemMinWidth = 260,
                    InvokeOnPointerReleased = true
                });

            try
            {
                menu.Show();
                menu.UpdateLayout();
                Point entryCenter = new(menu.Bounds.Width / 2, menu.Bounds.Height / 2);
                menu.MouseMove(entryCenter, RawInputModifiers.None);

                SettingsButton renameButton = menu.GetVisualDescendants()
                    .OfType<SettingsButton>()
                    .Single();
                Point? renameCenter = renameButton.TranslatePoint(
                    new Point(renameButton.Bounds.Width / 2, renameButton.Bounds.Height / 2),
                    menu);
                Assert.NotNull(renameCenter);
                menu.MouseDown(renameCenter.Value, MouseButton.Left, RawInputModifiers.None);
                menu.MouseUp(renameCenter.Value, MouseButton.Left, RawInputModifiers.None);

                TextBox editor = menu.GetVisualDescendants().OfType<TextBox>().Single();
                Assert.True(editor.IsVisible);
                editor.Text = "  Browsers  ";
                editor.Focus();
                menu.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);

                Assert.Equal("  Browsers  ", committedText);
                Assert.False(editor.IsVisible);
                Assert.False(entryInvoked);
                Assert.True(menu.IsVisible);
                Assert.False(menu.ClosedFromSelection);
            }
            finally
            {
                if (menu.IsVisible)
                    menu.Close();
            }
        });

    [Fact]
    public void InlineTextEditClickOutCommitsAndConsumesTheSelection() =>
        AvaloniaTestHost.Run(() =>
        {
            int selectedEntryIndex = -1;
            string committedText = string.Empty;
            EditableContextMenuWindow menu = new(
                [
                    new EditableContextMenuEntry("Saved Search 1", () => selectedEntryIndex = 0)
                    {
                        LeadingButton = new EditableContextMenuEntryButton(static () => { }),
                        InlineTextEdit = new EditableContextMenuInlineTextEdit(text =>
                        {
                            committedText = text;
                            return text;
                        })
                    },
                    new EditableContextMenuEntry("Saved Search 2", () => selectedEntryIndex = 1)
                ],
                new EditableContextMenuWindowOptions
                {
                    Palette = Palette(),
                    ItemHeight = 32,
                    ItemPadding = default,
                    ItemMargin = default,
                    RootBorderThickness = default,
                    RootPadding = default,
                    RowSpacing = 0,
                    ItemMinWidth = 260,
                    InvokeOnPointerReleased = true
                });

            try
            {
                menu.Show();
                menu.UpdateLayout();
                menu.MouseMove(new Point(menu.Bounds.Width / 2, 16), RawInputModifiers.None);

                SettingsButton renameButton = menu.GetVisualDescendants()
                    .OfType<SettingsButton>()
                    .Single();
                Point? renameCenter = renameButton.TranslatePoint(
                    new Point(renameButton.Bounds.Width / 2, renameButton.Bounds.Height / 2),
                    menu);
                Assert.NotNull(renameCenter);
                menu.MouseDown(renameCenter.Value, MouseButton.Left, RawInputModifiers.None);
                menu.MouseUp(renameCenter.Value, MouseButton.Left, RawInputModifiers.None);

                TextBox editor = menu.GetVisualDescendants().OfType<TextBox>().Single();
                editor.Text = "Browsers";
                Point secondEntryCenter = new(menu.Bounds.Width / 2, 48);
                menu.MouseMove(secondEntryCenter, RawInputModifiers.None);
                menu.MouseDown(secondEntryCenter, MouseButton.Left, RawInputModifiers.None);
                menu.MouseUp(secondEntryCenter, MouseButton.Left, RawInputModifiers.None);

                Assert.Equal("Browsers", committedText);
                Assert.Equal(-1, selectedEntryIndex);
                Assert.True(menu.IsVisible);
                Assert.False(menu.ClosedFromSelection);
            }
            finally
            {
                if (menu.IsVisible)
                    menu.Close();
            }
        });

    private static SettingsPalette Palette() => new(
        Colors.Black,
        Colors.White,
        Colors.Gray,
        Colors.DarkGray,
        Colors.DimGray,
        Colors.Black,
        Colors.DarkGray,
        Colors.LightGray,
        Colors.Gray,
        Colors.Blue,
        Colors.Blue,
        Colors.White,
        Colors.DarkBlue,
        Colors.Blue,
        Colors.DarkBlue,
        Colors.Blue,
        Colors.Gray,
        Colors.White,
        Colors.Red,
        Colors.DarkRed,
        Colors.White);
}
