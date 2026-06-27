using System.Xml.Serialization;
using TrayAppDotNETCommon.Serialization;

namespace BatteryTrayAppDotNET.Models;

[XmlRoot("AppSettings")]
public sealed class AppSettings : AppSettingsCommon
{
    private static readonly AsyncThrottler<AppSettings> SaveThrottle = new(
        TimeConstants.SettingsSaveDebounceMs,
        drainPollIntervalMs: TimeConstants.DrainPollIntervalMs);

    public const int ContextMenuFontSizeDefault = 15;
    public const int ContextMenuFontSizeMin = 8;
    public const int ContextMenuFontSizeMax = 48;

    public ContextMenuPosition ContextMenuPosition
    {
        get;
        set => SetField(ref field, value);
    } = ContextMenuPosition.Modern;

    public int ContextMenuFontSize
    {
        get;
        set
        {
            int clamped = Math.Clamp(value, ContextMenuFontSizeMin, ContextMenuFontSizeMax);
            if (field == clamped) return;
            field = clamped;
            OnPropertyChanged();
            RaiseChanged();
        }
    } = ContextMenuFontSizeDefault;

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

    public bool FlyoutUndocked
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool FlyoutHasSavedPosition
    {
        get;
        set => SetField(ref field, value);
    }

    public double FlyoutLeft
    {
        get;
        set => SetField(ref field, value);
    }

    public double FlyoutTop
    {
        get;
        set => SetField(ref field, value);
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

    public AppSettings()
        : base(TimeConstants.UpdateCheckIntervalDefaultMs)
    {
        WireColorCallbacks();
    }

    public override void OnTrayXmlDeserialized()
    {
        WireColorCallbacks();
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
        new() { TriggerID = 3, Title = "Trigger 3" },
    ];

    public void WireColorCallbacks()
    {
        Action onChanged = RaiseChanged;
        foreach (NullableThemeColor color in EnumerateColorOverrides())
        {
            color.Unsubscribe(onChanged);
            color.Subscribe(onChanged);
        }
    }

    private IEnumerable<NullableThemeColor> EnumerateColorOverrides()
    {
        yield return TextColor;
        yield return BackgroundColor;
        yield return TrayIconColor;
        yield return FlyoutBackgroundColor;
        yield return FlyoutTitleBarBackgroundColor;
    }

    protected override void RequestSave()
    {
        if (!Autosave) return;

        AppSettings self = this;
        _ = SaveThrottle.RunAsync(self, _ =>
        {
            self.Save();
            return Task.CompletedTask;
        });
    }

    public static string GetDefaultPath()
    {
        string appFolder = Program.AppLocalAppDataDirectory;
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, "settings.xml");
    }

    public static string GetDefaultDirectory() =>
        Program.AppLocalAppDataDirectory;

    public void Save() => Save(GetDefaultPath());

    public void Save(string path) =>
        TrayXmlSerializer.TryWriteFile(
            path,
            this,
            ex => TADNLog.Log($"AppSettings.Save: {ex.Message}"));

    public static AppSettings LoadOrDefault() => LoadOrDefault(GetDefaultPath());

    public static AppSettings LoadOrDefault(string path) =>
        TrayXmlSerializer.LoadFileOrDefault(
            path,
            () =>
            {
                AppSettings defaults = new();
                defaults.Save(path);
                return defaults;
            },
            ex => TADNLog.Log($"AppSettings.LoadOrDefault: {ex.Message}"));

    public static bool IsDefaultHotkeyIdentity(HotkeyAction action, string parameter, int bindingID) =>
        HotkeyDefaults.IsDefaultIdentity(action, parameter, bindingID);
}
