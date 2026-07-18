using UnityEngine;

/// Computes environment-conditioned outcome preview strings for gene event choices,
/// per outcome-preview-annotations_1.md. Era 1 = 3-tier (Favorable/Marginal/Unfavorable);
/// Era 2 = 5-tier magnitude. Returns null if no preview is defined for the gene.
/// Array is indexed to GeneDefinition.Choices (use originalIdx from visible list).
public static class GeneEventPreview
{
    /// One computed preview cell: the localization key for its outcome string, plus the tier the
    /// environment math produced (0 = best) and the scale it was rated on (3 for Era 1, 5 for Era 2).
    /// Carrying the tier lets the UI color a choice by favorability instead of throwing the tier away.
    public readonly struct Px
    {
        public readonly string Key;
        public readonly int Tier;
        public readonly int Scale;
        public Px(string key, int tier, int scale) { Key = key; Tier = tier; Scale = scale; }
    }

    /// Localized outcome strings, parallel-indexed to GeneDefinition.Choices. Null if no preview.
    public static string[] Get(string geneId, AgentController agent)
    {
        var cells = Compute(geneId, agent);
        if (cells == null) return null;
        var res = new string[cells.Length];
        for (int i = 0; i < cells.Length; i++) res[i] = LocalizationManager.L(cells[i].Key);
        return res;
    }

    /// Per-choice favorability normalized to 0..1 "goodness" (1 = most favorable under current local
    /// conditions, 0 = least), parallel-indexed to Get(). Null if no preview is defined. Both the
    /// Era 1 3-tier and Era 2 5-tier scales collapse onto the same 0..1 range so one color mapping
    /// works for all genes. NOTE: this reflects IMMEDIATE local fitness, not long-term strategy.
    public static float[] GetFavorability(string geneId, AgentController agent)
    {
        var cells = Compute(geneId, agent);
        if (cells == null) return null;
        var res = new float[cells.Length];
        for (int i = 0; i < cells.Length; i++)
        {
            int last = cells[i].Scale - 1;                     // T3 → 2, T5 → 4
            res[i] = last <= 0 ? 1f : 1f - (float)cells[i].Tier / last;
        }
        return res;
    }

    static Px[] Compute(string geneId, AgentController agent)
    {
        if (agent == null) return null;
        return geneId switch
        {
            // ── Era 1 ────────────────────────────────────────────────────────────
            "PhotosynthesisEmergence"           => PhotosynthesisPreview(agent),
            "EfficientRespiration"              => RespCrisisPreview(agent),
            "MotilityEmergence"                 => MotilityPreview(agent),
            "GermLayerComplexity"               => GermLayerPreview(agent),
            "ProtectiveStructureEmergence"      => ProtectiveStructurePreview(agent),
            "ReproductiveStrategyShift"         => ReproductivePreview(agent),
            "SequentialHermaphroditism"         => HermaphroditismPreview(agent),
            "SensoryOrganDevelopment"           => SensoryPreview(agent),
            "LocomotorAppendageDevelopment"     => LocomotorPreview(agent),
            "KingdomFork"                       => KingdomForkPreview(agent),
            "ManipulationAppendageDevelopment"  => ManipulationPreview(agent),
            "ArticulatedAppendages"             => ArticulatedPreview(agent),
            "DexterousAppendages"               => DexterousPreview(agent),
            "SocialityEmergence"                => SocialityPreview(agent),
            "GroupFormation"                    => GroupFormationPreview(agent),
            "Mixotrophy"                        => MixotrophyPreview(agent),
            // ── Era 2 ────────────────────────────────────────────────────────────
            "CognitiveInvestmentStrategy"       => CognitivePreview(agent),
            "CommunicationMedium"               => CommMediumPreview(agent),
            "NicheConstructionOrientation"      => NichePreview(agent),
            "MetabolicAllocation"               => MetaAllocPreview(agent),
            "SocialStructure"                   => SocialStructurePreview(agent),
            "CommunicationCodeification"        => CommCodePreview(agent),
            "GlidingAdaptation"                 => GlidingPreview(agent),
            "AerialLocomotion"                  => AerialPreview(agent),
            _ => null
        };
    }

    // ── Tier helpers ─────────────────────────────────────────────────────────────

    // Era 1: 3-tier. ratio [0,1]: >= 0.60 = favorable (0), >= 0.35 = marginal (1), else unfavorable (2)
    static int T3(float ratio) => ratio >= 0.60f ? 0 : ratio >= 0.35f ? 1 : 2;

