using System.Collections.Generic;
using System.Text;

/// Appearance descriptor (appearance-generation-spec §2.4) — the generator output format the
/// renderer consumes directly. Built as a pure, deterministic function of an organism's current
/// state (never authored/edited directly by the player), matching §1's core principle:
/// STATE + SEED → GENERATOR → GEOMETRY. This is the typed C# object other systems (rendering,
/// save/load, and the future §3.4 Historical Record UI) read; ToYamlString() additionally
/// reproduces the spec's own human-readable shape for logging/debugging/save-file purposes.
public class AppearanceDescriptor
{
    public string SpeciesId;
    public string Symmetry;
    public string StructuralSupport;
    public string Segmentation;
    public string SizeClass;
    public string Locomotion;

    public string AppendageClass;
    public int LocomotorPairs;
    public int ManipulatorPairs;
    public string TerminalLocomotor;
    public string TerminalManipulator;

    public string EyeType;
    public int EyeCount;
    public string EyePlacement;

    public string Feeding;
    public string MouthType;

    // Named "Integument" rather than "IntegumentType" to avoid shadowing the IntegumentType enum
    // (referenced by type below in IntegumentToString) within this class's scope.
    public string Integument;
    // Awaiting the circulatory-chromophore spec (referenced by appearance-generation-spec §2.4 but
    // never itself implemented in this codebase) — left null rather than fabricating a color system
    // this descriptor doesn't own.
    public string ColorPrimary;
    public string ColorAccent;

    public string Neural;
    public bool ToolCeiling;
    public bool VocalApparatus;
    public List<string> CommunicationOptions = new List<string>();

    // appearance-generation-spec §3.1: "a new sociality field feeding Section 3.2" — the herd/group
    // formation signature (schooling/territorial-dispersed/defensive-herd) this lineage's current
    // SocialityBaseline produces. See AgentController.ComputeSocialAggregationBias for the actual
    // movement-steering implementation this field describes.
    public string Sociality;

    /// "grounded" for ordinary carbon biochemistry, "speculative" for exotic backbones (§2.4).
    public string Confidence;

    public static AppearanceDescriptor Build(AgentController agent)
    {
        var d = new AppearanceDescriptor
        {
            SpeciesId = $"lineage_0x{(uint)agent.MorphSeedValue:X}",
            Symmetry = SymmetryToString(agent.GetEffectiveSymmetry()),
            StructuralSupport = StructuralSupportToString(agent.BodyPlan),
            Segmentation = SegmentationToString(agent.Segmentation),
            SizeClass = agent.BodySizeClass.ToString().ToLowerInvariant(),
            Locomotion = LocomotionToString(agent),

            AppendageClass = AppendageClassToString(agent),
            LocomotorPairs = agent.LocomotorPairs,
            ManipulatorPairs = agent.ManipulatorPairs,
            TerminalLocomotor = TerminalLocomotorToString(agent),
            TerminalManipulator = agent.ManipulatorPairs > 0 ? TerminalManipulatorToString(agent) : "none",

            Feeding = FeedingToString(agent.Feeding),
            MouthType = MouthTypeToString(agent.Feeding),

            Integument = IntegumentToString(agent.Integument),
            ColorPrimary = null,
            ColorAccent = null,

            Neural = NeuralToString(agent.NeuralComplexity),
            ToolCeiling = agent.ManipulatorPairs >= 1,
            VocalApparatus = agent.VocalApparatus,
            Sociality = SocialityToString(agent.Sociality),

            Confidence = agent.Backbone == BackboneElement.Carbon ? "grounded" : "speculative",
        };

        d.EyeType = EyeTypeToString(agent);
        d.EyeCount = EyeCountFor(agent);
        d.EyePlacement = EyePlacementFor(agent);

        d.CommunicationOptions.Add("chemical-pheromonal"); // baseline — always available
        if (agent.VocalApparatus) d.CommunicationOptions.Add("vocal-auditory");
        if (agent.PrimarySense == SensoryModality.Visual || agent.PrimarySense == SensoryModality.Multimodal || agent.visionTrait >= 40f)
            d.CommunicationOptions.Add("visual-gestural");
        if (agent.PrimarySense == SensoryModality.Electroreceptive || agent.PrimarySense == SensoryModality.Multimodal)
            d.CommunicationOptions.Add("bioluminescent-electrical");

        return d;
    }

    // ── Enum → spec-vocabulary string mapping ──────────────────────────────────────────────

    private static string SymmetryToString(MorphologyGenerator.Symmetry s) => s switch
    {
        MorphologyGenerator.Symmetry.Radial => "radial",
        MorphologyGenerator.Symmetry.Bilateral => "bilateral",
        MorphologyGenerator.Symmetry.Biradial => "biradial",
        MorphologyGenerator.Symmetry.Asymmetric => "asymmetric",
        MorphologyGenerator.Symmetry.ColonialModular => "colonial-modular",
        _ => "asymmetric",
    };

