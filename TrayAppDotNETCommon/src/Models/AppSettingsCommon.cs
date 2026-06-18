using System.ComponentModel;
using System.Runtime.CompilerServices;
using TrayAppDotNETCommon.UI.Models;

namespace TrayAppDotNETCommon.Models;

public enum TrayAppDotNETThemeMode
{
    System,
    Light,
    Dark,
}

public enum TrayAppDotNETAnimationMode
{
    System,
    Disabled,
    Enabled,
}

public interface ITrayAppDotNETUpdateSettings
{
    bool CheckForUpdatesEnabled { get; set; }
    bool ShowUpdateNotificationsEnabled { get; set; }
    bool ShowUpdateButtonInFlyout { get; set; }
    int UpdateCheckIntervalMs { get; set; }
}

public interface ITrayAppDotNETKeepWarmSettings
{
    bool KeepFlyoutWarm { get; set; }
    bool KeepTrayContextMenuWarm { get; set; }
}

public interface ITrayAppDotNETStartupMemorySettings
{
    bool PurgeMemoryOnStartup { get; set; }
}

public abstract class AppSettingsCommon(int updateCheckIntervalDefaultMs) : INotifyPropertyChanged,
    ITrayAppDotNETUpdateSettings, ITrayAppDotNETKeepWarmSettings, ITrayAppDotNETStartupMemorySettings
{
    public const int ToolTipShowDelayDefaultMs = 750;
    public const int ToolTipShowDelayMinMs = 0;
    public const int ToolTipShowDelayMaxMs = 10_000;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when any setting is changed through the settings window.</summary>
    public event Action? Changed;

    protected bool SuppressChangeNotification { get; set; }

    public bool RunOnStartup
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool Autosave
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public TrayAppDotNETThemeMode ThemeMode
    {
        get;
        set => SetField(ref field, value);
    } = TrayAppDotNETThemeMode.System;

    public NullableThemeColor TextColor
    {
        get;
        set => SetField(ref field, value);
    } = new();

    public NullableThemeColor BackgroundColor
    {
        get;
        set => SetField(ref field, value);
    } = new();

    public NullableThemeColor TrayIconColor
    {
        get;
        set => SetField(ref field, value);
    } = new();

    public bool EnableRoundedCorners
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public TrayAppDotNETAnimationMode AnimationMode
    {
        get;
        set => SetField(ref field, value);
    } = TrayAppDotNETAnimationMode.System;

    public int ToolTipShowDelayMs
    {
        get;
        set => SetField(
            ref field,
            Math.Clamp(value, ToolTipShowDelayMinMs, ToolTipShowDelayMaxMs));
    } = ToolTipShowDelayDefaultMs;

    public bool CheckForUpdatesEnabled
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool ShowUpdateNotificationsEnabled
    {
        get;
        set => SetField(ref field, value);
    }

    public bool ShowUpdateButtonInFlyout
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public int UpdateCheckIntervalMs
    {
        get;
        set => SetField(ref field, value);
    } = updateCheckIntervalDefaultMs;

    public bool KeepFlyoutWarm
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool KeepTrayContextMenuWarm
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public bool PurgeMemoryOnStartup
    {
        get;
        set => SetField(ref field, value);
    } = true;

    public List<HotkeyBinding> Hotkeys
    {
        get;
        set => SetField(ref field, value);
    } = [];

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// Fires <see cref="Changed"/> and schedules the app-specific save hook.
    /// Bulk load paths can temporarily suppress this while assigning persisted values.
    /// </summary>
    public void RaiseChanged()
    {
        if (SuppressChangeNotification) return;
        Changed?.Invoke();
        RequestSave();
    }

    protected virtual void RequestSave()
    {
    }
}
