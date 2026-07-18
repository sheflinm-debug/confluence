using System.Collections.Generic;
using UnityEngine;

/// Which existing channel-investment dial (CivilizationState.InvestXxx) drives a node's research.
public enum EraChannel { Economic, Biological, Informational, Existential, Coercive }

/// era3-tech-idea-trees-spec: the 26-node (13 Tech + 13 Idea) progression tree and its §7
/// acquisition engine. Tech = material/physical capability, applies to all seven tracks
/// (three Commerce Engine architectures + LivingReef + the three ecological paths — everyone
/// builds things). Idea = institutional/social, applies only to the three Commerce Engine
/// architectures (the ecological paths have no government/belief institutions to organize — spec §0).
///
/// Two structural gaps in the source spec, resolved here (documented since they're judgment calls,
/// not spec text): (1) §6's channel-touchpoint table omits T1a/T4b/I2b/I4c entirely and dual-lists
/// I1b/I2c/I3b under both Informational AND Existential — each is resolved below to the single
/// channel its §2.1/§3.1 FUNCTIONAL description actually ties to (see inline comments on those four
/// nodes). (2) §7.1's formula includes both `VariationFactor(civ,node)` (itself already a function
/// of `variation_sensitivity`, per §7.3) AND a separate `^w_v(node)` exponent that is never defined
/// anywhere in the spec — no second weight table exists. Rather than silently reusing
/// `variation_sensitivity` a second time as an undocumented exponent (double-counting the same
/// scalar in two roles), VariationFactor is applied as a single multiplicative term, which is the
/// coherent reading of §7.3's stated purpose.
public static class Era3TechTree
{
    public struct Node
    {
        public string   Id;
        public bool     IsIdea;
        public int      Tier;
        public string[] Prereqs;
        public EraChannel Channel;
        public float    VariationSensitivity;
    }

