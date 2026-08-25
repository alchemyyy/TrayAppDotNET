namespace BrightnessTrayAppDotNET.Visuals;

/// <summary>
/// Preserves the Brightness-specific default color on the shared runtime glyph renderer.
/// </summary>
internal abstract class SkiaFlyoutGlyphIcon : TrayAppDotNETCommon.Visuals.SkiaFlyoutGlyphIcon
{
    protected SkiaFlyoutGlyphIcon() =>
        IconColor = AppTheme.Default.IconForeground.For(AppTheme.Default.IsLightTheme);

    /// <summary>Disposes shared runtime glyph resources during app shutdown.</summary>
    public new static void DisposeSharedResources() =>
        TrayAppDotNETCommon.Visuals.SkiaFlyoutGlyphIcon.DisposeSharedResources();
}