    // BodyPlanType.None and .SoftBody both collapse to the spec's single "hydrostatic" M3 value —
    // the code keeps them as two distinct historical states (see the enum's own doc comment) but
    // the spec's value set doesn't distinguish them.
    private static string StructuralSupportToString(BodyPlanType b) => b switch
    {
        BodyPlanType.None => "hydrostatic",
        BodyPlanType.SoftBody => "hydrostatic",
        BodyPlanType.Exoskeleton => "exo-chitin",
        BodyPlanType.Shell => "exo-mineral",
        BodyPlanType.Endoskeleton => "endo-cartilage",
        BodyPlanType.EndoMineralized => "endo-mineralized",
        BodyPlanType.MixedArmor => "mixed-armor",
        BodyPlanType.Crystalline => "crystalline",
        _ => "hydrostatic",
    };

    private static string SegmentationToString(SegmentationType s) => s switch
    {
        SegmentationType.Unsegmented => "unsegmented",
        SegmentationType.Metameric => "metameric",
        SegmentationType.Tagmatized => "tagmatized",
        SegmentationType.SecondarilySimplified => "secondarily-simplified",
        _ => "unsegmented",
    };

    private static string LocomotionToString(AgentController agent)
    {
        return agent.LocomotionMedium switch
        {
            LocomotionMedium.Sessile => "sessile",
            LocomotionMedium.Aquatic => "pelagic",
            LocomotionMedium.Terrestrial => "terrestrial-walking",
            LocomotionMedium.Gliding => "aerial-gliding",
            LocomotionMedium.Aerial => "aerial-flapping",
            _ => "terrestrial-walking",
        };
    }

    // Best-effort read of appendage CLASS (M5) from the existing dexterity-tier (Manipulation) and
    // rig-treatment (BodyPlan) axes — no separate appendage-class state is tracked, matching §1's
    // "state is derived, not authored" principle rather than adding a fully-redundant parallel axis.
    private static string AppendageClassToString(AgentController agent)
    {
        bool rigidSupport = agent.BodyPlan == BodyPlanType.Exoskeleton || agent.BodyPlan == BodyPlanType.Shell
            || agent.BodyPlan == BodyPlanType.Endoskeleton || agent.BodyPlan == BodyPlanType.EndoMineralized
            || agent.BodyPlan == BodyPlanType.MixedArmor || agent.BodyPlan == BodyPlanType.Crystalline;

        if (agent.LocomotionMedium == LocomotionMedium.Aerial || agent.LocomotionMedium == LocomotionMedium.Gliding)
            return agent.ManipulatorPairs > 0 ? "wings-separate" : "wings-from-limbs";

        return agent.Manipulation switch
        {
            ManipulationLevel.None => "none",
            ManipulationLevel.Simple => "pseudopods",
            ManipulationLevel.Articulated => rigidSupport ? "jointed-limbs" : (agent.LocomotionMedium == LocomotionMedium.Aquatic ? "fins" : "tentacles-hydrostatic"),
            ManipulationLevel.Dexterous => rigidSupport ? "jointed-limbs" : "tentacles-hydrostatic",
            _ => "none",
        };
    }

    private static string TerminalLocomotorToString(AgentController agent) => agent.Manipulation switch
    {
        ManipulationLevel.Dexterous => "grasping digit",
        ManipulationLevel.Articulated => "claw",
        ManipulationLevel.Simple => "blunt pad",
        _ => "none",
    };

    private static string TerminalManipulatorToString(AgentController agent) => agent.Manipulation switch
    {
        ManipulationLevel.Dexterous => "prehensile digits",
        ManipulationLevel.Articulated => "grasping chelicera",
        _ => "none",
    };

    private static string FeedingToString(FeedingApparatus f) => f switch
    {
        FeedingApparatus.FilterPassive => "filter-passive",
        FeedingApparatus.Grazer => "grazer",
        FeedingApparatus.Detritivore => "detritivore",
        FeedingApparatus.PredatorActive => "predator-active",
        FeedingApparatus.Parasitic => "parasitic",
        FeedingApparatus.Chemosymbiotic => "chemosymbiotic",
        FeedingApparatus.Photosymbiotic => "photosymbiotic",
        _ => "filter-passive",
    };

    private static string MouthTypeToString(FeedingApparatus f) => f switch
    {
        FeedingApparatus.PredatorActive => "piercing-proboscis",
        FeedingApparatus.Grazer => "scraping-radula",
        FeedingApparatus.Detritivore => "sifting-mouthparts",
        FeedingApparatus.Parasitic => "attachment-sucker",
        FeedingApparatus.Chemosymbiotic => "reduced-vestigial",
        FeedingApparatus.Photosymbiotic => "reduced-vestigial",
        _ => "filter-comb",
    };

