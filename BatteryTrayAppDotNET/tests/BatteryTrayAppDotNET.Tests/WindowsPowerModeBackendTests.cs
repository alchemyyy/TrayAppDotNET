using BatteryTrayAppDotNET.Models;
using BatteryTrayAppDotNET.Services;
using Xunit;

namespace BatteryTrayAppDotNET.Tests;

public sealed class WindowsPowerModeBackendTests
{
    private static readonly Guid UltimateTemplate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    private static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    private static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid DuplicatedUltimate = new("0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d");

    private const string GermanUltimateName = "Ultimative Leistung";

    [Fact]
    public void DuplicatedPlanIsRecognizedByItsLocalizedTemplateName()
    {
        BatteryPowerMode? mode = WindowsPowerModeBackend.ClassifyScheme(
            DuplicatedUltimate,
            GermanUltimateName,
            createdScheme: null,
            localizedUltimateName: GermanUltimateName);

        Assert.Equal(BatteryPowerMode.Ultimate, mode);
    }

    [Fact]
    public void PlanThisProcessCreatedIsUltimateEvenAfterARename()
    {
        BatteryPowerMode? mode = WindowsPowerModeBackend.ClassifyScheme(
            DuplicatedUltimate,
            "My plan",
            createdScheme: DuplicatedUltimate,
            localizedUltimateName: GermanUltimateName);

        Assert.Equal(BatteryPowerMode.Ultimate, mode);
    }

    [Fact]
    public void BuiltInPlansAreRecognizedByGuidInAnyLanguage()
    {
        Assert.Equal(
            BatteryPowerMode.Balanced,
            WindowsPowerModeBackend.ClassifyScheme(Balanced, "Ausbalanciert", null, GermanUltimateName));
        Assert.Equal(
            BatteryPowerMode.PowerSaver,
            WindowsPowerModeBackend.ClassifyScheme(PowerSaver, "Energiesparmodus", null, GermanUltimateName));
        Assert.Null(WindowsPowerModeBackend.ClassifyScheme(HighPerformance, "Höchstleistung", null, GermanUltimateName));
    }

    [Fact]
    public void ExistingLocalizedCopyIsReusedInsteadOfDuplicatingAgain()
    {
        Guid? selected = WindowsPowerModeBackend.SelectUltimateScheme(
            [(Balanced, "Ausbalanciert"), (DuplicatedUltimate, GermanUltimateName), (PowerSaver, "Energiesparmodus")],
            createdScheme: null,
            localizedUltimateName: GermanUltimateName);

        Assert.Equal(DuplicatedUltimate, selected);
    }

    [Fact]
    public void TemplateOrCreatedPlanWinsOverANameMatch()
    {
        Guid created = Guid.NewGuid();

        Guid? selected = WindowsPowerModeBackend.SelectUltimateScheme(
            [(DuplicatedUltimate, GermanUltimateName), (created, "Renamed")],
            createdScheme: created,
            localizedUltimateName: GermanUltimateName);

        Assert.Equal(created, selected);
        Assert.Equal(
            UltimateTemplate,
            WindowsPowerModeBackend.SelectUltimateScheme(
                [(DuplicatedUltimate, GermanUltimateName), (UltimateTemplate, GermanUltimateName)],
                createdScheme: null,
                localizedUltimateName: GermanUltimateName));
    }

    [Fact]
    public void NoUltimatePlanSelectsNothing()
    {
        Assert.Null(WindowsPowerModeBackend.SelectUltimateScheme(
            [(Balanced, "Balanced"), (HighPerformance, "High performance")],
            createdScheme: null,
            localizedUltimateName: null));
    }

    /// <summary>Read-only checks of the powrprof calls on this machine; nothing switches or writes a plan.</summary>
    [Fact]
    public async Task ActivePlanAndEnergySaverCanBeReadOnThisMachine()
    {
        BatteryPowerModeReadResult result = await WindowsPowerModeBackend.ReadAsync(CancellationToken.None);
        Assert.True(result.Success);

        _ = await WindowsPowerModeBackend.ReadEnergySaverAsync(CancellationToken.None);
    }

    [Fact]
    public void InstalledPlansAndTheirNamesCanBeEnumerated()
    {
        List<Guid> schemes = WindowsPowerModeBackend.EnumerateSchemes();

        Assert.Contains(Balanced, schemes);
        Assert.False(string.IsNullOrWhiteSpace(WindowsPowerModeBackend.ReadFriendlyName(Balanced)));
        _ = WindowsPowerModeBackend.FindUltimateScheme();
    }
}
