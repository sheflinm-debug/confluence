using System.Collections.Generic;
using UnityEngine;

/// era3-adaptation-trees-spec §2, revised by era3-systems-implementation-spec §3: the tree for the
/// three "adapter" tracks (Living Reef/Terraformer/BloomFront) — evolved, not learned. Apex Predator
/// moved OUT of this tree (era3-systems-implementation-spec §3: it's grouped with CommerceEngine as
/// a "builder" track now, Tech+Idea only) and its domestication/host-guest gates moved to the Idea
/// tree (Era3TechTree.I_domestication / I_host_guest_tolerance) — see Era3Manager.HasDomesticationGate
/// / CanUseHostGuestRelation.
///
/// Same acquisition-formula shape as Era3TechTree, different substrate: primary channel investment
/// (now per-node via Channel, §1 — previously hardcoded to InvestBiological for every node, which
/// over-concentrated all twelve nodes on one dial), ReproductiveRate (fast breeders adapt faster), a
/// REQUIRED SelectionPressure term (zero pressure ⇒ zero progress, not just slow progress —
/// "crisis-driven transitions, not timer unlocks"), and genetic-diversity-based VariationFactor.
/// InvestBiological still applies to every node as a SECONDARY multiplier regardless of primary
/// channel (§1: "these remain fundamentally biological processes even when flavored toward another
/// channel") — folded into the primary term itself for Biological-channel nodes rather than applied
/// twice. No diffusion_bonus: evolution isn't taught, and there is no stealing an evolved trait (only
/// Contaminate-style attacks can force it, which is aggression, not a gift).
public static class Era3AdaptationTree
{
    public struct Node
    {
        public string     Id;
        public int        Tier;
        public string[]   Prereqs;
        public EraChannel Channel;
        public bool       LivingReefOnly; // A4a — "requires eusociality," only Living Reef qualifies here
    }

    public static readonly List<Node> Nodes = new List<Node>
    {
        new Node{ Id="A1a", Tier=1, Prereqs=new string[0],      Channel=EraChannel.Economic },
        new Node{ Id="A1b", Tier=1, Prereqs=new string[0],      Channel=EraChannel.Economic },
        new Node{ Id="A1c", Tier=1, Prereqs=new string[0],      Channel=EraChannel.Biological },
        new Node{ Id="A2a", Tier=2, Prereqs=new[]{"A1a"},       Channel=EraChannel.Informational },
        new Node{ Id="A2b", Tier=2, Prereqs=new[]{"A1b"},       Channel=EraChannel.Economic },
        new Node{ Id="A2c", Tier=2, Prereqs=new[]{"A1c"},       Channel=EraChannel.Coercive },
        new Node{ Id="A3a", Tier=3, Prereqs=new[]{"A2a","A2b"}, Channel=EraChannel.Biological },
        new Node{ Id="A3b", Tier=3, Prereqs=new[]{"A2c"},       Channel=EraChannel.Coercive },
        new Node{ Id="A4a", Tier=4, Prereqs=new[]{"A3a"},       Channel=EraChannel.Existential, LivingReefOnly=true },
        new Node{ Id="A4b", Tier=4, Prereqs=new[]{"A3a","A3b"}, Channel=EraChannel.Economic },
        // era3-systems-implementation-spec §3b: Tier-4 Coercive "I4c equivalent" — the three adapter
        // tracks have no Tech tree to reach I4c's own T4b prereq through, so they get a dedicated
        // Adaptation-tree node with the same permanent +20% MaxSustainableForce payoff instead of a
        // bypassed/auto-satisfied prereq. Mirrors I4c's own {I3a,T4b} prereq shape in Adaptation terms
        // (A3a = the tier-3 scale/mobilization spine, A4b = this tree's own "structural requirement").
        new Node{ Id="A4c", Tier=4, Prereqs=new[]{"A3a","A4b"}, Channel=EraChannel.Coercive },
        // domestication-spec.md §1: the ecological-track counterpart to Era3TechTree's
        // I_domestication — same tier/no-prereqs shape, applicable to Living Reef/Terraformer/
        // BloomFront (default IsApplicable already excludes CommerceEngine and, as of the tree
        // restructuring, Apex Predator too — Apex Predator uses I_domestication instead).
        new Node{ Id="A_domestication", Tier=1, Prereqs=new string[0], Channel=EraChannel.Economic },
        // era3-sovereignty-interaction-gaps-spec.md §2: HostGuestRelation eligibility for Terraformer/
        // BloomFront (Living Reef already has Symbiotic Integration serving this role — excluded below
        // in IsApplicable rather than needing this node too; Apex Predator uses the Idea-tree
        // I_host_guest_tolerance instead, same builder-track split as domestication above).
        new Node{ Id="A_host_guest_tolerance", Tier=1, Prereqs=new string[0], Channel=EraChannel.Existential },
    };

