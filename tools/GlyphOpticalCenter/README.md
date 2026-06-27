# Glyph Optical Center

Interactive Avalonia utility for deriving `GlyphDefinition` translation metadata from an optical center selected on a large glyph rasterization.

Run it from the repository root:

```powershell
dotnet run --project tools/GlyphOpticalCenter/GlyphOpticalCenter.csproj -p:Platform=x64
```

Enter a glyph or Unicode code point, choose the render and target font sizes, click the glyph's perceived center, then copy the generated `TranslateX` and `TranslateY` attributes. The preview uses the same antialiasing, light hinting, and unaligned-baseline settings as the shared flyout icon builder.
