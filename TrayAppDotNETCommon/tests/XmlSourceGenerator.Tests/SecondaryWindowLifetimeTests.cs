using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.Models;
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
            "Title",
            "Description",
            "Changes",
            "Confirm",
            "Cancel",
            Palette(),
            rounded: true);

        prompt.Show();
        prompt.Close();
        prompt.Dispose();
        prompt.Dispose();

        Assert.Null(prompt.Content);
    });

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
            Localize = callbackTarget.Localize,
            RetargetStartupShortcut = callbackTarget.RetargetStartupShortcut,
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

        public string Localize(string key, string fallback)
        {
            _callCount++;
            return fallback;
        }

        public void RetargetStartupShortcut(InstallScope installScope)
        {
            _callCount++;
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
