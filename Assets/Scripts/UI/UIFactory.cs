using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TankBattle.Audio;

namespace TankBattle.UI
{
    /// <summary>
    /// Code-driven UI construction helpers. All menus and the HUD are built at
    /// runtime from these primitives, which guarantees every reference is wired
    /// correctly and keeps the scenes tiny. Uses uGUI with the built-in legacy
    /// font, flat colors and a generated circle sprite - clean and cheap.
    /// Reference resolution: 1920x1080 landscape.
    /// </summary>
    public static class UIFactory
    {
        // ---- palette ----
        public static readonly Color PanelColor = new Color(0.08f, 0.10f, 0.14f, 0.92f);
        public static readonly Color PanelLight = new Color(0.15f, 0.18f, 0.24f, 1f);
        public static readonly Color Accent = new Color(0.25f, 0.65f, 1f, 1f);
        public static readonly Color AccentGreen = new Color(0.30f, 0.85f, 0.40f, 1f);
        public static readonly Color AccentRed = new Color(1f, 0.35f, 0.30f, 1f);
        public static readonly Color TextColor = new Color(0.95f, 0.96f, 1f, 1f);
        public static readonly Color TextDim = new Color(0.65f, 0.70f, 0.78f, 1f);

        static Font _font;
        public static Font DefaultFont
        {
            get
            {
                if (_font == null)
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        static Sprite _circle;
        /// <summary>Procedurally generated anti-aliased white circle sprite.</summary>
        public static Sprite CircleSprite
        {
            get
            {
                if (_circle != null) return _circle;
                const int size = 128;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                float r = size * 0.5f - 1f;
                Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                        float a = Mathf.Clamp01(r - d + 0.5f); // 1px soft edge
                        pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                    }
                tex.SetPixels32(pixels);
                tex.Apply();
                _circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                return _circle;
            }
        }

        static Sprite _vignette;
        /// <summary>Soft dark-corner vignette overlay (subtle "cinematic" feel).</summary>
        public static Sprite VignetteSprite
        {
            get
            {
                if (_vignette != null) return _vignette;
                const int size = 256;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var px = new Color32[size * size];
                Vector2 c = new Vector2(size * 0.5f, size * 0.5f);
                float maxD = size * 0.72f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), c) / maxD;
                        float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d - 0.45f) / 0.55f);
                        px[y * size + x] = new Color(0f, 0f, 0f, a);
                    }
                tex.SetPixels32(px);
                tex.Apply();
                _vignette = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                return _vignette;
            }
        }

        static Sprite _reticle;
        /// <summary>Targeting reticle: a ring plus a small centre crosshair (white, tintable).</summary>
        public static Sprite ReticleSprite
        {
            get
            {
                if (_reticle != null) return _reticle;
                const int size = 128;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var px = new Color32[size * size];
                float cx = size * 0.5f, cy = size * 0.5f;
                float rOut = size * 0.42f, rIn = size * 0.33f;
                float tick = size * 0.13f;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = 0f;
                        if (d <= rOut && d >= rIn)                              // ring band
                            a = Mathf.Clamp01(Mathf.Min(rOut - d, d - rIn) + 1f);
                        if (Mathf.Abs(dx) < 2f && Mathf.Abs(dy) < tick) a = 1f; // vertical tick
                        if (Mathf.Abs(dy) < 2f && Mathf.Abs(dx) < tick) a = 1f; // horizontal tick
                        px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                    }
                tex.SetPixels32(px);
                tex.Apply();
                _reticle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                return _reticle;
            }
        }

        static Sprite _rounded;
        /// <summary>
        /// 9-sliced rounded-rectangle sprite. Everything in the menu uses this
        /// instead of hard square blocks, which is most of why v2.7 looks less
        /// like a prototype.
        /// </summary>
        public static Sprite RoundedSprite
        {
            get
            {
                if (_rounded != null) return _rounded;
                const int size = 64;
                const float radius = 18f;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        // Distance to the rounded-rect boundary.
                        float qx = Mathf.Max(Mathf.Max(radius - (x + 0.5f), 0f),
                                             Mathf.Max((x + 0.5f) - (size - radius), 0f));
                        float qy = Mathf.Max(Mathf.Max(radius - (y + 0.5f), 0f),
                                             Mathf.Max((y + 0.5f) - (size - radius), 0f));
                        float d = Mathf.Sqrt(qx * qx + qy * qy) - radius;
                        float a = Mathf.Clamp01(-d + 0.5f);
                        px[y * size + x] = new Color(1f, 1f, 1f, a);
                    }
                tex.SetPixels32(px);
                tex.Apply();
                _rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                    100f, 0, SpriteMeshType.FullRect, new Vector4(22, 22, 22, 22));
                return _rounded;
            }
        }

        static Sprite _menuBg;
        /// <summary>Navy gradient + faint grid for the main-menu backdrop.</summary>
        public static Sprite MenuBackgroundSprite
        {
            get
            {
                if (_menuBg != null) return _menuBg;
                const int size = 256;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var px = new Color32[size * size];
                Color top = new Color(0.045f, 0.065f, 0.10f);
                Color bottom = new Color(0.10f, 0.16f, 0.24f);
                for (int y = 0; y < size; y++)
                {
                    Color row = Color.Lerp(bottom, top, y / (float)(size - 1));
                    for (int x = 0; x < size; x++)
                    {
                        Color cpx = row;
                        if (x % 32 == 0 || y % 32 == 0)
                            cpx = Color.Lerp(row, Color.white, 0.03f); // faint grid
                        px[y * size + x] = cpx;
                    }
                }
                tex.SetPixels32(px);
                tex.Apply();
                _menuBg = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                return _menuBg;
            }
        }

        // ---- canvas / panels ----

        /// <summary>Full-screen scaled canvas (1920x1080 reference).</summary>
        public static Canvas CreateCanvas(string name, int sortOrder = 0)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        /// <summary>Ensure an EventSystem exists (touch/click input for uGUI).</summary>
        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        public static RectTransform CreatePanel(Transform parent, string name, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            return rt;
        }

        /// <summary>Panel anchored to the center with a fixed size.</summary>
        public static RectTransform CreateCenterPanel(Transform parent, string name, Color color, Vector2 size)
        {
            var rt = CreatePanel(parent, name, color, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                 Vector2.zero, Vector2.zero);
            rt.sizeDelta = size;
            return rt;
        }

        // ---- widgets ----

        public static Text CreateText(Transform parent, string name, string content, int fontSize,
            Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject(name, typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = DefaultFont;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = anchor;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        public static Button CreateButton(Transform parent, string name, string label,
            Vector2 size, Color color, UnityEngine.Events.UnityAction onClick, int fontSize = 34)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = size;
            go.GetComponent<Image>().color = color;

            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(() => AudioManager.Instance?.PlayClick());
            if (onClick != null) btn.onClick.AddListener(onClick);

            var text = CreateText(go.transform, "Label", label, fontSize, TextColor);
            Stretch((RectTransform)text.transform);
            return btn;
        }

        /// <summary>
        /// Big menu "card" button: rounded dark body, a coloured accent stripe
        /// down the left edge, a bold title and a small caption underneath.
        /// Much easier to scan than a row of flat coloured rectangles.
        /// </summary>
        public static Button CreateMenuButton(Transform parent, string name, string title,
            string caption, Color accent, UnityEngine.Events.UnityAction onClick,
            Vector2? size = null)
        {
            var go = new GameObject(name, typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size ?? new Vector2(560, 96);

            var body = go.GetComponent<Image>();
            body.sprite = RoundedSprite;
            body.type = Image.Type.Sliced;
            body.color = PanelLight;

            var btn = go.GetComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.fadeDuration = 0.08f;
            btn.colors = colors;
            btn.onClick.AddListener(() => AudioManager.Instance?.PlayClick());
            if (onClick != null) btn.onClick.AddListener(onClick);

            // Accent stripe hugging the left edge.
            var stripe = new GameObject("Accent", typeof(Image));
            stripe.transform.SetParent(go.transform, false);
            var srt = (RectTransform)stripe.transform;
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.sizeDelta = new Vector2(12f, -20f);
            srt.anchoredPosition = new Vector2(10f, 0f);
            var simg = stripe.GetComponent<Image>();
            simg.sprite = RoundedSprite;
            simg.type = Image.Type.Sliced;
            simg.color = accent;
            simg.raycastTarget = false;

            bool hasCaption = !string.IsNullOrEmpty(caption);

            var titleText = CreateText(go.transform, "Title", title, 34, TextColor,
                TextAnchor.MiddleLeft);
            titleText.fontStyle = FontStyle.Bold;
            var trt = (RectTransform)titleText.transform;
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(40f, hasCaption ? 26f : 0f);
            trt.offsetMax = new Vector2(-24f, 0f);

            if (hasCaption)
            {
                var capText = CreateText(go.transform, "Caption", caption, 22, TextDim,
                    TextAnchor.MiddleLeft);
                var crt = (RectTransform)capText.transform;
                crt.anchorMin = new Vector2(0f, 0f);
                crt.anchorMax = new Vector2(1f, 1f);
                crt.offsetMin = new Vector2(40f, 0f);
                crt.offsetMax = new Vector2(-24f, -40f);
            }

            return btn;
        }

        /// <summary>Rounded panel (same look as the menu cards, no button behaviour).</summary>
        public static RectTransform CreateRoundedPanel(Transform parent, string name,
            Color color, Vector2 size)
        {
            var go = new GameObject(name, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = RoundedSprite;
            img.type = Image.Type.Sliced;
            img.color = color;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            return rt;
        }

        public static InputField CreateInputField(Transform parent, string name, string placeholder,
            Vector2 size, int fontSize = 32)
        {
            var go = new GameObject(name, typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = size;
            go.GetComponent<Image>().color = PanelLight;

            var field = go.GetComponent<InputField>();

            var textComp = CreateText(go.transform, "Text", "", fontSize, TextColor, TextAnchor.MiddleLeft);
            textComp.raycastTarget = false;
            Stretch((RectTransform)textComp.transform, new Vector2(20, 8), new Vector2(-20, -8));
            textComp.supportRichText = false;

            var ph = CreateText(go.transform, "Placeholder", placeholder, fontSize,
                                TextDim, TextAnchor.MiddleLeft);
            ph.fontStyle = FontStyle.Italic;
            Stretch((RectTransform)ph.transform, new Vector2(20, 8), new Vector2(-20, -8));

            field.textComponent = textComp;
            field.placeholder = ph;
            field.characterLimit = 16;
            return field;
        }

        public static Toggle CreateToggle(Transform parent, string name, string label, bool value,
            UnityEngine.Events.UnityAction<bool> onChanged)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Toggle));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(560, 64);

            // Box background + checkmark.
            var boxRt = CreatePanel(go.transform, "Box", PanelLight,
                new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            boxRt.sizeDelta = new Vector2(48, 48);
            boxRt.anchoredPosition = new Vector2(30, 0);

            var checkGo = new GameObject("Check", typeof(Image));
            checkGo.transform.SetParent(boxRt, false);
            checkGo.GetComponent<Image>().color = AccentGreen;
            Stretch((RectTransform)checkGo.transform, new Vector2(8, 8), new Vector2(-8, -8));

            var labelText = CreateText(go.transform, "Label", label, 32, TextColor, TextAnchor.MiddleLeft);
            var lrt = (RectTransform)labelText.transform;
            Stretch(lrt, new Vector2(80, 0), Vector2.zero);
            labelText.raycastTarget = false;

            var toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = boxRt.GetComponent<Image>();
            toggle.graphic = checkGo.GetComponent<Image>();
            toggle.isOn = value;
            toggle.onValueChanged.AddListener(v => AudioManager.Instance?.PlayClick());
            if (onChanged != null) toggle.onValueChanged.AddListener(onChanged);
            return toggle;
        }

        /// <summary>
        /// Horizontal slider with a label that shows the live value. Used by the
        /// control editor (size / opacity / sensitivity).
        /// </summary>
        public static Slider CreateSlider(Transform parent, string name, string label,
            float min, float max, float value, System.Action<float> onChanged,
            Vector2? size = null, string format = "0.00")
        {
            var holder = new GameObject(name, typeof(RectTransform));
            holder.transform.SetParent(parent, false);
            var hrt = (RectTransform)holder.transform;
            hrt.sizeDelta = size ?? new Vector2(600, 86);

            var caption = CreateText(holder.transform, "Caption", label, 26, TextDim,
                TextAnchor.UpperLeft);
            var crt = (RectTransform)caption.transform;
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.sizeDelta = new Vector2(0, 30);
            crt.anchoredPosition = Vector2.zero;

            var sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderGo.transform.SetParent(holder.transform, false);
            var srt = (RectTransform)sliderGo.transform;
            srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(1f, 0f);
            srt.pivot = new Vector2(0.5f, 0f);
            srt.sizeDelta = new Vector2(0, 44);
            srt.anchoredPosition = new Vector2(0, 6);

            // Track.
            var bg = CreatePanel(sliderGo.transform, "Background", PanelLight,
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0, -10), new Vector2(0, 10));
            // Fill.
            var fillArea = CreatePanel(sliderGo.transform, "FillArea", new Color(0, 0, 0, 0),
                new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0, -10), new Vector2(0, 10));
            var fill = CreatePanel(fillArea, "Fill", Accent,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            // Handle.
            var handleArea = CreatePanel(sliderGo.transform, "HandleArea", new Color(0, 0, 0, 0),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var handle = CreatePanel(handleArea, "Handle", TextColor,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            handle.sizeDelta = new Vector2(36, 44);
            handle.GetComponent<Image>().sprite = CircleSprite;

            var slider = sliderGo.GetComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.value = Mathf.Clamp(value, min, max);
            _ = bg;

            caption.text = $"{label}   {slider.value.ToString(format)}";
            slider.onValueChanged.AddListener(v =>
            {
                caption.text = $"{label}   {v.ToString(format)}";
                onChanged?.Invoke(v);
            });
            return slider;
        }

        /// <summary>Vertical layout group helper for stacking widgets.</summary>
        public static VerticalLayoutGroup AddVerticalLayout(RectTransform rt, float spacing,
            RectOffset padding = null, TextAnchor align = TextAnchor.UpperCenter)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding ?? new RectOffset(20, 20, 20, 20);
            v.childAlignment = align;
            v.childControlWidth = false;
            v.childControlHeight = false;
            v.childForceExpandWidth = false;
            v.childForceExpandHeight = false;
            return v;
        }

        public static void Stretch(RectTransform rt, Vector2? offsetMin = null, Vector2? offsetMax = null)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin ?? Vector2.zero;
            rt.offsetMax = offsetMax ?? Vector2.zero;
        }

        public static void SetAnchoredPos(Component c, Vector2 anchor, Vector2 pos)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
            rt.anchoredPosition = pos;
        }
    }
}
