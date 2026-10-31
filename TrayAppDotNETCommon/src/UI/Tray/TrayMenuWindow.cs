using Avalonia.Media;
using TrayAppDotNETCommon.Localization;
using TrayAppDotNETCommon.Models;
using TrayAppDotNETCommon.UI.ContextMenus;
using TrayAppDotNETCommon.UI.ControlMapping;
using TrayAppDotNETCommon.UI.Controls;
using TrayAppDotNETCommon.Visuals;

namespace TrayAppDotNETCommon.UI.Tray;

/// <summary>One entry of the standard tray menu tail.</summary>
public sealed record TrayMenuStandardEntry(string Text, Action Click, ControlMapNodeID Node)
{
    /// <summary>The standard Settings entry; an app with a different label (Network's "App Settings") passes it.</summary>
    public static TrayMenuStandardEntry Settings(Action click, ControlMapNodeID node, string? text = null) =>
        new(text ?? LocalizationManager.Instance[nameof(CommonStrings.TrayMenu_Settings)], click, node);

    public static TrayMenuStandardEntry Exit(Action click, ControlMapNodeID node) =>
        new(LocalizationManager.Instance[nameof(CommonStrings.TrayMenu_Exit)], click, node);
}

/// <summary>What a tray menu looks like and which standard entries close it.</summary>
public sealed record TrayMenuWindowOptions
{
    public required SettingsPalette Palette { get; init; }
    public required AppTheme Theme { get; init; }
    public required bool IsLight { get; init; }
    public required ITrayAppDotNETTrayMenuSettings MenuSettings { get; init; }

    /// <summary>The app's TrayMenu surface; the ContextMenu template is shared, so each menu carries its own id.</summary>
    public required ControlMapNodeID Surface { get; init; }

    public bool Rounded { get; init; } = true;
    public double FontSize { get; init; } = AppSettingsCommon.ContextMenuFontSizeDefault;
    public FontWeight FontWeight { get; init; } = FontWeight.Normal;
    public double ItemHeight { get; init; } = double.NaN;

    /// <summary>Opens a long menu scrolled to its standard entries, which sit right above the taskbar.</summary>
    public bool ScrollToBottom { get; init; }

    /// <summary>The Settings entry, or null for an app whose tray menu has none (Task Manager).</summary>
    public TrayMenuStandardEntry? Settings { get; init; }

    public required TrayMenuStandardEntry Exit { get; init; }

    /// <summary>The options every app with <see cref="AppSettingsCommon"/> uses: its theme, corners and font size.</summary>
    public static TrayMenuWindowOptions Create(
        AppSettingsCommon settings,
        AppTheme? theme,
        ControlMapNodeID surface,
        TrayMenuStandardEntry? settingsEntry,
        TrayMenuStandardEntry exit)
    {
        ArgumentNullException.ThrowIfNull(settings);

        bool isLight = AppTheme.ResolveIsLight(settings, theme);
        return new TrayMenuWindowOptions
        {
            Palette = AppSettingsPalette.Create(theme, settings, isLight),
            Theme = theme ?? AppTheme.Default,
            IsLight = isLight,
            MenuSettings = settings,
            Surface = surface,
            Rounded = settings.EnableRoundedCorners,
            FontSize = settings.ContextMenuFontSize,
            Settings = settingsEntry,
            Exit = exit
        };
    }
}

/// <summary>
/// The tray icon's context menu: the app's entries, then the standard Settings and Exit tail, with the theme's menu
/// shadow and separator in every app.
/// </summary>
public sealed class TrayMenuWindow : ContextMenuWindow
{
    public TrayMenuWindow(ContextMenuEntryBuilder appEntries, TrayMenuWindowOptions options)
        : base(BuildEntries(appEntries, options), CreateMenuOptions(options)) =>
        this.MapTo(options.Surface);

    private static List<ContextMenuEntry> BuildEntries(ContextMenuEntryBuilder entries, TrayMenuWindowOptions options)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);

        entries.AddSeparator();
        if (options.Settings is { } settings)
        {
            entries.Add(new ContextMenuEntry(settings.Text, settings.Click) { Node = settings.Node });
            entries.AddSeparator();
        }

        entries.Add(new ContextMenuEntry(options.Exit.Text, options.Exit.Click) { Node = options.Exit.Node });
        return entries.ToList();
    }

    private static ContextMenuWindowOptions CreateMenuOptions(TrayMenuWindowOptions options) =>
        new()
        {
            Palette = options.Palette,
            Rounded = options.Rounded,
            FontSize = options.FontSize,
            FontWeight = options.FontWeight,
            ItemHeight = options.ItemHeight,
            ContextMenuSettings = options.MenuSettings,
            SeparatorColor = options.Theme.Separator.For(options.IsLight),
            ShadowColor = options.Theme.MenuShadow.For(options.IsLight),
            ScrollToBottom = options.ScrollToBottom
        };
}
