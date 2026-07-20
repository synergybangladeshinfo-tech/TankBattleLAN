using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using TankBattle.Core;

namespace TankBattle.Networking
{
    /// <summary>Info about a host discovered on the LAN.</summary>
    public class DiscoveredHost
    {
        public string Address;      // IPv4 of the host
        public ushort GamePort;     // Unity Transport port to connect to
        public string HostName;     // host player's display name
        public string MapName;      // display name of the selected map
        public int PlayerCount;
        public int MaxPlayers;
        public float LastSeen;      // Time.realtimeSinceStartup of last reply
    }

    /// <summary>
    /// UDP broadcast based LAN discovery. Works fully offline (Wi-Fi hotspot or router).
    ///
    /// Host side:   listens on GameConstants.DiscoveryPort and answers probe datagrams
    ///              with a pipe-separated info string.
    /// Client side: periodically broadcasts a probe to 255.255.255.255 and to every
    ///              interface's directed broadcast address (more reliable on Android),
    ///              then collects replies into a thread-safe list the UI can poll.
    /// </summary>
    public class LanDiscovery : MonoBehaviour
    {
        public static LanDiscovery Instance { get; private set; }

        UdpClient _serverUdp;
        UdpClient _clientUdp;
        CancellationTokenSource _serverCts;
        CancellationTokenSource _clientCts;

        // Written from background threads, drained on the main thread.
        readonly ConcurrentQueue<DiscoveredHost> _incoming = new ConcurrentQueue<DiscoveredHost>();

        // Main-thread list of currently visible hosts (deduped by address).
        readonly List<DiscoveredHost> _hosts = new List<DiscoveredHost>();

        /// <summary>Delegate the host uses to report live lobby info (name, map, player count).</summary>
        public Func<(string hostName, string mapName, int players)> ServerInfoProvider;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            StopServer();
            StopSearch();
        }

        void Update()
        {
            // Merge background-thread discoveries into the main-thread list.
            while (_incoming.TryDequeue(out var host))
            {
                var existing = _hosts.Find(h => h.Address == host.Address);
                if (existing != null)
                {
                    existing.HostName = host.HostName;
                    existing.MapName = host.MapName;
                    existing.PlayerCount = host.PlayerCount;
                    existing.LastSeen = Time.realtimeSinceStartup;
                }
                else
                {
                    host.LastSeen = Time.realtimeSinceStartup;
                    _hosts.Add(host);
                }
            }
            // Drop hosts we haven't heard from in a while.
            _hosts.RemoveAll(h => Time.realtimeSinceStartup - h.LastSeen > 4f);
        }

        /// <summary>Snapshot of hosts currently visible on the LAN (UI polls this).</summary>
        public IReadOnlyList<DiscoveredHost> Hosts => _hosts;

        // ------------------------------------------------------------------ host

        /// <summary>Start answering discovery probes. Call after StartHost().</summary>
        public void StartServer()
        {
            StopServer();
            _serverCts = new CancellationTokenSource();
            var token = _serverCts.Token;

            try
            {
                _serverUdp = new UdpClient();
                _serverUdp.ExclusiveAddressUse = false;
                _serverUdp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _serverUdp.Client.Bind(new IPEndPoint(IPAddress.Any, GameConstants.DiscoveryPort));
                _serverUdp.EnableBroadcast = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LanDiscovery] Could not bind discovery port: {e.Message}");
                return;
            }

            Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = _serverUdp.Receive(ref remote); // blocking
                        string msg = Encoding.UTF8.GetString(data);
                        if (msg != GameConstants.DiscoveryProbe) continue;

