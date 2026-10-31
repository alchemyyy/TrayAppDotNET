using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Services;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class GlobalHotkeyServiceTests
{
    // Combinations no app or Windows binds: Ctrl+Alt+Shift+Win with F22 and F23
    private const uint AllModifiers =
        HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift | HotkeyModifiers.Win;

    private const uint VirtualKeyF22 = 0x85;
    private const uint VirtualKeyF23 = 0x86;

    [Fact]
    public void SecondBindingOfOneCombinationReportsTheBindingThatKeepsIt() => AvaloniaTestHost.Run(() =>
    {
        using GlobalHotkeyService<HotkeyAction, HotkeyBinding> service = Create();
        HotkeyBinding first = Binding(HotkeyAction.OpenFlyout, VirtualKeyF22);
        HotkeyBinding second = Binding(HotkeyAction.OpenSettings, VirtualKeyF22);

        HotkeyApplyResult<HotkeyBinding> result = service.Apply([first, second]);

        Assert.Same(first, Assert.Single(result.Registered));
        HotkeyRegistrationFailure<HotkeyBinding> failure = result.Failed[second];
        Assert.Equal(HotkeyRegistrationError.DuplicateInApp, failure.Error);
        Assert.Same(first, failure.ConflictingBinding);
        Assert.Same(result, service.LastResult);
    });

    [Fact]
    public void ApplyKeepsRegistrationsOfUnchangedCombinations() => AvaloniaTestHost.Run(() =>
    {
        using GlobalHotkeyService<HotkeyAction, HotkeyBinding> service = Create();
        HotkeyBinding kept = Binding(HotkeyAction.OpenFlyout, VirtualKeyF22);
        HotkeyBinding changed = Binding(HotkeyAction.OpenSettings, VirtualKeyF23);
        service.Apply([kept, changed]);
        int? keptID = service.RegistrationIDOf(kept);
        Assert.NotNull(keptID);

        changed.Enabled = false;
        service.Apply([kept, changed]);

        Assert.Equal(keptID, service.RegistrationIDOf(kept));
        Assert.Null(service.RegistrationIDOf(changed));
    });

    [Fact]
    public void IDsStartOverOnceNothingIsRegistered() => AvaloniaTestHost.Run(() =>
    {
        using GlobalHotkeyService<HotkeyAction, HotkeyBinding> service = Create();
        HotkeyBinding binding = Binding(HotkeyAction.OpenFlyout, VirtualKeyF22);
        service.Apply([binding]);
        int? firstID = service.RegistrationIDOf(binding);

        service.Apply([]);
        service.Apply([binding]);

        Assert.Equal(firstID, service.RegistrationIDOf(binding));
    });

    [Fact]
    public void InvalidBindingsAreReportedWithoutRegistering() => AvaloniaTestHost.Run(() =>
    {
        using GlobalHotkeyService<HotkeyAction, HotkeyBinding> service = Create();
        HotkeyBinding reserved = new()
        {
            Action = HotkeyAction.OpenFlyout,
            Modifiers = HotkeyModifiers.Control,
            VirtualKey = HotkeyVirtualKeys.DebuggerReserved
        };

        HotkeyApplyResult<HotkeyBinding> result = service.Apply([reserved]);

        Assert.Empty(result.Registered);
        Assert.Equal(HotkeyRegistrationError.ReservedKey, result.Failed[reserved].Error);
    });

    private static GlobalHotkeyService<HotkeyAction, HotkeyBinding> Create()
    {
        GlobalHotkeyService<HotkeyAction, HotkeyBinding> service = new("TrayAppDotNETCommon.Tests.Hotkeys");
        service.Initialize();
        return service;
    }

    private static HotkeyBinding Binding(HotkeyAction action, uint virtualKey) => new()
    {
        Action = action,
        Modifiers = AllModifiers,
        VirtualKey = virtualKey
    };
}
