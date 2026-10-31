using BrightnessTrayAppDotNET.Models;
using BrightnessTrayAppDotNET.Utils;
using Xunit;

namespace BrightnessTrayAppDotNET.Tests;

public sealed class KnownDisplaysStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    // Created by the test or the code under test, so a test can also start from a missing folder
    private string TestDirectory => _temp.Combine("data");

    private string StorePath => Path.Combine(TestDirectory, "displays.json");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void StateLearnedDuringTheSessionSurvivesAnOrderlyExit()
    {
        using (KnownDisplaysStore store = new(StorePath))
        {
            store.Load();
            store.RegisterMany([new KnownDisplayEntry { EDIDKey = "DEL-1", OriginalName = "Dell" }]);
            store.MarkDDCCapable("DEL-1");
            store.StampLastBusBrightness("DEL-1", value: 42);
        }

        using KnownDisplaysStore reloaded = new(StorePath);
        reloaded.Load();
        KnownDisplayEntry entry = Assert.Single(reloaded.Entries);
        Assert.True(entry.WasEverDDCCapable);
        Assert.Equal(expected: 42, entry.LastBusBrightness);
    }

    [Fact]
    public void AnUnchangedStoreNeverWrites()
    {
        Directory.CreateDirectory(TestDirectory);
        using (KnownDisplaysStore store = new(StorePath))
        {
            store.Load();
            store.Save();
        }

        Assert.False(File.Exists(StorePath));
    }

    [Fact]
    public void ClearForgetsEveryDisplayOnDisk()
    {
        using (KnownDisplaysStore store = new(StorePath))
        {
            store.Load([new KnownDisplayEntry { EDIDKey = "DEL-1", WasEverDDCCapable = true }]);
            store.Clear();
        }

        using KnownDisplaysStore reloaded = new(StorePath);
        reloaded.Load();
        Assert.Empty(reloaded.Entries);
    }

    [Fact]
    public void ACorruptFileIsKeptAsABackupInsteadOfBeingOverwritten()
    {
        Directory.CreateDirectory(TestDirectory);
        File.WriteAllText(StorePath, "{ not json");

        using (KnownDisplaysStore store = new(StorePath))
        {
            Assert.True(store.Load([new KnownDisplayEntry { EDIDKey = "LEGACY" }]));
        }

        string backup = Assert.Single(Directory.GetFiles(TestDirectory, "displays.json.corrupt-*"));
        Assert.Equal(expected: "{ not json", File.ReadAllText(backup));
        using KnownDisplaysStore reloaded = new(StorePath);
        reloaded.Load();
        Assert.Equal(expected: "LEGACY", Assert.Single(reloaded.Entries).EDIDKey);
    }

    [Fact]
    public void ALockedFileKeepsTheLegacyListAsTheOnlyCopy()
    {
        Directory.CreateDirectory(TestDirectory);
        File.WriteAllText(StorePath, "[]");

        using FileStream locked = new(StorePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using KnownDisplaysStore store = new(StorePath);

        Assert.False(store.Load([new KnownDisplayEntry { EDIDKey = "LEGACY", WasEverDDCCapable = true }]));
        Assert.False(store.Load());
    }

    [Fact]
    public void AFailedMigrationWriteKeepsTheLegacyList()
    {
        // A file where the store's folder should be makes every write fail
        File.WriteAllText(TestDirectory, "not a folder");
        try
        {
            using KnownDisplaysStore store = new(StorePath);

            Assert.False(store.Load([new KnownDisplayEntry { EDIDKey = "LEGACY", WasEverDDCCapable = true }]));
        }
        finally
        {
            File.Delete(TestDirectory);
        }
    }

    [Fact]
    public void ALoadedFileAndASuccessfulMigrationLetTheLegacyListGo()
    {
        using (KnownDisplaysStore migrating = new(StorePath))
            Assert.True(migrating.Load([new KnownDisplayEntry { EDIDKey = "LEGACY" }]));

        using KnownDisplaysStore reloaded = new(StorePath);
        Assert.True(reloaded.Load([new KnownDisplayEntry { EDIDKey = "LEGACY" }]));
        Assert.True(reloaded.Load());
    }
}
