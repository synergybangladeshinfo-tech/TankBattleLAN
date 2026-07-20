using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace TankBattle.Networking
{
    /// <summary>
    /// In-scene NetworkObject placed in the MainMenu scene. Replicates the list
    /// of connected player names so every phone's lobby screen stays in sync.
    /// The server rebuilds the list on every connect/disconnect.
    /// </summary>
    public class LobbyState : NetworkBehaviour
    {
        public static LobbyState Instance { get; private set; }

        /// <summary>Replicated list of player display names (join order).</summary>
        public NetworkList<FixedString32Bytes> PlayerNames;

        void Awake()
        {
            Instance = this;
            PlayerNames = new NetworkList<FixedString32Bytes>();
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                Rebuild();
                NetworkManager.OnClientConnectedCallback += OnClientsChanged;
                NetworkManager.OnClientDisconnectCallback += OnClientsChanged;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientsChanged;
                NetworkManager.OnClientDisconnectCallback -= OnClientsChanged;
            }
        }

        void OnClientsChanged(ulong _) => Rebuild();

        void Rebuild()
        {
            PlayerNames.Clear();
            foreach (ulong id in NetworkManager.ConnectedClientsIds)
                PlayerNames.Add(new FixedString32Bytes(ConnectionManager.Instance.GetPlayerName(id)));
        }
    }
}