    // Era 2: 5-tier. far more (0) / more (1) / about as much (2) / less (3) / far less (4)
    static int T5(float ratio) => ratio >= 0.78f ? 0 : ratio >= 0.58f ? 1 : ratio >= 0.38f ? 2 : ratio >= 0.20f ? 3 : 4;

    static Px L3(string prefix, string opt, int tier) =>
        new Px($"px_{prefix}_{opt}_{tier}", tier, 3);
    static Px L5(string prefix, string opt, int tier) =>
        new Px($"px_{prefix}_{opt}_{tier}", tier, 5);

    // ── Environmental reads ───────────────────────────────────────────────────────

    // WorldSolarFluxFactor: L/d² normalized so Earth-at-1AU = 1. Clamp to [0,1] for tier math.
    static float SolarFlux() => Mathf.Clamp01(AgentController.WorldSolarFluxFactor / 1.5f);

    // Breathed gas fraction: proxy for how viable the adapted atmosphere path is.
    static float BreathedFraction(AgentController a)
    {
        if (AtmosphereManager.Instance == null) return 0.5f;
        float f = AtmosphereManager.Instance.GetFraction(a.BreathedGasName);
        return Mathf.Clamp01(f * 4f); // 25%+ breathed gas = max score
    }

    // Vent / chemical richness: inversely proportional to resource scarcity
    static float VentRichness(AgentController a) => Mathf.Clamp01(1f - a.ResourceScarcity);

    // ── Era 1 ─────────────────────────────────────────────────────────────────────

    // Choices[0]=Photosynthesize, Choices[1]=Remain Chemosynthetic
    static Px[] PhotosynthesisPreview(AgentController a) => new[]
    {
        L3("photo", "photo", T3(SolarFlux())),
        L3("photo", "chemo", T3(VentRichness(a))),
    };

    // Choices[0]=Tolerate aerobic, Choices[1]=Retreat to refuge
    static Px[] RespCrisisPreview(AgentController a)
    {
        float gasRich = BreathedFraction(a);
        float refugeSafe = Mathf.Clamp01(1f - a.PredationPressure - a.StressLevel / 120f);
        return new[]
        {
            L3("resp", "adapt", T3(gasRich)),
            L3("resp", "hide",  T3(refugeSafe)),
        };
    }

    // Choices[0]=Motile, Choices[1]=Sessile
    static Px[] MotilityPreview(AgentController a) => new[]
    {
        L3("moti", "move",    T3(a.ResourceScarcity)),           // high scarcity → moving helps
        L3("moti", "sessile", T3(1f - a.ResourceScarcity)),
    };

    // Choices[0]=Three layers (triploblastic), Choices[1]=Remain diploblastic
    static Px[] GermLayerPreview(AgentController a) => new[]
    {
        L3("germ", "three", T3(a.PredationPressure)),
        L3("germ", "two",   T3(1f - a.PredationPressure)),
    };

    // Choices[0]=Exoskeleton, [1]=Shell, [2]=Endoskeleton, [3]=Soft-body
    static Px[] ProtectiveStructurePreview(AgentController a)
    {
        float pred  = a.PredationPressure;
        float speed = Mathf.Clamp01(a.speedTrait / 50f);
        float mass  = Mathf.Clamp01(a.CurrentMass / 4f);
        return new[]
        {
            L3("prot", "exo",   T3(pred)),
            L3("prot", "shell", T3(Mathf.Lerp(pred, 1f - speed, 0.5f))),
            L3("prot", "endo",  T3(Mathf.Lerp(1f - pred, mass, 0.35f))),
            L3("prot", "soft",  T3(speed)),
        };
    }

    // Choices[0]=Asexual, Choices[1]=Sexual
    static Px[] ReproductivePreview(AgentController a) => new[]
    {
        L3("repro", "asex",   T3(1f - a.PathogenPressure)),
        L3("repro", "sexual", T3(a.PathogenPressure)),
    };

    // Choices[0]=Hermaphroditic, Choices[1]=Fixed sex
    static Px[] HermaphroditismPreview(AgentController a) => new[]
    {
        L3("herm", "hermap", T3(a.MateScarcity)),
        L3("herm", "fixed",  T3(1f - a.MateScarcity)),
    };

