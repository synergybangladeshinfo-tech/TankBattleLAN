using UnityEngine;

namespace TankBattle.Core
{
    /// <summary>The five selectable game modes.</summary>
    public enum GameMode
    {
        Deathmatch = 0,       // classic free-for-all, most kills wins
        TeamDeathmatch = 1,   // two teams, most team kills wins
        LastTankStanding = 2, // 3 lives each, last tank alive wins
        KingOfTheHill = 3,    // hold the centre zone to earn points
        GunGame = 4           // every 2 kills upgrades your weapon; finish all tiers
    }

    /// <summary>
    /// Central place for every tunable constant shared across the project.
    /// Keeping them here avoids "magic numbers" scattered through scripts.
    /// </summary>
    public static class GameConstants
    {
        // ---- Networking ----
        public const ushort GamePort = 7777;        // Unity Transport game traffic
        public const int DiscoveryPort = 47777;     // UDP LAN discovery
        public const int MaxPlayers = 16;
        public const string DiscoveryProbe = "TBLAN_DISCOVER_V1";   // client -> broadcast
        public const string DiscoveryReplyPrefix = "TBLAN_HOST_V1"; // host  -> client

        // ---- Match rules ----
        public const int MaxHealth = 100;
        public const float RespawnDelay = 3f;

        /// <summary>Host-selectable match lengths (seconds), index-aligned with the labels.</summary>
        public static readonly float[] MatchDurations = { 120f, 300f, 600f, 900f };
        public static readonly string[] MatchDurationLabels = { "2 MIN", "5 MIN", "10 MIN", "15 MIN" };
        public const int DefaultDurationIndex = 1; // 5 minutes

        // ---- Mode tuning ----
        public const int LastTankLives = 3;        // lives in Last Tank Standing
        public const float KothZoneRadius = 7f;    // metres, King of the Hill zone
        public const int KothWinScore = 100;       // zone-seconds needed to win instantly
        public const int GunGameKillsPerTier = 2;  // kills to advance one weapon tier
        public const float PickupRespawnSeconds = 12f;

        // ---- Shield pickup (invincibility) ----
        public const float ShieldSeconds = 120f;    // 2-minute invincibility
        public const int ShieldPickupId = 999;      // special WeaponPickup.Type value
        public static readonly Color ShieldColor = new Color(0.25f, 0.85f, 1f); // cyan

        // ---- Solo mode (vs AI bots) ----
        public const int BotCount = 5;
        public const ulong BotIdBase = 9000; // fake client ids for bot score rows
        public static readonly string[] BotNames =
        { "BOT Rex", "BOT Max", "BOT Zed", "BOT Ivy", "BOT Ace" };

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

        /// <summary>UI names for the game modes, index-aligned with the GameMode enum.</summary>
        public static readonly string[] GameModeNames =
        {
            "DEATHMATCH",
            "TEAM BATTLE",
            "LAST TANK",
            "KING OF THE HILL",
            "GUN GAME"
        };

        /// <summary>One-line description of each mode for the host screen.</summary>
        public static readonly string[] GameModeHints =
        {
            "Free for all - most kills wins",
            "Two teams - most team kills wins",
            "3 lives each - survive to the end",
            "Hold the glowing zone to score",
            "Every 2 kills upgrades your weapon"
        };

        // ---- Tank customization ----

        /// <summary>Selectable tank colors (Garage). Index 0/1 double as the team colors.</summary>
        public static readonly Color[] PlayerColors =
        {
            new Color(0.20f, 0.55f, 1.00f), // blue   (Team A)
            new Color(1.00f, 0.30f, 0.25f), // red    (Team B)
            new Color(0.30f, 0.85f, 0.35f), // green
            new Color(1.00f, 0.85f, 0.20f), // yellow
            new Color(0.75f, 0.40f, 1.00f), // purple
            new Color(1.00f, 0.55f, 0.15f), // orange
            new Color(0.25f, 0.90f, 0.85f), // teal
            new Color(0.95f, 0.95f, 0.95f)  // white
        };

        public static readonly string[] PlayerColorNames =
        { "BLUE", "RED", "GREEN", "YELLOW", "PURPLE", "ORANGE", "TEAL", "WHITE" };

        /// <summary>Tank body styles built into the tank prefab (Hull_0..Hull_2).</summary>
        public static readonly string[] TankStyleNames = { "STANDARD", "HEAVY", "SCOUT" };

        /// <summary>Selectable hull camo patterns (Resources/Patterns/*).</summary>
        public static readonly string[] TankPatternNames = { "PLAIN", "CAMO", "HEX", "STRIPE" };
        public static readonly string[] TankPatternFiles = { "Plain", "Camo", "Hex", "Stripe" };

        /// <summary>Per-style stat bars for the Garage (0..1): speed, armor, agility.</summary>
        public static readonly Vector3[] TankStyleStats =
        {
            new Vector3(0.70f, 0.60f, 0.65f), // Standard - balanced
            new Vector3(0.50f, 1.00f, 0.40f), // Heavy    - tanky, slow
            new Vector3(1.00f, 0.35f, 0.95f)  // Scout    - fast, fragile
        };

        // ---- Mini-Militia-style abilities ----
        public const float DashSpeedMultiplier = 2.6f;
        public const float DashDuration = 0.35f;
        public const float DashCooldown = 4.5f;
        public const int RamDamage = 35;            // damage from dashing into an enemy
        public const float GrenadeCooldown = 5f;
        public const int GrenadeDamage = 55;
        public const float GrenadeSplashRadius = 5.5f;
        public const float GrenadeFuse = 1.5f;      // seconds before it blows

        /// <summary>Per-style (moveSpeed, turnSpeed) multipliers - small, fair differences.</summary>
        public static readonly Vector2[] TankStyleSpeed =
        {
            new Vector2(1.00f, 1.00f), // Standard - balanced
            new Vector2(0.85f, 0.90f), // Heavy    - slower, feels weighty
            new Vector2(1.15f, 1.10f)  // Scout    - nimble
        };

        public static Color GetPlayerColor(int index)
        {
            if (index < 0) index = 0;
            return PlayerColors[index % PlayerColors.Length];
        }
    }
}
