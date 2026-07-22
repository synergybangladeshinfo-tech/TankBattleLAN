namespace TankBattle.Core
{
    /// <summary>
    /// Lightweight static holder for data that must survive scene loads
    /// but does not need network syncing (purely local choices).
    /// </summary>
    public static class GameSession
    {
        /// <summary>Local player's display name (set in the main menu).</summary>
        public static string PlayerName = "Player";

        /// <summary>Garage: chosen tank color (index into GameConstants.PlayerColors).</summary>
        public static int TankColorIndex = 0;

        /// <summary>Garage: chosen tank body style (index into GameConstants.TankStyleNames).</summary>
        public static int TankStyleIndex = 0;

        /// <summary>Garage: chosen hull camo pattern (index into GameConstants.TankPatternNames).</summary>
        public static int TankPatternIndex = 0;

        /// <summary>Map scene index chosen by the host (index into GameConstants.MapScenes).</summary>
        public static int SelectedMapIndex = 0;

        /// <summary>Game mode chosen by the host (index into the GameMode enum).</summary>
        public static int SelectedModeIndex = 0;

        /// <summary>Match length chosen by the host (index into GameConstants.MatchDurations).</summary>
        public static int SelectedTimeIndex = GameConstants.DefaultDurationIndex;

        /// <summary>Set when the local NetworkManager is acting as host.</summary>
        public static bool IsHost = false;

        /// <summary>Solo (vs bots) session: no discovery, no joiners, straight to battle.</summary>
        public static bool SoloMode = false;

        /// <summary>Optional message shown on the main menu (e.g. "Host disconnected").</summary>
        public static string MenuNotice = null;
    }
}
