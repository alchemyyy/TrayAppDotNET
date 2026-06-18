namespace BatteryTrayAppDotNET.Models;

public sealed partial class AppSettings : AppSettingsCommon
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

    public AppSettings()
        : base(TimeConstants.UpdateCheckIntervalDefaultMs)
    {
        WireColorCallbacks();
    }

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

    public void Save(string path)
    {
        string tmp = path + ".tmp";
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            using (FileStream stream = new(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                SaveXml(stream);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            TADNLog.Log($"AppSettings.Save: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch
            {
            }
        }
    }

    public static AppSettings LoadOrDefault() => LoadOrDefault(GetDefaultPath());

    public static AppSettings LoadOrDefault(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return LoadXml(stream);
            }
        }
        catch (Exception ex)
        {
            TADNLog.Log($"AppSettings.LoadOrDefault: {ex.Message}");
        }

        AppSettings defaults = new();
        defaults.Save(path);
        return defaults;
    }

    public static bool IsDefaultHotkeyIdentity(HotkeyAction action, string parameter, int bindingID) =>
        HotkeyDefaults.IsDefaultIdentity(action, parameter, bindingID);
}
