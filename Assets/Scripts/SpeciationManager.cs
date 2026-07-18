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

    // Real-time seconds between auto-speciation events for the same lineage.
    [Header("Rate limiter")]
    [Tooltip("Minimum real-time seconds between two speciation events for the same lineage. " +
             "120s = one branch per two minutes per lineage at most, regardless of time compression.")]
    public float minSecondsBetweenSplits = 120f;

    [Tooltip("Minimum living members a lineage must have before it can branch. " +
             "Prevents 1-member communities from speciation-chaining before they can reproduce.")]
    public int minMembersToSpeciate = 2;

    // Cap on the effective kyr-per-frame used in the SI roll so extreme geological
    // time-compression (Minimal-Replicator Seas ≈ 8000 kyr/sec) doesn't make speciation
    // a near-certainty every cooldown expiry. 0.5 kyr per frame ≈ 30 fps effective rate.
    [Tooltip("Maximum simulated kiloyears per frame counted toward the speciation roll. " +
             "Prevents time-compression from overwhelming the SI probability. " +
             "50 kyr/frame gives ~1 event per lineage per minute at Minimal-Replicator baseRate.")]
    public float maxDtKyrPerRoll = 50f;

    // Tracks the last real time (Time.time) each lineage split, for cooldown enforcement.
    private readonly Dictionary<string, float> _lastSpeciationTime = new Dictionary<string, float>();

    // Diagnostic-log interval (real seconds). Periodic per-lineage SI dump so the gap between
    // accumulated SI and the effective firing probability is observable in logs, not just the
    // rare moments FireSpeciation actually succeeds (spec: population-speciation-gene-ordering fix §2.1).
    [Header("Diagnostics")]
    public float siLogIntervalSeconds = 15f;
    private float _siLogTimer;

    // Monotonically-increasing community ID counter. Community 0 = player's founding
    // lineage. Each speciation event branches a new community from an existing one.
    private int _nextCommunityId = 1;

    // Diagnostic state (read by OnGUI / HUD).
    private float _maxSIThisFrame;
    private string _dominantLineage = "—";
    private string _eraLabel = "—";
    public float MaxSI => _maxSIThisFrame;
    public string EraLabel => _eraLabel;
    /// Living species this frame (distinct AtmoLineage names among active agents).
    public int SpeciesCount => _speciesCount;
    /// Total species ever observed (monotonically increasing).
    public int HistoricalSpeciesCount => _historicalSpeciesCount;
    /// Species that existed at some point but have no living members now.
    public int ExtinctSpeciesCount => _historicalSpeciesCount - _speciesCount;
    private int _speciesCount = 1;
    private int _historicalSpeciesCount = 1;
    private readonly HashSet<string> _seenLineages = new HashSet<string>();

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

    /// Called by the player-facing gene/choice UI when the player decides to diverge their
    /// lineage. Finds all agents with communityId==0 and fires a speciation on one of them.
    public void TriggerPlayerSpeciation()
    {
        if (_agentSpawner == null) return;
        var playerAgents = new List<AgentController>();
        foreach (var a in _agentSpawner.ActiveAgents)
            if (a != null && a.communityId == 0) playerAgents.Add(a);
        if (playerAgents.Count > 0)
            FireSpeciation(playerAgents);
    }

    /// Returns true if the given AtmoLineage name belongs to any player-community agent.
    public bool IsPlayerLineage(string lineageName)
    {
        if (_agentSpawner == null) return false;
        foreach (var a in _agentSpawner.ActiveAgents)
            if (a != null && a.communityId == 0 && a.AtmoLineage == lineageName) return true;
        return false;
    }

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
        foreach (var name in lineages.Keys)
            if (_seenLineages.Add(name)) _historicalSpeciesCount++;
        _eraLabel = GetEraLabel();
        _maxSIThisFrame = 0f;
        _dominantLineage = "—";

        // Prune cooldown entries for extinct lineages so the dict doesn't grow forever.
        if (_lastSpeciationTime.Count > lineages.Count * 2)
        {
            var toRemove = new List<string>();
            foreach (var k in _lastSpeciationTime.Keys)
                if (!lineages.ContainsKey(k)) toRemove.Add(k);
            foreach (var k in toRemove) _lastSpeciationTime.Remove(k);
        }

        float compressionKyrPerSec = GetCompressionKyrPerSecond();
        float baseRate = GetBaseRate();
        float fragmentation = GetFragmentation();
        float diversitySaturation = Mathf.Max(0.1f, 1f - (float)_speciesCount / nicheCapacity);
        float ecoPressure = firstPredationActive ? 1.35f : 1.0f;
        int totalPop = _agentSpawner.ActiveAgents.Count;

        float dtKyr = compressionKyrPerSec * Time.deltaTime;

        _siLogTimer += Time.deltaTime;
        bool emitSiLog = _siLogTimer >= siLogIntervalSeconds;
        if (emitSiLog) _siLogTimer = 0f;

        foreach (var kvp in lineages)
        {
            List<AgentController> members = kvp.Value;
            int lineagePop = members.Count;

            float populationFactor = Mathf.Clamp(
                0.7f + 0.3f * Mathf.Log10(Mathf.Max(1, lineagePop)) / Mathf.Log10(Mathf.Max(2f, populationCap)),
                0.7f, 1.3f);

            // Fragmentation now varies per lineage with real spatial dispersion (allopatric driver),
            // taking the stronger of that and any active isolation-event boost. This lets stable,
            // geographically-spread lineages accumulate SI without a climate crisis.
            float spatialFrag = ComputeSpatialFragmentation(members);
            float lineageFrag = Mathf.Max(fragmentation, spatialFrag);

            float si = baseRate * lineageFrag * diversitySaturation * _climateVolatility * ecoPressure * populationFactor;

            if (si > _maxSIThisFrame)
            {
                _maxSIThisFrame = si;
                _dominantLineage = kvp.Key;
            }

            // Per-lineage cooldown: even at high kyr-compression, don't fire more than
            // once per minSecondsBetweenSplits real seconds for the same lineage.
            _lastSpeciationTime.TryGetValue(kvp.Key, out float lastFired);
            bool cooledDown = (Time.time - lastFired) >= minSecondsBetweenSplits;

            // Roll: random() < SI × tick_length_in_kyr, capped so large time-compression
            // ratios don't make speciation a near-certainty every cooldown expiry.
            float rollDt = Mathf.Min(dtKyr, maxDtKyrPerRoll);

            // Periodic diagnostic (spec §2.1): shows accumulated-SI-vs-effective-probability gap
            // for every lineage every siLogIntervalSeconds, not just the rare successful rolls —
            // makes it possible to tell whether SI is genuinely low or the roll window is too rare.
            if (emitSiLog)
            {
                float pFire = si * rollDt;
                float cooldownRemaining = Mathf.Max(0f, minSecondsBetweenSplits - (Time.time - lastFired));

                // Priority 5: limitingFactor = whichever input contributes least relative to its own
                // potential range, i.e. the one holding SI down. Turns "why isn't this community
                // speciating" into a direct read instead of a formula-reconstruction exercise.
                float fragN = lineageFrag / 3f, divN = diversitySaturation, popN = populationFactor / 1.3f,
                      ecoN = ecoPressure / 1.35f, climN = _climateVolatility / 2.5f;
                string limiting = "frag"; float lo = fragN;
                if (divN  < lo) { lo = divN;  limiting = "divSat";    }
                if (popN  < lo) { lo = popN;  limiting = "popFactor"; }
                if (ecoN  < lo) { lo = ecoN;  limiting = "eco";       }
                if (climN < lo) { lo = climN; limiting = "climate";   }

                Debug.Log($"[SpeciationManager] community={kvp.Key} pop={lineagePop} SI={si:G4} " +
                          $"pFireThisRoll={pFire:G4} rollDtKyr={rollDt:F3} cooldownRemaining={cooldownRemaining:F0}s " +
                          $"frag={lineageFrag:F2}(spatial={spatialFrag:F2}) divSat={diversitySaturation:F2} popFactor={populationFactor:F2} " +
                          $"eco={ecoPressure:F2} climate={_climateVolatility:F2} limitingFactor={limiting}");
            }

            // Require minimum viable population before branching.
            if (lineagePop < minMembersToSpeciate) continue;

            if (cooledDown && Random.value < si * rollDt)
            {
                _lastSpeciationTime[kvp.Key] = Time.time;
                FireSpeciation(members);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    private void FireSpeciation(List<AgentController> lineage)
    {
        AgentController parent = lineage[Random.Range(0, lineage.Count)];
        if (parent == null) return;

        string newLineageName = KingdomNameGenerator.Generate();
        Color newColor = Color.HSVToRGB(Random.value, Random.Range(0.65f, 0.95f), Random.Range(0.85f, 1f));

        // Speciation branches a new OFFSPRING rather than relabeling the parent.
        // The ancestral lineage continues unchanged; the divergent descendant founds
        // its own clade with slightly drifted traits (niche divergence).
        float drift = parent.mutationStdDev * 2f;
        float vision    = Mathf.Clamp(PopulationStats.SampleDimension(parent.visionTrait,          drift), 0f, 100f);
        float speed     = Mathf.Clamp(PopulationStats.SampleDimension(parent.speedTrait,           drift), 0f, 100f);
        float strength  = Mathf.Clamp(PopulationStats.SampleDimension(parent.strengthTrait,        drift), 0f, 100f);
        float hardiness = Mathf.Clamp(PopulationStats.SampleDimension(parent.hardinessTrait,       drift), 0f, 100f);
        float tempPref  = Mathf.Clamp(PopulationStats.SampleDimension(parent.temperaturePreference,drift), 0f, 100f);
        float moistPref = Mathf.Clamp(PopulationStats.SampleDimension(parent.moisturePreference,   drift), 0f, 100f);

        Vector3 surfNormal = (parent.transform.position - _agentSpawner.planetCenter).normalized;
        Vector3 tangent = Vector3.Cross(surfNormal, Random.onUnitSphere).normalized;
        Vector3 pos = SphereSurface.MoveAlongSurface(
            parent.transform.position, tangent, 1.5f,
            _agentSpawner.planetCenter, _agentSpawner.planetRadius);

        // Technical safety valve only — see AgentController.TryReproduce for rationale. Speciation
        // spawns one new individual, same accounting as ordinary reproduction.
        if (_agentSpawner.ActiveAgents.Count >= AgentSpawner.MaxIndividualAgents) return;

        int newCommunityId = _nextCommunityId++;
        AgentController child = _agentSpawner.SpawnAgent(
            vision, speed, strength, hardiness, tempPref, moistPref,
            pos, newCommunityId, newColor);
        child.InheritGenesFrom(parent);
        child.TriggerSpeciation(newLineageName, newColor);
        AudioManager.Instance?.OnSpeciation();
        GameLog.LogGlobal($"Speciation: '{parent.AtmoLineage}' → '{newLineageName}' (community {newCommunityId})");

        Debug.Log($"[SpeciationManager] '{parent.AtmoLineage}' branched → '{newLineageName}' (community {newCommunityId}) | SI={_maxSIThisFrame:G4} era={_eraLabel} species={_speciesCount + 1}");
    }

    /// Fragmentation: maximum active isolation multiplier, or 1.0 background.
    private float GetFragmentation()
    {
        float best = 1.0f;
        foreach (var evt in _isolationEvents)
            if (evt.Multiplier > best) best = evt.Multiplier;
        return best;
    }

    /// Per-lineage geographic fragmentation from the actual spatial dispersion of a lineage's
    /// members across the sphere. This is the primary real driver of allopatric/peripatric
    /// speciation: a lineage whose members have drifted into widely separated clusters is
    /// diverging in isolation and should accumulate speciation pressure even in stable, calm
    /// conditions — WITHOUT needing a climate crisis. Previously fragmentation only ever moved
    /// via explicit isolation events (which nothing triggers in normal play), so it sat pinned
    /// at 1.00 for entire runs and speciation was effectively climate-crisis-only.
    ///
    /// Returns 1.0 for a tightly-clustered lineage, rising toward ~3.0 as members spread across
    /// the globe. Uses circular-variance (1 − mean resultant length) of member directions.
    private float ComputeSpatialFragmentation(List<AgentController> members)
    {
        if (_agentSpawner == null || members.Count < 3) return 1f;
        Vector3 c = _agentSpawner.planetCenter;

        Vector3 mean = Vector3.zero;
        int n = 0;
        foreach (var m in members)
        {
            if (m == null) continue;
            mean += (m.transform.position - c).normalized;
            n++;
        }
        if (n < 3) return 1f;
        float R = mean.magnitude / n;             // 1 = all same direction (clustered), →0 = dispersed
        float dispersion = 1f - Mathf.Clamp01(R); // 0 clustered → 1 spread across the globe
        return Mathf.Lerp(1f, 3f, dispersion);
    }

    /// Kiloyears of sim-time per real second for the currently-active EraTimeline phase.
    /// Falls back to Minimal-Replicator Seas compression when the clock is past its last phase
    /// or on a pre-Era-1 phase.
    private float GetCompressionKyrPerSecond()
    {
        // Fallback: Minimal-Replicator Seas is Era1StartIndex + 1 (index 9).
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
    /// Non-crisis floor raised ~5× (0.00001→0.00005 default, 0.00005→0.0002 Compartmentalized)
    /// so that ordinary spatial-fragmentation-driven divergence produces speciation during stable
    /// periods, not only when ClimateVolatility spikes at era transitions. Previously the default
    /// rate was so far below the climate multiplier's reach that branch events were effectively
    /// crisis-only. These remain TUNABLE — calibrate against per-lineage SI diagnostic output.
    private float GetBaseRate()
    {
        if (DeepTimeClock.Instance == null) return 0.00005f;
        int idx = DeepTimeClock.Instance.CurrentPhaseIndex;
        if (idx >= EraTimeline.Phases.Length) return 0.00005f;
        string label = EraTimeline.Phases[idx].PhaseLabel;
        if (label.Contains("Morphological Complexity") || label.Contains("Multicellularity")) return 0.0005f;
        if (label.Contains("Compartmentalized")) return 0.0002f;
        return 0.00005f;
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
        if (GameHUD.SuppressRawOverlays) return;
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
