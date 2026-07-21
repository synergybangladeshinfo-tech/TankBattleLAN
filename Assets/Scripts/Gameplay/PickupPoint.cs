using UnityEngine;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// Marker component for weapon-crate spawn locations. Placed by the map
    /// builder; collected by MatchManager at scene load.
    /// </summary>
    public class PickupPoint : MonoBehaviour
    {
        void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(transform.position + Vector3.up, Vector3.one * 0.8f);
        }
    }
}
