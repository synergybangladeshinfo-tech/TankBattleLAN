using Unity.Netcode.Components;

namespace TankBattle.Networking
{
    /// <summary>
    /// Owner-authoritative NetworkTransform (standard Unity Multiplayer samples
    /// pattern, MIT licensed). The owning client moves its own tank locally for
    /// zero-latency controls and its transform is replicated to everyone else.
    ///
    /// NOTE: this trusts clients with their own position, which is the right
    /// trade-off for a local couch/LAN game (no cheating concerns, best feel).
    /// </summary>
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
