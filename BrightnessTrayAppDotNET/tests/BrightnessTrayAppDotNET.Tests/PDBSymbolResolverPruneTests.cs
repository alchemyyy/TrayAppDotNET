using BrightnessTrayAppDotNET.Interop.NightLight;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class PDBSymbolResolverPruneTests
{
    [Fact]
    public void OnlyTheCurrentBuildsPDBFolderIsKept()
    {
        using TempDirectory temp = new();
        string root = temp.Path;
        string current = Path.Combine(root, "AAAA1");
        string earlier = Path.Combine(root, "BBBB1");
        Directory.CreateDirectory(current);
        Directory.CreateDirectory(earlier);
        File.WriteAllText(Path.Combine(current, "SettingsHandlers_Display.pdb"), "current");
        File.WriteAllText(Path.Combine(earlier, "SettingsHandlers_Display.pdb"), "earlier");

        PDBSymbolResolver.PruneOtherPDBFolders(root, keepSymbolKey: "aaaa1");

        Assert.True(File.Exists(Path.Combine(current, "SettingsHandlers_Display.pdb")));
        Assert.False(Directory.Exists(earlier));
    }

    [Fact]
    public void MissingRootIsIgnored() =>
        PDBSymbolResolver.PruneOtherPDBFolders(
            Path.Combine(Path.GetTempPath(), "BrightnessTrayAppDotNET.Tests", "PDBPrune", Guid.NewGuid().ToString("N")),
            keepSymbolKey: "AAAA1");
}
