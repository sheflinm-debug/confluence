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
            IsEligible = agent => agent.LifetimeEats >= agent.sensoryGeneEatThreshold,
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
            IsEligible = agent => agent.LifetimeEats >= agent.locomotorGeneEatThreshold,
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
            // Thematically a DNA-repair/cell-machinery follow-on to the sensory/locomotor
            // genes: only an agent that has endured SUSTAINED adversity (not a single bad
            // moment) "discovers" sexual reproduction as a viable alternative.
            // TODO: swap this proxy out for AgentController.StressLevel (a per-agent
            // sustained-stress accumulator) once that task lands - it's the intended real
            // gate per the design discussion. Until then, age + lifetime eats stands in as
            // a rough "has survived a long, hard while" proxy.
            IsEligible = agent => agent.AgeSeconds >= agent.reproductiveShiftAgeThreshold
                && agent.LifetimeEats >= agent.reproductiveShiftEatThreshold,
            Choices = new[]
            {
                new GeneChoice { Label = "Remain Asexual (clone with mutation drift)", Apply = agent => agent.BecomeAsexual() },
                new GeneChoice { Label = "Shift to Sexual Reproduction (blend traits with a mate)", Apply = agent => agent.BecomeSexual() }
            },
            // Background agents that mature after the player has already chosen default to
            // the conservative/unchanged option (stay asexual).
            DefaultAutoApply = agent => agent.BecomeAsexual()
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "EfficientRespiration",
            Prerequisites = new[] { "Multicellularity" },
            // Only eligible after the Great Gas Event fires (AtmosphereManager sets this flag).
            IsEligible = agent => AtmosphereManager.Instance != null && AtmosphereManager.Instance.GreatGasEventFired,
            AutoApply = agent =>
            {
                // Survivors get a starvation-time bonus (more efficient O2 use).
                agent.starvationTime *= 1.5f;
            }
        });

        GeneEvolutionManager.Register(new GeneDefinition
        {
            Id = "KingdomFork",
            Prerequisites = new[] { "Multicellularity" },
            IsEligible = agent => agent.AgeSeconds >= agent.kingdomForkAgeThreshold,
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
    }
}
