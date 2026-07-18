using System.Collections.Generic;
using UnityEngine;

/// Sector-allocation economy model (policy-allocation-spec §0–§9).
/// One instance per CivilizationState; ticked by Era3Manager each trade tick.
public class CivilizationEconomy
{
    // ── Canonical sector keys ────────────────────────────────────────────────
    public const string Housing     = "sector.housing";
    public const string Industry    = "sector.industry";
    public const string Research    = "sector.research";
    public const string Culture     = "sector.culture";
    public const string Military    = "sector.military";
    public const string Environment = "sector.environment";

    public static readonly string[] AllSectors =
        { Housing, Industry, Research, Culture, Military, Environment };

    // ── Tunable constants (flag for tuning pass before ship, §8) ────────────
    private const float Alpha     = 0.45f;   // flow exponent in Cobb-Douglas
    private const float Beta      = 0.30f;   // stock exponent
    private const float GammaBase = 0.06f;   // stock-build rate per second
    private const float DeltaBase = 0.012f;  // stock depreciation per second (default)
    private const float DeltaSlow = 0.004f;  // slow-depreciation for signature stock (§6)

    // Mobilization drag: SectorVulnerability_i (§5.1)
    private static readonly Dictionary<string, float> Vulnerability = new()
    {
        { Housing,     0.70f },
        { Industry,    1.00f },
        { Research,    0.50f },
        { Culture,     0.35f },
        { Military,    0.00f },   // receiving sector — pays no drag
        { Environment, 0.10f },
    };

    // ── Per-sector observable state ───────────────────────────────────────────
    /// Allocation share for each sector (a_i, player-adjusted). Sums need not equal
    /// exactly 1 — they are normalized inside Tick before use.
    public readonly Dictionary<string, float> Allocation = new();
    /// Accumulated capacity (stock) per sector, [0, ∞).
    public readonly Dictionary<string, float> Stock = new();
    /// This-tick Cobb-Douglas output (for HUD display).
    public readonly Dictionary<string, float> Output = new();
    /// Post-mobilization-drag effective effort per sector (for HUD display).
    public readonly Dictionary<string, float> EffortEff = new();

    // ── Economy-wide observables ──────────────────────────────────────────────
    public float GDP              = 1.0f;
    public float MobilizationDrag = 0.0f;
    public float WarWeariness     = 0.0f;
    public float ExtractionTax    = 0.0f;

    // ── Architecture metadata ─────────────────────────────────────────────────
    private readonly CognitiveArchitecture _arch;
    private readonly string _slowDepreciationSector;  // §6 signature stock

    public CivilizationEconomy(CognitiveArchitecture arch)
    {
        _arch = arch;
        _slowDepreciationSector = arch switch
        {
            CognitiveArchitecture.Individuated => Research,    // externalized codified knowledge
            CognitiveArchitecture.Distributed  => Industry,   // network redundancy
            CognitiveArchitecture.Collective   => Culture,    // institutional/caste memory
            _                                   => Research,
        };

        // Balanced default allocation; players adjust via HUD sliders.
        foreach (var s in AllSectors) { Allocation[s] = 1f / AllSectors.Length; Stock[s] = 0.05f; Output[s] = 0f; EffortEff[s] = 0f; }
    }

