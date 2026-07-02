using System.Collections.Generic;
using UnityEngine;

/// Startup screen overlay. Renders on top of the idle (not-yet-started) scene.
/// Attach to the same GameObject as SimulationBootstrap (or any persistent GO).
/// State machine drives all menu screens via IMGUI.
public class MainMenuManager : MonoBehaviour
{
    private enum Screen
    {
        Main,
        Settings,
        SinglePlayer_Creation,
        Multiplayer_Lobby,
        Multiplayer_Creation,
        Multiplayer_Join,
    }

    private Screen _screen = Screen.Main;

    // Game creation state
    private PlanetConfig _config = PlanetConfig.Default();
    private bool         _isMultiplayerCreation;

    // LAN browser state
    private float _scanTimer;
    private bool  _scanning;
    private Vector2 _lanScroll;

    // Star field (procedural, drawn once)
    private Vector2[] _stars;
    private float[]   _starBrightness;
    private const int StarCount = 320;

    // UI layout constants
    private const float BtnW  = 280f;
    private const float BtnH  = 42f;
    private const float SlimH = 28f;
    private const float Pad   = 12f;

    private SimulationBootstrap _bootstrap;
    private bool _menuDone;

    // ── Unity ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _bootstrap = GetComponent<SimulationBootstrap>();

        // Generate star field once
        _stars          = new Vector2[StarCount];
        _starBrightness = new float[StarCount];
        for (int i = 0; i < StarCount; i++)
        {
            _stars[i]          = new Vector2(Random.Range(0f, 1f), Random.Range(0f, 1f));
            _starBrightness[i] = Random.Range(0.4f, 1.0f);
        }

