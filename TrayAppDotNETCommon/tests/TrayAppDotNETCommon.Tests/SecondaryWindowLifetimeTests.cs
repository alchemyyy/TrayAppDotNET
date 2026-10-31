using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Install;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.WarmWindows;
using Xunit;
using ControlMap = TrayAppDotNETCommon.UI.ControlMap;

namespace TrayAppDotNETCommon.Tests;

public sealed class SecondaryWindowLifetimeTests
{
    private const string FlyoutMapName = "FlyoutLifetimeTestMap";
    private static readonly ControlMapNodeID FlyoutSurfaceID = new(FlyoutMapName, "Flyout");
    private static readonly ControlMapNodeID FlyoutCloseID = new(FlyoutMapName, "Flyout.Close");
    private static readonly ControlMapNodeID FlyoutOpenWindowID = new(FlyoutMapName, "Flyout.OpenWindow");

    // A flyout surface with an Escape close command and a window-opening button, as every app map declares one
    static SecondaryWindowLifetimeTests()
    {
        FlyoutTestControlMap map = new();
        Surface surface = new() { ID = "Flyout" };
        surface.Children.Add(new Leaf { ID = "Close", Kind = LeafKind.Command, Keys = "Escape" });
        surface.Children.Add(new Leaf { ID = "OpenWindow", Kind = LeafKind.Button });
        map.Children.Add(surface);
        ControlMapCatalog.Register(map);
    }

    [Fact]
    public void ColorPickerCloseSeversContentAndExternalDelegates() => AvaloniaTestHost.Run(() =>
    {
        (TrayAppDotNETColorPickerWindow picker, WeakReference listenerReference) = CreateColorPicker();
        picker.Show();
        picker.Close();
        picker.Dispose();
        picker.Dispose();

        Assert.Null(picker.Content);
        Collect();
        Assert.False(listenerReference.IsAlive);
        GC.KeepAlive(picker);
    });

