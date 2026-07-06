# OXRMC Bridge — SimHub Plugin

Controller-free VR motion compensation for **any motion rig** — with a bonus **sensor-free** mode for **Sigma Integrale** rigs.  
Writes rig pose data to the `motionRigPose` shared memory that **OpenXR-MotionCompensation (OXRMC)** reads, eliminating the need for a physical VR controller mounted on the rig.

## How it works

```
Game telemetry (via SimHub) ──┐
                              ├──▶ OXRMC Bridge plugin ──▶ motionRigPose MMF ──▶ OXRMC ──▶ stable VR view
WitMotion sensor (optional) ──┘
```

Pick the source that fits your rig (plus two blend modes):

| Mode | Source | Accuracy | Requires |
|------|--------|----------|----------|
| **SENSOR** | WitMotion WT901C on the rig | High — actual physical tilt | Sensor + RS232-USB adapter |
| **SIGMA** | The Sigma rig's own commanded motion | High — real pitch/roll, drift-free, no sensor | A Sigma Integrale rig; SimHub run as admin |
| **TELEMETRY** | Game g-forces via SimHub | Rough estimate — see note below | Nothing extra |
| **TEL+SENSOR** | Sensor blended with telemetry | — | Sensor |
| **SIG+SENSOR** | Sigma blended with the sensor | — | Sigma rig + sensor |

