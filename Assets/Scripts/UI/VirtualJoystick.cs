using UnityEngine;
using UnityEngine.EventSystems;

namespace TankBattle.UI
{
    /// <summary>
    /// Floating on-screen joystick (bottom-left). The base re-centres under
    /// your thumb wherever you press inside its touch area, which feels far
    /// more natural than a fixed stick, and snaps home on release.
    /// Includes a dead zone plus a smoothed response curve so tiny thumb
    /// wobble never twitches the tank. Built entirely from code by
    /// HUDController; works with both touch and mouse via uGUI events.
    /// </summary>
    public class VirtualJoystick : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] float handleRange = 90f;  // px the handle may travel (reference res)
        [SerializeField] float deadZone = 0.12f;   // ignore tiny deflections

        RectTransform _background;
        RectTransform _handle;
        Vector2 _homePosition;      // where the base rests when untouched
        Vector2 _direction;
        Vector2 _smoothed;

        /// <summary>Normalized, smoothed input direction; (0,0) when released.</summary>
        public Vector2 Direction => _smoothed;

        /// <summary>Called by HUDController right after building the visuals.</summary>
        public void Init(RectTransform background, RectTransform handle)
        {
            _background = background;
            _handle = handle;
            _homePosition = background.anchoredPosition;
        }

        void Update()
        {
            // Short exponential smoothing = fluid, frame-rate independent feel.
            float t = 1f - Mathf.Exp(-14f * Time.deltaTime);
            _smoothed = Vector2.Lerp(_smoothed, _direction, t);
            if (_smoothed.sqrMagnitude < 0.0001f && _direction == Vector2.zero)
                _smoothed = Vector2.zero;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (_background == null) return;

            // Float: move the whole stick so its centre sits under the finger.
            // (The stick is anchored & pivoted at the parent's bottom-left.)
            var parent = (RectTransform)_background.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, e.position, e.pressEventCamera, out Vector2 local);

            // local is relative to the parent's pivot; re-base to bottom-left.
            Vector2 fromCorner = local + Vector2.Scale(parent.rect.size, parent.pivot);

            // Bottom-left corner of the stick so that its centre is under the
            // finger, clamped to the lower-left region of the screen.
            Vector2 corner = fromCorner - _background.rect.size * 0.5f;
            corner.x = Mathf.Clamp(corner.x, 0f,
                parent.rect.size.x * 0.45f - _background.rect.size.x);
            corner.y = Mathf.Clamp(corner.y, 0f,
                parent.rect.size.y * 0.60f - _background.rect.size.y);

            // pivot offset -> anchoredPosition (anchor is the bottom-left corner).
            _background.anchoredPosition = corner +
                Vector2.Scale(_background.rect.size, _background.pivot);
            OnDrag(e);
        }

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

            Vector2 raw = clamped / handleRange;

            // Dead zone with re-normalisation, so movement starts exactly at
            // the edge of the zone instead of jumping.
            float mag = raw.magnitude;
            if (mag < deadZone)
            {
                _direction = Vector2.zero;
            }
            else
            {
                float scaled = (mag - deadZone) / (1f - deadZone);
                _direction = raw.normalized * Mathf.Clamp01(scaled);
            }
        }

        public void OnPointerUp(PointerEventData e)
        {
            _direction = Vector2.zero;
            if (_handle != null) _handle.anchoredPosition = Vector2.zero;
            if (_background != null) _background.anchoredPosition = _homePosition;
        }
    }
}
