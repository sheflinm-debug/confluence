using System.Collections.Generic;
using UnityEngine;

/// Implements era3-ecological-paths-spec.md — the non-CommerceEngine Era 3 paths' policy dials, war
/// maneuvers, and effect resolution.
///
/// IMPORTANT — approximation notice: the spec assumes four companion documents (era3-structure-spec,
/// era3-interface-addendum, era3-effect-resolution-spec, era3-formulae-spec) that define a formal
/// "ring" (Self/Target/Diffuse) + "channel" + severity-tier effect-resolution engine as ALREADY
/// BUILT. That formal engine does not exist in this codebase — what exists is CivilizationState's
/// Domain*/Resilience/Trade fields and the ad-hoc TickConflict war system in Era3Manager. This file
/// is a best-effort, clearly-approximate implementation built to satisfy this spec's intent using
/// what's actually here, reusing existing machinery wherever it already matches (Resilience/
/// DrainResilience IS the "crisis-stacking/collapse machinery" §3's runaway risk wants to feed into).
/// Every numeric constant is TUNABLE and was not sourced from the missing companion docs.
public static class Era3EcologicalPaths
{
    // ── Option catalogs (spec §4.2-§4.5) ────────────────────────────────────────────────────
    public struct OptionRow { public string[] Labels; public string[] Hints; }

    public static readonly Dictionary<Era3Path, OptionRow> ResourcePolicy = new()
    {
        [Era3Path.LivingReef] = new OptionRow
        {
            Labels = new[] { "Aggressive Spread", "Dense Consolidation", "Symbiotic Integration" },
            Hints  = new[] { "fast territorial growth, fragile thin margins", "slower growth, higher resilience", "recruit other species — unlocks biological-market access" },
        },
        [Era3Path.Terraformer] = new OptionRow
        {
            Labels = new[] { "Oxygenate", "Acidify / Reduce", "Stabilize" },
            Hints  = new[] { "push atmosphere toward your optimum", "push the opposite direction", "push toward a shared middle ground — suppresses runaway risk" },
        },
        [Era3Path.BloomFront] = new OptionRow
        {
            Labels = new[] { "Boom-Bust", "Sustainable Cropping", "Seasonal Following" },
            Hints  = new[] { "explosive growth then die-off", "capped growth, low collapse risk", "migrate to track resource pulses" },
        },
        [Era3Path.ApexPredator] = new OptionRow
        {
            Labels = new[] { "Overhunt", "Sustainable Cropping", "Prey Specialization" },
            Hints  = new[] { "max yield now, risks prey collapse", "capped, indefinitely sustainable", "high efficiency, one-niche concentration risk" },
        },
    };

    public static readonly Dictionary<Era3Path, OptionRow> ConflictPosture = new()
    {
        [Era3Path.LivingReef] = new OptionRow
        {
            Labels = new[] { "Smother", "Chemical Defense", "Substrate Partition" },
            Hints  = new[] { "overgrowth denies territory", "toxins, escalates with contact time", "de-escalate — stable non-aggression" },
        },
        [Era3Path.Terraformer] = new OptionRow
        {
            Labels = new[] { "Biochemical Warfare", "Niche Hoarding", "Neutral Terraforming" },
            Hints  = new[] { "atmosphere pushed to max extremity at a rival", "narrower, precisely targeted, less runaway risk", "actively suppresses everyone's runaway risk" },
        },
        [Era3Path.BloomFront] = new OptionRow
        {
            Labels = new[] { "Shade-Out", "Toxic Bloom", "Migratory Avoidance" },
            Hints  = new[] { "blocks light for producer rivals", "wide-radius biotoxin — hits the whole local food web", "de-escalate — relocate instead" },
        },
        [Era3Path.ApexPredator] = new OptionRow
        {
            Labels = new[] { "Territorial Exclusion", "Kleptoparasitism", "Trophic Coexistence" },
            Hints  = new[] { "drive off rival predators", "steal kills — resource transfer, no combat", "de-escalate — niche partitioning" },
        },
    };

