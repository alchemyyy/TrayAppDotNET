# Avalonia text layout retention and OOM mitigation

## Status

- **Local status:** Mitigated across TrayAppDotNET
- **Affected baseline:** Avalonia 12.1.1 on .NET 10
- **Upstream report:** [Avalonia issue #21955](https://github.com/AvaloniaUI/Avalonia/issues/21955)
- **TextBlock follow-up:** [detached and already-invalid layout retention](https://github.com/AvaloniaUI/Avalonia/issues/21955#issuecomment-5256741715)

The mitigation is application code around an Avalonia ownership gap. Keep it in
place until an upgraded Avalonia version is confirmed to release both direct
formatted text and detached `TextBlock` layouts deterministically.

## Observed failure

TaskManagerTrayAppDotNET exhausted memory during a debug render of the process
table header. The relevant stack was:

```text
System.OutOfMemoryException
  Avalonia.Media.TextFormatting.ShapedTextRun.Split
  Avalonia.Media.TextFormatting.TextFormatterImpl.SplitTextRuns
  Avalonia.Media.TextFormatting.TextEllipsisHelper.Collapse
  Avalonia.Media.TextFormatting.TextLineImpl.Collapse
  Avalonia.Media.FormattedText.Draw
  ProcessDetailsCanvas.DrawHeaderContent
```

`ShapedTextRun.Split` is the allocation failure point, not proof that this one
header draw allocated all exhausted memory. Native text resources retained by
earlier draws or retired controls can accumulate until a later text operation
is the first allocation that fails. Debug mode is where this incident was
observed; the ownership defects are not debug-only behavior.

Current `ProcessDetailsCanvas` source uses `TextLayout`, not `FormattedText`,
and disposes transient, replaced, and owner-held layouts. If a new stack from
that method contains `FormattedText.Draw`, first confirm that the binary and
PDB match the current source, then check for a reintroduced call site.

## Two related retention paths

### Direct `FormattedText`

In Avalonia 12.1.1, `FormattedText` creates text lines backed by native shaping
and glyph resources. The lower-level objects have disposal paths, but
`FormattedText` neither exposes `IDisposable` nor disposes all produced lines.
The retained cost grows with distinct text, typeface, size, and constraint
combinations. A long-lived application that continually formats changing text
can therefore grow its native heap even when managed objects are collected.

`TextLayout` uses the same general text pipeline but is `IDisposable`. It is the
required replacement for application-owned formatted text while this Avalonia
behavior remains present.

### Detached `TextBlock`

`TextBlock` caches a private `TextLayout` in `_textLayout`. Avalonia 12.1.1
disposes that layout when measure is invalidated, but it has no detach handler
that releases the final layout when the control leaves the visual tree.

Calling only `InvalidateMeasure()` is not deterministic. The call is gated by
`IsMeasureValid`; when a property change has already invalidated measure and a
new layout is materialized before retirement, a second invalidation is a no-op
and `_textLayout` remains held. This is common when content changes during the
same redraw pass that replaces its control tree.

The deterministic application-side release sequence is:

```csharp
if (!textBlock.IsMeasureValid)
    textBlock.Measure(new Size(0, 0));

textBlock.InvalidateMeasure();
```

The zero-size measure makes the control measure-valid. The following
invalidation can then enter Avalonia's disposal path and clear the cached
layout.

## Why TrayAppDotNET was exposed

TrayAppDotNET applications are long-running processes and all currently pin
Avalonia 12.1.1. Static analysis found both relevant workload patterns:

- Custom canvases repeatedly format labels, readings, axis values, legends,
  headers, and ellipsized table cells with changing text and constraints.
- Settings and generated UI replace control subtrees, combo-box content,
  search results, and temporary measurement probes.
- A whole ancestor can be detached while its descendant `TextBlock` objects
  remain referenced by the retired root. Normal object reachability does not
  invoke the missing text-layout disposal path.
- A temporary `TextBlock` can be measured without ever attaching to a visual
  tree, so a detach-only workaround cannot cover every case.

The repository therefore suffered the same ownership defects described in the
upstream issue, not merely a superficially similar OOM stack.

## Implemented mitigation

### Disposable text in custom drawing

All repository-owned `FormattedText` use was replaced with `TextLayout`.
Render-local layouts are declared with `using`; collections and cached layouts
are disposed on failure, replacement, and final owner disposal.

The audited custom-drawing areas include:

- `TaskManagerTrayAppDotNET/src/UI/ProcessDetailsCanvas.cs`
- `BrightnessTrayAppDotNET/src/UI/Settings/Environmental/EnvironmentalCurveEditor.Math.cs`
- `BrightnessTrayAppDotNET/src/UI/Settings/Environmental/EnvironmentalCurveEditor.Rendering.cs`
- `BrightnessTrayAppDotNET/src/UI/Settings/Environmental/EnvironmentalMapPickerCanvas.cs`
- `FanControlTrayAppDotNET/src/UI/Curves/FanCurveEditor.cs`
- `TrayAppDotNETCommon/src/UI/Controls/FlyoutSlider.cs`

This command must return no C# call sites:

```console
rg -n '\bFormattedText\b' -g '*.cs' .
```

### Process-wide `TextBlock` detach handling

`TrayAppDotNETCommon/src/UI/TextBlockLayoutLifetime.cs` installs an
application-wide style for every `TextBlock`, including text blocks created by
control templates. The style enables attached lifetime handling with these
rules:

1. On visual detach, release the cached layout with the deterministic measure
   and invalidate sequence.
2. Mark the control released so explicit retirement and detach can safely
   overlap.
3. Reset that marker if the same control is attached again.

`TrayAppDotNETAvalonia.InitializeDefaults` installs the style immediately after
the Fluent theme. Applications using the common initialization path receive the
mitigation automatically.

### Explicit retirement fallback

Detach events do not cover measured controls that were never attached, and
teardown code can discard traversal access when it clears a root first.
`TextBlockLayoutLifetime.ReleaseForRetirement` therefore iterates a retiring
visual subtree and releases every realized `TextBlock` before descendant links
are severed.

The fallback is applied in:

- `UIContentGeneration.Dispose`, before resource disposal and root teardown
- settings scroll-host and viewport disposal
- settings combo-box content replacement, rollback, and disposal
- settings combo-box item measurement and disposal
- temporary text-width measurement probes

The settings integration lives in
`TrayAppDotNETCommon/src/UI/Controls/SettingsUI.cs`.

## Regression coverage

`BrightnessTrayAppDotNET/tests/BrightnessTrayAppDotNET.Tests/TextBlockLayoutLifetimeTests.cs`
checks the private Avalonia 12.1.1 `_textLayout` field so the result is binary
rather than inferred from noisy process-memory measurements. It verifies:

1. A layout materialized while measure is already invalid is cleared by
   `UIContentGeneration` retirement.
2. Detaching an ancestor clears a descendant layout even while the retired root
   still references the `TextBlock`.

The reflection is intentionally version-sensitive. If an Avalonia upgrade
renames or removes `_textLayout`, investigate the new ownership behavior rather
than weakening or deleting the test to make it pass.

## Rules for future UI code

1. Do not introduce `FormattedText` while the affected Avalonia behavior is in
   the pinned runtime.
2. Use `using TextLayout` for render-local and measurement-only layouts.
3. An owner that caches `TextLayout` must implement deterministic disposal and
   dispose old layouts before replacing them.
4. Release generated or replaceable control roots before clearing their child
   references.
5. Do not rely on `InvalidateMeasure()` alone for a retiring `TextBlock`; it is
   a no-op when measure is already invalid.
6. Perform layout retirement on the Avalonia UI thread.

## Upgrade and removal criteria

Do not remove the mitigation solely because Avalonia issue #21955 is closed.
For the exact version being adopted:

1. Confirm direct formatted-text ownership is fixed or keep using disposable
   `TextLayout` regardless.
2. Confirm a detached, already-measure-invalid `TextBlock` releases its cached
   layout without the application workaround.
3. Update and run the reflection regression against the new implementation.
4. Run a native-memory soak with changing text and repeated UI generation
   replacement. Managed heap stability alone is insufficient.
5. Remove the common detach and retirement hooks only after native memory
   reaches a stable plateau and the deterministic release cases still pass.

