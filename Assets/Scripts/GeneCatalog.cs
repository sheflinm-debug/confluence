using UnityEngine;

/// Builds the default Section 6b gene set (see design spec Section 14e's dependency table).
public static class GeneCatalog
{
    public static void BuildDefault()
    {
        GeneEvolutionManager.ResetCatalog();
        CommunityNameRegistry.Reset();

        // Nucleus and Multicellularity fire automatically (no player choice) early in
        // Era 1 — they are prerequisites for every subsequent gene event.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "Nucleus",
            IsEra1Event = true,
            IsEligible = agent => GeneEvolutionManager.SessionElapsed >= 3f,
            AutoApply = agent => { }
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "Multicellularity",
            Prerequisites = new[] { "Nucleus" },
            IsEra1Event = true,
            IsEligible = agent => GeneEvolutionManager.SessionElapsed >= 8f,
            AutoApply = agent => { }
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "SensoryOrganDevelopment",
            // Gated on motility, not just Multicellularity: sensory organs are only selectively
            // useful once directed movement exists to act on sensory input — an undifferentiated
            // sessile cell gains nothing from vision it can't move in response to.
            Prerequisites = new[] { "MotilityEmergence" },
            IsEra1Event = true,
            // Whether to express: ResourceScarcity drives pre-predation sensing (find patchy food);
            // PredationPressure drives post-KingdomFork sensing (detect threats). Eat-count is the
            // origination floor — always available at low probability regardless of pressure.
            IsEligible = agent => (agent.LifetimeEats >= agent.sensoryGeneEatThreshold
                || (!agent.AcquiredGenes.Contains("KingdomFork") && agent.ResourceScarcity >= 0.55f)
                || (agent.AcquiredGenes.Contains("KingdomFork") && agent.PredationPressure >= 0.25f)
                || GeneEvolutionManager.SessionElapsed >= 60f) // time fallback: always fires ~27s into Era 1
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Acute Vision (narrow, high-precision)",
                    Apply = agent => agent.SetTraits(agent.visionTrait + 18f, agent.speedTrait, agent.strengthTrait,
                        agent.hardinessTrait, agent.temperaturePreference, agent.moisturePreference)
                },
                new GeneChoice
                {
                    Label = "Wide-Field Vision (broad, lower-precision)",
                    Apply = agent => agent.SetTraits(agent.visionTrait + 9f, agent.speedTrait, agent.strengthTrait,
                        agent.hardinessTrait, agent.temperaturePreference, agent.moisturePreference)
                }
            }
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "LocomotorAppendageDevelopment",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            IsEligible = agent => (agent.LifetimeEats >= agent.locomotorGeneEatThreshold
                || agent.StressLevel >= 35f
                || GeneEvolutionManager.SessionElapsed >= 75f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Fast Limbs (-> Speed)",
                    Apply = agent => agent.SetTraits(agent.visionTrait, agent.speedTrait + 18f, agent.strengthTrait,
                        agent.hardinessTrait, agent.temperaturePreference, agent.moisturePreference)
                },
                new GeneChoice
                {
                    Label = "Powerful Limbs (-> Strength)",
                    Apply = agent => agent.SetTraits(agent.visionTrait, agent.speedTrait, agent.strengthTrait + 18f,
                        agent.hardinessTrait, agent.temperaturePreference, agent.moisturePreference)
                }
            }
        });

        // ── Sexual DIFFERENTIATION (anisogamy → separate sexes) ──────────────────────────────
        // Real evolutionary order: sexes DIFFERENTIATE before sexual REPRODUCTION as we know it. This
        // event only splits the lineage into male/female — each adopter (and, via inheritance, its
        // offspring) is 50/50 male/female. It does NOT yet enable mate-based reproduction; that is the
        // separate, later ReproductiveStrategyShift event which REQUIRES this first. Gated to the
        // mid-late biological sub-phases (EraManager.CurrentEra ≥ 3) so sexes don't appear near
        // abiogenesis. TUNABLE era gate.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "SexualDifferentiation",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            IsEligible = agent => (EraManager.Instance == null || EraManager.Instance.CurrentEra >= 3)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice { Label = "Differentiate into separate sexes (male / female)", Apply = agent => agent.DifferentiateSex() },
                new GeneChoice { Label = "Remain undifferentiated (isogamous / asexual lineage)", Apply = agent => { /* stays undifferentiated; gene already marked acquired */ } }
            },
            // NPCs / late maturers differentiate by default so sexual reproduction can later emerge.
            DefaultAutoApply = agent => agent.DifferentiateSex()
        });

        // ── Sexual REPRODUCTION (mate-based trait blending) ──────────────────────────────────
        // Requires sexual DIFFERENTIATION first (separate sexes must exist before they can combine),
        // and fires MUCH later than it used to — gated to EraManager.CurrentEra ≥ 4 so a lineage
        // doesn't start pairing off almost immediately. Only differentiated organisms are eligible.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "ReproductiveStrategyShift",
            Prerequisites = new[] { "SexualDifferentiation" },
            IsEra1Event = true,
            // Red Queen: sex is favored under coevolving parasite pressure; age/time act as origination
            // floors. Differentiation + late era gate are the hard prerequisites.
            IsEligible = agent => agent.IsDifferentiated
                && (EraManager.Instance == null || EraManager.Instance.CurrentEra >= 4)
                && (agent.PathogenPressure >= 0.45f
                    || agent.AgeSeconds >= agent.reproductiveShiftAgeThreshold
                    || GeneEvolutionManager.SessionElapsed >= 120f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice { Label = "Remain Asexual (clone with mutation drift)", Apply = agent => agent.BecomeAsexual() },
                new GeneChoice { Label = "Adopt Sexual Reproduction (blend traits with a mate)", Apply = agent => agent.BecomeSexual() }
            },
            // Background agents that mature after the player has chosen default to staying asexual.
            DefaultAutoApply = agent => agent.BecomeAsexual()
        });

        // Sequential hermaphroditism: organism evolves the ability to switch sex in response
        // to local sex-ratio imbalance. Biologically, this trait emerged independently dozens
        // of times (clownfish, wrasses, parrotfish, many invertebrates) because it directly
        // solves the mating bottleneck that arises when one sex is locally scarce.
        // Eligibility: must be sexual and currently experiencing imbalance > 0.55 locally.
        // The 0.55 threshold means the event CAN fire at mild imbalance but is much more
        // likely to present itself when the player's community is genuinely struggling to find
        // mates (e.g. 80% of nearby agents are the same sex as the player's focal organism).
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "SequentialHermaphroditism",
            Prerequisites = new[] { "ReproductiveStrategyShift" },
            IsEra1Event = true,
            IsEligible = agent => agent.IsSexual
                && !agent.CanChangeSex
                && (agent.MateScarcity >= 0.65f || agent.LocalSexImbalance() > 0.55f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Evolve Sequential Hermaphroditism (switch sex when mates are scarce)",
                    Apply = agent => agent.BecomeSequentialHermaphrodite()
                },
                new GeneChoice
                {
                    Label = "Maintain Fixed Sex (simpler; relies on natural 50:50 ratio recovering)",
                    Apply = agent => { /* no change — gene event resolved without gaining capability */ }
                }
            },
            // Background organisms in imbalanced populations auto-evolve the capability —
            // it's a straightforward fitness gain when mates are scarce.
            DefaultAutoApply = agent =>
            {
                if (agent.LocalSexImbalance() > 0.7f) agent.BecomeSequentialHermaphrodite();
                // Below 0.7 imbalance: background agents don't auto-adopt (player gets the choice).
            }
        });

        // e1_tolerant_metabolism_emergence / e1_anoxic_refuge_speciation: OR-gate at the
        // Great Oxidation Event. Lineages must choose: adapt to the new oxidizing atmosphere
        // (Path A) or retreat permanently to anoxic refuges (Path B). Eligible on EITHER the
        // pre-existing GreatGasEventFired crisis OR rising O2 itself — previously this ONLY checked
        // GreatGasEventFired, an unrelated trigger that isn't guaranteed to correlate with O2
        // accumulation at all, meaning a world where O2 rose from photosynthesis without that
        // specific crisis ever firing had this real escape route (retreat to anoxic refuge) simply
        // unavailable — the actual real-world response to the GOE, now genuinely reachable.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "EfficientRespiration",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            IsEligible = agent => AtmosphereManager.Instance != null
                && (AtmosphereManager.Instance.GreatGasEventFired
                    || AtmosphereManager.Instance.GetFraction("O2") >= AgentController.AerobicUnlockO2Threshold)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Adapt to Oxidizing Atmosphere — develop efficient respiration (+50% starvation tolerance)",
                    Apply = agent => { agent.starvationTime *= 1.5f; }
                },
                new GeneChoice
                {
                    Label = "Retreat to Anoxic Refuges — specialize in oxygen-free niches (+Hardiness, -Speed; permanently restricted)",
                    Apply = agent =>
                    {
                        agent.SetAnoxicRefuge();
                        agent.SetTraits(agent.visionTrait, Mathf.Max(agent.speedTrait - 15f, 0f),
                            agent.strengthTrait, agent.hardinessTrait + 15f,
                            agent.temperaturePreference, agent.moisturePreference);
                    }
                }
            },
            DefaultAutoApply = agent => { agent.starvationTime *= 1.5f; }
        });

        // e1_phototrophic_innovation: evolve the ability to harvest stellar energy directly.
        // This is a soft-order event — can occur before or after multicellularity — but
        // requires enough chemical-pool cycling to have built the pre-cursor pigments.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "PhotosynthesisEmergence",
            Prerequisites = new[] { "Nucleus" },
            IsEra1Event = true,
            BaseMutationProbability = 0.03f, // MultiOrigin — photosynthesis arose independently many times
            // KingdomFork supersedes this: if the lineage already chose heterotrophy (or
            // any other metabolic direction) at KingdomFork, don't re-open the question.
            IsEligible = agent => agent.Metabolism == MetabolismType.Chemosynthetic
                && !agent.AcquiredGenes.Contains("KingdomFork")
                && (agent.LifetimeEats >= agent.sensoryGeneEatThreshold
                    || agent.ResourceScarcity >= 0.55f
                    || GeneEvolutionManager.SessionElapsed >= 65f) // time fallback
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice
                {
                    // Always offered to the player (IsAvailable unrestricted — an informed human can
                    // still gamble on it). FitnessGate blocks autonomous NPC/mutation-origin paths
                    // from picking it on a world where it wouldn't actually pay off.
                    Label = "Evolve Photosynthesis (harvest stellar energy; drives the Great Gas Event)",
                    Apply = agent => agent.BecomePhototrophic(),
                    FitnessGate = agent => agent.IsPhotosynthesisLocallyViable()
                },
                new GeneChoice
                {
                    Label = "Remain Chemosynthetic (vent chemistry; stable but spatially constrained)",
                    Apply = agent => { /* stays chemosynthetic */ }
                }
            },
            // NPC/background agents only auto-convert if photosynthesis would actually be
            // energy-positive HERE (real local star luminosity, orbit distance, atmosphere, depth).
            // Previously this fired unconditionally, so on a light-starved world (e.g. a
            // near-zero-luminosity red dwarf) entire populations converted en masse to a metabolism
            // that couldn't sustain them, abandoning a working chemosynthetic energy source for
            // nothing — the direct cause of at least one observed near-total ecosystem collapse.
            DefaultAutoApply = agent => { if (agent.IsPhotosynthesisLocallyViable()) agent.BecomePhototrophic(); }
        });

        // ── Respiration evolutionary sequence (respiration-evolutionary-sequence-fix-spec) ──────
        // Real-world reference: anaerobic metabolism → anoxygenic photosynthesis → oxygenic
        // photosynthesis → atmospheric O2 accumulation (Great Oxidation Event) → aerobic respiration.
        // Every organism SPAWNS at RespirationTier.Primitive (a low-efficiency generic anaerobic
        // metabolism — see GibbsFactor's tier multiplier), not pre-assigned a fully-efficient gas
        // pair. The genes below are the OR-gate branch point: multiple independent, real anaerobic
        // strategies exist in parallel, each keyed to a different locally-available substrate, so
        // different lineages under different local chemistry can genuinely diverge — replacing the
        // old single AlternativeRespirationPathway swap-gene. Minimum substrate fraction (5%) keeps
        // a branch from offering itself on a world where that gas barely exists.
        const float SubstrateViableFraction = 0.05f; // TUNABLE — "present in meaningful quantity"

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "Methanogenesis",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            BaseMutationProbability = 0.04f, // MultiOrigin — real anaerobic strategy, evolved independently many times
            IsEligible = agent => agent.RespirationTier == RespirationTier.Primitive
                && AtmosphereManager.Instance != null
                && AtmosphereManager.Instance.GetFraction("H2") >= SubstrateViableFraction,
            Choices = new[]
            {
                new GeneChoice { Label = "Evolve Methanogenesis (H2 → CH4) — an efficient anaerobic pathway exploiting locally abundant hydrogen",
                    Apply = agent => agent.SpecializeAnaerobic("H2", "CH4", "Methanogenesis") },
                new GeneChoice { Label = "Remain Undifferentiated — stay on the primitive, low-efficiency baseline metabolism",
                    Apply = agent => { /* stays Primitive */ } }
            },
            DefaultAutoApply = agent => agent.SpecializeAnaerobic("H2", "CH4", "Methanogenesis")
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "SulfurRespiration",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            BaseMutationProbability = 0.04f,
            IsEligible = agent => agent.RespirationTier == RespirationTier.Primitive
                && AtmosphereManager.Instance != null
                && (AtmosphereManager.Instance.GetFraction("SO2") >= SubstrateViableFraction
                    || AtmosphereManager.Instance.GetFraction("H2S") >= SubstrateViableFraction),
            Choices = new[]
            {
                new GeneChoice { Label = "Evolve Sulfur Respiration — specialize into whichever sulfur compound (SO2/H2S) is locally richer",
                    Apply = agent =>
                    {
                        float so2 = AtmosphereManager.Instance?.GetFraction("SO2") ?? 0f;
                        float h2s = AtmosphereManager.Instance?.GetFraction("H2S") ?? 0f;
                        if (so2 >= h2s) agent.SpecializeAnaerobic("SO2", "H2S", "SulfurRespiration");
                        else agent.SpecializeAnaerobic("H2S", "SO2", "SulfurRespiration");
                    } },
                new GeneChoice { Label = "Remain Undifferentiated — stay on the primitive, low-efficiency baseline metabolism",
                    Apply = agent => { /* stays Primitive */ } }
            },
            DefaultAutoApply = agent =>
            {
                float so2 = AtmosphereManager.Instance?.GetFraction("SO2") ?? 0f;
                float h2s = AtmosphereManager.Instance?.GetFraction("H2S") ?? 0f;
                if (so2 >= h2s) agent.SpecializeAnaerobic("SO2", "H2S", "SulfurRespiration");
                else agent.SpecializeAnaerobic("H2S", "SO2", "SulfurRespiration");
            }
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "NitrogenRespiration",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            BaseMutationProbability = 0.03f,
            IsEligible = agent => agent.RespirationTier == RespirationTier.Primitive
                && AtmosphereManager.Instance != null
                && AtmosphereManager.Instance.GetFraction("NH3") >= SubstrateViableFraction,
            Choices = new[]
            {
                new GeneChoice { Label = "Evolve Nitrogen Respiration (NH3 → N2) — exploit locally abundant ammonia",
                    Apply = agent => agent.SpecializeAnaerobic("NH3", "N2", "NitrogenRespiration") },
                new GeneChoice { Label = "Remain Undifferentiated — stay on the primitive, low-efficiency baseline metabolism",
                    Apply = agent => { /* stays Primitive */ } }
            },
            DefaultAutoApply = agent => agent.SpecializeAnaerobic("NH3", "N2", "NitrogenRespiration")
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "PhosphineRespiration",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            BaseMutationProbability = 0.03f,
            // Shares the H2 substrate check with Methanogenesis by design — an H2-rich world can
            // support multiple simultaneously-viable anaerobic strategies, and different lineages
            // genuinely diverging between them (rather than converging on one) is the whole point
            // of this being an OR-gate branch instead of a single deterministic path.
            IsEligible = agent => agent.RespirationTier == RespirationTier.Primitive
                && AtmosphereManager.Instance != null
                && AtmosphereManager.Instance.GetFraction("H2") >= SubstrateViableFraction,
            Choices = new[]
            {
                new GeneChoice { Label = "Evolve Phosphine Respiration (H2 → PH3) — an alternative hydrogen-exploiting pathway",
                    Apply = agent => agent.SpecializeAnaerobic("H2", "PH3", "PhosphineRespiration") },
                new GeneChoice { Label = "Remain Undifferentiated — stay on the primitive, low-efficiency baseline metabolism",
                    Apply = agent => { /* stays Primitive */ } }
            },
            DefaultAutoApply = agent => agent.SpecializeAnaerobic("H2", "PH3", "PhosphineRespiration")
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "HalideMetalRespiration",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            BaseMutationProbability = 0.03f,
            IsEligible = agent => agent.RespirationTier == RespirationTier.Primitive
                && !agent.WouldGasBeLethal("F2") // F2 is lethal to Carbon/Boron/Nitrogen/Phosphorus/Sulfur backbones
                && AtmosphereManager.Instance != null
                && AtmosphereManager.Instance.GetFraction("F2") >= SubstrateViableFraction,
            Choices = new[]
            {
                new GeneChoice { Label = "Evolve Halide-Metal Respiration (F2 → SiF4) — exploit locally abundant fluorine",
                    Apply = agent => agent.SpecializeAnaerobic("F2", "SiF4", "HalideMetalRespiration") },
                new GeneChoice { Label = "Remain Undifferentiated — stay on the primitive, low-efficiency baseline metabolism",
                    Apply = agent => { /* stays Primitive */ } }
            },
            DefaultAutoApply = agent => agent.SpecializeAnaerobic("F2", "SiF4", "HalideMetalRespiration")
        });

        // Issue 3: aerobic respiration — a late, atmosphere-gated unlock causally downstream of
        // accumulated PhotosynthesisEmergence activity (which produces the O2 this gate reads), NOT
        // available from tick one and NOT dependent on any specific anaerobic branch above — it
        // represents a genuinely new capability, not a swap. Real aerobic respiration's substantially
        // higher ATP yield (AerobicYieldMultiplier in GibbsFactor) is what should drive it toward
        // eventual dominance once unlocked, mirroring its actual historical trajectory.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "AerobicRespiration",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            BaseMutationProbability = 0.06f, // MultiOrigin, higher than the anaerobic branches — a strong, real fitness win once available
            IsEligible = agent => agent.RespirationTier != RespirationTier.Aerobic
                && !agent.WouldGasBeLethal("O2") // Silicon/Germanium/Tin/Boron/Phosphorus backbones: O2 is lethal, not a fitness win — their real escape is EfficientRespiration's anoxic-refuge path
                && AtmosphereManager.Instance != null
                && AtmosphereManager.Instance.GetFraction("O2") >= AgentController.AerobicUnlockO2Threshold,
            Choices = new[]
            {
                new GeneChoice { Label = "Evolve Aerobic Respiration (O2 → CO2) — dramatically higher energy yield now that atmospheric O2 has accumulated",
                    Apply = agent => agent.BecomeAerobic() },
                new GeneChoice { Label = "Remain Anaerobic — forgo the yield advantage; oxygen exposure will carry a rising toxicity cost",
                    Apply = agent => { /* stays anaerobic; ComputeOxygenToxicityCost now applies */ } }
            },
            DefaultAutoApply = agent => agent.BecomeAerobic() // NPCs take the strictly-better option once available, same as PhotosynthesisEmergence's pattern
        });

        // e1_thermal_tolerance_expansion: widen the survivable temperature band. MultiOrigin —
        // thermal tolerance evolved independently across countless lineages. Driven by ACTUAL thermal
        // stress (StressLevel folds in climate/thermal-cycle discomfort), not a bare timer, so a
        // lineage under climate pressure is far more likely to fix the tolerance-widening mutation —
        // giving climate crises a real adaptive response instead of flat unaffected survival.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "ThermalToleranceExpansion",
            Prerequisites = new string[0], // foundational: baseline organisms have SOME tolerance; this widens it
            IsEra1Event = true,
            BaseMutationProbability = 0.04f, // MultiOrigin — arises independently per lineage at reproduction
            IsEligible = agent => (agent.StressLevel >= 35f || GeneEvolutionManager.SessionElapsed >= 80f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Broaden Thermal Tolerance — widen the survivable temperature band (+Hardiness, +thermal-cycle tolerance)",
                    Apply = agent => agent.ExpandThermalTolerance()
                },
                new GeneChoice
                {
                    Label = "Specialize to Current Climate — stay narrowly adapted (lower overhead, higher risk if climate shifts)",
                    Apply = agent => { /* no change */ }
                }
            },
            // Background lineages broaden tolerance automatically only when genuinely stressed.
            DefaultAutoApply = agent => { if (agent.StressLevel >= 45f) agent.ExpandThermalTolerance(); }
        });

        // e1_motility_emergence: the transition from passive drifter to self-directed mover.
        // Fires once the organism has established itself (Era 1+ and enough reproduction
        // cycles). Sessile lineages that skip motility can never become consumers.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "MotilityEmergence",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            BaseMutationProbability = 0.03f, // MultiOrigin — directed motility arose independently across lineages
            IsEligible = agent => (EraManager.Instance == null || EraManager.Instance.CurrentEra >= 1)
                && (agent.LifetimeEats >= agent.locomotorGeneEatThreshold
                    || agent.ResourceScarcity >= 0.50f
                    || GeneEvolutionManager.SessionElapsed >= 80f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Develop Flagellar Motility (self-directed movement — prerequisite for predation)",
                    Apply = agent => agent.BecomeMotile()
                },
                new GeneChoice
                {
                    Label = "Remain Sessile (wind-dispersed; lower energy cost, no active foraging)",
                    Apply = agent => agent.RemainSessile()
                }
            },
            DefaultAutoApply = agent => agent.BecomeMotile()
        });

        // ── Land Colonization (the missing land/sea axis) ─────────────────────────────────────
        // Before this, EVERY organism was permanently aquatic — _isAquatic was never flipped anywhere,
        // so "land species" never really existed (an agent standing on dry ground was still counted as
        // aquatic, just taking a desiccation penalty). This is the real, heritable habitat transition —
        // requires motility (you can't colonize land by drifting) and enough time/exposure that the
        // lineage has plausibly been spending time out of the water. Can fire any time in Era 1 or 2.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "LandColonization",
            Prerequisites = new[] { "MotilityEmergence" },
            IsEra1Event = true,
            BaseMutationProbability = 0.02f, // MultiOrigin — terrestrialization arose independently many times
            IsEligible = agent => agent.IsAquatic && agent.HasMotility
                && (agent.CurrentMedium == HabitatMedium.Land || agent.ResourceScarcity >= 0.5f
                    || GeneEvolutionManager.SessionElapsed >= 100f),
            Choices = new[]
            {
                new GeneChoice { Label = "Colonize Land (leave the water; moisture tolerance shifts dry)", Apply = agent => agent.ColonizeLand() },
                new GeneChoice { Label = "Remain Aquatic (stay in the water)", Apply = agent => { } },
            },
            // NPCs default to staying aquatic — colonizing land is a deliberate, less-common branch,
            // not the safe default (an aquatic organism forced onto land without adaptation risks
            // desiccation same as before this gene existed).
            DefaultAutoApply = agent => { }
        });

        // Reverse transition: much rarer than the forward one (real precedent: cetaceans, pinnipeds,
        // sea snakes re-adapting to water after their ancestors left it).
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "ReturnToSea",
            Prerequisites = new[] { "LandColonization" },
            IsEra1Event = true,
            BaseMutationProbability = 0.004f, // much rarer than the forward transition
            IsEligible = agent => !agent.IsAquatic && agent.HasMotility
                && (agent.CurrentMedium == HabitatMedium.Sea && agent.ResourceScarcity >= 0.6f),
            Choices = new[]
            {
                new GeneChoice { Label = "Return to the Sea (re-adapt to an aquatic existence)", Apply = agent => agent.ReturnToSea() },
                new GeneChoice { Label = "Remain Terrestrial", Apply = agent => { } },
            },
            DefaultAutoApply = agent => { }
        });

        // Manipulation appendage development: evolve increasingly dexterous structures.
        // Gated behind motility (you need to move to develop limbs) and Minimal-Replicator Seas+.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "ManipulationAppendageDevelopment",
            Prerequisites = new[] { "MotilityEmergence" },
            IsEra1Event = true,
            IsEligible = agent => agent.Manipulation == ManipulationLevel.None
                && (EraManager.Instance == null || EraManager.Instance.CurrentEra >= 1)
                && (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 2 || agent.StressLevel >= 40f
                    || GeneEvolutionManager.SessionElapsed >= 110f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Evolve Simple Appendages (cilia, pseudopods — push/pull, no grip)",
                    Apply = agent => { agent.SetManipulation(ManipulationLevel.Simple);
                                       agent.SetNeuralComplexity(NeuralComplexityStage.NerveNet); }
                },
                new GeneChoice
                {
                    Label = "Remain Without Manipulators (lower metabolic cost, no tool potential)",
                    Apply = agent => { /* no change */ }
                }
            },
            DefaultAutoApply = agent => { agent.SetManipulation(ManipulationLevel.Simple);
                                          agent.SetNeuralComplexity(NeuralComplexityStage.NerveNet); }
        });

        // Articulated appendages: upgrade from simple cilia/pseudopods to limbs/tentacles.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "ArticulatedAppendages",
            Prerequisites = new[] { "ManipulationAppendageDevelopment" },
            IsEra1Event = true,
            IsEligible = agent => agent.Manipulation == ManipulationLevel.Simple
                && (EraManager.Instance == null || EraManager.Instance.CurrentEra >= 3) // Compartmentalized Cells+
                && (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 4 || agent.StressLevel >= 45f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Develop Articulated Limbs (tentacles, limb-buds — crude tool use)",
                    Apply = agent => { agent.SetManipulation(ManipulationLevel.Articulated);
                                       agent.SetNeuralComplexity(NeuralComplexityStage.NerveCord); }
                },
                new GeneChoice
                {
                    Label = "Retain Simple Appendages (lower cost, sufficient for current needs)",
                    Apply = agent => { /* no change */ }
                }
            },
            DefaultAutoApply = agent => agent.SetManipulation(ManipulationLevel.Articulated)
        });

        // Dexterous appendages: grasping digits — prerequisite for Era 2 tool use.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "DexterousAppendages",
            Prerequisites = new[] { "ArticulatedAppendages" },
            IsEra1Event = true,
            IsEligible = agent => agent.Manipulation == ManipulationLevel.Articulated
                && (EraManager.Instance == null || EraManager.Instance.CurrentEra >= 4) // Multicellularity+
                && (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 6 || agent.StressLevel >= 50f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Develop Dexterous Digits (prehensile grasping — precision manipulation, Era 2 tool use)",
                    Apply = agent => { agent.SetManipulation(ManipulationLevel.Dexterous);
                                       agent.SetNeuralComplexity(NeuralComplexityStage.GanglionicCephalization); }
                },
                new GeneChoice
                {
                    Label = "Retain Articulated Appendages (stable, lower neural overhead)",
                    Apply = agent => { /* no change */ }
                }
            },
            DefaultAutoApply = agent => { /* NPCs rarely reach this tier automatically */ }
        });

        // Sociality emergence: transition from solitary existence to collective behavior.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "SocialityEmergence",
            Prerequisites = new[] { "MotilityEmergence" },
            IsEra1Event = true,
            IsEligible = agent => agent.Sociality == SocialityBaseline.Solitary
                && (EraManager.Instance == null || EraManager.Instance.CurrentEra >= 2) // GOE+
                && (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 3 || agent.StressLevel >= 40f
                    || GeneEvolutionManager.SessionElapsed >= 120f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Develop Aggregating Behavior (passive clustering via quorum signals — density defense)",
                    Apply = agent => agent.SetSociality(SocialityBaseline.Aggregating)
                },
                new GeneChoice
                {
                    Label = "Remain Solitary (optimal for specialist niches; lower coordination overhead)",
                    Apply = agent => { /* no change */ }
                }
            },
            DefaultAutoApply = agent => agent.SetSociality(SocialityBaseline.Aggregating)
        });

        // Group formation: upgrade from passive aggregation to active cohesion and role division.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "GroupFormation",
            Prerequisites = new[] { "SocialityEmergence" },
            IsEra1Event = true,
            IsEligible = agent => agent.Sociality == SocialityBaseline.Aggregating
                && (EraManager.Instance == null || EraManager.Instance.CurrentEra >= 4) // Multicellularity+
                && (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 5 || agent.StressLevel >= 45f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Develop Group Formation (coordinated behavior, role specialization — Era 2 social architecture)",
                    Apply = agent => agent.SetSociality(SocialityBaseline.GroupForming)
                },
                new GeneChoice
                {
                    Label = "Retain Aggregating Behavior (simpler, sufficient for current environment)",
                    Apply = agent => { /* no change */ }
                }
            },
            DefaultAutoApply = agent => { /* rare auto-progression — mostly player-driven */ }
        });

        // Mixotrophy: combine photosynthesis + heterotrophy at reduced efficiency.
        // Requires photosynthesis already established; GOE+ (need O2 or equivalent for the dual pathway).
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "Mixotrophy",
            Prerequisites = new[] { "PhotosynthesisEmergence" },
            IsEra1Event = true,
            IsEligible = agent => agent.Metabolism == MetabolismType.Phototrophic
                && (EraManager.Instance == null || EraManager.Instance.CurrentEra >= 2)
                && (agent.LifetimeEats >= agent.sensoryGeneEatThreshold * 2 || agent.StressLevel >= 40f
                    || GeneEvolutionManager.SessionElapsed >= 130f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Develop Mixotrophy (both photosynthesis + heterotrophy at 70% efficiency — flexible fallback)",
                    Apply = agent => agent.BecomeMixotrophic()
                },
                new GeneChoice
                {
                    Label = "Remain Exclusively Phototrophic (full efficiency; no heterotrophic capability)",
                    Apply = agent => { /* no change */ }
                }
            },
            DefaultAutoApply = agent => { /* rare — mixotrophy is a player strategic choice */ }
        });

        // ── Era 1 Body Plan chain ─────────────────────────────────────────────────

        // e1_structural_spicule_support: simple mineral/fibrous elements for body rigidity —
        // sponge spicules, coccolithophore tests. Pre-predation; not defense-motivated.
        // Auto-apply — no player decision required, it's a passive metabolic investment.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "StructuralSpiculeSupport",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            IsEligible = agent => (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 2 || agent.StressLevel >= 35f
                    || GeneEvolutionManager.SessionElapsed >= 100f)
                && (EraManager.Instance == null || EraManager.Instance.CurrentEra >= 2)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            AutoApply = agent =>
            {
                agent.SetTraits(agent.visionTrait, agent.speedTrait, agent.strengthTrait,
                    agent.hardinessTrait + 5f, agent.temperaturePreference, agent.moisturePreference);
            }
        });

        // e1_germ_layer_complexity: diploblastic → triploblastic tissue organization.
        // Hard prerequisite for endoskeleton only; exoskeleton and shell don't require it.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "GermLayerComplexity",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            // Era gate lowered from >=3 to >=1: germ-layer differentiation is foundational tissue
            // organization that specialized structural genes (ProtectiveStructureEmergence's
            // endoskeleton option) are built from, so it needs to resolve on a comparable or
            // earlier timescale — a >=3 floor was making it fire well after ProtectiveStructureEmergence
            // (which has no era gate), backwards from the intended dependency order.
            IsEligible = agent => (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 3
                    || agent.StressLevel >= 40f
                    || agent.PredationPressure >= 0.30f) // predation weakly promotes tissue-layer complexity
                && (EraManager.Instance == null || EraManager.Instance.CurrentEra >= 1)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Develop Three Tissue Layers (triploblastic — enables endoskeleton, complex organs; +Hardiness)",
                    Apply = agent =>
                    {
                        agent.SetGermLayers();
                        agent.SetTraits(agent.visionTrait, agent.speedTrait, agent.strengthTrait,
                            agent.hardinessTrait + 3f, agent.temperaturePreference, agent.moisturePreference);
                    }
                },
                new GeneChoice
                {
                    Label = "Retain Two Tissue Layers (diploblastic — lower metabolic cost; endoskeleton path unavailable)",
                    Apply = agent => { /* stays diploblastic */ }
                }
            },
            DefaultAutoApply = agent =>
            {
                agent.SetGermLayers();
                agent.SetTraits(agent.visionTrait, agent.speedTrait, agent.strengthTrait,
                    agent.hardinessTrait + 3f, agent.temperaturePreference, agent.moisturePreference);
            }
        });

        // e1_protective_structure_emergence: body-plan fork after predation emerges.
        // Satisfies the protective-structure AND-gate required for DiversificationExplosion.
        // All four options are valid; endoskeleton requires HasGermLayers (triploblastic).
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "ProtectiveStructureEmergence",
            Prerequisites = new[] { "KingdomFork" },
            IsEra1Event = true,
            // Vermeij's escalation hypothesis: armor emerges specifically in response to predation
            // intensity, not general hardship. PredationPressure is the correct fixation driver.
            IsEligible = agent => agent.HasMotility
                && (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 4
                    || agent.PredationPressure >= 0.25f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Exoskeleton — hardened cuticle (+Strength, +Hardiness, -Speed; jointed-limb leverage)",
                    Apply = agent =>
                    {
                        agent.SetBodyPlan(BodyPlanType.Exoskeleton);
                        agent.SetTraits(agent.visionTrait, Mathf.Max(agent.speedTrait - 8f, 0f),
                            agent.strengthTrait + 15f, agent.hardinessTrait + 10f,
                            agent.temperaturePreference, agent.moisturePreference);
                    }
                },
                new GeneChoice
                {
                    Label = "Shell — mineralized test (++Hardiness, --Speed; continuous growth, no molt risk)",
                    Apply = agent =>
                    {
                        agent.SetBodyPlan(BodyPlanType.Shell);
                        agent.SetTraits(agent.visionTrait, Mathf.Max(agent.speedTrait - 15f, 0f),
                            agent.strengthTrait, agent.hardinessTrait + 20f,
                            agent.temperaturePreference, agent.moisturePreference);
                    }
                },
                new GeneChoice
                {
                    Label = "Endoskeleton — internal skeleton (++Strength, +Speed; vertebrate ancestor — largest body sizes)",
                    IsAvailable = agent => agent.HasGermLayers,
                    Apply = agent =>
                    {
                        agent.SetBodyPlan(BodyPlanType.Endoskeleton);
                        agent.SetTraits(agent.visionTrait, agent.speedTrait + 5f,
                            agent.strengthTrait + 20f, agent.hardinessTrait,
                            agent.temperaturePreference, agent.moisturePreference);
                    }
                },
                new GeneChoice
                {
                    Label = "Remain Soft-Bodied — invest in speed and evasion (+Speed; metabolically cheap, no armor)",
                    Apply = agent =>
                    {
                        agent.SetBodyPlan(BodyPlanType.SoftBody);
                        agent.SetTraits(agent.visionTrait, agent.speedTrait + 10f,
                            agent.strengthTrait, agent.hardinessTrait,
                            agent.temperaturePreference, agent.moisturePreference);
                    }
                }
            },
            DefaultAutoApply = agent =>
            {
                float roll = Random.value;
                if (roll < 0.40f)
                {
                    agent.SetBodyPlan(BodyPlanType.Exoskeleton);
                    agent.SetTraits(agent.visionTrait, Mathf.Max(agent.speedTrait - 8f, 0f),
                        agent.strengthTrait + 15f, agent.hardinessTrait + 10f,
                        agent.temperaturePreference, agent.moisturePreference);
                }
                else if (roll < 0.65f)
                {
                    agent.SetBodyPlan(BodyPlanType.Shell);
                    agent.SetTraits(agent.visionTrait, Mathf.Max(agent.speedTrait - 15f, 0f),
                        agent.strengthTrait, agent.hardinessTrait + 20f,
                        agent.temperaturePreference, agent.moisturePreference);
                }
                else if (roll < 0.82f && agent.HasGermLayers)
                {
                    agent.SetBodyPlan(BodyPlanType.Endoskeleton);
                    agent.SetTraits(agent.visionTrait, agent.speedTrait + 5f,
                        agent.strengthTrait + 20f, agent.hardinessTrait,
                        agent.temperaturePreference, agent.moisturePreference);
                }
                else
                {
                    agent.SetBodyPlan(BodyPlanType.SoftBody);
                    agent.SetTraits(agent.visionTrait, agent.speedTrait + 10f,
                        agent.strengthTrait, agent.hardinessTrait,
                        agent.temperaturePreference, agent.moisturePreference);
                }
            }
        });

        // appearance-generation-spec §2.7 — the three previously-unspecified M3 structural-support
        // branches. Automatic (no player choice, matching the spec's own framing that these are
        // rare/crisis-driven or hard-gated at chemistry level, not a normal decision point), each an
        // OR-gate sibling/follow-on of the ProtectiveStructureEmergence fork above, not a new
        // prerequisite chain.

        // e1_endoskeleton_mineralization: endo-cartilage's mineralized upgrade. Requires the
        // cartilage precursor already present; driven by sustained large size OR a high-gravity
        // world — the same Kleiber-scaling size-pressure logic already governing body-size limits
        // elsewhere, applied to why a body would bother mineralizing an internal skeleton at all.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "e1_endoskeleton_mineralization",
            Prerequisites = new[] { "ProtectiveStructureEmergence" },
            IsEra1Event = true,
            IsEligible = agent => agent.BodyPlan == BodyPlanType.Endoskeleton
                && (agent.strengthTrait >= 60f
                    || (PlanetGravity.Instance != null && PlanetGravity.Instance.SurfaceGravityMs2 >= 11f))
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            AutoApply = agent =>
            {
                agent.SetBodyPlan(BodyPlanType.EndoMineralized);
                agent.SetTraits(agent.visionTrait, agent.speedTrait,
                    agent.strengthTrait + 10f, agent.hardinessTrait + 8f,
                    agent.temperaturePreference, agent.moisturePreference);
            }
        });

        // e1_mixed_armor_emergence: dermal ossification layered over an existing exoskeleton (or the
        // reverse) — genuinely crisis-driven, not a normal branch, gated on EXTREME sustained
        // predation well above ProtectiveStructureEmergence's own 0.25 origination threshold.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "e1_mixed_armor_emergence",
            Prerequisites = new[] { "ProtectiveStructureEmergence" },
            IsEra1Event = true,
            IsEligible = agent => (agent.BodyPlan == BodyPlanType.Exoskeleton || agent.BodyPlan == BodyPlanType.Shell)
                && agent.PredationPressure >= 0.6f
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            AutoApply = agent =>
            {
                agent.SetBodyPlan(BodyPlanType.MixedArmor);
                agent.SetTraits(agent.visionTrait, Mathf.Max(agent.speedTrait - 5f, 0f),
                    agent.strengthTrait + 5f, agent.hardinessTrait + 15f,
                    agent.temperaturePreference, agent.moisturePreference);
            }
        });

        // e1_crystalline_lattice_emergence: hard-gated by world/backbone generation at world-seed
        // time, not a lineage choice — no other branch can reach this value, and a silicon-backbone
        // lineage has no reason NOT to express it (there is no competing soft-bodied silicon
        // strategy in this design), so it simply supersedes whatever ProtectiveStructureEmergence
        // chose the moment backbone chemistry makes it available.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "e1_crystalline_lattice_emergence",
            Prerequisites = new[] { "ProtectiveStructureEmergence" },
            IsEra1Event = true,
            IsEligible = agent => agent.Backbone == BackboneElement.Silicon
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            AutoApply = agent =>
            {
                agent.SetBodyPlan(BodyPlanType.Crystalline);
                agent.SetTraits(agent.visionTrait, agent.speedTrait,
                    agent.strengthTrait + 8f, agent.hardinessTrait + 25f,
                    agent.temperaturePreference, agent.moisturePreference);
            }
        });

        // ── Era 1 remaining morphological axes (appearance-generation-spec §2.2/§2.8) ──────────
        // M2 Segmentation, M6 Primary Sensory Modality, M9 Feeding Apparatus, M10 Integument
        // Elaboration, M5 limb-pair differentiation, vocal apparatus, and the two rare M1
        // non-bilaterian symmetry branches (Biradial/ColonialModular). Same OR-gated, contingent,
        // order-sensitive resolution as the rest of Era 1 — no forced deterministic sequence.

        // e1_metameric_segmentation: repeated body segments. Requires triploblastic tissue
        // organization (the same GermLayerComplexity prerequisite the endoskeleton path uses) —
        // true repeated segmentation is a tissue-organization achievement, not a surface pattern.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "e1_metameric_segmentation",
            Prerequisites = new[] { "GermLayerComplexity" },
            IsEra1Event = true,
            IsEligible = agent => agent.HasGermLayers && agent.Segmentation == SegmentationType.Unsegmented
                && (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 3 || agent.StressLevel >= 35f
                    || GeneEvolutionManager.SessionElapsed >= 115f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice { Label = "Develop Metameric Segmentation (repeated body segments — enables later tagmatization)",
                    Apply = agent => agent.SetSegmentation(SegmentationType.Metameric) },
                new GeneChoice { Label = "Remain Unsegmented (lower developmental cost)",
                    Apply = agent => { /* no change */ } },
            },
            DefaultAutoApply = agent => agent.SetSegmentation(SegmentationType.Metameric)
        });

        // e1_tagmatization: segments fuse into specialized functional regions (head/thorax/abdomen
        // equivalent). Requires differentiated appendages (arthropod tagmata evolved alongside
        // jointed limbs, not independently of them).
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "e1_tagmatization",
            Prerequisites = new[] { "e1_metameric_segmentation", "ArticulatedAppendages" },
            IsEra1Event = true,
            IsEligible = agent => agent.Segmentation == SegmentationType.Metameric
                && agent.Manipulation >= ManipulationLevel.Articulated
                && (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 5 || agent.StressLevel >= 45f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice { Label = "Develop Tagmatization (fused functional body regions — specialized locomotion/feeding/reproduction zones)",
                    Apply = agent => agent.SetSegmentation(SegmentationType.Tagmatized) },
                new GeneChoice { Label = "Retain Uniform Segmentation (simpler, sufficient for current niche)",
                    Apply = agent => { /* no change */ } },
            },
            DefaultAutoApply = agent => { /* rare auto-progression — mostly player-driven, matches GroupFormation's tier */ }
        });

        // e1_segment_simplification: rare reversion — a segmented ancestor's small-bodied descendant
        // smooths its segmentation back out (real precedent: secondarily-simplified segmentation in
        // miniaturized derived lineages). Much rarer than the forward transitions, matching ReturnToSea.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "e1_segment_simplification",
            Prerequisites = new[] { "e1_metameric_segmentation" },
            IsEra1Event = true,
            BaseMutationProbability = 0.006f,
            IsEligible = agent => (agent.Segmentation == SegmentationType.Metameric || agent.Segmentation == SegmentationType.Tagmatized)
                && agent.BodySizeClass <= SizeClass.Small
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            AutoApply = agent => agent.SetSegmentation(SegmentationType.SecondarilySimplified)
        });

        // e1_primary_sensory_modality: which sense predominates, refining the generic vision-boost
        // choice from SensoryOrganDevelopment into a real dominant-modality axis. OR-gated siblings,
        // matching the M3 catalog pattern — one card, mutually exclusive choices, per-choice
        // preconditions rather than five separate always-eligible events.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "e1_primary_sensory_modality",
            Prerequisites = new[] { "SensoryOrganDevelopment" },
            IsEra1Event = true,
            IsEligible = agent => agent.PrimarySense == SensoryModality.Chemosensory
                && (agent.LifetimeEats >= agent.sensoryGeneEatThreshold * 3 || agent.StressLevel >= 35f
                    || GeneEvolutionManager.SessionElapsed >= 120f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice { Label = "Visual — image-forming eyes (requires developed vision)",
                    IsAvailable = agent => agent.visionTrait >= 40f,
                    Apply = agent => agent.SetPrimarySense(SensoryModality.Visual) },
                new GeneChoice { Label = "Mechanosensory — vibration/pressure detection (aquatic-favored)",
                    Apply = agent => agent.SetPrimarySense(SensoryModality.Mechanosensory) },
                new GeneChoice { Label = "Electroreceptive — bioelectric field detection (aquatic specialist)",
                    IsAvailable = agent => agent.IsAquatic,
                    Apply = agent => agent.SetPrimarySense(SensoryModality.Electroreceptive) },
                new GeneChoice { Label = "Thermoreceptive — heat-gradient detection (favored in high thermal-variance habitats)",
                    Apply = agent => agent.SetPrimarySense(SensoryModality.Thermoreceptive) },
                new GeneChoice { Label = "Magnetoreceptive — geomagnetic-field detection (long-range dispersal)",
                    IsAvailable = agent => agent.HasMotility,
                    Apply = agent => agent.SetPrimarySense(SensoryModality.Magnetoreceptive) },
                new GeneChoice { Label = "Remain Chemosensory — taste/smell gradients only (lowest cost, no change)",
                    Apply = agent => { /* no change */ } },
            },
            DefaultAutoApply = agent => { /* NPCs keep the chemosensory default unless player-driven */ }
        });

        // e1_multimodal_sensory_integration: ceiling value, reached only after 2+ modalities already
        // acquired — auto-apply, no player decision (an emergent integration, not a fresh choice).
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "e1_multimodal_sensory_integration",
            Prerequisites = new[] { "e1_primary_sensory_modality" },
            IsEra1Event = true,
            IsEligible = agent => agent.AcquiredSenseCount >= 2 && agent.PrimarySense != SensoryModality.Multimodal
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            AutoApply = agent => agent.SetPrimarySense(SensoryModality.Multimodal)
        });

        // e1_feeding_apparatus_specialization: HOW food is physically taken in, distinct from
        // MetabolismType (the energy-source axis) — a Heterotrophic organism can be a grazer, a
        // detritivore, an active predator, or (rarely, under extreme scarcity) a parasite.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "e1_feeding_apparatus_specialization",
            Prerequisites = new[] { "KingdomFork" },
            IsEra1Event = true,
            IsEligible = agent => agent.Feeding == FeedingApparatus.FilterPassive
                && (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 3 || agent.StressLevel >= 35f
                    || GeneEvolutionManager.SessionElapsed >= 140f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice { Label = "Active Predation (chase and consume prey)",
                    IsAvailable = agent => agent.HasMotility && (agent.Metabolism == MetabolismType.Heterotrophic || agent.Metabolism == MetabolismType.Mixotrophic),
                    Apply = agent => agent.SetFeedingApparatus(FeedingApparatus.PredatorActive) },
                new GeneChoice { Label = "Grazing (steady consumption of abundant, low-defense food)",
                    IsAvailable = agent => agent.Metabolism == MetabolismType.Heterotrophic || agent.Metabolism == MetabolismType.Mixotrophic,
                    Apply = agent => agent.SetFeedingApparatus(FeedingApparatus.Grazer) },
                new GeneChoice { Label = "Detritivory (consume dead organic matter — low-competition niche)",
                    Apply = agent => agent.SetFeedingApparatus(FeedingApparatus.Detritivore) },
                new GeneChoice { Label = "Parasitic Feeding (exploit host organisms directly — high-scarcity environments)",
                    IsAvailable = agent => agent.Metabolism == MetabolismType.Heterotrophic && agent.ResourceScarcity >= 0.8f,
                    Apply = agent => agent.SetFeedingApparatus(FeedingApparatus.Parasitic) },
                new GeneChoice { Label = "Chemosymbiosis (host chemosynthetic partners)",
                    IsAvailable = agent => agent.Metabolism == MetabolismType.Chemosynthetic,
                    Apply = agent => agent.SetFeedingApparatus(FeedingApparatus.Chemosymbiotic) },
                new GeneChoice { Label = "Photosymbiosis (host photosynthetic partners)",
                    IsAvailable = agent => agent.Metabolism == MetabolismType.Phototrophic || agent.Metabolism == MetabolismType.Mixotrophic,
                    Apply = agent => agent.SetFeedingApparatus(FeedingApparatus.Photosymbiotic) },
            },
            DefaultAutoApply = agent =>
            {
                if (agent.Metabolism == MetabolismType.Heterotrophic || agent.Metabolism == MetabolismType.Mixotrophic)
                    agent.SetFeedingApparatus(agent.HasMotility ? FeedingApparatus.PredatorActive : FeedingApparatus.Grazer);
                else
                    agent.SetFeedingApparatus(FeedingApparatus.Detritivore);
            }
        });

        // e1_integument_elaboration: surface texture/type only (color/bioluminescence remain the
        // separate, still-unimplemented circulatory-chromophore-spec concern). Chitin/ShellExternal/
        // Crystalline mirror an existing BodyPlanType choice; FilamentsFur is the one genuinely
        // independent branch (thermoregulatory, not a structural-support byproduct).
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "e1_integument_elaboration",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            IsEligible = agent => agent.Integument == IntegumentType.BareMucous
                && (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 2 || agent.StressLevel >= 30f
                    || GeneEvolutionManager.SessionElapsed >= 100f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice { Label = "Develop Scaled Integument (dermal scales — moderate protection, low cost)",
                    Apply = agent => agent.SetIntegument(IntegumentType.Scales) },
                new GeneChoice { Label = "Chitinous Cuticle (matches the existing exoskeleton)",
                    IsAvailable = agent => agent.BodyPlan == BodyPlanType.Exoskeleton,
                    Apply = agent => agent.SetIntegument(IntegumentType.Chitin) },
                new GeneChoice { Label = "Mineral Shell Surface (matches the existing shell)",
                    IsAvailable = agent => agent.BodyPlan == BodyPlanType.Shell,
                    Apply = agent => agent.SetIntegument(IntegumentType.ShellExternal) },
                new GeneChoice { Label = "Filament/Fur Insulation (thermoregulatory covering — cold/variance specialist)",
                    IsAvailable = agent => agent.hardinessTrait >= 55f,
                    Apply = agent => agent.SetIntegument(IntegumentType.FilamentsFur) },
                new GeneChoice { Label = "Crystalline Surface (matches the crystalline backbone)",
                    IsAvailable = agent => agent.BodyPlan == BodyPlanType.Crystalline,
                    Apply = agent => agent.SetIntegument(IntegumentType.Crystalline) },
                new GeneChoice { Label = "Remain Bare/Mucous-Coated (lowest cost, no change)",
                    Apply = agent => { /* no change */ } },
            },
            DefaultAutoApply = agent => agent.SetIntegument(IntegumentType.Scales)
        });

        // e1_limb_differentiation (§2.8 — flagged missing): splits a previously-undifferentiated
        // appendage budget into dedicated locomotor vs. manipulator pairs. Before this fires,
        // ManipulatorPairs stays 0 and the descriptor's derived tool_ceiling stays false even at
        // high Manipulation tiers — appendages exist but serve both roles at once.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "e1_limb_differentiation",
            Prerequisites = new[] { "ManipulationAppendageDevelopment" },
            IsEra1Event = true,
            IsEligible = agent => agent.Manipulation >= ManipulationLevel.Articulated && agent.LocomotorPairs == 0
                && (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 4 || agent.StressLevel >= 40f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice { Label = "Differentiate Limbs (dedicated locomotor + manipulator pairs — raises tool ceiling)",
                    Apply = agent => agent.SetLimbDifferentiation(2, 1) },
                new GeneChoice { Label = "Remain Undifferentiated (shared-purpose appendages — no dedicated manipulators)",
                    Apply = agent => { /* no change */ } },
            },
            DefaultAutoApply = agent => agent.SetLimbDifferentiation(2, 1)
        });

        // e1_vocal_structure_emergence (§2.8 — flagged missing): develops dedicated vocal/resonance
        // structures. Anatomical prerequisite for Era 2's Vocal/Auditory CommunicationMedium choice —
        // an Era 1 biological capability, not the Era 2 cultural adoption decision itself.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "e1_vocal_structure_emergence",
            Prerequisites = new[] { "GermLayerComplexity", "SensoryOrganDevelopment" },
            IsEra1Event = true,
            IsEligible = agent => !agent.VocalApparatus
                && (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 3 || agent.StressLevel >= 35f
                    || GeneEvolutionManager.SessionElapsed >= 125f)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice { Label = "Develop Vocal/Resonance Structures (enables acoustic signaling — Era 2 vocal communication prerequisite)",
                    Apply = agent => agent.SetVocalApparatus() },
                new GeneChoice { Label = "Remain Silent (no dedicated vocal apparatus)",
                    Apply = agent => { /* no change */ } },
            },
            DefaultAutoApply = agent => agent.SetVocalApparatus()
        });

        // e1_colonial_modularity_emergence: rare non-bilaterian body-plan fork (M1) — a sessile
        // lineage growing as a linked colony of modules (coral/bryozoan-equivalent) rather than a
        // single discrete body. Genuinely different topology, not a variant of Bilateral/Radial —
        // see MorphologyGenerator.BuildColonial. Favored by stable (low-stress) sessile conditions,
        // matching the real ecological niche these body plans occupy.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "e1_colonial_modularity_emergence",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            IsEligible = agent => !agent.HasMotility && !agent.IsColonialModular && !agent.IsBiradial
                && agent.StressLevel < 25f && GeneEvolutionManager.SessionElapsed >= 150f
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            AutoApply = agent => agent.SetColonialModular()
        });

        // e1_biradial_symmetry_emergence: rare non-bilaterian body-plan fork (M1) — ctenophore-style
        // dual symmetry plane, aquatic/pelagic-favored. OR-gate sibling of the ordinary motile→
        // Bilateral read, not a required step.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "e1_biradial_symmetry_emergence",
            Prerequisites = new[] { "MotilityEmergence" },
            IsEra1Event = true,
            IsEligible = agent => agent.HasMotility && agent.IsAquatic && !agent.IsColonialModular && !agent.IsBiradial
                && GeneEvolutionManager.SessionElapsed >= 150f
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            AutoApply = agent => agent.SetBiradial()
        });

        // e1_diversification_explosion: Morphological Complexity Threshold closure AND-gate.
        // Fires automatically once predation + sensory + protective structure thresholds are all met.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "DiversificationExplosion",
            Prerequisites = new[] { "KingdomFork", "SensoryOrganDevelopment", "ProtectiveStructureEmergence" },
            IsEra1Event = true,
            IsEligible = agent => agent.AcquiredGenes.Contains("KingdomFork")
                && agent.AcquiredGenes.Contains("SensoryOrganDevelopment")
                && agent.AcquiredGenes.Contains("ProtectiveStructureEmergence")
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            AutoApply = agent =>
            {
                // General diversification fitness bonus — morphological complexity optimisation.
                agent.SetTraits(agent.visionTrait + 3f, agent.speedTrait + 3f,
                    agent.strengthTrait + 3f, agent.hardinessTrait + 3f,
                    agent.temperaturePreference, agent.moisturePreference);
                Debug.Log($"[Era1] {agent.name} crossed the Morphological Complexity Threshold.");
            }
        });

        // ── Era 2 Player Decision Layer (§6) ─────────────────────────────────────
        // These fire once Era 2 is active; choices set community-level intelligence
        // multipliers in Era2Manager. NPCs receive a neutral/auto default.

        // §6.1 Cognitive Investment Strategy: direct which sub-track the lineage leans into.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "CognitiveInvestmentStrategy",
            Prerequisites = new[] { "MotilityEmergence" },
            IsEligible = agent => Era2Manager.Instance != null && Era2Manager.Instance.IsActive
                && Era2Manager.Instance.GetRecord(agent.communityId)?.Architecture == CognitiveArchitecture.Individuated,
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Social-Cognitive Breadth (A1) — invest in group coordination and foraging intelligence",
                    Apply = agent => Era2Manager.Instance?.ApplyCognitiveInvestment(
                        agent.communityId, IndividuatedSubTrack.A1_SocialForaging, 1.30f)
                },
                new GeneChoice
                {
                    Label = "Manipulative Specialization (A2) — invest in individual learning and tool-mastery",
                    Apply = agent => Era2Manager.Instance?.ApplyCognitiveInvestment(
                        agent.communityId, IndividuatedSubTrack.A2_SolitaryManipulative, 1.20f)
                },
                new GeneChoice
                {
                    Label = "Scale Investment (A3) — invest in raw neuron count and social/communicative ceiling",
                    Apply = agent => Era2Manager.Instance?.ApplyCognitiveInvestment(
                        agent.communityId, IndividuatedSubTrack.A3_BulkBrain, 1.10f)
                },
            },
            DefaultAutoApply = agent => Era2Manager.Instance?.ApplyCognitiveInvestment(
                agent.communityId, IndividuatedSubTrack.A1_SocialForaging, 1.00f)
        });

        // §6.2 Communication Medium — gated by morphology.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "CommunicationMedium",
            Prerequisites = new[] { "SocialityEmergence" },
            IsEligible = agent => Era2Manager.Instance != null && Era2Manager.Instance.IsActive
                && Era2Manager.Instance.GetRecord(agent.communityId)?.CommMedium == CommunicationMedium.Unset,
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Vocal / Auditory — rich oral tradition; prerequisite for spoken codification",
                    // Available to all; no morphological gate needed at this abstraction level.
                    Apply = agent => Era2Manager.Instance?.ApplyCommunicationMedium(
                        agent.communityId, CommunicationMedium.VocalAuditory)
                },
                new GeneChoice
                {
                    Label = "Visual / Gestural — high-bandwidth signaling; requires good vision",
                    IsAvailable = agent => agent.visionTrait >= 10f,
                    Apply = agent => Era2Manager.Instance?.ApplyCommunicationMedium(
                        agent.communityId, CommunicationMedium.VisualGestural)
                },
                new GeneChoice
                {
                    Label = "Chemical / Pheromonal — precise, persistent signals; slower codification",
                    Apply = agent => Era2Manager.Instance?.ApplyCommunicationMedium(
                        agent.communityId, CommunicationMedium.ChemicalPheromonal)
                },
                new GeneChoice
                {
                    Label = "Bioluminescent / Electrical — aquatic or deep-environment specialist",
                    Apply = agent => Era2Manager.Instance?.ApplyCommunicationMedium(
                        agent.communityId, CommunicationMedium.BioluminescentElectrical)
                },
            },
            DefaultAutoApply = agent => Era2Manager.Instance?.ApplyCommunicationMedium(
                agent.communityId, CommunicationMedium.VocalAuditory)
        });

        // §6.3 Niche Construction Orientation.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "NicheConstructionOrientation",
            Prerequisites = new[] { "ManipulationAppendageDevelopment" },
            IsEligible = agent => Era2Manager.Instance != null && Era2Manager.Instance.IsActive
                && Era2Manager.Instance.GetRecord(agent.communityId)?.NicheOrientation == NicheConstructionOrientation.Unset,
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Tool-Based — manufacture and use discrete objects; synergizes with Manipulation",
                    Apply = agent => Era2Manager.Instance?.ApplyNicheOrientation(
                        agent.communityId, NicheConstructionOrientation.ToolBased)
                },
                new GeneChoice
                {
                    Label = "Environment-Modification — reshape the landscape (dams, mounds, nests)",
                    Apply = agent => Era2Manager.Instance?.ApplyNicheOrientation(
                        agent.communityId, NicheConstructionOrientation.EnvironmentModification)
                },
                new GeneChoice
                {
                    Label = "Social Transmission Only — teaching-based culture; no physical artifacts",
                    Apply = agent => Era2Manager.Instance?.ApplyNicheOrientation(
                        agent.communityId, NicheConstructionOrientation.SocialTransmissionOnly)
                },
            },
            DefaultAutoApply = agent => Era2Manager.Instance?.ApplyNicheOrientation(
                agent.communityId, NicheConstructionOrientation.EnvironmentModification)
        });

        // §6.4 Metabolic Allocation — brain vs body investment dial.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "MetabolicAllocation",
            Prerequisites = new[] { "ManipulationAppendageDevelopment" },
            IsEligible = agent => Era2Manager.Instance != null && Era2Manager.Instance.IsActive
                && Era2Manager.Instance.GetRecord(agent.communityId)?.MetabolicBrainWeight == 1.0f,
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Brain-Heavy Investment — accelerate intelligence growth at the cost of physical robustness",
                    Apply = agent => Era2Manager.Instance?.ApplyMetabolicAllocation(agent.communityId, 1.40f)
                },
                new GeneChoice
                {
                    Label = "Balanced Allocation — moderate intelligence growth with stable physical capability",
                    Apply = agent => Era2Manager.Instance?.ApplyMetabolicAllocation(agent.communityId, 1.00f)
                },
                new GeneChoice
                {
                    Label = "Somatic Investment — prioritize physical robustness; slower II accumulation",
                    Apply = agent => Era2Manager.Instance?.ApplyMetabolicAllocation(agent.communityId, 0.65f)
                },
            },
            DefaultAutoApply = agent => Era2Manager.Instance?.ApplyMetabolicAllocation(agent.communityId, 1.00f)
        });

        // §6.5 Social Structure — seeds Sociality multiplier adjustment and Era 3 governance arc.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "SocialStructure",
            Prerequisites = new[] { "SocialityEmergence" },
            IsEligible = agent => Era2Manager.Instance != null && Era2Manager.Instance.IsActive
                && Era2Manager.Instance.GetRecord(agent.communityId)?.SocialStructure == SocialStructureType.Unset,
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Pair-Bonded / Monogamous — stable nuclear units; moderate II, Era 3 → structured governance",
                    Apply = agent => Era2Manager.Instance?.ApplySocialStructure(
                        agent.communityId, SocialStructureType.PairBonded)
                },
                new GeneChoice
                {
                    Label = "Multi-Member Troop — group stability and coordinated foraging; high Sociality bonus",
                    Apply = agent => Era2Manager.Instance?.ApplySocialStructure(
                        agent.communityId, SocialStructureType.MultiMemberTroop)
                },
                new GeneChoice
                {
                    Label = "Fission-Fusion — flexible group composition; highest variance, Era 3 → decentralized",
                    Apply = agent => Era2Manager.Instance?.ApplySocialStructure(
                        agent.communityId, SocialStructureType.FissionFusion)
                },
                new GeneChoice
                {
                    Label = "Solitary-Territorial with Aggregation — low coordination overhead; penalizes group-brain effect",
                    Apply = agent => Era2Manager.Instance?.ApplySocialStructure(
                        agent.communityId, SocialStructureType.SolitaryTerritorial)
                },
            },
            DefaultAutoApply = agent => Era2Manager.Instance?.ApplySocialStructure(
                agent.communityId, SocialStructureType.MultiMemberTroop)
        });

        // ── Era 2 End-of-Era threshold layer (§8) ────────────────────────────────

        // §8.5b Cumulative Culture Threshold: ratchet gate for codification + seafaring.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "CumulativeCultureThreshold",
            Prerequisites = new[] { "SocialityEmergence" },
            IsEligible = agent => Era2Manager.Instance != null && Era2Manager.Instance.IsActive
                && agent.Sociality >= SocialityBaseline.Aggregating
                && (Era2Manager.Instance.GetRecord(agent.communityId)?.SocialStructure ?? SocialStructureType.Unset) != SocialStructureType.SolitaryTerritorial
                && !(Era2Manager.Instance.GetRecord(agent.communityId)?.ThresholdCumulativeCulture ?? false),
            AutoApply = agent =>
            {
                var rec = Era2Manager.Instance?.GetRecord(agent.communityId);
                if (rec != null) rec.ThresholdCumulativeCulture = true;
                Debug.Log($"[Era2] Community {agent.communityId} crossed Cumulative Culture threshold.");
            }
        });

        // §8.2 LLFP Emergence: stable low-level food production.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "LLFPEmergence",
            Prerequisites = new[] { "NicheConstructionOrientation", "KingdomFork" },
            IsEligible = agent => Era2Manager.Instance != null && Era2Manager.Instance.IsActive
                && (agent.Metabolism == MetabolismType.Heterotrophic || agent.Metabolism == MetabolismType.Mixotrophic)
                && (Era2Manager.Instance.GetRecord(agent.communityId)?.NicheOrientation ?? NicheConstructionOrientation.Unset) != NicheConstructionOrientation.Unset
                && !(Era2Manager.Instance.GetRecord(agent.communityId)?.ThresholdLLFP ?? false),
            AutoApply = agent =>
            {
                var rec = Era2Manager.Instance?.GetRecord(agent.communityId);
                if (rec != null) rec.ThresholdLLFP = true;
                Debug.Log($"[Era2] Community {agent.communityId} achieved Low-Level Food Production.");
            }
        });

        // §8.3 Fire and Heat Mastery: prerequisite for cooking and tool refinement.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "FireHeatMastery",
            Prerequisites = new[] { "ManipulationAppendageDevelopment" },
            IsEligible = agent => Era2Manager.Instance != null && Era2Manager.Instance.IsActive
                && agent.Manipulation >= ManipulationLevel.Articulated
                && !agent.IsAquatic // combustion needs atmospheric O2 contact — cannot fire underwater
                && !agent.IsAnoxicRefugeLineage // anoxic-refuge lineages lack fire-compatible environment
                && !agent.AcquiredGenes.Contains("FireHeatMastery"),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Harness Fire — controlled combustion enables cooking, warmth, and tool refinement (+Hardiness, unlocks Pyrotechnology)",
                    Apply = agent =>
                    {
                        agent.SetTraits(agent.visionTrait, agent.speedTrait, agent.strengthTrait,
                            agent.hardinessTrait + 8f, agent.temperaturePreference, agent.moisturePreference);
                    }
                },
                new GeneChoice
                {
                    Label = "Avoid Fire — lineage does not control combustion (no benefit; no risk)",
                    Apply = agent => { /* no change */ }
                }
            },
            DefaultAutoApply = agent =>
            {
                agent.SetTraits(agent.visionTrait, agent.speedTrait, agent.strengthTrait,
                    agent.hardinessTrait + 8f, agent.temperaturePreference, agent.moisturePreference);
            }
        });

        // §8.10 Pyrotechnology / Cooking: retrospective energy budget boost from fire mastery.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "PyrotechnologyCooking",
            Prerequisites = new[] { "FireHeatMastery" },
            IsEligible = agent => Era2Manager.Instance != null && Era2Manager.Instance.IsActive
                && agent.AcquiredGenes.Contains("FireHeatMastery"),
            AutoApply = agent =>
            {
                // Cooking increases caloric extraction — modeled as starvation tolerance boost.
                agent.starvationTime *= 1.25f;
                Debug.Log($"[Era2] {agent.name} unlocked Pyrotechnology/Cooking — energy budget expanded.");
            }
        });

        // §8.4 Communication Codification: crystallize oral/ephemeral communication into a recorded form.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "CommunicationCodeification",
            Prerequisites = new[] { "CumulativeCultureThreshold", "CommunicationMedium" },
            IsEligible = agent => Era2Manager.Instance != null && Era2Manager.Instance.IsActive
                && (Era2Manager.Instance.GetRecord(agent.communityId)?.ThresholdCumulativeCulture ?? false)
                && !(Era2Manager.Instance.GetRecord(agent.communityId)?.ThresholdCommunicationCodeified ?? false),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Crystallize Communication — develop recorded/symbolic language system (+II multiplier bonus, seeds Era 3 culture graph)",
                    Apply = agent =>
                    {
                        var rec = Era2Manager.Instance?.GetRecord(agent.communityId);
                        if (rec != null)
                        {
                            rec.ThresholdCommunicationCodeified = true;
                            rec.CognitiveInvestmentMult *= 1.15f; // codified knowledge compounds II
                        }
                    }
                },
                new GeneChoice
                {
                    Label = "Remain Oral/Ephemeral — knowledge transmitted only through living memory (no additional bonus)",
                    Apply = agent => { /* no change */ }
                }
            },
            DefaultAutoApply = agent =>
            {
                var rec = Era2Manager.Instance?.GetRecord(agent.communityId);
                if (rec != null)
                {
                    rec.ThresholdCommunicationCodeified = true;
                    rec.CognitiveInvestmentMult *= 1.15f;
                }
            }
        });

        // §8.11 Symbolic Ornamentation: shared meaning-systems that can substitute for or
        // precede verbal codification.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "SymbolicOrnamentation",
            Prerequisites = new[] { "SocialityEmergence" },
            IsEligible = agent => Era2Manager.Instance != null && Era2Manager.Instance.IsActive
                && agent.Sociality >= SocialityBaseline.Aggregating
                && agent.AgeSeconds >= 30f,
            AutoApply = agent =>
            {
                // Symbolic ornamentation reduces social friction — modest II contribution.
                var rec = Era2Manager.Instance?.GetRecord(agent.communityId);
                if (rec != null) rec.CognitiveInvestmentMult *= 1.05f;
            }
        });

        // ── Era 2 Track A — aerial locomotion events (addendum §1.2) ─────────────────────
        // Both events are Era 2 only; Track A (active-mobile lineage) only.

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "GlidingAdaptation",
            IsEra1Event = false,
            Prerequisites = new[] { "MotilityEmergence" },
            IsEligible = agent =>
                (Era2Manager.Instance != null && Era2Manager.Instance.IsActive)
                && agent.HasMotility
                && agent.Manipulation >= ManipulationLevel.Simple // articulated appendage proxy
                && !agent.AcquiredGenes.Contains("GlidingAdaptation")
                && !agent.AcquiredGenes.Contains("AerialLocomotion")
                && agent.PredationPressure >= 0.25f, // gliding as predator-escape response
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Develop Gliding Membranes (controlled descent; prerequisite path for powered flight)",
                    Apply = agent => agent.BecomeGlider()
                },
                new GeneChoice
                {
                    Label = "Forego Gliding (retain ground locomotion)",
                    Apply = agent => { agent.AcquiredGenes.Add("GlidingAdaptation"); }
                }
            },
            DefaultAutoApply = agent => agent.AcquiredGenes.Add("GlidingAdaptation")
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "AerialLocomotion",
            IsEra1Event = false,
            Prerequisites = new[] { "MotilityEmergence" },
            IsEligible = agent =>
                (Era2Manager.Instance != null && Era2Manager.Instance.IsActive)
                && agent.HasMotility
                && agent.Manipulation >= ManipulationLevel.Simple
                && !agent.AcquiredGenes.Contains("AerialLocomotion")
                // Live mass check — evaluated fresh each eligibility test, not cached.
                && agent.CurrentMass < AgentController.FlightMassCeiling
                // OR-gate: prior gliding raises the probability but is not required.
                && (agent.AcquiredGenes.Contains("GlidingAdaptation") || agent.PredationPressure >= 0.40f),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Evolve Powered Flight (aerial locomotion; sidesteps elevation costs; mass ceiling applies)",
                    Apply = agent => agent.BecomeAerial()
                },
                new GeneChoice
                {
                    Label = "Remain Ground/Gliding Locomotion",
                    Apply = agent => { agent.AcquiredGenes.Add("AerialLocomotion"); }
                }
            },
            DefaultAutoApply = agent => agent.AcquiredGenes.Add("AerialLocomotion")
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "KingdomFork",
            Prerequisites = new[] { "Multicellularity", "MotilityEmergence" },
            IsEra1Event = true,
            // Heterotrophy emerges specifically when autotrophic pathways are resource-starved —
            // predation becomes competitive only when chemical-gradient energy is genuinely scarce.
            // Age is the origination floor; ResourceScarcity is the fixation multiplier.
            IsEligible = agent => (agent.AgeSeconds >= agent.kingdomForkAgeThreshold
                || agent.ResourceScarcity >= 0.6f)
                && agent.HasMotility
                && (EraManager.Instance == null || EraManager.Instance.CurrentEra >= 1)
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice { Label = "Develop Photosynthesis (become a Producer)", Apply = agent => agent.BecomeProducer() },
                new GeneChoice { Label = "Remain Heterotrophic (stay a Consumer)", Apply = agent => agent.BecomeConsumer() }
            },
            // Default to Producer: in early eras there are no corpses to scavenge, so
            // background agents that auto-apply must have a viable energy source.
            // The player still gets the choice popup first.
            DefaultAutoApply = agent => agent.BecomeProducer()
        });

        // era3-systems-implementation-spec §4: the 8 orphaned Era 3 Decision Node (d3_*) duplicate
        // GeneDefinitions formerly here (d3_trade_policy, d3_kinship_policy, d3_government_transition,
        // d3_idea_patronage, d3_war_or_diplomacy, d3_domain_investment, d3_caste_labor,
        // d3_large_initiative_1 ×2 for CommerceEngine/LivingReef) are confirmed dead — deleted
        // outright. GeneEvolutionManager.CheckEligibleGenes bails on Era3.IsActive and skips any "d3_"
        // -prefixed id by name, so these could never fire via the normal gene-evolution UI path; the
        // real, live versions are the Era3HUD.cs Card entries sharing the same Ids, resolved through
        // an entirely different mechanism (DrawCards/Apply). d3_caste_labor's real sibling was itself
        // deleted this same pass (§2/§4 — wrote to now-deleted SectorMilitary/Caste* fields).
    }
}
