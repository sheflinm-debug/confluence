# Era 3 Ungated Policy Options — Full Inventory

Generated for gating analysis / parallel-option-track design. Every `d3_*` decision card that is
currently reachable with **no** Tech/Idea/Adaptation requirement, its full choice list, exactly what
each choice sets, and whether removing/gating it risks stranding downstream content ("load-bearing").

**Structural note before the list:** almost every card below is defined **twice** — once in
`GeneCatalog.cs` (the original Era 1/2-style gene-popup pipeline) and once in `Era3HUD.cs`'s own
`_cards` list (the newer in-panel/screen-popup pipeline). Both copies currently carry the *same*
gate condition for every card except `d3_government_transition` (already fixed to match). This
means: **any gating change must be applied to both copies**, or the ungated copy will simply fire
first and the gate on the other copy becomes dead code. This bit us once already (see the
`d3_government_transition` fix a few turns back) and will bite again on any of the 9 duplicated ids
below if only one copy is edited.

---

## 1. Genuinely ungated (no Tech/Idea/Adaptation gate at all)

### `d3_trade_policy` — Tab: Economic
**Defined in:** `GeneCatalog.cs` + `Era3HUD.cs` (duplicate, same gate)
**Gate:** `e3_exchange_contact` only
**Sets:** `ForeignOpenness`, `FormalTradeActive` (via `Era3Manager.SetTradePolicy`)

| Choice | Effect |
|---|---|
| Open Routes | tariff 0.05, openness 0.90 — max exchange, arbitrage risk |
| Balanced Tariffs | tariff 0.35, openness 0.60 |
| Embargo | tariff 0.95, openness 0.15 — isolationist, resilience cost |

**Load-bearing?** No. `ForeignOpenness`/`FormalTradeActive` are also set by several other cards
(`d3_large_initiative_1`, `d3_negotiate_treaty`, `d3_graft_link_treaty`, crisis responses). Safe to
gate — nothing else depends on *this specific card* being the only path.

---

### `d3_settlement_admission_policy` — Tab: Economic
**Already gated** (I4b / I3c fallback) — listed here only for completeness since it's the direct
predecessor of the analysis; see prior turn.

---

### `d3_formal_currency` — Tab: Economic (Individuated only)
**Gate:** `Architecture==Individuated && e3_surplus_economy && e3_trade_network`
**Sets:** `DomainEconomic`

| Choice | Effect |
|---|---|
| Adopt coinage/tokens | `DomainEconomic += 0.15` |
| Keep barter | no change |

**Load-bearing?** No. `DomainEconomic` is also set by `d3_domain_investment`'s Economic choice and
`ApplyDomainInvestment` calls elsewhere. Safe to gate.

---

### `d3_graft_link_treaty` — Tab: Economic (Distributed only)
**Gate:** `Architecture==Distributed && e3_trade_network`
**Sets:** `FormalTradeActive = true`, `RecoverResilience(0.05)`

**Load-bearing?** No — same `FormalTradeActive` flag as `d3_trade_policy`, not the sole path. Safe
to gate.

---

### `d3_large_initiative_1` — Tab: Economic
**Defined in:** `GeneCatalog.cs` + `Era3HUD.cs` (duplicate, same gate)
**Gate:** `e3_surplus_economy` only
**Sets:** varies by choice

| Choice | Effect |
|---|---|
| Vaccination Drive | `RecoverResilience(0.10)` |
| Trade Expansion | `SetTradePolicy(tariff 0.10, openness 0.80)` |
| Monument | `InvestReligion += 0.15` |

**Load-bearing?** No. All three effects are duplicated elsewhere (resilience recovery, trade policy,
religion investment all have other levers). Safe to gate — good candidate for an actual Tier-3/4
"large initiative" Tech/Idea requirement (I4c "Mass Mobilization Doctrine" is the spec's own named
gate for large initiatives generally).

---

### `d3_domesticate_species` — Tab: Genetic/Biological (Individuated only)
**Already gated** (T1c) — listed for completeness.

---

### `d3_symbiotic_defender` — Tab: Genetic/Biological (Distributed only)
**Gate:** `Architecture==Distributed && e3_trade_network`
**Sets:** `DomainBiochemical += 0.15` (via `ApplyDomainInvestment`)

**Load-bearing?** No — same `DomainBiochemical` field `d3_domain_investment`/`d3_bioweapon_option`
also write to. Safe to gate.

---

### `d3_idea_patronage` — Tab: Informational
**Defined in:** `GeneCatalog.cs` + `Era3HUD.cs` (duplicate, same gate)
**Gate:** `e3_chiefdom` only
**Sets:** `IdeaPatronage` (categorical enum: Culture / Religion / Science / Military)

