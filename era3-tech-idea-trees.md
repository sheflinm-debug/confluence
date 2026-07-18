# Era 3 Tech, Idea &amp; Adaptation Trees — Full Current Contents

Three progression trees, 36 nodes total. Tech + Idea (26 nodes) live in
[Era3TechTree.cs](Assets/Scripts/Era3TechTree.cs) and share one acquisition engine; Adaptation
(10 nodes) lives in [Era3AdaptationTree.cs](Assets/Scripts/Era3AdaptationTree.cs) with its own,
evolution-flavored engine. Tech = material/physical capability (all seven tracks — everyone builds
things). Idea = institutional/social capability (Commerce Engine architectures only, plus a thin
Coercive-only slice for Living Reef — see Gating Rules below). Adaptation = evolved biological
capability (the three ecological paths, plus Living Reef's biological side — Commerce Engine never
touches it).

Every node has a **single display name per track** — the same node id means something different
depending on which of the seven Era 3 tracks the civ is on (Commerce Engine ×3 architectures,
LivingReef, Terraformer, BloomFront, ApexPredator). `—` means that track doesn't get this node at all.

---

## Tech Tree (13 nodes)

### Tier 1 (no prerequisites)

| Id | Channel | VarSens | Individuated | Distributed | Collective | LivingReef | Terraformer | BloomFront | ApexPredator |
|---|---|---|---|---|---|---|---|---|---|
| T1a | Coercive | 0.05 | Toolcraft | Structural Biomass Allocation | Carapace Development | Reef Substrate Deposition | Bulk Tissue Accumulation | Rapid Cell-Wall Synthesis | Musculoskeletal Reinforcement |
| T1b | Coercive | 0.05 | Land Claim | Chemical Perimeter Signaling | Nest Boundary Marking | Colonial Margin Definition | — | — | Range Scent-Marking |
| T1c | Economic | 0.20 | Craft Specialization | Assimilation Efficiency | Foraging Efficiency | Filter-Feeding Optimization | Metabolic Throughput Scaling | Nutrient Uptake Acceleration | Digestive Efficiency |

### Tier 2

| Id | Channel | VarSens | Prereqs | Individuated | Distributed | Collective | LivingReef | Terraformer | BloomFront | ApexPredator |
|---|---|---|---|---|---|---|---|---|---|---|
| T2a | Coercive | -0.30 | T1a | Military Doctrine | Coordinated Severance Response | Soldier-Caste Doctrine | Coordinated Aggression Response | Adversarial Chemistry Threshold | Bloom Synchronization | Hunting-Coordination Instinct |
| T2b | Coercive | 0.05 | T1a, T1b | Fortification | Underground Hardening | Chamber Reinforcement | Skeletal Density Increase | Buffering Capacity | Cyst/Dormancy Defense | Den/Range Defense |
| T2c | Economic | 0.20 | T1c | Trade Roads | Extended Graft Reach | Trail Pheromone Networks | Colonial Current-Riding | Atmospheric Circulation Reach | Current-Borne Dispersal | Extended Range Tracking |
| T2d | Economic | 0.20 | T1c | Granaries | Boom/Crash Buffering | Biomass Stockpile Chambers | Nutrient Bank Storage | Reserve Biomass Banking | Resting-Spore Banking | Fat/Reserve Storage |

### Tier 3

| Id | Channel | VarSens | Prereqs | Individuated | Distributed | Collective | LivingReef | Terraformer | BloomFront | ApexPredator |
|---|---|---|---|---|---|---|---|---|---|---|
| T3a | Coercive | 0.30 | T2a, T2b | Cross-Domain Doctrine | Cross-Medium Adaptation | Cross-Caste Flexibility | Mixed-Substrate Tolerance | Alternate-Chemistry Tolerance | Cross-Habitat Tolerance | Alternate-Prey Adaptation |
| T3b | Economic | 0.10 | T2c, T2d | Mass Production | Network-Wide Output Scaling | Mass Caste Output | Colonial Mass Growth | Bulk Metabolic Scaling | Explosive Reproduction Scaling | Pack-Scale Yield |
| T3c | Biological | -0.30 | T2a | Bioweapons Program | Mycotoxin Engineering | Venom/Toxin Caste Development | Allelochemical Escalation | Full Biochemical Warfare | Red-Tide Toxin Synthesis | Venom/Toxin Adaptation |
| T3d | Informational | 0.45 | T2c | Propaganda Infrastructure | Signal-Protocol Warfare | Stigmergic Disruption | — | — | — | — |

