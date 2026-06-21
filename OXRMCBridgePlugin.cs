using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Ports;
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

        // Mode: 0=TELEMETRY, 1=SENSOR, 2=BLENDED
        private int _modeOverride = 0;

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
            this.AttachDelegate("OXRMCBridge.MaxPitchDeg", () => GetMaxPitchDeg());
            this.AttachDelegate("OXRMCBridge.MaxRollDeg", () => GetMaxRollDeg());
            this.AttachDelegate("OXRMCBridge.RigLengthMm", () => _rigLengthMm);
            this.AttachDelegate("OXRMCBridge.RigWidthMm", () => _rigWidthMm);
            this.AttachDelegate("OXRMCBridge.StrokeMm", () => _strokeMm);
            this.AttachDelegate("OXRMCBridge.PostConfig", () => _postConfig);

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

        private static readonly string[] MODE_NAMES = new string[] { "TELEMETRY", "SENSOR", "BLENDED" };

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
            _modeOverride = (_modeOverride + 1) % 3;
        }
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

        public void AdjustRollGain(double delta) { _rollGain = Math.Max(0, Math.Min(0.2, _rollGain + delta)); }
        public void AdjustPitchGain(double delta) { _pitchGain = Math.Max(0, Math.Min(0.2, _pitchGain + delta)); }
        public void AdjustBlendAlpha(double delta) { _blendAlpha = Math.Max(0, Math.Min(1.0, _blendAlpha + delta)); }

        // Rig dimension accessors
        public double GetRigLengthMm() { return _rigLengthMm; }
        public double GetRigWidthMm() { return _rigWidthMm; }
        public double GetStrokeMm() { return _strokeMm; }
        public int GetPostConfig() { return _postConfig; }

        public void AdjustRigLength(double delta) { _rigLengthMm = Math.Max(100, Math.Min(3000, _rigLengthMm + delta)); }
        public void AdjustRigWidth(double delta) { _rigWidthMm = Math.Max(100, Math.Min(3000, _rigWidthMm + delta)); }
        public void AdjustStroke(double delta) { _strokeMm = Math.Max(5, Math.Min(500, _strokeMm + delta)); }
        public void SetRigLength(double val) { _rigLengthMm = Math.Max(100, Math.Min(3000, val)); }
        public void SetRigWidth(double val) { _rigWidthMm = Math.Max(100, Math.Min(3000, val)); }
        public void SetStroke(double val) { _strokeMm = Math.Max(5, Math.Min(500, val)); }
        public void CyclePostConfig() { _postConfig = _postConfig == 3 ? 4 : 3; }

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
        public void SetOverridePitch(double val) { _overridePitchDeg = Math.Max(0, Math.Min(30, val)); }
        public void SetOverrideRoll(double val) { _overrideRollDeg = Math.Max(0, Math.Min(30, val)); }
        public void ToggleInvertRoll() { _invertRoll = !_invertRoll; }
        public void ToggleInvertPitch() { _invertPitch = !_invertPitch; }
        public void CalibrateSensor() { _needsCalibration = true; }

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
                                }

                                lock (_sensorLock)
                                {
                                    _sensorRoll = rollDeg - _rollOffset;
                                    _sensorPitch = pitchDeg - _pitchOffset;
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

            if (effectiveMode == "BLENDED")
            {
                if (!sensorActive || !_gameRunning)
                {
                    // Fallback if forced blended but missing a source
                    if (sensorActive) effectiveMode = "SENSOR";
                    else if (_gameRunning) effectiveMode = "TELEMETRY";
                    else return;
                }
            }

            if (effectiveMode == "BLENDED")
            {
                double sr, sp;
                lock (_sensorLock) { sr = _sensorRoll; sp = _sensorPitch; }
                double sensorRollRad = sr * Math.PI / 180.0;
                double sensorPitchRad = sp * Math.PI / 180.0;

                rollRad = _blendAlpha * sensorRollRad + (1.0 - _blendAlpha) * _smoothedRoll;
                pitchRad = _blendAlpha * sensorPitchRad + (1.0 - _blendAlpha) * _smoothedPitch;
            }
            else if (effectiveMode == "SENSOR")
            {
                if (!sensorActive) return;
                double sr, sp;
                lock (_sensorLock) { sr = _sensorRoll; sp = _sensorPitch; }
                rollRad = sr * Math.PI / 180.0;
                pitchRad = sp * Math.PI / 180.0;
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

        public void End(PluginManager pluginManager)
        {
            SimHub.Logging.Current.Info("OXRMCBridge: stopping");
            StopSensor();
            if (_accessor != null) { try { _accessor.Dispose(); } catch (Exception) { } }
            if (_mmf != null) { try { _mmf.Dispose(); } catch (Exception) { } }
        }
    }
}
