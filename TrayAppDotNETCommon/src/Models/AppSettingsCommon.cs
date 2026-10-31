using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Serialization;
using TrayAppDotNETCommon.UI.Models;

namespace TrayAppDotNETCommon.Models;

public enum TrayAppDotNETThemeMode
{
    System,
    Light,
    Dark
}

public enum TrayAppDotNETAnimationMode
{
    System,
    Disabled,
    Enabled
}

/// <summary>Startup-time Avalonia rendering backend selection.</summary>
public enum TrayAppDotNETRenderingBackend
{
    GPUPreferred,
    Software
}

/// <summary>
/// Where the tray right-click menu appears. <c>Classic</c> opens at the cursor (the OS default for tray menus);
/// <c>Modern</c> docks the menu in the bottom-right corner of the primary work area with an 8px inset, matching the
/// Windows 11 system-flyout pattern. Member names are the values stored in <c>settings.xml</c>.
/// </summary>
public enum TrayAppDotNETContextMenuPosition
{
    Classic,
    Modern
}

/// <summary>
/// <c>Dynamic</c> tray icons reflect live state; <c>Static</c> ones always show the plain app glyph.
/// Member names are the values stored in <c>settings.xml</c>.
/// </summary>
public enum TrayAppDotNETTrayIconStyle
{
    Dynamic,
    Static
}

public interface ITrayAppDotNETUpdateSettings
{
    bool CheckForUpdatesEnabled { get; set; }
    bool ShowUpdateNotificationsEnabled { get; set; }
    bool ShowUpdateButtonInFlyout { get; set; }
    int UpdateCheckIntervalMs { get; set; }
    int SkippedUpdateVersion { get; set; }
}

public interface ITrayAppDotNETRenderingSettings
{
    TrayAppDotNETRenderingBackend RenderingBackend { get; set; }
}

public interface ITrayAppDotNETWarmWindowSettings
{
    bool KeepFlyoutWarm { get; set; }
    bool KeepTrayContextMenuWarm { get; set; }
}

public interface ITrayAppDotNETTrayMenuSettings
{
    bool UseSystemSubmenuShowDelay { get; set; }
    int SubmenuShowDelayMs { get; set; }
}

/// <summary>Persists the adjustable settings-window navigation width for one app.</summary>
public interface ISettingsSidebarWidthSettings
{
    double SettingsSidebarWidth { get; set; }
}