    [Fact]
    public void ConfirmationCloseIsIdempotentAndSeversContent() => AvaloniaTestHost.Run(() =>
    {
        TrayAppDotNETConfirmationWindow prompt = new(
            title: "Proceed with update?",
            description: "App: TestApp",
            confirmText: "Install update",
            TestPalettes.Default(),
            rounded: true,
            alternateText: "Skip release",
            cancelText: "Close",
            new TrayAppDotNETUpdateModalDetails(
                NewVersionText: "New version available: 236",
                CurrentVersionText: "Current running version: 235",
                ReleasesLinkText: "view releases",
                new Uri("https://github.com/test-owner/test-repository/releases"),
                WebsiteLinkText: "trayapp.net"),
            modalFooterText: "Updating will cause app to restart.",
            useModalContentLayout: true);

        prompt.Show();
        SettingsButton alternateButton = Assert.Single(
            prompt.GetVisualDescendants().OfType<SettingsButton>(),
            button => button.Text == "Skip release");
        SettingsButton confirmButton = Assert.Single(
            prompt.GetVisualDescendants().OfType<SettingsButton>(),
            button => button.Text == "Install update");
        SettingsButton closeButton = Assert.Single(
            prompt.GetVisualDescendants().OfType<SettingsButton>(),
            button => button.Text == "Close");
        Assert.Empty(prompt.GetVisualDescendants().OfType<TrayAppDotNETCaptionCloseButton>());
        Grid actionButtons = Assert.Single(
            prompt.GetVisualDescendants().OfType<Grid>(),
            grid => grid.Children.Contains(alternateButton));
        Assert.Equal(HorizontalAlignment.Stretch, actionButtons.HorizontalAlignment);
        Assert.Equal(ConfirmationWindowLayout.ModalActionButtonSpacing, actionButtons.ColumnSpacing);
        Assert.Equal(ConfirmationWindowLayout.ModalActionButtonsMargin, actionButtons.Margin);
        Assert.Equal(
            expected: 12d,
            ConfirmationWindowLayout.ModalBodyMargin.Left + actionButtons.Margin.Left);
        Assert.Equal(expected: 12d, actionButtons.Margin.Right);
        Assert.Equal(HorizontalAlignment.Left, alternateButton.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Left, confirmButton.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Stretch, closeButton.HorizontalAlignment);
        Assert.Equal(ConfirmationWindowLayout.ActionButtonPadding, alternateButton.Padding);
        Assert.Equal(ConfirmationWindowLayout.ActionButtonPadding, closeButton.Padding);
        Assert.Equal(expected: 3, actionButtons.Children.Count);
        Assert.Equal(expected: 3, actionButtons.ColumnDefinitions.Count);
        Assert.Equal(GridLength.Auto, actionButtons.ColumnDefinitions[0].Width);
        Assert.Equal(GridLength.Auto, actionButtons.ColumnDefinitions[1].Width);
        Assert.Equal(GridLength.Star, actionButtons.ColumnDefinitions[2].Width);
        Assert.Equal(expected: 0, Grid.GetColumn(alternateButton));
        Assert.Equal(expected: 1, Grid.GetColumn(confirmButton));
        Assert.Equal(expected: 2, Grid.GetColumn(closeButton));
        Grid modalBody = Assert.Single(
            prompt.GetVisualDescendants().OfType<Grid>(),
            grid => grid.Margin == ConfirmationWindowLayout.ModalBodyMargin);
        Assert.Equal(expected: 4, modalBody.RowDefinitions.Count);
        TextBlock descriptionText = Assert.Single(
            prompt.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "App: TestApp");
        Assert.Equal(SettingsUILayout.DescriptionFontSize, descriptionText.FontSize);
        Assert.Equal(expected: default, descriptionText.Margin);
        Assert.Equal(
            SettingsUILayout.DescriptionFontSize + ConfirmationWindowLayout.VersionLineHeightPadding,
            descriptionText.LineHeight);
        Assert.Equal(TextWrapping.Wrap, descriptionText.TextWrapping);
        TextBlock newVersionText = Assert.Single(
            prompt.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "New version available: 236");
        TextBlock currentVersionText = Assert.Single(
            prompt.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "Current running version: 235");
        TextBlock releasesLink = Assert.Single(
            prompt.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "view releases");
        TextBlock websiteLink = Assert.Single(
            prompt.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "trayapp.net");
        Grid modalDescription = Assert.Single(
            prompt.GetVisualDescendants().OfType<Grid>(),
            grid => grid.Children.Contains(newVersionText) && grid.Children.Contains(currentVersionText));
        Assert.Equal(ConfirmationWindowLayout.ModalDescriptionMargin, modalDescription.Margin);
        Assert.Equal(expected: 3, modalDescription.Children.Count);
        Assert.Equal(expected: 1, Grid.GetRow(newVersionText));
        Assert.Equal(expected: 2, Grid.GetRow(currentVersionText));
        StackPanel modalLinks = Assert.Single(
            prompt.GetVisualDescendants().OfType<StackPanel>(),
            panel => panel.Children.Contains(releasesLink));
        Assert.Equal(Orientation.Vertical, modalLinks.Orientation);
        Assert.Equal(HorizontalAlignment.Right, modalLinks.HorizontalAlignment);
        Assert.Equal(ConfirmationWindowLayout.ModalLinkSpacing, modalLinks.Spacing);
        Assert.Equal(ConfirmationWindowLayout.ModalLinksMargin, modalLinks.Margin);
        Assert.Equal(expected: 0, Grid.GetRow(modalLinks));
        Assert.Equal(expected: 2, modalLinks.Children.Count);
        Assert.Same(websiteLink, modalLinks.Children[0]);
        Assert.Same(releasesLink, modalLinks.Children[1]);
        Assert.DoesNotContain(modalLinks, modalBody.Children);
        Assert.Equal(expected: default, releasesLink.Margin);
        Assert.Equal(expected: default, websiteLink.Margin);
        Assert.True(double.IsNaN(releasesLink.LineHeight));
        Assert.True(double.IsNaN(websiteLink.LineHeight));
        Assert.Equal(HorizontalAlignment.Right, releasesLink.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Right, websiteLink.HorizontalAlignment);
        Assert.Equal(
            TextDecorationLocation.Underline,
            Assert.Single(Assert.IsType<TextDecorationCollection>(releasesLink.TextDecorations)).Location);
        Assert.Equal(
            TextDecorationLocation.Underline,
            Assert.Single(Assert.IsType<TextDecorationCollection>(websiteLink.TextDecorations)).Location);
        Assert.Same(TrayAppDotNETCursors.Hand, releasesLink.Cursor);
        Assert.Same(TrayAppDotNETCursors.Hand, websiteLink.Cursor);
        Assert.True(releasesLink.Focusable);
        Assert.True(websiteLink.Focusable);
        TextBlock restartNotice = Assert.Single(
            prompt.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "Updating will cause app to restart.");
        Assert.Equal(ConfirmationWindowLayout.ModalRestartNoticeMargin, restartNotice.Margin);
        TextBlock titleText = Assert.Single(
            prompt.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "Proceed with update?");
        Assert.Equal(ConfirmationWindowLayout.ModalTitleFontSize, titleText.FontSize);
        Assert.Equal(FontWeight.SemiBold, titleText.FontWeight);
        prompt.Close();
        prompt.Dispose();
        prompt.Dispose();

        Assert.Null(prompt.Content);
    });

