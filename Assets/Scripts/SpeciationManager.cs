using System.Collections.Generic;
using UnityEngine;

/// Formula-driven speciation system implementing the SI model from speciation_spec.md §2.
///
/// SI(lineage, t) = BaseRate(era) × Fragmentation × DiversitySaturation × ClimateVolatility
///                 × EcoPressure × PopulationFactor
///
/// SI is in expected splits per lineage per kiloyear. It converts to a per-real-second
/// probability by multiplying by the current era's time-compression ratio (kyr of sim-time
/// that elapse per real second). When SpeciationManager is present, AgentController's own
/// atmospheric-stress roll is suppressed.
public class SpeciationManager : MonoBehaviour
{
    public static SpeciationManager Instance { get; private set; }

    [Header("Niche capacity")]
    [Tooltip("Maximum species count before DiversitySaturation approaches its floor (0.1). Tune per biome.")]
    public float nicheCapacity = 20f;

    [Header("Population factor")]
    [Tooltip("Population at which PopulationFactor reaches its ceiling (1.3). Agents above this count as cap.")]
    public float populationCap = 200f;

    [Header("Eco pressure")]
    [Tooltip("Set true when the first predation event fires to apply the EcoPressure multiplier (1.35).")]
    public bool firstPredationActive = false;

    private AgentSpawner _agentSpawner;
    private float _climateVolatility = 1.0f;

    private struct IsolationEvent
    {
        public float Multiplier;
        public float EndTime;
    }
    private readonly List<IsolationEvent> _isolationEvents = new List<IsolationEvent>();

    // Diagnostic state (read by OnGUI).
    private float _maxSIThisFrame;
    private string _dominantLineage = "—";
    private string _eraLabel = "—";
    private int _speciesCount = 1;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public void Init(AgentSpawner spawner)
    {
        Instance = this;
        _agentSpawner = spawner;
    }

    /// Call from geographic-isolation events to temporarily boost Fragmentation.
    public void AddIsolationEvent(float multiplier, float durationSeconds)
    {
        _isolationEvents.Add(new IsolationEvent
        {
            Multiplier = multiplier,
            EndTime = Time.time + durationSeconds
        });
    }

