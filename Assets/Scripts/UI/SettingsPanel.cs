using UnityEngine;
using UnityEngine.UI;
using TankBattle.Core;

namespace TankBattle.UI
{
    /// <summary>
    /// Reusable settings panel (music/SFX toggles + graphics quality buttons).
    /// Used by both the main menu and the in-game pause menu.
    /// </summary>
    public static class SettingsPanel
    {
        /// <summary>Builds the settings widgets inside 'parent'. Returns the root.</summary>
        public static RectTransform Build(Transform parent, System.Action onBack)
        {
            var root = UIFactory.CreateCenterPanel(parent, "SettingsPanel",
                UIFactory.PanelColor, new Vector2(700, 640));
            UIFactory.AddVerticalLayout(root, 24, new RectOffset(40, 40, 30, 30));

            var title = UIFactory.CreateText(root, "Title", "SETTINGS", 44, UIFactory.TextColor);
            ((RectTransform)title.transform).sizeDelta = new Vector2(600, 60);

            UIFactory.CreateToggle(root, "MusicToggle", "Music", SettingsManager.MusicOn,
                v => SettingsManager.MusicOn = v);
            UIFactory.CreateToggle(root, "SfxToggle", "Sound Effects", SettingsManager.SfxOn,
                v => SettingsManager.SfxOn = v);

            var qLabel = UIFactory.CreateText(root, "QualityLabel", "Graphics Quality", 32,
                UIFactory.TextDim);
            ((RectTransform)qLabel.transform).sizeDelta = new Vector2(600, 46);

            // Quality selector row.
            var row = new GameObject("QualityRow", typeof(RectTransform));
            row.transform.SetParent(root, false);
            ((RectTransform)row.transform).sizeDelta = new Vector2(600, 80);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = false; h.childControlHeight = false;
            h.childForceExpandWidth = false; h.childForceExpandHeight = false;

            string[] names = { "Low", "Medium", "High" };
            var buttons = new Button[3];
            for (int i = 0; i < 3; i++)
            {
                int level = i;
                buttons[i] = UIFactory.CreateButton(row.transform, $"Q{i}", names[i],
                    new Vector2(180, 70), UIFactory.PanelLight, () =>
                    {
                        SettingsManager.Quality = level;
                        Highlight(buttons, level);
                    }, 30);
            }
            Highlight(buttons, SettingsManager.Quality);

            UIFactory.CreateButton(root, "Back", "BACK", new Vector2(300, 80),
                UIFactory.Accent, () => onBack?.Invoke());

            return root;
        }

        static void Highlight(Button[] buttons, int selected)
        {
            for (int i = 0; i < buttons.Length; i++)
                buttons[i].GetComponent<Image>().color =
                    i == selected ? UIFactory.Accent : UIFactory.PanelLight;
        }
    }
}
