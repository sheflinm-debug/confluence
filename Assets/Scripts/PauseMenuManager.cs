using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// In-game Escape-key pause overlay.
/// Attach to the same root GameObject as SimulationBootstrap (added automatically by it).
/// Shows Save / Settings / Quit with a semi-transparent dimmer behind.
public class PauseMenuManager : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    private enum SubPanel { None, Settings }
    private SubPanel _sub = SubPanel.None;

    private bool _escWasPressed;

    // Settings sub-panel state
    private float _masterVolume = 1f; // placeholder — wire to AudioListener.volume

    private const float PanelW = 320f;
    private const float BtnH   = 40f;
    private const float Pad    = 12f;

    private void Update()
    {
        if (Keyboard.current == null) return;
        bool escDown = Keyboard.current.escapeKey.isPressed;
        if (escDown && !_escWasPressed)
        {
            if (IsOpen) ClosePause();
            else        OpenPause();
        }
        _escWasPressed = escDown;
    }

    private void OpenPause()
    {
        IsOpen = true;
        _sub = SubPanel.None;
        Time.timeScale = 0f; // freeze simulation
    }

    private void ClosePause()
    {
        IsOpen = false;
        _sub = SubPanel.None;
        Time.timeScale = 1f;
    }

    private void OnGUI()
    {
        if (!IsOpen) return;

        float sw = Screen.width;
        float sh = Screen.height;

        // Dim the world behind the menu
        Color prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0, 0, sw, sh), Texture2D.whiteTexture);
        GUI.color = prev;

        if (_sub == SubPanel.Settings)
        {
            DrawSettingsPanel(sw, sh);
            return;
        }

        DrawMainPanel(sw, sh);
    }

    // ── Main pause panel ──────────────────────────────────────────────────────

    private void DrawMainPanel(float sw, float sh)
    {
        float panH = BtnH * 4 + Pad * 5;
        float px   = (sw - PanelW) * 0.5f;
        float py   = (sh - panH)   * 0.5f;

        DrawRect(px - 2f, py - 2f, PanelW + 4f, panH + 4f, new Color(0.3f, 0.55f, 0.8f, 0.4f));
        DrawRect(px, py, PanelW, panH, new Color(0.04f, 0.06f, 0.1f, 0.97f));

        float by = py + Pad;

        // Paused title
        GUI.Label(new Rect(px, by, PanelW, BtnH),
            LocalizationManager.CurrentLanguage == LocalizationManager.Language.Chinese ? "已暂停" : "PAUSED",
            TitleStyle());
        by += BtnH + Pad * 0.5f;

        if (PauseBtn(px, by, PanelW, BtnH, L("pause_resume"))) ClosePause();
        by += BtnH + Pad;

        if (PauseBtn(px, by, PanelW, BtnH, L("pause_settings")))
            _sub = SubPanel.Settings;
        by += BtnH + Pad;

        if (PauseBtn(px, by, PanelW, BtnH, L("pause_quit")))
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    // ── Settings sub-panel ───────────────────────────────────────────────────

    private void DrawSettingsPanel(float sw, float sh)
    {
        float panH = BtnH * 2 + 60f + Pad * 6;
        float px   = (sw - PanelW) * 0.5f;
        float py   = (sh - panH)   * 0.5f;

        DrawRect(px - 2f, py - 2f, PanelW + 4f, panH + 4f, new Color(0.3f, 0.55f, 0.8f, 0.4f));
        DrawRect(px, py, PanelW, panH, new Color(0.04f, 0.06f, 0.1f, 0.97f));

        float by = py + Pad;

        GUI.Label(new Rect(px, by, PanelW, BtnH),
            L("pause_settings"), TitleStyle());
        by += BtnH + Pad;

        // Language toggle
        SectionLabel(px + Pad, by, PanelW - Pad * 2, L("sec_language"));
        by += 22f;
        bool isChinese = LocalizationManager.CurrentLanguage == LocalizationManager.Language.Chinese;
        string langLbl = isChinese ? "English / 英文" : "中文 / Chinese";
        if (PauseBtn(px + Pad, by, PanelW - Pad * 2, 30f, langLbl, small: true))
        {
            LocalizationManager.CurrentLanguage = isChinese
                ? LocalizationManager.Language.English
                : LocalizationManager.Language.Chinese;
        }
        by += 36f + Pad;

        // Volume (placeholder — wire AudioListener when audio is added)
        SectionLabel(px + Pad, by, PanelW - Pad * 2,
            LocalizationManager.CurrentLanguage == LocalizationManager.Language.Chinese
                ? "主音量" : "Master Volume");
        by += 22f;
        float newVol = GUI.HorizontalSlider(
            new Rect(px + Pad, by + 4f, PanelW - Pad * 2 - 50f, 14f), _masterVolume, 0f, 1f);
        if (!Mathf.Approximately(newVol, _masterVolume))
        {
            _masterVolume = newVol;
            AudioListener.volume = _masterVolume;
        }
        GUI.Label(new Rect(px + PanelW - Pad - 44f, by, 44f, 22f),
            $"{_masterVolume * 100f:F0}%", SmallStyle());
        by += 32f + Pad;

        if (PauseBtn(px + Pad, by, PanelW - Pad * 2, 30f,
                LocalizationManager.L("back"), small: true))
            _sub = SubPanel.None;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string L(string key) => LocalizationManager.L(key);

    private static bool PauseBtn(float x, float y, float w, float h,
        string label, bool small = false)
    {
        var style = small ? SmallBtnStyle() : BtnStyle();
        return GUI.Button(new Rect(x, y, w, h), label, style);
    }

    private static void SectionLabel(float x, float y, float w, string text)
    {
        Color p = GUI.color;
        GUI.color = new Color(0.55f, 0.75f, 1f);
        GUI.Label(new Rect(x, y, w, 20f), text.ToUpper(), SmallStyle());
        GUI.color = p;
    }

    private static void DrawRect(float x, float y, float w, float h, Color c)
    {
        Color p = GUI.color; GUI.color = c;
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = p;
    }

    // ── Styles ────────────────────────────────────────────────────────────────

    private static GUIStyle _titleStyle, _btnStyle, _smallBtnStyle, _smallStyle;

    private static GUIStyle TitleStyle()
    {
        if (_titleStyle != null) return _titleStyle;
        _titleStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _titleStyle.normal.textColor = new Color(0.85f, 0.95f, 1f);
        return _titleStyle;
    }

    private static GUIStyle BtnStyle()
    {
        if (_btnStyle != null) return _btnStyle;
        _btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 14 };
        _btnStyle.normal.textColor = new Color(0.9f, 0.95f, 1f);
        return _btnStyle;
    }

    private static GUIStyle SmallBtnStyle()
    {
        if (_smallBtnStyle != null) return _smallBtnStyle;
        _smallBtnStyle = new GUIStyle(GUI.skin.button) { fontSize = 12 };
        _smallBtnStyle.normal.textColor = new Color(0.75f, 0.82f, 0.9f);
        return _smallBtnStyle;
    }

    private static GUIStyle SmallStyle()
    {
        if (_smallStyle != null) return _smallStyle;
        _smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        _smallStyle.normal.textColor = new Color(0.75f, 0.85f, 0.9f);
        return _smallStyle;
    }
}