    // Choices[0]=Acute vision, Choices[1]=Wide-field vision
    static Px[] SensoryPreview(AgentController a)
    {
        // High predation + low speed → directional ambush threats → favors acute
        // High predation + high speed → open-field threats → favors wide-field
        float acuteScore = a.PredationPressure * (1f - Mathf.Clamp01(a.speedTrait / 55f));
        float wideScore  = a.PredationPressure * Mathf.Clamp01(a.speedTrait / 55f);
        return new[]
        {
            L3("sens", "acute", T3(acuteScore)),
            L3("sens", "wide",  T3(wideScore)),
        };
    }

    // Choices[0]=Fast limbs, Choices[1]=Powerful limbs
    static Px[] LocomotorPreview(AgentController a)
    {
        bool isConsumer = a.AcquiredGenes.Contains("KingdomFork");
        float pred = a.PredationPressure;
        // Consumers hunting prey: powerful; non-consumers being hunted: fast
        float fastScore  = isConsumer ? Mathf.Clamp01(pred * 0.4f + 0.1f) : pred;
        float powerScore = isConsumer ? Mathf.Clamp01(pred * 0.7f + 0.2f) : 1f - pred;
        return new[]
        {
            L3("loco", "fast",  T3(fastScore)),
            L3("loco", "power", T3(powerScore)),
        };
    }

    // Choices[0]=Producer, Choices[1]=Consumer
    static Px[] KingdomForkPreview(AgentController a) => new[]
    {
        L3("king", "prod", T3(1f - a.ResourceScarcity)),
        // High scarcity + predation pressure = competitive dense ecosystem = prey available
        L3("king", "cons", T3(Mathf.Clamp01(a.ResourceScarcity * 0.6f + a.PredationPressure * 0.4f))),
    };

    // Choices[0]=Simple appendages, Choices[1]=Remain without
    static Px[] ManipulationPreview(AgentController a) => new[]
    {
        L3("mani", "simple", T3(1f - a.ResourceScarcity)),      // lots nearby = worth reaching for
        L3("mani", "none",   T3(Mathf.Clamp01(a.StressLevel / 60f))), // high stress = can't afford overhead
    };

    // Choices[0]=Articulated, Choices[1]=Retain simple
    static Px[] ArticulatedPreview(AgentController a)
    {
        float handling = Mathf.Clamp01((1f - a.ResourceScarcity) * 1.1f);
        float thrift   = Mathf.Clamp01(a.StressLevel / 55f);
        return new[]
        {
            L3("arti", "artic",  T3(handling)),
            L3("arti", "simple", T3(thrift)),
        };
    }

    // Choices[0]=Dexterous, Choices[1]=Retain articulated
    static Px[] DexterousPreview(AgentController a)
    {
        float precision = Mathf.Clamp01((1f - a.ResourceScarcity) * 1.2f);
        float thrift    = Mathf.Clamp01(a.StressLevel / 65f);
        return new[]
        {
            L3("dext", "dext",  T3(precision)),
            L3("dext", "artic", T3(thrift)),
        };
    }

    // Choices[0]=Aggregate, Choices[1]=Solitary
    static Px[] SocialityPreview(AgentController a) => new[]
    {
        L3("soci", "agg",  T3(a.PredationPressure)),
        L3("soci", "solo", T3(a.ResourceScarcity)),      // high scarcity → sharing hurts
    };

    // Choices[0]=Group Formation, Choices[1]=Retain aggregation
    static Px[] GroupFormationPreview(AgentController a)
    {
        float coordBenefit = a.PredationPressure * Mathf.Clamp01(1f - a.StressLevel / 80f);
        return new[]
        {
            L3("grpf", "group", T3(coordBenefit)),
            L3("grpf", "agg",   T3(1f - coordBenefit)),
        };
    }

    // Choices[0]=Mixotrophic, Choices[1]=Stay phototrophic
    static Px[] MixotrophyPreview(AgentController a)
    {
        float light = Mathf.Lerp(SolarFlux(), 1f - a.ResourceScarcity, 0.4f);
        return new[]
        {
            L3("mixo", "mixo",  T3(1f - light)),    // mixo favored when light unreliable
            L3("mixo", "photo", T3(light)),
        };
    }

    // ── Era 2 ─────────────────────────────────────────────────────────────────────