    public static readonly List<Node> Nodes = new List<Node>
    {
        // ── Tech (§2.1) ────────────────────────────────────────────────────────────────────────
        new Node{ Id="T1a", Tier=1, Prereqs=new string[0],             Channel=EraChannel.Coercive,      VariationSensitivity= 0.05f },
        new Node{ Id="T1b", Tier=1, Prereqs=new string[0],             Channel=EraChannel.Coercive,      VariationSensitivity= 0.05f },
        new Node{ Id="T1c", Tier=1, Prereqs=new string[0],             Channel=EraChannel.Economic,      VariationSensitivity= 0.20f },
        new Node{ Id="T2a", Tier=2, Prereqs=new[]{"T1a"},              Channel=EraChannel.Coercive,      VariationSensitivity=-0.30f },
        new Node{ Id="T2b", Tier=2, Prereqs=new[]{"T1a","T1b"},        Channel=EraChannel.Coercive,      VariationSensitivity= 0.05f },
        new Node{ Id="T2c", Tier=2, Prereqs=new[]{"T1c"},              Channel=EraChannel.Economic,      VariationSensitivity= 0.20f },
        new Node{ Id="T2d", Tier=2, Prereqs=new[]{"T1c"},              Channel=EraChannel.Economic,      VariationSensitivity= 0.20f },
        new Node{ Id="T3a", Tier=3, Prereqs=new[]{"T2a","T2b"},        Channel=EraChannel.Coercive,      VariationSensitivity= 0.30f },
        new Node{ Id="T3b", Tier=3, Prereqs=new[]{"T2c","T2d"},        Channel=EraChannel.Economic,      VariationSensitivity= 0.10f },
        new Node{ Id="T3c", Tier=3, Prereqs=new[]{"T2a"},              Channel=EraChannel.Biological,    VariationSensitivity=-0.30f },
        new Node{ Id="T3d", Tier=3, Prereqs=new[]{"T2c"},              Channel=EraChannel.Informational, VariationSensitivity= 0.45f },
        new Node{ Id="T4a", Tier=4, Prereqs=new[]{"T3a","T3b"},        Channel=EraChannel.Coercive,      VariationSensitivity=-0.35f },
        new Node{ Id="T4b", Tier=4, Prereqs=new[]{"T3b"},              Channel=EraChannel.Economic,      VariationSensitivity=-0.10f }, // "Structures ≥ threshold" extra gate — see IsApplicable
        new Node{ Id="T4c", Tier=4, Prereqs=new[]{"T3a","T3c"},        Channel=EraChannel.Biological,    VariationSensitivity= 0.20f },

        // ── Idea (§3.1) ────────────────────────────────────────────────────────────────────────
        new Node{ Id="I1a", IsIdea=true, Tier=1, Prereqs=new string[0],           Channel=EraChannel.Biological,    VariationSensitivity=0.25f },
        // I1b: §6 dual-lists Informational+Existential; §3.1 ties it directly to "tier-1/2
        // Existential investment" — Existential is the mechanically correct single channel.
        new Node{ Id="I1b", IsIdea=true, Tier=1, Prereqs=new string[0],           Channel=EraChannel.Existential,    VariationSensitivity=0.25f },
        new Node{ Id="I1c", IsIdea=true, Tier=1, Prereqs=new string[0],           Channel=EraChannel.Economic,       VariationSensitivity=0.25f },
        // domestication-spec §1: Commerce Engine's domestication gate — "sits alongside I1c," same
        // tier/channel/no-prereqs shape. Living Reef/Terraformer/BloomFront/ApexPredator use
        // A_domestication (Era3AdaptationTree) instead — see IdeaNames below (null for those tracks).
        new Node{ Id="I_domestication", IsIdea=true, Tier=1, Prereqs=new string[0], Channel=EraChannel.Economic,     VariationSensitivity=0.25f },
        new Node{ Id="I2a", IsIdea=true, Tier=2, Prereqs=new[]{"I1a"},            Channel=EraChannel.Coercive,       VariationSensitivity=0.35f },
        // I2b: absent from §6 entirely — "Codified Communication (writing)" is squarely the
        // Informational channel (this codebase's InvestInformation dial, matching e3_writing's own
        // effect of raising InvestInformation/DomainInformational).
        new Node{ Id="I2b", IsIdea=true, Tier=2, Prereqs=new[]{"I1c"},            Channel=EraChannel.Informational,  VariationSensitivity=0.35f },
        // I2c: same dual-listing as I1b, same resolution (§3.1: "crosses the tier-3 Existential
        // eligibility threshold").
        new Node{ Id="I2c", IsIdea=true, Tier=2, Prereqs=new[]{"I1b"},            Channel=EraChannel.Existential,    VariationSensitivity=0.40f },
        new Node{ Id="I2d", IsIdea=true, Tier=2, Prereqs=new[]{"I1a","I1c"},      Channel=EraChannel.Biological,     VariationSensitivity=0.35f },
        new Node{ Id="I3a", IsIdea=true, Tier=3, Prereqs=new[]{"I2a","I2b"},      Channel=EraChannel.Coercive,       VariationSensitivity=0.50f },
        // I3b: same dual-listing, same resolution (§3.1: "orthodoxy/pluralism dial" = OrthodoxyLevel,
        // an Existential-tab dial).
        new Node{ Id="I3b", IsIdea=true, Tier=3, Prereqs=new[]{"I2c"},            Channel=EraChannel.Existential,    VariationSensitivity=0.55f },
        new Node{ Id="I3c", IsIdea=true, Tier=3, Prereqs=new[]{"I2d","I3a"},      Channel=EraChannel.Coercive,       VariationSensitivity=0.50f },
        new Node{ Id="I3d", IsIdea=true, Tier=3, Prereqs=new[]{"I1c","I2b"},      Channel=EraChannel.Economic,       VariationSensitivity=0.45f },
        new Node{ Id="I4a", IsIdea=true, Tier=4, Prereqs=new[]{"I3b"},            Channel=EraChannel.Informational,  VariationSensitivity=0.55f },
        new Node{ Id="I4b", IsIdea=true, Tier=4, Prereqs=new[]{"I3c"},            Channel=EraChannel.Coercive,       VariationSensitivity=0.60f },
        // I4c: absent from §6; spec text explicitly gives its prereq as "I3a + Tech's T4b" — the one
        // cross-tree prereq in the whole set, encoded directly (Prereqs is tree-agnostic by id).
        new Node{ Id="I4c", IsIdea=true, Tier=4, Prereqs=new[]{"I3a","T4b"},      Channel=EraChannel.Coercive,       VariationSensitivity=0.05f },
        // era3-systems-implementation-spec §5: I3b was believed Informational (it gates "Open
        // Academy," a knowledge-institution PolCat option) but is confirmed Existential — the
        // spec's own fallback for that case is a genuinely new Tier-3 node instead of repurposing
        // I3b. Prereq I2b (Codified Communication) mirrors I3a's own Informational-spine dependency.
        new Node{ Id="I3e", IsIdea=true, Tier=3, Prereqs=new[]{"I2b"},            Channel=EraChannel.Informational,  VariationSensitivity=0.40f },
        // era3-sovereignty-interaction-gaps-spec.md §2 / era3-systems-implementation-spec §3: Apex
        // Predator's HostGuestRelation gate, moved here from the Adaptation tree (A_host_guest_tolerance)
        // now that Apex Predator is a Tech+Idea "builder" track with no Adaptation-tree access at all.
        new Node{ Id="I_host_guest_tolerance", IsIdea=true, Tier=1, Prereqs=new string[0], Channel=EraChannel.Economic, VariationSensitivity=0.25f },
    };

