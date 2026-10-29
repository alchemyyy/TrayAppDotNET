# DDC pipeline audit

## Acknowledged SETs that a read-back contradicts (2026-10-26)

### Report

btadn showed a monitor as applying brightness that the monitor was not applying. The user's question was whether the
Club3D DP-to-HDMI converter in front of some of the LG ULTRAGEAR+ panels was responsible.

### What a SET acknowledgement proves

`SetVCPFeature` success means only that something on the DDC path took the bytes. On a native HDMI port that is the
panel's MCU, which can be booting or flaky and drop the SET. Through an active DP-to-HDMI converter it is the
converter's own I2C-over-AUX bridge, which acknowledges every SET while its panel is dark (the simulator's link model,
taken from the 2026-09-29 recorder logs). Only a read-back that names the target proves application.

btadn's read-degraded state is entered on exactly such an acknowledgement: recovery's reads all fail, a probe SET is
accepted, the post-write read fails, and `PromoteReadDegraded` makes the row operable with a degraded glyph. In the
user's logs (2026-10-13 to 2026-10-26), 18 of 19 recovery write probes were acknowledged. Nine were followed by a
successful read in the same pass and recovered through `PromoteRecovered`. The other nine entered read-degraded state
on the acknowledgement alone. In five of those the write made in that state exhausted its verification with every reply
unreadable, in two it ended without a logged failure after two unreadable replies (a matching third read), and two
logged no verification at all. Every one of the nine rows was brought back by a successful read 2.1 to 4.7 s later,
and TrayDiag reported each as `failed=False` with its target in the meantime.

### Gaps

- `VerifyAppliedAsync` kept a read-degraded row's best-effort state after its last attempt even when a reply had named
  another value, which proves the acknowledged SET was not applied. The row stayed operable, showing its target.
- Demoting such a row alone would not hold: the next recovery pass whose reads failed would accept another probe SET
  and make the row read-degraded again, cycling with every contradicting read-back.
- TrayDiag printed a read-degraded row as `failed=False` with `eff` set to its target, indistinguishable from a
  confirmed row.

### Behavior now

- A verification whose replies include a value other than the target demotes the row, read-degraded or not, with the
  reason `Brightness read back as X% instead of Y% after N attempts`. Only verifications whose every reply was
  unreadable keep a read-degraded row's best-effort state.
- Such a reply marks the row as contradicted. Until a matching read-back confirms a write to it, recovery sends that
  row no generic or blind write probe and never makes it read-degraded: an acknowledged SET leaves it Failed, with a
  reason saying the acknowledgement could not be trusted. The checksum resynchronization SET still runs, because it
  promotes only on a confirming read.
- TrayDiag prints `degraded=True|False` for every row; a degraded row's `eff` is its intent, not the panel's value.
- The `recovered` and `promoted` log lines name the brightness the panel reported, because the recovery GET proves the
  reply path, not the brightness. The replay that follows is the first write confirmed or refuted.
- Nothing persists an unconfirmed value: `LastBusBrightness` is still stamped only by a matching read-back.

Regression cover: `ConverterPanelThatDropsAcknowledgedSetsIsNotKeptReadDegraded` (DDC simulator, with the new
`IgnoresSetsAfterColdBootMs` post-boot SET drop modeled on the 2026-10-26 10:53 log),
`ReadDegradedRowIsDemotedWhenAReplyContradictsItsAcknowledgedWrite` and
`AcknowledgementAloneCannotRestoreARowWhoseWritesWereContradictedUntilAWriteIsConfirmed` (`MonitorRecoveryTests`).

### Left as is

A dark panel behind a converter still turns read-degraded on the converter's acknowledgement while every reply stays
garbage, because no read-back ever contradicts it. That state is shown as degraded in the flyout and TrayDiag. Removing
the read-degraded state and the `AllowBlindDDCWritesDuringDegradedState` setting entirely is a product decision this
change does not make.

## Power-off intent outliving a button power-on (2026-10-29)

### Report

On some mornings no slider reached the three LG ULTRAGEAR+ panels, and on others one panel's DDC stayed unavailable.
Restarting btadn fixed both. The user proposed relaunching the panel's DDC helper process as the recovery step.

### Evidence

The user hard-powers all three panels off with btadn at night (`SetPowerState ... code=0xD6; value=0x05; mode=Hard`)
and switches them on with their buttons in the morning. The RTX 5080 and the active DP-to-HDMI converters keep the
panels enumerated, so Windows reports neither.

- 2026-10-27: the rows still held the power-off intent from 00:42. Between 08:07:34 and 08:07:39 the master slider
  moved from 41 to 92 to 17 under the engaged curve with no DDC traffic at all. The user restarted at 08:07:45.
- 2026-10-29: a Refresh re-probe demoted DISPLAY2, behind a converter, at 07:57:45 with `0xC0262589`; its replies were
  `0xC0262589` and `0xC0262585`. Read-only recovery probes failed every 30 s until 08:02:41. In the restarted process
  the first recovery pass sent a blind probe SET at 08:02:49.805 and DISPLAY2 recovered at 08:02:50.062. The other
  DISPLAY2 wedges in the logs since 2026-10-20 also cleared 2 to 5 s after a SET.