    public static readonly Dictionary<Era3Path, OptionRow> Organization = new()
    {
        [Era3Path.LivingReef] = new OptionRow
        {
            Labels = new[] { "Polymorphic Castes", "Generalist Units", "Sacrificial Specialists" },
            Hints  = new[] { "specialized roles, higher ceiling, more overhead", "resilient to local loss, lower peak efficiency", "living munitions — high war effect, costs population directly" },
        },
        [Era3Path.Terraformer] = new OptionRow
        {
            Labels = new[] { "Local Optimization", "Planetary Engineering" },
            Hints  = new[] { "bounded influence — near-zero runaway risk", "unbounded — high effect, real runaway risk" },
        },
        [Era3Path.BloomFront] = new OptionRow
        {
            Labels = new[] { "Wide Scatter", "Concentrated Fronts" },
            Hints  = new[] { "low density — resilient, low peak dominance", "dense — high dominance, fragile, runaway-adjacent" },
        },
        [Era3Path.ApexPredator] = new OptionRow
        {
            Labels = new[] { "Nomadic Hunting", "Fixed Territory" },
            Hints  = new[] { "follows prey — resilient to local depletion", "defends a range — high dominance, vulnerable to incursion" },
        },
    };

    // ── Effect resolution: Self / Target / Diffuse rings (§2-§3) ───────────────────────────
    public enum EcoRing { Self, Target, Diffuse }

    private const float TargetStrikeRange  = 40f;  // same order as Era3Manager's war StrikeRange
    private const float DiffuseRange       = 70f;  // wider — "affects everyone sharing the biome"
    private const float DiffuseFalloff     = 0.35f; // Diffuse effects hit at this fraction of Target strength

    /// Applies an ecological effect. Self = grows/costs the acting civ's own largest settlement.
    /// Target = the nearest rival settlement in range takes the effect (population damage, flagged
    /// with the same RecentAttackFlash visual pulse the Commerce Engine war system already uses).
    /// Diffuse = EVERY other civ's settlement within DiffuseRange takes a reduced version — this is
    /// new: TickConflict only ever picked one nearest target, but "shared atmosphere" / "shared water
    /// column" effects described in the spec are inherently many-target, not single-target.
    public static void ApplyEffect(Era3Manager mgr, CivilizationState actingCiv, EcoRing ring, float magnitude)
    {
        if (mgr == null || actingCiv == null || Mathf.Approximately(magnitude, 0f)) return;
        var vis = Era3VisualManager.Instance;

        switch (ring)
        {
            case EcoRing.Self:
            {
                Era3Manager.Settlement best = LargestOwned(mgr, actingCiv.CommunityId);
                if (best != null) best.Population = Mathf.Max(1f, best.Population + magnitude);
                break;
            }
            case EcoRing.Target:
            {
                Era3Manager.Settlement mine = LargestOwned(mgr, actingCiv.CommunityId);
                if (mine == null) return;
                Vector3 minePos = vis != null ? vis.GetCurrentWorldPosition(mine) : mine.Position;
                Era3Manager.Settlement target = null; float bestDist = TargetStrikeRange;
                foreach (var s in mgr.Settlements)
                {
                    if (s.OwnerCivId == actingCiv.CommunityId) continue;
                    Vector3 p = vis != null ? vis.GetCurrentWorldPosition(s) : s.Position;
                    float d = Vector3.Distance(minePos, p);
                    if (d < bestDist) { bestDist = d; target = s; }
                }
                if (target == null) return;
                target.Population = Mathf.Max(1f, target.Population - Mathf.Abs(magnitude));
                mgr.RecentAttackFlash[target.Id] = Time.time + 5f;
                break;
            }
            case EcoRing.Diffuse:
            {
                Era3Manager.Settlement mine = LargestOwned(mgr, actingCiv.CommunityId);
                if (mine == null) return;
                Vector3 minePos = vis != null ? vis.GetCurrentWorldPosition(mine) : mine.Position;
                float diffuseMag = Mathf.Abs(magnitude) * DiffuseFalloff;
                foreach (var s in mgr.Settlements)
                {
                    if (s.OwnerCivId == actingCiv.CommunityId) continue;
                    Vector3 p = vis != null ? vis.GetCurrentWorldPosition(s) : s.Position;
                    if (Vector3.Distance(minePos, p) > DiffuseRange) continue;
                    s.Population = Mathf.Max(1f, s.Population - diffuseMag);
                    mgr.RecentAttackFlash[s.Id] = Time.time + 3f; // shorter/dimmer read than a direct Target strike
                }
                break;
            }
        }
    }

    private static Era3Manager.Settlement LargestOwned(Era3Manager mgr, int civId)
    {
        Era3Manager.Settlement best = null;
        foreach (var s in mgr.Settlements)
            if (s.OwnerCivId == civId && (best == null || s.Population > best.Population)) best = s;
        return best;
    }

    // ── base_magnitude formulas (§3) ────────────────────────────────────────────────────────
    private static float CivPopulation(Era3Manager mgr, int civId)
    {
        float sum = 0f;
        foreach (var s in mgr.Settlements) if (s.OwnerCivId == civId) sum += s.Population;
        return sum;
    }

