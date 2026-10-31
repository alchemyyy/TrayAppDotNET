using System.Xml.Serialization;

namespace BatteryTrayAppDotNET.Models;

[XmlRoot("AppSettings")]
public sealed class AppSettings : AppSettingsCommon<HotkeyBinding, HotkeyAction>, IFlyoutDockSettings
{
    public const int DischargeEstimateChecksDefault = 24;
    public const int DischargeEstimateChecksMin = 1;
    public const int DischargeEstimateChecksMax = 720;
    public const double DischargeEstimateHalfLifeChecksDefault = 6;
    public const double DischargeEstimateHalfLifeChecksMin = 0.5;
    public const double DischargeEstimateHalfLifeChecksMax = 720;

    public int DischargeEstimateChecks
    {
        get;
        set => SetField(ref field, Math.Clamp(value, DischargeEstimateChecksMin, DischargeEstimateChecksMax));
    } = DischargeEstimateChecksDefault;

    public double DischargeEstimateHalfLifeChecks
    {
        get;
        set => SetField(ref field, double.IsFinite(value)
            ? Math.Clamp(value, DischargeEstimateHalfLifeChecksMin, DischargeEstimateHalfLifeChecksMax)
            : DischargeEstimateHalfLifeChecksDefault);
    } = DischargeEstimateHalfLifeChecksDefault;

    // Flyout undock/redock.
    // These mirror the Volume flyout settings so the titlebar's undock button can persist state and position.
    public bool AllowFlyoutUndock
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool ClampUndockedFlyoutToScreen
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool RestoreFlyoutUndockedOnStartup
    {
        get;
        set => SetField(ref field, value);
    } = true;

    // Runtime dock state: persisted without raising Changed, so a drag never re-applies the app or rebuilds the flyout
    public bool FlyoutUndocked
    {
        get;
        set => SetState(ref field, value);
    } = true;

    public bool FlyoutHasSavedPosition
    {
        get;
        set => SetState(ref field, value);
    }

    public double FlyoutLeft
    {
        get;
        set => SetState(ref field, value);
    }

    public double FlyoutTop
    {
        get;
        set => SetState(ref field, value);
    }

    public bool FlyoutHeaderAtBottom
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public NullableThemeColor FlyoutBackgroundColor
    {
        get;
        set => SetField(ref field, value);
    } = new();

    public NullableThemeColor FlyoutTitleBarBackgroundColor
    {
        get;
        set => SetField(ref field, value);
    } = new();

    [XmlArray("Triggers")]
    [XmlArrayItem("Trigger")]
    public List<BatteryTriggerEntry> Triggers
    {
        get;
        set => SetField(ref field, value);
    } = CreateDefaultTriggers();

    public override void OnTrayXmlDeserialized()
    {
        EnsureTriggerDefaults();
        base.OnTrayXmlDeserialized();
    }

    public void EnsureTriggerDefaults()
    {
        if (Triggers.Count == 0)
            Triggers.AddRange(CreateDefaultTriggers());

        for (int i = 0; i < Triggers.Count; i++)
        {
            BatteryTriggerEntry trigger = Triggers[i];
            if (trigger.TriggerID <= 0)
                trigger.TriggerID = i + 1;
            if (string.IsNullOrWhiteSpace(trigger.Title))
                trigger.Title = $"Trigger {i + 1}";
        }
    }

    private static List<BatteryTriggerEntry> CreateDefaultTriggers() =>
    [
        new() { TriggerID = 1, Title = "Trigger 1" },
        new() { TriggerID = 2, Title = "Trigger 2" },
        new() { TriggerID = 3, Title = "Trigger 3" }
    ];

    protected override IEnumerable<NullableThemeColor> EnumerateColorOverrides()
    {
        foreach (NullableThemeColor color in base.EnumerateColorOverrides()) yield return color;
        yield return FlyoutBackgroundColor;
        yield return FlyoutTitleBarBackgroundColor;
    }

    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, path2: "settings.xml");
    }

    public static string GetDefaultDirectory() =>
        Program.AppLocalAppDataDirectory;

    public static AppSettings LoadOrDefault() => LoadOrDefault(GetDefaultPath());

    public static AppSettings LoadOrDefault(string path) => LoadOrCreate<AppSettings>(path);
}