### Tier 4

| Id | Channel | VarSens | Prereqs | Individuated | Distributed | Collective | LivingReef | Terraformer | BloomFront | ApexPredator |
|---|---|---|---|---|---|---|---|---|---|---|
| T4a | Coercive | -0.35 | T3a, T3b | Long-Range Weapons | Explosive Spore/Propagule Dispersal | Long-Range Raiding Castes | Long-Range Larval Dispersal | Global Circulation Engineering | Extended Bloom-Front Range | Extended Territorial Range |
| T4b | Economic | -0.10 | T3b | Orbital/Space Infrastructure | Continental Network Engineering | Mega-Colony Engineering | Basin-Scale Reef Engineering | Planetary Atmosphere Engineering | Ocean-Basin Bloom Engineering | Continental Range Dominance |
| T4c | Biological | 0.20 | T3a, T3c | Public Health Infrastructure | Full Compartmentalization Suite | Immune-Caste Infrastructure | Colonial Immune Response | Self-Chemistry Regulation | Bloom-Collapse Resistance | Disease/Parasite Resistance |

**Tech-specific gating quirks (`IsApplicable`):**
- **T1b** — unavailable to Terraformer/BloomFront (no fixed boundary to claim/mark; zone-spread tracks).
- **T3d** — Commerce Engine only (ecological paths have no Informational-channel mediation layer).
- **T4b** — requires the civ to have built at least one structure (`BuiltStructures.Count > 0`), approximating the spec's "Structures ≥ threshold" gate.

---

## Idea Tree (13 nodes)

Commerce Engine architectures only (Individuated/Distributed/Collective), plus a **thin slice** for
Living Reef: Living Reef only ever sees Idea nodes on the **Coercive** channel (organization/
governance-adjacent) — everything else returns not-applicable for it. Terraformer/BloomFront/
ApexPredator get no Idea nodes at all (they use the separate Adaptation tree instead — see below).

### Tier 1 (no prerequisites)

| Id | Channel | VarSens | Individuated | Distributed | Collective | LivingReef |
|---|---|---|---|---|---|---|
| I1a | Biological | 0.25 | Kinship Custom | Clonal-Branch Recognition | Brood/Caste Norms | Colonial Lineage Recognition |
| I1b | Existential | 0.25 | Folk Ritual | Chemical Ritual Signaling | Pheromone Ritual Memory | Colonial Ritual Cycling |
| I1c | Economic | 0.25 | Gift/Reciprocity Custom | Graft Reciprocity Norms | Trophallaxis Exchange Norms | Symbiotic Exchange Norms |

### Tier 2

| Id | Channel | VarSens | Prereqs | Individuated | Distributed | Collective | LivingReef |
|---|---|---|---|---|---|---|---|
| I2a | Coercive | 0.35 | I1a | Chieftaincy | Hub-Node Precedence | Queen/Founder Precedence | Founder-Colony Precedence |
| I2b | Informational | 0.35 | I1c | Writing | Signal-Protocol Standardization | Stigmergic Encoding Standard | Colonial Signal Standardization |
| I2c | Existential | 0.40 | I1b | Cosmology | — | — | — |
| I2d | Biological | 0.35 | I1a, I1c | Ethnic/Tribal Affinity | Network-Kin Affinity | Colony-Kin Affinity | Colonial-Kin Affinity |

### Tier 3

| Id | Channel | VarSens | Prereqs | Individuated | Distributed | Collective | LivingReef |
|---|---|---|---|---|---|---|---|
| I3a | Coercive | 0.50 | I2a, I2b | Law Code | Topological Governance Standard | Command-Structure Codification | Colonial Governance Codification |
| I3b | Existential | 0.55 | I2c | Religious Pluralism | — | — | — |
| I3c | Coercive | 0.50 | I2d, I3a | Diplomatic Protocol | Formal Graft-Treaty Norms | Inter-Colony Pact Norms | Inter-Colonial Pact Norms |
| I3d | Economic | 0.45 | I1c, I2b | Currency | Standardized Exchange-Compound Value | Standardized Biomass Value | Standardized Resource-Share Value |

