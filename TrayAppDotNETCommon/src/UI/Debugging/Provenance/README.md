# Debug UI provenance

This subsystem links the hover inspector's effective Avalonia property values to
source-level assignment history.

## Data paths

- Centralized C# builders use one debug-only
  `DebugUIProvenance.RecordBuilder(target)` boundary per constructed or updated
  control. The source generator resolves that target's preceding Avalonia
  property mutations and emits exact property symbols, expressions, source
  lines, and columns. Production code does not contain per-property recording.
- `DebugUIProvenance.RecordProperty` remains available for exceptional mutations
  outside a centralized builder boundary.
- The AXAML property linker parses each compiled AXAML document once and emits a
  typed, debug-only catalog with source paths, line and column positions,
  structural element paths, properties, selectors, and resource keys.
- Generated module initializers register catalogs with the shared runtime index.
- The inspector displays the current effective value separately from C# history
  and AXAML source candidates. History and candidates are not labeled as the
  currently active source.

## Memory boundary

- Assignment targets use `ConditionalWeakTable`; recording never keeps a control
  alive.
- Assigned values are converted immediately to bounded text snapshots and are
  never retained by reference.
- Each object/property pair keeps at most 256 assignments.
- Inspector capture coalesces pointer traffic and applies independent ancestry,
  property, assignment, AXAML-candidate, and total-node limits.

## Deliberate exclusions

The system does not use passive `PropertyChanged` observation, value-equality
matching, or automatic recording of every CLR setter. Those mechanisms cannot
identify an exact source mutation reliably without adding pervasive noise.
C# assignment history is therefore present only for explicit common-builder or
exceptional mutation boundaries.
