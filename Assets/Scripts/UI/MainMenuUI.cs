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
    ///   Home  -> player name + HOST / JOIN / SETTINGS / QUIT
    ///   Host  -> pick one of the 5 maps, start hosting
    ///   Join  -> live list of LAN hosts discovered via UDP broadcast
    ///   Lobby -> replicated player list; host presses START MATCH
    /// Also handles returning from a finished match while still connected
    /// (drops everyone straight back into the lobby).
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        Canvas _canvas;
        RectTransform _homePanel, _hostPanel, _joinPanel, _lobbyPanel, _settingsPanel;
        InputField _nameField;
        Text _noticeText, _joinStatusText, _lobbyPlayersText, _lobbyStatusText;
        RectTransform _hostListRoot;
        Button _startMatchButton;
        int _selectedMap;
        readonly List<Button> _mapButtons = new List<Button>();
        float _nextHostListRefresh;

        void Start()
        {
            UIFactory.EnsureEventSystem();
            _canvas = UIFactory.CreateCanvas("MenuCanvas");
            _canvas.transform.SetParent(transform, false);

            BuildBackground();
            BuildHomePanel();
            BuildHostPanel();
            BuildJoinPanel();
            BuildLobbyPanel();
            _settingsPanel = SettingsPanel.Build(_canvas.transform, () => Show(_homePanel));

            _selectedMap = GameSession.SelectedMapIndex;
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
                "OFFLINE  ·  WI-FI / HOTSPOT  ·  UP TO 4 PLAYERS", 28, UIFactory.TextDim);
            UIFactory.SetAnchoredPos(sub, new Vector2(0.5f, 1f), new Vector2(0, -180));

            _noticeText = UIFactory.CreateText(_canvas.transform, "Notice", "", 28, UIFactory.AccentRed);
            UIFactory.SetAnchoredPos(_noticeText, new Vector2(0.5f, 0f), new Vector2(0, 40));
        }

        void BuildHomePanel()
        {
            _homePanel = UIFactory.CreateCenterPanel(_canvas.transform, "HomePanel",
                Color.clear, new Vector2(640, 660));
            UIFactory.AddVerticalLayout(_homePanel, 22);

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

            UIFactory.CreateButton(_homePanel, "Host", "HOST GAME", new Vector2(520, 92),
                UIFactory.Accent, () => Show(_hostPanel));
            UIFactory.CreateButton(_homePanel, "Join", "JOIN GAME", new Vector2(520, 92),
                UIFactory.AccentGreen, () =>
                {
                    Show(_joinPanel);
                    LanDiscovery.Instance?.StartSearch();
                });
            UIFactory.CreateButton(_homePanel, "Settings", "SETTINGS", new Vector2(520, 92),
                UIFactory.PanelLight, () => Show(_settingsPanel));
            UIFactory.CreateButton(_homePanel, "Quit", "QUIT", new Vector2(520, 92),
                UIFactory.PanelLight, Application.Quit);
        }

        void BuildHostPanel()
        {
            _hostPanel = UIFactory.CreateCenterPanel(_canvas.transform, "HostPanel",
                UIFactory.PanelColor, new Vector2(760, 760));
            UIFactory.AddVerticalLayout(_hostPanel, 16, new RectOffset(30, 30, 24, 24));

            var title = UIFactory.CreateText(_hostPanel, "Title", "CHOOSE MAP", 44, UIFactory.TextColor);
            ((RectTransform)title.transform).sizeDelta = new Vector2(600, 60);

            _mapButtons.Clear();
            for (int i = 0; i < GameConstants.MapScenes.Length; i++)
            {
                int index = i;
                var b = UIFactory.CreateButton(_hostPanel, $"Map{i}",
                    GameConstants.MapDisplayNames[i], new Vector2(640, 84),
                    UIFactory.PanelLight, () =>
                    {
                        _selectedMap = index;
                        GameSession.SelectedMapIndex = index;
                        HighlightMapButtons();
                    }, 32);
                _mapButtons.Add(b);
            }
            HighlightMapButtons();

            UIFactory.CreateButton(_hostPanel, "StartHost", "START HOSTING", new Vector2(640, 92),
                UIFactory.Accent, () =>
                {
                    GameSession.SelectedMapIndex = _selectedMap;
                    if (ConnectionManager.Instance.StartHost()) Show(_lobbyPanel);
                    else _noticeText.text = "Could not start host (port in use?)";
                });
            UIFactory.CreateButton(_hostPanel, "Back", "BACK", new Vector2(300, 72),
                UIFactory.PanelLight, () => Show(_homePanel));
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
                UIFactory.PanelColor, new Vector2(760, 700));
            UIFactory.AddVerticalLayout(_lobbyPanel, 18, new RectOffset(30, 30, 24, 24));

            var title = UIFactory.CreateText(_lobbyPanel, "Title", "LOBBY", 44, UIFactory.TextColor);
            ((RectTransform)title.transform).sizeDelta = new Vector2(600, 60);

            _lobbyPlayersText = UIFactory.CreateText(_lobbyPanel, "Players", "", 32,
                UIFactory.TextColor, TextAnchor.UpperCenter);
            ((RectTransform)_lobbyPlayersText.transform).sizeDelta = new Vector2(640, 280);

            _lobbyStatusText = UIFactory.CreateText(_lobbyPanel, "Status", "", 26, UIFactory.TextDim);
            ((RectTransform)_lobbyStatusText.transform).sizeDelta = new Vector2(640, 44);

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
                ? $"Map: {GameConstants.MapDisplayNames[GameSession.SelectedMapIndex]}  ·  {count}/{GameConstants.MaxPlayers} players"
                : "Waiting for the host to start the match...";
        }

        // ------------------------------------------------------------------ misc

        void HighlightMapButtons()
        {
            for (int i = 0; i < _mapButtons.Count; i++)
                _mapButtons[i].GetComponent<Image>().color =
                    i == _selectedMap ? UIFactory.Accent : UIFactory.PanelLight;
        }

        void Show(RectTransform panel)
        {
            _homePanel.gameObject.SetActive(panel == _homePanel);
            _hostPanel.gameObject.SetActive(panel == _hostPanel);
            _joinPanel.gameObject.SetActive(panel == _joinPanel);
            _lobbyPanel.gameObject.SetActive(panel == _lobbyPanel);
            _settingsPanel.gameObject.SetActive(panel == _settingsPanel);
        }
    }
}