    /// Terraformer: metabolic_footprint = biomass × gas_exchange_rate. gas_exchange_rate has no
    /// direct source field, approximated from population scale (more biomass = more throughput).
    public static float TerraformerMagnitude(Era3Manager mgr, CivilizationState civ)
    {
        float biomass = CivPopulation(mgr, civ.CommunityId);
        const float gasExchangeRate = 0.02f; // TUNABLE
        return biomass * gasExchangeRate;
    }

    /// Bloom Front: reproduction_rate × population × mobility. mobility = 1 (motile by definition of
    /// this path). reproduction_rate approximated from the chosen resource policy (Boom-Bust highest).
    public static float BloomFrontMagnitude(Era3Manager mgr, CivilizationState civ)
    {
        float population = CivPopulation(mgr, civ.CommunityId);
        float reproductionRate = civ.EcoResourcePolicy switch
        {
            0 => 0.06f, // Boom-Bust
            1 => 0.02f, // Sustainable Cropping
            2 => 0.03f, // Seasonal Following
            _ => 0.03f,
        };
        const float mobility = 1f;
        return population * reproductionRate * mobility;
    }

    /// Apex Predator: predation_success / prey_population. No tracked "prey population" per predator
    /// civ exists, so this approximates prey availability from local wild (non-civilized) population
    /// density near the predator's own settlement — thin where the predator has already hunted hard.
    public static float ApexPredatorMagnitude(Era3Manager mgr, AgentSpawner spawner, CivilizationState civ)
    {
        float predationSuccess = civ.EcoResourcePolicy switch
        {
            0 => 1.4f, // Overhunt
            1 => 0.8f, // Sustainable Cropping
            2 => 1.1f, // Prey Specialization
            _ => 1.0f,
        };
        int preyPopulation = 1;
        if (spawner != null)
        {
            var mine = LargestOwned(mgr, civ.CommunityId);
            if (mine != null)
            {
                Vector3 pos = Era3VisualManager.Instance != null ? Era3VisualManager.Instance.GetCurrentWorldPosition(mine) : mine.Position;
                var buf = new List<AgentController>();
                spawner.QueryNearby(pos, 25f, buf);
                foreach (var a in buf) if (a != null && a.communityId != civ.CommunityId) preyPopulation++;
            }
        }
        return predationSuccess / preyPopulation * 10f; // scaled so magnitude lands in a comparable range to the other two paths
    }

    // ── Runaway risk (§3) ───────────────────────────────────────────────────────────────────
    // runaway_probability(t) = base_runaway_rate × (atmosphere_dial_extremity)^δ × time_sustained
    private const float BaseRunawayRate = 0.0006f; // per-second, TUNABLE — no source value in the missing formulae spec
    private const float Delta = 1.6f;              // TUNABLE, spec requires > 1 (accelerating)

    /// Ticks a Terraformer's runaway exposure. Extremity is high for Planetary Engineering + a
    /// non-Stabilize policy, low otherwise. On a successful roll, drains Resilience hard — reusing
    /// CivilizationState's EXISTING collapse machinery (DrainResilience/HasCollapsed) rather than
    /// inventing a parallel one, matching the spec's explicit instruction to feed the same system.
    public static void TickRunawayRisk(Era3Manager mgr, CivilizationState civ, float dt)
    {
        bool planetaryEngineering = civ.EcoOrganization == 1;
        bool atExtremity = civ.EcoResourcePolicy == 0 || civ.EcoResourcePolicy == 1; // Oxygenate/Acidify, not Stabilize
        float extremity = planetaryEngineering && atExtremity ? 1f : planetaryEngineering ? 0.4f : atExtremity ? 0.3f : 0.05f;

        // era3-systems-implementation-spec §8: Large Initiative's Terraformer ongoing cost — doubled
        // RunawayExposure accumulation rate for the commitment's duration.
        float accumRate = civ.LargeInitiativeActive ? 2f : 1f;
        civ.RunawayExposure = atExtremity
            ? civ.RunawayExposure + dt * accumRate
            : Mathf.Max(0f, civ.RunawayExposure - dt * 2f); // decays faster than it accumulates when not pushing extremity

        float probability = BaseRunawayRate * Mathf.Pow(extremity, Delta) * civ.RunawayExposure;
        if (Random.value < probability * dt)
        {
            civ.DrainResilience(0.35f); // a runaway event is a real resilience-collapse event, not a nudge
            civ.RunawayExposure = 0f;
            Debug.Log($"[Era3][Ecological] {civ.Name} triggered a RUNAWAY terraforming event — resilience drained.");
        }
    }
}