    private static Dictionary<string, Node> _byId;
    private static void EnsureById()
    {
        if (_byId == null) { _byId = new Dictionary<string, Node>(); foreach (var n in Nodes) _byId[n.Id] = n; }
    }
    public static Node Get(string id) { EnsureById(); return _byId[id]; }
    public static bool TryGet(string id, out Node n) { EnsureById(); return _byId.TryGetValue(id, out n); }

    // ── Per-track display names ──────────────────────────────────────────────────────────────
    // Tech order: [Individuated, Distributed, Collective, LivingReef, Terraformer, BloomFront, ApexPredator]. Null = N/A.
    private static readonly Dictionary<string, string[]> TechNames = new()
    {
        ["T1a"] = new[]{ "Toolcraft", "Structural Biomass Allocation", "Carapace Development", "Reef Substrate Deposition", "Bulk Tissue Accumulation", "Rapid Cell-Wall Synthesis", "Musculoskeletal Reinforcement" },
        ["T1b"] = new[]{ "Land Claim", "Chemical Perimeter Signaling", "Nest Boundary Marking", "Colonial Margin Definition", null, null, "Range Scent-Marking" },
        ["T1c"] = new[]{ "Craft Specialization", "Assimilation Efficiency", "Foraging Efficiency", "Filter-Feeding Optimization", "Metabolic Throughput Scaling", "Nutrient Uptake Acceleration", "Digestive Efficiency" },
        ["T2a"] = new[]{ "Military Doctrine", "Coordinated Severance Response", "Soldier-Caste Doctrine", "Coordinated Aggression Response", "Adversarial Chemistry Threshold", "Bloom Synchronization", "Hunting-Coordination Instinct" },
        ["T2b"] = new[]{ "Fortification", "Underground Hardening", "Chamber Reinforcement", "Skeletal Density Increase", "Buffering Capacity", "Cyst/Dormancy Defense", "Den/Range Defense" },
        ["T2c"] = new[]{ "Trade Roads", "Extended Graft Reach", "Trail Pheromone Networks", "Colonial Current-Riding", "Atmospheric Circulation Reach", "Current-Borne Dispersal", "Extended Range Tracking" },
        ["T2d"] = new[]{ "Granaries", "Boom/Crash Buffering", "Biomass Stockpile Chambers", "Nutrient Bank Storage", "Reserve Biomass Banking", "Resting-Spore Banking", "Fat/Reserve Storage" },
        ["T3a"] = new[]{ "Cross-Domain Doctrine", "Cross-Medium Adaptation", "Cross-Caste Flexibility", "Mixed-Substrate Tolerance", "Alternate-Chemistry Tolerance", "Cross-Habitat Tolerance", "Alternate-Prey Adaptation" },
        ["T3b"] = new[]{ "Mass Production", "Network-Wide Output Scaling", "Mass Caste Output", "Colonial Mass Growth", "Bulk Metabolic Scaling", "Explosive Reproduction Scaling", "Pack-Scale Yield" },
        ["T3c"] = new[]{ "Bioweapons Program", "Mycotoxin Engineering", "Venom/Toxin Caste Development", "Allelochemical Escalation", "Full Biochemical Warfare", "Red-Tide Toxin Synthesis", "Venom/Toxin Adaptation" },
        ["T3d"] = new[]{ "Propaganda Infrastructure", "Signal-Protocol Warfare", "Stigmergic Disruption", null, null, null, null },
        ["T4a"] = new[]{ "Long-Range Weapons", "Explosive Spore/Propagule Dispersal", "Long-Range Raiding Castes", "Long-Range Larval Dispersal", "Global Circulation Engineering", "Extended Bloom-Front Range", "Extended Territorial Range" },
        ["T4b"] = new[]{ "Orbital/Space Infrastructure", "Continental Network Engineering", "Mega-Colony Engineering", "Basin-Scale Reef Engineering", "Planetary Atmosphere Engineering", "Ocean-Basin Bloom Engineering", "Continental Range Dominance" },
        ["T4c"] = new[]{ "Public Health Infrastructure", "Full Compartmentalization Suite", "Immune-Caste Infrastructure", "Colonial Immune Response", "Self-Chemistry Regulation", "Bloom-Collapse Resistance", "Disease/Parasite Resistance" },
    };