    [Fact]
    public void UpdateOwnerBackdropWrapsExactOwnerContentAndRestoresIt() => AvaloniaTestHost.Run(() =>
    {
        CornerRadius ownerCornerRadius = new(8);
        Border originalContent = new() { Background = Brushes.Red, CornerRadius = ownerCornerRadius };
        Window owner = new() { Width = 300, Height = 200, Content = originalContent };
        owner.Show();
        owner.UpdateLayout();
        Color backdropColor = Color.FromArgb(a: 0xA0, r: 0x10, g: 0x20, b: 0x30);

        UpdatePromptOwnerBackdrop ownerBackdrop = Assert.IsType<UpdatePromptOwnerBackdrop>(
            UpdatePromptOwnerBackdrop.Attach(owner, backdropColor));
        Grid overlayHost = Assert.IsType<Grid>(owner.Content);
        Border backdrop = Assert.Single(
            overlayHost.Children.OfType<Border>(),
            child => !ReferenceEquals(child, originalContent));
        owner.UpdateLayout();

        Assert.Same(originalContent, overlayHost.Children[0]);
        SolidColorBrush brush = Assert.IsType<SolidColorBrush>(backdrop.Background);
        Assert.Equal(backdropColor, brush.Color);
        Assert.Equal(originalContent.Bounds.Size, backdrop.Bounds.Size);
        Assert.Equal(ownerCornerRadius, backdrop.CornerRadius);
        Assert.False(backdrop.Focusable);
        Assert.False(backdrop.IsHitTestVisible);
        Assert.Equal(int.MaxValue, backdrop.ZIndex);

        ownerBackdrop.Dispose();
        ownerBackdrop.Dispose();

        Assert.Same(originalContent, owner.Content);
        Assert.Empty(overlayHost.Children);
        owner.Close();
    });

    [Fact]
    public void FlyoutUpdatePromptUsesOwnerUpperThirdAndStaysInWorkArea() => AvaloniaTestHost.Run(() =>
    {
        PixelPoint upperThirdPosition = ConfirmationWindowPositioning.ResolveOwnerPosition(
            new PixelRect(x: 100, y: 120, width: 360, height: 600),
            new PixelSize(width: 320, height: 240),
            new PixelRect(x: 0, y: 0, width: 1_920, height: 1_080),
            ConfirmationWindowLayout.FlyoutVerticalAnchorRatio);
        PixelPoint bottomClampedPosition = ConfirmationWindowPositioning.ResolveOwnerPosition(
            new PixelRect(x: -1_900, y: 850, width: 350, height: 300),
            new PixelSize(width: 320, height: 260),
            new PixelRect(x: -1_920, y: 0, width: 1_920, height: 1_040),
            ConfirmationWindowLayout.FlyoutVerticalAnchorRatio);

        Assert.Equal(new PixelPoint(x: 120, y: 200), upperThirdPosition);
        Assert.Equal(new PixelPoint(x: -1_885, y: 780), bottomClampedPosition);
    });

#if DEBUG
    [Fact]
    public void ConfirmationRebuildsAfterAXAMLResourceReload() => AvaloniaTestHost.Run(() =>
    {
        TrayAppDotNETConfirmationWindow prompt = new(
            title: "Title",
            description: "Description",
            confirmText: "Confirm",
            TestPalettes.Default(),
            rounded: true);

        prompt.Show();
        object? initialContent = prompt.Content;
        Assert.NotNull(initialContent);

        ConfirmationWindowResources.ReloadNow();

        Assert.NotSame(initialContent, prompt.Content);
        prompt.Close();
        prompt.Dispose();
    });
#endif

