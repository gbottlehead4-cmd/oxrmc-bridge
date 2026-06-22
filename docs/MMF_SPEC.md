# OXRMC `flypt` shared-memory pose interface

This documents the exact memory-mapped file that **OpenXR-MotionCompensation (OXRMC)**
reads when its tracker `type = flypt`. Any motion application can write this file
to drive VR motion compensation directly — no SimHub, no VR controller, and no
change to OXRMC. The open-source **OXRMC Bridge** SimHub plugin is the reference
implementation; a standalone writer is in [`FlyptPoseWriter.cs`](FlyptPoseWriter.cs).

## Why write this directly

OXRMC needs an independent reference for how the rig is moving so it can subtract
that motion and keep the headset world-locked. A motion application already
computes the rig's pose every control tick, so it can publish that pose straight
to OXRMC at its **native rate** (e.g. game physics at 400 Hz). Going through a
SimHub plugin instead caps the update to SimHub's ~60 Hz `DataUpdate` tick (≈17 ms
worst-case staleness) — fine as a fallback, but writing this file yourself gives
~2.5 ms at 400 Hz, which is the latency/feel difference.

## The file

| Property | Value |
|---|---|
| Name | `motionRigPose` |
| Namespace | Default (session-local). Writer and OXRMC must run in the **same Windows session** as the interactive user. |
| Capacity | 256 bytes (OXRMC maps 256; only the first 48 are used) |
| Lifetime | `CreateOrOpen` semantics — either process may start first |

## Payload — 6 contiguous little-endian `float64` (doubles), from offset 0

| Index | Byte offset | Field | Unit |
|------:|------------:|-------|------|
| 0 | 0  | sway  | metres  |
| 1 | 8  | surge | metres  |
| 2 | 16 | heave | metres  |
| 3 | 24 | yaw   | radians |
| 4 | 32 | roll  | radians |
| 5 | 40 | pitch | radians |

Write **all six** every update; set any axis you don't drive to `0`. Little-endian
(x86/x64). OXRMC Bridge today populates roll & pitch and leaves the rest at 0 — a
6-DOF rig should send heave/surge/sway/yaw as well.

## Pose conventions

- **Values are deltas from the rig's neutral rest pose.** OXRMC captures a
  reference pose on calibration (`CTRL+DEL`) and compensates relative to it, so
  publish the rig's instantaneous offset from rest (rest = all zeros).
- **Send the commanded pose _after_ washout, limiting and per-axis allocation** —
  i.e. what the platform physically achieves, not the raw telemetry. Raw signal
  would over-compensate (washout and e.g. 60 % pitch / 40 % heave allocation mean
  the platform does not follow telemetry 1:1).
- **Polarity** is finalized on the rig: OXRMC has its own per-axis invert/scale and
  the `CTRL+DEL` zeroing. If a driven axis compensates the wrong way, flip that
  axis's sign at the source or in OXRMC. Treat the table above as the layout; verify
  direction once on the rig.

## OXRMC configuration

File: `%LOCALAPPDATA%\OpenXR-MotionCompensation\OpenXR-MotionCompensation.ini`

```ini
[tracker]
type = flypt

[startup]
auto_activate = 1   ; optional: start compensating automatically after the countdown
```

In-VR hotkeys (OXRMC's, optional): `CTRL+DEL` calibrate/re-centre · `CTRL+INS`
toggle compensation · `CTRL+D` centre-of-rotation overlay · `CTRL+SHIFT+L` reload
config. Shaky view → raise `[input_stabilizer] strength`.

## Verifying

- **`MmfReader.exe`** (ships with OXRMC, in its install folder) shows the live
  values in the file — confirm your pose updates and units before going into VR.
- In VR, enable the `CTRL+D` overlay and check the horizon stays put while the rig moves.

## Reference

- Standalone writer: [`FlyptPoseWriter.cs`](FlyptPoseWriter.cs)
- Full plugin implementation: [`OXRMCBridgePlugin.cs`](../OXRMCBridgePlugin.cs)
  (`MMF_NAME`, `WritePose`, the `IDX_*` field order)
