# App Icon Generator

Generates the seven TrayAppDotNET `app.ico` files from the SVG sources in `resources/app_icons/`. Each ICO contains native PNG frames at 16, 20, 24, 32, 40, 48, 64, 96, 128, and 256 pixels.

Run all targets from the repository root:

```powershell
dotnet run --project tools/AppIconGenerator/AppIconGenerator.csproj -p:Platform=x64
```

Generate one or more targets:

```powershell
dotnet run --project tools/AppIconGenerator/AppIconGenerator.csproj -p:Platform=x64 -- --target BATADN --target TMTADN
```

Targets may be selected by short name or full project-directory name. The tool replaces `app.ico` in each selected project root.

## Sources

The source assets are read from `resources/app_icons/` at run time, so SVG edits take effect on the next run without rebuilding the tool:

- BATADN: `ic_fluent_battery_6_24_regular.svg`
- BTADN: `ic_fluent_brightness_high_20_filled.svg`
- FCTADN: `fan_first_good_attempt4_onverted.svg`
- NTADN: `ic_fluent_desktop_signal_20_regular.svg`
- TMTADN: `ic_fluent_desktop_tower_20_regular.svg` plus `ic_fluent_pulse_20_regular.svg`
- VTADN: `ic_fluent_speaker_2_24_regular.svg`
- TADN: `tadnlogo.svg`

Source fill colors are normalized to white so the output matches the existing TrayAppDotNET application-icon convention.
The Fluent UI System Icons license and notice are copied beside those SVG sources.
