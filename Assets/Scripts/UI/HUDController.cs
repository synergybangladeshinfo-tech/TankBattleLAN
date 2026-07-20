using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TankBattle.Audio;
using TankBattle.Core;
using TankBattle.Gameplay;
using TankBattle.Networking;

namespace TankBattle.UI
{
    /// <summary>
    /// Builds and drives the entire in-game HUD at runtime:
    ///   virtual joystick + fire button, health bar, match timer, kill counter,
    ///   scoreboard overlay, pause menu, respawn overlay and the win screen.
    /// One instance lives in every map scene (placed by the scene builder).
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        public static HUDController Instance { get; private set; }

        public VirtualJoystick Joystick { get; private set; }
        public FireButton FireButton { get; private set; }

        Canvas _canvas;
        Image _healthFill;
        Text _timerText, _killsText, _respawnText;
        RectTransform _scoreboardPanel, _pausePanel, _winPanel, _settingsPanel;
        Text _scoreboardText, _winTitle, _winBoard;
        float _respawnUntil;
        bool _winShown;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            UIFactory.EnsureEventSystem();
            _canvas = UIFactory.CreateCanvas("HUDCanvas");
            _canvas.transform.SetParent(transform, false);

            BuildControls();
            BuildStatusBar();
            BuildScoreboard();
            BuildPauseMenu();
            BuildWinScreen();
            BuildRespawnOverlay();

