using UnityEngine;

/// era3-warfare-mechanics-spec: a real, deliberate declare-war/peace flow (replacing the previous
/// fully-automatic "invest in the doctrine and strikes just start happening" behavior — the user's
/// explicit ask), tech-gated force projection, Levy vs. Standing Force phases, and per-track
/// upkeep/fiscal-ceiling costs. "There is no military primitive" (§7): war capability is expressed
/// through whichever domain(s) a civ's architecture natively reaches (DomainKinetic/Biochemical/
/// Informational/Economic, already tracked on CivilizationState) — this file doesn't add a
/// competing generic "military stat", it computes real numbers ON TOP of that existing domain model.
///
/// §13's icosahedral geodesic grid (distance/pathfinding) is explicitly flagged by its own source
/// spec as unconfirmed-to-exist and blocking — it doesn't exist in this codebase (confirmed: no grid,
/// only render-mesh generation). Distance uses the same straight-line world-position approach
/// TickConflict already used for its old fixed StrikeRange, now driving a real tech-scaled
/// ProjectionRange instead of one constant — a flagged placeholder, not a silent gap.
public static class Era3Warfare
{
    /// What an attack targets — the spec's "target-subsystem selection" (§9). Population/conquest
    /// is the old default behavior; the other three are new, distinct mechanical outcomes.
    public enum WarSubsystem { Population, Military, Production, Structures }

    // 1.0 range unit ≈ the old fixed 40-world-unit StrikeRange, so Tier-1 civs (no relevant tech
    // yet) see unchanged reach — this only EXTENDS range as tech unlocks, never regresses it.
    public const float ProjectionRangeWorldScale = 40f;

    /// BaseRange=1 + T2c+1, I2b+1, T3a+0.5, T4a+3 (spec §3, verbatim).
    public static float ComputeProjectionRange(CivilizationState civ)
    {
        float r = 1f;
        if (civ.UnlockedNodes.Contains("T2c")) r += 1f;
        if (civ.UnlockedNodes.Contains("I2b")) r += 1f;
        if (civ.UnlockedNodes.Contains("T3a")) r += 0.5f;
        if (civ.UnlockedNodes.Contains("T4a")) r += 3f;
        return r;
    }

    /// Levy (pre-I2b): no continuous force, no upkeep, raised per-conflict only — approximated
    /// here as "war capability exists but StandingForce never accrues". Standing Force (post-I2b):
    /// continuous force tied to the Coercive structure-investment dial (index 4), the same
    /// "no new UI, real opportunity cost" pattern the Tech tree's ChannelDial reuse already
    /// established — sinking Coercive into a standing army is capacity you didn't spend elsewhere.
    /// Requires I2b unlocked, full stop — no e3_writing fallback (removed: that let this phase
    /// transition happen without the real tech-tree gate, the same soft-bypass pattern flagged
    /// elsewhere for e3_state_formation).
    public static bool IsStandingForcePhase(CivilizationState civ)
        => civ.UnlockedNodes.Contains("I2b");

    private const float ForceCapacityMultiplier = 2.5f; // TUNABLE

    /// AdministrativeReach × ForceCapacityMultiplier × (1.5 if I3a) × (1.2 if I4c/A4c) — the fiscal
    /// ceiling; exceeding it bleeds Resilience rather than being hard-capped, so it's a real cost,
    /// not a wall.
    public static float ComputeMaxSustainableForce(CivilizationState civ)
    {
        float mult = civ.UnlockedNodes.Contains("I3a") ? 1.5f : 1f;
        // era3-systems-implementation-spec §5/§3b: I4c (Commerce Engine/Apex Predator, shares T4b's
        // structural requirement) or its Adaptation-tree equivalent A4c (Living Reef/Terraformer/
        // BloomFront) grant a permanent +20% on completion. Recomputed fresh here rather than applied
        // as a one-shot OnNodeUnlocked mutation, matching how I3a's own bonus already works — this
        // whole function is already a pure read of unlocked-node state every call.
        if (civ.UnlockedNodes.Contains("I4c") || civ.UnlockedAdaptations.Contains("A4c")) mult *= 1.2f;
        // era3-systems-implementation-spec §8: Apex Predator's Large Initiative ("Coordinated
        // Territory Network") completion bonus — permanent +20% MaxSustainableForce, same shape as
        // I4c/A4c above.
        if (civ.Path == Era3Path.ApexPredator && civ.LargeInitiativeCompleted) mult *= 1.2f;
        // era3-systems-implementation-spec §7: MobilizationDrag reduces the effective ceiling —
        // pairs directly with I4c/A4c's +20% (investment counteracting ongoing friction). Provisional
        // coefficient, pending a tuning pass once running.
        if (civ.Economy != null) mult *= Mathf.Max(0f, 1f - civ.Economy.MobilizationDrag * 0.15f);
        // civ.AdministrativeReach is already policy-adjusted (Era3Manager.TickPolity); this multiplier
        // is for policies that act DIRECTLY on MaxSustainableForce (Codified Legalism, Soldier Surge,
        // Devolved Federation, ...), distinct from the ones that act on AdministrativeReach itself.
        return civ.AdministrativeReach * ForceCapacityMultiplier * mult
             * Era3PolicyCatalog.GetVar(civ, Era3PolicyCatalog.Var.MaxSustainableForce);
    }

    private static float UpkeepRate(CivilizationState civ) => civ.Architecture switch
    {
        CognitiveArchitecture.Individuated => 0.05f, // salary/food — kinetic-domain armies
        CognitiveArchitecture.Distributed  => 0.03f, // biochemical upkeep is cheaper per unit force
        CognitiveArchitecture.Collective   => 0.04f, // caste-fed soldiers, mid-cost
        _ => 0.05f,
    };

    /// StandingForce^1.3 × UpkeepRate(architecture) — super-linear, so a large standing army costs
    /// disproportionately more than a small one (spec §4).
    public static float ComputeUpkeepCost(CivilizationState civ)
        => Mathf.Pow(Mathf.Max(0f, civ.StandingForce), 1.3f) * UpkeepRate(civ)
         * Era3PolicyCatalog.GetVar(civ, Era3PolicyCatalog.Var.UpkeepCost);

    /// Super-linear overextension penalty: distance-beyond-range × force-size ratio (spec §3-4).
    /// Returns a [0,1] multiplier applied to strike effectiveness — 1 = full strength at/under range,
    /// falling off sharply once a strike reaches beyond a civ's ProjectionRange.
    public static float OverextensionMultiplier(float distanceWorld, CivilizationState attacker)
    {
        float rangeWorld = attacker.ProjectionRange * ProjectionRangeWorldScale;
        if (distanceWorld <= rangeWorld) return 1f;
        float overBy = (distanceWorld - rangeWorld) / rangeWorld;
        float forceRatio = Mathf.Clamp01(attacker.StandingForce / Mathf.Max(1f, Era3Warfare.ComputeMaxSustainableForce(attacker)));
        // Super-linear falloff — quadratic in overextension, cushioned somewhat by a larger relative force.
        return Mathf.Clamp01(1f - overBy * overBy * (1f + (1f - forceRatio)));
    }
}
