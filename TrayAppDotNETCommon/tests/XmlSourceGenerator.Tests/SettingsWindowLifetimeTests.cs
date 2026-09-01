using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.Visuals;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class SettingsWindowLifetimeTests
{
#if DEBUG
    [Fact]
    public void CommonAXAMLReloadRebuildsOpenShellAndUnsubscribesOnClose() =>
        AvaloniaTestHost.Run(() =>
        {
            CommonAXAMLReloadSettingsWindow window = new();
            window.Show();
            object? initialContent = window.Content;
            try
            {
                Assert.Equal(expected: 1, window.PageBuildCount);
                Assert.Equal(expected: 0, window.HotReloadPreparationCount);
                Assert.Equal(expected: 0, window.HotReloadRestorationCount);

                window.HotReloadEvents.Clear();
                CommonAXAMLHotReload.NotifyResourcesReloaded("Test common resources");

                Assert.Equal(expected: 2, window.PageBuildCount);
                Assert.Equal(expected: 1, window.HotReloadPreparationCount);
                Assert.Equal(expected: 1, window.HotReloadRestorationCount);
                Assert.Equal(new[] { "before", "build", "after" }, window.HotReloadEvents);
                Assert.NotSame(initialContent, window.Content);

                window.HotReloadEvents.Clear();
                GlyphCatalogHotReload.NotifyResourcesReloaded("Test glyph resources");

                Assert.Equal(expected: 3, window.PageBuildCount);
                Assert.Equal(expected: 2, window.HotReloadPreparationCount);
                Assert.Equal(expected: 2, window.HotReloadRestorationCount);
                Assert.Equal(new[] { "before", "build", "after" }, window.HotReloadEvents);
            }
            finally
            {
                window.Close();
            }

            CommonAXAMLHotReload.NotifyResourcesReloaded("Test common resources");
            GlyphCatalogHotReload.NotifyResourcesReloaded("Test glyph resources");
            Assert.Equal(expected: 3, window.PageBuildCount);
            Assert.Equal(expected: 2, window.HotReloadPreparationCount);
            Assert.Equal(expected: 2, window.HotReloadRestorationCount);
        });

    [Fact]
    public void FailedShellInitializationDoesNotAttachStaticReloadHandlers() =>
        AvaloniaTestHost.Run(() =>
        {
            FailedInitializationSettingsWindow window = new();

            Assert.True(window.InitializationFailed);

            CommonAXAMLHotReload.NotifyResourcesReloaded("Test common resources");
            GlyphCatalogHotReload.NotifyResourcesReloaded("Test glyph resources");

            Assert.Equal(expected: 0, window.HotReloadPreparationCount);
            Assert.Equal(expected: 0, window.HotReloadRestorationCount);
        });

    [Fact]
    public void StandardWindowReloadChangesOnlyChangedDimensionBaselines() =>
        AvaloniaTestHost.Run(() =>
        {
            const string widthKey = "SettingsWindow.StandardWindowWidth";
            const string heightKey = "SettingsWindow.StandardWindowHeight";
            const string minWidthKey = "SettingsWindow.StandardWindowMinWidth";
            const string minHeightKey = "SettingsWindow.StandardWindowMinHeight";
            SettingsWindowCommonResources resources = SettingsWindowCommonResources.Current;
            double originalWidth = Assert.IsType<double>(resources[widthKey]);
            double originalHeight = Assert.IsType<double>(resources[heightKey]);
            double originalMinWidth = Assert.IsType<double>(resources[minWidthKey]);
            double originalMinHeight = Assert.IsType<double>(resources[minHeightKey]);
            DimensionReloadSettingsWindow window = new(false);
            window.Show();
            try
            {
                window.Width = 1201;
                window.Height = 901;
                window.MinWidth = 701;
                window.MinHeight = 501;
                resources[widthKey] = originalWidth + 17;
                resources[minHeightKey] = originalMinHeight + 11;

                CommonAXAMLHotReload.NotifyResourcesReloaded("Test common resources");

                Assert.Equal(originalWidth + 17, window.Width);
                Assert.Equal(expected: 901, window.Height);
                Assert.Equal(expected: 701, window.MinWidth);
                Assert.Equal(originalMinHeight + 11, window.MinHeight);
            }
            finally
            {
                resources[widthKey] = originalWidth;
                resources[heightKey] = originalHeight;
                resources[minWidthKey] = originalMinWidth;
                resources[minHeightKey] = originalMinHeight;
                window.Close();
            }
        });

    [Fact]
    public void CompactWindowReloadChangesOnlyChangedDimensionBaselines() =>
        AvaloniaTestHost.Run(() =>
        {
            const string widthKey = "SettingsWindow.CompactWindowWidth";
            const string heightKey = "SettingsWindow.CompactWindowHeight";
            const string minWidthKey = "SettingsWindow.CompactWindowMinWidth";
            const string minHeightKey = "SettingsWindow.CompactWindowMinHeight";
            SettingsWindowCommonResources resources = SettingsWindowCommonResources.Current;
            double originalWidth = Assert.IsType<double>(resources[widthKey]);
            double originalHeight = Assert.IsType<double>(resources[heightKey]);
            double originalMinWidth = Assert.IsType<double>(resources[minWidthKey]);
            double originalMinHeight = Assert.IsType<double>(resources[minHeightKey]);
            DimensionReloadSettingsWindow window = new(true);
            window.Show();
            try
            {
                window.Width = 1202;
                window.Height = 902;
                window.MinWidth = 702;
                window.MinHeight = 502;
                resources[heightKey] = originalHeight + 19;
                resources[minWidthKey] = originalMinWidth + 13;

                CommonAXAMLHotReload.NotifyResourcesReloaded("Test common resources");

                Assert.Equal(expected: 1202, window.Width);
                Assert.Equal(originalHeight + 19, window.Height);
                Assert.Equal(originalMinWidth + 13, window.MinWidth);
                Assert.Equal(expected: 502, window.MinHeight);
            }
            finally
            {
                resources[widthKey] = originalWidth;
                resources[heightKey] = originalHeight;
                resources[minWidthKey] = originalMinWidth;
                resources[minHeightKey] = originalMinHeight;
                window.Close();
            }
        });

    [Fact]
    public void DerivedWindowCanRetainAppOwnedDimensionsDuringCommonReload() =>
        AvaloniaTestHost.Run(() =>
        {
            const string widthKey = "SettingsWindow.StandardWindowWidth";
            const string minWidthKey = "SettingsWindow.StandardWindowMinWidth";
            SettingsWindowCommonResources resources = SettingsWindowCommonResources.Current;
            double originalWidth = Assert.IsType<double>(resources[widthKey]);
            double originalMinWidth = Assert.IsType<double>(resources[minWidthKey]);
            DimensionReloadSettingsWindow window = new(
                useCompactProfile: false,
                applyCommonDimensionsOnReload: false);
            window.Show();
            try
            {
                window.Width = 1401;
                window.MinWidth = 801;
                resources[widthKey] = originalWidth + 23;
                resources[minWidthKey] = originalMinWidth + 17;

                CommonAXAMLHotReload.NotifyResourcesReloaded("Test common resources");

                Assert.Equal(expected: 1401, window.Width);
                Assert.Equal(expected: 801, window.MinWidth);
            }
            finally
            {
                resources[widthKey] = originalWidth;
                resources[minWidthKey] = originalMinWidth;
                window.Close();
            }
        });

    [Fact]
    public void CommonAXAMLReloadContinuesAfterSubscriberFailure()
    {
        int notificationCount = 0;
        Action failingHandler = static () => throw new InvalidOperationException("expected failure");
        Action successfulHandler = () => notificationCount++;
        CommonAXAMLHotReload.ResourcesReloaded += failingHandler;
        CommonAXAMLHotReload.ResourcesReloaded += successfulHandler;
        try
        {
            CommonAXAMLHotReload.NotifyResourcesReloaded("Test common resources");

            Assert.Equal(expected: 1, notificationCount);
        }
        finally
        {
            CommonAXAMLHotReload.ResourcesReloaded -= successfulHandler;
            CommonAXAMLHotReload.ResourcesReloaded -= failingHandler;
        }
    }
#endif

#if DEBUG
    [Fact]
    public void CommonAXAMLSynchronizationReplacesEntriesInPlace()
    {
        ResourceDictionary currentResources = new() { ["Existing"] = 1, ["Removed"] = 2 };
        ResourceDictionary candidateResources = new() { ["Existing"] = 3, ["Added"] = 4 };

        CommonAXAMLHotReload.SynchronizeResources(currentResources, candidateResources);

        Assert.Equal(expected: 3, currentResources["Existing"]);
        Assert.Equal(expected: 4, currentResources["Added"]);
        Assert.False(currentResources.ContainsKey("Removed"));
    }
#endif

    [Fact]
    public void FirstFrameShowTaskCompletesAfterTheWindowIsRevealed() =>
        AvaloniaTestHost.RunAsync(async () =>
        {
            TestSettingsWindow window = new();
            double restoredOpacity = window.Opacity;
            try
            {
                Task firstFrameReveal = window.ShowAtDefaultPositionAndActivateAfterFirstFrameAsync();

                await firstFrameReveal.WaitAsync(TimeSpan.FromSeconds(5));

                Assert.True(window.IsVisible);
                Assert.Equal(restoredOpacity, window.Opacity);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void FullWorkAreaAxisUsesSharpCorners()
    {
        PixelRect workArea = new(x: 0, y: 0, width: 1920, height: 1040);

        Assert.False(SettingsWindowCommon<TestPage>.SpansFullWorkAreaAxis(
            new PixelRect(x: 200, y: 150, width: 960, height: 670),
            workArea));
        Assert.True(SettingsWindowCommon<TestPage>.SpansFullWorkAreaAxis(
            new PixelRect(x: 300, y: 0, width: 900, height: 1040),
            workArea));
        Assert.True(SettingsWindowCommon<TestPage>.SpansFullWorkAreaAxis(
            new PixelRect(x: 300, y: -8, width: 900, height: 1056),
            workArea));
        Assert.True(SettingsWindowCommon<TestPage>.SpansFullWorkAreaAxis(
            new PixelRect(x: 0, y: 200, width: 1920, height: 600),
            workArea));
        Assert.False(SettingsWindowCommon<TestPage>.SpansFullWorkAreaAxis(
            new PixelRect(x: 2, y: 2, width: 1916, height: 1036),
            workArea));
    }

    [Fact]
    public void SettingsWindowUsesOnlyNativeResizeBorderWithCustomChrome() => AvaloniaTestHost.Run(() =>
    {
        TestSettingsWindow window = new();
        Border root = Assert.IsType<Border>(window.Content);
        Border contentSurface = Assert.IsType<Border>(root.Child);

        Assert.Equal(WindowDecorations.BorderOnly, window.WindowDecorations);
        Assert.True(window.ExtendClientAreaToDecorationsHint);
        Assert.True(window.CanResize);
        Assert.Equal(new Thickness(0), root.BorderThickness);
        Assert.Equal(new Thickness(0), contentSurface.Margin);
    });

    [Theory]
    [InlineData(true, 749.0, true)]
    [InlineData(true, 750.0, false)]
    [InlineData(false, 749.0, false)]
    public void CommonShellOwnsResponsiveSidebarCollapse(
        bool isEnabled,
        double windowWidth,
        bool expectedCollapsed) => AvaloniaTestHost.Run(() =>
    {
        ResponsiveSettingsWindow window = new(isEnabled, windowWidth);
        Border root = Assert.IsType<Border>(window.Content);
        Border contentSurface = Assert.IsType<Border>(root.Child);
        Grid shell = Assert.IsType<Grid>(contentSurface.Child);
        Grid body = Assert.Single(shell.Children.OfType<Grid>(), child => Grid.GetRow(child) == 1);
        Grid sidebar = Assert.Single(body.Children.OfType<Grid>(), child => Grid.GetColumn(child) == 0);

        Assert.Equal(!expectedCollapsed, sidebar.IsVisible);
        Assert.Equal(expectedCollapsed ? 0 : window.ConfiguredSidebarWidth, body.ColumnDefinitions[0].Width.Value);
    });

    [Fact]
    public void SidebarActionFollowsPageLayoutAndTogglesCompactRail() => AvaloniaTestHost.Run(() =>
    {
        SidebarActionSettingsWindow window = new(width: 900);
        Border root = Assert.IsType<Border>(window.Content);
        Border contentSurface = Assert.IsType<Border>(root.Child);
        Grid shell = Assert.IsType<Grid>(contentSurface.Child);
        Grid body = Assert.Single(shell.Children.OfType<Grid>(), candidate => Grid.GetRow(candidate) == 1);
        SettingsSidebar sidebar = Assert.Single(body.Children.OfType<SettingsSidebar>());
        SettingsNavItem[] navigationItems = sidebar.Navigation.Children
            .OfType<SettingsNavItem>()
            .ToArray();

        Assert.Equal(new[] { "Stable", "Collapse navigation" }, navigationItems.Select(item => item.Text));
        Assert.Same(window.SidebarAction, navigationItems[1]);

        int initialActionUpdateCount = window.SidebarActionUpdateCount;
        window.RefreshSidebarActions();
        Assert.Equal(initialActionUpdateCount + 1, window.SidebarActionUpdateCount);

        window.SidebarAction.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter
        });

        Assert.True(sidebar.IsVisible);
        Assert.Equal(expected: 52, body.ColumnDefinitions[0].Width.Value);
        Assert.Equal(SettingsUILayout.Windows11CompactNavItemWidth, window.SidebarAction.Width);
        Assert.Equal("Expand navigation", window.SidebarAction.Text);
        Assert.Equal("Expand navigation", ToolTip.GetTip(window.SidebarAction));

        window.SidebarAction.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter
        });

        Assert.Equal(window.ConfiguredSidebarWidth, body.ColumnDefinitions[0].Width.Value);
        Assert.True(double.IsNaN(window.SidebarAction.Width));
        Assert.Equal("Collapse navigation", window.SidebarAction.Text);
        Assert.Null(ToolTip.GetTip(window.SidebarAction));
    });

    [Theory]
    [InlineData(true, 900.0, false)]
    [InlineData(true, 749.0, true)]
    [InlineData(false, 900.0, false)]
    public void PageOverlayCanTrackTheVisibleContentArea(
        bool alignToContentArea,
        double windowWidth,
        bool expectedCollapsed) => AvaloniaTestHost.Run(() =>
    {
        OverlaySettingsWindow window = new(alignToContentArea, windowWidth);
        Border root = Assert.IsType<Border>(window.Content);
        Border contentSurface = Assert.IsType<Border>(root.Child);
        Grid shell = Assert.IsType<Grid>(contentSurface.Child);
        Grid overlayHost = Assert.Single(
            shell.Children.OfType<Grid>(),
            candidate => candidate.Children.Contains(window.Overlay));
        double expectedLeftInset = alignToContentArea && !expectedCollapsed
            ? window.ConfiguredSidebarWidth
            : 0;

        Assert.Equal(expectedLeftInset, overlayHost.Margin.Left);
    });

    [Theory]
    [InlineData(0, 230, 230)]
    [InlineData(double.NaN, 230, 230)]
    [InlineData(90, 230, 140)]
    [InlineData(600, 230, 520)]
    [InlineData(310, 230, 310)]
    public void SidebarWidthResolutionUsesDefaultSentinelAndBounds(
        double persistedWidth,
        double defaultWidth,
        double expectedWidth)
    {
        double width = SettingsSidebarWidthLayout.ResolvePersistedWidth(
            persistedWidth,
            defaultWidth,
            minimumWidth: 140,
            maximumWidth: 520);

        Assert.Equal(expectedWidth, width);
    }

    [Theory]
    [InlineData(960, 520)]
    [InlineData(720, 400)]
    [InlineData(400, 140)]
    public void SidebarMaximumWidthPreservesMinimumContentWidth(double windowWidth, double expectedWidth)
    {
        double width = SettingsSidebarWidthLayout.GetAvailableMaximumWidth(
            windowWidth,
            minimumWidth: 140,
            maximumWidth: 520,
            minimumContentWidth: 320);

        Assert.Equal(expectedWidth, width);
    }

    [Fact]
    public void ConfirmationVisibilityHookTracksOverlayLifetime() => AvaloniaTestHost.Run(() =>
    {
        TestSettingsWindow window = new();
        window.Show();
        try
        {
            Task<bool> confirmation = window.ConfirmAsync(title: "Confirm", message: "Message", confirmText: "Yes",
                cancelText: "No");

            Assert.Equal(new[] { true }, window.ConfirmOverlayVisibilityChanges);

            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });

            Assert.True(confirmation.IsCompletedSuccessfully);
            Assert.False(confirmation.Result);
            Assert.Equal(new[] { true, false }, window.ConfirmOverlayVisibilityChanges);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void CtrlDragPersistsSidebarWidthAndCtrlRightClickResetsIt() => AvaloniaTestHost.Run(() =>
    {
        SidebarResizeSettingsWindow window = new();
        window.Show();
        window.UpdateLayout();

        SettingsSidebarResizeHandle resizeHandle = Assert.Single(
            window.GetVisualDescendants().OfType<SettingsSidebarResizeHandle>());
        Assert.False(resizeHandle.IsHitTestVisible);
        Assert.Equal(expected: 230, window.DisplayedSidebarWidth);

        Point dragStart = GetWindowPoint(window, resizeHandle);
        window.MouseMove(dragStart, RawInputModifiers.None);
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.LeftCtrl });

        Assert.True(resizeHandle.IsHitTestVisible);
        Assert.Same(TrayAppDotNETCursors.SizeWestEast, resizeHandle.Cursor);

        Point dragEnd = new(dragStart.X + 60, dragStart.Y);
        window.MouseDown(dragStart, MouseButton.Left, RawInputModifiers.Control);
        window.MouseMove(dragEnd, RawInputModifiers.Control);
        window.MouseUp(dragEnd, MouseButton.Left, RawInputModifiers.Control);

        Assert.Equal(expected: 290, window.PersistedSidebarWidth);
        Assert.Equal(expected: 290, window.DisplayedSidebarWidth);
        Assert.Equal(expected: 1, window.SaveCount);

        Point resetPoint = GetWindowPoint(window, resizeHandle);
        window.MouseMove(resetPoint, RawInputModifiers.Control);
        window.MouseDown(resetPoint, MouseButton.Right, RawInputModifiers.Control);
        window.MouseUp(resetPoint, MouseButton.Right, RawInputModifiers.Control);

        Assert.Equal(expected: 0, window.PersistedSidebarWidth);
        Assert.Equal(expected: 230, window.DisplayedSidebarWidth);
        Assert.Equal(expected: 2, window.SaveCount);

        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.LeftCtrl });
        Assert.False(resizeHandle.IsHitTestVisible);
        window.Close();

        return;

        static Point GetWindowPoint(Window owner, Control control)
        {
            Point controlCenter = new(control.Bounds.Width / 2, control.Bounds.Height / 2);
            return control.TranslatePoint(controlCenter, owner)
                   ?? throw new InvalidOperationException("Resize handle is not attached to the test window.");
        }
    });

    [Fact]
    public void FailedPageBuildKeepsPreviousPageGenerationActive() => AvaloniaTestHost.Run(() =>
    {
        TestSettingsWindow window = new();
        object stableDataContext = window.StablePage.DataContext!;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(window.SelectFailingPage);

        Assert.Equal(expected: "expected page failure", exception.Message);
        Assert.Equal(TestPage.Stable, window.SelectedPage);
        Assert.Same(stableDataContext, window.StablePage.DataContext);
        Assert.Equal(expected: 1, window.FailedPageCleanupCount);
    });

    [Fact]
    public void PaletteRefreshUpdatesBrushWithoutReplacingVisualTree() => AvaloniaTestHost.Run(() =>
    {
        PaletteRefreshSettingsWindow window = new();
        Border root = Assert.IsType<Border>(window.Content);
        TextBlock page = window.StablePage;
        SolidColorBrush background = Assert.IsType<SolidColorBrush>(root.Background);
        SolidColorBrush foreground = Assert.IsType<SolidColorBrush>(page.Foreground);

        window.SetColors(Colors.DarkRed, Colors.Yellow);

        Assert.Same(root, window.Content);
        Assert.Same(page, window.StablePage);
        Assert.Same(background, root.Background);
        Assert.Same(foreground, page.Foreground);
        Assert.Equal(Colors.DarkRed, background.Color);
        Assert.Equal(Colors.Yellow, foreground.Color);
    });

    [Fact]
    public void SettingsSearchStitchesFiltersAndRestoresPages() => AvaloniaTestHost.Run(() =>
    {
        SearchSettingsWindow window = new();
        window.Show();
        Border shellRoot = Assert.IsType<Border>(window.Content);
        SettingsSearchBox searchBox = Assert.Single(
            shellRoot.GetVisualDescendants().OfType<SettingsSearchBox>());
        TextBox searchInput = Assert.Single(searchBox.GetVisualDescendants().OfType<TextBox>());

        searchInput.Focus();
        window.KeyTextInput("al");

        Assert.True(window.AlphaPageRoot.IsVisible);
        Assert.True(window.AlphaCard.IsVisible);
        Assert.False(window.BetaPageRoot.IsVisible);
        Assert.False(window.BetaCard.IsVisible);
        Assert.Equal(expected: 1, window.PageCleanupCount);

        searchBox.Clear();

        Assert.Equal(SearchPage.Alpha, window.SelectedPage);
        Assert.True(window.AlphaPageRoot.IsVisible);
        Assert.Equal(expected: 3, window.PageCleanupCount);
        window.Close();
    });

    [Fact]
    public void SettingsSearchUsesLocalizedSynonymGroups() => AvaloniaTestHost.Run(() =>
    {
        SearchSettingsWindow window = new();
        window.Show();
        Border shellRoot = Assert.IsType<Border>(window.Content);
        SettingsSearchBox searchBox = Assert.Single(
            shellRoot.GetVisualDescendants().OfType<SettingsSearchBox>());

        searchBox.SearchText = "highest";

        Assert.True(window.AlphaPageRoot.IsVisible);
        Assert.True(window.MaximumCard.IsVisible);
        Assert.False(window.AlphaCard.IsVisible);
        Assert.False(window.BetaPageRoot.IsVisible);
        window.Close();
    });

    [Fact]
    public void ClickingSelectedNavigationItemScrollsCurrentPageToTop() => AvaloniaTestHost.Run(() =>
    {
        TestSettingsWindow window = new();
        window.Show();
        try
        {
            window.UpdateLayout();
            FieldInfo pageScrollOffsetsField = typeof(SettingsWindowCommon<TestPage>).GetField(
                                                   name: "_pageScrollOffsets",
                                                   BindingFlags.Instance | BindingFlags.NonPublic)
                                               ?? throw new InvalidOperationException(
                                                   "The settings scroll-offset store is unavailable.");
            Dictionary<TestPage, double> pageScrollOffsets = Assert.IsType<Dictionary<TestPage, double>>(
                pageScrollOffsetsField.GetValue(window));
            pageScrollOffsets[TestPage.Stable] = 300;

            SettingsNavItem selectedNavigationItem = Assert.Single(
                window.GetVisualDescendants().OfType<SettingsNavItem>(),
                navigationItem => navigationItem.IsSelected);
            Point navigationItemCenter = new(
                selectedNavigationItem.Bounds.Width / 2,
                selectedNavigationItem.Bounds.Height / 2);
            Point windowPoint = selectedNavigationItem.TranslatePoint(navigationItemCenter, window)
                                ?? throw new InvalidOperationException(
                                    "The selected navigation item is not attached to the test window.");
            window.MouseDown(windowPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(windowPoint, MouseButton.Left, RawInputModifiers.None);

            Assert.Equal(expected: 0, pageScrollOffsets[TestPage.Stable]);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void SwatchColorUpdateReusesBrush() => AvaloniaTestHost.Run(() =>
    {
        SettingsSwatch swatch = new(CreatePalette(Colors.Black, Colors.White));
        swatch.SetColor(Colors.Blue, Colors.Black);
        SolidColorBrush brush = Assert.IsType<SolidColorBrush>(swatch.Background);

        swatch.SetColor(Colors.Red, Colors.Black);

        Assert.Same(brush, swatch.Background);
        Assert.Equal(Colors.Red, brush.Color);
    });

    [Fact]
    public void FocusedNumberEditPublishesImmediatelyAndEnterBlurs() => AvaloniaTestHost.Run(() =>
    {
        EditorSettingsWindow window = new();
        window.Show();
        window.UpdateLayout();

        SettingsNumberBox numberBox = Assert.Single(
            window.GetVisualDescendants().OfType<SettingsNumberBox>());
        TextBox textBox = Assert.Single(numberBox.GetVisualDescendants().OfType<TextBox>());
        textBox.Focus();
        textBox.SelectAll();
        window.KeyTextInput("750");

        Assert.Equal(expected: 750, window.PersistedValue);
        Assert.Equal(expected: 1, window.SaveCount);
        Assert.Same(textBox, window.FocusManager.GetFocusedElement());

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, keySymbol: null);

        Assert.Null(window.FocusManager.GetFocusedElement());
        window.Close();

        Assert.Equal(expected: 750, window.PersistedValue);
        Assert.Equal(expected: 1, window.SaveCount);
    });

    [Fact]
    public void StandardTextBoxEnterSavesThroughLostFocusAndBlurs() => AvaloniaTestHost.Run(() =>
    {
        EditorSettingsWindow window = new();
        window.Show();
        window.UpdateLayout();

        TextBox textBox = window.TextEditor;
        textBox.Focus();
        textBox.SelectAll();
        window.KeyTextInput("updated");

        Assert.Equal(expected: "initial", window.PersistedText);
        Assert.Same(textBox, window.FocusManager.GetFocusedElement());

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, keySymbol: null);
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, keySymbol: null);

        Assert.Equal(expected: "updated", window.PersistedText);
        Assert.Equal(expected: 1, window.SaveCount);
        Assert.Null(window.FocusManager.GetFocusedElement());
        window.Close();
    });

    [Fact]
    public void ClickingOutsideNumberBoxCommitsAndBlurs() => AvaloniaTestHost.Run(() =>
    {
        EditorSettingsWindow window = new();
        window.Show();
        window.UpdateLayout();

        SettingsNumberBox numberBox = Assert.Single(
            window.GetVisualDescendants().OfType<SettingsNumberBox>());
        TextBox textBox = Assert.Single(numberBox.GetVisualDescendants().OfType<TextBox>());
        textBox.Focus();
        textBox.SelectAll();
        window.KeyTextInput("750");

        ClickSelectedNavigationItem(window);

        Assert.Equal(expected: 750, window.PersistedValue);
        Assert.Equal(expected: 1, window.SaveCount);
        Assert.NotSame(textBox, window.FocusManager.GetFocusedElement());
        window.Close();
    });

    [Fact]
    public void ClickingOutsideStandardTextBoxSavesAndBlurs() => AvaloniaTestHost.Run(() =>
    {
        EditorSettingsWindow window = new();
        window.Show();
        window.UpdateLayout();

        TextBox textBox = window.TextEditor;
        textBox.Focus();
        textBox.SelectAll();
        window.KeyTextInput("updated");

        ClickSelectedNavigationItem(window);

        Assert.Equal(expected: "updated", window.PersistedText);
        Assert.Equal(expected: 1, window.SaveCount);
        Assert.NotSame(textBox, window.FocusManager.GetFocusedElement());
        window.Close();
    });

    private static void ClickSelectedNavigationItem(EditorSettingsWindow window)
    {
        SettingsNavItem selectedNavigationItem = Assert.Single(
            window.GetVisualDescendants().OfType<SettingsNavItem>(),
            navigationItem => navigationItem.IsSelected);
        Point navigationItemCenter = new(
            selectedNavigationItem.Bounds.Width / 2,
            selectedNavigationItem.Bounds.Height / 2);
        Point windowPoint = selectedNavigationItem.TranslatePoint(navigationItemCenter, window)
                            ?? throw new InvalidOperationException(
                                "The selected navigation item is not attached to the test window.");
        window.MouseDown(windowPoint, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(windowPoint, MouseButton.Left, RawInputModifiers.None);
    }

    private enum TestPage
    {
        Stable,
        Failing
    }

    private enum SearchPage
    {
        Alpha,
        Beta
    }

    private sealed class TestSettingsWindow : SettingsWindowCommon<TestPage>
    {
        private readonly SettingsPalette _testPalette = CreatePalette(Colors.Black, Colors.White);

        public TestSettingsWindow()
        {
            StablePage = new TextBlock { Text = "stable", DataContext = new object() };
            ConfigureSettingsWindow(title: "Test", icon: null);
            InitializeSettingsShell();
        }

        public TextBlock StablePage { get; }

        public TestPage SelectedPage => CurrentPageKey;

        public int FailedPageCleanupCount { get; private set; }
        public List<bool> ConfirmOverlayVisibilityChanges { get; } = [];

        protected override SettingsPalette ResolvePalette() => _testPalette;
        protected override bool EnableRoundedCorners => false;
        protected override TestPage DefaultPageKey => TestPage.Stable;
        protected override string HeaderText => "Test";
        protected override string OpenSettingsFolderText => "Open";
        protected override string SettingsFolderPath => Environment.CurrentDirectory;

        protected override void OnConfirmOverlayVisibilityChanged(bool isVisible) =>
            ConfirmOverlayVisibilityChanges.Add(isVisible);

        public void SelectFailingPage() => SelectPage(TestPage.Failing);

        protected override IReadOnlyList<SettingsPageDescriptor<TestPage>> CreatePageDescriptors() =>
        [
            new(TestPage.Stable, Label: "Stable", () => StablePage),
            new(TestPage.Failing, Label: "Failing", BuildFailingPage)
        ];

        protected override void Save()
        {
        }

        private Control BuildFailingPage()
        {
            AddPageCleanup(() => FailedPageCleanupCount++);
            throw new InvalidOperationException("expected page failure");
        }
    }

    private sealed class CommonAXAMLReloadSettingsWindow : SettingsWindowCommon<TestPage>
    {
        private readonly SettingsPalette _testPalette = CreatePalette(Colors.Black, Colors.White);

        public CommonAXAMLReloadSettingsWindow() => InitializeSettingsShell();

        public int PageBuildCount { get; private set; }
        public int HotReloadPreparationCount { get; private set; }
        public int HotReloadRestorationCount { get; private set; }
        public List<string> HotReloadEvents { get; } = [];

        protected override SettingsPalette ResolvePalette() => _testPalette;
        protected override bool EnableRoundedCorners => false;
        protected override TestPage DefaultPageKey => TestPage.Stable;
        protected override string HeaderText => "Test";
        protected override string OpenSettingsFolderText => "Open";
        protected override string SettingsFolderPath => Environment.CurrentDirectory;

#if DEBUG
        protected override void OnBeforeHotReloadShellRebuild()
        {
            HotReloadPreparationCount++;
            HotReloadEvents.Add("before");
        }

        protected override void OnAfterHotReloadShellRebuild()
        {
            HotReloadRestorationCount++;
            HotReloadEvents.Add("after");
        }
#endif

        protected override IReadOnlyList<SettingsPageDescriptor<TestPage>> CreatePageDescriptors() =>
        [
            new(TestPage.Stable, Label: "Stable", BuildPage)
        ];

        protected override void Save()
        {
        }

        private TextBlock BuildPage()
        {
            PageBuildCount++;
            HotReloadEvents.Add("build");
            return new TextBlock { Text = "stable" };
        }
    }

#if DEBUG
    private sealed class FailedInitializationSettingsWindow : SettingsWindowCommon<TestPage>
    {
        private readonly SettingsPalette _testPalette = CreatePalette(Colors.Black, Colors.White);

        public FailedInitializationSettingsWindow()
        {
            try
            {
                InitializeSettingsShell();
            }
            catch (InvalidOperationException exception)
                when (exception.Message == "expected initialization failure")
            {
                InitializationFailed = true;
            }
        }

        public bool InitializationFailed { get; }
        public int HotReloadPreparationCount { get; private set; }
        public int HotReloadRestorationCount { get; private set; }

        protected override SettingsPalette ResolvePalette() => _testPalette;
        protected override bool EnableRoundedCorners => false;
        protected override TestPage DefaultPageKey => TestPage.Stable;
        protected override string HeaderText => "Test";
        protected override string OpenSettingsFolderText => "Open";
        protected override string SettingsFolderPath => Environment.CurrentDirectory;
        protected override void OnBeforeHotReloadShellRebuild() => HotReloadPreparationCount++;
        protected override void OnAfterHotReloadShellRebuild() => HotReloadRestorationCount++;

        protected override IReadOnlyList<SettingsPageDescriptor<TestPage>> CreatePageDescriptors() =>
        [
            new(TestPage.Stable, Label: "Stable", BuildFailingPage)
        ];

        protected override void Save()
        {
        }

        private static Control BuildFailingPage() =>
            throw new InvalidOperationException("expected initialization failure");
    }

    private sealed class DimensionReloadSettingsWindow : SettingsWindowCommon<TestPage>
    {
        private readonly SettingsPalette _testPalette = CreatePalette(Colors.Black, Colors.White);

        public DimensionReloadSettingsWindow(
            bool useCompactProfile,
            bool applyCommonDimensionsOnReload = true)
        {
            ApplyCommonAXAMLWindowDimensionsOnReload = applyCommonDimensionsOnReload;
            if (useCompactProfile)
                ConfigureCompactSettingsWindow(title: "Compact test", icon: null);
            else
                ConfigureSettingsWindow(title: "Standard test", icon: null);
            InitializeSettingsShell();
        }

        protected override SettingsPalette ResolvePalette() => _testPalette;
        protected override bool ApplyCommonAXAMLWindowDimensionsOnReload { get; }
        protected override bool EnableRoundedCorners => false;
        protected override TestPage DefaultPageKey => TestPage.Stable;
        protected override string HeaderText => "Test";
        protected override string OpenSettingsFolderText => "Open";
        protected override string SettingsFolderPath => Environment.CurrentDirectory;

        protected override IReadOnlyList<SettingsPageDescriptor<TestPage>> CreatePageDescriptors() =>
        [
            new(TestPage.Stable, Label: "Stable", static () => new TextBlock())
        ];

        protected override void Save()
        {
        }
    }
#endif

    private sealed class PaletteRefreshSettingsWindow : SettingsWindowCommon<TestPage>
    {
        private Color _background = Colors.Black;
        private Color _foreground = Colors.White;

        public PaletteRefreshSettingsWindow()
        {
            StablePage = TrayAppDotNETSettingsUI.Text(text: "stable", Palette);
            InitializeSettingsShell();
        }

        public TextBlock StablePage { get; }

        protected override bool EnableRoundedCorners => false;
        protected override TestPage DefaultPageKey => TestPage.Stable;
        protected override string HeaderText => "Test";
        protected override string OpenSettingsFolderText => "Open";
        protected override string SettingsFolderPath => Environment.CurrentDirectory;

        public void SetColors(Color background, Color foreground)
        {
            _background = background;
            _foreground = foreground;
            RefreshPalette();
        }

        protected override SettingsPalette ResolvePalette() => CreatePalette(_background, _foreground);

        protected override IReadOnlyList<SettingsPageDescriptor<TestPage>> CreatePageDescriptors() =>
        [
            new(TestPage.Stable, Label: "Stable", () => StablePage)
        ];

        protected override void Save()
        {
        }
    }

    private sealed class ResponsiveSettingsWindow : SettingsWindowCommon<TestPage>
    {
        private readonly SettingsPalette _testPalette = CreatePalette(Colors.Black, Colors.White);

        public ResponsiveSettingsWindow(bool enableResponsiveSidebarCollapse, double width)
        {
            EnableResponsiveSidebarCollapse = enableResponsiveSidebarCollapse;
            ConfigureSettingsWindow(title: "Responsive Test", icon: null);
            MinWidth = 0;
            Width = width;
            ClientSize = new Size(width, height: 600);
            InitializeSettingsShell();
        }

        public double ConfiguredSidebarWidth => SidebarWidth;

        protected override bool EnableRoundedCorners => false;
        protected override bool EnableResponsiveSidebarCollapse { get; }
        protected override double SidebarCollapseThreshold => 750;
        protected override TestPage DefaultPageKey => TestPage.Stable;
        protected override string HeaderText => "Responsive Test";
        protected override string OpenSettingsFolderText => "Open";
        protected override string SettingsFolderPath => Environment.CurrentDirectory;
        protected override SettingsPalette ResolvePalette() => _testPalette;

        protected override IReadOnlyList<SettingsPageDescriptor<TestPage>> CreatePageDescriptors() =>
        [
            new(TestPage.Stable, Label: "Stable", static () => new TextBlock())
        ];

        protected override void Save()
        {
        }
    }

    private sealed class SidebarActionSettingsWindow : SettingsWindowCommon<TestPage>
    {
        private const string CollapseGlyph = "\uE76B";
        private const string ExpandGlyph = "\uE76C";
        private readonly SettingsPalette _testPalette = CreatePalette(Colors.Black, Colors.White);

        public SidebarActionSettingsWindow(double width)
        {
            ConfigureSettingsWindow(title: "Sidebar Action Test", icon: null);
            MinWidth = 0;
            Width = width;
            ClientSize = new Size(width, height: 600);
            InitializeSettingsShell();
        }

        public SettingsNavItem SidebarAction { get; private set; } = null!;
        public int SidebarActionUpdateCount { get; private set; }
        public double ConfiguredSidebarWidth => SidebarWidth;

        public void RefreshSidebarActions() => RefreshSidebarCollapseControls();

        protected override bool EnableRoundedCorners => false;
        protected override bool UseWindows11SettingsNavigation => true;
        protected override double CollapsedSidebarWidth => 52;
        protected override TestPage DefaultPageKey => TestPage.Stable;
        protected override string HeaderText => "Sidebar Action Test";
        protected override string OpenSettingsFolderText => "Open";
        protected override string SettingsFolderPath => Environment.CurrentDirectory;
        protected override SettingsPalette ResolvePalette() => _testPalette;

        protected override IReadOnlyList<SettingsPageDescriptor<TestPage>> CreatePageDescriptors() =>
        [
            new(TestPage.Stable, Label: "Stable", static () => new TextBlock())
        ];

        protected override IReadOnlyList<SettingsNavItem> CreateSidebarNavigationActions(SettingsPalette palette)
        {
            SidebarAction = new SettingsNavItem(
                "Collapse navigation",
                palette,
                useWindows11Style: true,
                navigationGlyph: Glyph.SegoeFluent(CollapseGlyph));
            SidebarAction.Click += (_, _) => ToggleSidebarCollapse();
            return [SidebarAction];
        }

        protected override void UpdateSidebarNavigationActions(
            IReadOnlyList<SettingsNavItem> navigationActions,
            bool isCollapsed)
        {
            SidebarActionUpdateCount++;
            SettingsNavItem sidebarAction = navigationActions[0];
            sidebarAction.SetText(isCollapsed ? "Expand navigation" : "Collapse navigation");
            sidebarAction.SetNavigationGlyph(
                Glyph.SegoeFluent(isCollapsed ? ExpandGlyph : CollapseGlyph));
        }

        protected override void Save()
        {
        }
    }

    private sealed class OverlaySettingsWindow : SettingsWindowCommon<TestPage>
    {
        private readonly SettingsPalette _testPalette = CreatePalette(Colors.Black, Colors.White);
        private readonly bool _alignToContentArea;

        public OverlaySettingsWindow(bool alignToContentArea, double width)
        {
            _alignToContentArea = alignToContentArea;
            Overlay = new Border();
            ConfigureSettingsWindow(title: "Overlay Test", icon: null);
            MinWidth = 0;
            Width = width;
            ClientSize = new Size(width, height: 600);
            InitializeSettingsShell();
        }

        public Border Overlay { get; }
        public double ConfiguredSidebarWidth => SidebarWidth;

        protected override bool EnableRoundedCorners => false;
        protected override bool EnableResponsiveSidebarCollapse => true;
        protected override double SidebarCollapseThreshold => 750;
        protected override TestPage DefaultPageKey => TestPage.Stable;
        protected override string HeaderText => "Overlay Test";
        protected override string OpenSettingsFolderText => "Open";
        protected override string SettingsFolderPath => Environment.CurrentDirectory;
        protected override SettingsPalette ResolvePalette() => _testPalette;
        protected override Control? ResolvePageOverlay(Control pageRoot) => Overlay;
        protected override bool PageOverlayAlignsToContentArea(Control pageRoot) => _alignToContentArea;

        protected override IReadOnlyList<SettingsPageDescriptor<TestPage>> CreatePageDescriptors() =>
        [
            new(TestPage.Stable, Label: "Stable", static () => new TextBlock())
        ];

        protected override void Save()
        {
        }
    }

    private sealed class SidebarResizeSettingsWindow : SettingsWindowCommon<TestPage>
    {
        private readonly SettingsPalette _testPalette = CreatePalette(Colors.Black, Colors.White);
        private readonly TestSidebarWidthSettings _settings = new();

        public SidebarResizeSettingsWindow()
        {
            ConfigureSettingsWindow(title: "Sidebar Resize Test", icon: null);
            InitializeSettingsShell();
        }

        public double PersistedSidebarWidth => _settings.SettingsSidebarWidth;
        public double DisplayedSidebarWidth => FindBody().ColumnDefinitions[0].Width.Value;
        public int SaveCount { get; private set; }

        protected override bool EnableRoundedCorners => false;
        protected override ISettingsSidebarWidthSettings SidebarWidthSettings => _settings;
        protected override TestPage DefaultPageKey => TestPage.Stable;
        protected override string HeaderText => "Test";
        protected override string OpenSettingsFolderText => "Open";
        protected override string SettingsFolderPath => Environment.CurrentDirectory;
        protected override SettingsPalette ResolvePalette() => _testPalette;

        protected override IReadOnlyList<SettingsPageDescriptor<TestPage>> CreatePageDescriptors() =>
        [
            new(TestPage.Stable, Label: "Stable", static () => new TextBlock())
        ];

        protected override void Save() => SaveCount++;

        private Grid FindBody()
        {
            Border root = Assert.IsType<Border>(Content);
            Border contentSurface = Assert.IsType<Border>(root.Child);
            Grid shell = Assert.IsType<Grid>(contentSurface.Child);
            return Assert.Single(shell.Children.OfType<Grid>(), child => Grid.GetRow(child) == 1);
        }
    }

    private sealed class EditorSettingsWindow : SettingsWindowCommon<TestPage>
    {
        private readonly SettingsPalette _testPalette = CreatePalette(Colors.Black, Colors.White);

        public EditorSettingsWindow()
        {
            ConfigureSettingsWindow(title: "Editor Test", icon: null);
            InitializeSettingsShell();
        }

        public int PersistedValue { get; private set; } = 60_000;
        public string PersistedText { get; private set; } = "initial";
        public TextBox TextEditor { get; private set; } = null!;
        public int SaveCount { get; private set; }

        protected override bool EnableRoundedCorners => false;
        protected override TestPage DefaultPageKey => TestPage.Stable;
        protected override string HeaderText => "Test";
        protected override string OpenSettingsFolderText => "Open";
        protected override string SettingsFolderPath => Environment.CurrentDirectory;
        protected override SettingsPalette ResolvePalette() => _testPalette;

        protected override IReadOnlyList<SettingsPageDescriptor<TestPage>> CreatePageDescriptors() =>
        [
            new(TestPage.Stable, Label: "Stable", BuildPage)
        ];

        protected override void Save() => SaveCount++;

        private StackPanel BuildPage()
        {
            StackPanel stack = PageStack(title: "Test", Palette);
            stack.Children.Add(IntCard(
                title: "Sampling interval",
                description: "Test value",
                PersistedValue,
                min: 1,
                max: 60_000,
                value => PersistedValue = value,
                Palette));
            TextEditor = TrayAppDotNETSettingsUI.TextBox(Palette, width: 120, PersistedText);
            TextEditor.LostFocus += (_, _) =>
            {
                PersistedText = TextEditor.Text ?? string.Empty;
                Save();
            };
            stack.Children.Add(Card(
                title: "Text value",
                description: "Test text",
                TextEditor,
                Palette));
            return stack;
        }
    }

    private sealed class TestSidebarWidthSettings : ISettingsSidebarWidthSettings
    {
        public double SettingsSidebarWidth { get; set; }
    }

    private sealed class SearchSettingsWindow : SettingsWindowCommon<SearchPage>
    {
        private readonly SettingsPalette _testPalette = CreatePalette(Colors.Black, Colors.White);

        public SearchSettingsWindow()
        {
            ConfigureSettingsWindow(title: "Search Test", icon: null);
            InitializeSettingsShell();
        }

        public StackPanel AlphaPageRoot { get; private set; } = null!;
        public StackPanel BetaPageRoot { get; private set; } = null!;
        public Border AlphaCard { get; private set; } = null!;
        public Border BetaCard { get; private set; } = null!;
        public Border MaximumCard { get; private set; } = null!;
        public int PageCleanupCount { get; private set; }
        public SearchPage SelectedPage => CurrentPageKey;

        protected override bool EnableRoundedCorners => false;
        protected override SearchPage DefaultPageKey => SearchPage.Alpha;
        protected override string HeaderText => "Search Test";
        protected override string OpenSettingsFolderText => "Open";
        protected override string SettingsFolderPath => Environment.CurrentDirectory;
        protected override SettingsPalette ResolvePalette() => _testPalette;

        protected override IReadOnlyList<SettingsPageDescriptor<SearchPage>> CreatePageDescriptors() =>
        [
            new(SearchPage.Alpha, Label: "Alpha", BuildAlphaPage),
            new(SearchPage.Beta, Label: "Beta", BuildBetaPage)
        ];

        protected override void Save()
        {
        }

        private StackPanel BuildAlphaPage()
        {
            AddPageCleanup(() => PageCleanupCount++);
            AlphaPageRoot = PageStack(title: "Alpha", Palette);
            AlphaCard = Card(title: "Alpha option", description: "Starts the alpha feature.", rightControl: null,
                Palette);
            MaximumCard = Card(title: "Max apps per row", description: "Limits each row.", rightControl: null, Palette);
            AlphaPageRoot.Children.Add(AlphaCard);
            AlphaPageRoot.Children.Add(MaximumCard);
            return AlphaPageRoot;
        }

        private StackPanel BuildBetaPage()
        {
            AddPageCleanup(() => PageCleanupCount++);
            BetaPageRoot = PageStack(title: "Beta", Palette);
            BetaCard = Card(title: "Beta option", description: "Starts the beta feature.", rightControl: null, Palette);
            BetaPageRoot.Children.Add(BetaCard);
            return BetaPageRoot;
        }
    }

    private static SettingsPalette CreatePalette(Color background, Color foreground) =>
        new(
            background,
            foreground,
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