    // Idea order: [Individuated, Distributed, Collective, LivingReef, Terraformer, BloomFront, ApexPredator]
    // — extended from 4 to 7 columns by era3-systems-implementation-spec §3 (Idea tree now applies to
    // all five tracks, not Commerce Engine + a Living-Reef Coercive-only slice). The six-node
    // administrative/coercive spine (I2a/I2b/I3a/I3c/I4b/I4c) uses the spec's own §3a names verbatim.
    // The remaining nine (I1a/I1b/I1c/I_domestication/I2c/I2d/I3b/I3d/I4a) are extrapolated here from
    // the existing four-column names, following the same per-track vocabulary each track's TechNames
    // row already establishes (Terraformer=chemistry/atmosphere, BloomFront=bloom/current, ApexPredator
    // =pack/territory/range). I2c/I3b/I4a keep Distributed/Collective/LivingReef null exactly as they
    // already were pre-existing (sparse, Individuated-flavored-only nodes) — not a new gap.
    private static readonly Dictionary<string, string[]> IdeaNames = new()
    {
        ["I1a"] = new[]{ "Kinship Custom", "Clonal-Branch Recognition", "Brood/Caste Norms", "Colonial Lineage Recognition", "Strain-Lineage Recognition", "Bloom-Kin Recognition", "Pack-Kinship Recognition" },
        ["I1b"] = new[]{ "Folk Ritual", "Chemical Ritual Signaling", "Pheromone Ritual Memory", "Colonial Ritual Cycling", "Chemical Ritual Cycling", "Bloom-Cycle Ritual Memory", "Den Ritual Memory" },
        ["I1c"] = new[]{ "Gift/Reciprocity Custom", "Graft Reciprocity Norms", "Trophallaxis Exchange Norms", "Symbiotic Exchange Norms", "Chemical Exchange Norms", "Nutrient Exchange Norms", "Prey-Share Reciprocity Norms" },
        // domestication-spec.md §1 / era3-systems-implementation-spec §3: Commerce Engine AND now Apex
        // Predator (moved from Adaptation-tree A_domestication now that it's a builder track) — Living
        // Reef/Terraformer/BloomFront still use A_domestication (Era3AdaptationTree), hence null here.
        ["I_domestication"] = new[]{ "Herding & Cultivation", "Graft-Stock Husbandry", "Caste-Farmed Broodstock", null, null, null, "Territorial Herding Doctrine" },
        ["I2a"] = new[]{ "Chieftaincy", "Hub-Node Precedence", "Queen/Founder Precedence", "Founder-Colony Precedence", "Primary Strain Dominance", "Founding Bloom Precedence", "Alpha Lineage Precedence" },
        ["I2b"] = new[]{ "Writing", "Signal-Protocol Standardization", "Stigmergic Encoding Standard", "Colonial Signal Standardization", "Chemical Signature Standardization", "Bloom-Signal Standardization", "Territorial Signal Standardization" },
        ["I2c"] = new[]{ "Cosmology", null, null, null, null, null, null },
        ["I2d"] = new[]{ "Ethnic/Tribal Affinity", "Network-Kin Affinity", "Colony-Kin Affinity", "Colonial-Kin Affinity", "Strain-Kin Affinity", "Bloom-Kin Affinity", "Pack-Kin Affinity" },
        ["I3a"] = new[]{ "Law Code", "Topological Governance Standard", "Command-Structure Codification", "Colonial Governance Codification", "Atmospheric Regulation Codification", "Propagation Codification", "Pack-Law Codification" },
        ["I3b"] = new[]{ "Religious Pluralism", null, null, null, null, null, null },
        ["I3c"] = new[]{ "Diplomatic Protocol", "Formal Graft-Treaty Norms", "Inter-Colony Pact Norms", "Inter-Colonial Pact Norms", "Cross-Biome Exchange Protocol", "Inter-Bloom Exchange Protocol", "Inter-Pack Treaty Protocol" },
        ["I3d"] = new[]{ "Currency", "Standardized Exchange-Compound Value", "Standardized Biomass Value", "Standardized Resource-Share Value", "Standardized Atmospheric-Yield Value", "Standardized Bloom-Yield Value", "Standardized Territory-Share Value" },
        // era3-systems-implementation-spec §5: new node, Informational-tagged — I3b's admin-efficiency
        // payoff plan didn't hold (confirmed Existential, not Informational). Names mirror I2b's own
        // per-track vocabulary one tier up ("Standardization" → "Routing Efficiency").
        ["I3e"] = new[]{ "Bureaucratic Streamlining", "Network Routing Optimization", "Caste-Efficient Administration", "Reef Administrative Streamlining", "Chemical Signal Routing Efficiency", "Bloom-Signal Routing Efficiency", "Territorial Signal Routing Efficiency" },
        ["I4a"] = new[]{ "Missionary Doctrine", null, null, null, null, null, null },
        ["I4b"] = new[]{ "Federalism", "Mesh-Sovereignty Doctrine", "Multi-Colony Sovereignty Doctrine", "Reef/Basin Sovereignty Doctrine", "Planetary Zone Sovereignty Doctrine", "Basin-Wide Bloom Sovereignty Doctrine", "Range-Wide Sovereignty Doctrine" },
        // I4c stays structurally CommerceEngine/ApexPredator-only (see IsApplicable) — Terraformer/
        // BloomFront are "unreachable" per spec §3a and use the Adaptation-tree A4c equivalent instead.
        ["I4c"] = new[]{ "Mass Mobilization", "Network-Wide Mobilization Doctrine", "Colony-Wide Mobilization Doctrine", "Basin-Wide Mobilization Doctrine", null, null, "Continental Mobilization Doctrine" },
        // era3-sovereignty-interaction-gaps-spec.md §2: Apex Predator only — name carried over from
        // its own former Adaptation-tree name (A_host_guest_tolerance) verbatim.
        ["I_host_guest_tolerance"] = new[]{ null, null, null, null, null, null, "Host-Acceptance Behavior" },
    };

