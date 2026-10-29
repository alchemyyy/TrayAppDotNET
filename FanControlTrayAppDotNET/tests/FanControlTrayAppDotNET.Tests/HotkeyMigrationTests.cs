using FanControlTrayAppDotNET.Models;
using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Models;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class HotkeyMigrationTests
{
    private const uint LegacyModifiers = HotkeyModifiers.Control | HotkeyModifiers.Win | HotkeyModifiers.Alt;
    private const uint VirtualKeyS = 0x53;
    private const uint VirtualKeyF = 0x46;

    /// <summary>
    /// Verifies the retired Ctrl+Alt+Win+S row and its tombstone are removed while other bindings stay.
    /// </summary>
    [Fact]
    public void RetireRemovesSeededRowAndTombstoneOnly()
    {
        AppSettings settings = new()
        {
            Hotkeys =
            [
                LegacyOpenSettings(),
                new HotkeyBinding
                {
                    Action = HotkeyAction.OpenSettings, Modifiers = LegacyModifiers, VirtualKey = VirtualKeyS,
                    BindingID = 0, RemovedByUser = true, Enabled = false
                },
                new HotkeyBinding
                {
                    Action = HotkeyAction.OpenFlyout, Modifiers = LegacyModifiers, VirtualKey = VirtualKeyF
                },
                new HotkeyBinding
                {
                    Action = HotkeyAction.OpenSettings, Modifiers = LegacyModifiers, VirtualKey = VirtualKeyS,
                    BindingID = 1
                }
            ]
        };

        Assert.True(settings.RetireLegacyOpenSettingsHotkey());

        Assert.True(settings.LegacyOpenSettingsHotkeyRetired);
        Assert.Equal(expected: 2, settings.Hotkeys.Count);
        Assert.Contains(settings.Hotkeys, b => b.Matches(HotkeyAction.OpenFlyout, string.Empty, 0));
        Assert.Contains(settings.Hotkeys, b => b.Matches(HotkeyAction.OpenSettings, string.Empty, 1));
    }

    /// <summary>
    /// Verifies a user's rebinding of the Open settings primary row to another key survives.
    /// </summary>
    [Fact]
    public void RetireKeepsRebindingToAnotherKey()
    {
        AppSettings settings = new()
        {
            Hotkeys =
            [
                new HotkeyBinding
                {
                    Action = HotkeyAction.OpenSettings, Modifiers = LegacyModifiers, VirtualKey = VirtualKeyF,
                    BindingID = 0
                }
            ]
        };

        Assert.True(settings.RetireLegacyOpenSettingsHotkey());

        HotkeyBinding kept = Assert.Single(settings.Hotkeys);
        Assert.Equal(VirtualKeyF, kept.VirtualKey);
    }

    /// <summary>
    /// Verifies the migration runs once: a binding re-added after it ran is never removed again.
    /// </summary>
    [Fact]
    public void RetireRunsOnlyOnce()
    {
        AppSettings settings = new() { Hotkeys = [LegacyOpenSettings()] };
        Assert.True(settings.RetireLegacyOpenSettingsHotkey());
        Assert.Empty(settings.Hotkeys);

        settings.Hotkeys.Add(LegacyOpenSettings());

        Assert.False(settings.RetireLegacyOpenSettingsHotkey());
        Assert.Single(settings.Hotkeys);
    }

    /// <summary>
    /// Verifies the load path removes the seeded row and persists the marker, so a later re-add survives
    /// the next load, and that no default binding is seeded back.
    /// </summary>
    [Fact]
    public void LoadRemovesSeededRowOnceAndKeepsLaterReAdd()
    {
        string path = TempSettingsPath();
        try
        {
            AppSettings legacy = new()
            {
                Hotkeys =
                [
                    LegacyOpenSettings(),
                    new HotkeyBinding
                    {
                        Action = HotkeyAction.OpenFlyout, Modifiers = LegacyModifiers, VirtualKey = VirtualKeyF
                    }
                ]
            };
            legacy.Save(path);

            AppSettings migrated = AppSettings.LoadOrDefault(path);
            Assert.True(migrated.LegacyOpenSettingsHotkeyRetired);
            Assert.DoesNotContain(migrated.Hotkeys, b => b.Action == HotkeyAction.OpenSettings);
            Assert.Contains(migrated.Hotkeys, b => b.Action == HotkeyAction.OpenFlyout);

            migrated.Hotkeys.Add(LegacyOpenSettings());
            migrated.Save(path);

            AppSettings reloaded = AppSettings.LoadOrDefault(path);
            Assert.True(reloaded.LegacyOpenSettingsHotkeyRetired);
            Assert.Contains(reloaded.Hotkeys, b =>
                b.Matches(HotkeyAction.OpenSettings, string.Empty, 0)
                && b.Modifiers == LegacyModifiers
                && b.VirtualKey == VirtualKeyS);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Verifies a fresh install ships with no hotkeys and is already marked as migrated.
    /// </summary>
    [Fact]
    public void FreshInstallHasNoHotkeys()
    {
        string path = TempSettingsPath();
        try
        {
            AppSettings fresh = AppSettings.LoadOrDefault(path);

            Assert.Empty(fresh.Hotkeys);
            Assert.True(fresh.LegacyOpenSettingsHotkeyRetired);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static HotkeyBinding LegacyOpenSettings() => new()
    {
        Action = HotkeyAction.OpenSettings,
        Parameter = string.Empty,
        Modifiers = LegacyModifiers,
        VirtualKey = VirtualKeyS,
        Enabled = true,
        BindingID = 0
    };

    private static string TempSettingsPath()
    {
        string directory = Path.Combine(Path.GetTempPath(), "FanControlTrayAppDotNET.Tests", "HotkeyMigration");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"settings-{Guid.NewGuid():N}.xml");
    }
}
