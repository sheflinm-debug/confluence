using System.Collections.Generic;
using UnityEngine;

/// Drives per-era gameplay changes for Era 1's six sub-phases (Abiogenesis through
/// Cambrian Explosion), keyed to DeepTimeClock's current phase index. Each era:
///   - Sets a target visual scale for all agents (microbe → macroscopic creature)
///   - Enforces a population cap (tiny primordial pool → Cambrian biodiversity burst)
///   - Shows a brief era-transition flash on screen
///
/// Wired up in SimulationBootstrap's onComplete callback, same as SpeciationManager.
/// Read-only by other systems: AgentController checks MaxPopulation before reproducing;
/// Update() rescales all live agents when an era boundary is crossed.
public class EraManager : MonoBehaviour
{
    public static EraManager Instance { get; private set; }

    // Seconds the era-transition banner stays on screen.
    private const float FlashDuration = 4f;

    // Per-Era1-subphase properties. Index 0 = Abiogenesis, 5 = Cambrian Explosion.
    // EraTimeline.Era1StartIndex = 8, so era sub-phase 0 = phase index 8.
    private static readonly float[] AgentScalePerEra    = { 0.05f, 0.08f, 0.10f, 0.15f, 0.22f, 0.32f };
    private static readonly int[]   MaxPopPerEra         = {    15,    40,    60,   100,   180,   300  };
    // Fraction of an agent's computed moveSpeed that's actually used.
    // Primordial microbes are sluggish; locomotion genes unlock speed over time.
    // Agents poll this in Update so era transitions take effect on all live agents.
    private static readonly float[] MoveSpeedMultPerEra = { 0.18f, 0.30f, 0.45f, 0.60f, 0.80f, 1.00f };
    private static readonly string[] EraNames          =
    {
        "Abiogenesis",
        "Prokaryotic Seas",
        "Great Oxidation Event",
        "Eukaryotes",
        "Multicellularity",
        "Cambrian Explosion",
    };

    /// Current Era1 sub-phase index (0–5). −1 while still in the cinematic / pre-Era1.
    public int CurrentEra { get; private set; } = -1;

    /// Visual scale all agents should be at in the current era.
    public float AgentTargetScale => CurrentEra >= 0 ? AgentScalePerEra[CurrentEra] : AgentScalePerEra[0];

    /// Hard population cap for the current era.
    public int MaxPopulation => CurrentEra >= 0 ? MaxPopPerEra[CurrentEra] : MaxPopPerEra[0];

    /// Fraction of computed moveSpeed that's active in the current era (0–1).
    public float MoveSpeedMultiplier => CurrentEra >= 0 ? MoveSpeedMultPerEra[CurrentEra] : MoveSpeedMultPerEra[0];

    private AgentSpawner _spawner;
    private string _flashText = "";
    private float _flashTimer;

    public void Init(AgentSpawner spawner)
    {
        Instance = this;
        _spawner = spawner;
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (DeepTimeClock.Instance == null) return;

        int phaseIdx = DeepTimeClock.Instance.CurrentPhaseIndex;
        int newEra = phaseIdx - EraTimeline.Era1StartIndex;
        newEra = Mathf.Clamp(newEra, 0, EraNames.Length - 1);

        // Only start tracking once we're actually in Era 1.
        if (phaseIdx < EraTimeline.Era1StartIndex) return;

        if (newEra != CurrentEra)
        {
            CurrentEra = newEra;
            OnEraTransition(newEra);
        }

        if (_flashTimer > 0f) _flashTimer -= Time.deltaTime;
    }

    private void OnEraTransition(int era)
    {
        _flashText = EraNames[era];
        _flashTimer = FlashDuration;

        // Rescale all existing agents to match the new era's visual scale.
        if (_spawner == null) return;
        float scale = AgentScalePerEra[era];
        foreach (var agent in _spawner.ActiveAgents)
        {
            if (agent != null)
                agent.transform.localScale = Vector3.one * scale;
        }

        // Nudge SpeciationManager climate volatility up at the Great Oxidation Event
        // and Cambrian Explosion — these are high-turnover periods.
        if (SpeciationManager.Instance != null)
        {
            if (era == 2) SpeciationManager.Instance.SetClimateVolatility(2.0f); // GOE
            else if (era == 5) SpeciationManager.Instance.SetClimateVolatility(2.5f); // Cambrian
            else SpeciationManager.Instance.SetClimateVolatility(1.0f);
        }

        Debug.Log($"[EraManager] Era transition → {EraNames[era]} (era {era}), agentScale={AgentScalePerEra[era]:F2}, maxPop={MaxPopPerEra[era]}");
    }

    void OnGUI()
    {
        if (_flashTimer <= 0f) return;

        float alpha = Mathf.Clamp01(_flashTimer / FlashDuration * 2f); // fade out in last half
        Color c = new Color(1f, 0.85f, 0.4f, alpha);
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 26,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };
        style.normal.textColor = c;

        string banner = $"── {_flashText} ──";
        GUI.Label(new Rect(0, Screen.height / 2f - 30f, Screen.width, 50f), banner, style);
    }
}
