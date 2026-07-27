using UnityEngine;
using UnityEngine.UI;
using TankBattle.Core;

namespace TankBattle.UI
{
    /// <summary>
    /// The settings screen, used by both the main menu and the in-game pause
    /// menu.
    ///
    /// v3.1: rebuilt as a two-column card layout with far more control - the old
    /// version only had three toggles and a quality row. Everything here is
    /// wired to something real: the volume sliders scale AudioManager, the shake
    /// toggle gates CameraFollow.Shake, aim assist gates the turret lock-on, and
    /// the frame cap sets Application.targetFrameRate.
    ///
    /// The action bar is anchored to the panel's bottom edge rather than being
    /// the last child of a vertical layout, so it can never be pushed off the
    /// panel by the content above it.
    /// </summary>
    public static class SettingsPanel
    {
        /// <summary>Builds the settings screen inside 'parent'. Returns the root.</summary>
        public static RectTransform Build(Transform parent, System.Action onBack)
        {
            var root = UIFactory.CreateCenterPanel(parent, "SettingsPanel",
                UIFactory.PanelColor, new Vector2(1380, 880));
            var rootImg = root.GetComponent<Image>();
            rootImg.sprite = UIFactory.RoundedSprite;
            rootImg.type = Image.Type.Sliced;

            // ---------------------------------------------------------- header
            var header = UIFactory.CreatePanel(root, "Header",
                new Color(1f, 0.66f, 0.20f, 0.10f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0, 92);
            header.anchoredPosition = Vector2.zero;
            header.GetComponent<Image>().raycastTarget = false;

            var title = UIFactory.CreateText(header, "Title", "SETTINGS", 44,
                UIFactory.TextColor, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            UIFactory.SetAnchoredPos(title, new Vector2(0f, 0.5f), new Vector2(44, 0));
            ((RectTransform)title.transform).sizeDelta = new Vector2(400, 56);

            var saved = UIFactory.CreateText(header, "SavedHint", "saved automatically",
                24, UIFactory.TextDim, TextAnchor.MiddleRight);
            UIFactory.SetAnchoredPos(saved, new Vector2(1f, 0.5f), new Vector2(-44, 0));
            ((RectTransform)saved.transform).sizeDelta = new Vector2(500, 40);

            // ------------------------------------------------------- two columns
            var left = Column(root, "AudioColumn", new Vector2(34, -108));
            var right = Column(root, "GameColumn", new Vector2(-34, -108), rightSide: true);

            // ---- left: audio + feedback ----
            CardLabel(left, "AudioHeading", "AUDIO");

            UIFactory.CreateSlider(left, "MusicVol", "Music volume",
                0f, 1f, SettingsManager.MusicVolume,
                v => SettingsManager.MusicVolume = v,
                new Vector2(600, 84), "0%");

            UIFactory.CreateSlider(left, "SfxVol", "Effects volume",
                0f, 1f, SettingsManager.SfxVolume,
                v => SettingsManager.SfxVolume = v,
                new Vector2(600, 84), "0%");

            UIFactory.CreateToggle(left, "MusicToggle", "Music on",
                SettingsManager.MusicOn, v => SettingsManager.MusicOn = v);
            UIFactory.CreateToggle(left, "SfxToggle", "Sound effects on",
                SettingsManager.SfxOn, v => SettingsManager.SfxOn = v);
            UIFactory.CreateToggle(left, "VibrateToggle", "Vibration",
                SettingsManager.VibrationOn, v => SettingsManager.VibrationOn = v);
            UIFactory.CreateToggle(left, "ShakeToggle", "Camera shake",
                SettingsManager.CameraShakeOn, v => SettingsManager.CameraShakeOn = v);

            // ---- right: gameplay + display ----
            CardLabel(right, "GameHeading", "GAMEPLAY");

            UIFactory.CreateToggle(right, "AimToggle", "Aim assist (turret lock-on)",
                SettingsManager.AimAssistOn, v => SettingsManager.AimAssistOn = v);
            UIFactory.CreateToggle(right, "MinimapToggle", "Show minimap",
                SettingsManager.ShowMinimap, v => SettingsManager.ShowMinimap = v);
            UIFactory.CreateToggle(right, "FpsToggle", "Show FPS counter",
                SettingsManager.ShowFps, v => SettingsManager.ShowFps = v);

            CardLabel(right, "DisplayHeading", "DISPLAY");

            Selector(right, "QualityRow", "Graphics quality",
                new[] { "LOW", "MEDIUM", "HIGH" }, SettingsManager.Quality,
                i => SettingsManager.Quality = i);

            Selector(right, "FrameRow", "Frame rate",
                new[] { "30 FPS", "60 FPS", "MAX" }, SettingsManager.FrameCap,
                i => SettingsManager.FrameCap = i);

            // ------------------------------------------------------- action bar
            var bar = UIFactory.CreatePanel(root, "ActionBar",
                new Color(0f, 0f, 0f, 0.22f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(0, 112);
            bar.anchoredPosition = Vector2.zero;
            bar.GetComponent<Image>().raycastTarget = false;

            var back = UIFactory.CreateButton(bar, "Back", "BACK",
                new Vector2(360, 80), UIFactory.Accent, () => onBack?.Invoke());
            UIFactory.SetAnchoredPos(back, new Vector2(0.5f, 0.5f), new Vector2(180, 0));

            var reset = UIFactory.CreateButton(bar, "Defaults", "RESET TO DEFAULTS",
                new Vector2(420, 80), UIFactory.PanelLight, () =>
                {
                    SettingsManager.MusicVolume = 0.55f;
                    SettingsManager.SfxVolume = 1f;
                    SettingsManager.MusicOn = true;
                    SettingsManager.SfxOn = true;
                    SettingsManager.VibrationOn = true;
                    SettingsManager.CameraShakeOn = true;
                    SettingsManager.AimAssistOn = true;
                    SettingsManager.ShowMinimap = true;
                    SettingsManager.ShowFps = false;
                    SettingsManager.Quality = 1;
                    SettingsManager.FrameCap = 1;
                    // Close back out: the panel is rebuilt from scratch next
                    // time the menu opens it, so every widget picks up the
                    // defaults without destroying a panel we still hold on to.
                    onBack?.Invoke();
                }, 26);
            UIFactory.SetAnchoredPos(reset, new Vector2(0.5f, 0.5f), new Vector2(-230, 0));

            return root;
        }

        // ------------------------------------------------------------- helpers

        static RectTransform Column(RectTransform parent, string name, Vector2 offset,
                                    bool rightSide = false)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(rightSide ? 1f : 0f, 1f);
            rt.pivot = new Vector2(rightSide ? 1f : 0f, 1f);
            rt.sizeDelta = new Vector2(636, 640);
            rt.anchoredPosition = offset;

            var bg = go.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.035f);
            bg.sprite = UIFactory.RoundedSprite;
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;

            UIFactory.AddVerticalLayout(rt, 14, new RectOffset(18, 18, 18, 18));
            return rt;
        }

        static void CardLabel(RectTransform parent, string name, string text)
        {
            var t = UIFactory.CreateText(parent, name, text, 24,
                new Color(1f, 0.66f, 0.20f, 1f), TextAnchor.MiddleLeft);
            t.fontStyle = FontStyle.Bold;
            ((RectTransform)t.transform).sizeDelta = new Vector2(600, 34);
        }

        /// <summary>Caption plus a row of mutually exclusive option buttons.</summary>
        static void Selector(RectTransform parent, string name, string caption,
                             string[] options, int selected, System.Action<int> onPick)
        {
            var holder = new GameObject(name, typeof(RectTransform));
            holder.transform.SetParent(parent, false);
            var hrt = (RectTransform)holder.transform;
            hrt.sizeDelta = new Vector2(600, 108);

            var cap = UIFactory.CreateText(holder.transform, "Caption", caption, 26,
                UIFactory.TextDim, TextAnchor.UpperLeft);
            var crt = (RectTransform)cap.transform;
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.sizeDelta = new Vector2(0, 30);
            crt.anchoredPosition = Vector2.zero;

            var row = new GameObject("Row", typeof(RectTransform));
            row.transform.SetParent(holder.transform, false);
            var rrt = (RectTransform)row.transform;
            rrt.anchorMin = new Vector2(0f, 0f); rrt.anchorMax = new Vector2(1f, 0f);
            rrt.pivot = new Vector2(0.5f, 0f);
            rrt.sizeDelta = new Vector2(0, 66);
            rrt.anchoredPosition = Vector2.zero;
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = false; h.childControlHeight = false;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;

            float w = (600f - 12f * (options.Length - 1)) / options.Length;
            var buttons = new Button[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                buttons[i] = UIFactory.CreateButton(rrt, $"Opt{i}", options[i],
                    new Vector2(w, 64), UIFactory.PanelLight, () =>
                    {
                        onPick?.Invoke(index);
                        Highlight(buttons, index);
                    }, 26);
            }
            Highlight(buttons, selected);
        }

        static void Highlight(Button[] buttons, int selected)
        {
            for (int i = 0; i < buttons.Length; i++)
                buttons[i].GetComponent<Image>().color =
                    i == selected ? UIFactory.Accent : UIFactory.PanelLight;
        }
    }
}