    private static string IntegumentToString(IntegumentType t) => t switch
    {
        IntegumentType.BareMucous => "bare-mucous",
        IntegumentType.Scales => "scales",
        IntegumentType.Chitin => "chitin",
        IntegumentType.FilamentsFur => "filaments-fur",
        IntegumentType.ShellExternal => "shell-external",
        IntegumentType.Crystalline => "crystalline",
        _ => "bare-mucous",
    };

    // appearance-generation-spec §3.2's three named formation signatures, mapped from the same
    // SocialityBaseline value AgentController.ComputeFormationBias's default branch actually steers
    // movement by — Schooling=GroupForming, Defensive-herd=Aggregating, Territorial-dispersed=Solitary.
    private static string SocialityToString(SocialityBaseline s) => s switch
    {
        SocialityBaseline.Solitary     => "territorial-dispersed",
        SocialityBaseline.Aggregating  => "defensive-herd",
        SocialityBaseline.GroupForming => "schooling",
        _ => "territorial-dispersed",
    };

    private static string NeuralToString(NeuralComplexityStage n) => n switch
    {
        NeuralComplexityStage.DiffuseSignaling => "diffuse-nerve-net",
        NeuralComplexityStage.NerveNet => "distributed-ganglia",
        NeuralComplexityStage.NerveCord => "ventral-cord",
        NeuralComplexityStage.GanglionicCephalization => "dorsal-CNS",
        NeuralComplexityStage.HighlyCentralized => "highly-centralized",
        _ => "diffuse-nerve-net",
    };

    private static string EyeTypeToString(AgentController agent)
    {
        if (agent.PrimarySense != SensoryModality.Visual && agent.PrimarySense != SensoryModality.Multimodal) return "none";
        return agent.visionTrait >= 70f ? "compound" : "simple-ocular";
    }

    private static int EyeCountFor(AgentController agent)
    {
        if (agent.PrimarySense != SensoryModality.Visual && agent.PrimarySense != SensoryModality.Multimodal) return 0;
        return agent.GetEffectiveSymmetry() == MorphologyGenerator.Symmetry.Radial ? 4 : 2;
    }

    private static string EyePlacementFor(AgentController agent)
    {
        if (agent.PrimarySense != SensoryModality.Visual && agent.PrimarySense != SensoryModality.Multimodal) return "none";
        return agent.GetEffectiveSymmetry() switch
        {
            MorphologyGenerator.Symmetry.Radial => "dorsal-radial",
            MorphologyGenerator.Symmetry.ColonialModular => "none",
            _ => "lateral-anterior",
        };
    }

    /// Reproduces the spec's own §2.4 YAML shape — for logging/save-file/debug purposes. The
    /// typed object above is what actual code (rendering, future Historical Record UI) reads.
    public string ToYamlString()
    {
        var sb = new StringBuilder();
        sb.Append("species_id: ").Append(SpeciesId).Append('\n');
        sb.Append("symmetry: ").Append(Symmetry).Append('\n');
        sb.Append("structural_support: ").Append(StructuralSupport).Append('\n');
        sb.Append("segmentation: ").Append(Segmentation).Append('\n');
        sb.Append("size_class: ").Append(SizeClass).Append('\n');
        sb.Append("locomotion: ").Append(Locomotion).Append('\n');
        sb.Append("appendages:\n");
        sb.Append("  class: ").Append(AppendageClass).Append('\n');
        sb.Append("  locomotor_pairs: ").Append(LocomotorPairs).Append('\n');
        sb.Append("  manipulator_pairs: ").Append(ManipulatorPairs).Append('\n');
        sb.Append("  terminal_locomotor: ").Append(TerminalLocomotor).Append('\n');
        sb.Append("  terminal_manipulator: ").Append(TerminalManipulator).Append('\n');
        sb.Append("eyes:\n");
        sb.Append("  type: ").Append(EyeType).Append('\n');
        sb.Append("  count: ").Append(EyeCount).Append('\n');
        sb.Append("  placement: ").Append(EyePlacement).Append('\n');
        sb.Append("feeding: ").Append(Feeding).Append('\n');
        sb.Append("mouth_type: ").Append(MouthType).Append('\n');
        sb.Append("integument:\n");
        sb.Append("  type: ").Append(Integument).Append('\n');
        sb.Append("  color_primary: ").Append(ColorPrimary ?? "null # awaiting circulatory-chromophore spec").Append('\n');
        sb.Append("  color_accent: ").Append(ColorAccent ?? "null # awaiting circulatory-chromophore spec").Append('\n');
        sb.Append("neural: ").Append(Neural).Append('\n');
        sb.Append("tool_ceiling: ").Append(ToolCeiling ? "true" : "false").Append('\n');
        sb.Append("vocal_apparatus: ").Append(VocalApparatus ? "true" : "false").Append('\n');
        sb.Append("sociality: ").Append(Sociality).Append('\n');
        sb.Append("communication_options: [").Append(string.Join(", ", CommunicationOptions)).Append("]\n");
        sb.Append("confidence: ").Append(Confidence).Append('\n');
        return sb.ToString();
    }
}
