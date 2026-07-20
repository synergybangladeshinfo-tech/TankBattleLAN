using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using TankBattle.Core;
using TankBattle.Networking;

namespace TankBattle.Gameplay
{
    /// <summary>One row of the replicated scoreboard.</summary>
    public struct ScoreEntry : INetworkSerializable, IEquatable<ScoreEntry>
    {
        public ulong ClientId;
        public FixedString32Bytes Name;
        public int Kills;
        public int Deaths;
        public int ColorIndex;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref Kills);
            serializer.SerializeValue(ref Deaths);
            serializer.SerializeValue(ref ColorIndex);
        }

        public bool Equals(ScoreEntry other) =>
            ClientId == other.ClientId && Name.Equals(other.Name) &&
            Kills == other.Kills && Deaths == other.Deaths && ColorIndex == other.ColorIndex;
    }

    /// <summary>
    /// In-scene NetworkObject placed in every map scene. The server:
    ///  - spawns one tank per connected player (and for late joiners),
    ///  - runs the 5-minute match timer,
    ///  - keeps the replicated scoreboard up to date,
    ///  - flags the end of the match (clients then show the win screen).
    /// </summary>
    public class MatchManager : NetworkBehaviour
    {
        public static MatchManager Instance { get; private set; }

        /// <summary>Seconds left in the match (server writes, everyone reads).</summary>
        public NetworkVariable<float> TimeRemaining =
            new NetworkVariable<float>(GameConstants.MatchDurationSeconds);

        /// <summary>True once the timer hits zero.</summary>
        public NetworkVariable<bool> MatchEnded = new NetworkVariable<bool>(false);

        /// <summary>Replicated scoreboard (one entry per player).</summary>
        public NetworkList<ScoreEntry> Scores;

        SpawnPoint[] _spawnPoints;
        int _nextColorIndex;

        void Awake()
        {
            Instance = this;
            Scores = new NetworkList<ScoreEntry>();

            // Deterministic spawn point order (sorted by name: Spawn_0..Spawn_3).
            _spawnPoints = FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None);
            Array.Sort(_spawnPoints, (a, b) => string.CompareOrdinal(a.name, b.name));
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            foreach (ulong id in NetworkManager.ConnectedClientsIds)
                SpawnTankFor(id);

            NetworkManager.OnClientConnectedCallback += OnLateJoin;
            NetworkManager.OnClientDisconnectCallback += OnPlayerLeft;
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnLateJoin;
                NetworkManager.OnClientDisconnectCallback -= OnPlayerLeft;
            }
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || MatchEnded.Value) return;

            TimeRemaining.Value = Mathf.Max(0f, TimeRemaining.Value - Time.deltaTime);
            if (TimeRemaining.Value <= 0f)
                MatchEnded.Value = true; // clients react via OnValueChanged
        }

        // ---------------------------------------------------------------- server

        void OnLateJoin(ulong clientId) => SpawnTankFor(clientId);

        void OnPlayerLeft(ulong clientId)
        {
            // NGO despawns the player's objects automatically; drop the score row.
            for (int i = 0; i < Scores.Count; i++)
                if (Scores[i].ClientId == clientId) { Scores.RemoveAt(i); break; }
        }

        void SpawnTankFor(ulong clientId)
        {
            // Guard against double spawn (e.g. host counted twice).
            for (int i = 0; i < Scores.Count; i++)
                if (Scores[i].ClientId == clientId) return;

            int slot = _nextColorIndex++;
            var sp = _spawnPoints.Length > 0
                ? _spawnPoints[slot % _spawnPoints.Length].transform
                : transform;

            GameObject go = Instantiate(ConnectionManager.Instance.TankPrefab,
                                        sp.position, sp.rotation);
            go.GetComponent<TankController>().ColorIndex.Value = slot;
            go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);

            Scores.Add(new ScoreEntry
            {
                ClientId = clientId,
                Name = new FixedString32Bytes(ConnectionManager.Instance.GetPlayerName(clientId)),
                Kills = 0,
                Deaths = 0,
                ColorIndex = slot
            });
        }

        /// <summary>Server: credit a kill (no credit for self-destruction).</summary>
        public void RegisterKill(ulong killerId, ulong victimId)
        {
            if (!IsServer) return;
            for (int i = 0; i < Scores.Count; i++)
            {
                var e = Scores[i];
                bool changed = false;
                if (e.ClientId == killerId && killerId != victimId) { e.Kills++; changed = true; }
                if (e.ClientId == victimId) { e.Deaths++; changed = true; }
                if (changed) Scores[i] = e; // struct list: write back to replicate
            }
        }

        /// <summary>Server: pick a respawn location (random spawn point).</summary>
        public (Vector3, Quaternion) GetRespawnPoint()
        {
            if (_spawnPoints.Length == 0) return (Vector3.up, Quaternion.identity);
            var sp = _spawnPoints[UnityEngine.Random.Range(0, _spawnPoints.Length)].transform;
            return (sp.position, sp.rotation);
        }

        // ---------------------------------------------------------------- client

        /// <summary>Client-side helper: entry with the most kills (ties: fewest deaths).</summary>
        public ScoreEntry GetWinner()
        {
            ScoreEntry best = default;
            bool first = true;
            for (int i = 0; i < Scores.Count; i++)
            {
                var e = Scores[i];
                if (first || e.Kills > best.Kills ||
                    (e.Kills == best.Kills && e.Deaths < best.Deaths))
                {
                    best = e;
                    first = false;
                }
            }
            return best;
        }

        /// <summary>Local player's current kill count (for the HUD).</summary>
        public int GetLocalKills()
        {
            ulong id = NetworkManager.LocalClientId;
            for (int i = 0; i < Scores.Count; i++)
                if (Scores[i].ClientId == id) return Scores[i].Kills;
            return 0;
        }
    }
}
