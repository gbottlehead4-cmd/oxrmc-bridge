using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.IO.MemoryMappedFiles;

namespace OxrmcSigmaBridge
{
    // Standalone, sensor-free Sigma -> OXRMC motion-compensation bridge.
    //
    // Reads the Sigma motion software's commanded pitch/roll from its stream on the
    // local rig network and writes the pose into OXRMC's "motionRigPose" flypt
    // shared-memory file, at full rate, drift-free, with NO sensor hardware. It only
    // reads its own machine's traffic and never modifies any Sigma software.
    // OXRMC's own in-game calibration (CTRL+DEL) sets the zero; its per-axis invert
    // fixes polarity. Uses a raw socket, so it MUST run elevated.
    //
    // Independent interop tool — not affiliated with or endorsed by Sigma Integrale.
    //
    // Build:  bridge\build.bat   (in-box .NET Framework csc, no SDK)
    // Run  :  SigmaOxrmcBridge.exe        (elevated; Ctrl+C to stop)
    //         SigmaOxrmcBridge.exe 2222 192.168.153.1   (force port / adapter)
    internal static class SigmaOxrmcBridge
    {
        // rad per raw count, from the sensor correlation (negative = polarity;
        // OXRMC invert can flip it if a driven axis compensates the wrong way).
        private const double PitchRadPerCount = -6.25e-11;
        private const double RollRadPerCount  = -6.21e-11;
        private const int PoseFrameLen = 97;   // the 60 Hz motion frame
        private const int PitchOff = 8;
        private const int RollOff = 12;

        // OXRMC flypt MMF: 6 LE doubles [sway,surge,heave,yaw,roll,pitch] (m / rad).
        private const string MmfName = "motionRigPose";

        private static void Main(string[] args)
        {
            int port = args.Length > 0 ? int.Parse(args[0]) : 2222;
            string bindIp = args.Length > 1 ? args[1] : AutoBindIp();
            // Optional exaggeration for on/off testing: set env BRIDGE_GAIN=4 to
            // amplify compensation so it's unmistakable. Leave unset (=1) for real use.
            double gain = 1.0;
            string g = Environment.GetEnvironmentVariable("BRIDGE_GAIN");
            if (g != null) double.TryParse(g, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out gain);
            if (gain == 0) gain = 1.0;
            if (bindIp == null)
            {
                Console.Error.WriteLine("No 192.168.153.x adapter found (the 'Sigma Motion Controller' NIC).");
                Console.Error.WriteLine("Pass its IPv4 as arg 2:  SigmaOxrmcBridge.exe 2222 <ip>");
                return;
            }

            Socket sock;
            try
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
                sock.Bind(new IPEndPoint(IPAddress.Parse(bindIp), 0));
                sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);
                sock.IOControl(IOControlCode.ReceiveAll, new byte[] { 1, 0, 0, 0 }, new byte[4]);
            }
            catch (SocketException ex)
            {
                Console.Error.WriteLine("Raw socket failed (" + ex.Message + "). Run elevated (as administrator).");
                return;
            }

            using (var mmf = MemoryMappedFile.CreateOrOpen(MmfName, 256))
            using (var view = mmf.CreateViewAccessor(0, 6 * sizeof(double)))
            {
                Console.WriteLine("Sigma->OXRMC bridge live on " + bindIp + " :" + port +
                                  "  ->  " + MmfName + "   gain=" + gain + "  (Ctrl+C to stop)");
                byte[] buf = new byte[65535];
                long frames = 0, lastReport = Environment.TickCount;
                double lastPitch = 0, lastRoll = 0;

                while (true)
                {
                    int n = sock.Receive(buf);
                    if (n < 28 || (buf[0] >> 4) != 4 || buf[9] != 17) continue; // IPv4 UDP
                    int ihl = (buf[0] & 0x0F) * 4;
                    if (ihl + 8 > n) continue;
                    int dstPort = (buf[ihl + 2] << 8) | buf[ihl + 3];
                    if (dstPort != port) continue;

                    int payOff = ihl + 8;
                    int payLen = n - payOff;
                    if (payLen < PoseFrameLen) continue;              // not the motion frame
                    if (buf[payOff] != 0x02) continue;                // frame type 0x02

                    int pitchRaw = BitConverter.ToInt32(buf, payOff + PitchOff);
                    int rollRaw  = BitConverter.ToInt32(buf, payOff + RollOff);
                    lastPitch = pitchRaw * PitchRadPerCount * gain;
                    lastRoll  = rollRaw * RollRadPerCount * gain;

                    // [sway,surge,heave,yaw,roll,pitch]; we drive roll+pitch only.
                    view.Write(4 * sizeof(double), lastRoll);
                    view.Write(5 * sizeof(double), lastPitch);

                    if (Environment.TickCount - lastReport > 2000)
                    {
                        Console.WriteLine(string.Format("{0} frames  roll={1,7:F3} deg  pitch={2,7:F3} deg",
                            frames, lastRoll * 180.0 / Math.PI, lastPitch * 180.0 / Math.PI));
                        lastReport = Environment.TickCount;
                    }
                    frames++;
                }
            }
        }

        private static string AutoBindIp()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                        ua.Address.ToString().StartsWith("192.168.153."))
                        return ua.Address.ToString();
            return null;
        }
    }
}
