using System.Xml.Serialization;
using TrayAppDotNETCommon.Serialization;

namespace TaskManagerTrayAppDotNET.Models;

[XmlRoot("AppSettings")]
public sealed class AppSettings : AppSettingsCommon
{
    private static readonly AsyncThrottler<AppSettings> SaveThrottle = new(
        TimeConstants.SettingsSaveDebounceMs,
        drainPollIntervalMs: TimeConstants.DrainPollIntervalMs);

    public AppSettings()
        : base(
            TimeConstants.UpdateCheckIntervalDefaultMs,
            TrayAppDotNETRenderingBackend.GPUPreferred)
    {
        SuppressChangeNotification = true;
        UseWindows11SettingsNavigation = true;
        SuppressChangeNotification = false;
    }

    public bool EnableLiveDetailsColumnResizing
    {
        get;
        set => SetField(ref field, value);
    } = true;

    [XmlArray("DetailsColumns")]
    [XmlArrayItem("Column")]
    public List<ProcessColumnSetting> DetailsColumns
    {
        get;
        set => SetField(ref field, ProcessColumnSettings.Normalize(value));
    } = ProcessColumnSettings.CreateDefault();

    public override void OnTrayXmlDeserialized()
    {
        DetailsColumns = ProcessColumnSettings.Normalize(DetailsColumns);
        base.OnTrayXmlDeserialized();
    }

    /// <summary>Persists an already-applied width or order change without rebuilding the app shell.</summary>
    internal void UpdateDetailsColumnLayout(List<ProcessColumnSetting> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        bool wasSuppressed = SuppressChangeNotification;
        SuppressChangeNotification = true;
        try
        {
            DetailsColumns = columns;
        }
        finally
        {
            SuppressChangeNotification = wasSuppressed;
        }

        if (!wasSuppressed) RequestSave();
    }

    protected override void RequestSave()
    {
        if (!Autosave) return;

        AppSettings settings = this;
        _ = SaveThrottle.RunAsync(settings, _ =>
        {
            settings.Save();
            return Task.CompletedTask;
        });
    }

    public static string GetDefaultPath()
    {
        string appDirectory = GetDefaultDirectory();
        Directory.CreateDirectory(appDirectory);
        return Path.Combine(appDirectory, "settings.xml");
    }

    public static string GetDefaultDirectory() => Program.AppLocalAppDataDirectory;

    public void Save() => Save(GetDefaultPath());

    public void Save(string path) =>
        TrayXmlSerializer.TryWriteFile(
            path,
            this,
            exception => TADNLog.Log($"AppSettings.Save: {exception.Message}"));

    public static AppSettings LoadOrDefault(string path) =>
        TrayXmlSerializer.LoadFileOrDefault(
            path,
            static () => new AppSettings(),
            exception => TADNLog.Log($"AppSettings.LoadOrDefault: {exception.Message}"));
}