**Load-bearing?** **Partially.** This is the ONLY thing that ever sets `civ.IdeaPatronage` — nothing
else writes it. Whether that's "load-bearing" depends on whether anything *reads* it as a hard gate
elsewhere (worth checking before retiring — I have not verified every consumer of `IdeaPatronage`).
Note this is conceptually distinct from the newer `Era3Manager.SetPatronageTarget` (Tech/Idea tree
per-node patronage, §7.1) — two different patronage systems currently coexist.

---

### `d3_writing_adoption` — Tab: Informational (Individuated only)
**Gate:** `Architecture==Individuated && e3_writing && CommMedium ∈ {VocalAuditory, VisualGestural}`
**Sets:** `InvestInformation += 0.12`, `DomainInformational += 0.10`

**Load-bearing?** No — both fields have other write sites. Safe to gate.

---

### `d3_kin_recognition_break` — Tab: Informational (Distributed only)
**Gate:** `Architecture==Distributed && SignalBandwidthTier>=1 && e3_trade_network`
**Sets:** `DetectionCapability += 0.20`, drains `TradeHealth` with all NPC partners by 0.10

**Load-bearing?** No. Safe to gate — thematically this is close to the Tech tree's T3d (Signal
Infrastructure) or I2d (Kin-Extended Trust, inverted), either would fit.

---

### `d3_cascade_error_mitigation` — Tab: Informational (Collective only)
**Gate:** `Architecture==Collective && DecVelocity==Slow`
**Sets:** `StigmergicBandwidth += 0.15`, `RecoverResilience(0.06)`

**Load-bearing?** No. Safe to gate.

---

### `d3_found_organized_religion` — Tab: Existential (Individuated only)
**Gate:** `Architecture==Individuated && BeliefTier>=2 && !HasOrganizedReligion && e3_religion_organized`
**Sets:** `HasOrganizedReligion = true`, `BeliefTier = 3`

**Load-bearing?** **Yes, somewhat.** `HasOrganizedReligion` gates `d3_schism_response` (a crisis
card) and the Theocracy government option's flavor. Not a hard system-breaker if ungated/gated
either way, but check `d3_schism_response`'s eligibility chain before touching this one.

---

### `d3_war_or_diplomacy` — Tab: Coercive
**Defined in:** `GeneCatalog.cs` + `Era3HUD.cs` (duplicate, same gate)
**Gate:** `e3_state_formation` only
**Sets:**
- "Organized Warfare" → `SetWarPath`: `Acquire("e3_warfare_organized")`, `InvestCoercive += 0.15`
- "Diplomacy" → `SetDiplomacyPath`: `Acquire("e3_diplomacy")`, `FormalAllianceActive = true`, `ForeignOpenness += 0.20`

**Load-bearing? YES — the clearest case in this whole list.**
- `e3_warfare_organized` gates: `d3_domain_investment`, `d3_colony_raid`, and (this session) the
  entire `TickConflict`/formal-war strike system. **However**, T2a (Organized Conflict, Tech tree)
  now ALSO grants `e3_warfare_organized` independently (wired this session) — so warfare is no
  longer *solely* dependent on this card. Safe-ish to gate now, where it wasn't before.
- `e3_diplomacy` gates: `d3_negotiate_treaty` and the `e3_empire` auto-event. **Nothing else sets
  `e3_diplomacy`.** If this card is retired/gated and the gate is ever unreachable, `e3_diplomacy`
  becomes permanently unset and both `d3_negotiate_treaty` and `e3_empire` become permanently dead
  for that civ. `FormalAllianceActive` (the mechanically-similar flag) DOES have an alternate path
  now (`Era3Manager.ProposeAlliance`), but `e3_diplomacy` the *flag itself* does not.
  **Recommended fix if gating this:** change `d3_negotiate_treaty` and `e3_empire`'s prerequisite
  from `e3_diplomacy` to `FormalAllianceActive`, closing the gap before gating the source card.

---

### `d3_domain_investment` — Tab: Coercive
**Defined in:** `GeneCatalog.cs` + `Era3HUD.cs` (duplicate, same gate)
**Gate:** `e3_warfare_organized` only
**Sets:** `DomainKinetic`/`DomainBiochemical`/`DomainInformational`/`DomainEconomic` (+0.25 to
whichever chosen)

| Choice | Effect |
|---|---|
| Kinetic | `+0.25 DomainKinetic` |
| Biochemical | `+0.25 DomainBiochemical` |
| Informational | `+0.25 DomainInformational` |
| Economic | `+0.25 DomainEconomic` |

**Load-bearing? YES.** This is the **only** thing that ever lets a civ deliberately shift its
war-domain allocation away from the fixed architecture-native starting values
(`CivilizationState.InitNativeDomains`). Retire/gate this with nothing to replace it and domain
allocation just freezes at whatever `InitNativeDomains` set at civ creation, forever. No verified
automatic replacement exists yet (a "domains drift proportional to Coercive channel investment"
mechanism was discussed but never built). **Do not gate without building that replacement first**, or
without accepting that domain allocation becomes permanently static for any civ that doesn't reach
the gate.

