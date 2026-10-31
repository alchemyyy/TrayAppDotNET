namespace TrayAppDotNETCommon.UI;

/// <summary>How a generated AXAML resource store publishes a Debug source reload.</summary>
public enum AxamlReloadMode
{
    /// <summary>
    /// A Common dictionary shared through <c>Current</c>: Debug reloads update the one instance in place and raise
    /// <c>CommonAXAMLHotReload.ResourcesReloaded</c>.
    /// </summary>
    Common,

    /// <summary>
    /// A Common dictionary each owner constructs and merges itself: Debug keeps a synchronized <c>Current</c> that owners
    /// copy from after <c>CommonAXAMLHotReload.ResourcesReloaded</c>; Release keeps no shared instance.
    /// </summary>
    CommonPerOwner,

    /// <summary>
    /// A dictionary shared through <c>Current</c>: Debug reloads update the one instance in place and raise the class's
    /// own <c>ResourcesReloaded</c> event.
    /// </summary>
    Synchronized,

    /// <summary>
    /// A dictionary shared through <c>Current</c>: each Debug reload publishes a new instance and raises the class's own
    /// <c>ResourcesReloaded</c> event, so consumers reacquire <c>Current</c>.
    /// </summary>
    Replaced,

    /// <summary>
    /// A glyph catalog: each Debug reload publishes a new instance and raises
    /// <c>GlyphCatalogHotReload.ResourcesReloaded</c>.
    /// </summary>
    GlyphCatalog,

    /// <summary>
    /// A theme-color catalog: Debug reloads update existing <c>ThemeColor</c> instances in place and raise
    /// <c>AppThemeHotReload.ResourcesReloaded</c>.
    /// </summary>
    AppTheme
}

/// <summary>
/// Marks the hand-written part of a standalone AXAML <c>ResourceDictionary</c> x:Class. The AXAML property linker then
/// generates its constructor, its shared <c>Current</c> instance, and, in Debug only, the source hot-reload store, the
/// <c>ReloadNow</c> helper, and the reload event the mode requires. The source path comes from the AXAML file itself.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AxamlResourceStoreAttribute(AxamlReloadMode mode, string label) : Attribute
{
    public AxamlReloadMode Mode { get; } = mode;

    /// <summary>Gets the name hot-reload log messages use for the dictionary.</summary>
    public string Label { get; } = label;
}
