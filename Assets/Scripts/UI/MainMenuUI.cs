using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TankBattle.Audio;
using TankBattle.Core;
using TankBattle.Networking;

namespace TankBattle.UI
{
    /// <summary>
    /// Builds and drives the whole main menu at runtime:
    ///   Home   -> player name + HOST / JOIN / GARAGE / SETTINGS / QUIT
    ///   Garage -> pick tank color (8) and body style (3)
    ///   Host   -> pick map, game mode (5) and match length, start hosting
    ///   Join   -> live list of LAN hosts discovered via UDP broadcast
    ///   Lobby  -> replicated player list; host presses START MATCH
    /// Also handles returning from a finished match while still connected
    /// (drops everyone straight back into the lobby).
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        Canvas _canvas;
        RectTransform _homePanel, _garagePanel, _hostPanel, _joinPanel, _lobbyPanel, _settingsPanel;
        InputField _nameField;
        Text _noticeText, _joinStatusText, _lobbyPlayersText, _lobbyStatusText;
        Text _modeHintText, _garagePreview;
        RectTransform _hostListRoot;
        Button _startMatchButton;
        Text _hostTitle, _startHostLabel;
        bool _soloIntent;
        int _selectedMap, _selectedMode, _selectedTime;
        readonly List<Button> _mapButtons = new List<Button>();
        readonly List<Button> _modeButtons = new List<Button>();
        readonly List<Button> _timeButtons = new List<Button>();
        readonly List<Button> _colorButtons = new List<Button>();
        readonly List<Button> _styleButtons = new List<Button>();
        float _nextHostListRefresh;

        void Start()
        {
            UIFactory.EnsureEventSystem();
            _canvas = UIFactory.CreateCanvas("MenuCanvas");
            _canvas.transform.SetParent(transform, false);

            // Restore the Garage choices before anything reads them.
            GameSession.TankColorIndex = SettingsManager.SavedTankColor;
            GameSession.TankStyleIndex = SettingsManager.SavedTankStyle;

            BuildBackground();
            BuildHomePanel();
            BuildGaragePanel();
            BuildHostPanel();
            BuildJoinPanel();
            BuildLobbyPanel();
            _settingsPanel = SettingsPanel.Build(_canvas.transform, () => Show(_homePanel));

            _selectedMap = GameSession.SelectedMapIndex;
            _selectedMode = GameSession.SelectedModeIndex;
            _selectedTime = GameSession.SelectedTimeIndex;
            HighlightSelectors();
            AudioManager.Instance?.PlayMenuMusic();

            // Returning from a match while still connected -> straight to lobby.
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening) Show(_lobbyPanel);
            else Show(_homePanel);

