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

        /// <summary>Map scene index chosen by the host (index into GameConstants.MapScenes).</summary>
        public static int SelectedMapIndex = 0;

        /// <summary>Set when the local NetworkManager is acting as host.</summary>
        public static bool IsHost = false;

        /// <summary>Optional message shown on the main menu (e.g. "Host disconnected").</summary>
        public static string MenuNotice = null;
    }
}