    private static int TechTrackIndex(CivilizationState civ) => civ.Path switch
    {
        Era3Path.CommerceEngine => civ.Architecture switch
        {
            CognitiveArchitecture.Individuated => 0,
            CognitiveArchitecture.Distributed  => 1,
            CognitiveArchitecture.Collective   => 2,
            _ => 0,
        },
        Era3Path.LivingReef => 3,
        Era3Path.Terraformer   => 4,
        Era3Path.BloomFront    => 5,
        Era3Path.ApexPredator  => 6,
        _ => 0,
    };

    /// Display name for a node, track-appropriate for the given civ. Falls back to the generic id
    /// for N/A cells (shouldn't normally be reached — IsApplicable filters those out first).
    public static string GetNodeName(string nodeId, CivilizationState civ)
    {
        var n = Get(nodeId);
        if (n.IsIdea)
        {
            if (!IdeaNames.TryGetValue(nodeId, out var arr)) return nodeId;
            // era3-systems-implementation-spec §3: Idea tree now spans all seven track/architecture
            // combos exactly like Tech does — reuses the same track-index mapping.
            int idx = TechTrackIndex(civ);
            return idx < arr.Length ? (arr[idx] ?? nodeId) : nodeId;
        }
        else
        {
            if (!TechNames.TryGetValue(nodeId, out var arr)) return nodeId;
            int idx = TechTrackIndex(civ);
            return idx < arr.Length ? (arr[idx] ?? nodeId) : nodeId;
        }
    }