    [Fact]
    public void UninstallerCloseSeversContentIconAndOptionDelegates() => AvaloniaTestHost.Run(() =>
    {
        (TrayAppDotNETUninstallerWindow window, WeakReference callbackTargetReference) = CreateUninstaller();
        window.Show();
        window.Close();
        window.Dispose();
        window.Dispose();

        Assert.Null(window.Content);
        Assert.Null(window.Icon);
        Collect();
        Assert.False(callbackTargetReference.IsAlive);
        GC.KeepAlive(window);
    });

    [Fact]
    public void InstallerReturnsSelectedLocationAndShortcutOptions() => AvaloniaTestHost.Run(() =>
    {
        string localInstallDirectory = Path.Combine(Path.GetTempPath(), path2: "TrayAppDotNET", path3: "Local");
        string systemInstallDirectory = Path.Combine(Path.GetTempPath(), path2: "TrayAppDotNET", path3: "System");
        TrayAppDotNETInstallerWindow window = new(new TrayAppDotNETInstallerWindowOptions
        {
            Layout = new TrayAppDotNETInstallLayout(
                ApplicationName: "Test",
                SharedRootFolderName: "TrayAppDotNET",
                localInstallDirectory,
                systemInstallDirectory,
                InstalledExecutableFileName: "Test.exe"),
            Icon = null,
            Palette = TestPalettes.Default(),
            EnableRoundedCorners = true
        });

        window.Show();

        Assert.Equal(InstallScope.LocalAppData, window.SelectedScope);
        Assert.Equal(localInstallDirectory, window.SelectedInstallDirectory);
        // Choices left at their presets are unset, so the install leaves those shortcuts as they are
        Assert.Null(window.SelectedInstallOptions.CreateDesktopShortcut);
        Assert.Null(window.SelectedInstallOptions.CreateStartMenuShortcut);
        Assert.Contains(
            window.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == localInstallDirectory);
        Assert.Contains(
            window.GetVisualDescendants().OfType<SettingsButton>(),
            button => button.Text == "Local");
        Assert.Contains(
            window.GetVisualDescendants().OfType<SettingsButton>(),
            button => button.Text == "System");

        SettingsButton systemButton = Assert.Single(
            window.GetVisualDescendants().OfType<SettingsButton>(),
            button => button.Text == "System");
        systemButton.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Equal(InstallScope.ProgramFiles, window.SelectedScope);
        Assert.Equal(systemInstallDirectory, window.SelectedInstallDirectory);
        Assert.Contains(
            window.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == systemInstallDirectory);

        CheckBox desktopShortcut = Assert.Single(
            window.GetVisualDescendants().OfType<CheckBox>(),
            checkBox => checkBox.Content is TextBlock { Text: "Desktop shortcut" });
        CheckBox startMenuShortcut = Assert.Single(
            window.GetVisualDescendants().OfType<CheckBox>(),
            checkBox => checkBox.Content is TextBlock { Text: "Start Menu entry" });
        desktopShortcut.IsChecked = true;
        startMenuShortcut.IsChecked = false;

        SettingsButton installButton = Assert.Single(
            window.GetVisualDescendants().OfType<SettingsButton>(),
            button => button.Text == "Install");
        installButton.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

        TrayAppDotNETInstallerWindowResult result = Assert.IsType<TrayAppDotNETInstallerWindowResult>(window.Result);
        Assert.Equal(InstallScope.ProgramFiles, result.Scope);
        Assert.Equal(systemInstallDirectory, result.InstallDirectory);
        Assert.True(result.InstallOptions.CreateDesktopShortcut);
        Assert.False(result.InstallOptions.CreateStartMenuShortcut);
        window.Dispose();
        window.Dispose();

        Assert.Null(window.Content);
        Assert.Null(window.Icon);
    });

