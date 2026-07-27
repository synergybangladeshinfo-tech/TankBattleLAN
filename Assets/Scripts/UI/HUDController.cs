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
        public FireButton HoverButton { get; private set; }

        /// <summary>Kill feed / streak banner (server pushes messages into it).</summary>
        public KillFeed Feed { get; private set; }

        bool _dashQueued, _grenadeQueued;

        /// <summary>Right aim-stick direction (camera-relative), for the turret.</summary>
        public Vector2 AimDirection => AimJoystick != null ? AimJoystick.Direction : Vector2.zero;

        /// <summary>
        /// Fire is held while the FIRE button is down, or - when the player has
        /// auto-fire enabled in the control settings - simply while aiming.
        /// </summary>
        public bool FireHeld =>
            (FireButton != null && FireButton.IsPressed) ||
            (ControlLayout.AutoFire && AimDirection.sqrMagnitude > 0.04f);

        /// <summary>True while the HOVER button is held (jetpack lift).</summary>
        public bool HoverHeld => HoverButton != null && HoverButton.IsPressed;

        /// <summary>One-shot dash request (consumed by TankController).</summary>
        public bool ConsumeDash() { if (_dashQueued) { _dashQueued = false; return true; } return false; }

        /// <summary>One-shot grenade request (consumed by TankShooting).</summary>
        public bool ConsumeGrenade() { if (_grenadeQueued) { _grenadeQueued = false; return true; } return false; }

        Canvas _canvas;
        Image _healthFill;
        Image _hoverFuelFill;
        Text _timerText, _modeText, _killsText, _respawnText, _weaponText;
        RectTransform _scoreboardPanel, _pausePanel, _winPanel, _settingsPanel;
        RectTransform _hoverFuelBar;
        Text _scoreboardText, _winTitle, _winBoard, _xpText, _countdownText;
        RectTransform _chatPanel;
        int _lastCountdownShown = -1;
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

            // Minimap (top-right) with the kill feed stacked underneath it.
            Minimap.Build(_canvas.transform, 250f);
            Feed = KillFeed.Build(_canvas.transform, 380f);

            BuildScoreboard();
            BuildPauseMenu();
            BuildWinScreen();
            BuildRespawnOverlay();
            BuildCountdown();
            BuildQuickChat();

            AudioManager.Instance?.PlayBattleMusic();
        }

        void Update()
        {
            var match = MatchManager.Instance;
            if (match == null || !match.IsSpawned) return;

            UpdateCountdown(match);

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

            // Hover fuel bar (hidden until the tank actually has a jetpack read).
            if (_hoverFuelFill != null && _localTank != null)
            {
                float fuel = _localTank.HoverFuel01;
                _hoverFuelFill.fillAmount = fuel;
                _hoverFuelFill.color = fuel < 0.25f
                    ? new Color(1f, 0.35f, 0.25f, 0.95f)
                    : new Color(0.95f, 0.75f, 0.2f, 0.95f);
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

        /// <summary>
        /// Places every control exactly where the player's saved layout says
        /// (see ControlLayout / the CONTROLS editor). All controls are anchored
        /// to the canvas bottom-left corner with a centred pivot, so a stored
        /// position is simply "the centre of this control in 1920x1080 space".
        /// </summary>
        void PlaceControl(Component c, ControlId id)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            float size = ControlLayout.SizeOf(id);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = ControlLayout.GetPos(id);
        }

        void BuildControls()
        {
            float op = ControlLayout.Opacity;

            // --- Movement joystick (floating: re-centres under your thumb) ---
            var joyGo = new GameObject("Joystick", typeof(RectTransform), typeof(Image),
                                       typeof(VirtualJoystick));
            joyGo.transform.SetParent(_canvas.transform, false);
            var joyBg = joyGo.GetComponent<Image>();
            joyBg.sprite = UIFactory.CircleSprite;
            joyBg.color = new Color(1f, 1f, 1f, 0.25f * op);
            joyBg.raycastTarget = false;              // the pad below handles input
            PlaceControl(joyBg, ControlId.MoveStick);
            var joyRt = (RectTransform)joyGo.transform;

            var handleGo = new GameObject("Handle", typeof(Image));
            handleGo.transform.SetParent(joyGo.transform, false);
            var handleImg = handleGo.GetComponent<Image>();
            handleImg.sprite = UIFactory.CircleSprite;
            handleImg.color = new Color(1f, 1f, 1f, 0.75f * op);
            handleImg.raycastTarget = false;
            ((RectTransform)handleGo.transform).sizeDelta =
                new Vector2(joyRt.sizeDelta.x * 0.43f, joyRt.sizeDelta.y * 0.43f);

            Joystick = joyGo.GetComponent<VirtualJoystick>();
            Joystick.Init(joyRt, (RectTransform)handleGo.transform);

            // --- Aim joystick (rotates the turret) ---
            var aimGo = new GameObject("AimJoystick", typeof(RectTransform), typeof(Image),
                                       typeof(VirtualJoystick));
            aimGo.transform.SetParent(_canvas.transform, false);
            var aimBg = aimGo.GetComponent<Image>();
            aimBg.sprite = UIFactory.CircleSprite;
            aimBg.color = new Color(1f, 0.45f, 0.4f, 0.26f * op);
            aimBg.raycastTarget = false;
            PlaceControl(aimBg, ControlId.AimStick);
            var aimRt = (RectTransform)aimGo.transform;

            var aimHandleGo = new GameObject("Handle", typeof(Image));
            aimHandleGo.transform.SetParent(aimGo.transform, false);
            var aimHandle = aimHandleGo.GetComponent<Image>();
            aimHandle.sprite = UIFactory.CircleSprite;
            aimHandle.color = new Color(1f, 0.6f, 0.55f, 0.8f * op);
            aimHandle.raycastTarget = false;
            ((RectTransform)aimHandleGo.transform).sizeDelta =
                new Vector2(aimRt.sizeDelta.x * 0.43f, aimRt.sizeDelta.y * 0.43f);

            AimJoystick = aimGo.GetComponent<VirtualJoystick>();
            AimJoystick.Init(aimRt, (RectTransform)aimHandleGo.transform);

            // Invisible touch pads: whichever stick sits further left owns the
            // left half of the screen, so a mirrored/custom layout still works.
            bool moveOnLeft = ControlLayout.GetPos(ControlId.MoveStick).x <=
                              ControlLayout.GetPos(ControlId.AimStick).x;
            CreateStickPad("JoystickPad", moveOnLeft, Joystick);
            CreateStickPad("AimPad", !moveOnLeft, AimJoystick);

            // --- FIRE (hold) ---
            var fireGo = new GameObject("FireButton", typeof(RectTransform), typeof(Image),
                                        typeof(FireButton));
            fireGo.transform.SetParent(_canvas.transform, false);
            var fireImg = fireGo.GetComponent<Image>();
            fireImg.sprite = UIFactory.CircleSprite;
            fireImg.color = new Color(1f, 0.30f, 0.25f, op);
            PlaceControl(fireImg, ControlId.Fire);
            var fireLabel = UIFactory.CreateText(fireGo.transform, "Label", "FIRE", 30,
                UIFactory.TextColor);
            fireLabel.fontStyle = FontStyle.Bold;
            UIFactory.Stretch((RectTransform)fireLabel.transform);
            FireButton = fireGo.GetComponent<FireButton>();

            // --- DASH (tap) ---
            var dashBtn = UIFactory.CreateButton(_canvas.transform, "Dash", "DASH",
                Vector2.one * ControlLayout.SizeOf(ControlId.Dash),
                new Color(0.25f, 0.6f, 1f, op), () => _dashQueued = true, 26);
            dashBtn.GetComponent<Image>().sprite = UIFactory.CircleSprite;
            PlaceControl(dashBtn, ControlId.Dash);

            // --- BOMB (tap) ---
            var grenBtn = UIFactory.CreateButton(_canvas.transform, "Grenade", "BOMB",
                Vector2.one * ControlLayout.SizeOf(ControlId.Bomb),
                new Color(0.35f, 0.8f, 0.35f, op), () => _grenadeQueued = true, 26);
            grenBtn.GetComponent<Image>().sprite = UIFactory.CircleSprite;
            PlaceControl(grenBtn, ControlId.Bomb);

            // --- HOVER (hold to rise, jetpack style) ---
            var hoverGo = new GameObject("HoverButton", typeof(RectTransform), typeof(Image),
                                         typeof(FireButton));
            hoverGo.transform.SetParent(_canvas.transform, false);
            var hoverImg = hoverGo.GetComponent<Image>();
            hoverImg.sprite = UIFactory.CircleSprite;
            hoverImg.color = new Color(0.95f, 0.75f, 0.2f, op);
            PlaceControl(hoverImg, ControlId.Hover);
            var hoverLabel = UIFactory.CreateText(hoverGo.transform, "Label", "HOVER", 24,
                UIFactory.TextColor);
            hoverLabel.fontStyle = FontStyle.Bold;
            UIFactory.Stretch((RectTransform)hoverLabel.transform);
            HoverButton = hoverGo.GetComponent<FireButton>();

            // Fuel bar hugging the hover button.
            _hoverFuelBar = UIFactory.CreatePanel(_canvas.transform, "HoverFuelBack",
                new Color(0f, 0f, 0f, 0.55f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                Vector2.zero, Vector2.zero);
            _hoverFuelBar.pivot = new Vector2(0.5f, 0.5f);
            _hoverFuelBar.sizeDelta = new Vector2(ControlLayout.SizeOf(ControlId.Hover), 14f);
            _hoverFuelBar.anchoredPosition = ControlLayout.GetPos(ControlId.Hover) +
                new Vector2(0f, ControlLayout.SizeOf(ControlId.Hover) * 0.5f + 14f);

            var fuelFill = UIFactory.CreatePanel(_hoverFuelBar, "Fill",
                new Color(0.95f, 0.75f, 0.2f, 0.95f), Vector2.zero, Vector2.one,
                new Vector2(2, 2), new Vector2(-2, -2));
            _hoverFuelFill = fuelFill.GetComponent<Image>();
            _hoverFuelFill.type = Image.Type.Filled;
            _hoverFuelFill.fillMethod = Image.FillMethod.Horizontal;

            // --- Current weapon readout (above the aim stick) ---
            _weaponText = UIFactory.CreateText(_canvas.transform, "Weapon", "CANNON", 34,
                UIFactory.TextColor);
            _weaponText.fontStyle = FontStyle.Bold;
            var wrt = (RectTransform)_weaponText.transform;
            wrt.anchorMin = wrt.anchorMax = new Vector2(0f, 0f);
            wrt.pivot = new Vector2(0.5f, 0.5f);
            wrt.anchoredPosition = ControlLayout.GetPos(ControlId.AimStick) +
                new Vector2(0f, ControlLayout.SizeOf(ControlId.AimStick) * 0.5f + 40f);
        }

        /// <summary>
        /// Big invisible half-screen pad that hands every press in its half to
        /// the floating stick, so you never have to hit the circle exactly.
        /// </summary>
        void CreateStickPad(string name, bool leftHalf, VirtualJoystick target)
        {
            var padGo = new GameObject(name, typeof(RectTransform), typeof(Image));
            padGo.transform.SetParent(_canvas.transform, false);
            padGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // invisible, raycastable
            var rt = (RectTransform)padGo.transform;
            rt.anchorMin = leftHalf ? new Vector2(0f, 0f) : new Vector2(0.5f, 0f);
            rt.anchorMax = leftHalf ? new Vector2(0.5f, 0.62f) : new Vector2(1f, 0.62f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            padGo.AddComponent<JoystickPadForwarder>().Target = target;
            padGo.transform.SetAsFirstSibling(); // never steal taps from real buttons
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

        /// <summary>Big centre "3 - 2 - 1 - FIGHT!" banner.</summary>
        void BuildCountdown()
        {
            _countdownText = UIFactory.CreateText(_canvas.transform, "Countdown", "",
                140, UIFactory.TextColor);
            _countdownText.fontStyle = FontStyle.Bold;
            UIFactory.SetAnchoredPos(_countdownText, new Vector2(0.5f, 0.5f), new Vector2(0, 60));
            _countdownText.gameObject.SetActive(false);
        }

        /// <summary>
        /// Counts 3, 2, 1 then flashes FIGHT!. Tanks are frozen by MatchManager
        /// while this is running, so everyone starts at the same instant.
        /// </summary>
        void UpdateCountdown(MatchManager match)
        {
            if (_countdownText == null) return;

            float left = match.Countdown.Value;
            if (left <= 0f)
            {
                // Hold "FIGHT!" briefly after the freeze lifts, then hide.
                if (_lastCountdownShown != 0)
                {
                    _lastCountdownShown = 0;
                    _countdownText.text = "FIGHT!";
                    _countdownText.color = UIFactory.AccentGreen;
                    _countdownText.gameObject.SetActive(true);
                    AudioManager.Instance?.PlayVictory();
                    CancelInvoke(nameof(HideCountdown));
                    Invoke(nameof(HideCountdown), 1.1f);
                }
                return;
            }

            int n = Mathf.CeilToInt(left - 1f);   // last second is reserved for FIGHT!
            if (n < 1) n = 1;
            if (n != _lastCountdownShown)
            {
                _lastCountdownShown = n;
                AudioManager.Instance?.PlayCountdownTick();
            }

            _countdownText.gameObject.SetActive(true);
            _countdownText.text = n.ToString();
            _countdownText.color = UIFactory.TextColor;

            // Pop each number as it appears.
            float frac = 1f - ((left - 1f) - Mathf.Floor(left - 1f));
            float scale = Mathf.Lerp(1.5f, 1f, Mathf.Clamp01(frac * 2.2f));
            _countdownText.transform.localScale = new Vector3(scale, scale, 1f);
        }

        void HideCountdown()
        {
            if (_countdownText != null) _countdownText.gameObject.SetActive(false);
        }

        /// <summary>Four canned phrases you can fire off without typing.</summary>
        void BuildQuickChat()
        {
            var toggle = UIFactory.CreateButton(_canvas.transform, "ChatBtn", "CHAT",
                new Vector2(120, 60), new Color(0.2f, 0.45f, 0.7f, 0.75f),
                () => _chatPanel.gameObject.SetActive(!_chatPanel.gameObject.activeSelf), 24);
            toggle.GetComponent<Image>().sprite = UIFactory.RoundedSprite;
            toggle.GetComponent<Image>().type = Image.Type.Sliced;
            UIFactory.SetAnchoredPos(toggle, new Vector2(1f, 1f), new Vector2(-380, -30));

            _chatPanel = UIFactory.CreateRoundedPanel(_canvas.transform, "QuickChat",
                UIFactory.PanelColor, new Vector2(360, 300));
            _chatPanel.anchorMin = _chatPanel.anchorMax = _chatPanel.pivot = new Vector2(1f, 1f);
            _chatPanel.anchoredPosition = new Vector2(-320, -100);
            UIFactory.AddVerticalLayout(_chatPanel, 10, new RectOffset(16, 16, 16, 16));

            for (int i = 0; i < GameConstants.QuickChatPhrases.Length; i++)
            {
                int index = i;
                UIFactory.CreateButton(_chatPanel, $"Phrase{i}",
                    GameConstants.QuickChatPhrases[i], new Vector2(320, 58),
                    UIFactory.PanelLight, () =>
                    {
                        if (MatchManager.Instance != null)
                            MatchManager.Instance.QuickChatServerRpc(index);
                        _chatPanel.gameObject.SetActive(false);
                    }, 24);
            }
            _chatPanel.gameObject.SetActive(false);
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

            // XP / rank line: filled in when the match ends.
            _xpText = UIFactory.CreateText(box, "Xp", "", 30, UIFactory.AccentGreen);
            _xpText.fontStyle = FontStyle.Bold;
            ((RectTransform)_xpText.transform).sizeDelta = new Vector2(760, 76);

            _winBoard = UIFactory.CreateText(box, "Board", "", 26, UIFactory.TextColor,
                TextAnchor.UpperCenter);
            ((RectTransform)_winBoard.transform).sizeDelta = new Vector2(760, 280);

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

            ulong localId = NetworkManager.Singleton.LocalClientId;
            _winTitle.text = match.GetWinnerTitle(localId);
            _winBoard.text = BuildScoreboardString();

            // Match summary line (kills / deaths this round).
            if (_xpText != null)
            {
                var entry = match.GetLocalEntry();
                _xpText.color = UIFactory.TextDim;
                _xpText.text = $"your round:  {entry.Kills} kills  ·  {entry.Deaths} deaths\n"
                             + BuildAwards(match);
            }

            // Only the host can drag everyone back to the lobby.
            foreach (var b in _winPanel.GetComponentsInChildren<Button>(true))
                if (b.name == "HostOnly_BackToLobby")
                    b.gameObject.SetActive(NetworkManager.Singleton.IsHost);

            _pausePanel.gameObject.SetActive(false);
            _scoreboardPanel.gameObject.SetActive(false);
            _winPanel.gameObject.SetActive(true);
            AudioManager.Instance?.PlayVictory();
        }

        /// <summary>
        /// Fun end-of-match awards pulled straight from the scoreboard: top
        /// killer, best kill/death ratio and whoever died least.
        /// </summary>
        string BuildAwards(MatchManager match)
        {
            if (match.Scores.Count == 0) return "";

            ScoreEntry topKills = default, bestRatio = default, survivor = default;
            bool first = true;
            float bestRatioValue = -1f;

            foreach (var e in match.Scores)
            {
                if (first)
                {
                    topKills = bestRatio = survivor = e;
                    bestRatioValue = e.Kills / Mathf.Max(1f, e.Deaths);
                    first = false;
                    continue;
                }
                if (e.Kills > topKills.Kills) topKills = e;
                if (e.Deaths < survivor.Deaths) survivor = e;

                float r = e.Kills / Mathf.Max(1f, e.Deaths);
                if (r > bestRatioValue) { bestRatioValue = r; bestRatio = e; }
            }

            var sb = new System.Text.StringBuilder();
            if (topKills.Kills > 0)
                sb.Append($"TOP GUN  {topKills.Name} ({topKills.Kills})     ");
            if (bestRatioValue > 0f)
                sb.Append($"BEST RATIO  {bestRatio.Name}     ");
            sb.Append($"HARDEST TO KILL  {survivor.Name} ({survivor.Deaths} deaths)");
            return sb.ToString();
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