        // Ensure discovery/network singletons exist
        if (LANDiscovery.Instance == null)
            new GameObject("LANDiscovery").AddComponent<LANDiscovery>();
        if (NetworkGameManager.Instance == null)
            new GameObject("NetworkGameManager").AddComponent<NetworkGameManager>();
    }

    private void Update()
    {
        if (_menuDone) return;

        if (_screen == Screen.Multiplayer_Join)
        {
            _scanTimer += Time.deltaTime;
            if (_scanTimer > 2f)
            {
                _scanTimer = 0f;
                _scanning = false;
            }
        }
    }

    private void OnGUI()
    {
        if (_menuDone) return;

        float sw = UnityEngine.Screen.width;
        float sh = UnityEngine.Screen.height;

        // ── Black deep-space background ───────────────────────────────────────
        DrawRect(0, 0, sw, sh, Color.black);

        // Star field
        foreach (var tex in GetStarTextures()) ; // ensure white 1×1 exists
        for (int i = 0; i < StarCount; i++)
        {
            float b = _starBrightness[i];
            Color sc = new Color(b, b * 0.95f + 0.05f, b * 0.9f + 0.1f, 1f);
            float px = _stars[i].x * sw;
            float py = _stars[i].y * sh;
            float sz = b > 0.85f ? 2f : 1f;
            DrawRect(px - sz * 0.5f, py - sz * 0.5f, sz, sz, sc);
        }

        switch (_screen)
        {
            case Screen.Main:                DrawMainMenu(sw, sh);           break;
            case Screen.Settings:            DrawSettingsPanel(sw, sh);      break;
            case Screen.SinglePlayer_Creation:
            case Screen.Multiplayer_Creation: DrawCreationScreen(sw, sh);   break;
            case Screen.Multiplayer_Lobby:   DrawMultiplayerLobby(sw, sh);  break;
            case Screen.Multiplayer_Join:    DrawLANBrowser(sw, sh);        break;
        }
    }

    // ── Screens ───────────────────────────────────────────────────────────────

    private void DrawMainMenu(float sw, float sh)
    {
        // Title
        float titleY = sh * 0.22f;
        GUI.Label(new Rect(0, titleY, sw, 72f), LocalizationManager.L("game_title"), TitleStyle());

        // Subtitle / tagline
        GUI.color = new Color(0.6f, 0.8f, 1f, 0.7f);
        GUI.Label(new Rect(0, titleY + 76f, sw, 28f),
            LocalizationManager.CurrentLanguage == LocalizationManager.Language.English
                ? "An evolution simulation"
                : "进化模拟游戏",
            SubtitleStyle());
        GUI.color = Color.white;

        float cx = (sw - BtnW) * 0.5f;
        float by = sh * 0.44f;

        if (MenuButton(cx, by, BtnW, BtnH, LocalizationManager.L("single_player")))
        {
            _isMultiplayerCreation = false;
            _config = PlanetConfig.Default();
            _screen = Screen.SinglePlayer_Creation;
        }
        by += BtnH + Pad;

        if (MenuButton(cx, by, BtnW, BtnH, LocalizationManager.L("multiplayer")))
        {
            _screen = Screen.Multiplayer_Lobby;
        }
        by += BtnH + Pad;

        if (MenuButton(cx, by, BtnW, BtnH, LocalizationManager.L("settings")))
            _screen = Screen.Settings;
        by += BtnH + Pad * 2f;

        if (MenuButton(cx, by, BtnW, SlimH, LocalizationManager.L("quit"), small: true))
            Application.Quit();
    }

    private void DrawMultiplayerLobby(float sw, float sh)
    {
        DrawPanelTitle(sw, sh, LocalizationManager.L("multiplayer"));

        float cx = (sw - BtnW) * 0.5f;
        float by = sh * 0.4f;

        if (MenuButton(cx, by, BtnW, BtnH, LocalizationManager.L("create_game")))
        {
            _isMultiplayerCreation = true;
            _config = PlanetConfig.Default();
            _screen = Screen.Multiplayer_Creation;
        }
        by += BtnH + Pad;

        if (MenuButton(cx, by, BtnW, BtnH, LocalizationManager.L("join_game")))
        {
            _screen = Screen.Multiplayer_Join;
            _scanning = true;
            _scanTimer = 0f;
            LANDiscovery.Instance?.StartClientScan();
        }
        by += BtnH + Pad * 2f;

        if (MenuButton(cx, by, BtnW, SlimH, LocalizationManager.L("back"), small: true))
            _screen = Screen.Main;
    }

    private void DrawCreationScreen(float sw, float sh)
    {
        DrawPanelTitle(sw, sh, LocalizationManager.L("new_world"));

        float cx   = (sw - 440f) * 0.5f;
        float panW = 440f;
        float y    = sh * 0.22f;

        // ── Geology preset grid ───────────────────────────────────────────────
        DrawLabel(cx, ref y, panW, LocalizationManager.L("geology_preset"));
        y += 4f;

        var presets = new[]
        {
            GeologyPreset.Continents, GeologyPreset.Pangea, GeologyPreset.Islands,
            GeologyPreset.OceanWorld, GeologyPreset.Highlands, GeologyPreset.Random,
        };
        float presetBtnW = (panW - Pad) / 3f - Pad * 0.5f;
        float presetBtnH = 36f;
        for (int i = 0; i < presets.Length; i++)
        {
            float bx = cx + (i % 3) * (presetBtnW + Pad);
            float by = y  + (i / 3) * (presetBtnH + 6f);
            bool  sel = _config.geology == presets[i];
            if (PresetButton(bx, by, presetBtnW, presetBtnH,
                    LocalizationManager.GeologyName(presets[i]), sel))
                _config.geology = presets[i];
        }
        y += (presetBtnH + 6f) * 2 + Pad;

        // ── Sliders ───────────────────────────────────────────────────────────
        y += 8f;
        DrawSlider(cx, ref y, panW, LocalizationManager.L("sea_level"),
            ref _config.seaLevel, 0f, 1f, v => $"{v * 100f:F0}%");
        DrawSlider(cx, ref y, panW, LocalizationManager.L("tectonic_activity"),
            ref _config.tectonicActivity, 0f, 1f, v => v < 0.35f ? (LocalizationManager.CurrentLanguage == LocalizationManager.Language.Chinese ? "低" : "Low")
                                                        : v < 0.65f ? (LocalizationManager.CurrentLanguage == LocalizationManager.Language.Chinese ? "中" : "Medium")
                                                        :              (LocalizationManager.CurrentLanguage == LocalizationManager.Language.Chinese ? "高" : "High"));
        DrawSlider(cx, ref y, panW, LocalizationManager.L("volcanism"),
            ref _config.volcanism, 0f, 1f, v => $"{_config.VolcanoCount}");

        y += Pad * 2f;

        // ── Info line ─────────────────────────────────────────────────────────
        GUI.color = new Color(0.6f, 0.8f, 1f, 0.7f);
        string infoEN = $"Plates: {_config.PlateCount}   Sea level: {_config.SeaLevelPercentile * 100f:F0}%   Volcanoes: {_config.VolcanoCount}";
        string infoZH = $"板块: {_config.PlateCount}   海平面: {_config.SeaLevelPercentile * 100f:F0}%   火山: {_config.VolcanoCount}";
        GUI.Label(new Rect(cx, y, panW, 20f),
            LocalizationManager.CurrentLanguage == LocalizationManager.Language.Chinese ? infoZH : infoEN,
            SmallStyle());
        GUI.color = Color.white;
        y += 26f;

        // ── Buttons ───────────────────────────────────────────────────────────
        string startLabel = _isMultiplayerCreation
            ? LocalizationManager.L("start_as_host")
            : LocalizationManager.L("start_simulation");

        if (MenuButton(cx, y, panW, BtnH, startLabel))
            LaunchGame();

        y += BtnH + Pad;
        if (MenuButton(cx, y, panW, SlimH, LocalizationManager.L("back"), small: true))
            _screen = _isMultiplayerCreation ? Screen.Multiplayer_Lobby : Screen.Main;
    }

    // Volume persists for the session (placeholder until audio is implemented)
    private float _masterVolume = 1f;

    private void DrawSettingsPanel(float sw, float sh)
    {
        DrawPanelTitle(sw, sh, LocalizationManager.L("settings"));

        float cx   = (sw - 400f) * 0.5f;
        float panW = 400f;
        float y    = sh * 0.28f;

        // ── Language ─────────────────────────────────────────────────────────
        DrawLabel(cx, ref y, panW, LocalizationManager.L("sec_language"));

        bool isChinese = LocalizationManager.CurrentLanguage == LocalizationManager.Language.Chinese;
        if (MenuButton(cx, y, (panW - Pad) / 2f, SlimH, "English", small: true))
            LocalizationManager.CurrentLanguage = LocalizationManager.Language.English;
        GUI.color = isChinese ? new Color(0.3f, 0.65f, 1f) : Color.white;
        if (MenuButton(cx + (panW - Pad) / 2f + Pad, y, (panW - Pad) / 2f, SlimH,
                "中文", small: true))
            LocalizationManager.CurrentLanguage = LocalizationManager.Language.Chinese;
        GUI.color = Color.white;
        y += SlimH + Pad * 2f;

        // ── Volume (placeholder) ──────────────────────────────────────────────
        DrawLabel(cx, ref y, panW,
            LocalizationManager.CurrentLanguage == LocalizationManager.Language.Chinese
                ? "主音量" : "Master Volume");

        float sliderW = panW - 60f;
        float newVol = GUI.HorizontalSlider(new Rect(cx, y + 4f, sliderW, 14f),
            _masterVolume, 0f, 1f);
        if (!Mathf.Approximately(newVol, _masterVolume))
        {
            _masterVolume = newVol;
            AudioListener.volume = _masterVolume;
        }
        GUI.Label(new Rect(cx + sliderW + 6f, y, 52f, 22f),
            $"{_masterVolume * 100f:F0}%", SmallStyle());
        y += 32f + Pad * 2f;

        if (MenuButton(cx, y, panW, SlimH, LocalizationManager.L("back"), small: true))
            _screen = Screen.Main;
    }

    private void DrawLANBrowser(float sw, float sh)
    {
        DrawPanelTitle(sw, sh, LocalizationManager.L("join_game"));

        float cx   = (sw - 440f) * 0.5f;
        float panW = 440f;
        float y    = sh * 0.25f;

        // Refresh button
        if (MenuButton(cx + panW - 100f, y - 36f, 100f, 28f,
                LocalizationManager.L("refresh"), small: true))
        {
            _scanning = true;
            _scanTimer = 0f;
            LANDiscovery.Instance?.RefreshScan();
        }

        var games = LANDiscovery.Instance?.DiscoveredGames ?? new List<LANDiscovery.GameEntry>();

        if (_scanning)
        {
            GUI.color = new Color(0.7f, 0.7f, 0.7f);
            GUI.Label(new Rect(cx, y, panW, 24f), LocalizationManager.L("scanning"), SmallStyle());
            GUI.color = Color.white;
            y += 28f;
        }

        if (games.Count == 0 && !_scanning)
        {
            GUI.color = new Color(0.6f, 0.6f, 0.6f);
            GUI.Label(new Rect(cx, y, panW, 24f), LocalizationManager.L("no_games"), SmallStyle());
            GUI.color = Color.white;
            y += 28f;
        }

        // List of games
        float rowH     = 48f;
        float viewH    = Mathf.Min(games.Count * (rowH + 6f) + 10f, sh * 0.4f);
        Rect  viewRect = new Rect(cx, y, panW, viewH);
        Rect  contRect = new Rect(0, 0, panW - 16f, games.Count * (rowH + 6f) + 4f);
        _lanScroll = GUI.BeginScrollView(viewRect, _lanScroll, contRect, false, true);

        float ry = 4f;
        foreach (var game in games)
        {
            DrawRect(0, ry, panW - 16f, rowH, new Color(0.1f, 0.15f, 0.25f, 0.9f));

            string hostLbl    = $"{LocalizationManager.L("host_label")} {game.hostName} ({game.hostIP})";
            string playersLbl = $"{LocalizationManager.L("players_label")} {game.playerCount}";
            GUI.Label(new Rect(8f, ry + 4f, panW - 110f, 18f), hostLbl, SmallStyle());
            GUI.Label(new Rect(8f, ry + 24f, panW - 110f, 18f), playersLbl, SmallStyle());

            if (GUI.Button(new Rect(panW - 108f, ry + 10f, 88f, 28f),
                    LocalizationManager.L("join"), BtnStyle(11)))
            {
                NetworkGameManager.Instance?.JoinGame(game.hostIP, game.port);
                // For now: start simulation locally (stub — real impl synchronizes seed from host)
                _config.worldSeed = Random.Range(0, int.MaxValue);
                LaunchGame();
            }
            ry += rowH + 6f;
        }
        GUI.EndScrollView();

        y += viewH + Pad * 2f;
        if (MenuButton(cx, y, BtnW, SlimH, LocalizationManager.L("back"), small: true))
        {
            LANDiscovery.Instance?.Stop();
            _screen = Screen.Multiplayer_Lobby;
        }
    }

    // ── Launch ────────────────────────────────────────────────────────────────

    private void LaunchGame()
    {
        if (_isMultiplayerCreation)
        {
            _config.worldSeed = Random.Range(0, int.MaxValue);
            NetworkGameManager.Instance?.StartHost(_config);
            NetworkGameManager.Instance?.BroadcastWorldSeed(_config.worldSeed);
        }

        _menuDone = true;
        _bootstrap.BeginSimulation(_config);
    }

    // ── IMGUI helpers ─────────────────────────────────────────────────────────

    private bool MenuButton(float x, float y, float w, float h, string label, bool small = false)
    {
        var style = small ? SmallBtnStyle() : BtnStyle(14);
        return GUI.Button(new Rect(x, y, w, h), label, style);
    }

    private bool PresetButton(float x, float y, float w, float h, string label, bool selected)
    {
        Color prev = GUI.color;
        GUI.color = selected ? new Color(0.3f, 0.65f, 1f, 1f) : new Color(0.5f, 0.5f, 0.5f, 0.8f);
        bool clicked = GUI.Button(new Rect(x, y, w, h), label, BtnStyle(11));
        GUI.color = prev;
        return clicked;
    }

    private void DrawSlider(float x, ref float y, float w, string label,
        ref float value, float min, float max, System.Func<float, string> fmt)
    {
        float labelW = 150f;
        float valW   = 60f;
        float sliderW = w - labelW - valW - Pad;
        GUI.Label(new Rect(x, y, labelW, 22f), label, SmallStyle());
        value = GUI.HorizontalSlider(new Rect(x + labelW, y + 5f, sliderW, 14f), value, min, max);
        GUI.Label(new Rect(x + labelW + sliderW + 4f, y, valW, 22f), fmt(value), SmallStyle());
        y += 28f;
    }

    private void DrawLabel(float x, ref float y, float w, string label)
    {
        GUI.color = new Color(0.7f, 0.8f, 1f);
        GUI.Label(new Rect(x, y, w, 22f), label, SmallStyle());
        GUI.color = Color.white;
        y += 26f;
    }

    private void DrawPanelTitle(float sw, float sh, string text)
    {
        GUI.Label(new Rect(0, sh * 0.1f, sw, 52f), text, TitleStyle());
    }

    private static void DrawRect(float x, float y, float w, float h, Color c)
    {
        Color prev = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = prev;
    }

    // ── Styles ────────────────────────────────────────────────────────────────

    private static GUIStyle _titleStyle, _subtitleStyle, _btnStyle, _smallBtnStyle, _smallStyle;

    private static GUIStyle TitleStyle()
    {
        if (_titleStyle != null) return _titleStyle;
        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 56,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        _titleStyle.normal.textColor = new Color(0.85f, 0.95f, 1f);
        return _titleStyle;
    }

    private static GUIStyle SubtitleStyle()
    {
        if (_subtitleStyle != null) return _subtitleStyle;
        _subtitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 16,
            alignment = TextAnchor.MiddleCenter,
        };
        _subtitleStyle.normal.textColor = new Color(0.6f, 0.8f, 1f, 0.7f);
        return _subtitleStyle;
    }

    private static GUIStyle BtnStyle(int fontSize = 14)
    {
        if (_btnStyle != null) { _btnStyle.fontSize = fontSize; return _btnStyle; }
        _btnStyle = new GUIStyle(GUI.skin.button) { fontSize = fontSize };
        _btnStyle.normal.textColor  = new Color(0.9f, 0.95f, 1f);
        _btnStyle.hover.textColor   = Color.white;
        _btnStyle.active.textColor  = Color.white;
        return _btnStyle;
    }

    private static GUIStyle SmallBtnStyle()
    {
        if (_smallBtnStyle != null) return _smallBtnStyle;
        _smallBtnStyle = new GUIStyle(GUI.skin.button) { fontSize = 11 };
        _smallBtnStyle.normal.textColor = new Color(0.7f, 0.75f, 0.8f);
        return _smallBtnStyle;
    }

    private static GUIStyle SmallStyle()
    {
        if (_smallStyle != null) return _smallStyle;
        _smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        _smallStyle.normal.textColor = new Color(0.8f, 0.85f, 0.9f);
        return _smallStyle;
    }

    // Dummy to satisfy the foreach star texture call — returns nothing.
    private static IEnumerable<object> GetStarTextures() { yield break; }
}
