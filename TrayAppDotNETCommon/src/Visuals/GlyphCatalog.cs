namespace TrayAppDotNETCommon.Visuals;

public abstract class GlyphCatalog
{
    protected internal static readonly Glyph SETTINGS = Glyph.SegoeFluent("\uE713"); // Fluent, Settings
    protected internal static readonly Glyph POWER = Glyph.SegoeFluent("\uE7E8"); // Fluent, PowerButton
    protected internal static readonly Glyph INFO = Glyph.SegoeFluent("\uE946"); // Fluent, Info
    protected internal static readonly Glyph EXIT = Glyph.SegoeFluent("\uE8BB"); // Fluent, ChromeClose
    protected internal static readonly Glyph WARNING = Glyph.SegoeFluent("\uE7BA"); // Fluent, Warning

    protected internal static readonly Glyph CHROME_MINIMIZE = Glyph.SegoeFluent("\uE921"); // Fluent, ChromeMinimize
    protected internal static readonly Glyph CHROME_MAXIMIZE = Glyph.SegoeFluent("\uE922"); // Fluent, ChromeMaximize
    protected internal static readonly Glyph CHROME_RESTORE = Glyph.SegoeFluent("\uE923"); // Fluent, ChromeRestore
    protected internal static readonly Glyph CHROME_CLOSE = Glyph.SegoeFluent("\uE8BB"); // Fluent, ChromeClose

    protected internal static readonly Glyph CHEVRON_UP = Glyph.SegoeFluent("\uE70E"); // Fluent, ChevronUp
    protected internal static readonly Glyph CHEVRON_DOWN = Glyph.SegoeFluent("\uE70D"); // Fluent, ChevronDown
    protected internal static readonly Glyph CHEVRON_LEFT = Glyph.SegoeFluent("\uE76B"); // Fluent, ChevronLeft
    protected internal static readonly Glyph CHEVRON_RIGHT = Glyph.SegoeFluent("\uE76C"); // Fluent, ChevronRight
    protected internal static readonly Glyph CHEVRON_DOWN_BIG = Glyph.SegoeFluent("\uE96D"); // Fluent, ChevronUpSmall
    protected internal static readonly Glyph CHEVRON_UP_BIG = Glyph.SegoeFluent("\uE96E"); // Fluent, ChevronDownSmall
    protected internal static readonly Glyph CALENDAR = Glyph.SegoeFluent("\uE787"); // Fluent, Calendar

    protected internal static readonly Glyph UNDOCK = Glyph.SegoeFluent("\uE75B"); // Fluent, SIPRedock
    protected internal static readonly Glyph REDOCK = Glyph.SegoeFluent("\uE75A"); // Fluent, SIPUndock

    // NOTE: Slider thumb options use Fluent; FlyoutSlider's indicator uses MDL2
    protected internal static readonly Glyph SLIDER_THUMB_CIRCLE = Glyph.SegoeFluent("\uE91F"); // Fluent, FullCircleMask
    protected internal static readonly Glyph SLIDER_THUMB_DIAMOND = Glyph.SegoeFluent("\uEA3B"); // Fluent, CircleFill
    protected internal static readonly Glyph SLIDER_THUMB_STAR = Glyph.SegoeFluent("\uE734"); // Fluent, FavoriteStar
    protected internal static readonly Glyph SLIDER_THUMB_SQUARE = Glyph.SegoeFluent("\uE73B"); // Fluent, CheckboxFill
    protected internal static readonly Glyph SLIDER_THUMB_HEART = Glyph.SegoeFluent("\uEB51"); // Fluent, Heart

    protected internal const string SEGOE_FLUENT_ICONS = TADNFontResolver.SegoeFluentIconsFamilyName;
    protected internal const string SEGOE_MDL2_ASSETS = TADNFontResolver.SegoeMDL2AssetsFamilyName;
}
