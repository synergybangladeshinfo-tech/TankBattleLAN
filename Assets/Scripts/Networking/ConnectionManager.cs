using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using TankBattle.Core;

namespace TankBattle.Networking
{
    /// <summary>Per-player info sent by every client in its connect payload.</summary>
    public struct PlayerInfo
    {
        public string Name;
        public int ColorIndex;  // Garage color choice
        public int StyleIndex;  // Garage body style choice
    }

    /// <summary>
    /// Central wrapper around Netcode for GameObjects: starting/stopping
    /// host and client, connection approval (player limit + player identity),
    /// and returning to the main menu on disconnect.
    ///
    /// Lives on the same GameObject as the NetworkManager (kept alive across
    /// scene loads by NGO itself).
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class ConnectionManager : MonoBehaviour
    {
        public static ConnectionManager Instance { get; private set; }

        [Header("Network prefabs (assigned by the setup tool)")]
        [SerializeField] GameObject tankPrefab;
        [SerializeField] GameObject bulletPrefab;
        [SerializeField] GameObject pickupPrefab;

        public GameObject TankPrefab => tankPrefab;
        public GameObject BulletPrefab => bulletPrefab;
        public GameObject PickupPrefab => pickupPrefab;

        NetworkManager _nm;
        UnityTransport _transport;

        /// <summary>Server-side map of clientId -> identity (from the connect payload).</summary>
        readonly Dictionary<ulong, PlayerInfo> _players = new Dictionary<ulong, PlayerInfo>();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _nm = GetComponent<NetworkManager>();
            _transport = GetComponent<UnityTransport>();
        }

