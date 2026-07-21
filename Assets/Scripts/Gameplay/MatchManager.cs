using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using TankBattle.Core;
using TankBattle.Networking;

namespace TankBattle.Gameplay
{
    /// <summary>One row of the replicated scoreboard.</summary>
    public struct ScoreEntry : INetworkSerializable, IEquatable<ScoreEntry>
    {
        public ulong ClientId;   // real client id, or a fake bot id (>= BotIdBase)
        public FixedString32Bytes Name;
        public int Kills;
        public int Deaths;
        public int Score;       // mode points: KOTH zone-seconds, GunGame tier progress
        public int Team;        // 0 = Team A (blue), 1 = Team B (red), -1 = no team
        public int ColorIndex;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref Kills);
            serializer.SerializeValue(ref Deaths);
            serializer.SerializeValue(ref Score);
            serializer.SerializeValue(ref Team);
            serializer.SerializeValue(ref ColorIndex);
        }

        public bool Equals(ScoreEntry other) =>
            ClientId == other.ClientId && Name.Equals(other.Name) &&
            Kills == other.Kills && Deaths == other.Deaths &&
            Score == other.Score && Team == other.Team && ColorIndex == other.ColorIndex;
    }

    /// <summary>
    /// In-scene NetworkObject placed in every map scene. The server:
    ///  - spawns one tank per player ONLY after that client has finished
    ///    loading the map (spawning earlier is exactly what made joiners
    ///    unable to play: their tank arrived during the scene switch and was
    ///    destroyed client-side with the old scene),
    ///  - spawns AI bot tanks in solo mode,
    ///  - runs the match timer and the per-mode rules (5 game modes),
    ///  - spawns/respawns weapon pickups,
    ///  - keeps the replicated scoreboard up to date,
    ///  - flags the end of the match (clients then show the win screen).
    /// </summary>
    public class MatchManager : NetworkBehaviour
    {
        public static MatchManager Instance { get; private set; }

        /// <summary>Seconds left in the match (server writes, everyone reads).</summary>
        public NetworkVariable<float> TimeRemaining = new NetworkVariable<float>(300f);

        /// <summary>True once the match is decided (timer or instant win).</summary>
        public NetworkVariable<bool> MatchEnded = new NetworkVariable<bool>(false);

        /// <summary>Active game mode (cast to GameMode). Set by the server at spawn.</summary>
        public NetworkVariable<int> Mode = new NetworkVariable<int>(0);

        /// <summary>Team kill totals for Team Deathmatch.</summary>
        public NetworkVariable<int> TeamAScore = new NetworkVariable<int>(0);
        public NetworkVariable<int> TeamBScore = new NetworkVariable<int>(0);

        /// <summary>Replicated scoreboard (one entry per player/bot).</summary>
        public NetworkList<ScoreEntry> Scores;

        public GameMode CurrentMode => (GameMode)Mode.Value;

        SpawnPoint[] _spawnPoints;
        PickupPoint[] _pickupPoints;
        int _nextSlot;
        bool _pickupsSpawned, _botsSpawned;
        readonly Dictionary<ulong, float> _kothAccum = new Dictionary<ulong, float>();

        void Awake()
        {
            Instance = this;
            Scores = new NetworkList<ScoreEntry>();

            // Deterministic spawn point order (sorted by name: Spawn_0..Spawn_7).
            _spawnPoints = FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None);
            Array.Sort(_spawnPoints, (a, b) => string.CompareOrdinal(a.name, b.name));
            _pickupPoints = FindObjectsByType<PickupPoint>(FindObjectsSortMode.None);
            Array.Sort(_pickupPoints, (a, b) => string.CompareOrdinal(a.name, b.name));
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            // Host-chosen rules for this match.
            Mode.Value = GameSession.SelectedModeIndex;
            int t = Mathf.Clamp(GameSession.SelectedTimeIndex, 0,
                                GameConstants.MatchDurations.Length - 1);
            TimeRemaining.Value = GameConstants.MatchDurations[t];

            // CRITICAL FIX: only spawn a player's tank after THAT client has
            // finished loading this scene. OnLoadComplete fires per client for
            // a normal scene switch; OnSynchronizeComplete fires for late
            // joiners once they have fully synced into the running match.
            NetworkManager.SceneManager.OnLoadComplete += OnClientSceneLoaded;
            NetworkManager.SceneManager.OnSynchronizeComplete += OnClientSynchronized;
            NetworkManager.OnClientDisconnectCallback += OnPlayerLeft;

            // Safety net: if a load-complete event was missed for any reason,
            // make sure every connected client gets a tank a moment later
            // (SpawnTankFor is guarded against duplicates).
            StartCoroutine(SpawnFallback());
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null)
            {
                if (NetworkManager.SceneManager != null)
                {
                    NetworkManager.SceneManager.OnLoadComplete -= OnClientSceneLoaded;
                    NetworkManager.SceneManager.OnSynchronizeComplete -= OnClientSynchronized;
                }
                NetworkManager.OnClientDisconnectCallback -= OnPlayerLeft;
            }
        }

        void Update()
        {
            if (!IsServer || !IsSpawned || MatchEnded.Value) return;

            TimeRemaining.Value = Mathf.Max(0f, TimeRemaining.Value - Time.deltaTime);
            if (TimeRemaining.Value <= 0f) { MatchEnded.Value = true; return; }

            if (CurrentMode == GameMode.KingOfTheHill) TickKingOfTheHill();
        }

        // ---------------------------------------------------------------- server

        void OnClientSceneLoaded(ulong clientId, string sceneName, LoadSceneMode mode)
        {
            if (sceneName != gameObject.scene.name) return;
            SpawnTankFor(clientId);
            OnWorldReady();
        }

        /// <summary>Late joiner finished syncing the running match -> give them a tank.</summary>
        void OnClientSynchronized(ulong clientId)
        {
            SpawnTankFor(clientId);
        }

        IEnumerator SpawnFallback()
        {
            yield return new WaitForSeconds(4f);
            if (!IsSpawned || MatchEnded.Value) yield break;
            foreach (ulong id in NetworkManager.ConnectedClientsIds)
                SpawnTankFor(id); // duplicate-guarded
            OnWorldReady();
        }

        /// <summary>Once the first tank exists, add crates + bots (exactly once).</summary>
        void OnWorldReady()
        {
            // Weapon crates in every mode except Gun Game (it manages weapons).
            if (!_pickupsSpawned && CurrentMode != GameMode.GunGame)
            {
                _pickupsSpawned = true;
                for (int i = 0; i < _pickupPoints.Length; i++)
                    SpawnPickup(i);
            }

            if (!_botsSpawned && GameSession.SoloMode)
            {
                _botsSpawned = true;
                SpawnBots();
            }
        }

        void OnPlayerLeft(ulong clientId)
        {
            // NGO despawns the player's objects automatically; drop the score row.
            for (int i = 0; i < Scores.Count; i++)
                if (Scores[i].ClientId == clientId) { Scores.RemoveAt(i); break; }
            _kothAccum.Remove(clientId);
            CheckLastTankVictory();
        }

        /// <summary>Balanced team assignment for Team Deathmatch.</summary>
        int PickTeam()
        {
            if (CurrentMode != GameMode.TeamDeathmatch) return -1;
            int a = 0, b = 0;
            for (int i = 0; i < Scores.Count; i++)
                { if (Scores[i].Team == 0) a++; else if (Scores[i].Team == 1) b++; }
            return a <= b ? 0 : 1;
        }

        void SpawnTankFor(ulong clientId)
        {
            // Guard against double spawn (e.g. fallback after load event).
            for (int i = 0; i < Scores.Count; i++)
                if (Scores[i].ClientId == clientId) return;

            var info = ConnectionManager.Instance.GetPlayerInfo(clientId);
            int slot = _nextSlot++;
            int team = PickTeam();

            // Team modes force the team color so sides are obvious at a glance.
            int color = team >= 0 ? team : info.ColorIndex;

            var sp = _spawnPoints.Length > 0
                ? _spawnPoints[slot % _spawnPoints.Length].transform
                : transform;

            GameObject go = Instantiate(ConnectionManager.Instance.TankPrefab,
                                        sp.position, sp.rotation);
            var tc = go.GetComponent<TankController>();
            tc.ColorIndex.Value = color;
            tc.StyleIndex.Value = info.StyleIndex;
            tc.TeamIndex.Value = team;
            tc.PlayerName.Value = new FixedString32Bytes(info.Name);
            go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);

            // Gun Game: everyone starts on tier 0 with infinite ammo.
            if (CurrentMode == GameMode.GunGame)
                ApplyGunGameWeapon(clientId, 0);

            Scores.Add(new ScoreEntry
            {
                ClientId = clientId,
                Name = new FixedString32Bytes(info.Name),
                Kills = 0,
                Deaths = 0,
                Score = 0,
                Team = team,
                ColorIndex = color
            });
        }

        /// <summary>Solo mode: fill the arena with AI-driven tanks.</summary>
        void SpawnBots()
        {
            for (int i = 0; i < GameConstants.BotCount; i++)
            {
                ulong botId = GameConstants.BotIdBase + (ulong)i;
                int slot = _nextSlot++;
                int team = PickTeam();
                int color = team >= 0 ? team : (i + 2) % GameConstants.PlayerColors.Length;
                string name = GameConstants.BotNames[i % GameConstants.BotNames.Length];

                var sp = _spawnPoints.Length > 0
                    ? _spawnPoints[slot % _spawnPoints.Length].transform
                    : transform;

                GameObject go = Instantiate(ConnectionManager.Instance.TankPrefab,
                                            sp.position, sp.rotation);
                var tc = go.GetComponent<TankController>();
                tc.ColorIndex.Value = color;
                tc.StyleIndex.Value = i % GameConstants.TankStyleNames.Length;
                tc.TeamIndex.Value = team;
                tc.PlayerName.Value = new FixedString32Bytes(name);

                // The AI brain: must be attached BEFORE Spawn so Awake caches refs.
                var bot = go.AddComponent<BotTank>();
                bot.BotId = botId;
                bot.Team = team;

                go.GetComponent<NetworkObject>().Spawn(true); // server-owned

                Scores.Add(new ScoreEntry
                {
                    ClientId = botId,
                    Name = new FixedString32Bytes(name),
                    Kills = 0,
                    Deaths = 0,
                    Score = 0,
                    Team = team,
                    ColorIndex = color
                });
            }
        }

        /// <summary>Server: credit a kill and run per-mode win checks.</summary>
        public void RegisterKill(ulong killerId, ulong victimId)
        {
            if (!IsServer || MatchEnded.Value) return;

            int killerKills = 0, killerTeam = -1;
            for (int i = 0; i < Scores.Count; i++)
            {
                var e = Scores[i];
                bool changed = false;
                if (e.ClientId == killerId && killerId != victimId)
                {
                    e.Kills++;
                    killerKills = e.Kills;
                    killerTeam = e.Team;
                    changed = true;
                }
                if (e.ClientId == victimId) { e.Deaths++; changed = true; }
                if (changed) Scores[i] = e; // struct list: write back to replicate
            }

            switch (CurrentMode)
            {
                case GameMode.TeamDeathmatch:
                    if (killerTeam == 0) TeamAScore.Value++;
                    else if (killerTeam == 1) TeamBScore.Value++;
                    break;

                case GameMode.GunGame:
                    if (killerId != victimId && killerKills > 0)
                    {
                        int tier = killerKills / GameConstants.GunGameKillsPerTier;
                        if (killerKills >= GameConstants.GunGameKillsPerTier *
                                           Weapons.GunGameOrder.Length)
                        {
                            MatchEnded.Value = true; // completed every tier
                        }
                        else
                        {
                            ApplyGunGameWeapon(killerId, tier);
                            SetScore(killerId, tier);
                        }
                    }
                    break;

                case GameMode.LastTankStanding:
                    CheckLastTankVictory();
                    break;
            }
        }

        void SetScore(ulong actorId, int score)
        {
            for (int i = 0; i < Scores.Count; i++)
                if (Scores[i].ClientId == actorId)
                {
                    var e = Scores[i]; e.Score = score; Scores[i] = e;
                    break;
                }
        }

        /// <summary>Server: hand the tier weapon (infinite ammo) to a Gun Game player.</summary>
        void ApplyGunGameWeapon(ulong clientId, int tier)
        {
            tier = Mathf.Clamp(tier, 0, Weapons.GunGameOrder.Length - 1);
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client)) return;
            var po = client.PlayerObject;
            if (po == null) return;
            var shooting = po.GetComponent<TankShooting>();
            if (shooting != null)
                shooting.ServerSetWeapon((int)Weapons.GunGameOrder[tier], -1);
        }

        /// <summary>Server: Last Tank Standing - is anyone but one still in?</summary>
        void CheckLastTankVictory()
        {
            if (CurrentMode != GameMode.LastTankStanding || MatchEnded.Value) return;
            if (Scores.Count < 2) return; // solo practice: never insta-end

            int stillIn = 0;
            for (int i = 0; i < Scores.Count; i++)
                if (Scores[i].Deaths < GameConstants.LastTankLives) stillIn++;
            if (stillIn <= 1) MatchEnded.Value = true;
        }

        /// <summary>Server: may this actor respawn? (LTS players can run out of lives).</summary>
        public bool AllowRespawn(ulong actorId)
        {
            if (CurrentMode != GameMode.LastTankStanding) return true;
            for (int i = 0; i < Scores.Count; i++)
                if (Scores[i].ClientId == actorId)
                    return Scores[i].Deaths < GameConstants.LastTankLives;
            return true;
        }

        /// <summary>Server: King of the Hill scoring - 1 point per second in the zone.</summary>
        void TickKingOfTheHill()
        {
            var zone = KothZone.Instance;
            if (zone == null) return;
            Vector3 c = zone.transform.position;

            // Iterate the global registry so AI bots contest the zone too.
            for (int t = 0; t < TankHealth.All.Count; t++)
            {
                var h = TankHealth.All[t];
                if (h == null || h.IsDead.Value) continue;

                Vector3 p = h.transform.position;
                p.y = c.y;
                if (Vector3.Distance(p, c) > GameConstants.KothZoneRadius) continue;

                ulong id = h.ActorId;
                _kothAccum.TryGetValue(id, out float acc);
                acc += Time.deltaTime;
                if (acc >= 1f)
                {
                    acc -= 1f;
                    for (int i = 0; i < Scores.Count; i++)
                        if (Scores[i].ClientId == id)
                        {
                            var e = Scores[i]; e.Score++; Scores[i] = e;
                            if (e.Score >= GameConstants.KothWinScore)
                                MatchEnded.Value = true;
                            break;
                        }
                }
                _kothAccum[id] = acc;
            }
        }

        /// <summary>Server: team of an actor (-1 outside Team Deathmatch).</summary>
        public int GetTeam(ulong actorId)
        {
            for (int i = 0; i < Scores.Count; i++)
                if (Scores[i].ClientId == actorId) return Scores[i].Team;
            return -1;
        }

        /// <summary>Server: pick a respawn location (random spawn point).</summary>
        public (Vector3, Quaternion) GetRespawnPoint()
        {
            if (_spawnPoints.Length == 0) return (Vector3.up, Quaternion.identity);
            var sp = _spawnPoints[UnityEngine.Random.Range(0, _spawnPoints.Length)].transform;
            return (sp.position, sp.rotation);
        }

        // --------------------------------------------------------------- pickups

        void SpawnPickup(int pointIndex)
        {
            var prefab = ConnectionManager.Instance.PickupPrefab;
            if (prefab == null || pointIndex >= _pickupPoints.Length) return;

            var p = _pickupPoints[pointIndex].transform;
            GameObject go = Instantiate(prefab, p.position + Vector3.up * 1.1f, p.rotation);
            var pickup = go.GetComponent<WeaponPickup>();
            pickup.Type.Value = (int)Weapons.RandomPickup();
            pickup.PointIndex = pointIndex;
            go.GetComponent<NetworkObject>().Spawn(true);
        }

        /// <summary>Server: a crate was collected - respawn one there after a delay.</summary>
        public void OnPickupTaken(int pointIndex)
        {
            if (IsServer && IsSpawned) StartCoroutine(RespawnPickup(pointIndex));
        }

        IEnumerator RespawnPickup(int pointIndex)
        {
            yield return new WaitForSeconds(GameConstants.PickupRespawnSeconds);
            if (IsSpawned && !MatchEnded.Value) SpawnPickup(pointIndex);
        }

        // ---------------------------------------------------------------- client

        int Metric(ScoreEntry e) => CurrentMode == GameMode.KingOfTheHill ? e.Score : e.Kills;

        /// <summary>Best entry under the current mode's metric.</summary>
        public ScoreEntry GetWinner()
        {
            ScoreEntry best = default;
            bool first = true;
            for (int i = 0; i < Scores.Count; i++)
            {
                var e = Scores[i];

                // LTS: anyone still holding lives beats anyone who is out.
                if (CurrentMode == GameMode.LastTankStanding && !first)
                {
                    bool eIn = e.Deaths < GameConstants.LastTankLives;
                    bool bIn = best.Deaths < GameConstants.LastTankLives;
                    if (eIn != bIn) { if (eIn) best = e; continue; }
                }

                if (first || Metric(e) > Metric(best) ||
                    (Metric(e) == Metric(best) && e.Deaths < best.Deaths))
                {
                    best = e;
                    first = false;
                }
            }
            return best;
        }

        /// <summary>Client: title line for the win screen ("VICTORY!", "x WINS!", draw...).</summary>
        public string GetWinnerTitle(ulong localClientId)
        {
            if (CurrentMode == GameMode.TeamDeathmatch)
            {
                if (TeamAScore.Value == TeamBScore.Value) return "DRAW!";
                return TeamAScore.Value > TeamBScore.Value
                    ? "BLUE TEAM WINS!" : "RED TEAM WINS!";
            }

            // Detect an exact draw between the top two players.
            var winner = GetWinner();
            int top = 0, m = Metric(winner);
            for (int i = 0; i < Scores.Count; i++)
                if (Metric(Scores[i]) == m && Scores[i].Deaths == winner.Deaths) top++;
            if (Scores.Count > 1 && top > 1 && CurrentMode != GameMode.LastTankStanding)
                return "DRAW!";

            return winner.ClientId == localClientId ? "VICTORY!" : $"{winner.Name}  WINS!";
        }

        /// <summary>Local player's scoreboard row (default entry if missing).</summary>
        public ScoreEntry GetLocalEntry()
        {
            ulong id = NetworkManager.LocalClientId;
            for (int i = 0; i < Scores.Count; i++)
                if (Scores[i].ClientId == id) return Scores[i];
            return default;
        }

        /// <summary>Local player's current kill count (for the HUD).</summary>
        public int GetLocalKills() => GetLocalEntry().Kills;
    }
}
