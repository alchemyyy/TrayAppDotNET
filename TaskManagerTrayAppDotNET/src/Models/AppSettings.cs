using System.Xml.Serialization;
using TrayAppDotNETCommon.Serialization;

namespace TaskManagerTrayAppDotNET.Models;

public enum TrayGraphStyle
{
    Current,
    Marquee
}

public enum TrayGraphDataSource
{
    CPUAverage,
    CPUHighestCore,
    Memory
}

public enum CPUPerformanceGraphView
{
    LogicalProcessors,
    OverallUtilization,
    DetailedView
}

public enum DetailsGridFontWeight
{
    Thin = 100,
    ExtraLight = 200,
    Light = 300,
    SemiLight = 350,
    Normal = 400,
    Medium = 500,
    SemiBold = 600,
    Bold = 700,
    ExtraBold = 800,
    Black = 900
}

[XmlRoot("AppSettings")]
public sealed class AppSettings : AppSettingsCommon
{
    public const double GridFontSizeDefault = 11.5;
    public const double GridFontSizeMinimum = 8.0;
    public const double GridFontSizeMaximum = 32.0;
    public const double GridRowSpacingDefault = 3.5;
    public const double GridRowSpacingMinimum = 0;
    public const double GridRowSpacingMaximum = 32;
    public const int GridRowHeightDefault = 19;
    public const int GridRowHeightMinimum = 14;
    public const int GridRowHeightMaximum = 64;

    private const double LegacyGridRowTextHeightMultiplier = 1.35;

    private static readonly AsyncThrottler<AppSettings> SaveThrottle = new(
        TimeConstants.SettingsSaveDebounceMs,
        drainPollIntervalMs: TimeConstants.DrainPollIntervalMs);

    private double _gridRowSpacing = GridRowSpacingDefault;
    private bool _gridRowSpacingWasDeserialized;

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

    public bool CollapseSidebarWhenNarrow
    {
        get;
        set => SetField(ref field, value);
    }

    public bool LeftAlignProcessSearchBar
    {
        get;
        set => SetField(ref field, value);
    }

    public bool GroupProcesses
    {
        get;
        set => SetField(ref field, value);
    }

    public bool AlwaysOnTop
    {
        get;
        set => SetField(ref field, value);
    }

    public bool CloseToTray
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool MinimizeToTray
    {
        get;
        set => SetField(ref field, value);
    }

    public TrayGraphStyle TrayGraphStyle
    {
        get;
        set => SetField(ref field, NormalizeTrayGraphStyle(value));
    } = TrayGraphStyle.Marquee;

    public TrayGraphDataSource TrayGraphDataSource
    {
        get;
        set => SetField(ref field, NormalizeTrayGraphDataSource(value));
    } = TrayGraphDataSource.CPUAverage;

    public double GridFontSize
    {
        get;
        set => SetField(ref field, NormalizeGridFontSize(value));
    } = GridFontSizeDefault;

    public DetailsGridFontWeight GridFontWeight
    {
        get;
        set => SetField(ref field, NormalizeGridFontWeight(value));
    } = DetailsGridFontWeight.Normal;

    /// <summary>Retains absolute row height for compatibility with older settings files.</summary>
    public int GridRowHeight
    {
        get;
        set => SetField(ref field, Math.Clamp(value, GridRowHeightMinimum, GridRowHeightMaximum));
    } = GridRowHeightDefault;

    /// <summary>Gets the visible gap added to the measured process-row text height.</summary>
    public double GridRowSpacing
    {
        get => _gridRowSpacing;
        set
        {
            _gridRowSpacingWasDeserialized = true;
            SetField(ref _gridRowSpacing, NormalizeGridRowSpacing(value));
        }
    }

    public int PerformanceHistoryLengthMinutes
    {
        get;
        set => SetField(
            ref field,
            PerformanceSamplingSettings.NormalizeHistoryLengthMinutes(value));
    } = PerformanceSamplingSettings.DefaultHistoryLengthMinutes;

    public int PerformanceSampleIntervalMilliseconds
    {
        get;
        set => SetField(
            ref field,
            PerformanceSamplingSettings.NormalizeSampleIntervalMilliseconds(value));
    } = PerformanceSamplingSettings.DefaultSampleIntervalMilliseconds;

