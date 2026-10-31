using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using CapriHost.Core;

namespace CapriHost.Network
{
    // Một điện thoại đang kết nối.
    internal sealed class ClientSession
    {
        internal readonly SecureChannel Channel;
        internal string DeviceId = "";
        internal string DeviceName = "Android";
        internal string Remote = "";
        internal volatile bool Alive = true;
        internal readonly TcpClient Tcp;

        internal ClientSession(TcpClient tcp, SecureChannel channel) { Tcp = tcp; Channel = channel; }

        // Gửi một gói JSON. Trả về false nếu kết nối đã hỏng.
        internal bool Send(Dictionary<string, object> message)
        {
            if (!Alive) return false;
            try { Channel.Send(Json.Write(message)); return true; }
            catch (IOException) { Alive = false; return false; }
            catch (ObjectDisposedException) { Alive = false; return false; }
            catch (SocketException) { Alive = false; return false; }
        }

        internal void Close()
        {
            Alive = false;
            try { Tcp.Close(); }
            catch (SocketException) { /* socket đã đóng */ }
        }
    }

    // Máy chủ TCP của Capri Host: bắt tay, xác thực, ping/pong, rồi chuyển gói tin cho Hub.
    internal sealed class CapriServer
    {
        private readonly int port;
        private TcpListener listener;
        private Thread acceptThread;
        private volatile bool running;
        private readonly List<ClientSession> sessions = new List<ClientSession>();
        private readonly Dictionary<string, FailureRecord> failures = new Dictionary<string, FailureRecord>();

        internal event Action<ClientSession> Connected;
        internal event Action<ClientSession> Disconnected;
        internal event Action<ClientSession, Dictionary<string, object>> Message;

        private sealed class FailureRecord { internal int Count; internal DateTime Until; }

        internal CapriServer(int port) { this.port = port; }

        internal int Port { get { return port; } }

        internal int ClientCount { get { lock (sessions) { return sessions.Count; } } }