    /// Exposed for climate/extinction events to override the ClimateVolatility factor.
    public void SetClimateVolatility(float v) => _climateVolatility = v;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Update()
    {
        if (_agentSpawner == null) return;

        // Prune expired isolation events.
        for (int i = _isolationEvents.Count - 1; i >= 0; i--)
            if (Time.time >= _isolationEvents[i].EndTime) _isolationEvents.RemoveAt(i);

        // Group living agents by AtmoLineage.
        var lineages = new Dictionary<string, List<AgentController>>();
        foreach (var agent in _agentSpawner.ActiveAgents)
        {
            if (agent == null) continue;
            if (!lineages.TryGetValue(agent.AtmoLineage, out var list))
            {
                list = new List<AgentController>();
                lineages[agent.AtmoLineage] = list;
            }
            list.Add(agent);
        }

        _speciesCount = lineages.Count;
        _eraLabel = GetEraLabel();
        _maxSIThisFrame = 0f;
        _dominantLineage = "—";

        float compressionKyrPerSec = GetCompressionKyrPerSecond();
        float baseRate = GetBaseRate();
        float fragmentation = GetFragmentation();
        float diversitySaturation = Mathf.Max(0.1f, 1f - (float)_speciesCount / nicheCapacity);
        float ecoPressure = firstPredationActive ? 1.35f : 1.0f;
        int totalPop = _agentSpawner.ActiveAgents.Count;

        float dtKyr = compressionKyrPerSec * Time.deltaTime;

        foreach (var kvp in lineages)
        {
            List<AgentController> members = kvp.Value;
            int lineagePop = members.Count;

            float populationFactor = Mathf.Clamp(
                0.7f + 0.3f * Mathf.Log10(Mathf.Max(1, lineagePop)) / Mathf.Log10(Mathf.Max(2f, populationCap)),
                0.7f, 1.3f);

            float si = baseRate * fragmentation * diversitySaturation * _climateVolatility * ecoPressure * populationFactor;

            if (si > _maxSIThisFrame)
            {
                _maxSIThisFrame = si;
                _dominantLineage = kvp.Key;
            }

            // Roll: random() < SI × tick_length_in_kyr
            if (Random.value < si * dtKyr)
                FireSpeciation(members);
        }
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    private void FireSpeciation(List<AgentController> lineage)
    {
        AgentController agent = lineage[Random.Range(0, lineage.Count)];
        if (agent == null) return;

        string oldLineage = agent.AtmoLineage;
        string newLineageName = KingdomNameGenerator.Generate();
        Color newColor = Color.HSVToRGB(Random.value, Random.Range(0.65f, 0.95f), Random.Range(0.85f, 1f));
        agent.TriggerSpeciation(newLineageName, newColor);

        Debug.Log($"[SpeciationManager] '{oldLineage}' → '{newLineageName}' | SI={_maxSIThisFrame:G4} era={_eraLabel} species={_speciesCount}");
    }

    /// Fragmentation: maximum active isolation multiplier, or 1.0 background.
    private float GetFragmentation()
    {
        float best = 1.0f;
        foreach (var evt in _isolationEvents)
            if (evt.Multiplier > best) best = evt.Multiplier;
        return best;
    }

    /// Kiloyears of sim-time per real second for the currently-active EraTimeline phase.
    /// Falls back to "Prokaryotic seas" compression when the clock is past its last phase
    /// or on a pre-Era-1 phase.
    private float GetCompressionKyrPerSecond()
    {
        // Fallback: "Prokaryotic seas" is Era1StartIndex + 1 (index 9).
        EraPhase fallbackPhase = EraTimeline.Phases[EraTimeline.Era1StartIndex + 1];
        float fallback = CompressionOf(fallbackPhase);

        if (DeepTimeClock.Instance == null) return fallback;
        int idx = DeepTimeClock.Instance.CurrentPhaseIndex;
        if (idx < EraTimeline.Era1StartIndex || idx >= EraTimeline.Phases.Length)
            return fallback;

        return CompressionOf(EraTimeline.Phases[idx]);
    }

    private static float CompressionOf(EraPhase phase)
    {
        if (phase.DurationSeconds <= 0f) return 1f;
        return (phase.YearsAgoStart - phase.YearsAgoEnd) / 1000f / phase.DurationSeconds;
    }

    /// BaseRate(era) per speciation_spec.md §2.1.
    private float GetBaseRate()
    {
        if (DeepTimeClock.Instance == null) return 0.00001f;
        int idx = DeepTimeClock.Instance.CurrentPhaseIndex;
        if (idx >= EraTimeline.Phases.Length) return 0.00001f;
        string label = EraTimeline.Phases[idx].PhaseLabel;
        if (label.Contains("Cambrian") || label.Contains("Multicellularity")) return 0.0005f;
        if (label.Contains("Eukaryote")) return 0.00005f;
        return 0.00001f;
    }

    private string GetEraLabel()
    {
        if (DeepTimeClock.Instance == null) return "Era 1a";
        int idx = DeepTimeClock.Instance.CurrentPhaseIndex;
        if (idx >= EraTimeline.Phases.Length) return "Era 1a (post-montage)";
        return EraTimeline.Phases[idx].PhaseLabel;
    }

    // -------------------------------------------------------------------------
    // Diagnostic overlay
    // -------------------------------------------------------------------------

    void OnGUI()
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleCenter
        };
        style.normal.textColor = new Color(0.7f, 1f, 0.75f);

        string extras = "";
        if (_isolationEvents.Count > 0)
            extras += $"  |  Isolation ×{GetFragmentation():F1}";
        if (!Mathf.Approximately(_climateVolatility, 1f))
            extras += $"  |  ClimateVol ×{_climateVolatility:F1}";
        if (firstPredationActive)
            extras += "  |  Predation active";

        string line = $"Speciation — {_eraLabel}  |  SI={_maxSIThisFrame:G4}  |  Species {_speciesCount}/{(int)nicheCapacity}{extras}";
        GUI.Label(new Rect(0, 38, Screen.width, 22), line, style);
    }
}
