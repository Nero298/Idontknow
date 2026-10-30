using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using CapriHost.Core;

namespace CapriHost.Network
{
    // Trả lời gói UDP broadcast "CAPRI_DISCOVER" để app Android tự tìm thấy Host trong mạng LAN.
    internal sealed class Discovery
    {
        private readonly int tcpPort;
        private UdpClient udp;
        private Thread thread;
        private volatile bool running;

        internal Discovery(int tcpPort) { this.tcpPort = tcpPort; }

        internal int UdpPort { get { return tcpPort + 1; } }

        internal void Start()
        {
            try
            {
                udp = new UdpClient(AddressFamily.InterNetwork);
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, UdpPort));
            }
            catch (SocketException error)
            {
                Log.Error("Không mở được cổng UDP dò tìm " + UdpPort, error);
                return;
            }
            running = true;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "Capri discovery";
            thread.Start();
        }

        internal void Stop()
        {
            running = false;
            try { if (udp != null) udp.Close(); }
            catch (SocketException) { }
        }

        private void Loop()
        {
            while (running)
            {
                try
                {
                    IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = udp.Receive(ref from);
                    string text = Encoding.UTF8.GetString(data).Trim();
                    if (text != "CAPRI_DISCOVER") continue;
                    byte[] reply = Encoding.UTF8.GetBytes("CAPRI_HOST|" + Environment.MachineName + "|" + tcpPort);
                    udp.Send(reply, reply.Length, from);
                }
                catch (SocketException) { if (!running) return; }
                catch (ObjectDisposedException) { return; }
            }
        }

        // Địa chỉ IPv4 của máy trong LAN, dùng để hiển thị "nhập địa chỉ này vào điện thoại".
        internal static List<string> LocalAddresses()
        {
            List<string> found = new List<string>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (info.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = info.Address.ToString();
                        if (!ip.StartsWith("169.254.", StringComparison.Ordinal)) found.Add(ip);
                    }
                }
            }
            catch (NetworkInformationException) { }
            return found;
        }
    }
}