    public bool ShowCPUHighestCoreTrace
    {
        get;
        set => SetField(ref field, value);
    }

    public CPUPerformanceGraphView CPUPerformanceGraphView
    {
        get;
        set => SetField(ref field, NormalizeCPUPerformanceGraphView(value));
    } = CPUPerformanceGraphView.LogicalProcessors;

    public bool ShowMemoryModuleSerialNumbers
    {
        get;
        set => SetField(ref field, value);
    }

    [XmlArray("ProcessHeaderButtonOrder")]
    [XmlArrayItem("Button")]
    public List<ProcessHeaderButtonKind> ProcessHeaderButtonOrder
    {
        get;
        set => SetField(ref field, ProcessHeaderButtonSettings.Normalize(value));
    } = ProcessHeaderButtonSettings.CreateDefault();

    [XmlArray("DetailsColumns")]
    [XmlArrayItem("Column")]
    public List<ProcessColumnSetting> DetailsColumns
    {
        get;
        set => SetField(ref field, ProcessColumnSettings.Normalize(value));
    } = ProcessColumnSettings.CreateDefault();

    [XmlArray("PerformanceDevicePriority")]
    [XmlArrayItem("Kind")]
    public List<PerformanceDeviceKind> PerformanceDevicePriority
    {
        get;
        set => SetField(ref field, PerformanceDeviceOrdering.NormalizePriority(value));
    } = PerformanceDeviceOrdering.CreateDefaultPriority();

    [XmlArray("PerformanceDeviceOrder")]
    [XmlArrayItem("DeviceID")]
    public List<string> PerformanceDeviceOrder
    {
        get;
        set => SetField(ref field, PerformanceDeviceOrdering.NormalizeExplicitOrder(value));
    } = [];

    [XmlArray("PerformanceHardwareNameReplacementRules")]
    [XmlArrayItem("Rule")]
    public List<PerformanceHardwareNameReplacementRule> PerformanceHardwareNameReplacementRules
    {
        get;
        set => SetField(ref field, PerformanceHardwareNameReplacementRuleCollection.Normalize(value));
    } = [];

    public override void OnTrayXmlDeserializing()
    {
        _gridRowSpacingWasDeserialized = false;
        base.OnTrayXmlDeserializing();
    }

    public override void OnTrayXmlDeserialized()
    {
        if (!_gridRowSpacingWasDeserialized)
        {
            GridRowSpacing = GridRowHeight
                             - GridFontSize * LegacyGridRowTextHeightMultiplier;
        }
        PerformanceHistoryLengthMinutes =
            PerformanceSamplingSettings.NormalizeHistoryLengthMinutes(
                PerformanceHistoryLengthMinutes);
        PerformanceSampleIntervalMilliseconds =
            PerformanceSamplingSettings.NormalizeSampleIntervalMilliseconds(
                PerformanceSampleIntervalMilliseconds);
        ProcessHeaderButtonOrder = ProcessHeaderButtonSettings.Normalize(ProcessHeaderButtonOrder);
        DetailsColumns = ProcessColumnSettings.Normalize(DetailsColumns);
        PerformanceDevicePriority = PerformanceDeviceOrdering.NormalizePriority(PerformanceDevicePriority);
        PerformanceDeviceOrder = PerformanceDeviceOrdering.NormalizeExplicitOrder(PerformanceDeviceOrder);
        PerformanceHardwareNameReplacementRules =
            PerformanceHardwareNameReplacementRuleCollection.Normalize(
                PerformanceHardwareNameReplacementRules);
        base.OnTrayXmlDeserialized();
    }

