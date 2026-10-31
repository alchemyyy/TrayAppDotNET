using System.Runtime.CompilerServices;
using Xunit;

// Headless sessions, the control map catalog and other process-wide state are shared, so tests run one at a time
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TrayAppDotNETCommon.Tests;

/// <summary>Points this test process's settings folder at a throwaway directory before any test runs.</summary>
internal static class TestAssemblySetup
{
    [ModuleInitializer]
    internal static void RedirectSettingsDirectory() => TestSettingsDirectory.Redirect("TrayAppDotNETCommon.Tests");
}