        internal bool Start()
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
            }
            catch (SocketException error)
            {
                Log.Error("Không mở được cổng TCP " + port + " (có thể đã bị chiếm)", error);
                return false;
            }
            running = true;
            acceptThread = new Thread(AcceptLoop);
            acceptThread.IsBackground = true;
            acceptThread.Name = "Capri accept";
            acceptThread.Start();
            Log.Info("Đang lắng nghe cổng TCP " + port);
            return true;
        }

        internal void Stop()
        {
            running = false;
            try { if (listener != null) listener.Stop(); }
            catch (SocketException) { /* đã dừng */ }
            List<ClientSession> copy;
            lock (sessions) copy = new List<ClientSession>(sessions);
            foreach (ClientSession session in copy) session.Close();
        }

        // Gửi cho tất cả điện thoại đang kết nối.
        internal void Broadcast(Dictionary<string, object> message)
        {
            List<ClientSession> copy;
            lock (sessions) copy = new List<ClientSession>(sessions);
            foreach (ClientSession session in copy) session.Send(message);
        }

        private void AcceptLoop()
        {
            while (running)
            {
                TcpClient tcp;
                try { tcp = listener.AcceptTcpClient(); }
                catch (SocketException) { if (!running) return; continue; }
                catch (InvalidOperationException) { return; }
                Thread thread = new Thread(delegate() { Serve(tcp); });
                thread.IsBackground = true;
                thread.Name = "Capri client";
                thread.Start();
            }
        }

        private void Serve(TcpClient tcp)
        {
            string remote = "?";
            ClientSession session = null;
            try
            {
                IPEndPoint endpoint = tcp.Client.RemoteEndPoint as IPEndPoint;
                remote = endpoint == null ? "?" : endpoint.Address.ToString();
                if (Blocked(remote)) { tcp.Close(); return; }

                tcp.NoDelay = true;
                tcp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                NetworkStream stream = tcp.GetStream();
                stream.ReadTimeout = Timeouts.TcpHandshakeMs;      // giai đoạn bắt tay
                stream.WriteTimeout = Timeouts.TcpWriteMs;
                SecureChannel channel = new SecureChannel(stream);
                session = new ClientSession(tcp, channel);
                session.Remote = remote;

                if (!Handshake(session, channel, remote)) return;

                stream.ReadTimeout = Timeouts.TcpIdleMs;      // điện thoại ping mỗi 10 giây; im lặng quá lâu là coi như mất kết nối
                lock (sessions) sessions.Add(session);
                Action<ClientSession> connected = Connected;
                if (connected != null) connected(session);

                while (running && session.Alive)
                {
                    string text = channel.Read();
                    Dictionary<string, object> message = Json.ReadObject(text);
                    if (message == null) continue;
                    string type = Json.Str(message, "t", "");
                    if (type == "ping") { session.Send(Json.D("t", "pong")); continue; }
                    Action<ClientSession, Dictionary<string, object>> handler = Message;
                    if (handler == null) continue;
                    ClientSession captured = session;
                    Dictionary<string, object> captured2 = message;
                    ThreadPool.QueueUserWorkItem(delegate(object state)
                    {
                        try { handler(captured, captured2); }
                        catch (Exception error)
                        {
                            // Không bao giờ nuốt lỗi: ghi log kèm stack trace VÀ báo lại cho điện thoại (nếu gói có id) để màn hình không treo.
                            string packetId = Json.Str(captured2, "id", "");
                            Log.Error("Lệnh", "Lỗi xử lý gói '" + Json.Str(captured2, "t", "?") + "'", error, packetId);
                            if (packetId.Length > 0)
                                captured.Send(Json.D("t", "reply", "id", packetId, "error", true,
                                    "text", "Host gặp lỗi khi xử lý lệnh: " + error.GetType().Name + " - " + error.Message + ". Xem chi tiết trong chat 'Nhật ký lỗi'."));
                        }
                    });
                }
            }
            catch (AuthException error) { Log.Warn("Mạng", "Từ chối " + remote + ": " + error.Message, null); Fail(remote); }
            catch (IOException error) { Log.Info("Mạng", "Ngắt kết nối " + remote + ": " + error.Message, null); }
            catch (SocketException error) { Log.Info("Mạng", "Lỗi socket " + remote + ": " + error.Message, null); }
            catch (ObjectDisposedException) { /* phiên đã đóng chủ động */ }
            catch (Exception error) { Log.Error("Mạng", "Lỗi kết nối " + remote, error, null); }
            finally
            {
                if (session != null)
                {
                    bool was;
                    lock (sessions) was = sessions.Remove(session);
                    session.Close();
                    Action<ClientSession> gone = Disconnected;
                    if (was && gone != null) gone(session);
                }
                else
                {
                    try { tcp.Close(); }
                    catch (SocketException) { /* socket đã đóng */ }
                }
            }
        }

        // Trả về true nếu bắt tay và xác thực thành công.
        private bool Handshake(ClientSession session, SecureChannel channel, string remote)
        {
            Dictionary<string, object> hello = Json.ReadObject(channel.ReadPlain());
            if (hello == null || Json.Str(hello, "t", "") != "hello") return false;

            string id = Json.Str(hello, "id", "");
            string name = Json.Str(hello, "name", "Android");
            string mode = Json.Str(hello, "mode", "token");
            byte[] clientNonce;
            try { clientNonce = Convert.FromBase64String(Json.Str(hello, "cn", "")); }
            catch (FormatException) { return false; }
            if (id.Length == 0 || id.Length > 64 || clientNonce.Length != 16) return false;
            if (name.Length > 60) name = name.Substring(0, 60);

            byte[] secret = null;
            bool known = false;
            if (mode == "token")
            {
                PairedDevice device = Settings.Current.FindDevice(id);
                if (device != null) { secret = device.Token; known = true; }
            }
            else if (mode == "code")
            {
                secret = Pairing.Secret;
                if (secret == null)
                {
                    channel.SendPlain(Json.Write(Json.D("t", "error", "code", "pairing_off", "msg", "Host chưa bật chế độ ghép đôi")));
                    return false;
                }
            }

            byte[] serverNonce = new byte[16];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(serverNonce);
            channel.SendPlain(Json.Write(Json.D("t", "hello_ok", "v", Settings.ProtocolVersion, "sn", Convert.ToBase64String(serverNonce),
                "host", Environment.MachineName, "known", known || mode == "code")));
            if (secret == null) return false;     // thiết bị lạ trong chế độ token: client sẽ báo cần ghép đôi

            channel.Arm(secret, clientNonce, serverNonce);
            Dictionary<string, object> auth = Json.ReadObject(channel.Read());   // sai khóa => AuthException
            if (auth == null || Json.Str(auth, "t", "") != "auth") throw new AuthException("Không có gói auth");

            session.DeviceId = id;
            session.DeviceName = name;
            if (mode == "code")
            {
                byte[] token = new byte[32];
                using (RandomNumberGenerator rng = RandomNumberGenerator.Create()) rng.GetBytes(token);
                Settings.Current.AddDevice(id, name, token);
                session.Send(Json.D("t", "paired", "token", Convert.ToBase64String(token)));
                Pairing.Complete(name);
                Log.Info("Đã ghép đôi với " + name);
            }
            session.Send(Json.D("t", "ready", "host", Environment.MachineName, "version", Settings.Version, "v", Settings.ProtocolVersion));
            return true;
        }

        // ---- chống dò mã: sai 5 lần thì khóa IP đó 5 phút ----

        private bool Blocked(string remote)
        {
            lock (failures)
            {
                FailureRecord record;
                return failures.TryGetValue(remote, out record) && record.Count >= 5 && DateTime.UtcNow < record.Until;
            }
        }

        private void Fail(string remote)
        {
            lock (failures)
            {
                FailureRecord record;
                if (!failures.TryGetValue(remote, out record) || DateTime.UtcNow > record.Until) { record = new FailureRecord(); failures[remote] = record; }
                record.Count++;
                record.Until = DateTime.UtcNow.AddMinutes(5);
            }
        }
    }
}
