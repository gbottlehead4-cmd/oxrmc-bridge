# Live-rig tracing tools (Sigma → OXRMC)

Goal: figure out the rig's real motion signal by **measuring it on the live rig**,
instead of waiting for Sigma to document their pipeline. Out of this we get the
units, signs, axes and rate we'd otherwise ask their dev for — and the basis for a
standalone Sigma→OXRMC bridge that writes the `motionRigPose` flypt file (see
[../docs/MMF_SPEC.md](../docs/MMF_SPEC.md)) at full rate.

> Run all of this **on the sim PC** (the one with the rig, sensor, and Sigma app).
> The packet capture needs an **elevated** (Run as administrator) shell.

## Pieces

| File | What it does |
|---|---|
| `SensorLogger.cs` | Logs WitMotion roll/pitch/yaw + UTC ms to `sensor_*.csv`. The **answer key** (known physical angles). |
| `SrtaSniffer.cs` | Promiscuous raw-socket capture of Sigma's UDP motion stream to the controller (port 2222, 192.168.153.x) → `srta_*.csv`. No npcap needed; **must be elevated**. The **cipher text**. |
| `correlate.py` | Aligns the two by timestamp and reports which payload bytes track roll/pitch, with scale + sign. The **codebreaker** (pure stdlib Python). |
| `build-tools.bat` | Compiles the two C# tools with the in-box .NET Framework csc (no SDK). |

## Workflow

0. **First, check for a supported output.** Open the Sigma app and look for a motion
   / telemetry **output / port-forwarding** setting (it has a `ForwardingParameters`
   feature). If it can emit pose in a known format, you may not need to decode anything.

1. **Build:** `.\build-tools.bat`

2. **Start both logs** (two shells; the sniffer one **elevated**):
   ```
   SensorLogger.exe                 (auto-detects the COM port)
   SrtaSniffer.exe                  (auto-detects the 192.168.153.x adapter)
   ```
   If the sniffer can't find the adapter, run `ipconfig`, find the rig-subnet IPv4,
   and pass it: `SrtaSniffer.exe 2222 <thatIp>`.

3. **Do isolated moves** while driving, ~1 min each so one axis dominates:
   - pure **roll** → hold a steady long corner
   - pure **pitch** → hard braking / acceleration
   - pure **heave** → kerbs / bumps
   Keep a note of which time window was which (or just do them in order).

4. **Decode:** `python correlate.py srta_xxx.csv sensor_xxx.csv`
   High `|r|` at an offset = that axis. The `deg/unit` slope gives the scale; its
   sign gives polarity. (Sigma's logs mention an "encoder", so the payload may be
   wrapped/checksummed — if nothing correlates cleanly, capture a single static
   pose vs a known tilt and diff the raw bytes first.)

5. **Build the bridge:** once the layout is known, a small writer (model it on
   [../docs/FlyptPoseWriter.cs](../docs/FlyptPoseWriter.cs)) reads the SRTA stream
   and writes the flypt MMF at full rate. For a 4-post rig, actuator lengths →
   pose is: heave = mean, pitch = front−rear, roll = left−right (rig dims are in
   the Sigma `MotionParameters.xml`).

Captures (`*.csv`) and built `*.exe` are gitignored — they stay local.
