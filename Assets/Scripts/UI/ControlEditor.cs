using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TankBattle.Core;

namespace TankBattle.UI
{
    /// <summary>
    /// Makes one control proxy draggable inside the editor. Reports the new
    /// canvas-space position back to ControlLayout as the finger moves, and
    /// tells the editor which control was last touched so the size slider
    /// applies to it.
    /// </summary>
    public class DraggableControl : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public ControlId Id;
        public ControlEditor Editor;

        RectTransform _rt;
        RectTransform _canvasRt;
        Vector2 _grabOffset;

        void Awake()
        {
            _rt = (RectTransform)transform;
            _canvasRt = _rt.parent as RectTransform;
        }

        public void OnPointerDown(PointerEventData e)
        {
            Editor?.Select(Id);
            if (_canvasRt == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRt, e.position, e.pressEventCamera, out Vector2 local);
            // local is relative to the canvas centre; our anchor is bottom-left.
            Vector2 fromCorner = local + Vector2.Scale(_canvasRt.rect.size, _canvasRt.pivot);
            _grabOffset = _rt.anchoredPosition - fromCorner;
        }

        public void OnDrag(PointerEventData e)
        {
            if (_canvasRt == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRt, e.position, e.pressEventCamera, out Vector2 local);
            Vector2 fromCorner = local + Vector2.Scale(_canvasRt.rect.size, _canvasRt.pivot);

            Vector2 want = fromCorner + _grabOffset;
            if (Editor != null) want = Editor.ApplySnap(want);
            ControlLayout.SetPos(Id, want);
            _rt.anchoredPosition = ControlLayout.GetPos(Id);
            Editor?.UpdateReadout();
        }

