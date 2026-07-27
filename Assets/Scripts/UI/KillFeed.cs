using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TankBattle.Core;

namespace TankBattle.UI
{
    /// <summary>
    /// Top-right kill feed ("ACE destroyed BOT Rex") plus the big centre banner
    /// used for killstreak announcements ("RAMPAGE!"). Rows fade out on their
    /// own after a few seconds and the oldest is recycled once the list is full.
    /// The server pushes messages to every client (see MatchManager).
    /// </summary>
    public class KillFeed : MonoBehaviour
    {
        class Row
        {
            public Text Label;
            public float ExpiresAt;
        }

        readonly List<Row> _rows = new List<Row>();
        RectTransform _root;
        Text _banner;
        float _bannerUntil;

        /// <summary>Creates the feed under 'parent' (below the minimap).</summary>
        public static KillFeed Build(Transform parent, float topOffset)
        {
            var go = new GameObject("KillFeed", typeof(RectTransform), typeof(KillFeed));
            go.transform.SetParent(parent, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(620f, 260f);
            rt.anchoredPosition = new Vector2(-30f, -topOffset);

            var feed = go.GetComponent<KillFeed>();
            feed._root = rt;

            // Centre banner for streak announcements.
            var banner = UIFactory.CreateText(parent, "StreakBanner", "", 62, UIFactory.AccentRed);
            banner.fontStyle = FontStyle.Bold;
            UIFactory.SetAnchoredPos(banner, new Vector2(0.5f, 0.5f), new Vector2(0f, 250f));
            banner.gameObject.SetActive(false);
            feed._banner = banner;

            return feed;
        }

        /// <summary>Show one "killer destroyed victim" line.</summary>
        public void AddKill(string killer, string victim, string weapon)
        {
            string text = string.IsNullOrEmpty(killer) || killer == victim
                ? $"{victim} was destroyed"
                : $"{killer}  »  {victim}";
            if (!string.IsNullOrEmpty(weapon)) text += $"   [{weapon}]";
            AddLine(text, UIFactory.TextColor);
        }

        /// <summary>Show a coloured note (pickups, streaks, joins).</summary>
        public void AddLine(string text, Color color)
        {
            Row row = null;

            // Reuse an expired row before creating a new one.
            for (int i = 0; i < _rows.Count; i++)
                if (!_rows[i].Label.gameObject.activeSelf) { row = _rows[i]; break; }

            if (row == null && _rows.Count >= GameConstants.KillFeedMaxRows)
            {
                row = _rows[0];
                _rows.RemoveAt(0);
            }

            if (row == null)
            {
                var label = UIFactory.CreateText(_root, "Row", "", 26,
                    UIFactory.TextColor, TextAnchor.UpperRight);
                var lrt = (RectTransform)label.transform;
                lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(1f, 1f);
                lrt.sizeDelta = new Vector2(600f, 34f);
                row = new Row { Label = label };
            }

            row.Label.text = text;
            row.Label.color = color;
            row.ExpiresAt = Time.time + GameConstants.KillFeedRowSeconds;
            row.Label.gameObject.SetActive(true);

            _rows.Remove(row);
            _rows.Add(row);   // newest at the bottom of the list
            Relayout();
        }

        /// <summary>Big centre announcement (killstreaks).</summary>
        public void ShowBanner(string text, Color color, float seconds = 2f)
        {
            if (_banner == null) return;
            _banner.text = text;
            _banner.color = color;
            _banner.gameObject.SetActive(true);
            _bannerUntil = Time.time + seconds;
        }

        void Update()
        {
            bool dirty = false;
            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];
                if (!r.Label.gameObject.activeSelf) continue;

                float left = r.ExpiresAt - Time.time;
                if (left <= 0f)
                {
                    r.Label.gameObject.SetActive(false);
                    dirty = true;
                }
                else if (left < 0.8f)
                {
                    var c = r.Label.color;
                    c.a = Mathf.Clamp01(left / 0.8f);   // gentle fade out
                    r.Label.color = c;
                }
            }
            if (dirty) Relayout();

            if (_banner != null && _banner.gameObject.activeSelf)
            {
                float left = _bannerUntil - Time.time;
                if (left <= 0f) _banner.gameObject.SetActive(false);
                else
                {
                    float s = 1f + Mathf.Sin(Time.time * 9f) * 0.04f;
                    _banner.transform.localScale = new Vector3(s, s, 1f);
                    var c = _banner.color;
                    c.a = left < 0.5f ? Mathf.Clamp01(left / 0.5f) : 1f;
                    _banner.color = c;
                }
            }
        }

        /// <summary>Stack the visible rows top-down, newest at the top.</summary>
        void Relayout()
        {
            int slot = 0;
            for (int i = _rows.Count - 1; i >= 0; i--)
            {
                var r = _rows[i];
                if (!r.Label.gameObject.activeSelf) continue;
                ((RectTransform)r.Label.transform).anchoredPosition =
                    new Vector2(0f, -slot * 36f);
                slot++;
            }
        }
    }
}