            // One-shot notice (e.g. "Host disconnected").
            if (!string.IsNullOrEmpty(GameSession.MenuNotice))
            {
                _noticeText.text = GameSession.MenuNotice;
                GameSession.MenuNotice = null;
            }
        }

        void Update()
        {
            // Live-refresh the host list while the join panel is open.
            if (_joinPanel.gameObject.activeSelf && Time.unscaledTime >= _nextHostListRefresh)
            {
                _nextHostListRefresh = Time.unscaledTime + 0.5f;
                RefreshHostList();
            }

            // Live-refresh the lobby player list.
            if (_lobbyPanel.gameObject.activeSelf)
                RefreshLobby();
        }

        // ------------------------------------------------------------------ build

        void BuildBackground()
        {
            UIFactory.CreatePanel(_canvas.transform, "Background",
                new Color(0.05f, 0.07f, 0.10f, 1f), Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero);

            var title = UIFactory.CreateText(_canvas.transform, "Title", "TANK BATTLE LAN",
                92, UIFactory.TextColor);
            title.fontStyle = FontStyle.Bold;
            UIFactory.SetAnchoredPos(title, new Vector2(0.5f, 1f), new Vector2(0, -110));

            var sub = UIFactory.CreateText(_canvas.transform, "Subtitle",
                "OFFLINE  ·  WI-FI / HOTSPOT  ·  UP TO 16 PLAYERS  ·  5 MODES", 28, UIFactory.TextDim);
            UIFactory.SetAnchoredPos(sub, new Vector2(0.5f, 1f), new Vector2(0, -180));

            _noticeText = UIFactory.CreateText(_canvas.transform, "Notice", "", 28, UIFactory.AccentRed);
            UIFactory.SetAnchoredPos(_noticeText, new Vector2(0.5f, 0f), new Vector2(0, 40));
        }

        void BuildHomePanel()
        {
            _homePanel = UIFactory.CreateCenterPanel(_canvas.transform, "HomePanel",
                Color.clear, new Vector2(640, 760));
            UIFactory.AddVerticalLayout(_homePanel, 20);

            string savedName = SettingsManager.SavedPlayerName;
            if (string.IsNullOrEmpty(savedName)) savedName = "Player" + Random.Range(100, 999);
            GameSession.PlayerName = savedName;

            _nameField = UIFactory.CreateInputField(_homePanel, "NameField", "Your name...",
                new Vector2(520, 84));
            _nameField.text = savedName;
            _nameField.onEndEdit.AddListener(v =>
            {
                if (string.IsNullOrWhiteSpace(v)) v = "Player" + Random.Range(100, 999);
                GameSession.PlayerName = v.Trim();
                SettingsManager.SavedPlayerName = GameSession.PlayerName;
            });

            UIFactory.CreateButton(_homePanel, "Solo", "PLAY SOLO  (VS BOTS)", new Vector2(520, 90),
                new Color(0.55f, 0.35f, 0.95f, 1f), () => OpenMatchSetup(solo: true));
            UIFactory.CreateButton(_homePanel, "Host", "HOST GAME", new Vector2(520, 90),
                UIFactory.Accent, () => OpenMatchSetup(solo: false));
            UIFactory.CreateButton(_homePanel, "Join", "JOIN GAME", new Vector2(520, 90),
                UIFactory.AccentGreen, () =>
                {
                    Show(_joinPanel);
                    LanDiscovery.Instance?.StartSearch();
                });
            UIFactory.CreateButton(_homePanel, "Garage", "GARAGE", new Vector2(520, 90),
                new Color(0.85f, 0.60f, 0.20f, 1f), () => Show(_garagePanel));
            UIFactory.CreateButton(_homePanel, "Settings", "SETTINGS", new Vector2(520, 90),
                UIFactory.PanelLight, () => Show(_settingsPanel));
            UIFactory.CreateButton(_homePanel, "Quit", "QUIT", new Vector2(520, 90),
                UIFactory.PanelLight, Application.Quit);
        }

        // ---- Garage: tank color + body style ----

        void BuildGaragePanel()
        {
            _garagePanel = UIFactory.CreateCenterPanel(_canvas.transform, "GaragePanel",
                UIFactory.PanelColor, new Vector2(980, 800));
            UIFactory.AddVerticalLayout(_garagePanel, 18, new RectOffset(30, 30, 24, 24));

            var title = UIFactory.CreateText(_garagePanel, "Title", "GARAGE - YOUR TANK",
                44, UIFactory.TextColor);
            ((RectTransform)title.transform).sizeDelta = new Vector2(800, 60);

            var colorLabel = UIFactory.CreateText(_garagePanel, "ColorLabel", "TANK COLOR",
                30, UIFactory.TextDim);
            ((RectTransform)colorLabel.transform).sizeDelta = new Vector2(800, 40);

            // Two rows of four color swatches.
            _colorButtons.Clear();
            for (int row = 0; row < 2; row++)
            {
                var rowGo = new GameObject($"ColorRow{row}", typeof(RectTransform));
                rowGo.transform.SetParent(_garagePanel, false);
                var rowRt = (RectTransform)rowGo.transform;
                rowRt.sizeDelta = new Vector2(840, 96);
                var h = rowGo.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 18;
                h.childAlignment = TextAnchor.MiddleCenter;
                h.childControlWidth = false; h.childControlHeight = false;
                h.childForceExpandWidth = false; h.childForceExpandHeight = false;

                for (int i = 0; i < 4; i++)
                {
                    int index = row * 4 + i;
                    var b = UIFactory.CreateButton(rowRt, $"Color{index}", "",
                        new Vector2(180, 88), GameConstants.PlayerColors[index], () =>
                        {
                            GameSession.TankColorIndex = index;
                            SettingsManager.SavedTankColor = index;
                            HighlightGarage();
                        }, 24);
                    _colorButtons.Add(b);
                }
            }

            var styleLabel = UIFactory.CreateText(_garagePanel, "StyleLabel", "BODY STYLE",
                30, UIFactory.TextDim);
            ((RectTransform)styleLabel.transform).sizeDelta = new Vector2(800, 40);

            var styleRow = new GameObject("StyleRow", typeof(RectTransform));
            styleRow.transform.SetParent(_garagePanel, false);
            var styleRt = (RectTransform)styleRow.transform;
            styleRt.sizeDelta = new Vector2(840, 96);
            var sh = styleRow.AddComponent<HorizontalLayoutGroup>();
            sh.spacing = 18;
            sh.childAlignment = TextAnchor.MiddleCenter;
            sh.childControlWidth = false; sh.childControlHeight = false;
            sh.childForceExpandWidth = false; sh.childForceExpandHeight = false;

            _styleButtons.Clear();
            for (int i = 0; i < GameConstants.TankStyleNames.Length; i++)
            {
                int index = i;
                var b = UIFactory.CreateButton(styleRt, $"Style{i}",
                    GameConstants.TankStyleNames[i], new Vector2(262, 88),
                    UIFactory.PanelLight, () =>
                    {
                        GameSession.TankStyleIndex = index;
                        SettingsManager.SavedTankStyle = index;
                        HighlightGarage();
                    }, 28);
                _styleButtons.Add(b);
            }

            _garagePreview = UIFactory.CreateText(_garagePanel, "Preview", "", 30, UIFactory.TextColor);
            ((RectTransform)_garagePreview.transform).sizeDelta = new Vector2(800, 50);

            UIFactory.CreateButton(_garagePanel, "Back", "SAVE & BACK", new Vector2(360, 80),
                UIFactory.AccentGreen, () => Show(_homePanel));

            HighlightGarage();
        }

        void HighlightGarage()
        {
            for (int i = 0; i < _colorButtons.Count; i++)
            {
                var outline = _colorButtons[i].GetComponent<Outline>() ??
                              _colorButtons[i].gameObject.AddComponent<Outline>();
                bool sel = i == GameSession.TankColorIndex;
                outline.effectColor = Color.white;
                outline.effectDistance = new Vector2(5, 5);
                outline.enabled = sel;
            }
            for (int i = 0; i < _styleButtons.Count; i++)
                _styleButtons[i].GetComponent<Image>().color =
                    i == GameSession.TankStyleIndex ? UIFactory.Accent : UIFactory.PanelLight;

            if (_garagePreview != null)
            {
                _garagePreview.text =
                    $"{GameConstants.PlayerColorNames[GameSession.TankColorIndex]}  " +
                    $"{GameConstants.TankStyleNames[GameSession.TankStyleIndex]}  TANK";
                _garagePreview.color = GameConstants.PlayerColors[GameSession.TankColorIndex];
            }
        }

        // ---- Host: map + mode + time ----

        void BuildHostPanel()
        {
            _hostPanel = UIFactory.CreateCenterPanel(_canvas.transform, "HostPanel",
                UIFactory.PanelColor, new Vector2(1720, 840));

            _hostTitle = UIFactory.CreateText(_hostPanel, "Title", "MATCH SETUP", 44, UIFactory.TextColor);
            UIFactory.SetAnchoredPos(_hostTitle, new Vector2(0.5f, 1f), new Vector2(0, -45));

            // --- Left column: map ---
            var mapCol = MakeColumn(_hostPanel, "MapCol", new Vector2(-560, -60));
            var mapLabel = UIFactory.CreateText(mapCol, "Label", "MAP", 30, UIFactory.TextDim);
            ((RectTransform)mapLabel.transform).sizeDelta = new Vector2(480, 40);

            _mapButtons.Clear();
            for (int i = 0; i < GameConstants.MapScenes.Length; i++)
            {
                int index = i;
                var b = UIFactory.CreateButton(mapCol, $"Map{i}",
                    GameConstants.MapDisplayNames[i], new Vector2(480, 78),
                    UIFactory.PanelLight, () =>
                    {
                        _selectedMap = index;
                        GameSession.SelectedMapIndex = index;
                        HighlightSelectors();
                    }, 30);
                _mapButtons.Add(b);
            }

            // --- Middle column: mode ---
            var modeCol = MakeColumn(_hostPanel, "ModeCol", new Vector2(0, -60));
            var modeLabel = UIFactory.CreateText(modeCol, "Label", "GAME MODE", 30, UIFactory.TextDim);
            ((RectTransform)modeLabel.transform).sizeDelta = new Vector2(480, 40);

            _modeButtons.Clear();
            for (int i = 0; i < GameConstants.GameModeNames.Length; i++)
            {
                int index = i;
                var b = UIFactory.CreateButton(modeCol, $"Mode{i}",
                    GameConstants.GameModeNames[i], new Vector2(480, 78),
                    UIFactory.PanelLight, () =>
                    {
                        _selectedMode = index;
                        GameSession.SelectedModeIndex = index;
                        HighlightSelectors();
                    }, 28);
                _modeButtons.Add(b);
            }

            _modeHintText = UIFactory.CreateText(modeCol, "Hint", "", 24, UIFactory.TextDim);
            ((RectTransform)_modeHintText.transform).sizeDelta = new Vector2(480, 60);

            // --- Right column: time + start ---
            var timeCol = MakeColumn(_hostPanel, "TimeCol", new Vector2(560, -60));
            var timeLabel = UIFactory.CreateText(timeCol, "Label", "MATCH TIME", 30, UIFactory.TextDim);
            ((RectTransform)timeLabel.transform).sizeDelta = new Vector2(480, 40);

            _timeButtons.Clear();
            for (int i = 0; i < GameConstants.MatchDurationLabels.Length; i++)
            {
                int index = i;
                var b = UIFactory.CreateButton(timeCol, $"Time{i}",
                    GameConstants.MatchDurationLabels[i], new Vector2(480, 70),
                    UIFactory.PanelLight, () =>
                    {
                        _selectedTime = index;
                        GameSession.SelectedTimeIndex = index;
                        HighlightSelectors();
                    }, 28);
                _timeButtons.Add(b);
            }

            var spacer = UIFactory.CreateText(timeCol, "Spacer", "", 10, UIFactory.TextDim);
            ((RectTransform)spacer.transform).sizeDelta = new Vector2(480, 14);

            var startBtn = UIFactory.CreateButton(timeCol, "StartHost", "START HOSTING",
                new Vector2(480, 92), UIFactory.Accent, () =>
                {
                    GameSession.SelectedMapIndex = _selectedMap;
                    GameSession.SelectedModeIndex = _selectedMode;
                    GameSession.SelectedTimeIndex = _selectedTime;
                    GameSession.SoloMode = _soloIntent;

                    if (!ConnectionManager.Instance.StartHost(advertise: !_soloIntent))
                    {
                        _noticeText.text = "Could not start host (port in use?)";
                        return;
                    }

                    if (_soloIntent)
                        // Solo: no lobby - straight into the battle with the bots.
                        NetworkManager.Singleton.SceneManager.LoadScene(
                            GameConstants.MapScenes[GameSession.SelectedMapIndex],
                            UnityEngine.SceneManagement.LoadSceneMode.Single);
                    else
                        Show(_lobbyPanel);
                }, 32);
            _startHostLabel = startBtn.GetComponentInChildren<Text>();
            UIFactory.CreateButton(timeCol, "Back", "BACK", new Vector2(300, 70),
                UIFactory.PanelLight, () => Show(_homePanel));
        }

        /// <summary>Open the match-setup screen for hosting or for a solo battle.</summary>
        void OpenMatchSetup(bool solo)
        {
            _soloIntent = solo;
            if (_hostTitle != null)
                _hostTitle.text = solo ? "SOLO BATTLE  ·  YOU VS 5 BOTS" : "MATCH SETUP";
            if (_startHostLabel != null)
                _startHostLabel.text = solo ? "START BATTLE" : "START HOSTING";
            Show(_hostPanel);
        }

        RectTransform MakeColumn(RectTransform parent, string name, Vector2 offset)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = offset;
            rt.sizeDelta = new Vector2(520, 700);
            UIFactory.AddVerticalLayout(rt, 12, new RectOffset(10, 10, 10, 10));
            return rt;
        }

        void BuildJoinPanel()
        {
            _joinPanel = UIFactory.CreateCenterPanel(_canvas.transform, "JoinPanel",
                UIFactory.PanelColor, new Vector2(860, 720));
            UIFactory.AddVerticalLayout(_joinPanel, 16, new RectOffset(30, 30, 24, 24));

            var title = UIFactory.CreateText(_joinPanel, "Title", "GAMES ON YOUR NETWORK",
                40, UIFactory.TextColor);
            ((RectTransform)title.transform).sizeDelta = new Vector2(700, 56);

            _joinStatusText = UIFactory.CreateText(_joinPanel, "Status",
                "Searching...  (make sure you're on the host's Wi-Fi or hotspot)",
                26, UIFactory.TextDim);
            ((RectTransform)_joinStatusText.transform).sizeDelta = new Vector2(760, 44);

            // Container the discovered-host buttons are rebuilt into.
            var listGo = new GameObject("HostList", typeof(RectTransform));
            listGo.transform.SetParent(_joinPanel, false);
            _hostListRoot = (RectTransform)listGo.transform;
            _hostListRoot.sizeDelta = new Vector2(760, 380);
            UIFactory.AddVerticalLayout(_hostListRoot, 12, new RectOffset(0, 0, 0, 0));

            UIFactory.CreateButton(_joinPanel, "Back", "BACK", new Vector2(300, 72),
                UIFactory.PanelLight, () =>
                {
                    LanDiscovery.Instance?.StopSearch();
                    Show(_homePanel);
                });
        }

        void BuildLobbyPanel()
        {
            _lobbyPanel = UIFactory.CreateCenterPanel(_canvas.transform, "LobbyPanel",
                UIFactory.PanelColor, new Vector2(860, 800));
            UIFactory.AddVerticalLayout(_lobbyPanel, 16, new RectOffset(30, 30, 24, 24));

            var title = UIFactory.CreateText(_lobbyPanel, "Title", "LOBBY", 44, UIFactory.TextColor);
            ((RectTransform)title.transform).sizeDelta = new Vector2(700, 60);

            _lobbyPlayersText = UIFactory.CreateText(_lobbyPanel, "Players", "", 26,
                UIFactory.TextColor, TextAnchor.UpperCenter);
            ((RectTransform)_lobbyPlayersText.transform).sizeDelta = new Vector2(740, 420);

            _lobbyStatusText = UIFactory.CreateText(_lobbyPanel, "Status", "", 26, UIFactory.TextDim);
            ((RectTransform)_lobbyStatusText.transform).sizeDelta = new Vector2(740, 44);

            _startMatchButton = UIFactory.CreateButton(_lobbyPanel, "StartMatch", "START MATCH",
                new Vector2(640, 92), UIFactory.AccentGreen, () =>
                {
                    // Host drives everyone into the map through NGO scene management.
                    NetworkManager.Singleton.SceneManager.LoadScene(
                        GameConstants.MapScenes[GameSession.SelectedMapIndex],
                        UnityEngine.SceneManagement.LoadSceneMode.Single);
                });

            UIFactory.CreateButton(_lobbyPanel, "Leave", "LEAVE", new Vector2(300, 72),
                UIFactory.PanelLight, () => ConnectionManager.Instance.Leave());
        }

        // ---------------------------------------------------------------- refresh

        void RefreshHostList()
        {
            foreach (Transform child in _hostListRoot) Destroy(child.gameObject);

            var hosts = LanDiscovery.Instance != null ? LanDiscovery.Instance.Hosts : null;
            if (hosts == null || hosts.Count == 0)
            {
                _joinStatusText.text = "Searching...  (make sure you're on the host's Wi-Fi or hotspot)";
                return;
            }

            _joinStatusText.text = "Tap a game to join:";
            foreach (var h in hosts)
            {
                var host = h;
                string label = $"{host.HostName}   ·   {host.MapName}   ·   {host.PlayerCount}/{host.MaxPlayers}";
                UIFactory.CreateButton(_hostListRoot, "Host_" + host.Address, label,
                    new Vector2(740, 80), UIFactory.PanelLight, () =>
                    {
                        LanDiscovery.Instance?.StopSearch();
                        if (ConnectionManager.Instance.StartClient(host.Address, host.GamePort))
                        {
                            _lobbyStatusText.text = "Connecting...";
                            Show(_lobbyPanel);
                        }
                    }, 28);
            }
        }

        void RefreshLobby()
        {
            var nm = NetworkManager.Singleton;
            bool isHost = nm != null && nm.IsHost;

            var sb = new System.Text.StringBuilder();
            int count = 0;
            if (LobbyState.Instance != null && LobbyState.Instance.PlayerNames != null &&
                LobbyState.Instance.IsSpawned)
            {
                foreach (var n in LobbyState.Instance.PlayerNames)
                {
                    sb.AppendLine($"{++count}.  {n}");
                }
            }
            _lobbyPlayersText.text = count > 0 ? sb.ToString() : "Connecting...";

            _startMatchButton.gameObject.SetActive(isHost);
            _lobbyStatusText.text = isHost
                ? $"{GameConstants.MapDisplayNames[GameSession.SelectedMapIndex]}  ·  " +
                  $"{GameConstants.GameModeNames[GameSession.SelectedModeIndex]}  ·  " +
                  $"{GameConstants.MatchDurationLabels[GameSession.SelectedTimeIndex]}  ·  " +
                  $"{count}/{GameConstants.MaxPlayers} players"
                : "Waiting for the host to start the match...";
        }

        // ------------------------------------------------------------------ misc

        void HighlightSelectors()
        {
            for (int i = 0; i < _mapButtons.Count; i++)
                _mapButtons[i].GetComponent<Image>().color =
                    i == _selectedMap ? UIFactory.Accent : UIFactory.PanelLight;
            for (int i = 0; i < _modeButtons.Count; i++)
                _modeButtons[i].GetComponent<Image>().color =
                    i == _selectedMode ? UIFactory.Accent : UIFactory.PanelLight;
            for (int i = 0; i < _timeButtons.Count; i++)
                _timeButtons[i].GetComponent<Image>().color =
                    i == _selectedTime ? UIFactory.Accent : UIFactory.PanelLight;
            if (_modeHintText != null)
                _modeHintText.text = GameConstants.GameModeHints[_selectedMode];
        }

        void Show(RectTransform panel)
        {
            _homePanel.gameObject.SetActive(panel == _homePanel);
            _garagePanel.gameObject.SetActive(panel == _garagePanel);
            _hostPanel.gameObject.SetActive(panel == _hostPanel);
            _joinPanel.gameObject.SetActive(panel == _joinPanel);
            _lobbyPanel.gameObject.SetActive(panel == _lobbyPanel);
            _settingsPanel.gameObject.SetActive(panel == _settingsPanel);
        }
    }
}