    /// <summary>Persists an already-applied header-button order without rebuilding the app shell.</summary>
    internal void UpdateProcessHeaderButtonOrder(IReadOnlyList<ProcessHeaderButtonKind> buttonOrder)
    {
        ArgumentNullException.ThrowIfNull(buttonOrder);
        List<ProcessHeaderButtonKind> normalized = ProcessHeaderButtonSettings.Normalize(buttonOrder);
        if (ProcessHeaderButtonOrder.SequenceEqual(normalized)) return;

        bool wasSuppressed = SuppressChangeNotification;
        SuppressChangeNotification = true;
        try
        {
            ProcessHeaderButtonOrder = normalized;
        }
        finally
        {
            SuppressChangeNotification = wasSuppressed;
        }

        if (!wasSuppressed) RequestSave();
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

    /// <summary>Persists an already-applied Performance device reorder without rebuilding the app shell.</summary>
    internal void UpdatePerformanceDeviceOrder(List<string> deviceIDs)
    {
        ArgumentNullException.ThrowIfNull(deviceIDs);

        bool wasSuppressed = SuppressChangeNotification;
        SuppressChangeNotification = true;
        try
        {
            PerformanceDeviceOrder = deviceIDs;
        }
        finally
        {
            SuppressChangeNotification = wasSuppressed;
        }

        if (!wasSuppressed) RequestSave();
    }

    /// <summary>Persists the live CPU graph view without rebuilding the application shell.</summary>
    internal void UpdateCPUPerformanceGraphView(CPUPerformanceGraphView graphView)
    {
        CPUPerformanceGraphView normalized = NormalizeCPUPerformanceGraphView(graphView);
        if (CPUPerformanceGraphView == normalized) return;

        bool wasSuppressed = SuppressChangeNotification;
        SuppressChangeNotification = true;
        try
        {
            CPUPerformanceGraphView = normalized;
        }
        finally
        {
            SuppressChangeNotification = wasSuppressed;
        }

        if (!wasSuppressed) RequestSave();
    }

    /// <summary>Persists live Performance hardware-name rules without rebuilding the app shell.</summary>
    internal void UpdatePerformanceHardwareNameReplacementRules(
        List<PerformanceHardwareNameReplacementRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        bool wasSuppressed = SuppressChangeNotification;
        SuppressChangeNotification = true;
        try
        {
            PerformanceHardwareNameReplacementRules = rules;
        }
        finally
        {
            SuppressChangeNotification = wasSuppressed;
        }

        if (!wasSuppressed) RequestSave();
    }

    /// <summary>Persists an already-applied grouping change without rebuilding the app shell.</summary>
    internal void UpdateGroupProcesses(bool groupProcesses)
    {
        bool wasSuppressed = SuppressChangeNotification;
        SuppressChangeNotification = true;
        try
        {
            GroupProcesses = groupProcesses;
        }
        finally
        {
            SuppressChangeNotification = wasSuppressed;
        }

        if (!wasSuppressed) RequestSave();
    }

    /// <summary>Persists already-applied grid typography without rebuilding the app shell.</summary>
    internal void UpdateGridMetrics(double fontSize, double rowHeight, double rowSpacing)
    {
        bool wasSuppressed = SuppressChangeNotification;
        SuppressChangeNotification = true;
        try
        {
            GridFontSize = fontSize;
            GridRowHeight = (int)Math.Round(rowHeight, MidpointRounding.AwayFromZero);
            GridRowSpacing = rowSpacing;
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
        if (!CanAutosaveToDefaultPath(AppServices.Settings)) return;

        AppSettings settings = this;
        _ = SaveThrottle.RunAsync(settings, _ =>
        {
            settings.Save();
            return Task.CompletedTask;
        });
    }

    /// <summary>Restricts implicit default-path writes to the application's live settings instance.</summary>
    internal bool CanAutosaveToDefaultPath(AppSettings? activeSettings) =>
        Autosave && ReferenceEquals(this, activeSettings);

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

    private static double NormalizeGridRowSpacing(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(value, GridRowSpacingMinimum, GridRowSpacingMaximum)
            : GridRowSpacingDefault;

    private static DetailsGridFontWeight NormalizeGridFontWeight(DetailsGridFontWeight value) =>
        Enum.IsDefined(value) ? value : DetailsGridFontWeight.Normal;

    private static TrayGraphStyle NormalizeTrayGraphStyle(TrayGraphStyle value) =>
        Enum.IsDefined(value) ? value : TrayGraphStyle.Marquee;

    private static TrayGraphDataSource NormalizeTrayGraphDataSource(TrayGraphDataSource value) =>
        Enum.IsDefined(value) ? value : TrayGraphDataSource.CPUAverage;

    private static CPUPerformanceGraphView NormalizeCPUPerformanceGraphView(
        CPUPerformanceGraphView value) =>
        Enum.IsDefined(value) ? value : CPUPerformanceGraphView.LogicalProcessors;
}
