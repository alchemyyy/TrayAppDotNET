using System.Runtime.CompilerServices;

namespace BrightnessTrayAppDotNET.Tests;

/// <summary>Points this test process's settings folder at a throwaway directory before any test runs.</summary>
internal static class TestAssemblySetup
{
    [ModuleInitializer]
    internal static void RedirectSettingsDirectory() => TestSettingsDirectory.Redirect("BrightnessTrayAppDotNET.Tests");
}