    // Choices[0]=Social breadth, [1]=Manipulative spec, [2]=Scale
    static Px[] CognitivePreview(AgentController a)
    {
        bool groupForming = a.Sociality == SocialityBaseline.GroupForming;
        bool dexterous    = a.Manipulation == ManipulationLevel.Dexterous;
        return new[]
        {
            L5("e2cog", "soc",  T5(groupForming ? 0.72f : 0.35f)),
            L5("e2cog", "mani", T5(dexterous    ? 0.72f : 0.35f)),
            L5("e2cog", "scal", T5(Mathf.Clamp01(a.LifetimeEats / 80f))),
        };
    }

    // Choices[0]=Vocal, [1]=Visual, [2]=Chemical, [3]=Bioelectric
    static Px[] CommMediumPreview(AgentController a)
    {
        bool liquid  = FluidDynamicsManager.Instance != null;
        bool lowTemp = PlanetTemperature.Instance != null && PlanetTemperature.Instance.CurrentK < 250f;
        return new[]
        {
            L5("e2comm", "voc",  T5(liquid ? 0.40f : 0.72f)),
            L5("e2comm", "vis",  T5(liquid ? 0.30f : 0.62f)),
            L5("e2comm", "chem", T5(liquid ? 0.65f : 0.32f)),
            L5("e2comm", "bio",  T5(lowTemp ? 0.60f : 0.25f)),
        };
    }

    // Choices[0]=Tool-Based, [1]=Environment-Modification, [2]=Social-Transmission
    static Px[] NichePreview(AgentController a)
    {
        bool dext = a.Manipulation == ManipulationLevel.Dexterous;
        bool grp  = a.Sociality == SocialityBaseline.GroupForming;
        return new[]
        {
            L5("e2nic", "tool", T5(dext ? 0.72f : 0.30f)),
            L5("e2nic", "env",  T5(Mathf.Clamp01(a.StressLevel / 55f + 0.25f))),
            L5("e2nic", "soc",  T5(grp  ? 0.65f : 0.35f)),
        };
    }

    // Choices[0]=Brain, [1]=Balanced, [2]=Body
    static Px[] MetaAllocPreview(AgentController a)
    {
        bool grp = a.Sociality == SocialityBaseline.GroupForming;
        return new[]
        {
            L5("e2met", "brain", T5(grp ? 0.68f : 0.45f)),
            L5("e2met", "bal",   T5(0.50f)),          // always marginal
            L5("e2met", "body",  T5(Mathf.Clamp01(a.PredationPressure + 0.25f))),
        };
    }

    // Choices[0]=Pair-bonded, [1]=Troop, [2]=Fission-fusion, [3]=Solitary-territorial
    static Px[] SocialStructurePreview(AgentController a)
    {
        float pred = a.PredationPressure;
        float scar = a.ResourceScarcity;
        return new[]
        {
            L5("e2sst", "pair", T5(Mathf.Clamp01(scar * 0.5f + 0.28f))),
            L5("e2sst", "trp",  T5(Mathf.Clamp01(pred * 0.75f + 0.18f))),
            L5("e2sst", "fiss", T5(Mathf.Clamp01(0.28f + (1f - Mathf.Abs(pred - scar)) * 0.45f))),
            L5("e2sst", "solo", T5(Mathf.Clamp01(scar * 0.4f + (1f - pred) * 0.35f))),
        };
    }

    // Choices[0]=Oral, Choices[1]=Recorded
    static Px[] CommCodePreview(AgentController a)
    {
        float retention = Mathf.Clamp01(a.LifetimeEats / 60f + 0.15f);
        return new[]
        {
            L5("e2cod", "oral", T5(1f - retention)),
            L5("e2cod", "rec",  T5(retention)),
        };
    }

    // Choices[0]=Develop gliding, Choices[1]=Forego gliding
    static Px[] GlidingPreview(AgentController a)
    {
        float aerial = Mathf.Clamp01(a.ResourceScarcity * 0.55f + a.PredationPressure * 0.45f);
        return new[]
        {
            L5("e2gli", "glide",  T5(aerial * 0.70f)),   // gliding cheaper → slightly more likely favorable
            L5("e2gli", "ground", T5(1f - aerial)),
        };
    }

    // Choices[0]=Powered flight, Choices[1]=Remain grounded
    static Px[] AerialPreview(AgentController a)
    {
        float aerial = Mathf.Clamp01(a.ResourceScarcity * 0.50f + a.PredationPressure * 0.50f);
        return new[]
        {
            L5("e2aer", "fly",    T5(aerial * 0.55f)),   // powered flight expensive → cost weighted down
            L5("e2aer", "ground", T5(1f - aerial)),
        };
    }
}