    private static Dictionary<string, Node> _byId;
    public static Node Get(string id)
    {
        if (_byId == null) { _byId = new Dictionary<string, Node>(); foreach (var n in Nodes) _byId[n.Id] = n; }
        return _byId[id];
    }

    // Order: [LivingReef, Terraformer, BloomFront, ApexPredator]. ApexPredator column is inert now
    // (IsApplicable excludes the whole tree for it) — left in place rather than reshaping the table.
    private static readonly Dictionary<string, string[]> Names = new()
    {
        ["A1a"] = new[]{ "Larval Dispersal Strategy", "Circulation Coupling", "Current-Riding", "Ranging Strategy" },
        ["A1b"] = new[]{ "Filter-Feeding Tuning", "Metabolic Throughput", "Nutrient Uptake", "Digestive Efficiency" },
        ["A1c"] = new[]{ "Substrate Tolerance", "Chemical Self-Regulation", "Salinity/Thermal Tolerance", "Climate Tolerance" },
        ["A2a"] = new[]{ "Polymorphic Castes", "Zonal Specialization", "Morph Switching", "Age/Sex Role Division" },
        ["A2b"] = new[]{ "Nutrient Banking", "Reserve Biomass", "Resting Spores", "Fat Reserves" },
        ["A2c"] = new[]{ "Allelochemistry", "Adversarial Chemistry", "Baseline Toxicity", "Venom" },
        ["A3a"] = new[]{ "Colonial Mass Scaling", "Bulk Metabolic Scaling", "Explosive Reproduction", "Pack Scaling" },
        ["A3b"] = new[]{ "Sweeper Tentacles", "Full Biochemical Warfare", "Red-Tide Synthesis", "Toxin Escalation" },
        ["A4a"] = new[]{ "Sacrificial Polyps", null, null, null },
        // era3-systems-implementation-spec §3b: exact T4b flavor text carried over verbatim (Era3TechTree
        // TechNames["T4b"] indices 3-5) — this content was already written for these tracks, just
        // unreachable behind Tech, which is now fully retired for them.
        ["A4b"] = new[]{ "Basin-Scale Reef Engineering", "Planetary Atmosphere Engineering", "Ocean-Basin Bloom Engineering", "Continental Dominance" },
        // §3b: Living Reef's name reused verbatim from I4c's own LivingReef flavor text (content was
        // prepared for a gate Living Reef could never structurally reach); Terraformer/BloomFront given
        // matching names.
        ["A4c"] = new[]{ "Basin-Wide Mobilization Doctrine", "Planetary Mobilization Doctrine", "Ocean-Basin Mobilization Doctrine", null },
        ["A_domestication"] = new[]{ "Symbiotic Cultivation", "Managed Growth Coupling", "Farmed Bloom Cycling", "Prey Herding Instinct" },
        ["A_host_guest_tolerance"] = new[]{ null, "Host-Acceptance Behavior", "Host-Acceptance Behavior", "Host-Acceptance Behavior" },
    };

    private static int TrackIndex(Era3Path path) => path switch
    {
        Era3Path.LivingReef   => 0,
        Era3Path.Terraformer  => 1,
        Era3Path.BloomFront   => 2,
        Era3Path.ApexPredator => 3,
        _ => 0,
    };

