using System;
using System.Globalization;
using System.IO;
using System.IO.Ports;

namespace OxrmcTools
{
    // Logs WitMotion WT901C roll/pitch/yaw + high-res UTC timestamp to CSV.
    // This is the "answer key" for decoding the Sigma SRTA stream: known physical
    // angles captured at the same wall-clock time as the packet capture, so we can
    // match a sensor angle against the bytes that move with it.
    //
    // Usage:  SensorLogger.exe [COMx] [out.csv]
    //   COM port omitted -> auto-detect by scanning for 0x55 0x53 frames @9600 baud.
    //   out.csv omitted   -> sensor_<timestamp>.csv
    // Ctrl+C to stop. Each row is flushed immediately. Frame parsing matches the
    // plugin's SensorLoop (0x55 0x53, 11 bytes, checksum, int16 LE, /32768*180).
    internal static class SensorLogger
    {
        private const int Baud = 9600;

        private static void Main(string[] args)
        {
            string port = args.Length > 0 ? args[0] : DetectPort();
            string outPath = args.Length > 1 ? args[1] : "sensor_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
            if (port == null) { Console.Error.WriteLine("No WitMotion sensor found (scanned @9600 for 0x55 0x53)."); return; }

            Console.WriteLine("Sensor on " + port + "  ->  " + outPath + "   (Ctrl+C to stop)");
            using (var sp = new SerialPort(port, Baud, Parity.None, 8, StopBits.One))
            using (var w = new StreamWriter(outPath))
            {
                sp.ReadTimeout = 1000;
                sp.Open();
                w.WriteLine("unix_ms,roll_deg,pitch_deg,yaw_deg");
                w.Flush();
                byte[] ring = new byte[64];
                int pos = 0;
                while (true)
                {
                    int b;
                    try { b = sp.ReadByte(); } catch (TimeoutException) { continue; }
                    ring[pos % 64] = (byte)b; pos++;
                    if (pos < 11) continue;
                    int s = (pos - 11) % 64;
                    if (ring[s] != 0x55 || ring[(s + 1) % 64] != 0x53) continue;
                    int sum = 0; for (int i = 0; i < 10; i++) sum += ring[(s + i) % 64];
                    if ((byte)(sum & 0xFF) != ring[(s + 10) % 64]) continue;

                    short rr = (short)(ring[(s + 2) % 64] | (ring[(s + 3) % 64] << 8));
                    short rp = (short)(ring[(s + 4) % 64] | (ring[(s + 5) % 64] << 8));
                    short ry = (short)(ring[(s + 6) % 64] | (ring[(s + 7) % 64] << 8));
                    double roll = rr / 32768.0 * 180.0;
                    double pitch = rp / 32768.0 * 180.0;
                    double yaw = ry / 32768.0 * 180.0;

                    var ci = CultureInfo.InvariantCulture;
                    w.WriteLine(NowMs().ToString(ci) + "," +
                                roll.ToString("F3", ci) + "," +
                                pitch.ToString("F3", ci) + "," +
                                yaw.ToString("F3", ci));
                    w.Flush();
                }
            }
        }

        private static long NowMs()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        private static string DetectPort()
        {
            foreach (string p in SerialPort.GetPortNames())
            {
                try
                {
                    using (var sp = new SerialPort(p, Baud, Parity.None, 8, StopBits.One))
                    {
                        sp.ReadTimeout = 500; sp.Open();
                        long deadline = Environment.TickCount + 1000;
                        int prev = -1;
                        while (Environment.TickCount < deadline)
                        {
                            int b; try { b = sp.ReadByte(); } catch (TimeoutException) { break; }
                            if (prev == 0x55 && b == 0x53) return p;
                            prev = b;
                        }
                    }
                }
                catch { }
            }
            return null;
        }
    }
}