        public void OnPointerUp(PointerEventData e)
        {
            Editor?.Refresh();
            Editor?.UpdateReadout();
        }
    }

    /// <summary>
    /// Full-screen control customiser: drag every button and stick wherever you
    /// like, resize the selected one, set global opacity, aim sensitivity,
    /// left-handed mirroring and auto-fire, or load one of three presets.
    /// SAVE writes the layout to PlayerPrefs; the HUD reads it when a match
    /// starts, so the change is visible next time you play.
    /// </summary>
    public class ControlEditor : MonoBehaviour
    {
        readonly Dictionary<ControlId, RectTransform> _proxies =
            new Dictionary<ControlId, RectTransform>();
        readonly Dictionary<ControlId, Image> _proxyImages = new Dictionary<ControlId, Image>();
        readonly Dictionary<ControlId, GameObject> _proxyRings = new Dictionary<ControlId, GameObject>();

        RectTransform _playfield;     // full-screen area the controls live in
        Slider _sizeSlider;
        Text _selectedLabel;
        Text _readout;                // live X / Y / size of the selected control
        ControlId _selected = ControlId.MoveStick;
        System.Action _onClose;

        /// <summary>Snap dragged controls to a 20 px grid (on by default).</summary>
        bool _snap = true;
        const float GridStep = 20f;

        /// <summary>Layout as it was when the editor opened, for REVERT.</summary>
        Vector2[] _openPos;
        float[] _openScale;
        float _openOpacity, _openSens;

        static readonly Color[] ProxyColors =
        {
            new Color(1f, 1f, 1f, 1f),           // move stick
            new Color(1f, 0.55f, 0.5f, 1f),      // aim stick
            new Color(1f, 0.30f, 0.25f, 1f),     // fire
            new Color(0.25f, 0.6f, 1f, 1f),      // dash
            new Color(0.35f, 0.8f, 0.35f, 1f),   // bomb
            new Color(0.95f, 0.75f, 0.2f, 1f)    // hover
        };

        /// <summary>Builds the editor as a child of 'parent'. Call Close to remove it.</summary>
        public static ControlEditor Build(Transform parent, System.Action onClose)
        {
            var go = new GameObject("ControlEditor", typeof(RectTransform), typeof(ControlEditor));
            go.transform.SetParent(parent, false);
            UIFactory.Stretch((RectTransform)go.transform);

            var editor = go.GetComponent<ControlEditor>();
            editor._onClose = onClose;
            editor.CaptureOpenState();
            editor.BuildUI((RectTransform)go.transform);
            return editor;
        }

        void BuildUI(RectTransform root)
        {
            // Dim backdrop so the proxies pop.
            var bg = UIFactory.CreatePanel(root, "Backdrop", new Color(0.03f, 0.05f, 0.08f, 0.985f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            bg.GetComponent<Image>().raycastTarget = true;

            // --- playfield ---
            // A true-to-scale 1920x1080 "screen" shrunk to fit beside the tool
            // column, so every control stays visible and draggable no matter
            // where the player parks it (the tools would otherwise cover the
            // right-hand side of the screen).
            const float toolWidth = 560f;
            const float margin = 40f;
            float scale = Mathf.Min(
                (ControlLayout.Reference.x - toolWidth - margin * 2f) / ControlLayout.Reference.x,
                (ControlLayout.Reference.y - 150f) / ControlLayout.Reference.y);

            // Phone-shaped frame so it reads as "this is your screen".
            var frame = UIFactory.CreatePanel(root, "ScreenFrame", new Color(0.30f, 0.60f, 0.85f, 0.35f),
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            frame.pivot = new Vector2(0f, 0.5f);
            frame.sizeDelta = ControlLayout.Reference * scale + new Vector2(10f, 10f);
            frame.anchoredPosition = new Vector2(margin, -20f);
            var frameImg = frame.GetComponent<Image>();
            frameImg.sprite = UIFactory.RoundedSprite;
            frameImg.type = Image.Type.Sliced;
            frameImg.raycastTarget = false;

            // Inner "screen" fill.
            var screen = UIFactory.CreatePanel(frame, "Screen", new Color(0.07f, 0.10f, 0.15f, 1f),
                Vector2.zero, Vector2.one, new Vector2(5, 5), new Vector2(-5, -5));
            var screenImg = screen.GetComponent<Image>();
            screenImg.sprite = UIFactory.RoundedSprite;
            screenImg.type = Image.Type.Sliced;
            screenImg.raycastTarget = false;

            // Faint thirds guides to help line buttons up.
            for (int i = 1; i <= 2; i++)
            {
                var vline = UIFactory.CreatePanel(screen, $"GuideV{i}",
                    new Color(1f, 1f, 1f, 0.055f),
                    new Vector2(i / 3f, 0f), new Vector2(i / 3f, 1f),
                    new Vector2(-1f, 12f), new Vector2(1f, -12f));
                vline.GetComponent<Image>().raycastTarget = false;

                var hline = UIFactory.CreatePanel(screen, $"GuideH{i}",
                    new Color(1f, 1f, 1f, 0.055f),
                    new Vector2(0f, i / 3f), new Vector2(1f, i / 3f),
                    new Vector2(12f, -1f), new Vector2(-12f, 1f));
                hline.GetComponent<Image>().raycastTarget = false;
            }

            var pf = new GameObject("Playfield", typeof(RectTransform));
            pf.transform.SetParent(frame, false);
            pf.transform.SetAsLastSibling();   // proxies draw above the screen fill
            _playfield = (RectTransform)pf.transform;
            _playfield.anchorMin = _playfield.anchorMax = _playfield.pivot = new Vector2(0.5f, 0.5f);
            _playfield.sizeDelta = ControlLayout.Reference;   // real 1920x1080 space
            _playfield.anchoredPosition = Vector2.zero;
            _playfield.localScale = new Vector3(scale, scale, 1f);

            var hint = UIFactory.CreateText(root, "Hint",
                "Tap a control to select it, then drag it - or use FINE POSITION", 30, UIFactory.TextDim);
            UIFactory.SetAnchoredPos(hint, new Vector2(0f, 1f),
                new Vector2(margin + frame.sizeDelta.x * 0.5f, -40));

            foreach (ControlId id in System.Enum.GetValues(typeof(ControlId)))
                CreateProxy(id);

            BuildToolbar(root);
            Select(ControlId.MoveStick);
            Refresh();
        }

        /// <summary>One draggable circle standing in for a real HUD control.</summary>
        void CreateProxy(ControlId id)
        {
            var go = new GameObject($"Proxy_{id}", typeof(RectTransform), typeof(Image),
                                    typeof(DraggableControl));
            go.transform.SetParent(_playfield, false);

            var img = go.GetComponent<Image>();
            img.sprite = UIFactory.CircleSprite;
            img.color = ProxyColors[(int)id];
            img.raycastTarget = true;

            // Selection ring: a slightly larger circle behind the proxy, shown
            // only for the control the size slider is currently editing.
            var ringGo = new GameObject("SelectRing", typeof(Image));
            ringGo.transform.SetParent(go.transform, false);
            ringGo.transform.SetAsFirstSibling();
            var ring = ringGo.GetComponent<Image>();
            ring.sprite = UIFactory.CircleSprite;
            ring.color = new Color(1f, 1f, 1f, 0.30f);
            ring.raycastTarget = false;
            UIFactory.Stretch((RectTransform)ringGo.transform,
                new Vector2(-16f, -16f), new Vector2(16f, 16f));
            ringGo.SetActive(false);
            _proxyRings[id] = ringGo;

            // Anchored to the bottom-left corner, centred on its own pivot, so
            // the stored position is the control's CENTRE in 1920x1080 space.
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            var label = UIFactory.CreateText(go.transform, "Label",
                ControlLayout.Names[(int)id], 22, new Color(0f, 0f, 0f, 0.85f));
            label.fontStyle = FontStyle.Bold;
            UIFactory.Stretch((RectTransform)label.transform);

            var drag = go.GetComponent<DraggableControl>();
            drag.Id = id;
            drag.Editor = this;

            _proxies[id] = rt;
            _proxyImages[id] = img;
        }

        void BuildToolbar(RectTransform root)
        {
            // Right-hand tool column so it never covers the natural thumb zones.
            var panel = UIFactory.CreatePanel(root, "Tools", UIFactory.PanelColor,
                new Vector2(1f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            panel.pivot = new Vector2(1f, 0.5f);
            panel.sizeDelta = new Vector2(560f, 0f);
            panel.anchoredPosition = Vector2.zero;
            panel.anchorMin = new Vector2(1f, 0f);
            panel.anchorMax = new Vector2(1f, 1f);
            panel.offsetMin = new Vector2(-560f, 0f);
            panel.offsetMax = Vector2.zero;

            var title = UIFactory.CreateText(panel, "Title", "CONTROLS", 40, UIFactory.TextColor);
            title.fontStyle = FontStyle.Bold;
            UIFactory.SetAnchoredPos(title, new Vector2(0.5f, 1f), new Vector2(0, -30));

            _selectedLabel = UIFactory.CreateText(panel, "Selected", "", 26, UIFactory.Accent);
            UIFactory.SetAnchoredPos(_selectedLabel, new Vector2(0.5f, 1f), new Vector2(0, -78));

            _readout = UIFactory.CreateText(panel, "Readout", "", 22,
                new Color(0.62f, 0.70f, 0.80f, 1f));
            UIFactory.SetAnchoredPos(_readout, new Vector2(0.5f, 1f), new Vector2(0, -112));
            ((RectTransform)_readout.transform).sizeDelta = new Vector2(520, 30);

            // Stack of controls under the headings.
            var column = new GameObject("Column", typeof(RectTransform));
            column.transform.SetParent(panel, false);
            var crt = (RectTransform)column.transform;
            crt.anchorMin = new Vector2(0f, 0f); crt.anchorMax = new Vector2(1f, 1f);
            crt.offsetMin = new Vector2(20, 20); crt.offsetMax = new Vector2(-20, -140);
            UIFactory.AddVerticalLayout(crt, 8, new RectOffset(0, 0, 0, 0));

            // ---- fine positioning pad: 1-step nudges for pixel accuracy ----
            BuildNudgePad(crt);

            var snapToggle = UIFactory.CreateToggle(crt, "Snap", "Snap to grid",
                _snap, v => { _snap = v; });
            ((RectTransform)snapToggle.transform).sizeDelta = new Vector2(500, 54);

            _sizeSlider = UIFactory.CreateSlider(crt, "Size", "BUTTON SIZE", 0.6f, 1.7f,
                ControlLayout.GetScale(_selected), v =>
                {
                    ControlLayout.SetScale(_selected, v);
                    Refresh();
                }, new Vector2(500, 74));

            UIFactory.CreateSlider(crt, "Opacity", "OPACITY", 0.15f, 1f,
                ControlLayout.Opacity, v => { ControlLayout.Opacity = v; Refresh(); },
                new Vector2(500, 74));

            UIFactory.CreateSlider(crt, "Sens", "AIM SENSITIVITY", 0.4f, 2.5f,
                ControlLayout.Sensitivity, v => ControlLayout.Sensitivity = v,
                new Vector2(500, 74));

            var leftToggle = UIFactory.CreateToggle(crt, "LeftHand", "Left-handed layout",
                ControlLayout.LeftHanded, v => { ControlLayout.LeftHanded = v; Refresh(); });
            ((RectTransform)leftToggle.transform).sizeDelta = new Vector2(500, 54);

            var autoToggle = UIFactory.CreateToggle(crt, "AutoFire", "Auto-fire while aiming",
                ControlLayout.AutoFire, v => ControlLayout.AutoFire = v);
            ((RectTransform)autoToggle.transform).sizeDelta = new Vector2(500, 54);

            var presetLabel = UIFactory.CreateText(crt, "PresetLabel", "PRESETS", 26,
                UIFactory.TextDim);
            ((RectTransform)presetLabel.transform).sizeDelta = new Vector2(500, 34);

            var presetRow = new GameObject("PresetRow", typeof(RectTransform));
            presetRow.transform.SetParent(crt, false);
            ((RectTransform)presetRow.transform).sizeDelta = new Vector2(500, 66);
            var h = presetRow.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = false; h.childControlHeight = false;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;

            for (int i = 0; i < ControlLayout.PresetNames.Length; i++)
            {
                int preset = i;
                UIFactory.CreateButton(presetRow.transform, $"Preset{i}",
                    ControlLayout.PresetNames[i], new Vector2(158, 62), UIFactory.PanelLight,
                    () => { ControlLayout.ApplyPreset(preset); RefreshAll(); }, 20);
            }

            var actionRow = new GameObject("ActionRow", typeof(RectTransform));
            actionRow.transform.SetParent(crt, false);
            ((RectTransform)actionRow.transform).sizeDelta = new Vector2(500, 76);
            var h2 = actionRow.AddComponent<HorizontalLayoutGroup>();
            h2.spacing = 10;
            h2.childAlignment = TextAnchor.MiddleCenter;
            h2.childControlWidth = false; h2.childControlHeight = false;
            h2.childForceExpandWidth = false; h2.childForceExpandHeight = false;

            UIFactory.CreateButton(actionRow.transform, "Reset", "DEFAULTS",
                new Vector2(158, 70), UIFactory.PanelLight,
                () => { ControlLayout.ResetDefaults(); RefreshAll(); }, 21);

            UIFactory.CreateButton(actionRow.transform, "Revert", "UNDO ALL",
                new Vector2(158, 70), UIFactory.PanelLight,
                () => { RestoreOpenState(); RefreshAll(); }, 21);

            UIFactory.CreateButton(actionRow.transform, "Save", "SAVE",
                new Vector2(158, 70), UIFactory.AccentGreen,
                () => { ControlLayout.Save(); Close(); }, 24);

            var cancelRow = new GameObject("CancelRow", typeof(RectTransform));
            cancelRow.transform.SetParent(crt, false);
            ((RectTransform)cancelRow.transform).sizeDelta = new Vector2(500, 66);
            var h3 = cancelRow.AddComponent<HorizontalLayoutGroup>();
            h3.spacing = 10;
            h3.childAlignment = TextAnchor.MiddleCenter;
            h3.childControlWidth = false; h3.childControlHeight = false;
            h3.childForceExpandWidth = false; h3.childForceExpandHeight = false;

            UIFactory.CreateButton(cancelRow.transform, "Back", "CANCEL (discard changes)",
                new Vector2(500, 62), UIFactory.AccentRed,
                () => { RestoreOpenState(); Close(); }, 22);
        }

        /// <summary>
        /// Four arrows plus a centre button: nudges the selected control by one
        /// grid step (or 4 px with snapping off) so it can be placed exactly.
        /// Dragging with a thumb is never pixel accurate on a phone.
        /// </summary>
        void BuildNudgePad(RectTransform parent)
        {
            var label = UIFactory.CreateText(parent, "NudgeLabel", "FINE POSITION", 24,
                UIFactory.TextDim);
            ((RectTransform)label.transform).sizeDelta = new Vector2(500, 30);

            var pad = new GameObject("NudgePad", typeof(RectTransform));
            pad.transform.SetParent(parent, false);
            var prt = (RectTransform)pad.transform;
            prt.sizeDelta = new Vector2(500, 130);

            void Arrow(string n, string glyph, Vector2 at, Vector2 delta)
            {
                var b = UIFactory.CreateButton(prt, n, glyph, new Vector2(62, 58),
                    UIFactory.PanelLight, () => Nudge(delta), 30);
                UIFactory.SetAnchoredPos(b, new Vector2(0.5f, 0.5f), at);
            }

            Arrow("Up", "\u25B2", new Vector2(0, 34), new Vector2(0, 1));
            Arrow("Down", "\u25BC", new Vector2(0, -34), new Vector2(0, -1));
            Arrow("Left", "\u25C0", new Vector2(-70, 0), new Vector2(-1, 0));
            Arrow("Right", "\u25B6", new Vector2(70, 0), new Vector2(1, 0));

            var centre = UIFactory.CreateButton(prt, "CentreX", "CENTRE X",
                new Vector2(200, 58), UIFactory.PanelLight, () =>
                {
                    var p = ControlLayout.GetPos(_selected);
                    ControlLayout.SetPos(_selected, new Vector2(ControlLayout.Reference.x * 0.5f, p.y));
                    Refresh(); UpdateReadout();
                }, 22);
            UIFactory.SetAnchoredPos(centre, new Vector2(0.5f, 0.5f), new Vector2(180, 0));
        }

        void Nudge(Vector2 dir)
        {
            float step = _snap ? GridStep : 4f;
            ControlLayout.SetPos(_selected, ControlLayout.GetPos(_selected) + dir * step);
            Refresh();
            UpdateReadout();
        }

        /// <summary>Rounds a dragged position to the grid when snapping is on.</summary>
        public Vector2 ApplySnap(Vector2 pos)
        {
            if (!_snap) return pos;
            return new Vector2(Mathf.Round(pos.x / GridStep) * GridStep,
                               Mathf.Round(pos.y / GridStep) * GridStep);
        }

        /// <summary>Live X / Y / size readout for the selected control.</summary>
        public void UpdateReadout()
        {
            if (_readout == null) return;
            var p = ControlLayout.GetPos(_selected);
            _readout.text = $"X {Mathf.RoundToInt(p.x)}    Y {Mathf.RoundToInt(p.y)}" +
                            $"    SIZE {Mathf.RoundToInt(ControlLayout.SizeOf(_selected))}";
        }

        /// <summary>Snapshot the layout so CANCEL / UNDO ALL can restore it.</summary>
        public void CaptureOpenState()
        {
            _openPos = new Vector2[ControlLayout.Count];
            _openScale = new float[ControlLayout.Count];
            for (int i = 0; i < ControlLayout.Count; i++)
            {
                _openPos[i] = ControlLayout.GetPos((ControlId)i);
                _openScale[i] = ControlLayout.GetScale((ControlId)i);
            }
            _openOpacity = ControlLayout.Opacity;
            _openSens = ControlLayout.Sensitivity;
        }

        void RestoreOpenState()
        {
            if (_openPos == null) return;
            for (int i = 0; i < ControlLayout.Count; i++)
            {
                ControlLayout.SetPos((ControlId)i, _openPos[i]);
                ControlLayout.SetScale((ControlId)i, _openScale[i]);
            }
            ControlLayout.Opacity = _openOpacity;
            ControlLayout.Sensitivity = _openSens;
        }

        /// <summary>Pick the control the size slider applies to.</summary>
        public void Select(ControlId id)
        {
            _selected = id;
            if (_selectedLabel != null)
                _selectedLabel.text = $"editing:  {ControlLayout.Names[(int)id]}";
            if (_sizeSlider != null)
                _sizeSlider.SetValueWithoutNotify(ControlLayout.GetScale(id));
            Refresh();
            UpdateReadout();
        }

        /// <summary>Re-apply position, size and opacity to every proxy.</summary>
        public void Refresh()
        {
            foreach (var kv in _proxies)
            {
                var id = kv.Key;
                var rt = kv.Value;
                float size = ControlLayout.SizeOf(id);
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = ControlLayout.GetPos(id);

                var img = _proxyImages[id];
                var c = ProxyColors[(int)id];
                bool sel = id == _selected;
                // Selected control stays brighter so you can see what you're sizing.
                c.a = sel ? Mathf.Max(0.9f, ControlLayout.Opacity) : ControlLayout.Opacity;
                img.color = c;

                if (_proxyRings.TryGetValue(id, out var ring) && ring != null)
                    ring.SetActive(sel);
            }
        }

        /// <summary>Refresh after a preset/reset also resyncs the slider value.</summary>
        void RefreshAll()
        {
            if (_sizeSlider != null)
                _sizeSlider.SetValueWithoutNotify(ControlLayout.GetScale(_selected));
            Refresh();
            UpdateReadout();
        }

        public void Close()
        {
            _onClose?.Invoke();
            Destroy(gameObject);
        }
    }
}
