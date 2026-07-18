using System.Collections.Generic;
using UnityEngine;

/// era3-polity-model-spec: Polity (not species) is the atomic tracked Era 3 unit. A civ's
/// AdministrativeReach caps how large a population + settlement spread it can coherently govern;
/// exceeding it builds SplinterPressure, which surfaces the Administrative Crisis decision card
/// (Era3HUD "Polity" tab). Population Roster tracks WHICH founding communities (this codebase's
/// species/community identity is 1:1) actually make up a civ's population, since Vassalization
/// (already implemented — Era3Manager.TryVassalize) and the new Federation path (TryFederate) can
/// pull other communities' population under one polity. SpeciesDisposition is a slow, species-pair
/// ledger seeded from actual Era 1/2 interaction history (SpeciesRelationshipManager) — the
/// substrate diplomacy-ai-spec §1.1 reads for a polity's opening position at first contact.
public static class Era3Polity
{
    // ── Roster ────────────────────────────────────────────────────────────────
    public struct RosterEntry
    {
        public int   CommunityId;   // founding community/species id
        public float Fraction;      // share of total polity population — entries sum to 1
    }

    // ── Administrative Reach (spec §2) ───────────────────────────────────────
    // TUNABLE base capacity + per-settlement scaling. Architecture changes how cheaply a polity
    // coordinates at range (Distributed/Collective's native coordination advantage — same
    // complementarity-spec logic used elsewhere for BehavioralFidelity).
    private const float BaseReach = 3f;
    private static readonly Dictionary<CognitiveArchitecture, float> ReachScalePerSettlement = new()
    {
        { CognitiveArchitecture.Individuated, 1.0f },
        { CognitiveArchitecture.Distributed,  1.4f },
        { CognitiveArchitecture.Collective,   1.2f },
    };

    /// How much reach a civ's current size/spread actually demands. Sub-linear in population
    /// (sqrt) — a bigger polity is harder to hold together, but not proportionally so.
    public static float ComputeReachDemand(int settlementCount, float totalPopulation)
        => settlementCount * 1.0f + Mathf.Sqrt(Mathf.Max(0f, totalPopulation)) * 0.15f;

    /// How much reach a civ can currently field. InvestInformation funds administration/
    /// communication — reuses the existing channel dial rather than adding a bespoke one.
    /// decentralizeBonus is the permanent multiplier granted by resolving an Administrative
    /// Crisis with "Decentralize" (Era3Manager.CivilizationState-side field, applied by caller).
    public static float ComputeReachCapacity(CivilizationState civ, int settlementCount, float decentralizeBonus)
    {
        float scale = ReachScalePerSettlement.TryGetValue(civ.Architecture, out float s) ? s : 1.0f;
        float infoBonus = 1f + civ.InvestInformation * 0.8f;
        // era3-systems-implementation-spec §2: CommInfraInvest (Individuated only) feeds AdminReach
        // directly — dedicated communication infrastructure, distinct from raw Informational-channel
        // investment (infoBonus above).
        float commBonus = civ.Architecture == CognitiveArchitecture.Individuated ? 1f + civ.CommInfraInvest * 0.3f : 1f;
        return (BaseReach + settlementCount * scale) * infoBonus * commBonus * (1f + decentralizeBonus);
    }

    /// Splinter pressure rises when demand exceeds capacity, decays otherwise. [0,1]. Builds
    /// faster than it decays — coherence is easy to lose, slow to rebuild.
    public static float TickSplinterPressure(float current, float demand, float capacity, float dt)
    {
        float deficitRatio = capacity > 0.01f ? Mathf.Max(0f, (demand - capacity) / capacity) : 1f;
        float target = Mathf.Clamp01(deficitRatio);
        float rate = target > current ? 0.05f : 0.02f;
        return Mathf.MoveTowards(current, target, rate * dt);
    }

    // ── Species Disposition seeding (diplomacy-ai-spec §1.1, primitives-spec) ──────────────────
    // Maps the classical ecology sign-table interaction a pair of species actually had in Era 1/2
    // onto the [-1,1] disposition scale a polity opens diplomacy with. Predation isn't a distinct
    // InteractionType in SpeciesRelationshipManager — Parasitism is its closest sign-table analog
    // (the exploiter/exploited "+/-" asymmetry) and is used for it here.
    public static float SeedFromInteraction(InteractionType t) => t switch
    {
        InteractionType.Mutualism    =>  0.5f,
        InteractionType.Commensalism =>  0.25f,
        InteractionType.Neutralism   =>  0.0f,
        InteractionType.Amensalism   => -0.2f,
        InteractionType.Competition  => -0.4f,
        InteractionType.Parasitism   => -0.55f,
        _ => 0f,
    };

    // ── Shannon-entropy roster diversity ────────────────────────────────────────────────────────
    // Reused by the Tech/Idea tree's VariationFactor (era3-tech-idea-trees-spec §7) — a
    // multi-species polity's idea/tech acquisition is sensitive to roster diversity, and this is
    // the one formula both specs need, so it lives here rather than being duplicated.
    public static float RosterShannonDiversity(List<RosterEntry> roster)
    {
        if (roster == null || roster.Count <= 1) return 0f;
        float h = 0f;
        foreach (var e in roster)
        {
            if (e.Fraction <= 0f) continue;
            h -= e.Fraction * Mathf.Log(e.Fraction, 2f);
        }
        float hMax = Mathf.Log(roster.Count, 2f);
        return hMax > 0f ? Mathf.Clamp01(h / hMax) : 0f;
    }
}
