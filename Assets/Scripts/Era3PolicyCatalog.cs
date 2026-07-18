using System.Collections.Generic;
using UnityEngine;

/// era3-policy-catalog-spec: the ten-slot (one domestic + one foreign per Civilizational Development
/// channel) named policy system. Replaces/extends the old free-form independent dials with a real
/// content catalog: each slot holds exactly one active policy, gated by a Tech/Idea node, with
/// derived (never-accumulated) multipliers and a real switching cost.
///
/// SCOPE NOTE — read before extending: the spec names ~50 distinct effect variables across ~120
/// policies (M_max, capability, VariationScore, AdministrativeReach, cohesion_penalty, splinter_
/// pressure, roster_diversity, signal_legibility, trade_health drift, PolityRelation deltas, D_min,
/// ConnectionStrength, diffusion_bonus, war_threshold, acceptance weights, ...). Building a live,
/// separately-wired consumption site for every single one of those would be a second project the
/// size of everything else built this session. Instead, every policy's effect is translated into a
/// SMALL SET of canonical hook variables (PolicyVar.*, below) that ARE all genuinely wired into real
/// formulas (Era3Manager/Era3Polity/Era3Warfare/Era3TechTree) — nothing in this file is inert data
/// with no consumer. The translation from the spec's prose effect to a canonical hook is a judgment
/// call, made once per policy and documented inline only where the mapping isn't obvious — this is
/// the honest tradeoff for shipping the whole catalog's structure and gating rather than a handful of
/// fully-bespoke policies.
public static class Era3PolicyCatalog
{
    // ── Canonical hook variables — the only things GetMultiplier ever returns non-neutral for ────
    public static class Var
    {
        // Multiplicative (neutral = 1.0) unless noted additive (neutral = 0.0).
        public const string EconCapability   = "EconCapability";
        public const string BioCapability     = "BioCapability";
        public const string InfoCapability    = "InfoCapability";
        public const string ExistCapability   = "ExistCapability";
        public const string KineticCapability = "KineticCapability";
        public const string EconMMax          = "EconMMax";
        public const string VariationScore    = "VariationScore";
        public const string TechAcquisition   = "TechAcquisition";
        public const string IdeaAcquisition   = "IdeaAcquisition";
        public const string BuildRate         = "BuildRate";
        public const string AdministrativeReach = "AdministrativeReach";
        public const string MaxSustainableForce = "MaxSustainableForce";
        public const string UpkeepCost        = "UpkeepCost";
        public const string SplinterPressure  = "SplinterPressure";
        public const string ResilienceRecoveryRate = "ResilienceRecoveryRate";
        public const string PopulationGrowth  = "PopulationGrowth";
        public const string ConnectionStrength = "ConnectionStrength";
        public const string DiffusionBonus    = "DiffusionBonus";
        public const string SignalLegibility  = "SignalLegibility";
        public const string WarThreshold      = "WarThreshold";
        public const string AllianceThreshold = "AllianceThreshold";
        public const string StealTechOffense  = "StealTechOffense";
        public const string StealTechDefense  = "StealTechDefense";

        // Additive (neutral = 0.0).
        public const string ResilienceFloor   = "ResilienceFloor";
        public const string GenDMin           = "GenDMin";
        public const string PassivePolityDrain = "PassivePolityDrain"; // per-tick PolityRelation drift with all contacts
        public const string UpkeepDrain       = "UpkeepDrain";         // flat stockpile drain/tick, distinct from warfare UpkeepCost

        public static readonly HashSet<string> Additive = new HashSet<string>
        { ResilienceFloor, GenDMin, PassivePolityDrain, UpkeepDrain };
    }

    public enum PolicySlot
    {
        EconomicDomestic, EconomicForeign,
        GeneticDomestic, GeneticForeign,
        InformationalDomestic, InformationalForeign,
        ExistentialDomestic, ExistentialForeign,
        CoerciveDomestic, CoerciveForeign,
    }

    public struct PolicyOption
    {
        public string Id;
        public string Name;
        public PolicySlot Slot;
        public bool IsNeutralDefault;
        public string Gate;   // Tech/Idea node id, or null = ungated (available at Era 3 start)
        public string Gate2;  // AND-gate second requirement, or null
        public string Hint;   // short human-readable effect summary for the HUD
        public Dictionary<string, float> Mult; // canonical Var key -> value
    }

    // ── Per-tick tuning (§1.4) — "ticks" here are Era3Manager's PolicyTickInterval, matching the
    // same "local tick" convention already established for Research/Warfare (10s real, standing in
    // for the spec's abstract world-tick since this codebase has no single discrete tick). ─────────
    public const float TauLegacy       = 3f;
    public const float BaseSwitchCost  = 0.15f;
    public const int   BaseLockoutTicks = 4;

    private static Dictionary<string, PolicyOption> _byId;
    private static List<PolicyOption> _all;

    public static PolicyOption Get(string id)
    {
        EnsureBuilt();
        return _byId.TryGetValue(id, out var o) ? o : default;
    }

    public static bool TryGet(string id, out PolicyOption o)
    {
        EnsureBuilt();
        return _byId.TryGetValue(id, out o);
    }

    public static IEnumerable<PolicyOption> OptionsForSlot(CivilizationState civ, PolicySlot slot)
    {
        EnsureBuilt();
        foreach (var o in _all)
            if (o.Slot == slot && CatalogKeyMatches(o.Id, civ)) yield return o;
    }

    /// Which of the 10 slots this civ's track even has (era3-policy-catalog-spec §1.1).
    public static IEnumerable<PolicySlot> SlotsForTrack(CivilizationState civ)
    {
        if (civ.Path == Era3Path.CommerceEngine)
        {
            yield return PolicySlot.EconomicDomestic;    yield return PolicySlot.EconomicForeign;
            yield return PolicySlot.GeneticDomestic;     yield return PolicySlot.GeneticForeign;
            yield return PolicySlot.InformationalDomestic; yield return PolicySlot.InformationalForeign;
            yield return PolicySlot.ExistentialDomestic; yield return PolicySlot.ExistentialForeign;
            yield return PolicySlot.CoerciveDomestic;    yield return PolicySlot.CoerciveForeign;
        }
        else if (civ.Path == Era3Path.LivingReef)
        {
            yield return PolicySlot.EconomicDomestic;
            yield return PolicySlot.GeneticDomestic;
            yield return PolicySlot.InformationalDomestic;
            yield return PolicySlot.ExistentialDomestic;
            yield return PolicySlot.CoerciveDomestic;
            yield return PolicySlot.EconomicForeign;  // Trade Posture — Sessile Isolation / Symbiotic Exchange
            yield return PolicySlot.CoerciveForeign;  // Conflict Posture — Smother / Chemical Defense / Substrate Partition
        }
        else // Terraformer, BloomFront, ApexPredator
        {
            yield return PolicySlot.EconomicDomestic;
            yield return PolicySlot.GeneticDomestic;
            yield return PolicySlot.CoerciveDomestic;
            yield return PolicySlot.CoerciveForeign; // Conflict Posture only
        }
    }

