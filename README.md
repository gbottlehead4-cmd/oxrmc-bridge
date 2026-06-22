# OXRMC Bridge — SimHub Plugin

Controller-free VR motion compensation for the **Sigma Integrale DK2** motion rig.  
Writes rig pose data to the `motionRigPose` shared memory that **OpenXR-MotionCompensation (OXRMC)** reads, eliminating the need for a physical VR controller mounted on the rig.

## How it works

```
Game telemetry (via SimHub) ──┐
                              ├──▶ OXRMC Bridge plugin ──▶ motionRigPose MMF ──▶ OXRMC ──▶ stable VR view
WitMotion sensor (optional) ──┘
```

The plugin has two modes with automatic switching:

| Mode | Source | Accuracy | Requires |
|------|--------|----------|----------|
| **SENSOR** | WitMotion WT901C on the rig | High — actual physical tilt | Sensor + RS232-USB adapter |
| **TELEMETRY** | Game g-forces via SimHub | Approximate — estimated from accelerations | Nothing extra |

When a WitMotion sensor is detected, the plugin reads the rig's actual pitch and roll. If no sensor is connected (or it disconnects), it falls back to estimating rig position from game telemetry.

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
- **OXRMC not compensating:** Verify `type = flypt` in OXRMC config. In-game: CTRL+DEL to calibrate, CTRL+INS to activate
- **Sensor not detected:** Check COM port in Device Manager. Click Reconnect Sensor. Try different baud rate (edit BAUD_RATE in source)
- **Compensation direction wrong:** Use Invert Roll / Invert Pitch buttons
- **Compensation too strong/weak (telemetry mode):** Adjust gain up/down

## License

**PolyForm Noncommercial License 1.0.0** — see [LICENSE.md](LICENSE.md).

Free for any **noncommercial** use: you may use, modify, and share it at no cost. **You may not sell it or use it commercially** without permission. Copyright © 2026 Gidrux.