---

### `d3_bioweapon_option` — Tab: Coercive
**Defined in:** `GeneCatalog.cs` + `Era3HUD.cs` (duplicate, same gate)
**Gate:** `d3_domain_investment` resolved AND `e3_warfare_organized`
**Sets:** `DomainBiochemical += 0.30` (if "Develop")

**Load-bearing?** Downstream of `d3_domain_investment` (inherits its risk) but doesn't itself gate
anything else. If `d3_domain_investment` gets a real replacement mechanism, this one can likely just
be retired outright (its effect folds into the replacement) rather than separately gated. The Policy
Catalog's own `*_bio_bioweapon`/`*_bio_mycotoxin` policies (T3c-gated) already cover this exact
decision at the Policy layer — this card may be pure duplication now.

---

### `d3_sever_graft_link` — Tab: Coercive (Distributed only)
**Gate:** `Architecture==Distributed && e3_trade_network`
**Sets:** `RecoverResilience(0.08)`, `ForeignOpenness -= 0.20`

**Load-bearing?** No. Safe to gate.

---

### `d3_colony_raid` — Tab: Coercive (Collective only)
**Gate:** `Architecture==Collective && e3_warfare_organized`
**Sets:** `Stockpile += 0.4`, `DomainKinetic += 0.10`, drains `TradeHealth` with all NPCs by 0.15

**Load-bearing?** No — downstream of `e3_warfare_organized` (see `d3_war_or_diplomacy` above) but
doesn't itself gate anything further. Safe to gate once/if the war-doctrine flag issue is resolved.

---

## 2. Deliberately kept ungated (per era3-adaptation-trees-spec §1.2 — do not gate)

### `d3_caste_labor` — Tab: Genetic/Biological
**Gate:** `e3_social_stratification` only. **Spec explicitly says**: "A species can always decide
who forages; it cannot run a natalist mobilization campaign without codified law." This is the
Tier-1 form; the *sophisticated* forms already live in the gated Policy Catalog (Specialized Castes
[I1a], Natalist Mobilization [I3a]).

### `d3_kinship_policy` — Tab: Existential
**Gate:** `e3_family_norms_emerge` only. Same spec carve-out as above — basic kinship structure is a
Tier-1 choice; sophisticated forms are Policy-Catalog-gated.

---

## 3. Crisis-response cards (reactive, different category — not "free policy adoption")

These only ever appear once a crisis has already fired (`e3_*_active` flags set by
`TriggerCrisisRoll`/`TickPolity`, not by player choice), so they're not really comparable to the
proactive policy cards above — there's no "adopt this whenever" exploit, only "respond to a crisis
that already happened." Listed for completeness since you asked for the full inventory:

- `d3_plague_response` (Quarantine / Treat / Ignore)
- `d3_schism_response` (Suppress / Accommodate / Embrace) — Individuated only, requires `HasOrganizedReligion`
- `d3_queen_succession`, `d3_secession_crisis`, `d3_succession_crisis` — architecture-specific crisis cards
- `d3_golden_age_response` — triggered by sustained mutualism, not a crisis exactly but same reactive shape
- `d3_administrative_crisis` — Polity Model crisis (already discussed this session)
- `d3_recognize_occupied_territory` — reactive to a real war outcome (occupied territory existing), not proactively adoptable

---

## 4. Summary table

| Card | Load-bearing risk | Safe to gate now? |
|---|---|---|
| `d3_trade_policy` | None | Yes |
| `d3_formal_currency` | None | Yes |
| `d3_graft_link_treaty` | None | Yes |
| `d3_large_initiative_1` | None | Yes |
| `d3_symbiotic_defender` | None | Yes |
| `d3_idea_patronage` | Partial (sole writer of `IdeaPatronage`, unverified readers) | Check first |
| `d3_writing_adoption` | None | Yes |
| `d3_kin_recognition_break` | None | Yes |
| `d3_cascade_error_mitigation` | None | Yes |
| `d3_found_organized_religion` | Partial (gates `d3_schism_response`) | Check first |
| `d3_war_or_diplomacy` | **Yes** — sole source of `e3_diplomacy` | Fix `e3_diplomacy` consumers first |
| `d3_domain_investment` | **Yes** — sole source of Domain* allocation shifts | Build a replacement first |
| `d3_bioweapon_option` | Downstream of domain_investment; may be pure duplicate of Policy Catalog | Consider retiring instead |
| `d3_sever_graft_link` | None | Yes |
| `d3_colony_raid` | Downstream of war-doctrine flag only | Yes, once war-doctrine resolved |