    [Fact]
    public void WarmSlotDetachesAndDisposesAfterCloseFailure() => AvaloniaTestHost.Run(() =>
    {
        List<Exception> errors = [];
        FakeWarmWindow window = new() { ThrowWhenClosing = true, ThrowWhenDisposingResources = true };
        TrayAppDotNETWarmWindowSlot<FakeWarmWindow> slot = new(() => false, errors.Add);
        slot.TakeOrCreate(() => window);

        slot.EvictNow();
        slot.Dispose();
        slot.Dispose();

        Assert.Null(slot.Cached);
        Assert.False(window.IsManagedByWarmSlot);
        Assert.False(window.IsWarmPriming);
        Assert.Equal(expected: 0, window.WarmDismissedSubscriberCount);
        Assert.Equal(expected: 1, window.CloseAttemptCount);
        Assert.Equal(expected: 1, window.DisposeWarmResourcesCount);
        Assert.Equal(expected: 2, errors.Count);
    });

    [Fact]
    public void WarmSlotKeepsAHiddenWindowWhileItOwnsAnUpdateFlow() => AvaloniaTestHost.Run(() =>
    {
        FakeWarmWindow window = new();
        TrayAppDotNETWarmWindowSlot<FakeWarmWindow> slot = new(() => false);
        slot.TakeOrCreate(() => window);
        IDisposable busy = TrayAppDotNETUpdatePromptPresenter.TryBeginBusy(window)
                           ?? throw new InvalidOperationException("The window already owns an update flow.");
        try
        {
            // A confirmed download still running for the hidden window must not lose its owner
            slot.Invalidate();
            Assert.Same(window, slot.Cached);
            Assert.Equal(expected: 0, window.CloseAttemptCount);
            Assert.Null(TrayAppDotNETUpdatePromptPresenter.TryBeginBusy(window));
        }
        finally
        {
            busy.Dispose();
        }

        // Once the flow ends the window evicts as before
        Assert.False(TrayAppDotNETUpdatePromptPresenter.IsBusy(window));
        slot.Invalidate();
        Assert.Null(slot.Cached);
        Assert.Equal(expected: 1, window.CloseAttemptCount);
        slot.Dispose();
    });

    [Fact]
    public void WarmSlotDisposalReleasesScheduledTimerAndWindowSubscription() => AvaloniaTestHost.Run(() =>
    {
        (FakeWarmWindow window, WeakReference slotReference) = CreateDisposedWarmSlot();

        Collect();

        Assert.False(slotReference.IsAlive);
        Assert.Equal(expected: 0, window.WarmDismissedSubscriberCount);
        GC.KeepAlive(window);
    });