### Tier 4

| Id | Channel | VarSens | Prereqs | Individuated | Distributed | Collective | LivingReef |
|---|---|---|---|---|---|---|---|
| I4a | Informational | 0.55 | I3b | Missionary Doctrine | — | — | — |
| I4b | Coercive | 0.60 | I3c | Federalism | Mesh-Sovereignty Doctrine | Multi-Colony Sovereignty Doctrine | Reef/Basin Sovereignty Doctrine |
| I4c | Coercive | 0.05 | I3a **+ T4b** (cross-tree) | Mass Mobilization | Network-Wide Mobilization Doctrine | Colony-Wide Mobilization Doctrine | Basin-Wide Mobilization Doctrine |

**Idea-specific gating quirks:**
- **I4c** is the one cross-tree prerequisite in the whole system — it needs I3a from the Idea tree *and* T4b from the Tech tree.
- For Living Reef, any prereq that leads through a non-Coercive node (e.g. I3c needs I2d, a Biological node Living Reef can never itself unlock) is treated as automatically satisfied rather than a permanent wall — the underlying capability is assumed to exist in evolved form even though it's not a trackable node for that track.

---

## Acquisition mechanics (shared engine, both trees)

Per-node acquisition rate each tick, roughly:

```
rate = ChannelDial(node.Channel)^0.7 × IntelligenceFactor^w_i × CultureFactor^w_c
     × VariationFactor(node) × PatronageBonus × PolicyMultiplier
     + DiffusionBonus(from other civs who already have this node)
```

