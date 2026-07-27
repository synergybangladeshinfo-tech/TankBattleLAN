using UnityEngine;

namespace TankBattle.Core
{
    /// <summary>
    /// Persistent player progression: XP earned from matches, the level it maps
    /// to, a rank title, and which garage items that level has unlocked.
    /// Everything is local (PlayerPrefs) - the game is offline/LAN only, so
    /// there is no account or server to sync with.
    /// </summary>
    public static class PlayerProgress
    {
        const string KeyXp = "tb_xp";

        /// <summary>XP rewards.</summary>
        public const int XpPerKill = 10;
        public const int XpMatchPlayed = 20;
        public const int XpWin = 50;

        public const int MaxLevel = 30;

        /// <summary>Rank title per level band (index = (level-1) / 3, capped).</summary>
        public static readonly string[] RankNames =
        {
            "RECRUIT", "PRIVATE", "CORPORAL", "SERGEANT", "LIEUTENANT",
            "CAPTAIN", "MAJOR", "COLONEL", "GENERAL", "LEGEND"
        };

        /// <summary>Level at which each hull pattern unlocks (index-aligned with TankPatternNames).</summary>
        public static readonly int[] PatternUnlockLevel = { 1, 3, 6, 10 };

        /// <summary>Level at which each body style unlocks (index-aligned with TankStyleNames).</summary>
        public static readonly int[] StyleUnlockLevel = { 1, 4, 8 };

        /// <summary>Total XP ever earned.</summary>
        public static int Xp
        {
            get => PlayerPrefs.GetInt(KeyXp, 0);
            private set { PlayerPrefs.SetInt(KeyXp, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        /// <summary>XP needed to go from 'level' to the next one (grows steadily).</summary>
        public static int XpToAdvance(int level) => 100 + (Mathf.Max(1, level) - 1) * 50;

        /// <summary>Total XP required to have reached 'level' (level 1 = 0).</summary>
        public static int XpForLevel(int level)
        {
            int total = 0;
            for (int l = 1; l < Mathf.Clamp(level, 1, MaxLevel); l++) total += XpToAdvance(l);
            return total;
        }

        /// <summary>Current level, 1..MaxLevel.</summary>
        public static int Level
        {
            get
            {
                int xp = Xp, level = 1;
                while (level < MaxLevel && xp >= XpForLevel(level + 1)) level++;
                return level;
            }
        }

        public static string Rank => RankName(Level);

        public static string RankName(int level)
        {
            int band = Mathf.Clamp((Mathf.Max(1, level) - 1) / 3, 0, RankNames.Length - 1);
            return RankNames[band];
        }

        /// <summary>Progress through the current level, 0..1 (1 at max level).</summary>
        public static float LevelProgress
        {
            get
            {
                int level = Level;
                if (level >= MaxLevel) return 1f;
                int start = XpForLevel(level);
                int need = XpToAdvance(level);
                return need <= 0 ? 1f : Mathf.Clamp01((Xp - start) / (float)need);
            }
        }

        /// <summary>XP still needed for the next level (0 at max level).</summary>
        public static int XpToNextLevel
        {
            get
            {
                int level = Level;
                return level >= MaxLevel ? 0 : Mathf.Max(0, XpForLevel(level + 1) - Xp);
            }
        }

        /// <summary>
        /// Award XP for a finished match. Returns how many levels were gained so
        /// the win screen can celebrate a level-up.
        /// </summary>
        public static int AwardMatch(int kills, bool won, out int gainedXp)
        {
            int before = Level;
            gainedXp = XpMatchPlayed + kills * XpPerKill + (won ? XpWin : 0);
            Xp += gainedXp;
            return Mathf.Max(0, Level - before);
        }

        public static bool IsPatternUnlocked(int index)
        {
            if (index < 0 || index >= PatternUnlockLevel.Length) return true;
            return Level >= PatternUnlockLevel[index];
        }

        public static bool IsStyleUnlocked(int index)
        {
            if (index < 0 || index >= StyleUnlockLevel.Length) return true;
            return Level >= StyleUnlockLevel[index];
        }

        /// <summary>Level required for a pattern (0 when it is always available).</summary>
        public static int PatternLevel(int index) =>
            index >= 0 && index < PatternUnlockLevel.Length ? PatternUnlockLevel[index] : 1;

        public static int StyleLevel(int index) =>
            index >= 0 && index < StyleUnlockLevel.Length ? StyleUnlockLevel[index] : 1;
    }
}
