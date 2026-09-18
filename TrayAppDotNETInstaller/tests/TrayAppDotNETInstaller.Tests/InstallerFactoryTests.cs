using TrayAppDotNETInstaller.Services;
using Xunit;

namespace TrayAppDotNETInstaller.Tests;

public sealed class InstallerFactoryTests : IDisposable
{
    private const string OutputPath = @"C:\out\Installer_VolumeTrayAppDotNET_270.exe";
    private const string VolumePayload = @"C:\packages\VolumeTrayAppDotNET_270.zip";
    private const string BatteryPayload = @"C:\packages\BatteryTrayAppDotNET_31.zip";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "TrayAppDotNETInstaller.Tests",
        Guid.NewGuid().ToString("N"));

    public InstallerFactoryTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Temp leftovers are harmless for the test outcome
        }
    }

    [Fact]
    public void IsFactoryInvocation_MatchesOnlyTheConsoleArguments()
    {
        Assert.True(InstallerFactory.IsFactoryInvocation(["--make-installer", "--output", OutputPath]));
        Assert.True(InstallerFactory.IsFactoryInvocation(["--MAKE-INSTALLER"]));
        // Verification is the second console mode, so it must also keep the process away from WPF
        Assert.True(InstallerFactory.IsFactoryInvocation(["--verify-installer", "--image", OutputPath]));
        Assert.True(InstallerFactory.IsFactoryInvocation(["--VERIFY-INSTALLER"]));
        Assert.False(InstallerFactory.IsFactoryInvocation([]));
        Assert.False(InstallerFactory.IsFactoryInvocation(["--worker", "--pipe", "p"]));
    }

    [Fact]
    public void Run_VerifyInstallerRejectsAnImageThatIsNotThere()
    {
        string missingImage = Path.Combine(_root, "absent.exe");

        Assert.Equal(1, InstallerFactory.Run(["--verify-installer", "--image", missingImage]));
    }

    [Fact]
    public void Run_VerifyInstallerRejectsAnImageArgumentWithNoValue()
    {
        Assert.Equal(1, InstallerFactory.Run(["--verify-installer"]));
    }

    [Fact]
    public void Run_VerifyInstallerRejectsAnImageCarryingNoPayloadArchive()
    {
        string bareImage = Path.Combine(_root, "bare.exe");
        File.WriteAllBytes(bareImage, new byte[4096]);

        Assert.Equal(1, InstallerFactory.Run(["--verify-installer", "--image", bareImage]));
    }

    [Fact]
    public void TryParseArguments_SinglePayload()
    {
        bool parsed = InstallerFactory.TryParseArguments(
            ["--make-installer", "--output", OutputPath, "--payload", VolumePayload],
            out FactoryArguments? arguments,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(arguments);
        Assert.Null(error);
        Assert.Equal(OutputPath, arguments.OutputPath);
        Assert.Equal([VolumePayload], arguments.PayloadPaths);
        Assert.Null(arguments.IconName);
    }

    [Fact]
    public void TryParseArguments_MultiplePayloadsKeepCommandLineOrder()
    {
        bool parsed = InstallerFactory.TryParseArguments(
            ["--make-installer", "--payload", VolumePayload, "--payload", BatteryPayload, "--output", OutputPath],
            out FactoryArguments? arguments,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(arguments);
        Assert.Equal([VolumePayload, BatteryPayload], arguments.PayloadPaths);
    }

    [Fact]
    public void TryParseArguments_PayloadListCombinesWithPayloadArguments()
    {
        string listPath = Path.Combine(_root, "payloads.txt");
        File.WriteAllText(listPath, $"{BatteryPayload}{Environment.NewLine}{Environment.NewLine}  {VolumePayload}  {Environment.NewLine}");

        bool parsed = InstallerFactory.TryParseArguments(
            ["--make-installer", "--output", OutputPath, "--payload", @"C:\packages\FanControlTrayAppDotNET_9.zip", "--payload-list", listPath],
            out FactoryArguments? arguments,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(arguments);
        Assert.Equal(
            [@"C:\packages\FanControlTrayAppDotNET_9.zip", BatteryPayload, VolumePayload],
            arguments.PayloadPaths);
    }

    [Fact]
    public void TryParseArguments_IconOverride()
    {
        bool parsed = InstallerFactory.TryParseArguments(
            ["--make-installer", "--output", OutputPath, "--payload", VolumePayload, "--icon", "TrayAppDotNET"],
            out FactoryArguments? arguments,
            out string? error);

        Assert.True(parsed, error);
        Assert.NotNull(arguments);
        Assert.Equal("TrayAppDotNET", arguments.IconName);
    }

    [Theory]
    [InlineData("--make-installer --payload C:\\packages\\VolumeTrayAppDotNET_270.zip")]
    [InlineData("--make-installer --output C:\\out\\Installer.exe")]
    [InlineData("--make-installer --output")]
    [InlineData("--make-installer --output C:\\out\\Installer.exe --payload")]
    [InlineData("--make-installer --output C:\\out\\Installer.exe --payload C:\\packages\\VolumeTrayAppDotNET_270.zip --icon")]
    [InlineData("--make-installer --output C:\\out\\Installer.exe --payload C:\\packages\\VolumeTrayAppDotNET_270.zip --payload-list")]
    [InlineData("--make-installer --output C:\\out\\Installer.exe --payload C:\\packages\\VolumeTrayAppDotNET_270.zip --unknown")]
    public void TryParseArguments_RejectsIncompleteOrUnknownInput(string commandLine)
    {
        // The .NET Framework has no Split(char, StringSplitOptions) overload, so the separator is an array
        string[] args = commandLine.Split([' '], StringSplitOptions.RemoveEmptyEntries);

        bool parsed = InstallerFactory.TryParseArguments(args, out FactoryArguments? arguments, out string? error);

        Assert.False(parsed);
        Assert.Null(arguments);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryParseArguments_RejectsAMissingPayloadListFile()
    {
        string listPath = Path.Combine(_root, "absent.txt");

        bool parsed = InstallerFactory.TryParseArguments(
            ["--make-installer", "--output", OutputPath, "--payload-list", listPath],
            out FactoryArguments? arguments,
            out string? error);

        Assert.False(parsed);
        Assert.Null(arguments);
        Assert.Contains("payload-list", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParseArguments_RejectsAnEmptyPayloadListFile()
    {
        string listPath = Path.Combine(_root, "empty.txt");
        File.WriteAllText(listPath, Environment.NewLine + "   " + Environment.NewLine);

        bool parsed = InstallerFactory.TryParseArguments(
            ["--make-installer", "--output", OutputPath, "--payload-list", listPath],
            out FactoryArguments? arguments,
            out string? error);

        Assert.False(parsed);
        Assert.Null(arguments);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryResolveIconName_SinglePayloadUsesThatApplication()
    {
        FactoryArguments arguments = new(OutputPath, [VolumePayload], IconName: null);

        bool resolved = InstallerFactory.TryResolveIconName(arguments, out string? iconName, out string? error);

        Assert.True(resolved, error);
        Assert.Equal("VolumeTrayAppDotNET", iconName);
    }

    [Fact]
    public void TryResolveIconName_BundleUsesTheSuiteIcon()
    {
        FactoryArguments arguments = new(OutputPath, [VolumePayload, BatteryPayload], IconName: null);

        bool resolved = InstallerFactory.TryResolveIconName(arguments, out string? iconName, out string? error);

        Assert.True(resolved, error);
        Assert.Equal(InstallerIcons.SuiteIconName, iconName);
    }

    [Fact]
    public void TryResolveIconName_OverrideWinsOverTheSinglePayload()
    {
        FactoryArguments arguments = new(OutputPath, [VolumePayload], "BatteryTrayAppDotNET");

        bool resolved = InstallerFactory.TryResolveIconName(arguments, out string? iconName, out string? error);

        Assert.True(resolved, error);
        Assert.Equal("BatteryTrayAppDotNET", iconName);
    }

    [Fact]
    public void TryResolveIconName_RejectsAPayloadThatIsNotAPackageName()
    {
        FactoryArguments arguments = new(OutputPath, [@"C:\packages\notes.zip"], IconName: null);

        bool resolved = InstallerFactory.TryResolveIconName(arguments, out string? iconName, out string? error);

        Assert.False(resolved);
        Assert.Null(iconName);
        Assert.Contains("notes.zip", error, StringComparison.Ordinal);
    }
}
