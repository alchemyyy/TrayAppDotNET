using FanControlTrayAppDotNET.UI.Curves;
using FanControlTrayAppDotNET.UI.Flyout;
using TrayAppDotNETCommon.UI;
using Xunit;

namespace FanControlTrayAppDotNET.Tests;

public sealed class FlyoutCompanionWindowTests
{
    [Fact]
    public void EveryFlyoutEditorUsesSharedCompanionBehavior()
    {
        Type[] editorWindowTypes =
        [
            typeof(FanCurveEditorWindow),
            typeof(FanPropertiesWindow),
            typeof(ProbeDataSelectorWindow)
        ];

        foreach (Type editorWindowType in editorWindowTypes)
            Assert.True(typeof(FlyoutCompanionWindow).IsAssignableFrom(editorWindowType));
    }
}
