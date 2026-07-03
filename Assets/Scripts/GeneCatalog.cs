using UnityEngine;

/// Builds the default Section 6b gene set (see design spec Section 14e's dependency
/// table). Nucleus and Multicellularity are pre-seeded directly on each agent at spawn
/// (AgentController.Init) rather than registered here, since they're assumed to have
/// already occurred before this simulation begins.
public static class GeneCatalog
{
    public static void BuildDefault()
    {
        GeneEvolutionManager.ResetCatalog();

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "SensoryOrganDevelopment",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            // Whether to express: ResourceScarcity drives pre-predation sensing (find patchy food);
            // PredationPressure drives post-KingdomFork sensing (detect threats). Eat-count is the
            // origination floor — always available at low probability regardless of pressure.
            IsEligible = agent => (agent.LifetimeEats >= agent.sensoryGeneEatThreshold
                || (!agent.AcquiredGenes.Contains("KingdomFork") && agent.ResourceScarcity >= 0.55f)
                || (agent.AcquiredGenes.Contains("KingdomFork") && agent.PredationPressure >= 0.25f))
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
                || agent.StressLevel >= 35f)
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

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "ReproductiveStrategyShift",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            // Red Queen: sex is favored specifically under strong coevolving parasite pressure.
            // PathogenPressure (density proxy) is the primary fixation driver; age+eats is the
            // origination floor. StressLevel kept as a secondary signal per §3.
            IsEligible = agent => (agent.PathogenPressure >= 0.55f
                || agent.StressLevel >= 45f
                || (agent.AgeSeconds >= agent.reproductiveShiftAgeThreshold
                    && agent.LifetimeEats >= agent.reproductiveShiftEatThreshold))
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice { Label = "Remain Asexual (clone with mutation drift)", Apply = agent => agent.BecomeAsexual() },
                new GeneChoice { Label = "Shift to Sexual Reproduction (blend traits with a mate)", Apply = agent => agent.BecomeSexual() }
            },
            // Background agents that mature after the player has already chosen default to
            // the conservative/unchanged option (stay asexual).
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
        // (Path A) or retreat permanently to anoxic refuges (Path B).
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "EfficientRespiration",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            IsEligible = agent => AtmosphereManager.Instance != null && AtmosphereManager.Instance.GreatGasEventFired
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
            // KingdomFork supersedes this: if the lineage already chose heterotrophy (or
            // any other metabolic direction) at KingdomFork, don't re-open the question.
            IsEligible = agent => agent.Metabolism == MetabolismType.Chemosynthetic
                && !agent.AcquiredGenes.Contains("KingdomFork")
                && (agent.LifetimeEats >= agent.sensoryGeneEatThreshold
                    || agent.ResourceScarcity >= 0.55f) // light becomes attractive when chemo substrate is scarce
                && !(Era2Manager.Instance != null && Era2Manager.Instance.IsActive),
            Choices = new[]
            {
                new GeneChoice
                {
                    Label = "Evolve Photosynthesis (harvest stellar energy; drives the Great Gas Event)",
                    Apply = agent => agent.BecomePhototrophic()
                },
                new GeneChoice
                {
                    Label = "Remain Chemosynthetic (vent chemistry; stable but spatially constrained)",
                    Apply = agent => { /* stays chemosynthetic */ }
                }
            },
            DefaultAutoApply = agent => agent.BecomePhototrophic()
        });

        // e1_motility_emergence: the transition from passive drifter to self-directed mover.
        // Fires once the organism has established itself (Era 1+ and enough reproduction
        // cycles). Sessile lineages that skip motility can never become consumers.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "MotilityEmergence",
            Prerequisites = new[] { "Multicellularity" },
            IsEra1Event = true,
            IsEligible = agent => (EraManager.Instance == null || EraManager.Instance.CurrentEra >= 1)
                && (agent.LifetimeEats >= agent.locomotorGeneEatThreshold
                    || agent.ResourceScarcity >= 0.50f) // movement pays off when resources are patchy, not just scarce
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

        // Manipulation appendage development: evolve increasingly dexterous structures.
        // Gated behind motility (you need to move to develop limbs) and Minimal-Replicator Seas+.
        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "ManipulationAppendageDevelopment",
            Prerequisites = new[] { "MotilityEmergence" },
            IsEra1Event = true,
            IsEligible = agent => agent.Manipulation == ManipulationLevel.None
                && (EraManager.Instance == null || EraManager.Instance.CurrentEra >= 1)
                && (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 2 || agent.StressLevel >= 40f)
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
                && (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 3 || agent.StressLevel >= 40f)
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
                && (agent.LifetimeEats >= agent.sensoryGeneEatThreshold * 2 || agent.StressLevel >= 40f)
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
            IsEligible = agent => (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 2 || agent.StressLevel >= 35f)
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
            IsEligible = agent => (agent.LifetimeEats >= agent.locomotorGeneEatThreshold * 3
                    || agent.StressLevel >= 40f
                    || agent.PredationPressure >= 0.30f) // predation weakly promotes tissue-layer complexity
                && (EraManager.Instance == null || EraManager.Instance.CurrentEra >= 3)
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

        // ── Era 3 Decision Nodes (d3_*) ──────────────────────────────────────────
        // These use the GeneDefinition system but their Apply lambdas modify
        // CivilizationState via Era3Manager, not agent gene stats.
        // IsEligible: player agent only (communityId == 0), Era 3 active, prereq acquired.

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "d3_trade_policy",
            IsEligible = agent => Era3Manager.Instance != null && Era3Manager.Instance.IsActive
                && agent.communityId == 0
                && Era3Manager.Instance.PlayerCiv.Has("e3_exchange_contact")
                && !Era3Manager.Instance.PlayerCiv.Has("d3_trade_policy"),
            Choices = new[]
            {
                new GeneChoice { Label = "Open Routes — low tariffs, max exchange",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_trade_policy"); Era3Manager.Instance?.SetTradePolicy(0, 0.05f, 0.9f); Era3Manager.Instance?.OnDecisionResolved("d3_trade_policy"); } },
                new GeneChoice { Label = "Balanced Tariffs — moderate protection",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_trade_policy"); Era3Manager.Instance?.SetTradePolicy(0, 0.35f, 0.6f); Era3Manager.Instance?.OnDecisionResolved("d3_trade_policy"); } },
                new GeneChoice { Label = "Embargo — economic isolation",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_trade_policy"); Era3Manager.Instance?.SetTradePolicy(0, 0.95f, 0.15f); Era3Manager.Instance?.OnDecisionResolved("d3_trade_policy"); } },
            }
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "d3_kinship_policy",
            IsEligible = agent => Era3Manager.Instance != null && Era3Manager.Instance.IsActive
                && agent.communityId == 0
                && Era3Manager.Instance.PlayerCiv.Has("e3_family_norms_emerge")
                && !Era3Manager.Instance.PlayerCiv.Has("d3_kinship_policy"),
            Choices = new[]
            {
                new GeneChoice { Label = "Nuclear — tight household unit",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_kinship_policy"); Era3Manager.Instance?.SetKinship(0, KinshipPolicy.Nuclear); Era3Manager.Instance?.OnDecisionResolved("d3_kinship_policy"); } },
                new GeneChoice { Label = "Extended — broader kin networks",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_kinship_policy"); Era3Manager.Instance?.SetKinship(0, KinshipPolicy.Extended); Era3Manager.Instance?.OnDecisionResolved("d3_kinship_policy"); } },
                new GeneChoice { Label = "Clan — kin coalitions, factionalism risk",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_kinship_policy"); Era3Manager.Instance?.SetKinship(0, KinshipPolicy.Clan); Era3Manager.Instance?.OnDecisionResolved("d3_kinship_policy"); } },
                new GeneChoice { Label = "CrossLineage — intermarriage, trade openness",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_kinship_policy"); Era3Manager.Instance?.SetKinship(0, KinshipPolicy.CrossLineage); Era3Manager.Instance?.OnDecisionResolved("d3_kinship_policy"); } },
            }
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "d3_government_transition",
            IsEligible = agent => Era3Manager.Instance != null && Era3Manager.Instance.IsActive
                && agent.communityId == 0
                && Era3Manager.Instance.PlayerCiv.Has("e3_social_stratification")
                && !Era3Manager.Instance.PlayerCiv.Has("d3_government_transition"),
            Choices = new[]
            {
                new GeneChoice { Label = "Monarchy / Hub Network / Single Queen",
                    Apply = agent => {
                        agent.AcquiredGenes.Add("d3_government_transition");
                        var arch = Era3Manager.Instance!.PlayerCiv.Architecture;
                        var gov  = arch == CognitiveArchitecture.Distributed ? GovernmentType.HubNetwork
                                 : arch == CognitiveArchitecture.Collective  ? GovernmentType.SingleQueen
                                 :                                              GovernmentType.Monarchy;
                        Era3Manager.Instance.SetGovernment(0, gov);
                        Era3Manager.Instance.OnDecisionResolved("d3_government_transition"); } },
                new GeneChoice { Label = "Oligarchy / Mesh Network / Nest Cluster",
                    Apply = agent => {
                        agent.AcquiredGenes.Add("d3_government_transition");
                        var arch = Era3Manager.Instance!.PlayerCiv.Architecture;
                        var gov  = arch == CognitiveArchitecture.Distributed ? GovernmentType.MeshNetwork
                                 : arch == CognitiveArchitecture.Collective  ? GovernmentType.NestCluster
                                 :                                              GovernmentType.Oligarchy;
                        Era3Manager.Instance.SetGovernment(0, gov);
                        Era3Manager.Instance.OnDecisionResolved("d3_government_transition"); } },
                new GeneChoice { Label = "Democracy — broad participation",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_government_transition"); Era3Manager.Instance?.SetGovernment(0, GovernmentType.Democracy); Era3Manager.Instance?.OnDecisionResolved("d3_government_transition"); } },
                new GeneChoice { Label = "Theocracy — sacred authority",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_government_transition"); Era3Manager.Instance?.SetGovernment(0, GovernmentType.Theocracy); Era3Manager.Instance?.OnDecisionResolved("d3_government_transition"); } },
            }
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "d3_idea_patronage",
            IsEligible = agent => Era3Manager.Instance != null && Era3Manager.Instance.IsActive
                && agent.communityId == 0
                && Era3Manager.Instance.PlayerCiv.Has("e3_chiefdom")
                && !Era3Manager.Instance.PlayerCiv.Has("d3_idea_patronage"),
            Choices = new[]
            {
                new GeneChoice { Label = "Culture — art, oral tradition, norms",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_idea_patronage"); Era3Manager.Instance?.SetIdeaPatronage(0, IdeaPatronageType.Culture); Era3Manager.Instance?.OnDecisionResolved("d3_idea_patronage"); } },
                new GeneChoice { Label = "Religion — cosmological tier",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_idea_patronage"); Era3Manager.Instance?.SetIdeaPatronage(0, IdeaPatronageType.Religion); Era3Manager.Instance?.OnDecisionResolved("d3_idea_patronage"); } },
                new GeneChoice { Label = "Science — proto-natural philosophy",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_idea_patronage"); Era3Manager.Instance?.SetIdeaPatronage(0, IdeaPatronageType.Science); Era3Manager.Instance?.OnDecisionResolved("d3_idea_patronage"); } },
                new GeneChoice { Label = "Military — tactical doctrine",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_idea_patronage"); Era3Manager.Instance?.SetIdeaPatronage(0, IdeaPatronageType.Military); Era3Manager.Instance?.OnDecisionResolved("d3_idea_patronage"); } },
            }
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "d3_war_or_diplomacy",
            IsEligible = agent => Era3Manager.Instance != null && Era3Manager.Instance.IsActive
                && agent.communityId == 0
                && Era3Manager.Instance.PlayerCiv.Has("e3_state_formation")
                && !Era3Manager.Instance.PlayerCiv.Has("d3_war_or_diplomacy"),
            Choices = new[]
            {
                new GeneChoice { Label = "Organized Warfare — invest in coercive capacity",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_war_or_diplomacy"); Era3Manager.Instance?.SetWarPath(0); Era3Manager.Instance?.OnDecisionResolved("d3_war_or_diplomacy"); } },
                new GeneChoice { Label = "Diplomacy — formal alliances, open borders",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_war_or_diplomacy"); Era3Manager.Instance?.SetDiplomacyPath(0); Era3Manager.Instance?.OnDecisionResolved("d3_war_or_diplomacy"); } },
            }
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "d3_domain_investment",
            IsEligible = agent => Era3Manager.Instance != null && Era3Manager.Instance.IsActive
                && agent.communityId == 0
                && Era3Manager.Instance.PlayerCiv.Has("e3_warfare_organized")
                && !Era3Manager.Instance.PlayerCiv.Has("d3_domain_investment"),
            Choices = new[]
            {
                new GeneChoice { Label = "Kinetic — conventional force",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_domain_investment"); Era3Manager.Instance?.ApplyDomainInvestment(0, 0.25f, 0f, 0f, 0f); Era3Manager.Instance?.OnDecisionResolved("d3_domain_investment"); } },
                new GeneChoice { Label = "Biochemical — plague & toxin doctrine",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_domain_investment"); Era3Manager.Instance?.ApplyDomainInvestment(0, 0f, 0.25f, 0f, 0f); Era3Manager.Instance?.OnDecisionResolved("d3_domain_investment"); } },
                new GeneChoice { Label = "Informational — espionage & disinformation",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_domain_investment"); Era3Manager.Instance?.ApplyDomainInvestment(0, 0f, 0f, 0.25f, 0f); Era3Manager.Instance?.OnDecisionResolved("d3_domain_investment"); } },
                new GeneChoice { Label = "Economic — sanctions & trade leverage",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_domain_investment"); Era3Manager.Instance?.ApplyDomainInvestment(0, 0f, 0f, 0f, 0.25f); Era3Manager.Instance?.OnDecisionResolved("d3_domain_investment"); } },
            }
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "d3_bioweapon_option",
            IsEligible = agent => Era3Manager.Instance != null && Era3Manager.Instance.IsActive
                && agent.communityId == 0
                && Era3Manager.Instance.PlayerCiv.Has("d3_domain_investment")
                && Era3Manager.Instance.PlayerCiv.Has("e3_warfare_organized")
                && !Era3Manager.Instance.PlayerCiv.Has("d3_bioweapon_option"),
            Choices = new[]
            {
                new GeneChoice { Label = "Develop Biochemical Weapons — high domain gain, risk",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_bioweapon_option"); Era3Manager.Instance?.ApplyDomainInvestment(0, 0f, 0.30f, 0f, 0f); Era3Manager.Instance?.OnDecisionResolved("d3_bioweapon_option"); } },
                new GeneChoice { Label = "Restrict to Defense — no offensive capacity",
                    Apply = agent => { agent.AcquiredGenes.Add("d3_bioweapon_option"); Era3Manager.Instance?.OnDecisionResolved("d3_bioweapon_option"); } },
            }
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "d3_caste_labor",
            IsEligible = agent => Era3Manager.Instance != null && Era3Manager.Instance.IsActive
                && agent.communityId == 0
                && Era3Manager.Instance.PlayerCiv.Has("e3_social_stratification")
                && !Era3Manager.Instance.PlayerCiv.Has("d3_caste_labor"),
            Choices = new[]
            {
                new GeneChoice { Label = "Production Focus — maximize output",
                    Apply = agent => {
                        agent.AcquiredGenes.Add("d3_caste_labor");
                        var arch = Era3Manager.Instance!.PlayerCiv.Architecture;
                        if (arch == CognitiveArchitecture.Collective) Era3Manager.Instance.SetCasteAllocation(0, 0.7f, 0.2f, 0.1f);
                        else Era3Manager.Instance.SetSectorAllocation(0, 0.65f, 0.2f, 0.15f);
                        Era3Manager.Instance.OnDecisionResolved("d3_caste_labor"); } },
                new GeneChoice { Label = "Military Focus — coercive expansion",
                    Apply = agent => {
                        agent.AcquiredGenes.Add("d3_caste_labor");
                        var arch = Era3Manager.Instance!.PlayerCiv.Architecture;
                        if (arch == CognitiveArchitecture.Collective) Era3Manager.Instance.SetCasteAllocation(0, 0.3f, 0.2f, 0.5f);
                        else Era3Manager.Instance.SetSectorAllocation(0, 0.3f, 0.55f, 0.15f);
                        Era3Manager.Instance.OnDecisionResolved("d3_caste_labor"); } },
                new GeneChoice { Label = "Culture Focus — ideas and legitimacy",
                    Apply = agent => {
                        agent.AcquiredGenes.Add("d3_caste_labor");
                        var arch = Era3Manager.Instance!.PlayerCiv.Architecture;
                        if (arch == CognitiveArchitecture.Collective) Era3Manager.Instance.SetCasteAllocation(0, 0.4f, 0.4f, 0.2f);
                        else Era3Manager.Instance.SetSectorAllocation(0, 0.3f, 0.2f, 0.5f);
                        Era3Manager.Instance.OnDecisionResolved("d3_caste_labor"); } },
            }
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "d3_large_initiative_1",
            IsEligible = agent => Era3Manager.Instance != null && Era3Manager.Instance.IsActive
                && agent.communityId == 0
                && Era3Manager.Instance.PlayerCiv.Has("e3_surplus_economy")
                && !Era3Manager.Instance.PlayerCiv.Has("d3_large_initiative_1"),
            Choices = new[]
            {
                new GeneChoice { Label = "Vaccination Drive — drain disease crises",
                    Apply = agent => {
                        agent.AcquiredGenes.Add("d3_large_initiative_1");
                        Era3Manager.Instance?.PlayerCiv.RecoverResilience(0.10f);
                        Era3Manager.Instance?.OnDecisionResolved("d3_large_initiative_1"); } },
                new GeneChoice { Label = "Trade Expansion — open new partner routes",
                    Apply = agent => {
                        agent.AcquiredGenes.Add("d3_large_initiative_1");
                        Era3Manager.Instance?.SetTradePolicy(0, 0.10f, 0.80f);
                        Era3Manager.Instance?.OnDecisionResolved("d3_large_initiative_1"); } },
                new GeneChoice { Label = "Monument — culture + legitimacy boost",
                    Apply = agent => {
                        agent.AcquiredGenes.Add("d3_large_initiative_1");
                        var civ = Era3Manager.Instance?.PlayerCiv;
                        if (civ != null) civ.InvestReligion = Mathf.Min(civ.InvestReligion + 0.15f, 1f);
                        Era3Manager.Instance?.OnDecisionResolved("d3_large_initiative_1"); } },
            }
        });
    }
}
