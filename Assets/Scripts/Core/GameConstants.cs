using UnityEngine;

namespace TankBattle.Core
{
    /// <summary>
    /// Central place for every tunable constant shared across the project.
    /// Keeping them here avoids "magic numbers" scattered through scripts.
    /// </summary>
    public static class GameConstants
    {
        // ---- Networking ----
        public const ushort GamePort = 7777;        // Unity Transport game traffic
        public const int DiscoveryPort = 47777;     // UDP LAN discovery
        public const int MaxPlayers = 4;
        public const string DiscoveryProbe = "TBLAN_DISCOVER_V1";   // client -> broadcast
        public const string DiscoveryReplyPrefix = "TBLAN_HOST_V1"; // host  -> client

        // ---- Match rules ----
        public const float MatchDurationSeconds = 300f; // 5 minute match
        public const int MaxHealth = 100;
        public const int BulletDamage = 25;
        public const float RespawnDelay = 3f;

        // ---- Scenes ----
        public const string MainMenuScene = "MainMenu";

        /// <summary>Scene names of the five playable maps (must match built scenes).</summary>
        public static readonly string[] MapScenes =
        {
            "Map01_Arena",
            "Map02_Crossfire",
            "Map03_Maze",
            "Map04_Pillars",
            "Map05_Fortress"
        };

        /// <summary>Human friendly names shown in the UI, index-aligned with MapScenes.</summary>
        public static readonly string[] MapDisplayNames =
        {
            "Open Arena",
            "Crossfire",
            "The Maze",
            "Pillar Field",
            "Fortress"
        };

        /// <summary>Per-player tank colors (index = join order).</summary>
        public static readonly Color[] PlayerColors =
        {
            new Color(0.20f, 0.55f, 1.00f), // blue
            new Color(1.00f, 0.30f, 0.25f), // red
            new Color(0.30f, 0.85f, 0.35f), // green
            new Color(1.00f, 0.85f, 0.20f)  // yellow
        };

        public static Color GetPlayerColor(int index)
        {
            if (index < 0) index = 0;
            return PlayerColors[index % PlayerColors.Length];
        }
    }
}