A helper relaunch is already in place and did not help: a panel's helper is retired after three failed reads in a row,
then at most once per 5 minutes (`DDCHelperFailuresBeforeRetirement`, `DDCHelperRetirementCooldownMs`). On 2026-10-29
the relaunched helpers, and the restarted process's new one, read garbage until the first SET. The wedge is in the
panel's reply pipeline, not in the helper.

### Gaps

- Under an engaged curve the flyout sends master changes and manual curve overrides through `EnqueueDirectBrightness`
  with the Brightness setter suspended. Only the setter lifted the power-off intent, so the intent refused the user's
  own drags exactly like curve ticks.
- `GetStuckRecoveryCandidateIDs`, `RequestRecoveryOfUnavailableMonitors` and `TryRecoverMonitor` skipped working rows,
  so a row the app powered off was never probed while it still worked (233cfbc6 only covered failed rows). Nothing
  else noticed a button power-on on this desk.
- A failed row holding the intent got reads only, and the SET that resynchronizes a wedged LG pipeline followed
  checksum failures (`0xC026258B`) only. DISPLAY2's wedge answered `0xC0262589` and `0xC0262585`, so no state of the
  row ever got one; neither did a row whose writes were contradicted.
- The simulator oracle only noted a working row that stayed suppressed after a button power-on, and exempted a wedged
  panel the app had last been told to keep off from `WedgeNeverResynchronized`.

### Behavior now

- `EnqueueUserBrightness` lifts the power-off intent before writing, as a Brightness setter change does. The flyout
  uses it for master changes under an engaged curve and for manual curve overrides. Curve ticks, restores and turning
  the curve off keep `EnqueueDirectBrightness`, which the intent still refuses.
- A row holding the intent is a recovery candidate even while it works, an accepted power-off asks recovery to watch
  it, and opening the flyout probes it at once. A reply lifts the intent, installs the fresh DDC snapshot and replays
  the current target; a working row also needs its power reading to say on, the rule Refresh applies. Recovery leaves
  a row alone while one of its power commands is still running, verification included: with all panels powered off at
  once, the first command to finish asks recovery to watch them all, and a panel that has not applied its command yet
  still reads as on.
- The intent records the command sent (`MonitorInfo.PowerOffIntent`): `HardOff` for `0xD6 = 0x05`, including Sleep on
  a profile without a standby value, `Off` for the rest. When a `HardOff` row's reads come back garbled, recovery
  resends its `LastBusBrightness`, waits 250 ms and reads back, and only a reply whose power reading is not off lifts
  the intent. A hard-off panel takes no SETs until its button wakes it, so the write can neither wake it nor change the
  value it comes back with. `Off` rows still get reads only.
- `DDCNativeError.IsGarbledReply` covers `0xC0262583`, `0xC0262585`, `0xC0262586`, `0xC0262589`, `0xC026258A` and
  `0xC026258B`. The confirmed resynchronization SET follows any of them for failed, read-degraded and contradicted
  rows. An acknowledgement alone still makes a row read-degraded only where the generic or blind probe, or a checksum
  failure, would have before, so with blind writes off a dark panel behind a converter stays Failed.
- Recovery logs `power-off resynchronization write` for the hard-off SET, and `garbled-reply recovery write probe`
  where it logged `checksum recovery write probe`.
- The oracle reports `PowerOnUnnoticed` for a working row that keeps the intent past the recovery backoff once its
  panel is on and healthy, applies `WedgeNeverResynchronized` to hard-off rows, and allows a hard-off row's
  resynchronizing SET of its `LastBusBrightness`. `MonitorProfile.WedgeGarblesReplies` models DISPLAY2's wedge.

Regression cover: `ButtonPowerOnAfterAppHardOffResumesTheCurve`,
`GarbageWedgeAfterButtonPowerOnFromAppHardOffIsResynchronized`, `SoftPowerOffIsOnlyReadUntilThePanelIsSwitchedBackOn`,
`GarbageWedgeIsResynchronizedWithoutBlindWrites` (DDC simulator, each failing without the change) and
`HardOffPanelsStayPoweredOffOvernight` (no row leaves its intent or turns read-degraded on an acknowledgement; 39 helper
launches in the hour for three dark panels), `UserBrightnessUnderACurveLiftsThePowerOffIntentThatCurveWritesKeep`,
`RecoveryLeavesARowAloneWhileItsPowerOffIsStillVerifying` and
`AcknowledgementAloneCannotRestoreARowWhoseWritesWereContradictedUntilAWriteIsConfirmed`, which now expects the one
confirmed resynchronization SET (`MonitorRecoveryTests`). 300 random seeds, all 384 matrix cells and 2,000 further seeds
(1000-2999) report none.

### Left as is

A panel sent standby or soft-off that comes back with a wedged pipeline is still only read, because a SET wakes some
panels from those states; a slider move or a power-on from the app writes to it and clears the wedge. Watching every
powered-off panel keeps the recovery worker running all night at one probe per panel every 30 s, plus one hard-off
resynchronization SET per probe for a panel whose converter answers garbage.