    [Fact]
    public void WarmSlotPrimesOnlyTheWindowItCreatesSoRetainedFramesSurviveRepriming() =>
        AvaloniaTestHost.RunAsync(async () =>
        {
            int createCount = 0;
            Func<FakeWarmWindow> createWindow = () =>
            {
                createCount++;
                return new FakeWarmWindow();
            };
            TrayAppDotNETWarmWindowSlot<FakeWarmWindow> slot = new(() => true);

            try
            {
                await slot.PrimeAsync(createWindow);
                FakeWarmWindow window = Assert.IsType<FakeWarmWindow>(slot.Cached);
                await slot.PrimeAsync(createWindow);
                slot.ApplyKeepWarmPolicy(createWindow);
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle);

                Assert.Equal(expected: 1, createCount);
                Assert.Same(window, slot.Cached);
                Assert.Equal(expected: 1, window.WarmPrimingCount);
                Assert.False(window.IsVisible);
            }
            finally
            {
                slot.Dispose();
            }
        });

    [Fact]
    public void CoordinatorDisposeRemovesPendingFlyoutOpenedHandler() => AvaloniaTestHost.Run(() =>
    {
        Window settingsWindow = new();
        TestFlyoutWindow flyout = new();
        SettingsFlyoutKeepOpenCoordinator coordinator = new(
            () => settingsWindow,
            () => flyout);

        settingsWindow.Show();
        flyout.Show();
        coordinator.Attach(settingsWindow);
        coordinator.HoldOpen();
        flyout.Hide();
        coordinator.Release();
        coordinator.Dispose();
        int hidesBeforeShow = flyout.HideCount;

        flyout.Show();

        Assert.True(flyout.IsVisible);
        Assert.Equal(hidesBeforeShow, flyout.HideCount);

        flyout.Close();
        settingsWindow.Close();
    });

    [Fact]
    public void CoordinatorPendingFlyoutOpenedHandlerHidesWithoutCancellation() => AvaloniaTestHost.Run(() =>
    {
        Window settingsWindow = new();
        TestFlyoutWindow flyout = new();
        SettingsFlyoutKeepOpenCoordinator coordinator = new(
            () => settingsWindow,
            () => flyout);

        settingsWindow.Show();
        flyout.Show();
        coordinator.Attach(settingsWindow);
        coordinator.HoldOpen();
        flyout.Hide();
        coordinator.Release();
        int hidesBeforeShow = flyout.HideCount;

        flyout.Show();

        Assert.False(flyout.IsVisible);
        Assert.Equal(hidesBeforeShow + 1, flyout.HideCount);

        coordinator.Dispose();
        flyout.Close();
        settingsWindow.Close();
    });

    [Fact]
    public void EnterOnAWindowOpeningButtonArmsTheNextAutoHideSuppressionLikeAClick() => AvaloniaTestHost.Run(() =>
    {
        WindowOpeningFlyout flyout = new();
        try
        {
            flyout.Show();
            Assert.True(flyout.OpenWindowButton.Focus(NavigationMethod.Tab));

            // A key that does not activate the button leaves the next hide alone
            KeyboardInput.PressKey(flyout, Key.A, PhysicalKey.A);
            flyout.HideFromCoordinator();
            Assert.False(flyout.IsVisible);

            // Enter activates the button, which arms the suppression before the window opens
            flyout.Show();
            Assert.True(flyout.OpenWindowButton.Focus(NavigationMethod.Tab));
            KeyboardInput.PressKey(flyout, Key.Enter, PhysicalKey.Enter);
            Assert.Equal(expected: 1, flyout.OpenCount);
            Assert.True(flyout.SuppressedWhenOpening);

            // No deactivation followed by the time the dispatcher idled, so the next hide is not swallowed
            Dispatcher.UIThread.RunJobs();
            Assert.False(flyout.IsNextAutoHideSuppressed);
            flyout.HideFromCoordinator();
            Assert.False(flyout.IsVisible);
        }
        finally
        {
            flyout.Close();
        }
    });

    [Fact]
    public void APressReleasedOutsideAWindowOpeningButtonLeavesTheNextAutoHideAlone() => AvaloniaTestHost.Run(() =>
    {
        WindowOpeningFlyout flyout = new() { Width = 200, Height = 100 };
        try
        {
            flyout.Show();
            flyout.UpdateLayout();

            // The press arms nothing; only an activation would, and a release outside the button cancels it
            flyout.MouseDown(new Point(x: 10, y: 10), MouseButton.Left);
            flyout.MouseUp(new Point(x: 150, y: 80), MouseButton.Left);
            Assert.Equal(expected: 0, flyout.OpenCount);
            Assert.False(flyout.IsNextAutoHideSuppressed);

            flyout.HideFromCoordinator();
            Assert.False(flyout.IsVisible);
        }
        finally
        {
            flyout.Close();
        }
    });

    [Fact]
    public void HidingAFlyoutClosesItsModalDialog() => AvaloniaTestHost.Run(() =>
    {
        TestFlyoutWindow flyout = new();
        Window dialog = new();
        try
        {
            flyout.Show();
            Task<object?> result = flyout.ShowOwnedDialogAsync<object>(dialog);
            Assert.True(dialog.IsVisible);

            flyout.Hide();

            Assert.False(dialog.IsVisible);
            Dispatcher.UIThread.RunJobs();
            Assert.True(result.IsCompleted);
        }
        finally
        {
            dialog.Close();
            flyout.Close();
        }
    });

    [Fact]
    public void EscapeHidesTheFlyoutThroughItsDismissCommand() => AvaloniaTestHost.Run(() =>
    {
        TestFlyoutWindow flyout = new();
        try
        {
            flyout.Show();
            flyout.Focus();

            KeyboardInput.PressKey(flyout, Key.Escape, PhysicalKey.Escape);

            Assert.False(flyout.IsVisible);
            Assert.Equal(expected: 1, flyout.HideCount);
        }
        finally
        {
            flyout.Close();
        }
    });

    [Theory]
    [InlineData(true, false, false, true, true)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, false, false, true, false)]
    public void FlyoutCompanionFocusGroupRequiresTheWholeGroupToBeInactive(
        bool isFlyoutVisible,
        bool isFlyoutActive,
        bool hasActiveCompanionWindow,
        bool canHideInactiveFocusGroup,
        bool expected)
    {
        bool shouldHide = FlyoutWindowCommon.ShouldHideFocusGroup(
            isFlyoutVisible,
            isFlyoutActive,
            hasActiveCompanionWindow,
            canHideInactiveFocusGroup);

        Assert.Equal(expected, shouldHide);
    }

    [Fact]
    public void FlyoutCompanionWindowsUseTransparentResizableDefaults() => AvaloniaTestHost.Run(() =>
    {
        TestFlyoutCompanionWindow companionWindow = new();

        Assert.Same(Brushes.Transparent, companionWindow.Background);
        Assert.Contains(WindowTransparencyLevel.Transparent, companionWindow.TransparencyLevelHint);
        Assert.True(companionWindow.CanResize);
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (TrayAppDotNETColorPickerWindow Picker, WeakReference ListenerReference) CreateColorPicker()
    {
        ColorChangedListener listener = new();
        TrayAppDotNETColorPickerWindow picker = new(
            title: "Color",
            hasAlpha: true,
            Colors.Blue,
            Colors.Red,
            TestPalettes.Default(),
            new TrayAppDotNETColorPickerStrings(
                DefaultTitle: "Color",
                CloseTooltip: "Close",
                HueLabel: "Hue",
                AlphaLabel: "Alpha",
                RedLabel: "Red",
                GreenLabel: "Green",
                BlueLabel: "Blue",
                RgbaHexLabel: "RGBA",
                ArgbHexLabel: "ARGB",
                DefaultButton: "Default",
                ResetButton: "Reset"));
        picker.ColorChanged += listener.OnColorChanged;
        return (picker, new WeakReference(listener));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (TrayAppDotNETUninstallerWindow Window, WeakReference CallbackTargetReference)
        CreateUninstaller()
    {
        UninstallerCallbackTarget callbackTarget = new();
        TrayAppDotNETUninstallerWindow window = new(new TrayAppDotNETUninstallerWindowOptions
        {
            ApplicationName = "Test",
            InstallDirectory = Environment.CurrentDirectory,
            SettingsDirectory = Environment.CurrentDirectory,
            InstallScope = InstallScope.LocalAppData,
            Icon = null,
            Palette = TestPalettes.Default(),
            EnableRoundedCorners = true,
            L = callbackTarget.L,
            RunUninstall = callbackTarget.RunUninstall
        });
        return (window, new WeakReference(callbackTarget));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (FakeWarmWindow Window, WeakReference SlotReference) CreateDisposedWarmSlot()
    {
        FakeWarmWindow window = new() { ThrowWhenClosing = true };
        TrayAppDotNETWarmWindowSlot<FakeWarmWindow> slot = new(() => false);
        slot.TakeOrCreate(() => window);
        slot.MarkDismissed();
        WeakReference slotReference = new(slot);
        slot.Dispose();
        return (window, slotReference);
    }

    private static void Collect()
    {
        for (int pass = 0; pass < 3; pass++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed class ColorChangedListener
    {
        private Color _lastColor;

        public void OnColorChanged(object? sender, Color color) => _lastColor = color;
    }

    private sealed class UninstallerCallbackTarget
    {
        private int _callCount;

        public string L(string key)
        {
            _callCount++;
            return key;
        }

        public TrayAppDotNETUninstallRun RunUninstall(
            InstallScope installScope,
            bool deleteSettings,
            Action exitCurrentProcess,
            IProgress<TrayAppDotNETInstallProgress>? progress)
        {
            _callCount++;
            return TrayAppDotNETUninstallRun.WithoutProcess;
        }
    }

    private sealed class FlyoutTestControlMap() : TrayAppDotNETCommon.UI.ControlMapping.ControlMap(FlyoutMapName);

    private sealed class TestFlyoutWindow : FlyoutWindowCommon
    {
        public TestFlyoutWindow()
            : base(FlyoutCloseID) =>
            this.MapTo(FlyoutSurfaceID);

        public int HideCount { get; private set; }

        protected override void OnHiding() => HideCount++;

        protected override void RebuildContent()
        {
        }
    }

    private sealed class TestFlyoutCompanionWindow : FlyoutCompanionWindow;

    // A flyout whose only control opens another window, as the header settings buttons do
    private sealed class WindowOpeningFlyout : FlyoutWindowCommon
    {
        public WindowOpeningFlyout()
            : base(FlyoutCloseID)
        {
            this.MapTo(FlyoutSurfaceID);

            OpenWindowButton = TrayAppDotNETFlyoutUI.IconButtonCore(
                new Grid(),
                new FlyoutControlPalette(
                    Colors.White,
                    Colors.Gray,
                    Colors.Gray,
                    Colors.DimGray,
                    Colors.Black,
                    Colors.Black,
                    Colors.Black,
                    Colors.White,
                    Colors.Gray,
                    Colors.Blue,
                    Colors.White),
                OpensWindow(_ =>
                {
                    OpenCount++;
                    SuppressedWhenOpening = IsNextAutoHideSuppressed;
                }),
                width: 20,
                height: 20,
                enabled: true,
                new Thickness(0),
                tooltip: null,
                rightClick: null,
                new CornerRadius(0)).MapTo(FlyoutOpenWindowID);
            OpenWindowButton.Focusable = true;
            OpenWindowButton.HorizontalAlignment = HorizontalAlignment.Left;
            OpenWindowButton.VerticalAlignment = VerticalAlignment.Top;
            Content = OpenWindowButton;
        }

        public Border OpenWindowButton { get; }

        public int OpenCount { get; private set; }

        // Whether the hide the opened window's activation would cause was already suppressed when it opened
        public bool SuppressedWhenOpening { get; private set; }

        protected override void RebuildContent()
        {
        }
    }

    private sealed class FakeWarmWindow : Window, ITrayAppDotNETWarmWindow, ITrayAppDotNETWarmResourceOwner
    {
        private EventHandler? _warmDismissed;
        private bool _isWarmPriming;

        public bool ThrowWhenClosing { get; init; }
        public bool ThrowWhenDisposingResources { get; init; }
        public int CloseAttemptCount { get; private set; }
        public int DisposeWarmResourcesCount { get; private set; }
        public int WarmDismissedSubscriberCount { get; private set; }
        public int WarmPrimingCount { get; private set; }
        public bool IsManagedByWarmSlot { get; set; }

        public bool IsWarmPriming
        {
            get => _isWarmPriming;
            set
            {
                if (value && !_isWarmPriming) WarmPrimingCount++;
                _isWarmPriming = value;
            }
        }

        public event EventHandler? WarmDismissed
        {
            add
            {
                _warmDismissed += value;
                WarmDismissedSubscriberCount++;
            }
            remove
            {
                _warmDismissed -= value;
                WarmDismissedSubscriberCount--;
            }
        }

        public void DismissForWarmCache() => _warmDismissed?.Invoke(this, EventArgs.Empty);

        public void CloseForWarmEviction()
        {
            CloseAttemptCount++;
            if (ThrowWhenClosing)
                throw new InvalidOperationException("Expected close failure");
            Close();
        }

        public void TrimHiddenWarmResources()
        {
        }

        public void DisposeWarmResources()
        {
            DisposeWarmResourcesCount++;
            if (ThrowWhenDisposingResources)
                throw new InvalidOperationException("Expected resource disposal failure");
        }
    }
}
