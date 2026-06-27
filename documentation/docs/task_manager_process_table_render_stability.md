# Task Manager process table render stability

## Status

- **Local status:** Mitigated in TaskManagerTrayAppDotNET
- **Affected surface:** Processes table while sorted by a dynamic value
- **Affected baseline:** Avalonia 12.1.1 on Windows
- **Primary trigger:** A selected or hovered process changes sort rank while viewport anchoring keeps it visible

Keep the process-table scroll-content layout-rounding opt-out in place unless a
replacement preserves the exact fractional anchor correction through Avalonia's
scroll layout.

## Observed symptom

When the Processes table is sorted by a changing value such as CPU, disk, or
network usage, rows can change rank on every snapshot. Viewport anchoring keeps
the selected process at approximately the same screen position, but the same row
can still shift vertically by a fraction of a physical pixel. This changes how
the process icon and text are sampled and antialiased.

The visible result is a small up or down movement and slightly different edge
pixels between updates. The icon source, icon size, text layout, and selected
process identity can all remain unchanged while the rasterized result changes.

## Relevant rendering path

`ProcessDetailsCanvas` paints retained table layers inside a
`SettingsScrollViewport`:

1. A row's content-space top is calculated as
   `HeaderHeight + VisibleIndex * RowHeight`.
2. `ProcessViewportAnchor` captures the selected process, its row top, and the
   content height before a new snapshot is projected and sorted.
3. After sorting, the anchor requests the exact difference between the new and
   old row tops.
4. `SettingsScrollViewport.AdjustVerticalOffset` adds that difference to the
   Avalonia `ScrollViewer.Offset`.
5. The existing retained `TextLayout` objects and process image are drawn at the
   row's new content-space position.

The anchor calculation is intentionally fractional. Rounding it would make the
selected row drift whenever a row height does not map to an integral number of
physical pixels.

## Root cause

Avalonia's `ScrollContentPresenter` arranges its content using `-Offset`. With
the default inherited `UseLayoutRounding = true`, `ContentPresenter` rounds that
arrangement origin to the physical pixel grid.

The process anchor operates in device-independent pixels (DIPs):

```text
offset delta = new row top - old row top
```

Without an additional rounding stage, the new row top and new offset cancel and
the anchored row retains the same viewport position. With layout rounding, the
effective screen position is approximately:

```text
screen row top = row top - round(offset * render scale) / render scale
```

The independently rounded offset does not necessarily change by the same
physical distance as the row top. For example, at 125 percent scaling, a
19-DIP rank change is 23.75 physical pixels. The presenter must translate by an
integral pixel count, while the row moves by 23.75 pixels. The remaining
fraction changes the row's pixel phase.

Across updates, the difference between the two rounding errors can approach one
physical pixel. Bitmap interpolation then produces different icon edge pixels,
and glyph rasterization can produce slightly different text antialiasing.

Avalonia 12.1.1 source locations relevant to this behavior are:

- [`ScrollContentPresenter.ArrangeWithAnchoring`](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Controls/Presenters/ScrollContentPresenter.cs)
- [`ContentPresenter.ArrangeOverrideImpl`](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Controls/Presenters/ContentPresenter.cs)
- [`Layoutable.ArrangeCore`](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Avalonia.Base/Layout/Layoutable.cs)

## Implemented mitigation

`SettingsScrollViewport.SetScrollContentLayoutRounding` controls layout rounding
on the internal `ScrollViewer`. `ProcessDetailsPage` calls it with `false` only
for the Processes table.

`UseLayoutRounding` is inherited by the ScrollViewer template's
`ScrollContentPresenter`, so its fractional `-Offset` arrangement is preserved.
The row-top change and anchor-offset change can therefore cancel without a
second quantization step.

The outer `SettingsScrollViewport`, header border, and custom scrollbars retain
their normal layout rounding. The change does not add per-frame or per-row
calculations, rebuild text, replace icons, or alter retained drawing membership.

Relevant repository locations:

- `TaskManagerTrayAppDotNET/src/UI/ProcessDetailsPage.cs`
- `TaskManagerTrayAppDotNET/src/UI/ProcessDetailsCanvas.cs`
- `TaskManagerTrayAppDotNET/src/UI/ProcessViewportAnchoring.cs`
- `TrayAppDotNETCommon/src/UI/Controls/SettingsUI.cs`