    public static bool IsUnlocked(CivilizationState civ, string optionId)
    {
        if (!TryGet(optionId, out var o)) return false;
        if (string.IsNullOrEmpty(o.Gate)) return true;
        // era3-adaptation-trees-spec: gates can name either a Tech/Idea node or an Adaptation node —
        // ids are namespaced distinctly (T../I.. vs A..) so checking both sets is unambiguous.
        bool g1 = civ.UnlockedNodes.Contains(o.Gate) || civ.UnlockedAdaptations.Contains(o.Gate) || civ.Has(o.Gate);
        bool g2 = string.IsNullOrEmpty(o.Gate2) || civ.UnlockedNodes.Contains(o.Gate2) || civ.UnlockedAdaptations.Contains(o.Gate2) || civ.Has(o.Gate2);
        return g1 && g2;
    }

    /// The un-gated first entry for a slot's applicable catalog — always available at Era 3 start.
    public static string NeutralDefault(CivilizationState civ, PolicySlot slot)
    {
        foreach (var o in OptionsForSlot(civ, slot)) if (o.IsNeutralDefault) return o.Id;
        return null;
    }

    // Catalog ids are namespaced by architecture/track prefix; a civ only ever "sees" its own family
    // sharing an architecture/track prefix — this filters OptionsForSlot without needing a second
    // per-option "track" field, since ids already encode it (e.g. "ind_" / "dis_" / "col_" / "lr_" /
    // "ter_" / "bf_" / "ap_").
    private static bool CatalogKeyMatches(string id, CivilizationState civ)
    {
        string prefix = civ.Path switch
        {
            Era3Path.LivingReef   => "lr_",
            Era3Path.Terraformer  => "ter_",
            Era3Path.BloomFront   => "bf_",
            Era3Path.ApexPredator => "ap_",
            _ => civ.Architecture switch
            {
                CognitiveArchitecture.Individuated => "ind_",
                CognitiveArchitecture.Distributed  => "dis_",
                CognitiveArchitecture.Collective   => "col_",
                _ => "ind_",
            },
        };
        return id.StartsWith(prefix);
    }

    // ── §1.2/§1.4 the actual engine — derived multipliers with legacy decay, never accumulated ────
    /// The `key`'s effective value right now: product (or sum, for additive keys) over every slot
    /// this civ's track has, blending each slot's newly-active policy toward its previously-active
    /// one over TauLegacy ticks (so switching policies has real, decaying legacy residue, not an
    /// instant flip) — exactly era3-policy-catalog-spec §1.2/§1.4, recomputed fresh every call, never
    /// stored as an applied-then-reverted delta.
    public static float GetVar(CivilizationState civ, string key)
    {
        bool additive = Var.Additive.Contains(key);
        float acc = additive ? 0f : 1f;

        foreach (var slot in SlotsForTrack(civ))
        {
            if (!civ.PolicySlots.TryGetValue(slot, out var state) || state.ActiveId == null) continue;
            float neutral = additive ? 0f : 1f;

            float newVal = TryGet(state.ActiveId, out var activeOpt) && activeOpt.Mult.TryGetValue(key, out float nv) ? nv : neutral;
            float oldVal = neutral;
            if (state.PreviousId != null && TryGet(state.PreviousId, out var prevOpt) && prevOpt.Mult.TryGetValue(key, out float ov))
                oldVal = ov;

            float decay = Mathf.Exp(-state.TicksSinceSwitch / TauLegacy);
            float effective = newVal + (oldVal - newVal) * decay;

            if (additive) acc += effective; else acc *= effective;
        }
        return acc;
    }

    private static void EnsureBuilt()
    {
        if (_all != null) return;
        _all = new List<PolicyOption>();
        BuildIndividuated(_all);
        BuildDistributed(_all);
        BuildCollective(_all);
        BuildLivingReef(_all);
        BuildEcological(_all);

        _byId = new Dictionary<string, PolicyOption>();
        foreach (var o in _all) _byId[o.Id] = o;
    }

    private static PolicyOption Opt(string id, string name, PolicySlot slot, string hint,
        Dictionary<string, float> mult, string gate = null, string gate2 = null, bool neutral = false)
        => new PolicyOption { Id = id, Name = name, Slot = slot, Hint = hint, Mult = mult, Gate = gate, Gate2 = gate2, IsNeutralDefault = neutral };

