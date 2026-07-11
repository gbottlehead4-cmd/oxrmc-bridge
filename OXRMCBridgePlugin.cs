using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Ports;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Media;

namespace User.OXRMCBridge
{
    [PluginDescription("Controller-free VR motion compensation for any motion rig. Uses WitMotion sensor, game telemetry, or both blended. Writes motionRigPose MMF for OXRMC.")]
    [PluginAuthor("Gidrux")]
    [PluginName("OXRMC Bridge")]
    public class OXRMCBridgePlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        private const string MMF_NAME = "motionRigPose";
        private const int MMF_SIZE = 256;
        private const int NUM_FIELDS = 6;

        private const int IDX_SWAY = 0;
        private const int IDX_SURGE = 1;
        private const int IDX_HEAVE = 2;
        private const int IDX_YAW = 3;
        private const int IDX_ROLL = 4;
        private const int IDX_PITCH = 5;

        // Rig physical dimensions (mm). Users enter these; angles are calculated.
        private double _rigLengthMm = 862;    // front-to-rear post distance
        private double _rigWidthMm = 748;     // left-to-right post distance
        private double _strokeMm = 50;        // actuator usable stroke
        private int _postConfig = 4;          // 3 or 4 post
        private double _overridePitchDeg = 0; // 0 = use calculated
        private double _overrideRollDeg = 0;  // 0 = use calculated

        // Telemetry gains (rad per g)
        private double _pitchGain = 0.04;
        private double _rollGain = 0.04;
        private bool _invertRoll = false;
        private bool _invertPitch = false;
        private const double SMOOTHING = 0.3;
        private double _smoothedRoll = 0.0;
        private double _smoothedPitch = 0.0;

        // Blended mode: complementary filter weight.
        // 0.0 = 100% telemetry, 1.0 = 100% sensor. Default 0.8 = sensor-dominant with telemetry for fast response.
        private double _blendAlpha = 0.8;

        // Mode: 0=TELEMETRY, 1=SENSOR, 2=TEL+SENSOR, 3=SIGMA, 4=SIG+SENSOR
        // Default = SENSOR (1): field-tested as the best compensation in every scenario.
        private int _modeOverride = 1;

        // --- Sigma Integrale source ---
        // Reads the rig's own commanded pitch/roll from the Sigma motion software's
        // stream on the local rig network and feeds it to OXRMC. Interop only, on the
        // user's own hardware — it reads, never modifies. No sensor, drift-free, full
        // rate. The raw socket needs SimHub to run as Administrator. Not affiliated
        // with or endorsed by Sigma Integrale.
        private const int SIGMA_PORT = 2222;
        private const int SIGMA_FRAME_LEN = 97;
        private const byte SIGMA_FRAME_TYPE = 0x02;
        private const int SIGMA_PITCH_OFF = 8;
        private const int SIGMA_ROLL_OFF = 12;
        private const double SIGMA_PITCH_RAD_PER_COUNT = -6.25e-11;
        private const double SIGMA_ROLL_RAD_PER_COUNT = -6.21e-11;
        private Socket _sigmaSocket;
        private Thread _sigmaThread;
        private volatile bool _sigmaRunning;
        private volatile bool _sigmaConnected;
        private long _sigmaLastPacketTicks;
        private readonly object _sigmaLock = new object();
        private double _sigmaRollRad;   // decoded, pre gain/invert
        private double _sigmaPitchRad;
        private double _sigmaGain = 1.0;
        private double _sigmaSensorBlend = 0.5;  // SIG+SENSOR mode: sensor weight (0=all Sigma, 1=all sensor)
        private string _sigmaStatus = "";  // last error / state for the UI

        // --- WitMotion sensor ---
        private SerialPort _serial;
        private Thread _sensorThread;
        private volatile bool _sensorRunning;
        private double _sensorRoll;
        private double _sensorPitch;
        private double _sensorYaw;
        private volatile bool _sensorConnected;
        private long _sensorLastPacketTicks;
        private readonly object _sensorLock = new object();
        private string _comPort = "";
        private const int BAUD_RATE = 9600;
        private const double SENSOR_TIMEOUT_SEC = 2.0;

        private double _rollOffset;
        private double _pitchOffset;
        private bool _needsCalibration = true;

        // Sensor mounting orientation — lets the sensor be fitted any way round.
        // _mountMode gives a coarse 0/90/180/270° in-plane rotation; _sensorYawOffsetDeg
        // is a fine "front" alignment trim added on top. Applied to the calibrated
        // (already zeroed) roll/pitch, so changing it never needs a re-calibration.
        private int _mountMode = 0;
        private double _sensorYawOffsetDeg = 0;
        // Plain-language names so non-technical users can pick by how they physically fitted it.
        private static readonly string[] MOUNT_NAMES = new string[] {
            "Top · label UP, X to front",
            "Label up, X to the right",
            "Underneath · label DOWN, X to front",
            "Label up, X to the left"
        };
        private static readonly string[] MOUNT_DESC = new string[] {
            "Sensor on top of the rig, sticker facing up, the printed X arrow pointing toward the FRONT. (The usual way.)",
            "Sensor flat, sticker up, but turned a quarter-turn so the X arrow points to the RIGHT.",
            "Sensor underneath the platform, sticker facing the FLOOR, X arrow still toward the FRONT.",
            "Sensor flat, sticker up, but turned a quarter-turn so the X arrow points to the LEFT."
        };

        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _accessor;
        private readonly double[] _pose = new double[NUM_FIELDS];

        public PluginManager PluginManager { get; set; }

        public ImageSource PictureIcon
        {
            get { return null; }
        }

