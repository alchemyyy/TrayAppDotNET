using TrayAppDotNETCommon.Interop;
using TrayAppDotNETCommon.UI.Hotkeys;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class HotkeyKeyNameTests
{
    // A key the resources do not name falls through to the VK format
    private const uint UnnamedVirtualKey = 0x99;

    [Theory]
    [InlineData(0x41u, "A")]
    [InlineData(0x37u, "7")]
    [InlineData(0x70u, "F1")]
    [InlineData(0x87u, "F24")]
    [InlineData(0x61u, "Num 1")]
    [InlineData(0x08u, "Backspace")]
    [InlineData(0x1Bu, "Esc")]
    [InlineData(0x21u, "Page Up")]
    [InlineData(0x2Eu, "Delete")]
    [InlineData(UnnamedVirtualKey, "VK 153")]
    public void KeyNamesComeFromTheSharedResources(uint virtualKey, string expected) =>
        Assert.Equal(expected, TrayAppDotNETHotkeyKeys.KeyName(virtualKey));

    [Fact]
    public void ModifierTextListsModifiersInWindowsOrder() =>
        Assert.Equal(
            expected: "Ctrl + Alt + Shift + Win",
            TrayAppDotNETHotkeyKeys.ModifierText(
                HotkeyModifiers.Win | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Control));
}
