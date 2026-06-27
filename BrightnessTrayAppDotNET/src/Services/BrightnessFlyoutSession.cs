using System.Collections.ObjectModel;
using BrightnessAppTheme = BrightnessTrayAppDotNET.Visuals.AppTheme;

namespace BrightnessTrayAppDotNET.Services;

internal sealed class BrightnessFlyoutSession : IDisposable
{
    private readonly Action<bool> _onDisabledPeriodChanged;
    private bool _disposed;

    public BrightnessFlyoutSession(
        ProfileManager profileManager,
        BrightnessAppTheme theme,
        MonitorService monitorService,
        AppSettings? settings,
        string masterRowName,
        string nightLightRowName,
        Action<bool> onDisabledPeriodChanged)
    {
        ProfileManager = profileManager;
        Theme = theme;
        MonitorService = monitorService;
        Settings = settings;
        _onDisabledPeriodChanged = onDisabledPeriodChanged;

        IsNightLightActive = NightLightProvider.IsSupported() && NightLightProvider.IsEnabled();
        Monitors = MonitorService.Monitors;
        AwaitingInitialAsyncMonitorEnrollment = Monitors.Count == 0;

        int initialNightLightStrength = NightLightProvider.IsSupported()
            ? NightLightProvider.GetStrength()
            : 0;

        NightLightMonitor = new MonitorInfo
        {
            ID = "nightlight",
            Name = nightLightRowName,
            IsNightLight = true,
            IconGlyph = GlyphCatalog.CRESCENT_SUN,
            Brightness = FlipIfNightLightInverted(initialNightLightStrength),
        };

        MasterMonitor = new MonitorInfo
        {
            ID = "master",
            Name = masterRowName,
            IconGlyph = GlyphCatalog.SYNC_BADGE,
            Brightness = Settings?.LastMasterBrightness ?? 100,
            IsMaster = true,
        };

        AllItems = [];
        foreach (MonitorInfo monitor in Monitors)
        {
            MasterMonitor.Dependents.Add(monitor);
            AllItems.Add(monitor);
        }

        AllItems.Add(MasterMonitor);
        AllItems.Add(NightLightMonitor);

        CurveService = new EnvironmentalCurveService(
            ProfileManager,
            MonitorService,
            Settings,
            Monitors,
            MasterMonitor,
            NightLightMonitor,
            FlipIfNightLightInverted,
            OnDisabledPeriodChanged)
        {
            IsBrightnessCurveEnabled = Settings?.EnvironmentalBrightnessCurveEnabled ?? false,
            IsNightLightCurveEnabled = Settings?.EnvironmentalNightLightCurveEnabled ?? false,
        };

        MonitorService.IsBrightnessCurveEnabledQuery = () => CurveService.IsBrightnessCurveEnabled;
        MonitorService.IsInDisabledPeriodQuery = () => IsInDisabledPeriod;
    }

    public ProfileManager ProfileManager { get; }
    public BrightnessAppTheme Theme { get; }
    public AppSettings? Settings { get; }
    public MonitorService MonitorService { get; }
    public ObservableCollection<MonitorInfo> Monitors { get; }
    public ObservableCollection<MonitorInfo> AllItems { get; }
    public MonitorInfo MasterMonitor { get; }
    public MonitorInfo NightLightMonitor { get; }
    public EnvironmentalCurveService CurveService { get; }
    public bool IsNightLightActive { get; set; }
    public bool AwaitingInitialAsyncMonitorEnrollment { get; set; }
    public bool IsInDisabledPeriod { get; set; }

    public bool IsBrightnessCurveEnabled
    {
        get => CurveService.IsBrightnessCurveEnabled;
        set => CurveService.IsBrightnessCurveEnabled = value;
    }

    public bool IsNightLightCurveEnabled
    {
        get => CurveService.IsNightLightCurveEnabled;
        set => CurveService.IsNightLightCurveEnabled = value;
    }

    public int FlipIfNightLightInverted(int value) =>
        Settings?.InvertNightLightSlider == true ? 100 - value : value;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        MonitorService.IsBrightnessCurveEnabledQuery = null;
        MonitorService.IsInDisabledPeriodQuery = null;
        CurveService.Dispose();
    }

    private void OnDisabledPeriodChanged(bool isDisabled) => _onDisabledPeriodChanged(isDisabled);
}