## Why the opt-out is process-table specific

The defect requires the combination of fractional row geometry, dynamic rank
changes, and an exact viewport correction tied to a row identity. The Processes
table has that combination. Other `SettingsScrollViewport` consumers do not
currently use this reorder-anchor path.

Leaving layout rounding enabled by default preserves pixel-aligned behavior for
ordinary settings and table content. Disabling it for the shared control as a
whole would change unrelated scrolling, content extents, and rendering without
addressing an observed defect in those surfaces.

If another table later gains identity-based anchoring across dynamic reorders,
it should opt out explicitly or adopt a different complete solution.

## Trade-offs

The mitigation prioritizes a stable pixel phase over forcing every process row
onto the nearest pixel at every scroll offset.

- Precision-trackpad scrolling or scrollbar dragging can leave the process
  content at a fractional physical position. Icons, text, selection borders,
  and tree glyphs can then look slightly softer, but they remain stable while
  the anchor follows rank changes.
- The content extent and maximum vertical offset can remain fractional. This
  can produce a subpixel remainder at the bottom edge of the table.
- A fractional transform may theoretically miss renderer optimizations that
  apply only to integral translations. No material cost is expected because
  the same layers and drawing operations are retained.
- The mitigation stabilizes the chosen phase; it does not guarantee that every
  phase is the sharpest possible phase.
- Moving the window to a monitor with a different render scale can still change
  rasterization because the physical pixel grid itself changes.

## Alternatives considered

### Quantize row height to physical pixels

This would make each rank delta an integral physical distance, but row height
would become DPI-dependent. Monitor changes, typography zoom, row spacing,
content extent, and cached geometry would all need coordinated updates.

### Snap every painted row boundary

Snapping cumulative row positions can alternate physical row heights. Hit
testing, hover geometry, selection overlays, sticky headers, icon centering,
and extent calculations would need to use the same snapped boundary model.
Partial adoption would create new mismatches.

### Paint entirely in viewport coordinates

This avoids large content-space positions and the parent scroll translation,
but requires a larger redesign of retained layers, scrolling, hit testing,
sticky headers, horizontal offsets, and accessibility geometry.

### Change bitmap interpolation

Nearest-neighbor or another interpolation mode can change icon appearance, but
it does not fix the row displacement or text antialiasing. It treats one symptom
instead of preserving the geometry.

### Disable layout rounding globally

This is simpler but changes every shared scroll viewport. The current targeted
setter keeps the default behavior and limits regression risk.

## Verification

Automated coverage includes:

- `ScrollContentLayoutRoundingCanBeConfigured`, which verifies that the inner
  ScrollViewer can opt out without disabling layout rounding on the outer
  viewport.
- `DisabledLayoutRoundingPreservesFractionalScrollOffset`, which attaches an
  Avalonia `ScrollContentPresenter` to a headless window and verifies that a
  19.25-DIP offset remains exactly 19.25 DIPs.
- `ResolveAdjustmentPreservesFractionalRowPhase`, which prevents the process
  anchor correction from being rounded.

At implementation time, the complete TaskManagerTrayAppDotNET test suite and
the complete TrayAppDotNETCommon test suite passed.

Manual verification should cover:

1. Sort Processes by CPU or another rapidly changing numeric column.
2. Scroll several rows down and select a process that remains alive.
3. Keep the pointer and window stationary while rows above and below reorder.
4. Compare the selected row icon and text across multiple updates.
5. Repeat at 100, 125, 150, and 175 percent display scaling when available.
6. Exercise wheel scrolling, precision scrolling, scrollbar dragging, window
   resizing, and moving the window between monitors.

## Maintenance notes

- Do not round `ProcessViewportAnchorAdjustment.VerticalOffsetDelta`.
- Do not re-enable scroll-content layout rounding for Processes without a
  replacement that keeps row and offset quantization in the same coordinate
  model.
- After an Avalonia upgrade, re-check the three upstream arrangement methods
  before removing the mitigation.
- Verify that a changed ScrollViewer theme does not set `UseLayoutRounding`
  locally on `ScrollContentPresenter`, which would override inheritance.
- Preserve the targeted scope unless another surface demonstrates the same
  identity-anchor failure mode.