- **ChannelDial** — the civ's existing Economic/Biological/Informational/Existential/Coercive investment slider for that node's channel. Zero investment ⇒ zero research in that node, by design.
- **IntelligenceFactor** — civ's Era 2 Intelligence Index ÷ 50 (weight 1.0 for Tech, 0.6 for Idea).
- **CultureFactor** — structure-capability floor of 0.3 (weight 0.5 for Tech, 1.0 for Idea).
- **VariationFactor** — each node has a `VariationSensitivity` (shown above); positive-sensitivity nodes reward high Variation (open/mesh/decentralized structure + roster diversity), negative-sensitivity nodes reward high Conformity instead. Suppressed by active war (`WarVariationSuppression`) and modulated by Policy Catalog effects.
- **DiffusionBonus** — Tech/Idea only: a civ can pick up research speed on a node from diplomatic/trade-connected civs who already have it (no such bonus exists on the Adaptation tree below — evolution isn't taught).
- **PatronageBonus** (1.5×) — the civ's currently-patronized node (Idea Patronage policy) gets a flat boost, for up to 10 ticks.
- Research cost by tier: Tier 1 = 8, Tier 2 = 20, Tier 3 = 50, Tier 4 = 120. (The array's index 0 = 0f is dead code — `Mathf.Clamp(tier, 1, 4)` never produces 0, so no node is actually free.)

---

## Adaptation Tree (10 nodes)

Source: [Era3AdaptationTree.cs](Assets/Scripts/Era3AdaptationTree.cs). The **third** tree — for the
three ecological paths (Terraformer/BloomFront/ApexPredator) plus Living Reef's *biological* side.
Commerce Engine civs never see this tree at all (they get Tech + Idea only). Evolved, not learned:
driven by Biological-channel investment, reproductive rate, and a **required** SelectionPressure term
(zero pressure ⇒ zero progress — crisis-driven transitions, not timer unlocks), with no diffusion
bonus (you can't teach evolution to another civ the way a Tech/Idea node can spread by contact).

Per-node display name order is **[LivingReef, Terraformer, BloomFront, ApexPredator]** — no
Individuated/Distributed/Collective columns here, since Commerce Engine never touches this tree.

### Tier 1 (no prerequisites)

| Id | LivingReef | Terraformer | BloomFront | ApexPredator |
|---|---|---|---|---|
| A1a | Larval Dispersal Strategy | Circulation Coupling | Current-Riding | Ranging Strategy |
| A1b | Filter-Feeding Tuning | Metabolic Throughput | Nutrient Uptake | Digestive Efficiency |
| A1c | Substrate Tolerance | Chemical Self-Regulation | Salinity/Thermal Tolerance | Climate Tolerance |

### Tier 2

| Id | Prereqs | LivingReef | Terraformer | BloomFront | ApexPredator |
|---|---|---|---|---|---|
| A2a | A1a | Polymorphic Castes | Zonal Specialization | Morph Switching | Age/Sex Role Division |
| A2b | A1b | Nutrient Banking | Reserve Biomass | Resting Spores | Fat Reserves |
| A2c | A1c | Allelochemistry | Adversarial Chemistry | Baseline Toxicity | Venom |

### Tier 3

| Id | Prereqs | LivingReef | Terraformer | BloomFront | ApexPredator |
|---|---|---|---|---|---|
| A3a | A2a, A2b | Colonial Mass Scaling | Bulk Metabolic Scaling | Explosive Reproduction | Pack Scaling |
| A3b | A2c | Sweeper Tentacles | Full Biochemical Warfare | Red-Tide Synthesis | Toxin Escalation |

### Tier 4

| Id | Prereqs | LivingReef | Terraformer | BloomFront | ApexPredator |
|---|---|---|---|---|---|
| A4a | A3a (**LivingReef only**) | Sacrificial Polyps | — | — | — |
| A4b | A3a, A3b | Basin Reef Engineering | Planetary Atmosphere Engineering | Ocean-Basin Blooms | Continental Dominance |

**Adaptation-specific gating quirks:**
- **A4a** is gated to Living Reef specifically — the spec ties it to "requires eusociality," and Living Reef is the only ecological-path track that qualifies here.
- Commerce Engine (all three architectures) is fully excluded from this tree — `IsApplicable` returns false outright, since those civs use Tech + Idea instead.

### Adaptation acquisition formula

Same diminishing-returns shape as Tech/Idea (`^0.7`), different substrate — no channel-dial-only
gate, no diffusion, and a genuinely required selection-pressure term:

```
rate = InvestBiological^0.7 × (ReproductiveRate / 5)^0.8 × SelectionPressure^0.6 × VariationFactor
```

- **InvestBiological** — the civ's Biological-channel investment dial (same dial the Tech tree's Biological-channel nodes use). Zero investment ⇒ zero progress.
- **ReproductiveRate** — `5 / eatsToReproduce` (averaged across the civ's population), normalized against a reference value of 5. Faster breeders adapt faster.
- **SelectionPressure** — a **required** term, not a multiplier-only nicety: below 0.01 the rate is hard-zeroed regardless of everything else, more strictly than the Idea tree's "zero dial ⇒ zero rate" rule. This is the tree's own "crisis-driven, not a timer" enforcement.
- **VariationFactor** — driven by genetic diversity (not structural/roster diversity like Tech/Idea), flat sensitivity of 0.3 for every node (the source spec gives no per-node variation-sensitivity table for this tree, unlike Tech/Idea's per-node values).
- Research cost by tier: Tier 1 = 2, Tier 2 = 6, Tier 3 = 15, Tier 4 = 35 — lighter than Tech/Idea's costs, same dead-index-0 quirk as above.

---

## Gated Policies (Policy Catalog)

**Different system from [era3-ungated-policies.md](era3-ungated-policies.md)** — that file audited
the older `d3_*` decision-card popups (GeneCatalog.cs/Era3HUD.cs). This is
[Era3PolicyCatalog.cs](Assets/Scripts/Era3PolicyCatalog.cs), the newer 10-slot named-policy system,
where every option's `Gate`/`Gate2` field names a Tech/Idea/Adaptation node id directly — this is
the tree payoff: what you actually unlock by researching each node. 81 of the catalog's 152 options
carry a real gate; the rest are either each slot's neutral starting default (49 options, one per
slot) or — a smaller, separate group called out at the end — available immediately with no gate and
no default flag either (22 options).

Each civ only ever sees its own track's options (ids are prefixed `ind_`/`dis_`/`col_`/`lr_`/`ter_`/
`bf_`/`ap_`) across its 10 (Commerce Engine), 7 (Living Reef), or 4 (ecological path) policy slots.

### Individuated (28 gated)

| Id | Name | Slot | Gate(s) | Hint |
|---|---|---|---|---|
| ind_prod_guild | Guild Monopoly | EconomicDomestic | I1c | capability(Econ) ×1.15, Variation ×0.9, Econ Tech ×1.1 |
| ind_prod_market | Market Liberalization | EconomicDomestic | I3d | partner-choice pressure ×1.3, resilience floor −0.10, splinter ×1.1 |
| ind_prod_command | Command Economy | EconomicDomestic | I3a | M_max ×1.25, Variation ×0.75, build_rate ×1.4, Idea ×0.8 |
| ind_prop_clan | Extended Lineage / Clan | GeneticDomestic | I1a | AdministrativeReach ×1.1 |
| ind_prop_health | Public Health Investment | GeneticDomestic | T4c | D_min(Genetic) +0.15, upkeep +0.05/tick |
| ind_prop_natalist | Natalist Mobilization | GeneticDomestic | I3a | pop growth +30%, need_satisfaction ×0.85 |
| ind_know_scribal | Scribal Bureaucracy | InformationalDomestic | I2b | AdministrativeReach +0.5, Tech ×1.15 |
| ind_know_academy | Open Academy | InformationalDomestic | I3b | Variation ×1.25, military Tech ×0.85 |
| ind_know_doctrine | State Doctrine Control | InformationalDomestic | I3a | Variation ×0.7, military Tech ×1.3, outsider legibility ×0.6 |
| ind_coh_state | State Religion | ExistentialDomestic | I2c | Variation ×0.85 |
| ind_coh_pluralism | Sanctioned Pluralism | ExistentialDomestic | I3b | Variation ×1.2 |
| ind_coh_secular | Secular Rationalism | ExistentialDomestic | I4a | Tech ×1.1 |
| ind_order_legalism | Codified Legalism | CoerciveDomestic | I3a | AdministrativeReach ×1.15, MaxSustainableForce ×1.5, Variation ×0.9 |
| ind_order_federation | Devolved Federation | CoerciveDomestic | I4b | splinter ×0.7, Variation ×1.3, MaxSustainableForce ×0.8 |
| ind_order_garrison | Garrison State | CoerciveDomestic | T2b + I3a | upkeep ×1.3, Variation ×0.7 |
| ind_trade_tariffs | Selective Tariffs | EconomicForeign | I1c | favorability vs weaker +0.15 |
| ind_trade_open | Open Routes | EconomicForeign | T2c | ConnectionStrength ×1.4, diffusion ×1.4 |
| ind_trade_mercantile | Mercantile Aggression | EconomicForeign | I3d | partner-choice ×1.4, relation −0.05/tick with partners |
| ind_bio_quarantine | Quarantine Regime | GeneticForeign | T4c | plague exposure ×0.4, ConnectionStrength ×0.8 |
| ind_bio_bioweapon | Bioweapon Doctrine | GeneticForeign | T3c | unlocks offensive Genetic maneuvers, relation −0.3 on discovery |
| ind_open_guarded | Guarded Archives | InformationalForeign | I2b | legibility to outsiders ×0.5, Steal Tech vs you ×0.6, own diffusion ×0.7 |
| ind_open_espionage | Espionage Program | InformationalForeign | T3d | unlocks Steal Tech/Idea, relation −0.4 if caught |
| ind_open_disinfo | Disinformation Campaign | InformationalForeign | T3d | target Informational acquisition ×0.8, own legibility ×0.7 |
| ind_conv_missionary | Missionary Outreach | ExistentialForeign | I4a | Existential diffuse ×1.5 |
| ind_conv_supremacy | Doctrinal Supremacy | ExistentialForeign | I4a | Existential effect ×2.0, relation −0.4 with rival believers |
| ind_dipl_balance | Balance of Power | CoerciveForeign | I3c | alliance vs strongest ×1.3 |
| ind_dipl_collective | Collective Security | CoerciveForeign | I3c | alliance dependency discount 0.8→0.9 |
| ind_dipl_hegemonic | Hegemonic Expansion | CoerciveForeign | I3c + T4a | Demand Vassalage ×1.3, war_threshold ×0.7, relation −0.05/tick |

### Distributed (20 gated)

| Id | Name | Slot | Gate(s) | Hint |
|---|---|---|---|---|
| dis_prod_adaptive | Adaptive Rerouting | EconomicDomestic | T2d | stockpile efficiency ×1.4, Econ Tech ×1.1 |
| dis_prod_aggressive | Aggressive Assimilation | EconomicDomestic | T1c | territory growth ×1.4, upkeep ×1.2 |
| dis_prop_codit | Compartmentalization (CODIT) | GeneticDomestic | T4c | damage cascade halved, growth ×0.85 |
| dis_prop_symbiotic | Symbiotic Recruitment | GeneticDomestic | T3a | borrowed Kinetic capability, upkeep +0.08/tick |
| dis_know_protocol | Protocol Standardization | InformationalDomestic | I2b | AdministrativeReach ×1.15, Tech ×1.15, external legibility ×0.7 |
| dis_know_deception | Deception Substrate | InformationalDomestic | T3d | native disinformation ×1.6, own legibility ×0.5 |
| dis_coh_chemical | Chemical Ritual Synchrony | ExistentialDomestic | I1b | resilience recovery ×1.15, upkeep +0.03/tick |
| dis_order_regional | Regional Clusters | CoerciveDomestic | I2a | splinter ×0.9 |
| dis_order_mesh | Full Mesh | CoerciveDomestic | I4b | Variation ×1.4, splinter ×0.6, AdministrativeReach ×0.8 |
| dis_trade_selective | Selective Grafting | EconomicForeign | I1c | favorability +0.10 |
| dis_trade_siphon | Resource Siphoning | EconomicForeign | T3a | extracts without reciprocity — mycorrhizal arbitrage |
| dis_bio_perimeter | Chemical Perimeter | GeneticForeign | T1b | hostile contact ×0.5, upkeep +0.05/tick |
| dis_bio_mycotoxin | Mycotoxin Doctrine | GeneticForeign | T3c | unlocks area-denial maneuvers, relation −0.3 with neighbors |
| dis_bio_leeching | Mineral Leeching | GeneticForeign | T3c | target's Econ M_max ×0.7, relation −0.15 on detection |
| dis_open_guarded | Guarded Archives | InformationalForeign | I2b | legibility ×0.5, Steal Tech vs you ×0.6 |
| dis_open_espionage | Espionage Program | InformationalForeign | T3d | unlocks Steal Tech/Idea, native strength ×1.3 |
| dis_open_disinfo | Disinformation Campaign | InformationalForeign | T3d | native strength ×1.4 |
| dis_dipl_balance | Balance of Power | CoerciveForeign | I3c | alliance ×1.3 |
| dis_dipl_collective | Collective Security | CoerciveForeign | I3c | dependency discount 0.8→0.9 |
| dis_dipl_hegemonic | Hegemonic Expansion | CoerciveForeign | I3c + T4a | war_threshold ×0.7 |

### Collective (20 gated)

| Id | Name | Slot | Gate(s) | Hint |
|---|---|---|---|---|
| col_prod_specialized | Specialized Castes | EconomicDomestic | I1a | capability(chosen) ×1.3, Variation ×0.8 |
| col_prod_soldier | Soldier Surge | EconomicDomestic | T2a | MaxSustainableForce ×1.5, upkeep ×1.3, Econ M_max ×0.75 |
| col_prop_polygyne | Polygyne | GeneticDomestic | I1a | AdministrativeReach ×0.9, splinter ×1.2 |
| col_prop_immune | Immune Caste Investment | GeneticDomestic | T4c | D_min +0.2, upkeep +0.06/tick |
| col_prop_sacrificial | Sacrificial Specialists | GeneticDomestic | T3c | unlocks living-munition caste |
| col_know_encoded | Encoded Standard | InformationalDomestic | I2b | AdministrativeReach ×1.15, Tech ×1.15 |
| col_coh_pheromone | Pheromone Ritual Memory | ExistentialDomestic | I1b | resilience recovery ×1.15 |
| col_order_nest | Nest Cluster | CoerciveDomestic | I4b | splinter ×0.7, Variation ×1.25, AdministrativeReach ×0.85 |
| col_order_caste | Caste Codification | CoerciveDomestic | I3a | MaxSustainableForce ×1.5 |
| col_trade_tariffs | Selective Tariffs | EconomicForeign | I1c | favorability +0.15 |
| col_trade_open | Open Routes | EconomicForeign | T2c | ConnectionStrength ×1.4 |
| col_trade_dulosis | Dulosis (Labor Raiding) | EconomicForeign | T2a | forcibly imports population from a defeated colony |
| col_bio_quarantine | Quarantine Regime | GeneticForeign | T4c | plague exposure ×0.4 |
| col_bio_bioweapon | Bioweapon Doctrine | GeneticForeign | T3c | unlocks offensive Genetic maneuvers |
| col_open_guarded | Guarded Archives | InformationalForeign | I2b | legibility ×0.5 |
| col_open_espionage | Espionage Program | InformationalForeign | T3d | unlocks Steal Tech/Idea |
| col_open_disinfo | Disinformation Campaign | InformationalForeign | T3d | target acquisition ×0.8 |
| col_dipl_balance | Balance of Power | CoerciveForeign | I3c | alliance ×1.3 |
| col_dipl_collective | Collective Security | CoerciveForeign | I3c | dependency discount 0.8→0.9 |
| col_dipl_absorption | Absorption Doctrine | CoerciveForeign | T2a | pursues colony-merger over peace |

### Living Reef (4 gated)

| Id | Name | Slot | Gate(s) | Hint |
|---|---|---|---|---|
| lr_prod_aggressive | Aggressive Spread | EconomicDomestic | A1a | high Self Econ gain, lowers Kinetic capability |
| lr_prod_dense | Dense Consolidation | EconomicDomestic | A1a | resilience floor +0.15, lowers expansion |
| lr_order_polymorphic | Polymorphic Castes | CoerciveDomestic | A2a | higher ceiling, more overhead |
| lr_order_sacrificial | Sacrificial Specialists | CoerciveDomestic | T3c + A4a | living munitions — high war effect, costs population |

### Terraformer (3 gated)

| Id | Name | Slot | Gate(s) | Hint |
|---|---|---|---|---|
| ter_order_planetary | Planetary Engineering | CoerciveDomestic | A4b | unbounded — high effect, real runaway risk |
| ter_conflict_niche | Niche Hoarding | CoerciveForeign | A2c | narrower, precise, low runaway risk |
| ter_conflict_adversarial | Adversarial | CoerciveForeign | A3b | unlocks/buffs Biochemical Warfare, relation −0.05/tick |

### Bloom Front (3 gated)

| Id | Name | Slot | Gate(s) | Hint |
|---|---|---|---|---|
| bf_order_scatter | Wide Scatter | CoerciveDomestic | A1a | resilient, low peak |
| bf_order_concentrated | Concentrated Fronts | CoerciveDomestic | A1a + A3a | dominant, fragile, runaway-adjacent |
| bf_conflict_aggressive | Aggressive Bloom | CoerciveForeign | A2c | unlocks Shade-Out / Toxic Bloom |

### Apex Predator (3 gated)

| Id | Name | Slot | Gate(s) | Hint |
|---|---|---|---|---|
| ap_order_nomadic | Nomadic Hunting | CoerciveDomestic | A1a | resilient to local depletion |
| ap_order_fixed | Fixed Territory | CoerciveDomestic | A1a | high dominance, vulnerable to incursion |
| ap_conflict_exclusionary | Exclusionary | CoerciveForeign | A2c | unlocks Territorial Exclusion / Kleptoparasitism |

### One more group worth knowing about: neither gated nor the neutral default

22 options across the catalog carry **no** `Gate` and are **not** flagged `neutral: true` either —
meaning they sit alongside each slot's actual starting default, selectable from the moment Era 3
begins, with no research required: `ind_bio_xenophobic`; `dis_prod_hub`, `dis_prop_permissive`,
`dis_know_mesh`, `dis_trade_open`; `col_prod_forager`, `col_know_fast`, `col_know_deliberative`,
`col_bio_xenophobic`; `lr_prod_symbiotic`, `lr_trade_symbiotic`, `lr_conflict_chemical`,
`lr_conflict_partition`; `ter_prod_acidify`, `ter_prod_stabilize`, `ter_prop_reserve`;
`bf_prod_sustainable`, `bf_prod_seasonal`, `bf_prop_explosive`; `ap_prod_sustainable`,
`ap_prod_specialization`, `ap_prop_disease`. Whether that's intentional (some are genuinely
low-stakes/lateral choices with no real progression gate) or an oversight worth a pass — same
question as the `d3_*` audit — is worth deciding the same way: check each against whether it sets
anything load-bearing before leaving it alone.
