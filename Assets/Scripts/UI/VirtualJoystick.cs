using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TankBattle.UI
{
    /// <summary>
    /// Fixed-position on-screen joystick (bottom-left). Exposes a normalized
    /// Direction vector (-1..1 on both axes). Built entirely from code by
    /// HUDController; works with both touch and mouse via uGUI events.
    /// </summary>
    public class VirtualJoystick : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] float handleRange = 90f; // px the handle may travel (reference res)

        RectTransform _background;
        RectTransform _handle;
        Vector2 _direction;

        /// <summary>Normalized input direction; (0,0) when released.</summary>
        public Vector2 Direction => _direction;

        /// <summary>Called by HUDController right after building the visuals.</summary>
        public void Init(RectTransform background, RectTransform handle)
        {
            _background = background;
            _handle = handle;
        }

        public void OnPointerDown(PointerEventData e) => OnDrag(e);

        public void OnDrag(PointerEventData e)
        {
            if (_background == null) return;

            // Pointer position in the joystick's local space, relative to the
            // rect CENTER (works regardless of the background's pivot).
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _background, e.position, e.pressEventCamera, out Vector2 local);
            local -= _background.rect.center;

            Vector2 clamped = Vector2.ClampMagnitude(local, handleRange);
            _handle.anchoredPosition = clamped;
            _direction = clamped / handleRange;
        }

        public void OnPointerUp(PointerEventData e)
        {
            _direction = Vector2.zero;
            if (_handle != null) _handle.anchoredPosition = Vector2.zero;
        }
    }
}
