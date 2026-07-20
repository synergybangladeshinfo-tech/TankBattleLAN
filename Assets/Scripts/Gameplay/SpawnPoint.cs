using UnityEngine;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// Marker component for tank spawn locations. Placed by the map builder;
    /// collected by MatchManager at scene load. Draws a gizmo in the editor.
    /// </summary>
    public class SpawnPoint : MonoBehaviour
    {
        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position + Vector3.up, 1f);
            Gizmos.DrawRay(transform.position + Vector3.up, transform.forward * 2f);
        }
    }
}
