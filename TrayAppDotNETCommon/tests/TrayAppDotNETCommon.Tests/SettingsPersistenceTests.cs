using System.Text;
using System.Xml.Serialization;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.Serialization;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

/// <summary>Settings store, load-result handling and generated-reader regressions (audit 06-1, 06-5, 06-6, 06-8,
/// 06-11, 06-23, 06-26, 06-38).</summary>
public sealed class SettingsPersistenceTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    // Created by the test or the code under test, so a test can also start from a missing folder
    private string TestDirectory => _temp.Combine("data");

    public SettingsPersistenceTests() => Directory.CreateDirectory(TestDirectory);

    private string SettingsPath => Path.Combine(TestDirectory, "settings.xml");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void MissingFileIsCreatedFromDefaults()
    {
        StoreTestSettings settings = StoreTestSettings.Load(SettingsPath);
        settings.Store!.Flush();

        Assert.Equal(DataFileLoadResult.Missing, settings.Store.LoadResult);
        Assert.True(File.Exists(SettingsPath));
        Assert.False(settings.Store.IsSavingDisabled);
    }

    [Fact]
    public void CorruptFileIsMovedToABackupAndNeverOverwritten()
    {
        const string corrupt = "<AppSettings><RunOnStartup>false</RunOnStartup";
        File.WriteAllText(SettingsPath, corrupt);

        StoreTestSettings settings = StoreTestSettings.Load(SettingsPath);
        settings.Store!.Flush();

        Assert.Equal(DataFileLoadResult.Corrupt, settings.Store.LoadResult);
        Assert.True(settings.RunOnStartup);
        string backup = Assert.Single(Directory.GetFiles(TestDirectory, "settings.xml.corrupt-*"));
        Assert.Equal(corrupt, File.ReadAllText(backup));
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void LockedFileRunsOnDefaultsWithSavingDisabled()
    {
        File.WriteAllText(SettingsPath, "<AppSettings><RunOnStartup>false</RunOnStartup></AppSettings>");
        StoreTestSettings settings;
        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            settings = StoreTestSettings.Load(SettingsPath);

        settings.RunOnStartup = false;
        settings.Save();
        settings.SaveOnExit();

        Assert.Equal(DataFileLoadResult.Locked, settings.Store!.LoadResult);
        Assert.True(settings.Store.IsSavingDisabled);
        Assert.Equal(
            expected: "<AppSettings><RunOnStartup>false</RunOnStartup></AppSettings>",
            File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void ReadOrDefaultNeverTouchesTheDisk()
    {
        StoreTestSettings missing = AppSettingsCommon.ReadOrDefault<StoreTestSettings>(SettingsPath);
        File.WriteAllText(SettingsPath, "not xml");
        StoreTestSettings corrupt = AppSettingsCommon.ReadOrDefault<StoreTestSettings>(SettingsPath);

        Assert.Null(missing.Store);
        Assert.Null(corrupt.Store);
        Assert.Equal(expected: "not xml", File.ReadAllText(SettingsPath));
        Assert.Single(Directory.GetFiles(TestDirectory));
    }

    [Fact]
    public void ImplicitSavesHonorAutosaveAndExplicitSavesAlwaysWrite()
    {
        StoreTestSettings settings = StoreTestSettings.Load(SettingsPath);
        settings.Autosave = false;
        settings.Save();
        settings.Store!.Flush();

        settings.RunOnStartup = false;
        settings.SaveOnExit();
        Assert.True(TrayXmlSerializer.ReadFile<StoreTestSettings>(SettingsPath).RunOnStartup);

        settings.Save();
        settings.Store.Flush();
        Assert.False(TrayXmlSerializer.ReadFile<StoreTestSettings>(SettingsPath).RunOnStartup);
    }

    [Fact]
    public void AutosaveOnWritesChangesAtExit()
    {
        StoreTestSettings settings = StoreTestSettings.Load(SettingsPath);
        settings.Store!.Flush();

        settings.RunOnStartup = false;
        settings.SaveOnExit();

        Assert.False(TrayXmlSerializer.ReadFile<StoreTestSettings>(SettingsPath).RunOnStartup);
    }

    [Fact]
    public void RuntimeStateChangesDoNotRaiseChanged()
    {
        StoreTestSettings settings = new();
        int changed = 0;
        settings.Changed += () => changed++;

        settings.DockLeft = 120;
        settings.RunOnStartup = false;

        Assert.Equal(expected: 1, changed);
    }

    [Fact]
    public void MenuFontSizeSubmenuDelayAndUpdateIntervalAreClampedInTheModel()
    {
        StoreTestSettings settings = new() { ContextMenuFontSize = 2 };
        Assert.Equal(AppSettingsCommon.ContextMenuFontSizeMin, settings.ContextMenuFontSize);
        settings.ContextMenuFontSize = 500;
        Assert.Equal(AppSettingsCommon.ContextMenuFontSizeMax, settings.ContextMenuFontSize);

        settings.SubmenuShowDelayMs = int.MaxValue;
        Assert.Equal(TimeConstants.TrayMenuSubmenuShowDelayMaxMs, settings.SubmenuShowDelayMs);
        settings.SubmenuShowDelayMs = -1;
        Assert.Equal(TimeConstants.TrayMenuSubmenuShowDelayMinMs, settings.SubmenuShowDelayMs);

        StoreTestSettings defaults = new();
        settings.UpdateCheckIntervalMs = 0;
        Assert.Equal(defaults.UpdateCheckIntervalMs, settings.UpdateCheckIntervalMs);
        settings.UpdateCheckIntervalMs = int.MaxValue;
        Assert.True(settings.UpdateCheckIntervalMs < int.MaxValue);
        settings.UpdateCheckIntervalMs = 1;
        Assert.True(settings.UpdateCheckIntervalMs > 1);
    }

    [Fact]
    public void HotkeysWithAnUnknownActionAndDuplicatesAreDroppedOnLoad()
    {
        File.WriteAllText(
            SettingsPath,
            """
            <AppSettings>
              <Hotkeys>
                <Binding Action="OpenFlyout" Parameter="" Modifiers="3" VirtualKey="70" BindingID="0" />
                <Binding Action="OpenFlyout" Parameter="" Modifiers="6" VirtualKey="71" BindingID="0" />
                <Binding Action="ActionFromANewerBuild" Parameter="" Modifiers="3" VirtualKey="72" BindingID="0" />
              </Hotkeys>
            </AppSettings>
            """);

        StoreTestSettings settings = StoreTestSettings.Load(SettingsPath);
        settings.Store!.Flush();

        HotkeyBinding kept = Assert.Single(settings.Hotkeys);
        Assert.Equal(HotkeyAction.OpenFlyout, kept.Action);
        Assert.Equal(expected: 70u, kept.VirtualKey);
        Assert.Single(TrayXmlSerializer.ReadFile<StoreTestSettings>(SettingsPath).Hotkeys);
    }

    [Fact]
    public void ADerivedTypeCanRenameAnInheritedElement()
    {
        RenamedAutosaveSettings settings = Read<RenamedAutosaveSettings>(
            "<Renamed><Autosave>false</Autosave><AutosaveSettings>false</AutosaveSettings></Renamed>");

        Assert.False(settings.AppMeaning);
        Assert.False(settings.Autosave);

        string xml = Write(new RenamedAutosaveSettings { AppMeaning = false });
        Assert.Contains(expectedSubstring: "<Autosave>false</Autosave>", xml);
        Assert.Contains(expectedSubstring: "<AutosaveSettings>true</AutosaveSettings>", xml);
    }

    [Fact]
    public void UnparseableOptionalValuesStayUnsetAndUnknownEnumsKeepTheirInitializer()
    {
        GeneratorEdgeDocument document = Read<GeneratorEdgeDocument>(
            """
            <Doc OptionalMode="FromTheFuture" OptionalNumber="twelve">
              <Mode>FromTheFuture</Mode>
            </Doc>
            """);

        Assert.Null(document.OptionalMode);
        Assert.Null(document.OptionalNumber);
        Assert.Equal(GeneratorEdgeMode.Second, document.Mode);
    }

    [Fact]
    public void CharactersXmlCannotCarryAreDroppedSoTheFileStaysWritable()
    {
        string xml = Write(new GeneratorEdgeDocument { Text = "My\u0001 Headphones \uD83C\uDFA7\uD800" });

        Assert.Equal(expected: "My Headphones \uD83C\uDFA7", Read<GeneratorEdgeDocument>(xml).Text);
    }

    [Fact]
    public void InlineListsReplaceTheirInitializer()
    {
        GeneratorEdgeDocument document = Read<GeneratorEdgeDocument>("<Doc><Item>loaded</Item></Doc>");

        Assert.Equal(expected: "loaded", Assert.Single(document.Items));
    }

    private static T Read<T>(string xml)
    {
        using MemoryStream stream = new(Encoding.UTF8.GetBytes(xml), writable: false);
        return TrayXmlSerializer.Read<T>(stream);
    }

    private static string Write(object value)
    {
        using MemoryStream stream = new();
        TrayXmlSerializer.Write(stream, value);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

[XmlRoot("AppSettings")]
public sealed class StoreTestSettings : AppSettingsCommon<HotkeyBinding, HotkeyAction>
{
    public double DockLeft
    {
        get;
        set => SetState(ref field, value);
    }

    public static StoreTestSettings Load(string path) => LoadOrCreate<StoreTestSettings>(path);
}

[XmlRoot("Renamed")]
public sealed class RenamedAutosaveSettings : AppSettingsCommon
{
    [XmlElement("AutosaveSettings")]
    public override bool Autosave
    {
        get => base.Autosave;
        set => base.Autosave = value;
    }

    [XmlElement("Autosave")]
    public bool AppMeaning { get; set; } = true;
}

public enum GeneratorEdgeMode
{
    First,
    Second
}

[XmlRoot("Doc")]
public sealed class GeneratorEdgeDocument
{
    [XmlAttribute]
    public GeneratorEdgeMode? OptionalMode { get; set; }

    [XmlAttribute]
    public int? OptionalNumber { get; set; }

    public GeneratorEdgeMode Mode { get; set; } = GeneratorEdgeMode.Second;

    public string Text { get; set; } = string.Empty;

    [XmlElement("Item")]
    public List<string> Items { get; set; } = ["seed"];
}