                        // Build reply with live lobby info.
                        var info = ServerInfoProvider != null
                            ? ServerInfoProvider()
                            : ("Host", "Unknown", 1);
                        string reply = string.Join("|",
                            GameConstants.DiscoveryReplyPrefix,
                            Sanitize(info.Item1),
                            Sanitize(info.Item2),
                            info.Item3.ToString(),
                            GameConstants.MaxPlayers.ToString(),
                            GameConstants.GamePort.ToString());
                        byte[] replyBytes = Encoding.UTF8.GetBytes(reply);
                        _serverUdp.Send(replyBytes, replyBytes.Length, remote);
                    }
                    catch (SocketException) { /* socket closed on stop */ }
                    catch (ObjectDisposedException) { break; }
                    catch (Exception e) { Debug.LogWarning($"[LanDiscovery] server loop: {e.Message}"); }
                }
            }, token);

            Debug.Log("[LanDiscovery] Discovery server started.");
        }

        public void StopServer()
        {
            _serverCts?.Cancel();
            _serverCts = null;
            _serverUdp?.Close();
            _serverUdp = null;
        }

        // ---------------------------------------------------------------- client

        /// <summary>Start broadcasting probes and collecting host replies.</summary>
        public void StartSearch()
        {
            StopSearch();
            _hosts.Clear();
            _clientCts = new CancellationTokenSource();
            var token = _clientCts.Token;

            try
            {
                _clientUdp = new UdpClient(); // ephemeral port
                _clientUdp.EnableBroadcast = true;
                _clientUdp.Client.ReceiveTimeout = 1000;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LanDiscovery] Could not open client socket: {e.Message}");
                return;
            }

            byte[] probe = Encoding.UTF8.GetBytes(GameConstants.DiscoveryProbe);

            // Sender loop: probe every second on all broadcast addresses.
            Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        foreach (var addr in GetBroadcastAddresses())
                            _clientUdp.Send(probe, probe.Length, new IPEndPoint(addr, GameConstants.DiscoveryPort));
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (Exception) { /* interface may be down; keep trying */ }
                    Thread.Sleep(1000);
                }
            }, token);

            // Receiver loop: parse replies.
            Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = _clientUdp.Receive(ref remote);
                        string msg = Encoding.UTF8.GetString(data);
                        string[] parts = msg.Split('|');
                        if (parts.Length < 6 || parts[0] != GameConstants.DiscoveryReplyPrefix) continue;

                        _incoming.Enqueue(new DiscoveredHost
                        {
                            Address = remote.Address.ToString(),
                            HostName = parts[1],
                            MapName = parts[2],
                            PlayerCount = int.TryParse(parts[3], out var pc) ? pc : 1,
                            MaxPlayers = int.TryParse(parts[4], out var mp) ? mp : GameConstants.MaxPlayers,
                            GamePort = ushort.TryParse(parts[5], out var gp) ? gp : GameConstants.GamePort
                        });
                    }
                    catch (SocketException) { /* receive timeout - normal */ }
                    catch (ObjectDisposedException) { break; }
                    catch (Exception e) { Debug.LogWarning($"[LanDiscovery] client loop: {e.Message}"); }
                }
            }, token);

            Debug.Log("[LanDiscovery] Searching for hosts...");
        }

        public void StopSearch()
        {
            _clientCts?.Cancel();
            _clientCts = null;
            _clientUdp?.Close();
            _clientUdp = null;
        }

        // ----------------------------------------------------------------- utils

        /// <summary>
        /// Returns 255.255.255.255 plus the directed broadcast address of every
        /// active IPv4 interface (e.g. 192.168.43.255 on a hotspot). Directed
        /// broadcasts are delivered more reliably on some Android devices.
        /// </summary>
        static List<IPAddress> GetBroadcastAddresses()
        {
            var result = new List<IPAddress> { IPAddress.Broadcast };
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        byte[] ip = ua.Address.GetAddressBytes();
                        byte[] mask = ua.IPv4Mask?.GetAddressBytes();
                        if (mask == null) continue;
                        byte[] bc = new byte[4];
                        for (int i = 0; i < 4; i++)
                            bc[i] = (byte)(ip[i] | ~mask[i]);
                        result.Add(new IPAddress(bc));
                    }
                }
            }
            catch (Exception) { /* fall back to global broadcast only */ }
            return result;
        }

        /// <summary>Strip the separator character so names can't break the protocol.</summary>
        static string Sanitize(string s) => string.IsNullOrEmpty(s) ? "?" : s.Replace("|", "/");
    }
}