/// <summary>
/// Settings shared by every app, plus the persistence lifecycle of <c>settings.xml</c>.
/// <para>
/// Saving: an instance loaded through <see cref="LoadOrCreate{TSettings}"/> is bound to a
/// <see cref="TrayXmlFileStore{T}"/>. Setters (<see cref="SetField{T}"/>) and <see cref="RaiseChanged"/> request an
/// <em>implicit</em> save, which is coalesced and only happens while <see cref="Autosave"/> is on, as does the save at
/// exit (<see cref="SaveOnExit"/>). <see cref="Save"/> is the <em>explicit</em> save for settings UI actions and always
/// writes. An instance created with <c>new</c> (tests, previews) or read with <see cref="ReadOrDefault{TSettings}"/>
/// is not bound to a store and never writes.
/// </para>
/// </summary>
public abstract class AppSettingsCommon : INotifyPropertyChanged,
    ITrayAppDotNETUpdateSettings, ITrayAppDotNETRenderingSettings, ITrayAppDotNETWarmWindowSettings,
    ITrayAppDotNETTrayMenuSettings, ISettingsSidebarWidthSettings, ITrayXmlSerializationCallbacks,
    ITrayXmlStoredDocument
{
    public const int ContextMenuFontSizeMin = 8;
    public const int ContextMenuFontSizeMax = 48;
    public const int ContextMenuFontSizeDefault = 15;

    private ITrayXmlFileStore? _store;

    protected AppSettingsCommon(
        TrayAppDotNETRenderingBackend renderingBackendDefault = TrayAppDotNETRenderingBackend.GPUPreferred)
    {
        RenderingBackend = renderingBackendDefault;
        WireColorCallbacks();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when any setting is changed through the settings window.</summary>
    public event Action? Changed;

    protected bool SuppressChangeNotification { get; set; }

    /// <summary>The store this instance was loaded from, or null for a detached instance that never saves.</summary>
    public ITrayXmlFileStore? Store => _store;

    public bool RunOnStartup
    {
        get;
        set => SetField(ref field, value);
    } = true;

    /// <summary>
    /// True allows implicit saves: the coalesced save after a setting changes and the save at exit. Explicit saves
    /// from settings UI actions (<see cref="Save"/>) write regardless. Brightness and Fan store this under a different
    /// element name because their older files used <c>Autosave</c> for an app-specific meaning.
    /// </summary>
    public virtual bool Autosave
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

    public bool UseWindows11SettingsNavigation
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>Gets the custom sidebar width, or zero when the app-specific default is active.</summary>
    public double SettingsSidebarWidth
    {
        get;
        set => SetField(ref field, value);
    }

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
            Math.Clamp(value, TimeConstants.ToolTipShowDelayMinMs, TimeConstants.ToolTipShowDelayMaxMs));
    } = TimeConstants.ToolTipShowDelayDefaultMs;

    public TrayAppDotNETContextMenuPosition ContextMenuPosition
    {
        get;
        set => SetField(ref field, value);
    } = TrayAppDotNETContextMenuPosition.Modern;

    /// <summary>
    /// Tray context-menu font size. Drives all text in the menu, and every other menu element scales with it, so it
    /// is effectively the menu zoom. Clamped to [<see cref="ContextMenuFontSizeMin"/>,
    /// <see cref="ContextMenuFontSizeMax"/>] so a hand-edited file passes through the range the settings UI accepts.
    /// </summary>
    public int ContextMenuFontSize
    {
        get;
        set => SetField(ref field, Math.Clamp(value, ContextMenuFontSizeMin, ContextMenuFontSizeMax));
    } = ContextMenuFontSizeDefault;

    public bool UseSystemSubmenuShowDelay
    {
        get;
        set => SetField(ref field, value);
    }

    public int SubmenuShowDelayMs
    {
        get;
        set => SetField(
            ref field,
            Math.Clamp(
                value,
                TimeConstants.TrayMenuSubmenuShowDelayMinMs,
                TimeConstants.TrayMenuSubmenuShowDelayMaxMs));
    } = TimeConstants.TrayMenuSubmenuShowDelayDefaultMs;

    public TrayAppDotNETRenderingBackend RenderingBackend
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>UI culture name, or <see cref="LocalizationManager.AutoLanguage"/> to follow Windows</summary>
    public string Language
    {
        get;
        set => SetField(ref field, value);
    } = LocalizationManager.AutoLanguage;

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

    /// <summary>
    /// Update poll interval. Values at or below zero (an old build's bad default) become the default; everything else
    /// is clamped to the range the update service and the settings UI accept.
    /// </summary>
    public int UpdateCheckIntervalMs
    {
        get;
        set => SetField(
            ref field,
            value <= 0
                ? TimeConstants.UpdateCheckIntervalDefaultMs
                : Math.Clamp(value, TimeConstants.UpdateCheckIntervalMinMs, TimeConstants.UpdateCheckIntervalMaxMs));
    } = TimeConstants.UpdateCheckIntervalDefaultMs;

    public int SkippedUpdateVersion { get; set; }

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

    /// <summary>
    /// Loads <paramref name="path"/> through a <see cref="TrayXmlFileStore{T}"/> bound to the returned instance. A
    /// missing file is created from defaults; a corrupt one is moved to a <c>.corrupt-*</c> backup; a locked one runs
    /// the session on defaults without saving. Every failure is logged.
    /// </summary>
    protected static TSettings LoadOrCreate<TSettings>(string path)
        where TSettings : AppSettingsCommon, new()
    {
        TrayXmlFileStore<TSettings> store = new(
            path,
            CreateNew<TSettings>,
            static message => TADNLog.Log($"Settings: {message}"),
            static settings => settings.NormalizeLoadedSettings());
        return store.Load();
    }

    /// <summary>
    /// Reads <paramref name="path"/> without touching the disk: no defaults written, no backup made, and the returned
    /// instance never saves. Missing, corrupt or locked files yield defaults. For installer and uninstaller modes.
    /// </summary>
    public static TSettings ReadOrDefault<TSettings>(string path)
        where TSettings : AppSettingsCommon, new()
    {
        DataFileLoadResult result = TrayXmlSerializer.TryLoadFile(
            path,
            out TSettings? loaded,
            static message => TADNLog.Log($"Settings (read-only): {message}"));
        TSettings settings = result == DataFileLoadResult.Loaded ? loaded! : CreateNew<TSettings>();
        settings.NormalizeLoadedSettings();
        return settings;
    }

    private static TSettings CreateNew<TSettings>()
        where TSettings : AppSettingsCommon, new()
    {
        TSettings settings = new();
        settings.SuppressChangeNotification = true;
        settings.InitializeNewSettings();
        settings.SuppressChangeNotification = false;
        return settings;
    }

    /// <summary>Seeds state that only a brand-new settings file starts with (one-time migration markers).</summary>
    protected virtual void InitializeNewSettings()
    {
    }

    /// <summary>
    /// Repairs and migrates a loaded or newly created instance. Returns true when persisted state changed, so a loaded
    /// file is saved once.
    /// </summary>
    protected virtual bool NormalizeLoadedSettings() => false;

    /// <summary>Explicit save for settings UI actions: always writes, regardless of <see cref="Autosave"/>.</summary>
    public void Save() => _store?.SaveNow();

    /// <summary>
    /// The implicit save at exit: writes current state when <see cref="Autosave"/> is on, then waits for every queued
    /// write. Call once from the shutdown path, on the UI thread.
    /// </summary>
    public void SaveOnExit()
    {
        if (_store == null) return;

        if (Autosave) _store.SaveNow();
        _store.Flush();
    }

    void ITrayXmlStoredDocument.AttachStore(ITrayXmlFileStore store) => _store = store;

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
    /// Setter for runtime state such as the flyout dock position: persists it like any setting but raises neither
    /// <see cref="PropertyChanged"/> nor <see cref="Changed"/>, so a drag never re-applies themes, hotkeys or layouts.
    /// </summary>
    protected bool SetState<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        if (!SuppressChangeNotification) RequestSave();
        return true;
    }

    /// <summary>
    /// Fires <see cref="Changed"/> and requests an implicit save.
    /// Bulk load paths can temporarily suppress this while assigning persisted values.
    /// </summary>
    public void RaiseChanged()
    {
        if (SuppressChangeNotification) return;
        Changed?.Invoke();
        RequestSave();
    }

    /// <summary>
    /// Debug theme hot reload: lets <see cref="Changed"/> subscribers re-read the theme without saving, because the
    /// settings themselves did not change.
    /// </summary>
    [Conditional("DEBUG")]
    public void RaiseChangedForThemeReload()
    {
        if (SuppressChangeNotification) return;
        Changed?.Invoke();
    }

    /// <summary>Schedules a coalesced implicit save, honoring <see cref="Autosave"/>.</summary>
    protected void RequestSave()
    {
        if (Autosave) _store?.ScheduleSave();
    }

    /// <summary>
    /// Every color override whose edits should raise <see cref="Changed"/>. Overrides yield the base ones too.
    /// </summary>
    protected virtual IEnumerable<NullableThemeColor> EnumerateColorOverrides()
    {
        yield return TextColor;
        yield return BackgroundColor;
        yield return TrayIconColor;
    }

    /// <summary>
    /// Bridges every color override to <see cref="Changed"/>, so a committed hex or a live-preview color flows through
    /// the same notification path as every other setting. Idempotent; runs again after the reader replaced the
    /// constructor's instances.
    /// </summary>
    private void WireColorCallbacks()
    {
        Action onChanged = RaiseChanged;
        foreach (NullableThemeColor color in EnumerateColorOverrides())
        {
            color.Unsubscribe(onChanged);
            color.Subscribe(onChanged);
        }
    }

    public virtual void OnTrayXmlSerializing()
    {
    }

    public virtual void OnTrayXmlDeserializing() =>
        SuppressChangeNotification = true;

    public virtual void OnTrayXmlDeserialized()
    {
        WireColorCallbacks();
        SuppressChangeNotification = false;
    }
}

