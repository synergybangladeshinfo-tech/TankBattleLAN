using UnityEngine;
using UnityEngine.EventSystems;

namespace TankBattle.UI
{
    /// <summary>
    /// Hold-to-fire button (bottom-right). TankShooting polls IsPressed and
    /// applies its own fire-rate limiting, so holding simply auto-fires.
    /// </summary>
    public class FireButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public bool IsPressed { get; private set; }

        public void OnPointerDown(PointerEventData e) => IsPressed = true;
        public void OnPointerUp(PointerEventData e) => IsPressed = false;

        void OnDisable() => IsPressed = false;
    }
}
