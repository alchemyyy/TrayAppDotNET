# Headless Test Limitations

Avalonia.Headless behaviors (Avalonia 12.1.1) that differ from a real Win32 session, found while testing tadn UI in
`TrayAppDotNETCommon/tests/TrayAppDotNETCommon.Tests`.

## Sessions own their objects

- `AvaloniaTestHost` (TrayAppDotNETCommon.TestSupport) keeps one `HeadlessUnitTestSession` per theme (plain and
  Fluent) for the whole test process; every `Run*` call gets its own isolated application on that session's
  dispatcher thread. Sessions are never disposed: Avalonia 12.1.1's `HeadlessUnitTestSession.Dispose` races its own
  dispatch loop and failed passing tests at random (InvalidOperationException from `BlockingCollection.Take`, or a
  NullReferenceException on the loop task).
- An `AvaloniaObject` belongs to the dispatcher thread that created it. A static Avalonia object touched first by a
  plain-theme test and rendered by a Fluent-theme test (or the reverse) throws "The calling thread cannot access this
  object", so failures depend on test order.
- Only async tests (`RunAsync`) hit this, because awaiting lets render jobs run. Sync `Run` tests never render.
- Known case: `TextDecorations.Underline`. tadn links use `TrayAppDotNETSettingsUI.Underline()`, a new collection
  per control. Do not hand shared static Avalonia objects to tadn controls.

## Activation is asynchronous

- `Show()` does not activate a window, and `Activate()` returns before `IsActive` is true. Activation events arrive
  later in varying order, and an owner and its dialog can both report `IsActive`.
- A late activation re-focuses the window's remembered control with `NavigationMethod.Unspecified`, which clears
  `:focus-visible`. `KeyboardDevice.SetFocusedElement` ignores a re-focus of the focused control, so the ring stays
  off.
- Wait for `IsActive` after `Activate()`. After a dialog, assert where focus is, not the focus ring; assert the ring
  only on paths without a dialog.

## Focus restoration after a prompt

- Avalonia re-focuses a window's remembered control on activation only if it can take focus, so a control that is
  still disabled at that moment loses focus on Win32.
- A headless test that focus returns to the owner's control after a prompt passed without the fix (Install,
  2026-10-02). Such a test checks the end state, not the restoration.

## Popups

- The headless platform has no popup windows. A popup opens only in a window whose theme template supplies
  `PART_VisualLayerManager`, so popup tests need `AvaloniaTestHost.RunWithFluentTheme`.