    // ── Applicability (§2, §3, §7.6 rule 3; tree spans revised by era3-systems-implementation-spec §3) ──
    public static bool IsApplicable(CivilizationState civ, Node n)
    {
        // Idea tree: now open to all five tracks, track-flavored per node (§3) — Living Reef/
        // Terraformer/BloomFront traded away Tech entirely for full (not thin-slice) Idea access,
        // matching Commerce Engine/Apex Predator's own Idea access one-for-one. Tracks are assigned
        // once at the Era 2→3 transition and never change (era3-civilization-tracks-spec §5).
        if (n.IsIdea)
        {
            // I4c "shares T4b's structural requirement" (§5) — Commerce Engine/Apex Predator only.
            // Living Reef/Terraformer/BloomFront get the Adaptation-tree A4c equivalent instead
            // (§3b) rather than an auto-satisfied/bypassed prereq.
            if (n.Id == "I4c") return civ.Path == Era3Path.CommerceEngine || civ.Path == Era3Path.ApexPredator;
            // Two builder-vs-adapter split gates: Living Reef/Terraformer/BloomFront use the
            // Adaptation-tree node instead (A_domestication / A_host_guest_tolerance) — without this
            // exclusion they'd also be able to reach the Idea-tree version, a redundant second path
            // with no flavor name (raw id would show in the tree).
            if (n.Id == "I_domestication" && civ.Path != Era3Path.CommerceEngine && civ.Path != Era3Path.ApexPredator) return false;
            if (n.Id == "I_host_guest_tolerance" && civ.Path != Era3Path.ApexPredator) return false;
            return true;
        }

        // Tech tree: Commerce Engine + Apex Predator only now — Living Reef/Terraformer/BloomFront
        // retired it entirely in favor of full Idea + Adaptation access (§3).
        if (civ.Path != Era3Path.CommerceEngine && civ.Path != Era3Path.ApexPredator) return false;
        if (n.Id == "T3d" && civ.Path != Era3Path.CommerceEngine) return false; // no flavor name for Apex Predator either (TechNames row is null) — stays Commerce-Engine-only
        if (n.Id == "T4b" && civ.BuiltStructures.Count == 0) return false; // "Structures ≥ threshold" (§2.1), approximated as "has built at least one"
        return true;
    }

    /// appearance-generation-spec §4.8: "Tech tier does double duty: same table drives the
    /// densification ceiling (settlement-growth-spec.md) so one derivation feeds two consumers."
    /// No prior tech-tier concept existed anywhere in the codebase — derived here from the highest
    /// Tier of any Tech (T-prefixed) node this civ has actually unlocked, since that's the one
    /// already-real per-civ technological-advancement signal available. Idea (I-prefixed) nodes
    /// don't count — this is about physical/material advancement, matching §4.8's own material-
    /// palette framing, not institutional advancement. 0-4, matching the spec's five-row table
    /// (Pre-agrarian/Agrarian/Pre-industrial urban/Industrial/Post-industrial).
    public static int GetTechTier(CivilizationState civ)
    {
        int highest = 0;
        foreach (var id in civ.UnlockedNodes)
        {
            if (!TryGet(id, out var n) || n.IsIdea) continue;
            if (n.Tier > highest) highest = n.Tier;
        }
        return Mathf.Clamp(highest, 0, 4);
    }

    public static bool PrereqsUnlocked(CivilizationState civ, Node n)
    {
        // era3-systems-implementation-spec §3: the old "Living Reef thin Idea slice" bypass this
        // function used to need is gone now that Idea is fully open to every track (no more
        // Coercive-only carve-out) — every call site already checks IsApplicable(n) before reaching
        // here (Era3HUD's tree builders, Era3Manager.TryUnlockNode, AcquisitionRate above), and I4c's
        // one remaining track-restricted prereq (T4b) is handled by I4c itself being inapplicable to
        // the tracks that can't reach T4b, not by bypassing the prereq check.
        foreach (var p in n.Prereqs) if (!civ.UnlockedNodes.Contains(p)) return false;
        return true;
    }