        void Start()
        {
            // Guard: a duplicate created by a scene reload is being destroyed.
            if (Instance != this || _nm == null) return;

            // Register dynamically-spawned prefabs on both host and clients
            // (must happen before StartHost / StartClient on every peer).
            if (tankPrefab != null) _nm.AddNetworkPrefab(tankPrefab);
            if (bulletPrefab != null) _nm.AddNetworkPrefab(bulletPrefab);
            if (pickupPrefab != null) _nm.AddNetworkPrefab(pickupPrefab);

            _nm.ConnectionApprovalCallback = ApprovalCheck;
            _nm.OnClientDisconnectCallback += OnClientDisconnect;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_nm != null) _nm.OnClientDisconnectCallback -= OnClientDisconnect;
        }

        /// <summary>"name|colorIndex|styleIndex" payload for the local player.</summary>
        static byte[] LocalPayload() => Encoding.UTF8.GetBytes(
            $"{GameSession.PlayerName}|{GameSession.TankColorIndex}|{GameSession.TankStyleIndex}");

        // ------------------------------------------------------------------ host

        /// <summary>
        /// Start hosting on all interfaces. advertise = false (solo mode) keeps
        /// the session invisible to LAN discovery, and ApprovalCheck also
        /// rejects joiners while GameSession.SoloMode is set.
        /// </summary>
        public bool StartHost(bool advertise = true)
        {
            _players.Clear();
            _nm.NetworkConfig.ConnectionApproval = true;
            _nm.NetworkConfig.ConnectionData = LocalPayload();
            _transport.SetConnectionData("0.0.0.0", GameConstants.GamePort, "0.0.0.0");

            if (!_nm.StartHost())
            {
                Debug.LogError("[ConnectionManager] StartHost failed.");
                return false;
            }

            GameSession.IsHost = true;
            _players[_nm.LocalClientId] = new PlayerInfo
            {
                Name = GameSession.PlayerName,
                ColorIndex = GameSession.TankColorIndex,
                StyleIndex = GameSession.TankStyleIndex
            };

            // Answer discovery probes with live lobby info (not in solo mode).
            var discovery = GetComponent<LanDiscovery>();
            if (discovery != null && advertise)
            {
                discovery.ServerInfoProvider = () => (
                    GameSession.PlayerName,
                    GameConstants.MapDisplayNames[GameSession.SelectedMapIndex],
                    _nm.ConnectedClientsIds.Count);
                discovery.StartServer();
            }
            return true;
        }

        // ---------------------------------------------------------------- client

        /// <summary>Connect to a discovered host.</summary>
        public bool StartClient(string address, ushort port)
        {
            _nm.NetworkConfig.ConnectionApproval = true;
            _nm.NetworkConfig.ConnectionData = LocalPayload();
            _transport.SetConnectionData(address, port);

            GameSession.IsHost = false;
            if (!_nm.StartClient())
            {
                Debug.LogError("[ConnectionManager] StartClient failed.");
                return false;
            }
            return true;
        }

        // -------------------------------------------------------------- approval

        /// <summary>
        /// Server-side gate for incoming connections: enforce the player limit
        /// and record the player's identity (sent as the connect payload).
        /// Tanks are NOT auto-spawned here - MatchManager spawns them per map.
        /// </summary>
        void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request,
                           NetworkManager.ConnectionApprovalResponse response)
        {
            // Solo sessions are private (the host itself is always approved).
            if (GameSession.SoloMode && request.ClientNetworkId != _nm.LocalClientId)
            {
                response.Approved = false;
                response.Reason = "Solo match";
                return;
            }

            if (_nm.ConnectedClientsIds.Count >= GameConstants.MaxPlayers)
            {
                response.Approved = false;
                response.Reason = "Match is full";
                return;
            }

            var info = new PlayerInfo { Name = "Player", ColorIndex = 0, StyleIndex = 0 };
            if (request.Payload != null && request.Payload.Length > 0)
            {
                string raw = Encoding.UTF8.GetString(request.Payload);
                string[] parts = raw.Split('|');
                if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                {
                    info.Name = parts[0].Trim();
                    if (info.Name.Length > 16) info.Name = info.Name.Substring(0, 16);
                }
                if (parts.Length > 1 && int.TryParse(parts[1], out int c))
                    info.ColorIndex = Mathf.Clamp(c, 0, GameConstants.PlayerColors.Length - 1);
                if (parts.Length > 2 && int.TryParse(parts[2], out int s))
                    info.StyleIndex = Mathf.Clamp(s, 0, GameConstants.TankStyleNames.Length - 1);
            }
            _players[request.ClientNetworkId] = info;

            response.Approved = true;
            response.CreatePlayerObject = false; // spawned manually by MatchManager
        }

        /// <summary>Server-side lookup of a player's identity.</summary>
        public PlayerInfo GetPlayerInfo(ulong clientId)
            => _players.TryGetValue(clientId, out var i)
               ? i : new PlayerInfo { Name = $"Player {clientId}", ColorIndex = 0, StyleIndex = 0 };

        /// <summary>Server-side lookup of a player's display name.</summary>
        public string GetPlayerName(ulong clientId) => GetPlayerInfo(clientId).Name;

        // ------------------------------------------------------------ disconnect

        void OnClientDisconnect(ulong clientId)
        {
            if (_nm.IsServer)
            {
                _players.Remove(clientId);
                return;
            }

            // We are a pure client and lost the connection (host quit, network drop,
            // or our own connection was rejected) -> back to the menu.
            if (clientId == _nm.LocalClientId)
            {
                string reason = string.IsNullOrEmpty(_nm.DisconnectReason)
                    ? "Disconnected from host" : _nm.DisconnectReason;
                Leave(reason);
            }
        }

        /// <summary>
        /// Shut down networking and return to the local main menu.
        /// Safe to call from any state.
        /// </summary>
        public void Leave(string notice = null)
        {
            GameSession.MenuNotice = notice;
            GameSession.IsHost = false;
            GameSession.SoloMode = false;

            var discovery = GetComponent<LanDiscovery>();
            if (discovery != null) { discovery.StopServer(); discovery.StopSearch(); }

            if (_nm.IsListening) _nm.Shutdown();

            Time.timeScale = 1f;
            SceneManager.LoadScene(GameConstants.MainMenuScene);
        }
    }
}