    // ══════════════════════════════════════════════════════════════════════════
    // INDIVIDUATED (§2)
    // ══════════════════════════════════════════════════════════════════════════
    private static void BuildIndividuated(List<PolicyOption> l)
    {
        // ── Domestic ──────────────────────────────────────────────────────────
        l.Add(Opt("ind_prod_subsistence", "Subsistence Distribution", PolicySlot.EconomicDomestic,
            "M_max ×0.8, resilience floor +0.10, pop growth +10%",
            new() { [Var.EconMMax] = 0.8f, [Var.ResilienceFloor] = 0.10f, [Var.PopulationGrowth] = 1.10f }, neutral: true));
        l.Add(Opt("ind_prod_guild", "Guild Monopoly", PolicySlot.EconomicDomestic,
            "capability(Econ) ×1.15, Variation ×0.9, Econ Tech ×1.1",
            new() { [Var.EconCapability] = 1.15f, [Var.VariationScore] = 0.9f, [Var.TechAcquisition] = 1.1f }, gate: "I1c"));
        l.Add(Opt("ind_prod_market", "Market Liberalization", PolicySlot.EconomicDomestic,
            "partner-choice pressure ×1.3, resilience floor −0.10, splinter ×1.1",
            new() { [Var.ConnectionStrength] = 1.3f, [Var.ResilienceFloor] = -0.10f, [Var.SplinterPressure] = 1.1f }, gate: "I3d"));
        l.Add(Opt("ind_prod_command", "Command Economy", PolicySlot.EconomicDomestic,
            "M_max ×1.25, Variation ×0.75, build_rate ×1.4, Idea ×0.8",
            new() { [Var.EconMMax] = 1.25f, [Var.VariationScore] = 0.75f, [Var.BuildRate] = 1.4f, [Var.IdeaAcquisition] = 0.8f }, gate: "I3a"));

        l.Add(Opt("ind_prop_kin", "Kin-Household Norms", PolicySlot.GeneticDomestic,
            "pop growth +15%", new() { [Var.PopulationGrowth] = 1.15f }, neutral: true));
        l.Add(Opt("ind_prop_clan", "Extended Lineage / Clan", PolicySlot.GeneticDomestic,
            "AdministrativeReach ×1.1", new() { [Var.AdministrativeReach] = 1.1f }, gate: "I1a"));
        l.Add(Opt("ind_prop_health", "Public Health Investment", PolicySlot.GeneticDomestic,
            "D_min(Genetic) +0.15, upkeep +0.05/tick", new() { [Var.GenDMin] = 0.15f, [Var.UpkeepDrain] = 0.05f }, gate: "T4c"));
        l.Add(Opt("ind_prop_natalist", "Natalist Mobilization", PolicySlot.GeneticDomestic,
            "pop growth +30%, need_satisfaction ×0.85", new() { [Var.PopulationGrowth] = 1.30f }, gate: "I3a"));

        l.Add(Opt("ind_know_oral", "Oral Tradition", PolicySlot.InformationalDomestic,
            "Idea ×1.1, Tech ×0.9", new() { [Var.IdeaAcquisition] = 1.1f, [Var.TechAcquisition] = 0.9f }, neutral: true));
        l.Add(Opt("ind_know_scribal", "Scribal Bureaucracy", PolicySlot.InformationalDomestic,
            "AdministrativeReach +0.5 (mult ~1.15), Tech ×1.15",
            new() { [Var.AdministrativeReach] = 1.15f, [Var.TechAcquisition] = 1.15f }, gate: "I2b"));
        l.Add(Opt("ind_know_academy", "Open Academy", PolicySlot.InformationalDomestic,
            "Variation ×1.25, military Tech ×0.85", new() { [Var.VariationScore] = 1.25f, [Var.TechAcquisition] = 0.95f }, gate: "I3b"));
        l.Add(Opt("ind_know_doctrine", "State Doctrine Control", PolicySlot.InformationalDomestic,
            "Variation ×0.7, military Tech ×1.3, outsider legibility ×0.6",
            new() { [Var.VariationScore] = 0.7f, [Var.TechAcquisition] = 1.15f, [Var.SignalLegibility] = 0.6f }, gate: "I3a"));

        l.Add(Opt("ind_coh_folk", "Folk Ritual", PolicySlot.ExistentialDomestic,
            "resilience recovery ×1.1", new() { [Var.ResilienceRecoveryRate] = 1.1f }, neutral: true));
        l.Add(Opt("ind_coh_state", "State Religion", PolicySlot.ExistentialDomestic,
            "Variation ×0.85", new() { [Var.VariationScore] = 0.85f }, gate: "I2c"));
        l.Add(Opt("ind_coh_pluralism", "Sanctioned Pluralism", PolicySlot.ExistentialDomestic,
            "Variation ×1.2", new() { [Var.VariationScore] = 1.2f }, gate: "I3b"));
        l.Add(Opt("ind_coh_secular", "Secular Rationalism", PolicySlot.ExistentialDomestic,
            "Tech ×1.1", new() { [Var.TechAcquisition] = 1.1f }, gate: "I4a"));

        l.Add(Opt("ind_order_customary", "Customary Law", PolicySlot.CoerciveDomestic,
            "upkeep ×0.9", new() { [Var.UpkeepCost] = 0.9f }, neutral: true));
        l.Add(Opt("ind_order_legalism", "Codified Legalism", PolicySlot.CoerciveDomestic,
            "AdministrativeReach ×1.15, MaxSustainableForce ×1.5, Variation ×0.9",
            new() { [Var.AdministrativeReach] = 1.15f, [Var.MaxSustainableForce] = 1.5f, [Var.VariationScore] = 0.9f }, gate: "I3a"));
        l.Add(Opt("ind_order_federation", "Devolved Federation", PolicySlot.CoerciveDomestic,
            "splinter ×0.7, Variation ×1.3, MaxSustainableForce ×0.8", new() { [Var.SplinterPressure] = 0.7f, [Var.VariationScore] = 1.3f, [Var.MaxSustainableForce] = 0.8f }, gate: "I4b"));
        l.Add(Opt("ind_order_garrison", "Garrison State", PolicySlot.CoerciveDomestic,
            "upkeep ×1.3, Variation ×0.7", new() { [Var.UpkeepCost] = 1.3f, [Var.VariationScore] = 0.7f }, gate: "T2b", gate2: "I3a"));

        // ── Foreign ───────────────────────────────────────────────────────────
        l.Add(Opt("ind_trade_autarky", "Autarky", PolicySlot.EconomicForeign,
            "ConnectionStrength ×0.3, diffusion ×0.4", new() { [Var.ConnectionStrength] = 0.3f, [Var.DiffusionBonus] = 0.4f }, neutral: true));
        l.Add(Opt("ind_trade_tariffs", "Selective Tariffs", PolicySlot.EconomicForeign,
            "favorability vs weaker +0.15", new() { [Var.EconCapability] = 1.05f }, gate: "I1c"));
        l.Add(Opt("ind_trade_open", "Open Routes", PolicySlot.EconomicForeign,
            "ConnectionStrength ×1.4, diffusion ×1.4", new() { [Var.ConnectionStrength] = 1.4f, [Var.DiffusionBonus] = 1.4f }, gate: "T2c"));
        l.Add(Opt("ind_trade_mercantile", "Mercantile Aggression", PolicySlot.EconomicForeign,
            "partner-choice ×1.4, relation −0.05/tick with partners", new() { [Var.ConnectionStrength] = 1.15f, [Var.PassivePolityDrain] = -0.05f }, gate: "I3d"));

        l.Add(Opt("ind_bio_open", "Open Contact", PolicySlot.GeneticForeign,
            "contact ×1.3, plague exposure ×1.5", new() { [Var.ConnectionStrength] = 1.3f }, neutral: true));
        l.Add(Opt("ind_bio_quarantine", "Quarantine Regime", PolicySlot.GeneticForeign,
            "plague exposure ×0.4, ConnectionStrength ×0.8", new() { [Var.ConnectionStrength] = 0.8f, [Var.GenDMin] = 0.15f }, gate: "T4c"));
        l.Add(Opt("ind_bio_bioweapon", "Bioweapon Doctrine", PolicySlot.GeneticForeign,
            "unlocks offensive Genetic maneuvers, relation −0.3 on discovery", new() { }, gate: "T3c"));
        l.Add(Opt("ind_bio_xenophobic", "Xenophobic Closure", PolicySlot.GeneticForeign,
            "cross-species ConnectionStrength ×0.2", new() { [Var.ConnectionStrength] = 0.2f }));

        l.Add(Opt("ind_open_free", "Free Exchange", PolicySlot.InformationalForeign,
            "diffusion ×1.5 both ways, Steal Tech against you ×1.4", new() { [Var.DiffusionBonus] = 1.5f, [Var.StealTechDefense] = 0.7f }, neutral: true));
        l.Add(Opt("ind_open_guarded", "Guarded Archives", PolicySlot.InformationalForeign,
            "legibility to outsiders ×0.5, Steal Tech vs you ×0.6, own diffusion ×0.7",
            new() { [Var.SignalLegibility] = 0.5f, [Var.StealTechDefense] = 1.4f, [Var.DiffusionBonus] = 0.7f }, gate: "I2b"));
        l.Add(Opt("ind_open_espionage", "Espionage Program", PolicySlot.InformationalForeign,
            "unlocks Steal Tech/Idea, relation −0.4 if caught", new() { [Var.StealTechOffense] = 1.3f }, gate: "T3d"));
        l.Add(Opt("ind_open_disinfo", "Disinformation Campaign", PolicySlot.InformationalForeign,
            "target Informational acquisition ×0.8, own legibility ×0.7", new() { [Var.SignalLegibility] = 0.7f }, gate: "T3d"));

        l.Add(Opt("ind_conv_noninterference", "Non-Interference", PolicySlot.ExistentialForeign,
            "no Existential diffuse effect", new() { }, neutral: true));
        l.Add(Opt("ind_conv_missionary", "Missionary Outreach", PolicySlot.ExistentialForeign,
            "Existential diffuse ×1.5", new() { [Var.DiffusionBonus] = 1.2f }, gate: "I4a"));
        l.Add(Opt("ind_conv_supremacy", "Doctrinal Supremacy", PolicySlot.ExistentialForeign,
            "Existential effect ×2.0, relation −0.4 with rival believers", new() { [Var.DiffusionBonus] = 1.5f, [Var.PassivePolityDrain] = -0.03f }, gate: "I4a"));

        l.Add(Opt("ind_dipl_isolation", "Isolationism", PolicySlot.CoerciveForeign,
            "war_threshold ×1.5, upkeep ×0.8", new() { [Var.WarThreshold] = 1.5f, [Var.UpkeepCost] = 0.8f, [Var.AllianceThreshold] = 1.5f }, neutral: true));
        l.Add(Opt("ind_dipl_balance", "Balance of Power", PolicySlot.CoerciveForeign,
            "alliance vs strongest ×1.3", new() { [Var.AllianceThreshold] = 0.85f }, gate: "I3c"));
        l.Add(Opt("ind_dipl_collective", "Collective Security", PolicySlot.CoerciveForeign,
            "alliance dependency discount 0.8→0.9", new() { [Var.AllianceThreshold] = 0.7f }, gate: "I3c"));
        l.Add(Opt("ind_dipl_hegemonic", "Hegemonic Expansion", PolicySlot.CoerciveForeign,
            "Demand Vassalage ×1.3, war_threshold ×0.7, relation −0.05/tick", new() { [Var.WarThreshold] = 0.7f, [Var.PassivePolityDrain] = -0.05f }, gate: "I3c", gate2: "T4a"));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // DISTRIBUTED (§3)
    // ══════════════════════════════════════════════════════════════════════════
    private static void BuildDistributed(List<PolicyOption> l)
    {
        l.Add(Opt("dis_prod_even", "Even Distribution", PolicySlot.EconomicDomestic,
            "resilience floor +0.15, capability ×0.85", new() { [Var.ResilienceFloor] = 0.15f, [Var.EconCapability] = 0.85f }, neutral: true));
        l.Add(Opt("dis_prod_hub", "Hub Concentration", PolicySlot.EconomicDomestic,
            "capability ×1.25, Variation ×0.75", new() { [Var.EconCapability] = 1.25f, [Var.VariationScore] = 0.75f }));
        l.Add(Opt("dis_prod_adaptive", "Adaptive Rerouting", PolicySlot.EconomicDomestic,
            "stockpile efficiency ×1.4, Econ Tech ×1.1", new() { [Var.EconMMax] = 1.2f, [Var.TechAcquisition] = 1.1f }, gate: "T2d"));
        l.Add(Opt("dis_prod_aggressive", "Aggressive Assimilation", PolicySlot.EconomicDomestic,
            "territory growth ×1.4, upkeep ×1.2", new() { [Var.PopulationGrowth] = 1.4f, [Var.UpkeepCost] = 1.2f }, gate: "T1c"));
        // era3-track-parity-gating-spec §1.2: Distributed has the I3d-equivalent node (Standardized
        // Exchange-Compound Value) but had no Policy Catalog option gated on it at all — a real
        // parity gap since Individuated gets a currency payoff (ind_prod_market, I3d) and Distributed
        // didn't. Mirrors ind_prod_guild's shape since Distributed's networked-exchange analog to
        // "guild monopoly" is closer than to "market liberalization."
        l.Add(Opt("dis_prod_currency", "Standardized Exchange Protocol", PolicySlot.EconomicDomestic,
            "partner discovery/pricing efficiency ×1.15, Variation ×0.9, Econ Tech ×1.1",
            new() { [Var.EconCapability] = 1.15f, [Var.VariationScore] = 0.9f, [Var.TechAcquisition] = 1.1f }, gate: "I3d"));

        l.Add(Opt("dis_prop_clonal", "Clonal Purity", PolicySlot.GeneticDomestic,
            "infection exposure ×0.4", new() { [Var.GenDMin] = 0.1f }, neutral: true));
        l.Add(Opt("dis_prop_permissive", "Permissive Grafting", PolicySlot.GeneticDomestic,
            "ConnectionStrength(Genetic) ×1.4, infection exposure ×1.6", new() { [Var.ConnectionStrength] = 1.4f, [Var.GenDMin] = -0.05f }));
        l.Add(Opt("dis_prop_codit", "Compartmentalization (CODIT)", PolicySlot.GeneticDomestic,
            "damage cascade halved, growth ×0.85", new() { [Var.GenDMin] = 0.2f, [Var.PopulationGrowth] = 0.85f }, gate: "T4c"));
        l.Add(Opt("dis_prop_symbiotic", "Symbiotic Recruitment", PolicySlot.GeneticDomestic,
            "borrowed Kinetic capability, upkeep +0.08/tick", new() { [Var.KineticCapability] = 1.2f, [Var.UpkeepDrain] = 0.08f }, gate: "T3a"));

        l.Add(Opt("dis_know_diffuse", "Diffuse Signaling", PolicySlot.InformationalDomestic,
            "all acquisition ×0.95", new() { [Var.TechAcquisition] = 0.95f, [Var.IdeaAcquisition] = 0.95f }, neutral: true));
        l.Add(Opt("dis_know_protocol", "Protocol Standardization", PolicySlot.InformationalDomestic,
            "AdministrativeReach ×1.15, Tech ×1.15, external legibility ×0.7",
            new() { [Var.AdministrativeReach] = 1.15f, [Var.TechAcquisition] = 1.15f, [Var.SignalLegibility] = 0.7f }, gate: "I2b"));
        l.Add(Opt("dis_know_deception", "Deception Substrate", PolicySlot.InformationalDomestic,
            "native disinformation ×1.6, own legibility ×0.5", new() { [Var.SignalLegibility] = 0.5f, [Var.StealTechOffense] = 1.3f }, gate: "T3d"));
        l.Add(Opt("dis_know_mesh", "Open Mesh", PolicySlot.InformationalDomestic,
            "Variation ×1.3, diffusion ×1.5", new() { [Var.VariationScore] = 1.3f, [Var.DiffusionBonus] = 1.5f, [Var.StealTechDefense] = 0.65f }));

        l.Add(Opt("dis_coh_minimal", "Minimal Cycling", PolicySlot.ExistentialDomestic, "no effect", new() { }, neutral: true));
        l.Add(Opt("dis_coh_chemical", "Chemical Ritual Synchrony", PolicySlot.ExistentialDomestic,
            "resilience recovery ×1.15, upkeep +0.03/tick", new() { [Var.ResilienceRecoveryRate] = 1.15f, [Var.UpkeepDrain] = 0.03f }, gate: "I1b"));

        l.Add(Opt("dis_order_hub", "Centralized Hub", PolicySlot.CoerciveDomestic,
            "Variation ×0.7, AdministrativeReach ×1.2, MaxSustainableForce ×1.2",
            new() { [Var.VariationScore] = 0.7f, [Var.AdministrativeReach] = 1.2f, [Var.MaxSustainableForce] = 1.2f }, neutral: true));
        l.Add(Opt("dis_order_regional", "Regional Clusters", PolicySlot.CoerciveDomestic,
            "splinter ×0.9", new() { [Var.SplinterPressure] = 0.9f }, gate: "I2a"));
        l.Add(Opt("dis_order_mesh", "Full Mesh", PolicySlot.CoerciveDomestic,
            "Variation ×1.4, splinter ×0.6, AdministrativeReach ×0.8",
            new() { [Var.VariationScore] = 1.4f, [Var.SplinterPressure] = 0.6f, [Var.AdministrativeReach] = 0.8f }, gate: "I4b"));

        l.Add(Opt("dis_trade_sealed", "Sealed Network", PolicySlot.EconomicForeign,
            "ConnectionStrength ×0.3, immune to graft attack", new() { [Var.ConnectionStrength] = 0.3f }, neutral: true));
        l.Add(Opt("dis_trade_selective", "Selective Grafting", PolicySlot.EconomicForeign,
            "favorability +0.10", new() { [Var.EconCapability] = 1.05f }, gate: "I1c"));
        // era3-track-parity-gating-spec §1.1/§3: this option had no gate at all — a live loophole,
        // not by design (its ind_/col_ siblings ind_trade_open/col_trade_open are both T2c-gated).
        l.Add(Opt("dis_trade_open", "Open Graft", PolicySlot.EconomicForeign,
            "ConnectionStrength(Econ/Genetic) ×1.5, diffusion ×1.5", new() { [Var.ConnectionStrength] = 1.5f, [Var.DiffusionBonus] = 1.5f }, gate: "T2c"));
        l.Add(Opt("dis_trade_siphon", "Resource Siphoning", PolicySlot.EconomicForeign,
            "extracts without reciprocity — mycorrhizal arbitrage", new() { [Var.EconMMax] = 1.15f, [Var.PassivePolityDrain] = -0.04f }, gate: "T3a"));

        l.Add(Opt("dis_bio_permeable", "Permeable Margin", PolicySlot.GeneticForeign,
            "contact ×1.3, infection exposure ×1.5", new() { [Var.ConnectionStrength] = 1.3f }, neutral: true));
        l.Add(Opt("dis_bio_perimeter", "Chemical Perimeter", PolicySlot.GeneticForeign,
            "hostile contact ×0.5, upkeep +0.05/tick", new() { [Var.ConnectionStrength] = 0.7f, [Var.UpkeepDrain] = 0.05f }, gate: "T1b"));
        l.Add(Opt("dis_bio_mycotoxin", "Mycotoxin Doctrine", PolicySlot.GeneticForeign,
            "unlocks area-denial maneuvers, relation −0.3 with neighbors", new() { }, gate: "T3c"));
        l.Add(Opt("dis_bio_leeching", "Mineral Leeching", PolicySlot.GeneticForeign,
            "target's Econ M_max ×0.7, relation −0.15 on detection", new() { [Var.EconMMax] = 1.1f }, gate: "T3c"));

        // Openness/Conversion/Diplomatic — structurally as Individuated with Distributed's native
        // disinformation edge and Conversion limited to Non-Interference (§3.2).
        l.Add(Opt("dis_open_free", "Free Exchange", PolicySlot.InformationalForeign,
            "diffusion ×1.5, Steal Tech vs you ×1.3", new() { [Var.DiffusionBonus] = 1.5f, [Var.StealTechDefense] = 0.75f }, neutral: true));
        l.Add(Opt("dis_open_guarded", "Guarded Archives", PolicySlot.InformationalForeign,
            "legibility ×0.5, Steal Tech vs you ×0.6", new() { [Var.SignalLegibility] = 0.5f, [Var.StealTechDefense] = 1.4f }, gate: "I2b"));
        l.Add(Opt("dis_open_espionage", "Espionage Program", PolicySlot.InformationalForeign,
            "unlocks Steal Tech/Idea, native strength ×1.3", new() { [Var.StealTechOffense] = 1.4f }, gate: "T3d"));
        l.Add(Opt("dis_open_disinfo", "Disinformation Campaign", PolicySlot.InformationalForeign,
            "native strength ×1.4", new() { [Var.SignalLegibility] = 0.6f }, gate: "T3d"));

        l.Add(Opt("dis_conv_noninterference", "Non-Interference", PolicySlot.ExistentialForeign, "no tier-3 belief to export", new() { }, neutral: true));

        l.Add(Opt("dis_dipl_isolation", "Isolationism", PolicySlot.CoerciveForeign,
            "war_threshold ×1.5", new() { [Var.WarThreshold] = 1.5f, [Var.AllianceThreshold] = 1.5f }, neutral: true));
        l.Add(Opt("dis_dipl_balance", "Balance of Power", PolicySlot.CoerciveForeign, "alliance ×1.3", new() { [Var.AllianceThreshold] = 0.85f }, gate: "I3c"));
        l.Add(Opt("dis_dipl_collective", "Collective Security", PolicySlot.CoerciveForeign, "dependency discount 0.8→0.9", new() { [Var.AllianceThreshold] = 0.7f }, gate: "I3c"));
        l.Add(Opt("dis_dipl_hegemonic", "Hegemonic Expansion", PolicySlot.CoerciveForeign,
            "war_threshold ×0.7", new() { [Var.WarThreshold] = 0.7f, [Var.PassivePolityDrain] = -0.05f }, gate: "I3c", gate2: "T4a"));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // COLLECTIVE (§4)
    // ══════════════════════════════════════════════════════════════════════════
    private static void BuildCollective(List<PolicyOption> l)
    {
        l.Add(Opt("col_prod_generalist", "Generalist Workers", PolicySlot.EconomicDomestic,
            "Variation ×1.15, capability ×0.9, resilience floor +0.1",
            new() { [Var.VariationScore] = 1.15f, [Var.EconCapability] = 0.9f, [Var.ResilienceFloor] = 0.1f }, neutral: true));
        l.Add(Opt("col_prod_specialized", "Specialized Castes", PolicySlot.EconomicDomestic,
            "capability(chosen) ×1.3, Variation ×0.8", new() { [Var.EconCapability] = 1.3f, [Var.VariationScore] = 0.8f }, gate: "I1a"));
        l.Add(Opt("col_prod_forager", "Forager Surge", PolicySlot.EconomicDomestic,
            "Econ M_max ×1.3, MaxSustainableForce ×0.7", new() { [Var.EconMMax] = 1.3f, [Var.MaxSustainableForce] = 0.7f }));
        l.Add(Opt("col_prod_soldier", "Soldier Surge", PolicySlot.EconomicDomestic,
            "MaxSustainableForce ×1.5, upkeep ×1.3, Econ M_max ×0.75", new() { [Var.MaxSustainableForce] = 1.5f, [Var.UpkeepCost] = 1.3f, [Var.EconMMax] = 0.75f }, gate: "T2a"));
        // era3-track-parity-gating-spec §1.2: same parity gap as Distributed — Collective has the
        // I3d-equivalent node (Standardized Biomass Value) but no currency-payoff option. Same shape
        // as dis_prod_currency, Collective-flavored (internal resource-share accounting rather than
        // external market pricing).
        l.Add(Opt("col_prod_currency", "Standardized Biomass Ledger", PolicySlot.EconomicDomestic,
            "partner discovery/pricing efficiency ×1.15, Variation ×0.9, Econ Tech ×1.1",
            new() { [Var.EconCapability] = 1.15f, [Var.VariationScore] = 0.9f, [Var.TechAcquisition] = 1.1f }, gate: "I3d"));

        l.Add(Opt("col_prop_monogyne", "Monogyne", PolicySlot.GeneticDomestic,
            "AdministrativeReach ×1.2, succession crisis recurring", new() { [Var.AdministrativeReach] = 1.2f }, neutral: true));
        l.Add(Opt("col_prop_polygyne", "Polygyne", PolicySlot.GeneticDomestic,
            "AdministrativeReach ×0.9, splinter ×1.2", new() { [Var.AdministrativeReach] = 0.9f, [Var.SplinterPressure] = 1.2f }, gate: "I1a"));
        l.Add(Opt("col_prop_immune", "Immune Caste Investment", PolicySlot.GeneticDomestic,
            "D_min +0.2, upkeep +0.06/tick", new() { [Var.GenDMin] = 0.2f, [Var.UpkeepDrain] = 0.06f }, gate: "T4c"));
        l.Add(Opt("col_prop_sacrificial", "Sacrificial Specialists", PolicySlot.GeneticDomestic,
            "unlocks living-munition caste", new() { [Var.MaxSustainableForce] = 1.1f }, gate: "T3c"));

        l.Add(Opt("col_know_pheromone", "Pheromone Tradition", PolicySlot.InformationalDomestic,
            "Idea ×1.1, Tech ×0.9", new() { [Var.IdeaAcquisition] = 1.1f, [Var.TechAcquisition] = 0.9f }, neutral: true));
        l.Add(Opt("col_know_encoded", "Encoded Standard", PolicySlot.InformationalDomestic,
            "AdministrativeReach ×1.15, Tech ×1.15", new() { [Var.AdministrativeReach] = 1.15f, [Var.TechAcquisition] = 1.15f }, gate: "I2b"));
        l.Add(Opt("col_know_fast", "High Decision Velocity", PolicySlot.InformationalDomestic,
            "all acquisition ×1.2, cascade-error risk", new() { [Var.TechAcquisition] = 1.2f, [Var.IdeaAcquisition] = 1.2f }));
        l.Add(Opt("col_know_deliberative", "Deliberative Threshold", PolicySlot.InformationalDomestic,
            "acquisition ×0.9, resilience floor +0.1", new() { [Var.TechAcquisition] = 0.9f, [Var.IdeaAcquisition] = 0.9f, [Var.ResilienceFloor] = 0.1f }));

        l.Add(Opt("col_coh_minimal", "Minimal Cycling", PolicySlot.ExistentialDomestic, "no effect", new() { }, neutral: true));
        l.Add(Opt("col_coh_pheromone", "Pheromone Ritual Memory", PolicySlot.ExistentialDomestic,
            "resilience recovery ×1.15", new() { [Var.ResilienceRecoveryRate] = 1.15f }, gate: "I1b"));

        l.Add(Opt("col_order_single", "Single-Hub Queen", PolicySlot.CoerciveDomestic,
            "AdministrativeReach ×1.25, Variation ×0.65", new() { [Var.AdministrativeReach] = 1.25f, [Var.VariationScore] = 0.65f }, neutral: true));
        l.Add(Opt("col_order_nest", "Nest Cluster", PolicySlot.CoerciveDomestic,
            "splinter ×0.7, Variation ×1.25, AdministrativeReach ×0.85", new() { [Var.SplinterPressure] = 0.7f, [Var.VariationScore] = 1.25f, [Var.AdministrativeReach] = 0.85f }, gate: "I4b"));
        l.Add(Opt("col_order_caste", "Caste Codification", PolicySlot.CoerciveDomestic,
            "MaxSustainableForce ×1.5", new() { [Var.MaxSustainableForce] = 1.5f }, gate: "I3a"));

        // Foreign — as Individuated with Collective-specific replacements (§4.2).
        l.Add(Opt("col_trade_autarky", "Autarky", PolicySlot.EconomicForeign, "ConnectionStrength ×0.3", new() { [Var.ConnectionStrength] = 0.3f }, neutral: true));
        l.Add(Opt("col_trade_tariffs", "Selective Tariffs", PolicySlot.EconomicForeign, "favorability +0.15", new() { [Var.EconCapability] = 1.05f }, gate: "I1c"));
        l.Add(Opt("col_trade_open", "Open Routes", PolicySlot.EconomicForeign, "ConnectionStrength ×1.4", new() { [Var.ConnectionStrength] = 1.4f, [Var.DiffusionBonus] = 1.4f }, gate: "T2c"));
        l.Add(Opt("col_trade_dulosis", "Dulosis (Labor Raiding)", PolicySlot.EconomicForeign,
            "forcibly imports population from a defeated colony (retention 0.5)", new() { [Var.PopulationGrowth] = 1.1f, [Var.PassivePolityDrain] = -0.04f }, gate: "T2a"));

        l.Add(Opt("col_bio_open", "Open Contact", PolicySlot.GeneticForeign, "contact ×1.3", new() { [Var.ConnectionStrength] = 1.3f }, neutral: true));
        l.Add(Opt("col_bio_quarantine", "Quarantine Regime", PolicySlot.GeneticForeign, "plague exposure ×0.4", new() { [Var.ConnectionStrength] = 0.8f, [Var.GenDMin] = 0.15f }, gate: "T4c"));
        l.Add(Opt("col_bio_bioweapon", "Bioweapon Doctrine", PolicySlot.GeneticForeign, "unlocks offensive Genetic maneuvers", new() { }, gate: "T3c"));
        l.Add(Opt("col_bio_xenophobic", "Xenophobic Closure", PolicySlot.GeneticForeign, "cross-species ConnectionStrength ×0.2", new() { [Var.ConnectionStrength] = 0.2f }));

        l.Add(Opt("col_open_free", "Free Exchange", PolicySlot.InformationalForeign, "diffusion ×1.5", new() { [Var.DiffusionBonus] = 1.5f, [Var.StealTechDefense] = 0.7f }, neutral: true));
        l.Add(Opt("col_open_guarded", "Guarded Archives", PolicySlot.InformationalForeign, "legibility ×0.5", new() { [Var.SignalLegibility] = 0.5f, [Var.StealTechDefense] = 1.4f }, gate: "I2b"));
        l.Add(Opt("col_open_espionage", "Espionage Program", PolicySlot.InformationalForeign, "unlocks Steal Tech/Idea", new() { [Var.StealTechOffense] = 1.3f }, gate: "T3d"));
        l.Add(Opt("col_open_disinfo", "Disinformation Campaign", PolicySlot.InformationalForeign, "target acquisition ×0.8", new() { [Var.SignalLegibility] = 0.7f }, gate: "T3d"));

        l.Add(Opt("col_conv_noninterference", "Non-Interference", PolicySlot.ExistentialForeign, "no tier-3 belief to export", new() { }, neutral: true));

        l.Add(Opt("col_dipl_isolation", "Isolationism", PolicySlot.CoerciveForeign, "war_threshold ×1.5", new() { [Var.WarThreshold] = 1.5f, [Var.AllianceThreshold] = 1.5f }, neutral: true));
        l.Add(Opt("col_dipl_balance", "Balance of Power", PolicySlot.CoerciveForeign, "alliance ×1.3", new() { [Var.AllianceThreshold] = 0.85f }, gate: "I3c"));
        l.Add(Opt("col_dipl_collective", "Collective Security", PolicySlot.CoerciveForeign, "dependency discount 0.8→0.9", new() { [Var.AllianceThreshold] = 0.7f }, gate: "I3c"));
        l.Add(Opt("col_dipl_absorption", "Absorption Doctrine", PolicySlot.CoerciveForeign,
            "pursues colony-merger over peace — Accept Peace ×0.4, Demand Vassalage ×1.3",
            new() { [Var.WarThreshold] = 0.8f, [Var.PassivePolityDrain] = -0.05f }, gate: "T2a"));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // LIVING REEF (§5) — domestic reuses civilization-tracks §4.2 catalog exactly
    // ══════════════════════════════════════════════════════════════════════════
    private static void BuildLivingReef(List<PolicyOption> l)
    {
        // era3-adaptation-trees-spec §2.4: Aggressive Spread/Dense Consolidation are now earned via
        // A1a (Dispersal Strategy) — the whole distribution axis is locked until then, so Passive
        // Growth is the genuine neutral default in the meantime.
        l.Add(Opt("lr_prod_passive", "Passive Growth", PolicySlot.EconomicDomestic,
            "undifferentiated baseline growth, no tradeoff yet", new() { }, neutral: true));
        l.Add(Opt("lr_prod_aggressive", "Aggressive Spread", PolicySlot.EconomicDomestic,
            "high Self Econ gain, lowers Kinetic capability", new() { [Var.EconMMax] = 1.3f, [Var.KineticCapability] = 0.7f }, gate: "A1a"));
        l.Add(Opt("lr_prod_dense", "Dense Consolidation", PolicySlot.EconomicDomestic,
            "resilience floor +0.15, lowers expansion", new() { [Var.ResilienceFloor] = 0.15f, [Var.EconMMax] = 0.8f }, gate: "A1a"));
        l.Add(Opt("lr_prod_symbiotic", "Symbiotic Integration", PolicySlot.EconomicDomestic,
            "unlocks Formal Trade (Economic foreign slot)", new() { [Var.ConnectionStrength] = 1.2f }));

        l.Add(Opt("lr_prop_none", "Growth-Only", PolicySlot.GeneticDomestic, "no separate propagation policy", new() { }, neutral: true));

        l.Add(Opt("lr_know_none", "Ambient Signaling", PolicySlot.InformationalDomestic, "tier-1/2 only", new() { }, neutral: true));

        l.Add(Opt("lr_coh_none", "Tier-1/2 Ritual", PolicySlot.ExistentialDomestic, "tier-1/2 only, per main spec §5", new() { }, neutral: true));

        // Polymorphic Castes now earned via A2a (Differentiation) — Generalist Units (unmentioned in
        // the gating table) is the real neutral default in the meantime.
        l.Add(Opt("lr_order_polymorphic", "Polymorphic Castes", PolicySlot.CoerciveDomestic,
            "higher ceiling, more overhead", new() { [Var.EconCapability] = 1.15f, [Var.UpkeepCost] = 1.1f }, gate: "A2a"));
        l.Add(Opt("lr_order_generalist", "Generalist Units", PolicySlot.CoerciveDomestic,
            "resilient to local loss, lower peak", new() { [Var.ResilienceFloor] = 0.1f, [Var.EconCapability] = 0.9f }, neutral: true));
        // T3c half of the dual gate dropped (era3-systems-implementation-spec follow-up correction)
        // — Living Reef has no Tech-tree access at all, so T3c was permanently unreachable. A4a
        // alone (Living Reef's eusociality node) already fits sacrificial caste specialization
        // thematically; no replacement needed for the dropped half.
        l.Add(Opt("lr_order_sacrificial", "Sacrificial Specialists", PolicySlot.CoerciveDomestic,
            "living munitions — high war effect, costs population", new() { [Var.MaxSustainableForce] = 1.2f }, gate: "A4a"));

        // Foreign — Economic + Coercive only (§5).
        l.Add(Opt("lr_trade_sessile", "Sessile Isolation", PolicySlot.EconomicForeign,
            "no external ConnectionStrength", new() { [Var.ConnectionStrength] = 0.1f }, neutral: true));
        l.Add(Opt("lr_trade_symbiotic", "Symbiotic Exchange", PolicySlot.EconomicForeign,
            "unlocks the full biological-market engine with neighbors", new() { [Var.ConnectionStrength] = 1.3f }));
        // era3-systems-implementation-spec follow-up correction: repointed from T2c (permanently
        // unreachable — Living Reef has no Tech-tree access at all) to I3c, whose confirmed Living
        // Reef flavor name is literally "Inter-Colonial Pact Norms" — an exact thematic match for
        // a Formal Trade Pact, and Living Reef has full Idea-tree access to reach it.
        l.Add(Opt("lr_trade_pact", "Formal Trade Pact", PolicySlot.EconomicForeign,
            "ConnectionStrength ×1.4, diffusion ×1.4",
            new() { [Var.ConnectionStrength] = 1.4f, [Var.DiffusionBonus] = 1.4f }, gate: "I3c"));

        l.Add(Opt("lr_conflict_smother", "Smother", PolicySlot.CoerciveForeign,
            "overgrowth — territory denial", new() { }, neutral: true));
        l.Add(Opt("lr_conflict_chemical", "Chemical Defense", PolicySlot.CoerciveForeign,
            "allelopathy — escalates with time_sustained", new() { }));
        l.Add(Opt("lr_conflict_partition", "Substrate Partition", PolicySlot.CoerciveForeign,
            "de-escalation — stable non-aggression", new() { }));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TERRAFORMER / BLOOM FRONT / APEX PREDATOR (§6) — 3 domestic + 1 foreign (Conflict Posture)
    // ══════════════════════════════════════════════════════════════════════════
    private static void BuildEcological(List<PolicyOption> l)
    {
        // Terraformer
        l.Add(Opt("ter_prod_oxygenate", "Oxygenate", PolicySlot.EconomicDomestic, "pushes toward own optimum", new() { }, neutral: true));
        l.Add(Opt("ter_prod_acidify", "Acidify / Reduce", PolicySlot.EconomicDomestic, "opposite chemical target", new() { }));
        l.Add(Opt("ter_prod_stabilize", "Stabilize", PolicySlot.EconomicDomestic, "shared middle ground, suppresses runaway", new() { [Var.ResilienceFloor] = 0.1f }));

        l.Add(Opt("ter_prop_bulk", "Bulk Biomass", PolicySlot.GeneticDomestic, "raw mass growth", new() { [Var.PopulationGrowth] = 1.15f }, neutral: true));
        l.Add(Opt("ter_prop_reserve", "Reserve Banking", PolicySlot.GeneticDomestic, "banks biomass reserves", new() { [Var.ResilienceFloor] = 0.1f }));

        l.Add(Opt("ter_order_local", "Local Optimization", PolicySlot.CoerciveDomestic,
            "bounded influence, near-zero runaway", new() { [Var.ResilienceFloor] = 0.05f }, neutral: true));
        l.Add(Opt("ter_order_planetary", "Planetary Engineering", PolicySlot.CoerciveDomestic,
            "unbounded — high effect, real runaway risk", new() { [Var.EconMMax] = 1.4f }, gate: "A4b"));

        // era3-adaptation-trees-spec §2.4: Neutral Terraforming is the genuine neutral default;
        // Niche Hoarding needs baseline Biochemical capability (A2c), Adversarial (Biochemical
        // Warfare) needs the escalated tier (A3b).
        l.Add(Opt("ter_conflict_neutral", "Neutral Terraforming", PolicySlot.CoerciveForeign,
            "suppresses runaway for all, forgoes offense", new() { [Var.ResilienceFloor] = 0.1f }, neutral: true));
        l.Add(Opt("ter_conflict_niche", "Niche Hoarding", PolicySlot.CoerciveForeign, "narrower, precise, low runaway risk", new() { }, gate: "A2c"));
        l.Add(Opt("ter_conflict_adversarial", "Adversarial", PolicySlot.CoerciveForeign,
            "unlocks/buffs Biochemical Warfare, relation −0.05/tick with co-located civs",
            new() { [Var.PassivePolityDrain] = -0.05f }, gate: "A3b"));

        // Bloom Front
        l.Add(Opt("bf_prod_boombust", "Boom-Bust", PolicySlot.EconomicDomestic, "explosive then die-off", new() { [Var.EconMMax] = 1.3f }, neutral: true));
        l.Add(Opt("bf_prod_sustainable", "Sustainable Cropping", PolicySlot.EconomicDomestic, "capped, low collapse risk", new() { [Var.ResilienceFloor] = 0.1f }));
        l.Add(Opt("bf_prod_seasonal", "Seasonal Following", PolicySlot.EconomicDomestic, "migrates to track resource pulses", new() { }));

        l.Add(Opt("bf_prop_spore", "Resting-Spore Banking", PolicySlot.GeneticDomestic, "dormancy reserve", new() { [Var.ResilienceFloor] = 0.1f }, neutral: true));
        l.Add(Opt("bf_prop_explosive", "Explosive Reproduction", PolicySlot.GeneticDomestic, "raw growth spike", new() { [Var.PopulationGrowth] = 1.2f }));

        // era3-adaptation-trees-spec §2.4: the whole distribution axis needs A1a; Concentrated
        // Fronts additionally needs A3a. Undifferentiated is the true neutral pre-A1a.
        l.Add(Opt("bf_order_undifferentiated", "Undifferentiated", PolicySlot.CoerciveDomestic, "no distribution strategy yet", new() { }, neutral: true));
        l.Add(Opt("bf_order_scatter", "Wide Scatter", PolicySlot.CoerciveDomestic, "resilient, low peak", new() { [Var.ResilienceFloor] = 0.1f }, gate: "A1a"));
        l.Add(Opt("bf_order_concentrated", "Concentrated Fronts", PolicySlot.CoerciveDomestic, "dominant, fragile, runaway-adjacent", new() { [Var.EconMMax] = 1.3f }, gate: "A1a", gate2: "A3a"));

        l.Add(Opt("bf_conflict_passive", "Passive Drift", PolicySlot.CoerciveForeign, "no offensive maneuvers, contact ×0.6", new() { }, neutral: true));
        l.Add(Opt("bf_conflict_aggressive", "Aggressive Bloom", PolicySlot.CoerciveForeign, "unlocks Shade-Out / Toxic Bloom", new() { }, gate: "A2c"));

        // Apex Predator
        l.Add(Opt("ap_prod_overhunt", "Overhunt", PolicySlot.EconomicDomestic, "max yield, prey-collapse risk", new() { [Var.EconMMax] = 1.4f }, neutral: true));
        l.Add(Opt("ap_prod_sustainable", "Sustainable Cropping", PolicySlot.EconomicDomestic, "capped, sustainable", new() { [Var.ResilienceFloor] = 0.1f }));
        l.Add(Opt("ap_prod_specialization", "Prey Specialization", PolicySlot.EconomicDomestic, "high efficiency, concentration risk", new() { [Var.EconMMax] = 1.1f }));

        l.Add(Opt("ap_prop_pack", "Pack Rearing", PolicySlot.GeneticDomestic, "cooperative rearing", new() { [Var.PopulationGrowth] = 1.1f }, neutral: true));
        l.Add(Opt("ap_prop_disease", "Disease Resistance", PolicySlot.GeneticDomestic, "raises D_min", new() { [Var.GenDMin] = 0.15f }));

        // era3-adaptation-trees-spec §2.4: Nomadic/Fixed both need A1a — an "Unsettled" default
        // stands in until then.
        l.Add(Opt("ap_order_unsettled", "Unsettled", PolicySlot.CoerciveDomestic, "no ranging strategy yet", new() { }, neutral: true));
        // era3-systems-implementation-spec follow-up correction, INTERIM fix: gates dropped, not
        // repointed — A1a is permanently unreachable (Apex Predator has no Adaptation-tree access
        // at all now) and its own Idea-tree node names are still pending. Ungating restores real
        // strategic depth to this slot (both non-floor options were locked) rather than leaving it
        // stuck on "Unsettled" forever. Repoint to a real Idea-tree gate once those names land.
        l.Add(Opt("ap_order_nomadic", "Nomadic Hunting", PolicySlot.CoerciveDomestic, "resilient to local depletion", new() { [Var.ResilienceFloor] = 0.1f }));
        l.Add(Opt("ap_order_fixed", "Fixed Territory", PolicySlot.CoerciveDomestic, "high dominance, vulnerable to incursion", new() { [Var.EconMMax] = 1.2f }));

        l.Add(Opt("ap_conflict_coexistence", "Trophic Coexistence", PolicySlot.CoerciveForeign, "niche partitioning, forgoes offense", new() { }, neutral: true));
        // era3-systems-implementation-spec follow-up correction, INTERIM fix — see ap_order_nomadic
        // above: A2c permanently unreachable, gate dropped rather than left dead; this was the
        // slot's only non-floor option, so it was stuck on Trophic Coexistence forever.
        l.Add(Opt("ap_conflict_exclusionary", "Exclusionary", PolicySlot.CoerciveForeign, "unlocks Territorial Exclusion / Kleptoparasitism", new() { }));
    }
}

/// Per-slot per-civ runtime state — which policy is active, what it replaced, and how long ago,
/// so GetVar's legacy-decay blend and SwitchPolicy's lockout have something to read.
public class PolicySlotState
{
    public string ActiveId;
    public string PreviousId;
    public float TicksSinceSwitch;
    public int LockoutTicksRemaining;
}
