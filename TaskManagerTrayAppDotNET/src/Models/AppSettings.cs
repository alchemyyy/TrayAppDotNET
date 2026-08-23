using System.Xml.Serialization;
using TrayAppDotNETCommon.Serialization;

namespace TaskManagerTrayAppDotNET.Models;

[XmlRoot("AppSettings")]
public sealed class AppSettings : AppSettingsCommon
{
    public const double GridFontSizeDefault = 11.5;
    public const double GridFontSizeMinimum = 8.0;
    public const double GridFontSizeMaximum = 32.0;
    public const int GridRowHeightDefault = 19;
    public const int GridRowHeightMinimum = 14;
    public const int GridRowHeightMaximum = 64;

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

    public double GridFontSize
    {
        get;
        set => SetField(ref field, NormalizeGridFontSize(value));
    } = GridFontSizeDefault;

    public int GridRowHeight
    {
        get;
        set => SetField(ref field, Math.Clamp(value, GridRowHeightMinimum, GridRowHeightMaximum));
    } = GridRowHeightDefault;

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

    private static double NormalizeGridFontSize(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(value, GridFontSizeMinimum, GridFontSizeMaximum)
            : GridFontSizeDefault;
}