        public string LeftMenuTitle
        {
            get { return "OXRMC Bridge"; }
        }

        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new SettingsControl(this);
        }

        public void Init(PluginManager pluginManager)
        {
            SimHub.Logging.Current.Info("OXRMCBridge: starting");

            // Restore saved settings (mount mode, gains, rig dims, calibration, etc.)
            // before touching the sensor, so the loaded calibration offset is in place.
            LoadSettings();

            try
            {
                _mmf = MemoryMappedFile.CreateOrOpen(MMF_NAME, MMF_SIZE);
                _accessor = _mmf.CreateViewAccessor(0, NUM_FIELDS * sizeof(double));
                WritePose();
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("OXRMCBridge: MMF failed: " + ex.Message);
            }

            _comPort = DetectSensorPort();
            if (_comPort.Length > 0)
            {
                StartSensor(_comPort);
            }
            else
            {
                SimHub.Logging.Current.Info("OXRMCBridge: no WitMotion sensor found, using telemetry fallback");
            }

            // If the saved mode is SIGMA, start its reader now.
            EnsureSigmaState();

            this.AttachDelegate("OXRMCBridge.SensorConnected", () => _sensorConnected);
            this.AttachDelegate("OXRMCBridge.SensorPort", () => _comPort.Length > 0 ? _comPort : "none");
            this.AttachDelegate("OXRMCBridge.Mode", () => GetMode());
            this.AttachDelegate("OXRMCBridge.CurrentRollDeg", () => _pose[IDX_ROLL] * 180.0 / Math.PI);
            this.AttachDelegate("OXRMCBridge.CurrentPitchDeg", () => _pose[IDX_PITCH] * 180.0 / Math.PI);
            this.AttachDelegate("OXRMCBridge.SensorRawRollDeg", () => { lock (_sensorLock) { return _sensorRoll; } });
            this.AttachDelegate("OXRMCBridge.SensorRawPitchDeg", () => { lock (_sensorLock) { return _sensorPitch; } });
            this.AttachDelegate("OXRMCBridge.TelemetryRollDeg", () => _smoothedRoll * 180.0 / Math.PI);
            this.AttachDelegate("OXRMCBridge.TelemetryPitchDeg", () => _smoothedPitch * 180.0 / Math.PI);
            this.AttachDelegate("OXRMCBridge.RollGain", () => _rollGain);
            this.AttachDelegate("OXRMCBridge.PitchGain", () => _pitchGain);
            this.AttachDelegate("OXRMCBridge.BlendAlpha", () => _blendAlpha);
            this.AttachDelegate("OXRMCBridge.InvertRoll", () => _invertRoll);
            this.AttachDelegate("OXRMCBridge.InvertPitch", () => _invertPitch);
            this.AttachDelegate("OXRMCBridge.MountMode", () => GetMountModeName());
            this.AttachDelegate("OXRMCBridge.SensorYawOffsetDeg", () => _sensorYawOffsetDeg);
            this.AttachDelegate("OXRMCBridge.MaxPitchDeg", () => GetMaxPitchDeg());
            this.AttachDelegate("OXRMCBridge.MaxRollDeg", () => GetMaxRollDeg());
            this.AttachDelegate("OXRMCBridge.RigLengthMm", () => _rigLengthMm);
            this.AttachDelegate("OXRMCBridge.RigWidthMm", () => _rigWidthMm);
            this.AttachDelegate("OXRMCBridge.StrokeMm", () => _strokeMm);
            this.AttachDelegate("OXRMCBridge.PostConfig", () => _postConfig);
            this.AttachDelegate("OXRMCBridge.SigmaConnected", () => IsSigmaActive());
            this.AttachDelegate("OXRMCBridge.SigmaRollDeg", () => GetSigmaRollDeg());
            this.AttachDelegate("OXRMCBridge.SigmaPitchDeg", () => GetSigmaPitchDeg());
            this.AttachDelegate("OXRMCBridge.SigmaGain", () => _sigmaGain);

            this.AddAction("OXRMCBridge.SigmaGainUp", (a, b) => { AdjustSigmaGain(0.1); });
            this.AddAction("OXRMCBridge.SigmaGainDown", (a, b) => { AdjustSigmaGain(-0.1); });
            this.AddAction("OXRMCBridge.RollGainUp", (a, b) => { AdjustRollGain(0.005); });
            this.AddAction("OXRMCBridge.RollGainDown", (a, b) => { AdjustRollGain(-0.005); });
            this.AddAction("OXRMCBridge.PitchGainUp", (a, b) => { AdjustPitchGain(0.005); });
            this.AddAction("OXRMCBridge.PitchGainDown", (a, b) => { AdjustPitchGain(-0.005); });
            this.AddAction("OXRMCBridge.BlendAlphaUp", (a, b) => { AdjustBlendAlpha(0.05); });
            this.AddAction("OXRMCBridge.BlendAlphaDown", (a, b) => { AdjustBlendAlpha(-0.05); });
            this.AddAction("OXRMCBridge.ToggleInvertRoll", (a, b) => { ToggleInvertRoll(); });
            this.AddAction("OXRMCBridge.ToggleInvertPitch", (a, b) => { ToggleInvertPitch(); });
            this.AddAction("OXRMCBridge.CalibrateSensor", (a, b) => { CalibrateSensor(); });
            this.AddAction("OXRMCBridge.ReconnectSensor", (a, b) => { ReconnectSensor(); });
            this.AddAction("OXRMCBridge.CycleMountMode", (a, b) => { CycleMountMode(); });
            this.AddAction("OXRMCBridge.SensorYawOffsetUp", (a, b) => { AdjustSensorYawOffset(5); });
            this.AddAction("OXRMCBridge.SensorYawOffsetDown", (a, b) => { AdjustSensorYawOffset(-5); });
            this.AddAction("OXRMCBridge.CycleMode", (a, b) => { CycleMode(); });
            this.AddAction("OXRMCBridge.ConfigureOXRMC", (a, b) => { ConfigureOXRMC(); });
        }

        // --- Auto-configure OpenXR-MotionCompensation ---
        // Applies the settings this plugin needs ([tracker] type = flypt so OXRMC
        // reads our MMF, and [startup] auto_activate = 1 so it starts on its own)
        // to OXRMC's config file(s). All other settings are left untouched.

        public string GetOXRMCConfigDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenXR-MotionCompensation");
        }

        public string ConfigureOXRMC()
        {
            try
            {
                string dir = GetOXRMCConfigDir();
                if (!Directory.Exists(dir))
                {
                    return "OXRMC config folder not found:\n" + dir +
                        "\n\nInstall OpenXR-MotionCompensation and run it (or launch a game with it) once so it creates its config, then try again.";
                }

                // Main config plus any per-app configs (OpenXR-MotionCompensation_<app>.ini)
                string[] files = Directory.GetFiles(dir, "OpenXR-MotionCompensation*.ini");
                if (files.Length == 0)
                {
                    return "No OXRMC .ini found in:\n" + dir +
                        "\n\nRun OpenXR-MotionCompensation once so it creates its config, then try again.";
                }

                int changed = 0;
                int alreadyOk = 0;
                foreach (string file in files)
                {
                    if (ApplyOxrmcSettings(file)) changed++;
                    else alreadyOk++;
                }

                SimHub.Logging.Current.Info("OXRMCBridge: ConfigureOXRMC — " + changed + " updated, " + alreadyOk + " already set, " + files.Length + " total");

                string msg = "Done. Checked " + files.Length + " OXRMC config file(s):\n" +
                    "  - " + changed + " updated\n" +
                    "  - " + alreadyOk + " already correct\n\n" +
                    "Applied:  tracker type = flypt,  auto_activate = 1.\n\n";
                if (changed > 0)
                    msg += "A backup (.oxrmcbridge.bak) was saved next to each changed file.\n\n";
                msg += "If OXRMC is running, reload its config (CTRL+SHIFT+L) or restart it for the change to take effect.";
                return msg;
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("OXRMCBridge: ConfigureOXRMC failed: " + ex.Message);
                return "Could not configure OXRMC:\n" + ex.Message;
            }
        }

        // The settings the plugin needs OXRMC to have: { section, key, value }.
        // type=flypt is required (OXRMC reads our MMF); auto_activate=1 starts
        // compensation on its own after the countdown. Everything else is left alone.
        private static string[][] DesiredSettings()
        {
            return new string[][]
            {
                new string[] { "tracker", "type", "flypt" },
                new string[] { "startup", "auto_activate", "1" },
            };
        }

        // Applies DesiredSettings() to one .ini, preserving all other content,
        // comments and ordering. Returns true if the file was changed.
        private bool ApplyOxrmcSettings(string file)
        {
            string[] lines = File.ReadAllLines(file);

            // Pending changes grouped by section (lower-cased). Each entry is [key, value].
            var pending = new Dictionary<string, List<string[]>>();
            foreach (string[] d in DesiredSettings())
            {
                string sec = d[0].ToLowerInvariant();
                if (!pending.ContainsKey(sec)) pending[sec] = new List<string[]>();
                pending[sec].Add(new string[] { d[1], d[2] });
            }

            var output = new List<string>(lines.Length + 8);
            bool changed = false;
            string curSection = "";

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    // Leaving curSection: append any of its keys we never found
                    AppendPending(output, pending, curSection, ref changed);
                    curSection = trimmed.Substring(1, trimmed.Length - 2).Trim().ToLowerInvariant();
                    output.Add(line);
                    continue;
                }

                if (curSection.Length > 0 && pending.ContainsKey(curSection) &&
                    trimmed.Length > 0 && !trimmed.StartsWith(";") && !trimmed.StartsWith("#") && trimmed.Contains("="))
                {
                    int eq = trimmed.IndexOf('=');
                    string key = trimmed.Substring(0, eq).Trim();
                    List<string[]> list = pending[curSection];
                    int idx = IndexOfKey(list, key);
                    if (idx >= 0)
                    {
                        string desiredVal = list[idx][1];
                        string existingVal = trimmed.Substring(eq + 1).Trim();
                        if (!existingVal.Equals(desiredVal, StringComparison.OrdinalIgnoreCase))
                            changed = true;
                        output.Add(list[idx][0] + " = " + desiredVal);
                        list.RemoveAt(idx);
                        continue;
                    }
                }

                output.Add(line);
            }

            // EOF: flush remaining keys for the final section
            AppendPending(output, pending, curSection, ref changed);

            // Any sections that never appeared in the file — add them
            foreach (KeyValuePair<string, List<string[]>> kv in pending)
            {
                if (kv.Value.Count == 0) continue;
                output.Add("");
                output.Add("[" + kv.Key + "]");
                foreach (string[] item in kv.Value)
                {
                    output.Add(item[0] + " = " + item[1]);
                    changed = true;
                }
            }

            if (!changed) return false;

            string bak = file + ".oxrmcbridge.bak";
            if (!File.Exists(bak))
                File.Copy(file, bak);

            File.WriteAllLines(file, output.ToArray());
            return true;
        }

        private static void AppendPending(List<string> output, Dictionary<string, List<string[]>> pending, string section, ref bool changed)
        {
            if (section.Length == 0 || !pending.ContainsKey(section)) return;
            List<string[]> list = pending[section];
            if (list.Count == 0) return;
            foreach (string[] item in list)
            {
                output.Add(item[0] + " = " + item[1]);
                changed = true;
            }
            list.Clear();
        }

        private static int IndexOfKey(List<string[]> list, string key)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i][0].Equals(key, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        // --- Public methods for the settings UI ---

        private static readonly string[] MODE_NAMES = new string[] { "TELEMETRY", "SENSOR", "TEL+SENSOR", "SIGMA", "SIG+SENSOR" };

        public string GetMode()
        {
            return MODE_NAMES[_modeOverride];
        }

        public string GetModeOverrideName()
        {
            return MODE_NAMES[_modeOverride];
        }

        public void CycleMode()
        {
            _modeOverride = (_modeOverride + 1) % MODE_NAMES.Length;
            EnsureSigmaState();
            SaveSettings();
        }

        // Select a mode directly (used by the "Use Sigma Integrale" button).
        public void SetMode(int mode)
        {
            _modeOverride = ((mode % MODE_NAMES.Length) + MODE_NAMES.Length) % MODE_NAMES.Length;
            EnsureSigmaState();
            SaveSettings();
        }
        public int GetModeIndex() { return _modeOverride; }
        public string GetSensorStatus()
        {
            if (_comPort.Length == 0) return "Not detected";
            if (!_sensorConnected) return _comPort + " (disconnected)";
            if (!IsSensorActive()) return _comPort + " (no data)";
            return _comPort + " (active)";
        }
        public double GetCurrentRollDeg() { return _pose[IDX_ROLL] * 180.0 / Math.PI; }
        public double GetCurrentPitchDeg() { return _pose[IDX_PITCH] * 180.0 / Math.PI; }
        public double GetTelemetryRollDeg() { return _smoothedRoll * 180.0 / Math.PI; }
        public double GetTelemetryPitchDeg() { return _smoothedPitch * 180.0 / Math.PI; }
        public double GetRollGain() { return _rollGain; }
        public double GetPitchGain() { return _pitchGain; }
        public double GetBlendAlpha() { return _blendAlpha; }

        public void AdjustRollGain(double delta) { _rollGain = Math.Max(0, Math.Min(0.2, _rollGain + delta)); SaveSettings(); }
        public void AdjustPitchGain(double delta) { _pitchGain = Math.Max(0, Math.Min(0.2, _pitchGain + delta)); SaveSettings(); }
        public void AdjustBlendAlpha(double delta) { _blendAlpha = Math.Max(0, Math.Min(1.0, _blendAlpha + delta)); SaveSettings(); }

        // Rig dimension accessors
        public double GetRigLengthMm() { return _rigLengthMm; }
        public double GetRigWidthMm() { return _rigWidthMm; }
        public double GetStrokeMm() { return _strokeMm; }
        public int GetPostConfig() { return _postConfig; }

        public void AdjustRigLength(double delta) { _rigLengthMm = Math.Max(100, Math.Min(3000, _rigLengthMm + delta)); SaveSettings(); }
        public void AdjustRigWidth(double delta) { _rigWidthMm = Math.Max(100, Math.Min(3000, _rigWidthMm + delta)); SaveSettings(); }
        public void AdjustStroke(double delta) { _strokeMm = Math.Max(5, Math.Min(500, _strokeMm + delta)); SaveSettings(); }
        public void SetRigLength(double val) { _rigLengthMm = Math.Max(100, Math.Min(3000, val)); SaveSettings(); }
        public void SetRigWidth(double val) { _rigWidthMm = Math.Max(100, Math.Min(3000, val)); SaveSettings(); }
        public void SetStroke(double val) { _strokeMm = Math.Max(5, Math.Min(500, val)); SaveSettings(); }
        public void CyclePostConfig() { _postConfig = _postConfig == 3 ? 4 : 3; SaveSettings(); }

        public double GetCalculatedPitchDeg()
        {
            if (_rigLengthMm <= 0) return 0;
            return Math.Atan(_strokeMm / _rigLengthMm) * 180.0 / Math.PI;
        }
        public double GetCalculatedRollDeg()
        {
            if (_rigWidthMm <= 0) return 0;
            return Math.Atan(_strokeMm / _rigWidthMm) * 180.0 / Math.PI;
        }
        public double GetMaxPitchDeg()
        {
            return _overridePitchDeg > 0 ? _overridePitchDeg : GetCalculatedPitchDeg();
        }
        public double GetMaxRollDeg()
        {
            return _overrideRollDeg > 0 ? _overrideRollDeg : GetCalculatedRollDeg();
        }
        public double GetOverridePitchDeg() { return _overridePitchDeg; }
        public double GetOverrideRollDeg() { return _overrideRollDeg; }
        public void SetOverridePitch(double val) { _overridePitchDeg = Math.Max(0, Math.Min(30, val)); SaveSettings(); }
        public void SetOverrideRoll(double val) { _overrideRollDeg = Math.Max(0, Math.Min(30, val)); SaveSettings(); }
        public void ToggleInvertRoll() { _invertRoll = !_invertRoll; SaveSettings(); }
        public void ToggleInvertPitch() { _invertPitch = !_invertPitch; SaveSettings(); }
        // Re-zero on the next sensor frame; the new offset is persisted from the sensor loop.
        public void CalibrateSensor() { _needsCalibration = true; }

        // Sensor mounting orientation
        public string GetMountModeName() { return MOUNT_NAMES[_mountMode]; }
        public string GetMountModeDescription() { return MOUNT_DESC[_mountMode]; }
        public int GetMountMode() { return _mountMode; }
        public void CycleMountMode() { _mountMode = (_mountMode + 1) % MOUNT_NAMES.Length; SaveSettings(); }
        public double GetSensorYawOffsetDeg() { return _sensorYawOffsetDeg; }
        public void AdjustSensorYawOffset(double delta) { _sensorYawOffsetDeg = NormalizeDeg(_sensorYawOffsetDeg + delta); SaveSettings(); }
        public void SetSensorYawOffset(double val) { _sensorYawOffsetDeg = NormalizeDeg(val); SaveSettings(); }
        private static double NormalizeDeg(double d) { d = d % 360.0; if (d < 0) d += 360.0; return d; }
        // Wrap a signed angle (degrees) into [-180, 180].
        private static double WrapDeg180(double d) { d = (d + 180.0) % 360.0; if (d < 0) d += 360.0; return d - 180.0; }

        // Maps the calibrated raw sensor roll/pitch into the rig frame using the
        // mounting orientation (in-plane rotation) and the invert flags. Returns radians.
        private void GetOrientedSensorRad(out double rollRad, out double pitchRad)
        {
            double sr, sp;
            lock (_sensorLock) { sr = _sensorRoll; sp = _sensorPitch; }
            double phi = (_mountMode * 90.0 + _sensorYawOffsetDeg) * Math.PI / 180.0;
            double c = Math.Cos(phi), s = Math.Sin(phi);
            double r = sr * c - sp * s;
            double p = sr * s + sp * c;
            if (_invertRoll) r = -r;
            if (_invertPitch) p = -p;
            rollRad = r * Math.PI / 180.0;
            pitchRad = p * Math.PI / 180.0;
        }

        // --- Sensor active check ---

        private bool IsSensorActive()
        {
            if (!_sensorConnected) return false;
            long ticks;
            lock (_sensorLock) { ticks = _sensorLastPacketTicks; }
            double elapsed = (DateTime.UtcNow.Ticks - ticks) / (double)TimeSpan.TicksPerSecond;
            return elapsed < SENSOR_TIMEOUT_SEC;
        }

        // --- WitMotion sensor ---

        private string DetectSensorPort()
        {
            string[] ports = SerialPort.GetPortNames();
            foreach (string port in ports)
            {
                try
                {
                    using (SerialPort sp = new SerialPort(port, BAUD_RATE, Parity.None, 8, StopBits.One))
                    {
                        sp.ReadTimeout = 500;
                        sp.Open();
                        byte[] buf = new byte[256];
                        long deadline = DateTime.UtcNow.Ticks + TimeSpan.TicksPerSecond;
                        int pos = 0;
                        while (DateTime.UtcNow.Ticks < deadline)
                        {
                            int b;
                            try { b = sp.ReadByte(); } catch (TimeoutException) { break; }
                            buf[pos++ % 256] = (byte)b;
                            if (pos >= 2 && buf[(pos - 2) % 256] == 0x55 && buf[(pos - 1) % 256] == 0x53)
                            {
                                SimHub.Logging.Current.Info("OXRMCBridge: WitMotion sensor detected on " + port);
                                sp.Close();
                                return port;
                            }
                        }
                        sp.Close();
                    }
                }
                catch (Exception)
                {
                }
            }
            return "";
        }

        private void StartSensor(string port)
        {
            try
            {
                _serial = new SerialPort(port, BAUD_RATE, Parity.None, 8, StopBits.One);
                _serial.ReadTimeout = 1000;
                _serial.Open();
                _sensorRunning = true;
                _sensorConnected = true;
                _sensorThread = new Thread(SensorLoop);
                _sensorThread.IsBackground = true;
                _sensorThread.Start();
                SimHub.Logging.Current.Info("OXRMCBridge: sensor started on " + port);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("OXRMCBridge: sensor open failed: " + ex.Message);
                _sensorConnected = false;
            }
        }

        private void StopSensor()
        {
            _sensorRunning = false;
            if (_sensorThread != null)
            {
                _sensorThread.Join(2000);
                _sensorThread = null;
            }
            if (_serial != null)
            {
                try { _serial.Close(); } catch (Exception) { }
                _serial = null;
            }
            _sensorConnected = false;
        }

        public void ReconnectSensor()
        {
            StopSensor();
            _comPort = DetectSensorPort();
            if (_comPort.Length > 0)
            {
                StartSensor(_comPort);
                _needsCalibration = true;
            }
        }

        private void SensorLoop()
        {
            byte[] ring = new byte[1024];
            int pos = 0;

            while (_sensorRunning)
            {
                try
                {
                    int b = _serial.ReadByte();
                    ring[pos % 1024] = (byte)b;
                    pos++;

                    if (pos >= 11)
                    {
                        int start = (pos - 11) % 1024;
                        if (ring[start] == 0x55 && ring[(start + 1) % 1024] == 0x53)
                        {
                            int sum = 0;
                            for (int i = 0; i < 10; i++)
                            {
                                sum += ring[(start + i) % 1024];
                            }
                            if ((byte)(sum & 0xFF) == ring[(start + 10) % 1024])
                            {
                                short rawRoll = (short)(ring[(start + 2) % 1024] | (ring[(start + 3) % 1024] << 8));
                                short rawPitch = (short)(ring[(start + 4) % 1024] | (ring[(start + 5) % 1024] << 8));
                                short rawYaw = (short)(ring[(start + 6) % 1024] | (ring[(start + 7) % 1024] << 8));

                                double rollDeg = rawRoll / 32768.0 * 180.0;
                                double pitchDeg = rawPitch / 32768.0 * 180.0;
                                double yawDeg = rawYaw / 32768.0 * 180.0;

                                if (_needsCalibration)
                                {
                                    _rollOffset = rollDeg;
                                    _pitchOffset = pitchDeg;
                                    _needsCalibration = false;
                                    SimHub.Logging.Current.Info("OXRMCBridge: sensor calibrated — roll offset " + rollDeg.ToString("F2") + " pitch offset " + pitchDeg.ToString("F2"));
                                    // Persist the new zero so it survives a restart (no re-calibrate needed).
                                    SaveSettings();
                                }

                                lock (_sensorLock)
                                {
                                    // Wrap the calibrated delta to [-180,180] so the zero point can sit
                                    // anywhere — including ~180° when the sensor is mounted upside down
                                    // (label facing the floor) — without glitching as the rig crosses level.
                                    _sensorRoll = WrapDeg180(rollDeg - _rollOffset);
                                    _sensorPitch = WrapDeg180(pitchDeg - _pitchOffset);
                                    _sensorYaw = yawDeg;
                                    _sensorLastPacketTicks = DateTime.UtcNow.Ticks;
                                }
                            }
                        }
                    }
                }
                catch (TimeoutException)
                {
                }
                catch (Exception ex)
                {
                    if (_sensorRunning)
                    {
                        SimHub.Logging.Current.Error("OXRMCBridge: sensor read error: " + ex.Message);
                        _sensorConnected = false;
                        Thread.Sleep(1000);
                    }
                }
            }
        }

        // --- Sigma Integrale reader ---

        // Start the reader iff SIGMA mode is selected; stop it otherwise. Safe to
        // call repeatedly (from Init, mode changes, End).
        private void EnsureSigmaState()
        {
            string m = MODE_NAMES[_modeOverride];
            bool want = m == "SIGMA" || m == "SIG+SENSOR";
            if (want && !_sigmaRunning) StartSigma();
            else if (!want && _sigmaRunning) StopSigma();
        }

        private static string AutoBindSigmaIp()
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                        ua.Address.ToString().StartsWith("192.168.153."))
                        return ua.Address.ToString();
            return null;
        }

        private void StartSigma()
        {
            string bindIp = AutoBindSigmaIp();
            if (bindIp == null)
            {
                _sigmaStatus = "No Sigma controller adapter (192.168.153.x) found. Power the rig on.";
                SimHub.Logging.Current.Info("OXRMCBridge: Sigma — no 192.168.153.x adapter");
                return;
            }
            try
            {
                _sigmaSocket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
                _sigmaSocket.Bind(new IPEndPoint(IPAddress.Parse(bindIp), 0));
                _sigmaSocket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);
                _sigmaSocket.IOControl(IOControlCode.ReceiveAll, new byte[] { 1, 0, 0, 0 }, new byte[4]);
            }
            catch (SocketException ex)
            {
                _sigmaStatus = "Run SimHub as Administrator (raw network capture needs it).";
                SimHub.Logging.Current.Error("OXRMCBridge: Sigma raw socket failed (" + ex.Message + ") — needs admin");
                if (_sigmaSocket != null) { try { _sigmaSocket.Close(); } catch (Exception) { } _sigmaSocket = null; }
                return;
            }
            _sigmaStatus = "Listening on " + bindIp + " — drive to see motion.";
            _sigmaRunning = true;
            _sigmaThread = new Thread(SigmaLoop) { IsBackground = true };
            _sigmaThread.Start();
            SimHub.Logging.Current.Info("OXRMCBridge: Sigma reader started on " + bindIp);
        }

        private void StopSigma()
        {
            _sigmaRunning = false;
            if (_sigmaSocket != null)
            {
                try { _sigmaSocket.Close(); } catch (Exception) { }  // unblocks Receive()
                _sigmaSocket = null;
            }
            if (_sigmaThread != null)
            {
                _sigmaThread.Join(2000);
                _sigmaThread = null;
            }
            _sigmaConnected = false;
        }

        private void SigmaLoop()
        {
            byte[] buf = new byte[65535];
            while (_sigmaRunning)
            {
                int n;
                try { n = _sigmaSocket.Receive(buf); }
                catch (Exception)
                {
                    if (_sigmaRunning) { _sigmaConnected = false; Thread.Sleep(200); }
                    continue;
                }
                if (n < 28 || (buf[0] >> 4) != 4 || buf[9] != 17) continue;   // IPv4 UDP
                int ihl = (buf[0] & 0x0F) * 4;
                if (ihl + 8 > n) continue;
                int dstPort = (buf[ihl + 2] << 8) | buf[ihl + 3];
                if (dstPort != SIGMA_PORT) continue;
                int payOff = ihl + 8;
                if (n - payOff < SIGMA_FRAME_LEN) continue;                   // not the motion frame
                if (buf[payOff] != SIGMA_FRAME_TYPE) continue;

                int pitchRaw = BitConverter.ToInt32(buf, payOff + SIGMA_PITCH_OFF);
                int rollRaw = BitConverter.ToInt32(buf, payOff + SIGMA_ROLL_OFF);
                lock (_sigmaLock)
                {
                    _sigmaPitchRad = pitchRaw * SIGMA_PITCH_RAD_PER_COUNT;
                    _sigmaRollRad = rollRaw * SIGMA_ROLL_RAD_PER_COUNT;
                    _sigmaLastPacketTicks = DateTime.UtcNow.Ticks;
                }
                _sigmaConnected = true;
            }
        }

        private bool IsSigmaActive()
        {
            if (!_sigmaConnected) return false;
            long ticks;
            lock (_sigmaLock) { ticks = _sigmaLastPacketTicks; }
            double elapsed = (DateTime.UtcNow.Ticks - ticks) / (double)TimeSpan.TicksPerSecond;
            return elapsed < SENSOR_TIMEOUT_SEC;
        }

        // Sigma accessors for the settings UI
        public double GetSigmaGain() { return _sigmaGain; }
        public void AdjustSigmaGain(double delta) { _sigmaGain = Math.Max(0.1, Math.Min(10.0, _sigmaGain + delta)); SaveSettings(); }
        public double GetSigmaSensorBlend() { return _sigmaSensorBlend; }
        public void AdjustSigmaSensorBlend(double delta) { _sigmaSensorBlend = Math.Max(0, Math.Min(1.0, _sigmaSensorBlend + delta)); SaveSettings(); }
        public double GetSigmaRollDeg() { lock (_sigmaLock) { return _sigmaRollRad * 180.0 / Math.PI; } }
        public double GetSigmaPitchDeg() { lock (_sigmaLock) { return _sigmaPitchRad * 180.0 / Math.PI; } }
        public string GetSigmaStatus()
        {
            string m = MODE_NAMES[_modeOverride];
            if (m != "SIGMA" && m != "SIG+SENSOR") return "Not selected";
            if (!_sigmaRunning) return _sigmaStatus.Length > 0 ? _sigmaStatus : "Stopped";
            if (!IsSigmaActive()) return _sigmaStatus.Length > 0 ? _sigmaStatus : "Waiting for stream (drive to see)";
            return "Active — receiving Sigma motion";
        }

        // --- Main data update ---

        private volatile bool _gameRunning;

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void UpdateTelemetry(GameData data)
        {
            if (!data.GameRunning || data.NewData == null)
            {
                _gameRunning = false;
                return;
            }
            _gameRunning = true;

            double swayG = data.NewData.AccelerationSway.HasValue ? data.NewData.AccelerationSway.Value : 0.0;
            double surgeG = data.NewData.AccelerationSurge.HasValue ? data.NewData.AccelerationSurge.Value : 0.0;

            double rawRoll = swayG * _rollGain * (_invertRoll ? -1.0 : 1.0);
            double rawPitch = surgeG * _pitchGain * (_invertPitch ? -1.0 : 1.0);

            _smoothedRoll = _smoothedRoll * SMOOTHING + rawRoll * (1.0 - SMOOTHING);
            _smoothedPitch = _smoothedPitch * SMOOTHING + rawPitch * (1.0 - SMOOTHING);
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            if (_accessor == null) return;

            bool sensorActive = IsSensorActive();

            // Always update telemetry so it's ready for blending
            UpdateTelemetry(data);

            string effectiveMode = MODE_NAMES[_modeOverride];

            double rollRad, pitchRad;

            if (effectiveMode == "TEL+SENSOR")
            {
                if (!sensorActive || !_gameRunning)
                {
                    // Fallback if forced blended but missing a source
                    if (sensorActive) effectiveMode = "SENSOR";
                    else if (_gameRunning) effectiveMode = "TELEMETRY";
                    else return;
                }
            }

            if (effectiveMode == "TEL+SENSOR")
            {
                double sensorRollRad, sensorPitchRad;
                GetOrientedSensorRad(out sensorRollRad, out sensorPitchRad);

                rollRad = _blendAlpha * sensorRollRad + (1.0 - _blendAlpha) * _smoothedRoll;
                pitchRad = _blendAlpha * sensorPitchRad + (1.0 - _blendAlpha) * _smoothedPitch;
            }
            else if (effectiveMode == "SENSOR")
            {
                if (!sensorActive) return;
                GetOrientedSensorRad(out rollRad, out pitchRad);
            }
            else if (effectiveMode == "SIGMA")
            {
                // Rig's own commanded pitch/roll from the Sigma UDP stream. When the
                // stream pauses (not driving) relax to level so no stale tilt lingers.
                if (IsSigmaActive())
                {
                    double sr, sp;
                    lock (_sigmaLock) { sr = _sigmaRollRad; sp = _sigmaPitchRad; }
                    rollRad = sr * _sigmaGain * (_invertRoll ? -1.0 : 1.0);
                    pitchRad = sp * _sigmaGain * (_invertPitch ? -1.0 : 1.0);
                }
                else
                {
                    rollRad = 0.0;
                    pitchRad = 0.0;
                }
            }
            else if (effectiveMode == "SIG+SENSOR")
            {
                // Blend the Sigma command (drift-free, low-latency) with the sensor
                // (true physical angle). _sigmaSensorBlend is the sensor weight.
                bool sigmaActive = IsSigmaActive();
                double sigR = 0, sigP = 0;
                if (sigmaActive)
                {
                    lock (_sigmaLock) { sigR = _sigmaRollRad; sigP = _sigmaPitchRad; }
                    sigR = sigR * _sigmaGain * (_invertRoll ? -1.0 : 1.0);
                    sigP = sigP * _sigmaGain * (_invertPitch ? -1.0 : 1.0);
                }
                double senR = 0, senP = 0;
                if (sensorActive) GetOrientedSensorRad(out senR, out senP);

                if (sigmaActive && sensorActive)
                {
                    double w = _sigmaSensorBlend;
                    rollRad = w * senR + (1.0 - w) * sigR;
                    pitchRad = w * senP + (1.0 - w) * sigP;
                }
                else if (sigmaActive) { rollRad = sigR; pitchRad = sigP; }
                else if (sensorActive) { rollRad = senR; pitchRad = senP; }
                else { rollRad = 0.0; pitchRad = 0.0; }
            }
            else
            {
                if (!_gameRunning) return;
                rollRad = _smoothedRoll;
                pitchRad = _smoothedPitch;
            }

            double maxRollRad = GetMaxRollDeg() * Math.PI / 180.0;
            double maxPitchRad = GetMaxPitchDeg() * Math.PI / 180.0;
            rollRad = Clamp(rollRad, -maxRollRad, maxRollRad);
            pitchRad = Clamp(pitchRad, -maxPitchRad, maxPitchRad);

            _pose[IDX_SWAY] = 0;
            _pose[IDX_SURGE] = 0;
            _pose[IDX_HEAVE] = 0;
            _pose[IDX_YAW] = 0;
            _pose[IDX_ROLL] = rollRad;
            _pose[IDX_PITCH] = pitchRad;

            WritePose();
        }

        private void WritePose()
        {
            if (_accessor == null) return;
            for (int i = 0; i < NUM_FIELDS; i++)
            {
                _accessor.Write(i * sizeof(double), _pose[i]);
            }
        }

        // --- Settings persistence ---
        // Saved to SimHub's PluginsData via ReadCommonSettings/SaveCommonSettings so
        // every user choice (mount mode, gains, rig dimensions, calibration, ...) survives
        // a SimHub restart. SaveSettings() is called after each change and from End().

        // Unique key — SimHub's Common store is keyed by this exact string and is NOT
        // namespaced per plugin, so a generic name like "GeneralSettings" could collide.
        private const string SETTINGS_KEY = "OXRMCBridgeSettings";

        private void LoadSettings()
        {
            try
            {
                OXRMCBridgeSettings s = this.ReadCommonSettings<OXRMCBridgeSettings>(SETTINGS_KEY, () => null);
                if (s == null) return;

                _modeOverride = (s.ModeOverride % MODE_NAMES.Length + MODE_NAMES.Length) % MODE_NAMES.Length;
                _sigmaGain = Math.Max(0.1, Math.Min(10.0, s.SigmaGain));
                _sigmaSensorBlend = Math.Max(0, Math.Min(1.0, s.SigmaSensorBlend));
                _rollGain = Math.Max(0, Math.Min(0.2, s.RollGain));
                _pitchGain = Math.Max(0, Math.Min(0.2, s.PitchGain));
                _invertRoll = s.InvertRoll;
                _invertPitch = s.InvertPitch;
                _blendAlpha = Math.Max(0, Math.Min(1.0, s.BlendAlpha));
                _mountMode = ((s.MountMode % MOUNT_NAMES.Length) + MOUNT_NAMES.Length) % MOUNT_NAMES.Length;
                _sensorYawOffsetDeg = NormalizeDeg(s.SensorYawOffsetDeg);
                _rigLengthMm = Math.Max(100, Math.Min(3000, s.RigLengthMm));
                _rigWidthMm = Math.Max(100, Math.Min(3000, s.RigWidthMm));
                _strokeMm = Math.Max(5, Math.Min(500, s.StrokeMm));
                _postConfig = s.PostConfig == 3 ? 3 : 4;
                _overridePitchDeg = Math.Max(0, Math.Min(30, s.OverridePitchDeg));
                _overrideRollDeg = Math.Max(0, Math.Min(30, s.OverrideRollDeg));

                // Reuse the saved calibration so the rig doesn't need re-zeroing every launch.
                if (s.HasCalibration)
                {
                    _rollOffset = s.RollOffset;
                    _pitchOffset = s.PitchOffset;
                    _needsCalibration = false;
                }

                SimHub.Logging.Current.Info("OXRMCBridge: settings loaded (mount=" + GetMountModeName() + ", mode=" + GetMode() + ")");
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("OXRMCBridge: LoadSettings failed: " + ex.Message);
            }
        }

        public void SaveSettings()
        {
            try
            {
                OXRMCBridgeSettings s = new OXRMCBridgeSettings
                {
                    ModeOverride = _modeOverride,
                    SigmaGain = _sigmaGain,
                    SigmaSensorBlend = _sigmaSensorBlend,
                    RollGain = _rollGain,
                    PitchGain = _pitchGain,
                    InvertRoll = _invertRoll,
                    InvertPitch = _invertPitch,
                    BlendAlpha = _blendAlpha,
                    MountMode = _mountMode,
                    SensorYawOffsetDeg = _sensorYawOffsetDeg,
                    RigLengthMm = _rigLengthMm,
                    RigWidthMm = _rigWidthMm,
                    StrokeMm = _strokeMm,
                    PostConfig = _postConfig,
                    OverridePitchDeg = _overridePitchDeg,
                    OverrideRollDeg = _overrideRollDeg,
                    HasCalibration = !_needsCalibration,
                    RollOffset = _rollOffset,
                    PitchOffset = _pitchOffset,
                };
                this.SaveCommonSettings(SETTINGS_KEY, s);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("OXRMCBridge: SaveSettings failed: " + ex.Message);
            }
        }

        public void End(PluginManager pluginManager)
        {
            SimHub.Logging.Current.Info("OXRMCBridge: stopping");
            SaveSettings();
            StopSensor();
            StopSigma();
            if (_accessor != null) { try { _accessor.Dispose(); } catch (Exception) { } }
            if (_mmf != null) { try { _mmf.Dispose(); } catch (Exception) { } }
        }
    }

    // Persisted settings (serialized by SimHub's ReadCommonSettings/SaveCommonSettings).
    // Defaults here match the plugin's in-memory defaults so a fresh install behaves the same.
    public class OXRMCBridgeSettings
    {
        public int ModeOverride = 0;
        public double SigmaGain = 1.0;
        public double SigmaSensorBlend = 0.5;
        public double RollGain = 0.04;
        public double PitchGain = 0.04;
        public bool InvertRoll = false;
        public bool InvertPitch = false;
        public double BlendAlpha = 0.8;
        public int MountMode = 0;
        public double SensorYawOffsetDeg = 0;
        public double RigLengthMm = 862;
        public double RigWidthMm = 748;
        public double StrokeMm = 50;
        public int PostConfig = 4;
        public double OverridePitchDeg = 0;
        public double OverrideRollDeg = 0;
        public bool HasCalibration = false;
        public double RollOffset = 0;
        public double PitchOffset = 0;
    }
}
