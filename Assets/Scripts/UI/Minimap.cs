using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TankBattle.Core;
using TankBattle.Gameplay;

namespace TankBattle.UI
{
    /// <summary>
    /// Corner minimap. Draws a dot for every living tank (green = you,
    /// blue/red = teams, grey = other players in free-for-all) plus a yellow
    /// dot for each weapon crate, all projected from world XZ into the map
    /// square. Built entirely from code by the HUD; dots are pooled so no
    /// allocation happens per frame.
    /// </summary>
    public class Minimap : MonoBehaviour
    {
        RectTransform _root;
        RectTransform _selfArrow;
        readonly List<Image> _tankDots = new List<Image>();
        readonly List<Image> _pickupDots = new List<Image>();
        float _half;   // map half-size in UI pixels

        static readonly Color SelfColor = new Color(0.35f, 1f, 0.45f, 1f);
        static readonly Color EnemyColor = new Color(1f, 0.35f, 0.30f, 1f);
        static readonly Color AllyColor = new Color(0.35f, 0.7f, 1f, 1f);
        static readonly Color NeutralColor = new Color(0.85f, 0.85f, 0.9f, 1f);
        static readonly Color PickupColor = new Color(1f, 0.85f, 0.25f, 0.95f);

        /// <summary>Creates the minimap under 'parent' (top-right corner).</summary>
        public static Minimap Build(Transform parent, float size = 250f)
        {
            var go = new GameObject("Minimap", typeof(RectTransform), typeof(Image), typeof(Minimap));
            go.transform.SetParent(parent, false);

            var bg = go.GetComponent<Image>();
            bg.color = new Color(0.04f, 0.06f, 0.09f, 0.55f);
            bg.raycastTarget = false;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(size, size);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-30f, -110f);   // under the pause buttons

            var map = go.GetComponent<Minimap>();
            map._root = rt;
            map._half = size * 0.5f;

            // Faint border so the square reads against bright maps.
            var border = UIFactory.CreatePanel(rt, "Border", new Color(1f, 1f, 1f, 0.10f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            border.GetComponent<Image>().raycastTarget = false;
            var inner = UIFactory.CreatePanel(border, "Inner", new Color(0.04f, 0.06f, 0.09f, 0.65f),
                Vector2.zero, Vector2.one, new Vector2(3, 3), new Vector2(-3, -3));
            inner.GetComponent<Image>().raycastTarget = false;

            // "You" marker sits on top of everything.
            var arrowGo = new GameObject("SelfArrow", typeof(Image));
            arrowGo.transform.SetParent(rt, false);
            var arrowImg = arrowGo.GetComponent<Image>();
            arrowImg.sprite = UIFactory.CircleSprite;
            arrowImg.color = SelfColor;
            arrowImg.raycastTarget = false;
            map._selfArrow = (RectTransform)arrowGo.transform;
            map._selfArrow.sizeDelta = new Vector2(16f, 16f);

            return map;
        }

        void LateUpdate()
        {
            if (_root == null) return;

            ulong localId = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.LocalClientId : 0;
            int localTeam = -1;
            Transform localTf = null;

            // Find the local tank first so we can colour allies vs enemies.
            for (int i = 0; i < TankHealth.All.Count; i++)
            {
                var h = TankHealth.All[i];
                if (h == null || !h.IsOwner) continue;
                if (h.GetComponent<BotTank>() != null) continue;
                localTf = h.transform;
                var tc = h.GetComponent<TankController>();
                if (tc != null) localTeam = tc.TeamIndex.Value;
                break;
            }

            // --- tanks ---
            int used = 0;
            for (int i = 0; i < TankHealth.All.Count; i++)
            {
                var h = TankHealth.All[i];
                if (h == null || h.IsDead.Value) continue;
                if (h.transform == localTf) continue;   // drawn as the self marker

                var dot = DotAt(_tankDots, used++, 13f);
                dot.rectTransform.anchoredPosition = WorldToMap(h.transform.position);

                var tc = h.GetComponent<TankController>();
                int team = tc != null ? tc.TeamIndex.Value : -1;
                dot.color = localTeam >= 0 && team >= 0
                    ? (team == localTeam ? AllyColor : EnemyColor)
                    : NeutralColor;
                dot.gameObject.SetActive(true);
            }
            for (int i = used; i < _tankDots.Count; i++) _tankDots[i].gameObject.SetActive(false);

            // --- weapon crates ---
            int pUsed = 0;
            var pickups = Object.FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None);
            for (int i = 0; i < pickups.Length; i++)
            {
                if (pickups[i] == null) continue;
                var dot = DotAt(_pickupDots, pUsed++, 9f);
                dot.rectTransform.anchoredPosition = WorldToMap(pickups[i].transform.position);
                dot.color = PickupColor;
                dot.gameObject.SetActive(true);
            }
            for (int i = pUsed; i < _pickupDots.Count; i++) _pickupDots[i].gameObject.SetActive(false);

            // --- you ---
            if (localTf != null)
            {
                _selfArrow.gameObject.SetActive(true);
                _selfArrow.anchoredPosition = WorldToMap(localTf.position);
            }
            else _selfArrow.gameObject.SetActive(false);

            _ = localId; // (kept for clarity: identity comes from IsOwner above)
        }

        /// <summary>Project world XZ into map-local pixels, clamped to the square.</summary>
        Vector2 WorldToMap(Vector3 world)
        {
            float r = GameConstants.MinimapWorldRadius;
            float x = Mathf.Clamp(world.x / r, -1f, 1f) * (_half - 8f);
            float y = Mathf.Clamp(world.z / r, -1f, 1f) * (_half - 8f);
            return new Vector2(x, y);
        }

        /// <summary>Pooled dot fetch - grows the list only when more are needed.</summary>
        Image DotAt(List<Image> pool, int index, float size)
        {
            while (pool.Count <= index)
            {
                var go = new GameObject("Dot", typeof(Image));
                go.transform.SetParent(_root, false);
                var img = go.GetComponent<Image>();
                img.sprite = UIFactory.CircleSprite;
                img.raycastTarget = false;
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(size, size);
                pool.Add(img);
            }
            var d = pool[index];
            ((RectTransform)d.transform).sizeDelta = new Vector2(size, size);
            return d;
        }
    }
}
