using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace OxrmcTools
{
    // Promiscuous raw-socket capture of the Sigma SRTA UDP stream to the motion
    // controller (default port 2222 on the 192.168.153.0/24 link). Uses a Windows
    // raw socket with SIO_RCVALL, so NO npcap/Wireshark driver is required - but it
    // MUST run elevated (Run as administrator), or the socket call throws.
    //
    // Logs: unix_ms, src ip:port, dst ip:port, payload length, payload hex.
    // Pair its timestamps with SensorLogger's CSV, then run correlate.py.
    //
    // Usage:  SrtaSniffer.exe [port] [localBindIp] [out.csv]
    //   port omitted     -> 2222
    //   localBindIp omitted -> auto-detect a 192.168.153.x adapter on this PC
    //   out.csv omitted  -> srta_<timestamp>.csv
    // Ctrl+C to stop.
    internal static class SrtaSniffer
    {
        private static void Main(string[] args)
        {
            int port = args.Length > 0 ? int.Parse(args[0]) : 2222;
            string bindIp = args.Length > 1 ? args[1] : AutoBindIp();
            string outPath = args.Length > 2 ? args[2] : "srta_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
            if (bindIp == null)
            {
                Console.Error.WriteLine("No 192.168.153.x adapter found. Run `ipconfig`, find the adapter on the rig's");
                Console.Error.WriteLine("subnet, and pass its IPv4 address as arg 2:  SrtaSniffer.exe 2222 <thatIp>");
                return;
            }

            Console.WriteLine("Sniffing UDP :" + port + " on " + bindIp + "  ->  " + outPath);
            Console.WriteLine("(must be elevated; Ctrl+C to stop)");

            Socket sock;
            try
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
                sock.Bind(new IPEndPoint(IPAddress.Parse(bindIp), 0));
                sock.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);
                sock.IOControl(IOControlCode.ReceiveAll, new byte[] { 1, 0, 0, 0 }, new byte[4]); // SIO_RCVALL
            }
            catch (SocketException ex)
            {
                Console.Error.WriteLine("Raw socket failed (" + ex.Message + "). Are you running elevated?");
                return;
            }

            using (var w = new StreamWriter(outPath))
            {
                w.WriteLine("unix_ms,src,dst,len,payload_hex");
                w.Flush();
                byte[] buf = new byte[65535];
                long count = 0, lastReport = Environment.TickCount;
                while (true)
                {
                    int n = sock.Receive(buf);
                    if (n < 28) continue;                 // IPv4(20) + UDP(8) minimum
                    if ((buf[0] >> 4) != 4) continue;     // IPv4 only
                    if (buf[9] != 17) continue;           // protocol 17 = UDP
                    int ihl = (buf[0] & 0x0F) * 4;
                    if (ihl + 8 > n) continue;
                    int srcPort = (buf[ihl] << 8) | buf[ihl + 1];
                    int dstPort = (buf[ihl + 2] << 8) | buf[ihl + 3];
                    if (srcPort != port && dstPort != port) continue;

                    int payLen = ((buf[ihl + 4] << 8) | buf[ihl + 5]) - 8;
                    if (payLen < 0 || ihl + 8 + payLen > n) payLen = n - ihl - 8;

                    string src = buf[12] + "." + buf[13] + "." + buf[14] + "." + buf[15] + ":" + srcPort;
                    string dst = buf[16] + "." + buf[17] + "." + buf[18] + "." + buf[19] + ":" + dstPort;
                    var sb = new StringBuilder(payLen * 2);
                    for (int i = 0; i < payLen; i++) sb.Append(buf[ihl + 8 + i].ToString("x2"));

                    long ms = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                    w.WriteLine(ms + "," + src + "," + dst + "," + payLen + "," + sb);
                    if ((++count % 10) == 0) w.Flush();

                    if (Environment.TickCount - lastReport > 2000)
                    {
                        Console.WriteLine(count + " pkts captured (last payload " + payLen + " bytes)");
                        lastReport = Environment.TickCount;
                    }
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
