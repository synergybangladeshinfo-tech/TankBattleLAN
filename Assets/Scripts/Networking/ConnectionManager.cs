using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using TankBattle.Core;

namespace TankBattle.Networking
{
    /// <summary>
    /// Central wrapper around Netcode for GameObjects: starting/stopping
    /// host and client, connection approval (player limit + player names),
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

        public GameObject TankPrefab => tankPrefab;
        public GameObject BulletPrefab => bulletPrefab;

        NetworkManager _nm;
        UnityTransport _transport;

        /// <summary>Server-side map of clientId -> display name (from the connect payload).</summary>
        readonly Dictionary<ulong, string> _playerNames = new Dictionary<ulong, string>();

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

            _nm.ConnectionApprovalCallback = ApprovalCheck;
            _nm.OnClientDisconnectCallback += OnClientDisconnect;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_nm != null) _nm.OnClientDisconnectCallback -= OnClientDisconnect;
        }

        // ------------------------------------------------------------------ host

        /// <summary>Start hosting on all interfaces and begin answering LAN discovery.</summary>
        public bool StartHost()
        {
            _playerNames.Clear();
            _nm.NetworkConfig.ConnectionApproval = true;
            _nm.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(GameSession.PlayerName);
            _transport.SetConnectionData("0.0.0.0", GameConstants.GamePort, "0.0.0.0");

            if (!_nm.StartHost())
            {
                Debug.LogError("[ConnectionManager] StartHost failed.");
                return false;
            }

            GameSession.IsHost = true;
            _playerNames[_nm.LocalClientId] = GameSession.PlayerName;

            // Answer discovery probes with live lobby info.
            var discovery = GetComponent<LanDiscovery>();
            if (discovery != null)
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
            _nm.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(GameSession.PlayerName);
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
        /// and record the player's display name (sent as the connect payload).
        /// Tanks are NOT auto-spawned here - MatchManager spawns them per map.
        /// </summary>
        void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request,
                           NetworkManager.ConnectionApprovalResponse response)
        {
            if (_nm.ConnectedClientsIds.Count >= GameConstants.MaxPlayers)
            {
                response.Approved = false;
                response.Reason = "Match is full";
                return;
            }

            string name = "Player";
            if (request.Payload != null && request.Payload.Length > 0)
            {
                name = Encoding.UTF8.GetString(request.Payload);
                if (name.Length > 16) name = name.Substring(0, 16);
            }
            _playerNames[request.ClientNetworkId] = name;

            response.Approved = true;
            response.CreatePlayerObject = false; // spawned manually by MatchManager
        }

        /// <summary>Server-side lookup of a player's display name.</summary>
        public string GetPlayerName(ulong clientId)
            => _playerNames.TryGetValue(clientId, out var n) ? n : $"Player {clientId}";

        // ------------------------------------------------------------ disconnect

        void OnClientDisconnect(ulong clientId)
        {
            if (_nm.IsServer)
            {
                _playerNames.Remove(clientId);
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

            var discovery = GetComponent<LanDiscovery>();
            if (discovery != null) { discovery.StopServer(); discovery.StopSearch(); }

            if (_nm.IsListening) _nm.Shutdown();

            Time.timeScale = 1f;
            SceneManager.LoadScene(GameConstants.MainMenuScene);
        }
    }
}
