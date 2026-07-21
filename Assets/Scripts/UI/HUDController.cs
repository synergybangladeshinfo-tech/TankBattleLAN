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
    ///   floating joystick + fire button, health bar, match timer, kill counter,
    ///   current-weapon + ammo readout, per-mode status line (team score, zone
    ///   points, lives, gun-game tier), scoreboard overlay, pause menu,
    ///   respawn overlay and the win screen.
    /// One instance lives in every map scene (placed by the scene builder).
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        public static HUDController Instance { get; private set; }

        public VirtualJoystick Joystick { get; private set; }
        public VirtualJoystick AimJoystick { get; private set; }
        public FireButton FireButton { get; private set; }

        bool _dashQueued, _grenadeQueued;

        /// <summary>Right aim-stick direction (camera-relative), for the turret.</summary>
        public Vector2 AimDirection => AimJoystick != null ? AimJoystick.Direction : Vector2.zero;

        /// <summary>Fire is held while aiming (auto-fire) or the FIRE button is down.</summary>
        public bool FireHeld =>
            AimDirection.sqrMagnitude > 0.04f || (FireButton != null && FireButton.IsPressed);

        /// <summary>One-shot dash request (consumed by TankController).</summary>
        public bool ConsumeDash() { if (_dashQueued) { _dashQueued = false; return true; } return false; }

        /// <summary>One-shot grenade request (consumed by TankShooting).</summary>
        public bool ConsumeGrenade() { if (_grenadeQueued) { _grenadeQueued = false; return true; } return false; }

        Canvas _canvas;
        Image _healthFill;
        Text _timerText, _modeText, _killsText, _respawnText, _weaponText;
        RectTransform _scoreboardPanel, _pausePanel, _winPanel, _settingsPanel;
        Text _scoreboardText, _winTitle, _winBoard;
        float _respawnUntil;
        bool _winShown;
        int _lastTickSecond = -1;
        TankController _localTank;
        TankShooting _localShooting;
        TurretAim _localTurret;
        Image _crosshair;
        RectTransform _targetMarker;

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

            // Subtle vignette (drawn first = under every HUD element).
            var vinGo = new GameObject("Vignette", typeof(Image));
            vinGo.transform.SetParent(_canvas.transform, false);
            var vin = vinGo.GetComponent<Image>();
            vin.sprite = UIFactory.VignetteSprite;
            vin.color = new Color(1f, 1f, 1f, 0.75f);
            vin.raycastTarget = false;
            UIFactory.Stretch((RectTransform)vinGo.transform);

            BuildControls();
            BuildTargeting();
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

            // Timer (mm:ss) + last-10-seconds ticking.
            int secs = Mathf.CeilToInt(match.TimeRemaining.Value);
            _timerText.text = $"{secs / 60}:{secs % 60:00}";
            if (secs <= 10 && secs > 0 && secs != _lastTickSecond && !match.MatchEnded.Value)
            {
                _lastTickSecond = secs;
                AudioManager.Instance?.PlayCountdownTick();
            }
            _timerText.color = secs <= 10 ? UIFactory.AccentRed : UIFactory.TextColor;

            // Mode-specific status line.
            _modeText.text = BuildModeLine(match);

            // Local kill counter.
            _killsText.text = $"Kills: {match.GetLocalKills()}";

            // Weapon + ammo readout (or SHIELDED banner while invincible).
            var localHealth = _localTank != null ? _localTank.GetComponent<TankHealth>() : null;
            if (localHealth != null && localHealth.Shielded.Value)
            {
                _weaponText.text = "SHIELDED";
                _weaponText.color = GameConstants.ShieldColor;
            }
            else if (_localShooting != null)
            {
                var def = Weapons.Get(_localShooting.Weapon.Value);
                _weaponText.text = _localShooting.Ammo.Value < 0
                    ? def.Name
                    : $"{def.Name}  x{_localShooting.Ammo.Value}";
                _weaponText.color = def.BulletColor;
            }

            // Scoreboard refresh while open.
            if (_scoreboardPanel.gameObject.activeSelf)
                _scoreboardText.text = BuildScoreboardString();

            // Respawn countdown.
            if (_respawnText.gameObject.activeSelf)
            {
                float left = _respawnUntil - Time.time;
                bool outOfLives = match.CurrentMode == GameMode.LastTankStanding &&
                                  match.GetLocalEntry().Deaths >= GameConstants.LastTankLives;
                _respawnText.text = outOfLives
                    ? "OUT OF LIVES\nWatch the battle end..."
                    : (left > 0f
                        ? $"DESTROYED\nRespawning in {Mathf.CeilToInt(left)}..."
                        : "Respawning...");
            }

            // Lock-on target marker follows the enemy the turret is tracking.
            UpdateTargeting();

            // Win screen once the match ends.
            if (match.MatchEnded.Value && !_winShown) ShowWinScreen();
        }

        /// <summary>Called by the local tank when it spawns.</summary>
        public void BindLocalTank(TankController tank)
        {
            _localTank = tank;
            _localShooting = tank != null ? tank.GetComponent<TankShooting>() : null;
            _localTurret = tank != null ? tank.GetComponent<TurretAim>() : null;
        }

        // --- targeting reticle: centre crosshair + lock marker on the enemy ---

        void BuildTargeting()
        {
            var chGo = new GameObject("Crosshair", typeof(Image));
            chGo.transform.SetParent(_canvas.transform, false);
            _crosshair = chGo.GetComponent<Image>();
            _crosshair.sprite = UIFactory.ReticleSprite;
            _crosshair.color = new Color(1f, 1f, 1f, 0.45f);
            _crosshair.raycastTarget = false;
            ((RectTransform)chGo.transform).sizeDelta = new Vector2(66, 66);
            UIFactory.SetAnchoredPos(_crosshair, new Vector2(0.5f, 0.5f), Vector2.zero);

            var tmGo = new GameObject("TargetMarker", typeof(Image));
            tmGo.transform.SetParent(_canvas.transform, false);
            var tm = tmGo.GetComponent<Image>();
            tm.sprite = UIFactory.ReticleSprite;
            tm.color = new Color(1f, 0.28f, 0.22f, 0.95f); // red lock
            tm.raycastTarget = false;
            _targetMarker = (RectTransform)tmGo.transform;
            _targetMarker.sizeDelta = new Vector2(120, 120);
            tmGo.SetActive(false);
        }

        void UpdateTargeting()
        {
            if (_targetMarker == null) return;
            var tgt = _localTurret != null ? _localTurret.CurrentTarget : null;
            var cam = Camera.main;
            if (tgt != null && cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(tgt.position + Vector3.up * 1.4f);
                if (sp.z > 0f)
                {
                    if (!_targetMarker.gameObject.activeSelf) _targetMarker.gameObject.SetActive(true);
                    _targetMarker.position = sp;   // screen-space overlay canvas
                    // Gentle pulse so the lock reads clearly.
                    float s = 1f + Mathf.Sin(Time.time * 6f) * 0.08f;
                    _targetMarker.localScale = new Vector3(s, s, 1f);
                    return;
                }
            }
            if (_targetMarker.gameObject.activeSelf) _targetMarker.gameObject.SetActive(false);
        }

        /// <summary>One-line, mode-specific status under the timer.</summary>
        string BuildModeLine(MatchManager match)
        {
            switch (match.CurrentMode)
            {
                case GameMode.TeamDeathmatch:
                    return $"BLUE  {match.TeamAScore.Value}  :  {match.TeamBScore.Value}  RED";
                case GameMode.KingOfTheHill:
                    return $"KING OF THE HILL  ·  ZONE {match.GetLocalEntry().Score}/{GameConstants.KothWinScore}";
                case GameMode.LastTankStanding:
                    int lives = Mathf.Max(0, GameConstants.LastTankLives - match.GetLocalEntry().Deaths);
                    return $"LAST TANK  ·  LIVES {lives}";
                case GameMode.GunGame:
                    int tier = Mathf.Min(match.GetLocalKills() / GameConstants.GunGameKillsPerTier,
                                         Weapons.GunGameOrder.Length - 1);
                    return $"GUN GAME  ·  WEAPON {tier + 1}/{Weapons.GunGameOrder.Length}";
                default:
                    return "DEATHMATCH";
            }
        }

        // ---------------------------------------------------------------- pieces

        void BuildControls()
        {
            // --- Joystick (bottom-left, floating) ---
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

            // Big invisible touch pad so the floating stick catches presses
            // anywhere in the lower-left quadrant.
            var padGo = new GameObject("JoystickPad", typeof(RectTransform), typeof(Image));
            padGo.transform.SetParent(_canvas.transform, false);
            var padImg = padGo.GetComponent<Image>();
            padImg.color = new Color(0f, 0f, 0f, 0f);   // invisible but raycastable
            var padRt = (RectTransform)padGo.transform;
            padRt.anchorMin = Vector2.zero;
            padRt.anchorMax = new Vector2(0.42f, 0.55f);
            padRt.offsetMin = Vector2.zero;
            padRt.offsetMax = Vector2.zero;
            // Forward pad events to the joystick.
            var fwd = padGo.AddComponent<JoystickPadForwarder>();
            fwd.Target = Joystick;
            joyBg.raycastTarget = false; // the pad handles all input

            // --- AIM joystick (bottom-right, floating) - rotates the turret ---
            var aimGo = new GameObject("AimJoystick", typeof(RectTransform), typeof(Image),
                                       typeof(VirtualJoystick));
            aimGo.transform.SetParent(_canvas.transform, false);
            var aimBg = aimGo.GetComponent<Image>();
            aimBg.sprite = UIFactory.CircleSprite;
            aimBg.color = new Color(1f, 0.4f, 0.35f, 0.16f);
            aimBg.raycastTarget = false;
            var aimRt = (RectTransform)aimGo.transform;
            aimRt.sizeDelta = new Vector2(280, 280);
            UIFactory.SetAnchoredPos(aimBg, new Vector2(1f, 0f), new Vector2(-90, 70));

            var aimHandleGo = new GameObject("Handle", typeof(Image));
            aimHandleGo.transform.SetParent(aimGo.transform, false);
            var aimHandle = aimHandleGo.GetComponent<Image>();
            aimHandle.sprite = UIFactory.CircleSprite;
            aimHandle.color = new Color(1f, 0.55f, 0.5f, 0.5f);
            aimHandle.raycastTarget = false;
            ((RectTransform)aimHandleGo.transform).sizeDelta = new Vector2(120, 120);

            AimJoystick = aimGo.GetComponent<VirtualJoystick>();
            AimJoystick.Init(aimRt, (RectTransform)aimHandleGo.transform);

            var aimPadGo = new GameObject("AimPad", typeof(RectTransform), typeof(Image));
            aimPadGo.transform.SetParent(_canvas.transform, false);
            aimPadGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var aimPadRt = (RectTransform)aimPadGo.transform;
            aimPadRt.anchorMin = new Vector2(0.58f, 0f);
            aimPadRt.anchorMax = new Vector2(1f, 0.5f);
            aimPadRt.offsetMin = Vector2.zero; aimPadRt.offsetMax = Vector2.zero;
            aimPadGo.AddComponent<JoystickPadForwarder>().Target = AimJoystick;

            var aimLabel = UIFactory.CreateText(_canvas.transform, "AimLabel", "AIM", 26, UIFactory.TextDim);
            UIFactory.SetAnchoredPos(aimLabel, new Vector2(1f, 0f), new Vector2(-90, 230));

            // --- Action buttons (bottom-centre, between the two sticks) ---
            var fireGo = new GameObject("FireButton", typeof(RectTransform), typeof(Image),
                                        typeof(FireButton));
            fireGo.transform.SetParent(_canvas.transform, false);
            var fireImg = fireGo.GetComponent<Image>();
            fireImg.sprite = UIFactory.CircleSprite;
            fireImg.color = new Color(1f, 0.30f, 0.25f, 0.6f);
            ((RectTransform)fireGo.transform).sizeDelta = new Vector2(150, 150);
            UIFactory.SetAnchoredPos(fireImg, new Vector2(0.5f, 0f), new Vector2(-170, 120));
            var fireLabel = UIFactory.CreateText(fireGo.transform, "Label", "FIRE", 30, UIFactory.TextColor);
            fireLabel.fontStyle = FontStyle.Bold;
            UIFactory.Stretch((RectTransform)fireLabel.transform);
            FireButton = fireGo.GetComponent<FireButton>();

            var dashBtn = UIFactory.CreateButton(_canvas.transform, "Dash", "DASH",
                new Vector2(140, 140), new Color(0.25f, 0.6f, 1f, 0.6f), () => _dashQueued = true, 26);
            var dashImg = dashBtn.GetComponent<Image>(); dashImg.sprite = UIFactory.CircleSprite;
            UIFactory.SetAnchoredPos(dashBtn, new Vector2(0.5f, 0f), new Vector2(0, 120));

            var grenBtn = UIFactory.CreateButton(_canvas.transform, "Grenade", "BOMB",
                new Vector2(140, 140), new Color(0.35f, 0.8f, 0.35f, 0.6f), () => _grenadeQueued = true, 26);
            var grenImg = grenBtn.GetComponent<Image>(); grenImg.sprite = UIFactory.CircleSprite;
            UIFactory.SetAnchoredPos(grenBtn, new Vector2(0.5f, 0f), new Vector2(165, 120));

            // --- Current weapon readout (above the aim stick) ---
            _weaponText = UIFactory.CreateText(_canvas.transform, "Weapon", "CANNON", 34,
                UIFactory.TextColor);
            _weaponText.fontStyle = FontStyle.Bold;
            UIFactory.SetAnchoredPos(_weaponText, new Vector2(1f, 0f), new Vector2(-215, 350));
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

            // --- Timer (top-center) + mode line under it ---
            _timerText = UIFactory.CreateText(_canvas.transform, "Timer", "5:00", 56, UIFactory.TextColor);
            _timerText.fontStyle = FontStyle.Bold;
            UIFactory.SetAnchoredPos(_timerText, new Vector2(0.5f, 1f), new Vector2(0, -55));

            _modeText = UIFactory.CreateText(_canvas.transform, "Mode", "", 28, UIFactory.TextDim);
            UIFactory.SetAnchoredPos(_modeText, new Vector2(0.5f, 1f), new Vector2(0, -105));

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
                UIFactory.PanelColor, new Vector2(860, 720));
            var title = UIFactory.CreateText(_scoreboardPanel, "Title", "SCOREBOARD", 40, UIFactory.TextColor);
            UIFactory.SetAnchoredPos(title, new Vector2(0.5f, 1f), new Vector2(0, -45));
            _scoreboardText = UIFactory.CreateText(_scoreboardPanel, "Rows", "", 26,
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
                new Vector2(860, 700));
            UIFactory.AddVerticalLayout(box, 20, new RectOffset(30, 30, 30, 30));

            _winTitle = UIFactory.CreateText(box, "Title", "MATCH OVER", 52, UIFactory.TextColor);
            _winTitle.fontStyle = FontStyle.Bold;
            ((RectTransform)_winTitle.transform).sizeDelta = new Vector2(760, 120);

            _winBoard = UIFactory.CreateText(box, "Board", "", 26, UIFactory.TextColor,
                TextAnchor.UpperCenter);
            ((RectTransform)_winBoard.transform).sizeDelta = new Vector2(760, 320);

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

            _winTitle.text = match.GetWinnerTitle(NetworkManager.Singleton.LocalClientId);
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
            var mode = match.CurrentMode;

            // Copy + sort: TDM groups by team, otherwise mode metric desc.
            var rows = new System.Collections.Generic.List<ScoreEntry>();
            foreach (var e in match.Scores) rows.Add(e);
            rows.Sort((a, b) =>
            {
                if (mode == GameMode.TeamDeathmatch && a.Team != b.Team)
                    return a.Team.CompareTo(b.Team);
                int ma = mode == GameMode.KingOfTheHill ? a.Score : a.Kills;
                int mb = mode == GameMode.KingOfTheHill ? b.Score : b.Kills;
                return ma != mb ? mb.CompareTo(ma) : a.Deaths.CompareTo(b.Deaths);
            });

            var sb = new System.Text.StringBuilder();
            switch (mode)
            {
                case GameMode.KingOfTheHill:
                    sb.AppendLine("PLAYER            ZONE   KILLS   DEATHS");
                    foreach (var e in rows)
                        sb.AppendLine($"{e.Name,-16}  {e.Score,4}   {e.Kills,3}      {e.Deaths,3}");
                    break;
                case GameMode.TeamDeathmatch:
                    sb.AppendLine("TEAM  PLAYER            KILLS   DEATHS");
                    foreach (var e in rows)
                        sb.AppendLine($"{(e.Team == 0 ? "BLUE" : "RED "),-5} {e.Name,-16}  {e.Kills,3}      {e.Deaths,3}");
                    break;
                case GameMode.LastTankStanding:
                    sb.AppendLine("PLAYER            LIVES   KILLS");
                    foreach (var e in rows)
                        sb.AppendLine($"{e.Name,-16}   {Mathf.Max(0, GameConstants.LastTankLives - e.Deaths),3}     {e.Kills,3}");
                    break;
                case GameMode.GunGame:
                    sb.AppendLine("PLAYER            WEAPON   KILLS");
                    foreach (var e in rows)
                        sb.AppendLine($"{e.Name,-16}   {Mathf.Min(e.Kills / GameConstants.GunGameKillsPerTier + 1, Weapons.GunGameOrder.Length),2}/{Weapons.GunGameOrder.Length}     {e.Kills,3}");
                    break;
                default:
                    sb.AppendLine("PLAYER            KILLS   DEATHS");
                    foreach (var e in rows)
                        sb.AppendLine($"{e.Name,-16}   {e.Kills,3}      {e.Deaths,3}");
                    break;
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Forwards pointer events from the big invisible lower-left touch pad to
    /// the floating joystick, so pressing anywhere in that region grabs it.
    /// </summary>
    public class JoystickPadForwarder : MonoBehaviour,
        UnityEngine.EventSystems.IPointerDownHandler,
        UnityEngine.EventSystems.IDragHandler,
        UnityEngine.EventSystems.IPointerUpHandler
    {
        public VirtualJoystick Target;

        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) => Target?.OnPointerDown(e);
        public void OnDrag(UnityEngine.EventSystems.PointerEventData e) => Target?.OnDrag(e);
        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e) => Target?.OnPointerUp(e);
    }
}
