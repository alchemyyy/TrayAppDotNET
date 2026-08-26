using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services.Install;
using TrayAppDotNETCommon.UI;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.UI.WarmWindows;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class SecondaryWindowLifetimeTests
{
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
        TrayAppDotNETUpdateConfirmationWindow prompt = new(
            "Proceed with update?",
            "App: TestApp\nNew version available: 236\nCurrent running version: 235",
            "Install update",
            Palette(),
            rounded: true,
            alternateText: "Skip release",
            cancelText: "Close",
            modalFooterText: "Installation of update will cause app to restart.",
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
        StackPanel actionButtons = Assert.Single(
            prompt.GetVisualDescendants().OfType<StackPanel>(),
            panel => panel.Children.Contains(alternateButton));
        Assert.Equal(Orientation.Horizontal, actionButtons.Orientation);
        Assert.Equal(HorizontalAlignment.Left, actionButtons.HorizontalAlignment);
        Assert.Equal(UpdateConfirmationLayout.ModalActionButtonSpacing, actionButtons.Spacing);
        Assert.Equal(UpdateConfirmationLayout.ModalActionButtonsMargin, actionButtons.Margin);
        Assert.Equal(HorizontalAlignment.Left, alternateButton.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Left, closeButton.HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Left, confirmButton.HorizontalAlignment);
        Assert.Equal(UpdateConfirmationLayout.ActionButtonPadding, alternateButton.Padding);
        Assert.True(
            actionButtons.Children.IndexOf(alternateButton) < actionButtons.Children.IndexOf(closeButton));
        Assert.True(
            actionButtons.Children.IndexOf(closeButton) < actionButtons.Children.IndexOf(confirmButton));
        Assert.Single(
            prompt.GetVisualDescendants().OfType<Grid>(),
            grid => grid.Margin == UpdateConfirmationLayout.ModalBodyMargin);
        TextBlock descriptionText = Assert.Single(
            prompt.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text?.StartsWith("App: TestApp", StringComparison.Ordinal) == true);
        Assert.Equal(SettingsUILayout.DescriptionFontSize, descriptionText.FontSize);
        Assert.Equal(UpdateConfirmationLayout.ModalDescriptionMargin, descriptionText.Margin);
        Assert.Equal(
            SettingsUILayout.DescriptionFontSize + UpdateConfirmationLayout.VersionLineHeightPadding,
            descriptionText.LineHeight);
        Assert.Equal(TextWrapping.Wrap, descriptionText.TextWrapping);
        TextBlock restartNotice = Assert.Single(
            prompt.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "Installation of update will cause app to restart.");
        Assert.Equal(UpdateConfirmationLayout.ModalRestartNoticeMargin, restartNotice.Margin);
        TextBlock titleText = Assert.Single(
            prompt.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text == "Proceed with update?");
        Assert.Equal(UpdateConfirmationLayout.ModalTitleFontSize, titleText.FontSize);
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
        Border originalContent = new()
        {
            Background = Brushes.Red,
            CornerRadius = ownerCornerRadius
        };
        Window owner = new()
        {
            Width = 300,
            Height = 200,
            Content = originalContent
        };
        owner.Show();
        owner.UpdateLayout();
        Color backdropColor = Color.FromArgb(0xA0, 0x10, 0x20, 0x30);

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

#if DEBUG
    [Fact]
    public void ConfirmationRebuildsAfterAXAMLResourceReload() => AvaloniaTestHost.Run(() =>
    {
        TrayAppDotNETUpdateConfirmationWindow prompt = new(
            "Title",
            "Description",
            "Confirm",
            Palette(),
            rounded: true);

        prompt.Show();
        object? initialContent = prompt.Content;
        Assert.NotNull(initialContent);

        UpdateConfirmationWindowResources.ReloadNow();

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
        string localInstallDirectory = Path.Combine(Path.GetTempPath(), "TrayAppDotNET", "Local");
        string systemInstallDirectory = Path.Combine(Path.GetTempPath(), "TrayAppDotNET", "System");
        TrayAppDotNETInstallerWindow window = new(new TrayAppDotNETInstallerWindowOptions
        {
            Layout = new TrayAppDotNETInstallLayout(
                "Test",
                "TrayAppDotNET",
                localInstallDirectory,
                systemInstallDirectory,
                "Test.exe"),
            Icon = null,
            Palette = Palette(),
            EnableRoundedCorners = true
        });

        window.Show();

        Assert.Equal(InstallScope.LocalAppData, window.SelectedScope);
        Assert.Equal(localInstallDirectory, window.SelectedInstallDirectory);
        Assert.False(window.SelectedInstallOptions.CreateDesktopShortcut);
        Assert.True(window.SelectedInstallOptions.CreateStartMenuShortcut);
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
        systemButton.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter
        });
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
        installButton.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter
        });

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
        FakeWarmWindow window = new()
        {
            ThrowWhenClosing = true,
            ThrowWhenDisposingResources = true
        };
        TrayAppDotNETWarmWindowSlot<FakeWarmWindow> slot = new(() => false, errors.Add);
        slot.TakeOrCreate(() => window);

        slot.EvictNow();
        slot.Dispose();
        slot.Dispose();

        Assert.Null(slot.Cached);
        Assert.False(window.IsManagedByWarmSlot);
        Assert.False(window.IsWarmPriming);
        Assert.Equal(0, window.WarmDismissedSubscriberCount);
        Assert.Equal(1, window.CloseAttemptCount);
        Assert.Equal(1, window.DisposeWarmResourcesCount);
        Assert.Equal(2, errors.Count);
    });

    [Fact]
    public void WarmSlotDisposalReleasesScheduledTimerAndWindowSubscription() => AvaloniaTestHost.Run(() =>
    {
        (FakeWarmWindow window, WeakReference slotReference) = CreateDisposedWarmSlot();

        Collect();

        Assert.False(slotReference.IsAlive);
        Assert.Equal(0, window.WarmDismissedSubscriberCount);
        GC.KeepAlive(window);
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

        flyout.Show();

        Assert.True(flyout.IsVisible);
        Assert.Equal(0, flyout.HideFromCoordinatorCount);

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

        flyout.Show();

        Assert.False(flyout.IsVisible);
        Assert.Equal(1, flyout.HideFromCoordinatorCount);

        coordinator.Dispose();
        flyout.Close();
        settingsWindow.Close();
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (TrayAppDotNETColorPickerWindow Picker, WeakReference ListenerReference) CreateColorPicker()
    {
        ColorChangedListener listener = new();
        TrayAppDotNETColorPickerWindow picker = new(
            "Color",
            hasAlpha: true,
            Colors.Blue,
            Colors.Red,
            Palette(),
            new TrayAppDotNETColorPickerStrings(
                "Color",
                "Close",
                "Hue",
                "Alpha",
                "Red",
                "Green",
                "Blue",
                "RGBA",
                "ARGB",
                "Default",
                "Reset"));
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
            Palette = Palette(),
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

    private sealed class ColorChangedListener
    {
        private Color _lastColor;

        public void OnColorChanged(object? sender, Color color)
        {
            _lastColor = color;
        }
    }

    private sealed class UninstallerCallbackTarget
    {
        private int _callCount;

        public string L(string key)
        {
            _callCount++;
            return key;
        }

        public Process? RunUninstall(InstallScope installScope, bool deleteSettings)
        {
            _callCount++;
            return null;
        }
    }

    private sealed class TestFlyoutWindow : FlyoutWindowCommon
    {
        public int HideFromCoordinatorCount { get; private set; }

        protected override void HideFlyout()
        {
            HideFromCoordinatorCount++;
            Hide();
        }
    }

    private sealed class FakeWarmWindow : Window, ITrayAppDotNETWarmWindow, ITrayAppDotNETWarmResourceOwner
    {
        private EventHandler? _warmDismissed;

        public bool ThrowWhenClosing { get; init; }
        public bool ThrowWhenDisposingResources { get; init; }
        public int CloseAttemptCount { get; private set; }
        public int DisposeWarmResourcesCount { get; private set; }
        public int WarmDismissedSubscriberCount { get; private set; }
        public bool IsWarmPriming { get; set; }
        public bool IsManagedByWarmSlot { get; set; }

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
