using UnityEngine;

/// Persistent "deep time" caption (game-eras-spec.md's Implementation Notes: a ticking
/// years-ago counter communicates elapsed time during stasis-heavy stretches even when
/// screen time is short). Auto-advances through EraTimeline phases on its own Update
/// loop using each phase's DurationSeconds, independent of whatever GenesisCinematic is
/// visually doing - since both read the same EraTimeline table they stay in lockstep
/// without needing to be wired together. Keeps running after the cinematic hands off to
/// the live colony simulation, ticking through Era 1's sub-phases as a caption overlay.
public class DeepTimeClock : MonoBehaviour
{
    public static DeepTimeClock Instance { get; private set; }

    // Current phase index into EraTimeline.Phases; clamped to array length when finished.
    public int CurrentPhaseIndex => _phaseIndex;
    public bool IsFinished => _finished;

    private int _phaseIndex;
    private float _phaseT;
    private bool _running;
    private bool _finished;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    public void StartFrom(int phaseIndex)
    {
        _phaseIndex = phaseIndex;
        _phaseT = 0f;
        _running = true;
        _finished = false;
    }

    void Update()
    {
        if (!_running) return;

        _phaseT += Time.deltaTime;
        EraPhase phase = EraTimeline.Phases[_phaseIndex];
        if (_phaseT >= phase.DurationSeconds)
        {
            _phaseIndex++;
            _phaseT = 0f;
            if (_phaseIndex >= EraTimeline.Phases.Length)
            {
                _running = false;
                _finished = true;
            }
        }
    }

    private string CurrentCaption()
    {
        EraPhase phase = EraTimeline.Phases[_phaseIndex];
        float t = phase.DurationSeconds > 0f ? Mathf.Clamp01(_phaseT / phase.DurationSeconds) : 1f;
        float yearsAgo = Mathf.Lerp(phase.YearsAgoStart, phase.YearsAgoEnd, t);
        return $"{phase.EraLabel} — {phase.PhaseLabel} — {FormatYearsAgo(yearsAgo)}";
    }

    private static string FormatYearsAgo(float years)
    {
        if (years >= 1_000_000_000f) return $"~{years / 1_000_000_000f:F2} billion years ago";
        if (years >= 1_000_000f) return $"~{years / 1_000_000f:F0} million years ago";
        if (years >= 1_000f) return $"~{years / 1_000f:F0} thousand years ago";
        return $"~{years:F0} years ago";
    }

    void OnGUI()
    {
        string caption;
        if (_running) caption = CurrentCaption();
        else if (_finished) caption = "Era 2: Age of Intelligence — simulation continues";
        else return;

        GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
        style.normal.textColor = Color.white;
        GUI.Label(new Rect(0, 10, Screen.width, 24), caption, style);
    }
}