    public static string GetNodeName(string nodeId, CivilizationState civ)
    {
        if (!Names.TryGetValue(nodeId, out var arr)) return nodeId;
        int idx = TrackIndex(civ.Path);
        return idx < arr.Length ? (arr[idx] ?? nodeId) : nodeId;
    }

    /// era3-systems-implementation-spec §3: Adaptation tree is now Living Reef/Terraformer/BloomFront
    /// only — Apex Predator moved to Tech+Idea (grouped with CommerceEngine as a "builder" track).
    public static bool IsApplicable(CivilizationState civ, Node n)
    {
        if (civ.Path == Era3Path.CommerceEngine || civ.Path == Era3Path.ApexPredator) return false;
        if (n.LivingReefOnly && civ.Path != Era3Path.LivingReef) return false; // A4a — eusociality-gated
        // era3-sovereignty-interaction-gaps-spec.md §2: Living Reef already has Symbiotic Integration
        // as its HostGuestRelation gate — doesn't need this node too.
        if (n.Id == "A_host_guest_tolerance" && civ.Path == Era3Path.LivingReef) return false;
        return true;
    }

    public static bool PrereqsUnlocked(CivilizationState civ, Node n)
    {
        foreach (var p in n.Prereqs) if (!civ.UnlockedAdaptations.Contains(p)) return false;
        return true;
    }

    private static readonly float[] ResearchCostByTier = { 0f, 2f, 6f, 15f, 35f }; // TUNABLE, lighter than Tech/Idea (§3 open item 3)
    public static float ResearchCost(int tier) => ResearchCostByTier[Mathf.Clamp(tier, 1, 4)];

    private const float GammaR         = 0.7f; // same diminishing-returns exponent as the Tech tree (§2.2 reuses trees spec §7.1's shape)
    private const float WRepro         = 0.8f;
    private const float WSel           = 0.6f;
    private const float WBioSecondary  = 0.35f; // secondary-multiplier weight for InvestBiological when it isn't already the primary channel

    /// era3-systems-implementation-spec §1: primary dial is now this node's own Channel (was
    /// hardcoded InvestBiological for all twelve nodes). §2.2's formula otherwise unchanged: dial^γ ×
    /// (ReproductiveRate/R_reference)^w_repro × SelectionPressure^w_sel × VariationFactor(genetic
    /// diversity) × InvestBiological-secondary + 0 (no diffusion). SelectionPressure is REQUIRED, not
    /// optional — zero pressure means zero progress, matching the "crisis-driven transitions, not
    /// timer unlocks" principle more purely than the Idea tree does.
    public static float AcquisitionRate(Era3Manager mgr, CivilizationState civ, Node n)
    {
        if (!IsApplicable(civ, n) || civ.UnlockedAdaptations.Contains(n.Id)) return 0f;
        if (!PrereqsUnlocked(civ, n)) return 0f;

        float dial = Era3TechTree.ChannelDialValue(civ, n.Channel);
        if (dial <= 0f) return 0f;

        float pressure = mgr.SelectionPressure(civ, n.Id);
        if (pressure <= 0.01f) return 0f; // required, not optional — the whole point of this tree

        float reproRate = 5f / Mathf.Max(1f, mgr.AverageEatsToReproduce(civ.CommunityId)); // R_reference = 5 (eatsToReproduce)
        float diversity = mgr.GeneticDiversity(civ.CommunityId);
        float variationFactor = Mathf.Max(0.05f, 1f + 0.3f * (diversity - 0.5f) * 2f); // flat 0.3 sensitivity — spec gives no per-node values for this tree

        // §1: every Adaptation node stays a fundamentally biological process regardless of primary
        // channel — folded into the primary term for Biological-channel nodes (avoids double-counting
        // the same dial), applied as a genuine secondary multiplier otherwise.
        float bioSecondary = n.Channel == EraChannel.Biological
            ? 1f
            : Mathf.Pow(Mathf.Max(0.05f, civ.InvestBiological), WBioSecondary);

        return Mathf.Pow(dial, GammaR)
             * Mathf.Pow(Mathf.Max(0.05f, reproRate), WRepro)
             * Mathf.Pow(pressure, WSel)
             * variationFactor
             * bioSecondary;
    }
}
