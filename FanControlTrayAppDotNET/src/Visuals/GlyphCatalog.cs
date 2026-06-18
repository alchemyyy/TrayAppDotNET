namespace FanControlTrayAppDotNET.Visuals;

/// <summary>
/// Segoe Fluent Icons codepoint strings shared by renderers, XAML, and theme defaults.
/// </summary>
internal abstract class GlyphCatalog : TrayAppDotNETCommon.Theming.GlyphCatalog
{
    // ===========================================================================
    // Generic UI glyphs
    // ===========================================================================

    public new const string SETTINGS = "\uE713"; // Setting (gear)
    public new const string POWER = "\uE7E8"; // Power
    public new const string INFO = "\uE946"; // Info
    public new const string EXIT = "\uE8BB"; // ChromeClose
    public new const string WARNING = "\uE7BA"; // Warning
    public const string FAN = "\U000F1111"; // FanFont.ttf U+F1111

    public const string CURVE_WINDOW = "\uE9E9"; // Equalizer
    public const string ADD = "\uE710"; // Add
    public const string GROUP = "\uE81E"; // Map Layers
    public const string GROUP_ADD = "GROUP_ADD"; // Composite mask resource key
    public const string DELETE = "\uE653"; // Close
    public const string VIEW = "\uE890"; // View
    public const string HIDE = "\uED1A"; // Hide
    public const string DRAG_HANDLE = "\uE700"; // GlobalNavButton

    public const string PIN = "\uE718"; // Pin
    public const string PINNED = "\uE840"; // Pinned

    public const string COLLAPSED = "\uE96D"; // Chevron Up Small
    public const string EXPANDED = "\uE96E"; // Chevron Down Small

    // Flyout text glyphs. These are rendered through IconFontFamily in XAML.
    public const string FLYOUT_FAN_GROUP = "\uF246"; // Folder-like
    public const string FLYOUT_FAN_CONTROL_MODE_MANUAL = "\uE785"; // Unlock
    public const string FLYOUT_FAN_CONTROL_MODE_CURVE = "\uE72E"; // Lock
    public const string FLYOUT_UNDOCK = "\uE8A0"; // Undock arrows
    public const string FLYOUT_REDOCK = "\uE8A2";

    // Flyout undock / redock arrows. DOCK is shown when the flyout is currently docked (click to
    // undock), UNDOCK is shown when it is currently undocked (click to redock). The aliases below
    // are what the XAML DataTrigger toggles between, so the codepoints can be swapped without
    // touching markup.
    public const string DOCK = "\uE75B";
    public const string UNDOCK = "\uE75A";
    public new const string FLYOUT_UNDOCK_ACTION = DOCK;
    public new const string FLYOUT_REDOCK_ACTION = UNDOCK;

    // Slider-thumb glyph defaults. Picked from Segoe Fluent Icons so we ship a working catalog
    // without an external font asset.
    public const string CIRCLE = "\uE91F"; // FullCircleMask
    public const string DIAMOND = "\uEB4B"; // Diamond
    public const string STAR = "\uE735"; // FavoriteStarFill
    public const string SQUARE = "\uE003"; // CheckboxFill (square)
    public const string HEART = "\uEB52"; // HeartFill
}