    // ── §7.1 acquisition formula ──────────────────────────────────────────────────────────────
    private const float GammaR = 0.7f;
    private const float NMax = 4f;
    private const float WDiv = 0.3f;
    public const float KDiffusionResearch = 0.15f;
    public const float PatronageBonus = 1.5f;
    public const int   PatronageDurationTicks = 10;
    private static readonly float[] ResearchCostByTier = { 0f, 8f, 20f, 50f, 120f };

    public static float ResearchCost(int tier) => ResearchCostByTier[Mathf.Clamp(tier, 1, 4)];

    /// Raw per-channel dial, degraded by AdminOverstretchFactor — every consumer of channel
    /// investment (Tech/Idea/Adaptation acquisition, the only call sites) funnels through here, so
    /// the overstretch penalty applies uniformly without each tree needing to apply it separately.
    public static float ChannelDialValue(CivilizationState civ, EraChannel ch) => RawChannelDialValue(civ, ch) * AdminOverstretchFactor(civ);

    public static float RawChannelDialValue(CivilizationState civ, EraChannel ch) => ch switch
    {
        EraChannel.Economic      => civ.InvestEconomic,
        EraChannel.Biological    => civ.InvestBiological,
        EraChannel.Informational => civ.InvestInformation,
        EraChannel.Existential   => civ.InvestReligion,
        EraChannel.Coercive      => civ.InvestCoercive,
        _ => 0f,
    };

    /// era3-systems-implementation-spec §1: AdminReach aggregate bandwidth cap — sum all five channel
    /// dials and compare against AdminReach (I3e raises the effective ceiling 25%, §5's admin-
    /// efficiency payoff). Exceeding it degrades every channel's *effectiveness* (what research/
    /// adaptation output the dial buys), not its funding (the dial value itself, or the direct §1
    /// cost model in Era3Manager, are both untouched by this) — an overstretch curve, not a hard wall.
    public static float AdminOverstretchFactor(CivilizationState civ)
    {
        float sum = civ.InvestEconomic + civ.InvestBiological + civ.InvestInformation + civ.InvestReligion + civ.InvestCoercive;
        float ceiling = civ.AdministrativeReach * (civ.UnlockedNodes.Contains("I3e") ? 1.25f : 1f);
        return 1f / (1f + Mathf.Max(0f, sum - ceiling));
    }

    /// II(civ) / II_reference — reuses the Era 2 Intelligence Index directly. II_reference=50 is
    /// the same mid-value baseline BuildCivFromCommunity already treats as the default/neutral II.
    public static float IntelligenceFactor(CivilizationState civ)
    {
        float ii = Era2Manager.Instance?.GetRecord(civ.CommunityId)?.II ?? 50f;
        return Mathf.Max(0.05f, ii / 50f);
    }

    /// capability(civ, Informational) — reuses the existing structure-capability formula
    /// (Era3Manager.Capability, formulae spec §1.1) directly; channel index 2 = Informational.
    /// StructureInvest starts at 0 for every civ and only builds up over many trade ticks
    /// (Era3Manager.BuildRate), so at Era 3's start this floored to 0.05 and, raised to the Idea
    /// tree's w_c=1.0, crushed acquisition_rate to near-zero for the first many minutes regardless
    /// of dial investment — reading as "the tech/idea system doesn't do anything." 0.3 keeps the
    /// factor meaningfully sub-1 (structures still matter) without gating research on a slow-burn
    /// stat that has nothing to do with "is the player actually investing in this channel."
    public static float CultureFactor(CivilizationState civ) => Mathf.Max(0.3f, Era3Manager.Capability(civ, 2));

    /// Neutral Variation/Conformity reframing of the EXISTING governance/topology dial (§7.2) —
    /// no new stat, just relabeling what each architecture's low/high end means.
    public static float StructuralVariation(CivilizationState civ) => civ.Architecture switch
    {
        CognitiveArchitecture.Individuated => civ.DomesticOpenness,           // controlled(0)=conformity ↔ open(1)=variation
        CognitiveArchitecture.Distributed  => 1f - civ.NetworkTopologySlider, // hub(1)=conformity ↔ mesh(0)=variation
        CognitiveArchitecture.Collective   => 1f - civ.CommandCentralization, // single-queen(1)=conformity ↔ nest-cluster(0)=variation
        _ => 0.5f,
    };