    /// Advance the economy by dt seconds (call from Era3Manager trade tick). The four channel-
    /// investment dials (Biological excluded — its direct cost lands on PopGrowth instead, in
    /// Era3Manager.TickCohortGroup) impose a direct opportunity cost on the sector each one is tied
    /// to (era3-systems-implementation-spec §1 — new design, not a port of existing content, flagged
    /// as such there and provisional here). Coefficients are first guesses pending a tuning pass.
    /// industryMult/environmentMult: era3-systems-implementation-spec §8 Large Initiative ongoing
    /// cost/completion bonus for CommerceEngine (Industry, -15% ongoing / +15% permanent) and
    /// Terraformer (Environment, +15% permanent only — no ongoing Environment cost specified).
    /// Applied here (not post-hoc in Era3Manager) so the boosted/drained Output value also correctly
    /// feeds this tick's ΔStock accumulation below, not just the display readout.
    public void Tick(float dt, float investEconomic = 0f, float investInformational = 0f, float investExistential = 0f, float investCoercive = 0f, float industryMult = 1f, float environmentMult = 1f)
    {
        float economicDrag        = 1f - Mathf.Clamp01(investEconomic * 0.20f);
        float infoCultureDrag     = 1f - Mathf.Clamp01(investInformational * 0.15f);
        float infoEconomicCross   = 1f - Mathf.Clamp01(investInformational * 0.08f); // "friction ... possibly Economic" — smaller, secondary
        float existentialDrag     = 1f - Mathf.Clamp01(investExistential * 0.20f);
        float coerciveDrag        = 1f - Mathf.Clamp01(investCoercive * 0.20f);

        // Normalize allocations so shares sum to 1 regardless of player input.
        float totalAlloc = 0f;
        foreach (var s in AllSectors) totalAlloc += Mathf.Max(0f, Allocation[s]);
        if (totalAlloc < 0.001f) totalAlloc = 1f;

        // GDP grows slowly with total accumulated stock (aggregate capacity effect).
        float totalStock = 0f;
        foreach (var s in AllSectors) totalStock += Stock[s];
        GDP = 1f + totalStock * 0.15f;

        // Mobilization drag — convex in military allocation share (§5.1).
        // Moving from low→moderate absorbs idle capacity cheaply; total-war levels cannibalise.
        float milShare = Mathf.Max(0f, Allocation[Military]) / totalAlloc;
        MobilizationDrag = milShare * milShare * 1.8f;

        // War weariness accumulates above 35% military share, decays during peace (§5.2).
        if (milShare > 0.35f)
            WarWeariness = Mathf.Min(1f, WarWeariness + 0.008f * dt * (milShare - 0.35f) * 4f);
        else
            WarWeariness = Mathf.Max(0f, WarWeariness - 0.002f * dt);

        // Extraction tax grows without Environment investment (§4).
        float envStock = Stock[Environment];
        float envCap   = Mathf.Max(1f, Stock[Environment] + 0.5f);
        float rawExtraction = Stock[Industry] * 0.8f + Stock[Housing] * 0.4f;
        ExtractionTax = rawExtraction * 0.04f * (1f - Mathf.Clamp01(envStock / envCap));

        // Per-sector Cobb-Douglas output and stock update.
        foreach (var s in AllSectors)
        {
            float a      = Mathf.Max(0f, Allocation[s]) / totalAlloc;
            float effort = a * GDP;

            // era3-systems-implementation-spec §1: channel-investment direct costs, sector-targeted.
            if (s == Housing || s == Industry) effort *= economicDrag * infoEconomicCross;
            if (s == Culture) effort *= existentialDrag * infoCultureDrag;
            if (s == Military) effort *= coerciveDrag;

            // Mobilization drag: global tax with sector-differentiated incidence (§5.1).
            float vuln           = Vulnerability.TryGetValue(s, out float v) ? v : 0.5f;
            float effectiveEffort = effort * Mathf.Clamp01(1f - MobilizationDrag * vuln);
            EffortEff[s]          = effectiveEffort;

            // Cobb-Douglas: Output_i = Stock_i^β × Effort_i^α  (α+β<1 → diminishing returns, §3)
            float stockTerm  = Mathf.Pow(Mathf.Max(Stock[s], 0.01f), Beta);
            float effortTerm = Mathf.Pow(Mathf.Max(effectiveEffort, 0.001f), Alpha);
            Output[s] = stockTerm * effortTerm;
            if (s == Industry)    Output[s] *= industryMult;
            if (s == Environment) Output[s] *= environmentMult;

            // ΔStock_i = γ_i × Output_i − δ_i × Stock_i  (§3)
            float delta  = (s == _slowDepreciationSector) ? DeltaSlow : DeltaBase;
            float dStock = (GammaBase * Output[s] - delta * Stock[s]) * dt;
            Stock[s]     = Mathf.Max(0f, Stock[s] + dStock);
        }

        // Extraction tax compounds as a GDP drain (pay-now vs pay-later, §4).
        GDP = Mathf.Max(0.1f, GDP - ExtractionTax * dt * 0.5f);

        // War weariness erodes culture stock (§5.2 → §9 crisis stack).
        Stock[Culture] = Mathf.Max(0f, Stock[Culture] - WarWeariness * 0.003f * dt);
    }

    // ── Display-layer label lookup (§7.3) ────────────────────────────────────
    public string GetLabel(string key) => (_arch, key) switch
    {
        (CognitiveArchitecture.Distributed, Housing)     => "Substrate Expansion",
        (CognitiveArchitecture.Distributed, Industry)    => "Enzymatic Digestion",
        (CognitiveArchitecture.Distributed, Military)    => "Toxin Synthesis",
        (CognitiveArchitecture.Distributed, Research)    => "Signal Integration",
        (CognitiveArchitecture.Distributed, Culture)     => "Fruiting/Sporulation",
        (CognitiveArchitecture.Distributed, Environment) => "Symbiont Maintenance",
        (CognitiveArchitecture.Collective,  Housing)     => "Chamber Excavation",
        (CognitiveArchitecture.Collective,  Industry)    => "Caste Production",
        (CognitiveArchitecture.Collective,  Military)    => "Soldier-Caste Allocation",
        (CognitiveArchitecture.Collective,  Research)    => "Trail Relay Density",
        (CognitiveArchitecture.Collective,  Culture)     => "Nuptial & Queen Rites",
        (CognitiveArchitecture.Collective,  Environment) => "Midden & Garden Ecology",
        _                                                 => DefaultLabel(key),
    };

    private static string DefaultLabel(string key) => key switch
    {
        Housing     => "Housing",
        Industry    => "Industry",
        Research    => "Research",
        Culture     => "Culture",
        Military    => "Military",
        Environment => "Environment",
        _           => key,
    };
}
