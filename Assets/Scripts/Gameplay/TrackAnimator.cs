using UnityEngine;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// Spins the road wheels and scrolls the track texture so a moving tank
    /// looks driven instead of sliding around on ice.
    ///
    /// Like the engine audio, this measures the tank's ACTUAL replicated motion
    /// rather than reading input, so it works identically for your own tank, for
    /// remote players and for bots with no extra network traffic.
    ///
    /// Turning is handled properly: the left and right tracks run at different
    /// speeds (and in opposite directions on the spot), which is what makes a
    /// tracked vehicle read as a tracked vehicle.
    /// </summary>
    public class TrackAnimator : MonoBehaviour
    {
        /// <summary>Metres travelled per full wheel revolution.</summary>
        const float WheelCircumference = 1.6f;

        /// <summary>How fast the track texture scrolls per metre travelled.</summary>
        const float TrackUvPerMetre = 0.55f;

        /// <summary>Half the distance between the two tracks (metres).</summary>
        const float TrackHalfWidth = 0.85f;

        Transform[] _leftWheels, _rightWheels;
        Material[] _leftTrackMats, _rightTrackMats;

        Vector3 _lastPos;
        float _lastYaw;
        float _leftUv, _rightUv;

        void Awake()
        {
            _lastPos = transform.position;
            _lastYaw = transform.eulerAngles.y;
            Collect();
        }

        /// <summary>
        /// Find the wheels and track surfaces the prefab builder created. Parts
        /// are recognised by name and sorted onto the left or right side by
        /// their local X, so the builder never has to wire anything up.
        /// </summary>
        void Collect()
        {
            var left = new System.Collections.Generic.List<Transform>();
            var right = new System.Collections.Generic.List<Transform>();
            var leftMats = new System.Collections.Generic.List<Material>();
            var rightMats = new System.Collections.Generic.List<Material>();

            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                bool isWheel = n.StartsWith("Wheel") || n.StartsWith("Sprocket") ||
                               n.StartsWith("Idler");
                bool isTrack = n.StartsWith("Track");
                if (!isWheel && !isTrack) continue;

                // Local X relative to the tank root decides the side.
                float x = transform.InverseTransformPoint(t.position).x;

                if (isWheel)
                {
                    if (x < 0f) left.Add(t); else right.Add(t);
                }
                else
                {
                    var mr = t.GetComponent<MeshRenderer>();
                    if (mr == null) continue;
                    // material (not sharedMaterial): each tank scrolls its own.
                    if (x < 0f) leftMats.Add(mr.material); else rightMats.Add(mr.material);
                }
            }

            _leftWheels = left.ToArray();
            _rightWheels = right.ToArray();
            _leftTrackMats = leftMats.ToArray();
            _rightTrackMats = rightMats.ToArray();
        }

        void LateUpdate()
        {
            // --- how far did each side travel since last frame? ---
            Vector3 delta = transform.position - _lastPos;
            _lastPos = transform.position;
            delta.y = 0f;

            // Signed forward distance: reversing must roll the wheels backwards.
            float forward = Vector3.Dot(delta, transform.forward);

            float yaw = transform.eulerAngles.y;
            float turnDeg = Mathf.DeltaAngle(_lastYaw, yaw);
            _lastYaw = yaw;

            // A turn of 'turnDeg' moves the outer track further than the inner
            // one; on the spot they move in opposite directions.
            float turnArc = turnDeg * Mathf.Deg2Rad * TrackHalfWidth;

            float leftDist = forward - turnArc;
            float rightDist = forward + turnArc;

            if (Time.deltaTime <= 0.0001f) return;

            // --- wheels ---
            float leftDeg = leftDist / WheelCircumference * 360f;
            float rightDeg = rightDist / WheelCircumference * 360f;

            for (int i = 0; i < _leftWheels.Length; i++)
                if (_leftWheels[i] != null) _leftWheels[i].Rotate(leftDeg, 0f, 0f, Space.Self);
            for (int i = 0; i < _rightWheels.Length; i++)
                if (_rightWheels[i] != null) _rightWheels[i].Rotate(rightDeg, 0f, 0f, Space.Self);

            // --- track texture scroll ---
            _leftUv = Mathf.Repeat(_leftUv + leftDist * TrackUvPerMetre, 1f);
            _rightUv = Mathf.Repeat(_rightUv + rightDist * TrackUvPerMetre, 1f);

            for (int i = 0; i < _leftTrackMats.Length; i++)
                if (_leftTrackMats[i] != null)
                    _leftTrackMats[i].mainTextureOffset = new Vector2(0f, -_leftUv);
            for (int i = 0; i < _rightTrackMats.Length; i++)
                if (_rightTrackMats[i] != null)
                    _rightTrackMats[i].mainTextureOffset = new Vector2(0f, -_rightUv);
        }
    }
}