            AudioManager.Instance?.PlayBattleMusic();
        }

        void Update()
        {
            var match = MatchManager.Instance;
            if (match == null || !match.IsSpawned) return;

            // Timer (mm:ss).
            int secs = Mathf.CeilToInt(match.TimeRemaining.Value);
            _timerText.text = $"{secs / 60}:{secs % 60:00}";

            // Local kill counter.
            _killsText.text = $"Kills: {match.GetLocalKills()}";

            // Scoreboard refresh while open.
            if (_scoreboardPanel.gameObject.activeSelf)
                _scoreboardText.text = BuildScoreboardString();

            // Respawn countdown.
            if (_respawnText.gameObject.activeSelf)
            {
                float left = _respawnUntil - Time.time;
                _respawnText.text = left > 0f
                    ? $"DESTROYED\nRespawning in {Mathf.CeilToInt(left)}..."
                    : "Respawning...";
            }

            // Win screen once the match ends.
            if (match.MatchEnded.Value && !_winShown) ShowWinScreen();
        }

        /// <summary>Called by the local tank when it spawns.</summary>
        public void BindLocalTank(TankController tank) { /* reserved for future use */ }

        // ---------------------------------------------------------------- pieces

        void BuildControls()
        {
            // --- Joystick (bottom-left) ---
            var joyGo = new GameObject("Joystick", typeof(RectTransform), typeof(Image),
                                       typeof(VirtualJoystick));
            joyGo.transform.SetParent(_canvas.transform, false);
            var joyBg = joyGo.GetComponent<Image>();
            joyBg.sprite = UIFactory.CircleSprite;
            joyBg.color = new Color(1f, 1f, 1f, 0.15f);
            var joyRt = (RectTransform)joyGo.transform;
            joyRt.sizeDelta = new Vector2(280, 280);
            UIFactory.SetAnchoredPos(joyBg, new Vector2(0f, 0f), new Vector2(90, 70));

            var handleGo = new GameObject("Handle", typeof(Image));
            handleGo.transform.SetParent(joyGo.transform, false);
            var handleImg = handleGo.GetComponent<Image>();
            handleImg.sprite = UIFactory.CircleSprite;
            handleImg.color = new Color(1f, 1f, 1f, 0.45f);
            handleImg.raycastTarget = false;
            ((RectTransform)handleGo.transform).sizeDelta = new Vector2(120, 120);

            Joystick = joyGo.GetComponent<VirtualJoystick>();
            Joystick.Init(joyRt, (RectTransform)handleGo.transform);

            // --- Fire button (bottom-right) ---
            var fireGo = new GameObject("FireButton", typeof(RectTransform), typeof(Image),
                                        typeof(FireButton));
            fireGo.transform.SetParent(_canvas.transform, false);
            var fireImg = fireGo.GetComponent<Image>();
            fireImg.sprite = UIFactory.CircleSprite;
            fireImg.color = new Color(1f, 0.30f, 0.25f, 0.55f);
            ((RectTransform)fireGo.transform).sizeDelta = new Vector2(230, 230);
            UIFactory.SetAnchoredPos(fireImg, new Vector2(1f, 0f), new Vector2(-100, 90));
            var fireLabel = UIFactory.CreateText(fireGo.transform, "Label", "FIRE", 40, UIFactory.TextColor);
            fireLabel.fontStyle = FontStyle.Bold;
            UIFactory.Stretch((RectTransform)fireLabel.transform);

            FireButton = fireGo.GetComponent<FireButton>();
        }

        void BuildStatusBar()
        {
            // --- Health bar (top-left) ---
            var hbBack = UIFactory.CreatePanel(_canvas.transform, "HealthBack",
                new Color(0f, 0f, 0f, 0.5f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                Vector2.zero, Vector2.zero);
            hbBack.sizeDelta = new Vector2(420, 44);
            hbBack.pivot = new Vector2(0f, 1f);
            hbBack.anchoredPosition = new Vector2(30, -30);

            var fillRt = UIFactory.CreatePanel(hbBack, "HealthFill", UIFactory.AccentGreen,
                Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4));
            _healthFill = fillRt.GetComponent<Image>();
            _healthFill.type = Image.Type.Filled;
            _healthFill.fillMethod = Image.FillMethod.Horizontal;
            _healthFill.sprite = null;

            // --- Timer (top-center) ---
            _timerText = UIFactory.CreateText(_canvas.transform, "Timer", "5:00", 56, UIFactory.TextColor);
            _timerText.fontStyle = FontStyle.Bold;
            UIFactory.SetAnchoredPos(_timerText, new Vector2(0.5f, 1f), new Vector2(0, -55));

            // --- Kill counter (under health) ---
            _killsText = UIFactory.CreateText(_canvas.transform, "Kills", "Kills: 0", 32, UIFactory.TextColor,
                TextAnchor.UpperLeft);
            UIFactory.SetAnchoredPos(_killsText, new Vector2(0f, 1f), new Vector2(34, -92));

            // --- Scoreboard + pause buttons (top-right) ---
            var scoreBtn = UIFactory.CreateButton(_canvas.transform, "ScoreBtn", "SCORES",
                new Vector2(180, 66), new Color(0f, 0f, 0f, 0.5f),
                () => _scoreboardPanel.gameObject.SetActive(!_scoreboardPanel.gameObject.activeSelf), 26);
            UIFactory.SetAnchoredPos(scoreBtn, new Vector2(1f, 1f), new Vector2(-240, -30));

            var pauseBtn = UIFactory.CreateButton(_canvas.transform, "PauseBtn", "II",
                new Vector2(66, 66), new Color(0f, 0f, 0f, 0.5f),
                () => _pausePanel.gameObject.SetActive(true), 30);
            UIFactory.SetAnchoredPos(pauseBtn, new Vector2(1f, 1f), new Vector2(-30, -30));
        }

        void BuildScoreboard()
        {
            _scoreboardPanel = UIFactory.CreateCenterPanel(_canvas.transform, "Scoreboard",
                UIFactory.PanelColor, new Vector2(720, 500));
            var title = UIFactory.CreateText(_scoreboardPanel, "Title", "SCOREBOARD", 40, UIFactory.TextColor);
            UIFactory.SetAnchoredPos(title, new Vector2(0.5f, 1f), new Vector2(0, -45));
            _scoreboardText = UIFactory.CreateText(_scoreboardPanel, "Rows", "", 32,
                UIFactory.TextColor, TextAnchor.UpperCenter);
            UIFactory.Stretch((RectTransform)_scoreboardText.transform,
                new Vector2(30, 20), new Vector2(-30, -100));
            _scoreboardPanel.gameObject.SetActive(false);
        }

        void BuildPauseMenu()
        {
            _pausePanel = UIFactory.CreatePanel(_canvas.transform, "PauseMenu",
                new Color(0f, 0f, 0f, 0.75f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var box = UIFactory.CreateCenterPanel(_pausePanel, "Box", UIFactory.PanelColor,
                new Vector2(560, 560));
            UIFactory.AddVerticalLayout(box, 20, new RectOffset(30, 30, 30, 30));

            var title = UIFactory.CreateText(box, "Title", "PAUSED", 44, UIFactory.TextColor);
            ((RectTransform)title.transform).sizeDelta = new Vector2(460, 60);
            var hint = UIFactory.CreateText(box, "Hint", "(the battle keeps running!)", 24, UIFactory.TextDim);
            ((RectTransform)hint.transform).sizeDelta = new Vector2(460, 36);

            UIFactory.CreateButton(box, "Resume", "RESUME", new Vector2(460, 88),
                UIFactory.AccentGreen, () => _pausePanel.gameObject.SetActive(false));
            UIFactory.CreateButton(box, "Settings", "SETTINGS", new Vector2(460, 88),
                UIFactory.PanelLight, () =>
                {
                    _settingsPanel ??= SettingsPanel.Build(_canvas.transform,
                        () => _settingsPanel.gameObject.SetActive(false));
                    _settingsPanel.gameObject.SetActive(true);
                });
            UIFactory.CreateButton(box, "Leave", "LEAVE MATCH", new Vector2(460, 88),
                UIFactory.AccentRed, () => ConnectionManager.Instance.Leave());

            _pausePanel.gameObject.SetActive(false);
        }

        void BuildWinScreen()
        {
            _winPanel = UIFactory.CreatePanel(_canvas.transform, "WinScreen",
                new Color(0f, 0f, 0f, 0.85f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var box = UIFactory.CreateCenterPanel(_winPanel, "Box", UIFactory.PanelColor,
                new Vector2(760, 640));
            UIFactory.AddVerticalLayout(box, 20, new RectOffset(30, 30, 30, 30));

            _winTitle = UIFactory.CreateText(box, "Title", "MATCH OVER", 52, UIFactory.TextColor);
            _winTitle.fontStyle = FontStyle.Bold;
            ((RectTransform)_winTitle.transform).sizeDelta = new Vector2(660, 130);

            _winBoard = UIFactory.CreateText(box, "Board", "", 32, UIFactory.TextColor,
                TextAnchor.UpperCenter);
            ((RectTransform)_winBoard.transform).sizeDelta = new Vector2(660, 260);

            // Host can pull everyone back to the lobby for a rematch;
            // anyone can leave on their own.
            var backBtn = UIFactory.CreateButton(box, "BackToLobby", "BACK TO LOBBY",
                new Vector2(560, 84), UIFactory.Accent, () =>
                {
                    NetworkManager.Singleton.SceneManager.LoadScene(
                        GameConstants.MainMenuScene,
                        UnityEngine.SceneManagement.LoadSceneMode.Single);
                });
            backBtn.name = "HostOnly_BackToLobby";

            UIFactory.CreateButton(box, "Leave", "LEAVE", new Vector2(560, 84),
                UIFactory.PanelLight, () => ConnectionManager.Instance.Leave());

            _winPanel.gameObject.SetActive(false);
        }

        void BuildRespawnOverlay()
        {
            _respawnText = UIFactory.CreateText(_canvas.transform, "RespawnOverlay",
                "", 52, UIFactory.AccentRed);
            _respawnText.fontStyle = FontStyle.Bold;
            UIFactory.SetAnchoredPos(_respawnText, new Vector2(0.5f, 0.5f), new Vector2(0, 120));
            _respawnText.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------- api

        /// <summary>Local player's health, 0..1 (called by TankHealth).</summary>
        public void SetHealth(float pct)
        {
            if (_healthFill == null) return;
            _healthFill.fillAmount = pct;
            _healthFill.color = Color.Lerp(UIFactory.AccentRed, UIFactory.AccentGreen, pct);
        }

        public void ShowRespawnOverlay(float seconds)
        {
            _respawnUntil = Time.time + seconds;
            _respawnText.gameObject.SetActive(true);
        }

        public void HideRespawnOverlay() => _respawnText.gameObject.SetActive(false);

        // ----------------------------------------------------------------- inner

        void ShowWinScreen()
        {
            _winShown = true;
            var match = MatchManager.Instance;

            var winner = match.GetWinner();
            bool isMe = winner.ClientId == NetworkManager.Singleton.LocalClientId;
            _winTitle.text = isMe
                ? "VICTORY!"
                : $"{winner.Name}  WINS!";
            _winBoard.text = BuildScoreboardString();

            // Only the host can drag everyone back to the lobby.
            foreach (var b in _winPanel.GetComponentsInChildren<Button>(true))
                if (b.name == "HostOnly_BackToLobby")
                    b.gameObject.SetActive(NetworkManager.Singleton.IsHost);

            _pausePanel.gameObject.SetActive(false);
            _scoreboardPanel.gameObject.SetActive(false);
            _winPanel.gameObject.SetActive(true);
            AudioManager.Instance?.PlayVictory();
        }

        string BuildScoreboardString()
        {
            var match = MatchManager.Instance;
            if (match == null) return "";

            // Copy + sort by kills desc, deaths asc.
            var rows = new System.Collections.Generic.List<ScoreEntry>();
            foreach (var e in match.Scores) rows.Add(e);
            rows.Sort((a, b) => a.Kills != b.Kills
                ? b.Kills.CompareTo(a.Kills)
                : a.Deaths.CompareTo(b.Deaths));

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("PLAYER            KILLS   DEATHS");
            foreach (var e in rows)
                sb.AppendLine($"{e.Name,-16}   {e.Kills,3}      {e.Deaths,3}");
            return sb.ToString();
        }
    }
}
