using UnityEngine;

/// Era 3's in-world calendar: a "Thus begins the new calendar of X" fade-in/fade-out banner the
/// moment Era 3 starts (same beat as a sub-era popup like a Great Oxidation Event label), followed by
/// a persistent Year/Month/Day counter at the top of the screen.
///
/// Rate, per time-scale-spec §2/§5.1: Era 3 is fixed at 5 world-years per tick (not player-
/// configurable — the spec explicitly rejects tuning this, since every other Era 3 time-denominated
/// constant is calibrated against it), with a real-seconds-per-tick GAME SPEED that IS configurable
/// (Settings page) — the spec's five presets, unchanged in meaning across a speed change. The old
/// "1 second = 1 in-world day" default was roughly 1000x too slow against this codebase's Era 3
/// event-graph pacing (reaching modest, early Era-3 milestones would have taken real DAYS) — this
/// replaces it with the spec's ~60-real-minutes-to-12,000-world-years default.
public class Era3Calendar : MonoBehaviour
{
    public static Era3Calendar Instance { get; private set; }

    public enum GameSpeed { Blitz, Brisk, Standard, Relaxed, Epic }

    // Static (not per-instance) so GameHUD's Settings page can change it without holding a scene
    // reference, same pattern as other global toggles in this project.
    public static GameSpeed Speed = GameSpeed.Standard;

    public static string SpeedLabel(GameSpeed s) => s switch
    {
        GameSpeed.Blitz    => "Blitz  (~30 min to modernity)",
        GameSpeed.Brisk    => "Brisk  (~45 min)",
        GameSpeed.Standard => "Standard  (~60 min)",
        GameSpeed.Relaxed  => "Relaxed  (~90 min)",
        GameSpeed.Epic     => "Epic  (~2.5 hr)",
        _ => "?",
    };

    // §5.1 seconds_per_tick multiplier — higher = slower real-time pacing, world-time meaning
    // (years_per_tick) never changes with this, only how fast real time advances it.
    private static float SpeedMultiplier(GameSpeed s) => s switch
    {
        GameSpeed.Blitz    => 0.5f,
        GameSpeed.Brisk    => 0.75f,
        GameSpeed.Standard => 1.0f,
        GameSpeed.Relaxed  => 1.5f,
        GameSpeed.Epic     => 2.5f,
        _ => 1.0f,
    };

    private const float YearsPerTick        = 5f;   // §2 — flat, locked, not configurable
    private const float BaseSecondsPerTick  = 1.5f; // §2 Standard-speed baseline

    private static float YearsPerRealSecond(GameSpeed s)
        => YearsPerTick / (BaseSecondsPerTick * SpeedMultiplier(s));

    private const int DaysPerMonth   = 30; // a simple fixed calendar, not a real astronomical one
    private const int MonthsPerYear  = 12;
    private const float DaysPerYear  = DaysPerMonth * MonthsPerYear; // 360 — matches the fixed calendar above
    private const float BannerDuration = 5f; // longer/more dramatic than the small event-flash banner — a chapter-opening beat, not a routine notice

    private double _daysElapsed;
    private bool _started;
    private string _bannerText = "";
    private float _bannerTimer;

    void Awake() { if (Instance == null) Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        var mgr = Era3Manager.Instance;
        if (mgr == null || !mgr.IsActive) return;

        if (!_started)
        {
            _started = true;
            string name = mgr.PlayerCiv != null ? mgr.PlayerCiv.Name : "the Civilization";
            _bannerText = $"Thus begins the new calendar of {name}";
            _bannerTimer = BannerDuration;
        }

        if (_bannerTimer > 0f) _bannerTimer -= Time.deltaTime;

        _daysElapsed += Time.deltaTime * YearsPerRealSecond(Speed) * DaysPerYear;
    }

    // time-scale-spec §2: Era 3's world span starts ~12,000 years before "modernity" — the calendar
    // now opens at year -12,000 instead of year 1, so the displayed year actually tracks the spec's
    // world-time span (crossing zero partway through) rather than restarting from scratch each game.
    private const int StartYear = -12000;

    /// Year/month/day since Era 3 began, offset so year 0 lands where the spec's span does.
    public (int year, int month, int day) CurrentDate()
    {
        int totalDays = (int)_daysElapsed;
        int day = totalDays % DaysPerMonth + 1;
        int totalMonths = totalDays / DaysPerMonth;
        int month = totalMonths % MonthsPerYear + 1;
        int year = StartYear + totalMonths / MonthsPerYear;
        return (year, month, day);
    }

    private GUIStyle _bannerStyle, _counterStyle;
    void OnGUI()
    {
        var mgr = Era3Manager.Instance;
        if (mgr == null || !mgr.IsActive) return;

        if (_counterStyle == null)
        {
            _counterStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            _counterStyle.normal.textColor = new Color(0.85f, 0.85f, 0.75f);
        }
        var (y, m, d) = CurrentDate();
        // Sits just below DeepTimeClock's "Era 3: The Commerce Engine" title (drawn at y=10, 24 tall).
        GUI.Label(new Rect(0f, 34f, Screen.width, 18f), $"Year {y}, Month {m}, Day {d}", _counterStyle);

        if (_bannerTimer > 0f)
        {
            if (_bannerStyle == null)
                _bannerStyle = new GUIStyle(GUI.skin.label)
                { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            float alpha = Mathf.Clamp01(_bannerTimer / BannerDuration * 1.6f); // holds, then fades over roughly the last 60%
            _bannerStyle.normal.textColor = new Color(1f, 0.95f, 0.8f, alpha);
            GUI.Label(new Rect(0f, Screen.height * 0.35f, Screen.width, 40f), _bannerText, _bannerStyle);
        }
    }
}