**Why Telemetry is only a rough guess:** it estimates tilt from the game's raw g-forces, but your rig doesn't move on raw g-forces — motion software (Sigma's, SimHub's, anyone's) runs the data through its own profile first (washout, tilt-coordination, per-axis scaling, smoothing, travel limits). So the platform's real motion never matches the raw numbers. For accuracy use **SENSOR** (true physical tilt) or **SIGMA** (the rig's own commanded pose); Telemetry is the no-hardware fallback.

### Sigma mode (sensor-free)

On a **Sigma Integrale** rig the plugin can read the rig's own commanded pitch/roll directly — the exact motion the Sigma software sends to the platform — so you need no WitMotion sensor, and it's drift-free and full-rate. Works with any Sigma model (tuned on a DK2). In the plugin's **Sigma Integrale** section, click **Use Sigma Integrale**, set **Strength** to taste, and **Invert** an axis if it leans the wrong way. **Sigma + Sensor** blends it with a WitMotion sensor (adjustable Sensor weight).

Requirements: **run SimHub as Administrator** (it reads the rig's local network stream via a raw socket), and start SimHub **before** loading the game. It only reads your own machine's traffic and never modifies any Sigma software.

## Supported games

Any game SimHub supports: **iRacing, LMU, rFactor 2, Assetto Corsa, ACC, AMS2, Project Cars, Dirt Rally**, and many more.

## Requirements

- **SimHub** 9.x (free version is fine — no motion addon license needed)
- **OpenXR-MotionCompensation** installed and configured with `type = flypt`
- **Windows** with .NET Framework 4.8
- **Optional:** WitMotion WT901C-RS232 sensor + RS232-to-USB adapter (CH340)

## Installation

1. Close SimHub
2. Copy `User.OXRMCBridge.dll` to your SimHub installation folder  
   (default: `C:\Program Files (x86)\SimHub\`)
3. Start SimHub
4. Go to **Add/remove features** (left menu) and enable **"OXRMC Bridge"** (also turn on *Show in left main menu*)
5. Restart SimHub
6. The plugin appears in the left sidebar as **"OXRMC Bridge"**

Or run `install.bat` (as admin if SimHub is in Program Files).

## OXRMC configuration

In your OXRMC config (`OpenXR-MotionCompensation.ini`), set:

```ini
[tracker]
type = flypt
```

You don't have to edit this by hand: open the plugin's settings panel and click **OXRMC Setup → Auto-configure OXRMC (flypt)**. It finds OXRMC's config (including any per-game configs), backs each one up as `*.oxrmcbridge.bak`, sets `type = flypt` and `auto_activate = 1` (so compensation starts on its own after the countdown), and leaves all your other settings untouched. Run OXRMC at least once first so the config file exists. If OXRMC is already running, reload its config (`CTRL+SHIFT+L`) afterwards.

### Smoothing / "shaky view"

If the VR horizon looks jittery — most likely in Telemetry mode — enable OXRMC's input stabilizer. Use **Open OXRMC Config**, then under `[input_stabilizer]` set:

```ini
enabled = 1
strength = 0.5    ; higher = smoother but more latency
```

For more damping you can also raise `[rotation_filter]` / `[translation_filter]` `strength` from 0 toward ~0.1–0.3. A mounted WitMotion sensor (Sensor mode) reads actual tilt and is much smoother than telemetry to begin with. Reload OXRMC config with `CTRL+SHIFT+L` after editing.

## Plugin UI

The plugin has a settings panel in SimHub showing:

- **Mode** — SENSOR (green) or TELEMETRY (yellow)
- **Sensor status** — COM port and connection state
- **Live roll/pitch** — current values being sent to OXRMC
- **Gain controls** — adjust telemetry-to-angle conversion (fallback mode only)
- **Invert buttons** — flip roll or pitch direction
- **Calibrate Sensor** — re-zero the sensor (rig must be at neutral position)
- **Reconnect Sensor** — re-scan COM ports if sensor was plugged in after startup
- **Auto-configure OXRMC (flypt)** — set OXRMC's tracker type to `flypt` automatically (backs up your config first)
- **Open OXRMC Config** — open OXRMC's `.ini` in your text editor to view/edit settings by hand
- **Open MMF Reader** — launch OXRMC's MMF Reader tool to verify data flow

## SimHub properties

Available for use in dashboards and formulas:

| Property | Description |
|----------|-------------|
| `OXRMCBridge.Mode` | "SENSOR" or "TELEMETRY" |
| `OXRMCBridge.SensorConnected` | true/false |
| `OXRMCBridge.SensorPort` | COM port or "none" |
| `OXRMCBridge.CurrentRollDeg` | Roll being sent (degrees) |
| `OXRMCBridge.CurrentPitchDeg` | Pitch being sent (degrees) |
| `OXRMCBridge.SensorRawRollDeg` | Raw sensor roll (degrees) |
| `OXRMCBridge.SensorRawPitchDeg` | Raw sensor pitch (degrees) |
| `OXRMCBridge.RollGain` | Current roll gain |
| `OXRMCBridge.PitchGain` | Current pitch gain |
| `OXRMCBridge.InvertRoll` | Roll inversion state |
| `OXRMCBridge.InvertPitch` | Pitch inversion state |

## Bindable actions

Map these to buttons via SimHub's Controls section:

| Action | Description |
|--------|-------------|
| `OXRMCBridge.RollGainUp/Down` | Adjust roll gain ±0.005 |
| `OXRMCBridge.PitchGainUp/Down` | Adjust pitch gain ±0.005 |
| `OXRMCBridge.ToggleInvertRoll` | Flip roll direction |
| `OXRMCBridge.ToggleInvertPitch` | Flip pitch direction |
| `OXRMCBridge.CalibrateSensor` | Re-zero sensor at neutral |
| `OXRMCBridge.ReconnectSensor` | Re-scan COM ports |
| `OXRMCBridge.ConfigureOXRMC` | Auto-set OXRMC tracker type to flypt |

## Rig dimensions (any rig)

Every rig has its own post spacing, so you set your own dimensions directly in the plugin panel — no code editing, no rebuild. Under **Rig Dimensions** you choose your post layout and enter:

- **Length** (front-to-rear post distance, mm)
- **Width** (left-to-right post distance, mm)
- **Actuator stroke** (mm)
- **3- or 4-post** configuration

The plugin calculates your max pitch/roll from these (`arctan(stroke / span)`). If your rig software reports different max angles, type them into the optional **Override** fields.

The panel ships pre-filled with the author's **Sigma Integrale DK2** values as an example — **measure your own rig and replace them**:

- Platform: 862 mm × 748 mm (33.94" × 29.45")
- Actuator stroke: 50 mm (2")
- Max pitch: ±2.43° · Max roll: ±2.80°

## Building from source

Run `build.bat`, or manually:

```
csc /target:library /out:User.OXRMCBridge.dll ^
  /reference:"%SIMHUB%\SimHub.Plugins.dll" ^
  /reference:"%SIMHUB%\GameReaderCommon.dll" ^
  /reference:"%SIMHUB%\SimHub.Logging.dll" ^
  /reference:"%SIMHUB%\WoteverCommon.dll" ^
  /reference:"%SIMHUB%\log4net.dll" ^
  /reference:PresentationCore.dll ^
  /reference:PresentationFramework.dll ^
  /reference:WindowsBase.dll ^
  /reference:System.Xaml.dll ^
  OXRMCBridgePlugin.cs SettingsControl.cs
```

Requires only the .NET Framework 4.8 C# compiler (included with Windows) and SimHub installed.

## WitMotion sensor setup

1. Get the **WT901C-RS232** (not TTL) and the official **RS232-to-USB adapter** with CH340 chip
2. Mount the sensor rigidly on the rig platform
3. Connect via USB — Windows will assign a COM port
4. Restart SimHub — the plugin auto-detects the sensor
5. With the rig at neutral position, click **Calibrate Sensor** in the plugin UI
6. The Mode indicator should show **SENSOR** (green)

## Troubleshooting

- **MMF Reader shows zeros:** Make sure the plugin is enabled in SimHub's **Add/remove features**, and a game is running
- **OXRMC not compensating:** Verify `type = flypt` and `auto_activate = 1` in OXRMC config (the plugin's Auto-configure OXRMC button sets both). With auto-activate on, compensation starts by itself a few seconds after the sim loads. OXRMC's optional in-VR hotkeys: CTRL+D shows the centre-of-rotation overlay, CTRL+DEL re-centres/calibrates, CTRL+INS toggles compensation off/on (it's already on from auto-activate, so the first press turns it off)
- **Sensor not detected:** Check COM port in Device Manager. Click Reconnect Sensor. Try different baud rate (edit BAUD_RATE in source)
- **Compensation direction wrong:** Use Invert Roll / Invert Pitch buttons
- **Compensation too strong/weak (telemetry mode):** Adjust gain up/down

## For motion-software developers

OXRMC Bridge works by writing the `motionRigPose` shared-memory file that OXRMC
reads as a `flypt` tracker. If your motion app already computes the rig's pose, you
can write that file directly at your native rate instead of going through SimHub —
no plugin, no 60 Hz cap, no change to OXRMC. See [docs/MMF_SPEC.md](docs/MMF_SPEC.md)
for the exact layout and [docs/FlyptPoseWriter.cs](docs/FlyptPoseWriter.cs) for a
drop-in reference writer.

## License

**PolyForm Noncommercial License 1.0.0** — see [LICENSE.md](LICENSE.md).

Free for any **noncommercial** use: you may use, modify, and share it at no cost. **You may not sell it or use it commercially** without permission. Copyright © 2026 Gidrux.

## Disclaimer

Independent, unofficial project. **Not affiliated with, endorsed by, or supported by Sigma Integrale, WitMotion, OpenXR-MotionCompensation, or SimHub.** "Sigma Integrale", "WitMotion", "SimHub" and other product names are trademarks of their respective owners, used here only to describe compatibility. The Sigma mode only reads your own rig's motion on your own PC to feed OXRMC; it does not modify or replace any Sigma software.