    /// Standard normalized Shannon diversity over the roster, base-e per spec §7.2's own formula
    /// and normalized against the fixed TUNABLE N_max=4 (NOT the roster's actual count — that's a
    /// different normalization, kept separate from Era3Polity.RosterShannonDiversity's own log2/
    /// roster-count normalization, which serves the Polity tab's general-purpose diversity readout).
    public static float RosterDiversityForVariation(CivilizationState civ)
    {
        if (civ.Roster.Count <= 1) return 0f;
        float h = 0f;
        foreach (var e in civ.Roster) if (e.Fraction > 0f) h -= e.Fraction * Mathf.Log(e.Fraction);
        return Mathf.Clamp01(h / Mathf.Log(NMax));
    }

    public static float VariationScore(CivilizationState civ)
    {
        float score = (1f - WDiv) * Mathf.Clamp01(StructuralVariation(civ)) + WDiv * RosterDiversityForVariation(civ);
        // era3-warfare-mechanics-spec §4: war suppresses variation — armies conform toward
        // exploitation of known tactics rather than broad recombinant search.
        score = Mathf.Clamp01(score - civ.WarVariationSuppression);
        // era3-policy-catalog-spec §1.2: active policies are a derived multiplicative product on
        // top of the structural/roster base — recomputed fresh every call, never stored as an
        // applied-then-reverted delta.
        return Mathf.Clamp01(score * Era3PolicyCatalog.GetVar(civ, Era3PolicyCatalog.Var.VariationScore));
    }

    public static float VariationFactor(CivilizationState civ, Node n)
        => Mathf.Max(0.05f, 1f + n.VariationSensitivity * (VariationScore(civ) - 0.5f) * 2f);

    private static (float wi, float wc) TreeWeights(bool isIdea) => isIdea ? (0.6f, 1.0f) : (1.0f, 0.5f);

    public static float DiffusionBonus(Era3Manager mgr, CivilizationState civ, string nodeId)
    {
        float bonus = 0f;
        foreach (var other in mgr.AllCivsView)
        {
            if (other == civ || !other.UnlockedNodes.Contains(nodeId)) continue;
            bonus += mgr.ConnectionStrength(civ, other) * KDiffusionResearch;
        }
        return bonus * Era3PolicyCatalog.GetVar(civ, Era3PolicyCatalog.Var.DiffusionBonus);
    }

    public static float AcquisitionRate(Era3Manager mgr, CivilizationState civ, Node n)
    {
        if (!IsApplicable(civ, n) || civ.UnlockedNodes.Contains(n.Id)) return 0f;
        if (!PrereqsUnlocked(civ, n)) return 0f; // §7.6 rule 1: zero accrual until prereqs clear

        float dial = ChannelDialValue(civ, n.Channel);
        float baseRate = 0f;
        if (dial > 0f) // no investment in this channel ⇒ no research in it, intended per §7.1
        {
            var (wi, wc) = TreeWeights(n.IsIdea);
            float patronage = (civ.PatronageNodeId == n.Id) ? PatronageBonus : 1f;
            // era3-policy-catalog-spec: e.g. Guild Monopoly/Scribal Bureaucracy raise Tech
            // acquisition, Command Economy suppresses Idea acquisition, etc.
            float policyMult = Era3PolicyCatalog.GetVar(civ,
                n.IsIdea ? Era3PolicyCatalog.Var.IdeaAcquisition : Era3PolicyCatalog.Var.TechAcquisition);
            baseRate = Mathf.Pow(dial, GammaR)
                     * Mathf.Pow(IntelligenceFactor(civ), wi)
                     * Mathf.Pow(CultureFactor(civ), wc)
                     * VariationFactor(civ, n)
                     * patronage
                     * policyMult;
        }
        return Mathf.Max(0f, baseRate + DiffusionBonus(mgr, civ, n.Id));
    }
}