/// <summary>
/// Settings base for apps with a hotkey list. <typeparamref name="TBinding"/> is the app's persisted binding type.
/// Loading drops bindings whose action this build does not know, removes duplicate identities and adds missing
/// defaults (see <see cref="HotkeyDefaults.Normalize{TBinding, TAction}"/>).
/// </summary>
public abstract class AppSettingsCommon<TBinding, TAction>(
    TrayAppDotNETRenderingBackend renderingBackendDefault = TrayAppDotNETRenderingBackend.GPUPreferred)
    : AppSettingsCommon(renderingBackendDefault)
    where TBinding : HotkeyBindingBase<TAction>
    where TAction : struct, Enum
{
    [XmlArray("Hotkeys")]
    [XmlArrayItem("Binding")]
    public List<TBinding> Hotkeys
    {
        get;
        set => SetField(ref field, value);
    } = [];

    /// <summary>
    /// Fresh instances of the bindings this app ships, seeded for new installs and topped up on load. Defaults live on
    /// BindingID 0, so a user-added secondary binding for the same action does not block re-seeding the primary row.
    /// </summary>
    protected virtual IReadOnlyList<TBinding> CreateDefaultHotkeys() => [];

    /// <summary>
    /// True if the identity is one of the shipped defaults. The settings UI keeps a removed default as a tombstone
    /// (<c>RemovedByUser</c>) so it is not re-seeded, and hard-deletes anything else.
    /// </summary>
    public bool IsDefaultHotkeyIdentity(TAction action, string parameter, int bindingID) =>
        HotkeyDefaults.IsDefaultIdentity(CreateDefaultHotkeys(), action, parameter, bindingID);

    protected override bool NormalizeLoadedSettings() =>
        base.NormalizeLoadedSettings() | HotkeyDefaults.Normalize<TBinding, TAction>(Hotkeys, CreateDefaultHotkeys());
}
