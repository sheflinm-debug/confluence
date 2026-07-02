using System.Collections.Generic;
using UnityEngine;

public enum MetabolismType
{
    Chemosynthetic, // default: absorbs dissolved chemicals from ChemicalNutrientPool
    Phototrophic,   // photosynthesis: harvests solar energy (evolved later)
    Heterotrophic,  // consumer: osmotrophy → saprotrophy → predation/herbivory
    Mixotrophic,    // combines photosynthesis + heterotrophy at ~70% efficiency each
}

// Manipulation tier: how dexterous the organism's appendages are (Era 2 input).
/// Locomotion medium axis (morphology axis M4). New values added by gene events.
public enum LocomotionMedium
{
    Sessile,    // no self-directed movement
    Terrestrial,// ground locomotion (default for motile land organisms)
    Aquatic,    // swimming (default for motile aquatic organisms)
    Gliding,    // passive membrane — controlled descent, not powered flight (e2_gliding_adaptation)
    Aerial,     // powered flight (e2_aerial_locomotion_emergence)
}

public enum ManipulationLevel
{
    None,        // no manipulators — sessile, flagella only
    Simple,      // cilia, pseudopods — can push/pull but not grip
    Articulated, // limbs, tentacles — can grip, carry, use tools crudely
    Dexterous,   // grasping digits, prehensile structures — precision manipulation
}

// Social baseline: how naturally this lineage forms group structures (Era 2 input).
public enum SocialityBaseline
{
    Solitary,     // default — no attraction to conspecifics
    Aggregating,  // passive clustering near same-community members; quorum sensing
    GroupForming, // active cohesion, coordinated behavior, division of roles
}

// Neural complexity stage: how sophisticated the organism's signaling architecture is.
// Drives Era 2 intelligence index starting value.
public enum NeuralComplexityStage
{
    DiffuseSignaling  = 0, // chemical gradients only — no discrete nervous structures
    NerveNet          = 1, // distributed nerve net (jellyfish equivalent)
    NerveCord         = 2, // centralized cord / ganglia (flatworm/annelid equivalent)
    GanglionicCephalization = 3, // cephalic ganglion — proto-brain (pre-vertebrate)
}

public enum HabitatMedium
{
    Sea,  // submerged in liquid — drift driven by liquid currents, UV attenuated
    Land, // above liquid surface on terrain — drift driven by wind
    Air,  // airborne (flying, future era) — not yet implemented, treated as Land
}

/// Protective structure evolved at the Cambrian body-plan fork (Era 1).
/// Inherited by all offspring; drives stat trade-offs and Era 2 manipulation bonuses.
public enum BodyPlanType
{
    None,
    Exoskeleton, // hardened cuticle: str+, hard+, speed-
    Shell,       // mineralized test: hard++, speed--
    Endoskeleton,// internal skeleton: str++, speed neutral/+; requires germ layers
    SoftBody,    // no protection: speed+
}

public enum BiologicalSex
{
    Asexual, // default before ReproductiveStrategyShift fires
    Male,
    Female,
}

public class AgentController : MonoBehaviour
{
    /// Set once at world genesis (SimulationBootstrap) from SolarSystemDef: L/d² normalized
    /// so Earth at 1 AU = 1. Shared by all agents; no per-organism copy needed.
    public static float WorldSolarFluxFactor = 1f;

    [Header("Sphere")]
    [HideInInspector] public Vector3 planetCenter;
    [HideInInspector] public float planetRadius;

    [Header("Movement")]
    public float turnSpeed = 8f;

    [Header("Sensing")]
    public float eatRadius = 0.5f;

    [Header("Wander")]
    public float wanderTurnRate = 60f; // max degrees/sec random heading change

    [Header("Energy efficiency traits (Tier 1 lineage, evolvable via SI drift)")]
    [Tooltip("Photosynthetic conversion efficiency. Seeded at ~15% of the C3 ceiling (0.046). SI drift pushes it upward over generations.")]
    public float photoEfficiencySpawn = 0.007f;  // 15% of 0.046 ceiling
    [Tooltip("Chemosynthetic uptake efficiency. Tunable design constant ceiling.")]
    public float chemoEfficiencySpawn = 0.15f;
    [Tooltip("Heterotrophic assimilation efficiency base [0.2, 0.9]. Carnivore-biased mid-range default.")]
    public float assimilationEfficiencySpawn = 0.55f;

    // Efficiency property ceilings — SI drift cannot exceed these.
    public const float PhotoEfficiencyCeiling  = 0.046f; // C3 photosynthesis theoretical max
    public const float ChemoEfficiencyCeiling  = 0.80f;  // tunable design constant
    public const float AssimEfficiencyMin      = 0.20f;
    public const float AssimEfficiencyMax      = 0.90f;

    /// Evolvable photosynthetic efficiency [0, PhotoEfficiencyCeiling]. Inherited by offspring; drifts on SI events.
    public float PhotoEfficiency        { get; set; }
    /// Evolvable chemosynthetic uptake efficiency [0, ChemoEfficiencyCeiling]. Inherited; drifts on SI events.
    public float ChemoEfficiency        { get; set; }
    /// Evolvable assimilation efficiency [AssimEfficiencyMin, AssimEfficiencyMax]. Inherited; drifts on SI events.
    public float AssimilationEfficiency { get; set; }

    [Header("Trait dimensions (0-100, Section 6a)")]
    public float visionTrait = 50f;
    public float speedTrait = 50f;
    public float strengthTrait = 50f; // currently inert (Section 7 strength-value formula, no predation/combat yet)
    public float hardinessTrait = 50f; // generalist (high) vs specialist (low) climate tolerance breadth
    public float temperaturePreference = 50f; // preferred local temperature
    public float moisturePreference = 50f;    // preferred local moisture

    [Header("Trait -> world-unit mapping")]
    public float minSenseRadius = 2f;
    public float maxSenseRadius = 16f;
    public float minMoveSpeed = 0.5f;
    public float maxMoveSpeed = 5f;

    [Header("Reproduction")]
    public int eatsToReproduce = 3;
    public float mutationStdDev = 5f; // offspring trait drift from parent, per Section 6a drift framing
    public float offspringSpawnOffset = 1.5f;

    [Header("Lifespan")]
    [Tooltip("Mean maximum age in seconds before senescence death. Actual lifespan is randomized ±40% per individual so generations overlap rather than dying in lockstep.")]
    public float meanLifespan = 120f;
    [Tooltip("Hardiness above 50 extends max lifespan (up to +25%); below 50 shortens it (down to -25%). Models the generalist/specialist tradeoff: hardy generalists are longer-lived.")]
    public float lifespanHardinessInfluence = 0.25f;

    [Header("Starvation / Solar energy")]
    public float starvationTime = 15f; // seconds without eating before death (consumers)
    [Tooltip("Max solar energy a producer can accumulate (seconds of survival).")]
    public float maxSolarEnergy = 30f;
    [Tooltip("Rate at which producers gain solar energy on the day side (energy/sec at full solar exposure).")]
    public float solarChargeRate = 2f;
    [Tooltip("Rate at which producers drain energy at night (energy/sec).")]
    public float solarDrainRate = 0.5f;

    [Header("Climate fitness pressure")]
    [Tooltip("Baseline strength of climate-mismatch's effect on starvation rate, before hardiness scaling.")]
    public float climateFitnessMultiplierRange = 0.8f;
    [Tooltip("How much hardiness can widen (specialist) or narrow (generalist) the baseline range above.")]
    public float hardinessRangeMin = 0.3f; // multiplier at hardiness=100 (generalist - shallow penalty)
    public float hardinessRangeMax = 1.6f; // multiplier at hardiness=0 (specialist - steep penalty)

    [Header("Comfort-seeking (territoriality)")]
    [Tooltip("How strongly discomfort biases wander direction toward better-matching climate.")]
    public float comfortSeekingStrength = 2.5f;
    public float comfortSampleDistance = 2f; // how far ahead to sample candidate climate

    [Header("UV tolerance")]
    [Tooltip("0-100 randomized per agent: 0 = deep-water origin (max UV harm), 100 = surface-adapted.")]
    public Vector2 uvToleranceSpawnRange = new Vector2(5f, 50f);

    [Header("Pressure tolerance")]
    [Tooltip("0-100: how well the organism handles deviation from its natal atmospheric pressure.")]
    public Vector2 pressureToleranceSpawnRange = new Vector2(20f, 80f);

    [Header("Thermal cycle tolerance")]
    [Tooltip("0-100: how well the organism handles day/night temperature swings.")]
    public Vector2 thermalCycleToleranceSpawnRange = new Vector2(20f, 80f);

    [Header("Atmospheric fitness pressure")]
    [Tooltip("Baseline strength of atmosphere-mismatch's effect on survival, before tolerance scaling.")]
    public float atmosFitnessMultiplierRange = 1.2f;
    public float toleranceRangeMin = 0.3f; // multiplier at gasTolerance=100 (generalist - shallow penalty)
    public float toleranceRangeMax = 2.2f; // multiplier at gasTolerance=0 (specialist - steep penalty)
    [Tooltip("0-100 randomized per agent at spawn; how tolerant this lineage is of deviation from its locked-in ideal gas mix.")]
    public Vector2 gasToleranceSpawnRange = new Vector2(20f, 80f);

    [Header("Stress accumulation (sustained-adversity state, 0-100)")]
    [Tooltip("0-100 randomized per agent at spawn; how much accumulated stressLevel it takes to cause a given amount of extra harm. High = resilient (shallow penalty), low = fragile (steep penalty) - same generalist/specialist role hardinessTrait and gasTolerance play for their own discomfort calcs.")]
    public Vector2 stressToleranceSpawnRange = new Vector2(20f, 80f);
    [Tooltip("Seconds of sustained max adversity (climate + atmosphere + near-starvation all maxed) for stressLevel to climb to ~95% of its ceiling. Picked so stress reflects a PROLONGED hardship, not a single bad tick - roughly a third of the default starvationTime, so a starving agent's stress is meaningfully elevated by the time it actually starves.")]
    public float stressRiseTime = 5f;
    [Tooltip("Seconds of zero adversity for stressLevel to decay back down to ~5% of its prior value. Slower than the rise (recovery lags onset), so a population under intermittent stress trends upward over time rather than fully resetting between bad patches.")]
    public float stressDecayTime = 10f;
    [Tooltip("Baseline strength of stressLevel's effect on starvation rate, before stressTolerance scaling. Kept modest relative to climateFitnessMultiplierRange/atmosFitnessMultiplierRange since stress is a derived/secondary pressure layered on top of those, not a replacement for them.")]
    public float stressFitnessMultiplierRange = 0.5f;
    public float stressToleranceRangeMin = 0.3f; // multiplier at stressTolerance=100 (resilient - shallow penalty)
    public float stressToleranceRangeMax = 1.8f; // multiplier at stressTolerance=0 (fragile - steep penalty)

    [Header("Atmospheric speciation")]
    [Tooltip("Atmospheric discomfort (0-1) above which speciation becomes possible. Raised from 0.25 - the old value let the whole population cross threshold together as the atmosphere drifted, producing a single burst of many simultaneous color changes instead of a gradual trickle.")]
    public float speciationStressThreshold = 0.45f;
    [Tooltip("Chance per second (scaled by excess stress) that a stressed individual re-locks its ideal mix to the current atmosphere, founding a new lineage. Lowered from 0.15 for the same reason.")]
    public float speciationChanceScale = 0.025f;

    [Header("Gene events (Section 6b)")]
    [Tooltip("Random per-agent eat-count thresholds so genes don't all fire in the same order or at the same time.")]
    public Vector2Int sensoryGeneEatThresholdRange = new Vector2Int(4, 9);
    public Vector2Int locomotorGeneEatThresholdRange = new Vector2Int(4, 9);
    // Lowered from (20,60): the Kingdom Fork (photosynthesis vs heterotrophy) is a
    // primordial metabolic choice that should happen very early in the Abiogenesis era,
    // well before the default 15 s starvation clock runs out on the first consumer.
    public Vector2 kingdomForkAgeThresholdRange = new Vector2(5f, 12f); // seconds
    // Stand-in "sustained adversity" gate for ReproductiveStrategyShift until a real
    // per-agent stress accumulator exists - see GeneCatalog.cs TODO.
    public Vector2 reproductiveShiftAgeThresholdRange = new Vector2(30f, 70f); // seconds
    public Vector2Int reproductiveShiftEatThresholdRange = new Vector2Int(6, 12);

    /// Genes this agent's lineage has already acquired (inherited by offspring).
    public readonly HashSet<string> AcquiredGenes = new HashSet<string>();

    public int LifetimeEats => _lifetimeEats;
    public float AgeSeconds { get; private set; }

    // Randomized per-agent trigger thresholds, set at spawn - see GeneCatalog.
    public int sensoryGeneEatThreshold;
    public int locomotorGeneEatThreshold;
    public float kingdomForkAgeThreshold;
    public float reproductiveShiftAgeThreshold;
    public int reproductiveShiftEatThreshold;

    // Current metabolic strategy. Defaults to Chemosynthetic (absorbs dissolved organics).
    // Evolves to Phototrophic via PhotosynthesisEmergence gene, or Heterotrophic via KingdomFork.
    public MetabolismType Metabolism { get; private set; } = MetabolismType.Chemosynthetic;

    // Computed from Metabolism for backwards compatibility with AtmosphereManager etc.
    // Mixotrophic is both producer and consumer (handled separately in update).
    public bool IsProducer => Metabolism == MetabolismType.Chemosynthetic
                           || Metabolism == MetabolismType.Phototrophic
                           || Metabolism == MetabolismType.Mixotrophic;

    // Set by the Kingdom Fork gene (Photosynthesis vs Heterotroph) - see GeneCatalog.
    public string Kingdom { get; private set; }

    // Set by the MotilityEmergence gene (e1_motility_emergence in spec).
    // False = passive drifter (wind-carried, no seeking); true = self-directed movement.
    // Consumer behavior and predation hard-require this to be true.
    public bool HasMotility { get; private set; }
    /// Current locomotion medium — updated by gene events.
    public LocomotionMedium LocomotionMedium { get; private set; } = LocomotionMedium.Sessile;
    /// Hard mass ceiling for flight eligibility (addendum §1.3). Evaluated live each frame.
    /// Organisms that grow past this ceiling lose flight eligibility dynamically.
    public const float FlightMassCeiling = 0.008f; // tune during playtesting

    // Set by the Reproductive Strategy Shift gene - see GeneCatalog. Asexual (false, the
    // default) clones a single parent with mutation drift; Sexual (true) requires finding
    // an opposite-sex mate in range and blends both parents' traits before drift is applied.
    public bool IsSexual { get; private set; }

    // Biological sex: Asexual until IsSexual is true, then Male or Female assigned when
    // BecomeSexual() fires. Re-rolled randomly for each offspring so sex ratio drifts
    // naturally and can create the imbalance that triggers SequentialHermaphroditism.
    public BiologicalSex Sex { get; private set; } = BiologicalSex.Asexual;

    // Set by the SequentialHermaphroditism gene. When true, organism autonomously switches
    // sex in response to local population imbalance, improving mating availability.
    public bool CanChangeSex { get; private set; }
    private float _sexSwitchTimer;

    // Which spawned community this agent belongs to (0 = player community).
    public int communityId;

    // Atmospheric adaptation (speciation): the gas-fraction mix this lineage is adapted
    // to, locked in at genesis and re-locked whenever AttemptAtmosphericSpeciation fires.
    // Inherited by offspring (see InheritGenesFrom) - NOT resampled every birth.
    private Dictionary<string, float> _idealGasMix = new Dictionary<string, float>();
    public float gasTolerance = 50f;
    public string AtmoLineage { get; private set; } = "Primordial";

    // Sustained-adversity accumulator (STATE, not a trait): builds up gradually under
    // combined climate/atmosphere/starvation pressure and decays gradually when conditions
    // improve - see UpdateStressLevel. Intended gate for a future "Reproductive Strategy
    // Shift" gene event (see GeneCatalog.cs TODO) once sustained hardship should be able to
    // push a lineage toward sexual reproduction, not just age + lifetime eats.
    public float StressLevel { get; private set; }
    private bool _stressRegistered;

    // 0-100 randomized per agent at spawn; how tolerant this individual is of its OWN
    // accumulated stressLevel (resilient generalist vs fragile specialist) - same
    // generalist/specialist role hardinessTrait and gasTolerance play for their discomfort calcs.
    public float stressTolerance = 50f;

    // World-unit values derived from the trait dimensions above.
    public float senseRadius { get; private set; }
    public float moveSpeed { get; private set; }

    // Founder/lineage color (Tier 2: one of 8-10 distinct founder hues; inherited by
    // offspring unchanged until Tier 3 visual speciation is implemented).
    public Color lineageColor = Color.white;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    /// Which physical medium this organism currently occupies. Updated every frame from
    /// FluidDynamicsManager liquid depth. Drives drift source (current vs wind) and
    /// medium-mismatch fitness penalty.
    public HabitatMedium CurrentMedium { get; private set; } = HabitatMedium.Sea;

    // True if this lineage is aquatic (adapted to live in liquid). Set at spawn for early
    // eras; transitions to false when a TerrestrialAdaptation gene is acquired (future).
    private bool _isAquatic = true;

    private Vector3 _heading; // tangent direction, world space
    private CorpseSpawner _corpseSpawner;
    private AgentSpawner _spawner;
    private int _eatsSinceReproduction;
    private float _timeSinceLastMeal;
    private int _lifetimeEats;
    private bool _traitsRegistered;
    private float _solarEnergy; // phototrophic producers use this
    private float _chemEnergy;  // chemosynthetic organisms use this

    // Simulated body mass (game-unit kg). Drives Kleiber BMR demand and biomass transfer
    // on predation. Initialized from era scale; grows/shrinks with sustained net balance.
    // Viability floor = SpawnMass * MassViabilityFloor — death below this threshold.
    private float _currentMass;
    private float _spawnMass;
    private const float MassViabilityFloor = 0.10f; // 10% of spawn mass = death threshold
    private const float MassGrowthRate     = 0.02f; // fraction of spawn mass gained per second of positive surplus
    private const float MassShrinkRate     = 0.03f; // fraction of spawn mass lost per second of negative surplus

    /// Simulated body mass in game units. Read by Kleiber demand, biomass-transfer (§7), and surface-area terms.
    public float CurrentMass => _currentMass;

    // Environmental pressure variables — refreshed every 3 s to avoid per-frame O(N) cost.
    private float _pressureRefreshTimer;
    private const float PressureRefreshInterval = 3f;
    /// 0 = abundant chemical energy; 1 = substrate fully depleted (drives metabolic innovation).
    public float ResourceScarcity  { get; private set; }
    /// Inverse of local same-community population density — high value = few nearby mates (mate-search difficulty).
    /// Primary trigger for SequentialHermaphroditism (low-density mate-limitation signal).
    public float MateScarcity      { get; private set; }
    /// 0 = no predators nearby; 1 = overwhelmingly consumer-dominated (drives armor/escape).
    public float PredationPressure { get; private set; }
    /// 0 = sparse population; 1 = dense (density is the real-world proxy for pathogen load).
    public float PathogenPressure  { get; private set; }

    // Flee state: tracks the current threat and how long to keep fleeing after losing sight.
    private AgentController _fleeTarget;
    private float _fleeCooldown;

    // Sexual isolation: counts seconds without any opposite-sex community member present.
    // When this exceeds the threshold, the lineage is ecologically extinct and the organism dies.
    private float _noMateTimer;
    private float _noMateCheckTimer; // throttle the O(N) scan to every 5 s

    // New tolerance traits — inherited, not re-rolled at birth.
    public float uvTolerance;            // 0-100: vulnerability to UV radiation
    public float pressureTolerance;      // 0-100: pressure generalist vs specialist
    public float thermalCycleTolerance;  // 0-100: day/night thermal swing tolerance
    public float pressurePreference;     // bar: natal atmospheric pressure, locked at spawn

    // Randomized per agent at spawn; influenced by hardinessTrait so specialists die younger.
    private float _maxLifespan;

    // Locked survival needs — set once at Init from world state, inherited by offspring.
    private string _breathedGasName      = "";  // Name of the Breathed gas at genesis
    private float  _minBreathableFraction;       // fraction below which asphyxia begins
    private string _requiredLiquidKind   = "";  // liquid Name this lineage evolved in

    public string BreathedGasName    => _breathedGasName;
    public string RequiredLiquidKind => _requiredLiquidKind;

    // Backbone element: set from AtmosphereManager.RolledBiochemistry at genesis,
    // inherited by all offspring. Drives gas-effect compatibility and Era 2 neural pathway.
    public BackboneElement Backbone { get; private set; } = BackboneElement.Carbon;

    // Era 1 trait axes — set by gene events, inherited by offspring.
    public ManipulationLevel Manipulation { get; private set; } = ManipulationLevel.None;
    public SocialityBaseline Sociality    { get; private set; } = SocialityBaseline.Solitary;
    public NeuralComplexityStage NeuralComplexity { get; private set; } = NeuralComplexityStage.DiffuseSignaling;

    // Era 1 body-plan attributes — set by protective structure gene events, inherited.
    public BodyPlanType BodyPlan          { get; private set; } = BodyPlanType.None;
    public bool HasGermLayers             { get; private set; } = false; // triploblastic development
    public bool IsAnoxicRefugeLineage     { get; private set; } = false; // retreated to anoxic refuges at GOE

    public void Init(Vector3 center, float radius, CorpseSpawner corpseSpawner, AgentSpawner spawner,
        float visionTraitValue, float speedTraitValue, float strengthTraitValue, float hardinessTraitValue,
        float temperaturePreferenceValue, float moisturePreferenceValue, int community = 0, Color? color = null)
    {
        planetCenter = center;
        planetRadius = radius;
        _corpseSpawner = corpseSpawner;
        _spawner = spawner;
        // Grace period: start starve-clock negative so new agents (especially the very
        // first organism, which has no food yet) have time for KingdomFork to fire.
        _timeSinceLastMeal = -starvationTime;
        _solarEnergy = maxSolarEnergy * 0.5f;
        _chemEnergy  = maxSolarEnergy * 0.5f; // all organisms start with half chemical reserve
        _pressureRefreshTimer = Random.Range(0f, PressureRefreshInterval); // stagger refreshes
        AgeSeconds = 0f;
        communityId = community;

        lineageColor = color ?? Color.white;
        ApplyLineageColor();

        // Lock in the current atmosphere as this lineage's "ideal mix" (genesis adaptation).
        // Offspring overwrite this via InheritGenesFrom rather than resampling at birth.
        _idealGasMix = AtmosphereManager.Instance != null ? AtmosphereManager.Instance.SnapshotMix() : new Dictionary<string, float>();
        gasTolerance = Random.Range(gasToleranceSpawnRange.x, gasToleranceSpawnRange.y);
        stressTolerance = Random.Range(stressToleranceSpawnRange.x, stressToleranceSpawnRange.y);

        StressLevel = 0f;
        PopulationStats.RegisterStress(StressLevel);
        PopulationStats.RegisterStressTolerance(stressTolerance);
        _stressRegistered = true;

        // Default: all primordial organisms are chemosynthetic — absorbing dissolved
        // organic chemicals from the ocean/vent pool. Photosynthesis evolves later
        // via the PhotosynthesisEmergence gene; heterotrophy via KingdomFork.
        Metabolism = MetabolismType.Chemosynthetic;

        // UV: early life evolved in deep water to escape surface radiation — spawn
        // with low-to-moderate UV tolerance. Pressure locked to natal atmosphere.
        uvTolerance           = Random.Range(uvToleranceSpawnRange.x, uvToleranceSpawnRange.y);
        pressureTolerance     = Random.Range(pressureToleranceSpawnRange.x, pressureToleranceSpawnRange.y);
        thermalCycleTolerance = Random.Range(thermalCycleToleranceSpawnRange.x, thermalCycleToleranceSpawnRange.y);
        pressurePreference    = AtmosphereManager.Instance != null ? AtmosphereManager.Instance.PressureBar : 1f;

        // Baseline genes assumed already acquired before this sim begins (Section 14a -
        // the sim starts post-eukaryotic-transition, not at the literal origin of life).
        AcquiredGenes.Add("Nucleus");
        AcquiredGenes.Add("Multicellularity");

        // Randomized per-agent thresholds so genes don't all fire in the same order or
        // at the same time across the population (Section 14e).
        sensoryGeneEatThreshold = Random.Range(sensoryGeneEatThresholdRange.x, sensoryGeneEatThresholdRange.y + 1);
        locomotorGeneEatThreshold = Random.Range(locomotorGeneEatThresholdRange.x, locomotorGeneEatThresholdRange.y + 1);
        kingdomForkAgeThreshold = Random.Range(kingdomForkAgeThresholdRange.x, kingdomForkAgeThresholdRange.y);
        reproductiveShiftAgeThreshold = Random.Range(reproductiveShiftAgeThresholdRange.x, reproductiveShiftAgeThresholdRange.y);
        reproductiveShiftEatThreshold = Random.Range(reproductiveShiftEatThresholdRange.x, reproductiveShiftEatThresholdRange.y + 1);

        SetTraits(visionTraitValue, speedTraitValue, strengthTraitValue, hardinessTraitValue, temperaturePreferenceValue, moisturePreferenceValue);

        // Lifespan: randomized ±40% around the mean, then scaled by hardiness so generalists
        // (high hardiness) live longer and specialists (low hardiness) burn bright but brief.
        float hardinessLifeBonus = Mathf.Lerp(-lifespanHardinessInfluence, lifespanHardinessInfluence, hardinessTrait / 100f);
        _maxLifespan = meanLifespan * (1f + hardinessLifeBonus) * Random.Range(0.6f, 1.4f);

        // Lock survival needs from current world state so offspring inherit them and can
        // accumulate asphyxiation pressure if the atmosphere drifts away from the genesis mix.
        _breathedGasName = "";
        if (AtmosphereManager.Instance != null)
            foreach (var g in AtmosphereManager.Instance.Gases)
                if (g.Role == GasRole.Breathed) { _breathedGasName = g.Name; break; }
        _minBreathableFraction = Mathf.Lerp(0.12f, 0.03f, hardinessTrait / 100f);
        _requiredLiquidKind = FluidDynamicsManager.Instance?.CurrentLiquid?.Name ?? "";

        // Backbone: derive from the world's rolled biochemistry (set once at genesis).
        Backbone = AtmosphereManager.Instance?.RolledBiochemistry?.Backbone ?? BackboneElement.Carbon;

        Vector3 normal = (transform.position - planetCenter).normalized;
        _heading = Vector3.Cross(normal, Random.onUnitSphere).normalized;
        AlignToSurface();

        // Apply current era's visual scale so new agents (including offspring) always
        // spawn at the right size, not at the prefab's default size.
        if (EraManager.Instance != null)
        {
            transform.localScale = Vector3.one * EraManager.Instance.AgentTargetScale;

            // In early eras (Abiogenesis / Prokaryotic) bias toward wet areas — life
            // begins in liquid, not on dry land. moisturePreference=90 drives comfort-
            // seeking toward the wettest available terrain.
            if (EraManager.Instance.CurrentEra <= 1)
                moisturePreference = 90f;
        }

        // Seed current_mass from visual scale — era scale (0.05–0.32) maps to game-unit mass.
        // Cubed because scale is a linear dimension and mass scales with volume.
        float s = transform.localScale.x;
        _spawnMass   = s * s * s;
        _currentMass = _spawnMass;

        // Seed efficiency traits at spawn values. InheritGenesFrom overwrites these for
        // offspring, so founders get the inspector defaults; children inherit parent values.
        PhotoEfficiency        = photoEfficiencySpawn;
        ChemoEfficiency        = chemoEfficiencySpawn;
        AssimilationEfficiency = assimilationEfficiencySpawn;
    }

    /// Sets this agent's trait dimensions and registers them with the live population stats.
    /// Safe to call again later (e.g. a gene event changing traits post-spawn) - the
    /// previously registered values are removed first so the population mean stays accurate.
    public void SetTraits(float visionTraitValue, float speedTraitValue, float strengthTraitValue, float hardinessTraitValue,
        float temperaturePreferenceValue, float moisturePreferenceValue)
    {
        if (_traitsRegistered)
        {
            PopulationStats.UnregisterVision(visionTrait);
            PopulationStats.UnregisterSpeed(speedTrait);
            PopulationStats.UnregisterStrength(strengthTrait);
            PopulationStats.UnregisterHardiness(hardinessTrait);
        }

        visionTrait = Mathf.Clamp(visionTraitValue, 0f, 100f);
        speedTrait = Mathf.Clamp(speedTraitValue, 0f, 100f);
        strengthTrait = Mathf.Clamp(strengthTraitValue, 0f, 100f);
        hardinessTrait = Mathf.Clamp(hardinessTraitValue, 0f, 100f);
        temperaturePreference = Mathf.Clamp(temperaturePreferenceValue, 0f, 100f);
        moisturePreference = Mathf.Clamp(moisturePreferenceValue, 0f, 100f);

        senseRadius = Mathf.Lerp(minSenseRadius, maxSenseRadius, visionTrait / 100f);
        moveSpeed = Mathf.Lerp(minMoveSpeed, maxMoveSpeed, speedTrait / 100f);

        PopulationStats.RegisterVision(visionTrait);
        PopulationStats.RegisterSpeed(speedTrait);
        PopulationStats.RegisterStrength(strengthTrait);
        PopulationStats.RegisterHardiness(hardinessTrait);
        _traitsRegistered = true;
    }

    private void ApplyLineageColor()
    {
        Renderer r = GetComponentInChildren<Renderer>();
        if (r == null) return;
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetColor(BaseColorId, lineageColor);
        block.SetColor(ColorId, lineageColor);
        r.SetPropertyBlock(block);
    }

    /// Natural death (starvation, energy depletion, atmosphere crisis): leaves a
    /// decaying corpse for scavengers before removing this agent. Direct predation
    /// kills (see UpdateConsumer) skip this - the prey is eaten immediately instead.
    public void Die()
    {
        if (_corpseSpawner != null) _corpseSpawner.SpawnCorpseAt(transform.position, _currentMass);
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        PopulationStats.UnregisterVision(visionTrait);
        PopulationStats.UnregisterSpeed(speedTrait);
        PopulationStats.UnregisterStrength(strengthTrait);
        PopulationStats.UnregisterHardiness(hardinessTrait);
        if (_stressRegistered)
        {
            PopulationStats.UnregisterStress(StressLevel);
            PopulationStats.UnregisterStressTolerance(stressTolerance);
        }
        if (_spawner != null) _spawner.Unregister(this);
    }

    void Update()
    {
        AgeSeconds += Time.deltaTime;
        if (AgeSeconds >= _maxLifespan) { Die(); return; }

        // Update habitat medium before movement so drift source and fitness multipliers
        // are based on where the organism actually is this frame.
        CurrentMedium = FluidDynamicsManager.Instance != null && FluidDynamicsManager.Instance.IsSubmerged(transform.position)
            ? HabitatMedium.Sea
            : HabitatMedium.Land;

        UpdateStressLevel();
        UpdatePressureVariables();
        ApplyMediumMismatchDrain();
        CheckSexualIsolation();
        CheckGasSurvival();
        CheckBackboneGasTolerance();

        if (!HasMotility)
            UpdatePassiveDrift();
        else if (IsProducer)
            UpdateProducer();
        else
            UpdateConsumer();

        // Separation only matters for motile organisms; drifters are carried by the same
        // currents so packing them is fine and the O(N²) check is wasted on sessile life.
        if (HasMotility) ApplySeparation();
        AttemptAtmosphericSpeciation();
        MaybeChangeSex();
        GeneEvolutionManager.CheckEligibleGenes(this);
    }

    /// Passive dispersal: no directed locomotion. Medium determines the carrying agent:
    /// Sea → liquid current (viscosity-scaled, identity-specific per LiquidDef);
    /// Land → wind (WindManager). A small Brownian component prevents perfect clumping
    /// in both cases. Producers still metabolize while drifting.
    private void UpdatePassiveDrift()
    {
        Vector3 normal = (transform.position - planetCenter).normalized;
        bool inSea = CurrentMedium == HabitatMedium.Sea;

        if (inSea)
        {
            // Liquid current: gradient-descent direction weighted by liquid's FlowSpeedFactor.
            // Organisms in slow liquids (molten sulfur) barely move; fast liquids (methane) carry them quickly.
            Vector3 current = FluidDynamicsManager.Instance != null
                ? FluidDynamicsManager.Instance.GetLiquidCurrentAt(transform.position)
                : Vector3.zero;

            if (current.sqrMagnitude > 0.0001f)
                _heading = Vector3.Slerp(_heading, current.normalized, 1.5f * Time.deltaTime).normalized;
            else
            {
                // Still water / equilibrium basin: slow Brownian diffusion.
                float angle = Random.Range(-wanderTurnRate * 0.15f, wanderTurnRate * 0.15f) * Time.deltaTime;
                _heading = (Quaternion.AngleAxis(angle, normal) * _heading).normalized;
            }
        }
        else
        {
            // Land / air: wind-carried.
            Vector3 wind = WindManager.GetWind(transform.position);
            Vector3 windTangent = wind - Vector3.Dot(wind, normal) * normal;

            if (windTangent.sqrMagnitude > 0.001f)
                _heading = Vector3.Slerp(_heading, windTangent.normalized, 2f * Time.deltaTime).normalized;
            else
            {
                float angle = Random.Range(-wanderTurnRate * 0.2f, wanderTurnRate * 0.2f) * Time.deltaTime;
                _heading = (Quaternion.AngleAxis(angle, normal) * _heading).normalized;
            }
        }

        // Liquid drag: organisms in high-viscosity media move more slowly.
        // FlowSpeedFactor ≈ 1.0 for water, 0.02 for molten sulfur.
        float liquidDrag = (inSea && FluidDynamicsManager.Instance?.CurrentLiquid != null)
            ? FluidDynamicsManager.Instance.CurrentLiquid.FlowSpeedFactor
            : 1.0f;

        // Sessile organisms can't override their medium-driven drift, but if a predator is
        // immediately adjacent, a weak flee impulse can redirect the drift heading.
        float fleeWeight = RefreshFleeState(out Vector3 fleeDir);
        if (fleeWeight > 0.3f)
            _heading = Vector3.Slerp(_heading, fleeDir, fleeWeight * 0.5f * Time.deltaTime).normalized;

        float eraMult = EraManager.Instance != null ? EraManager.Instance.MoveSpeedMultiplier : 1f;
        // Sessile organisms can drift slightly faster when threatened (turbulence response).
        float fleeBoost = 1f + fleeWeight * 0.3f;
        Vector3 newPos = SphereSurface.MoveAlongSurface(
            transform.position, _heading, moveSpeed * eraMult * 0.12f * liquidDrag * fleeBoost * Time.deltaTime,
            planetCenter, planetRadius);
        transform.position = newPos;
        AlignToSurface();

        switch (Metabolism)
        {
            case MetabolismType.Chemosynthetic: UpdateChemosyntheticMetabolism(); break;
            case MetabolismType.Phototrophic:   UpdateProducerMetabolism();       break;
            case MetabolismType.Heterotrophic:  UpdateOsmotrophy();               break;
            case MetabolismType.Mixotrophic:    UpdateMixotrophicMetabolism();    break;
        }
    }

    // Soft repulsion: push apart any two agents closer than their combined radii.
    // Uses surface distance so the separation force stays tangent to the sphere.
    // Only checks agents already in _spawner.ActiveAgents — no extra broad-phase needed
    // since populations are small and we short-circuit at sense radius.
    private void ApplySeparation()
    {
        if (_spawner == null) return;
        float agentRadius = transform.localScale.x * 0.5f;
        float minSep = agentRadius * 2f;
        const float repulseStrength = 4f; // world units per second at full overlap
        Vector3 myPos = transform.position;
        Vector3 myNormal = (myPos - planetCenter).normalized;

        foreach (var other in _spawner.ActiveAgents)
        {
            if (other == null || other == this) continue;
            float otherRadius = other.transform.localScale.x * 0.5f;
            float requiredSep = agentRadius + otherRadius;
            float surfDist = SphereSurface.SurfaceDistance(myPos, other.transform.position, planetCenter, planetRadius);
            if (surfDist >= requiredSep || surfDist < 0.001f) continue;

            // Push direction: tangent away from the other agent along the sphere surface.
            Vector3 awayWorld = (myPos - other.transform.position).normalized;
            Vector3 tangentAway = (awayWorld - Vector3.Dot(awayWorld, myNormal) * myNormal).normalized;
            if (tangentAway.sqrMagnitude < 0.0001f) continue;

            float overlap = requiredSep - surfDist;
            float push = repulseStrength * (overlap / requiredSep);
            Vector3 newPos = SphereSurface.MoveAlongSurface(myPos, tangentAway, push * Time.deltaTime, planetCenter, planetRadius);
            transform.position = newPos;
            myPos = newPos;
            myNormal = (myPos - planetCenter).normalized;
        }
    }

    /// Public read-only view of this agent's current atmospheric stress, for HUD display.
    public float AtmosphericDiscomfort => GetAtmosphericDiscomfort();

    /// 0 (current atmosphere matches this lineage's locked-in ideal mix exactly) .. 1
    /// (maximally divergent). Computed against EVERY gas present, not just one
    /// "poisonous"-tagged gas - any drift away from the ideal mix matters.
    private float GetAtmosphericDiscomfort()
    {
        if (AtmosphereManager.Instance == null || _idealGasMix.Count == 0) return 0f;
        float totalDiff = 0f;
        foreach (var gas in AtmosphereManager.Instance.Gases)
        {
            float idealFrac = _idealGasMix.TryGetValue(gas.Name, out float v) ? v : 0f;
            totalDiff += Mathf.Abs(gas.Fraction - idealFrac);
        }
        return Mathf.Clamp01(totalDiff / 2f); // max possible total diff between two distributions is 2
    }

    /// Same hardiness-style scaling as climate fitness: a generalist (high gasTolerance)
    /// takes a shallow penalty for a given mismatch, a specialist (low gasTolerance) a steep one.
    private float GetAtmosphericFitnessMultiplier()
    {
        float discomfort = GetAtmosphericDiscomfort();
        float effectiveRange = atmosFitnessMultiplierRange * Mathf.Lerp(toleranceRangeMax, toleranceRangeMin, gasTolerance / 100f);
        float multiplier = 1f + (discomfort * 2f - 1f) * effectiveRange;
        return Mathf.Clamp(multiplier, 0.2f, 3f);
    }

    /// Updates the sustained-adversity accumulator (StressLevel, 0-100). Each tick combines
    /// three independent adversity sources - climate discomfort, atmospheric discomfort, and
    /// starvation proximity - into a single 0-1 signal, then runs that signal through a
    /// leaky-bucket / exponential moving average so StressLevel rises gradually under
    /// sustained adversity and decays gradually when conditions improve, rather than
    /// spiking from one bad tick. Producers don't track _timeSinceLastMeal, so starvation
    /// proximity is omitted for them (climate + atmosphere only).
    private void UpdateStressLevel()
    {
        float climateDiscomfort  = GetDiscomfort(transform.position);
        float atmosphericDiscomfort = GetAtmosphericDiscomfort();
        float uvDiscomfort       = Mathf.Clamp01((GetUVFitnessMultiplier() - 1f) / 4f);
        float pressureDiscomfort = Mathf.Clamp01((GetPressureFitnessMultiplier() - 1f) / 2f);
        float thermalDiscomfort  = Mathf.Clamp01((GetThermalCycleFitnessMultiplier() - 1f) / 2f);
        float starvationProximity = IsProducer ? 0f : Mathf.Clamp01(_timeSinceLastMeal / starvationTime);

        float adversity = (climateDiscomfort + atmosphericDiscomfort + uvDiscomfort
                         + pressureDiscomfort + thermalDiscomfort + starvationProximity) / 6f;
        float targetStress = adversity * 100f; // 0-100 dimension, matching trait convention

        // Exponential moving average toward targetStress, with separate rise/decay rates so
        // stress climbs faster than it falls. tau ~= time to reach ~95% of the way to a new
        // target (3 time constants); using a per-frame factor keeps this framerate-independent.
        float tau = targetStress > StressLevel ? stressRiseTime : stressDecayTime;
        float alpha = tau > 0f ? 1f - Mathf.Exp(-Time.deltaTime / (tau / 3f)) : 1f;

        float newStress = Mathf.Lerp(StressLevel, targetStress, alpha);
        newStress = Mathf.Clamp(newStress, 0f, 100f);

        if (_stressRegistered)
        {
            PopulationStats.UnregisterStress(StressLevel);
            PopulationStats.RegisterStress(newStress);
        }
        StressLevel = newStress;
    }

    /// Refreshes ResourceScarcity, PredationPressure, and PathogenPressure every 3 s.
    /// Called from Update(); per-agent refreshes are staggered by a random offset set at Init
    /// so 350 agents don't all scan on the same frame.
    private void UpdatePressureVariables()
    {
        _pressureRefreshTimer -= Time.deltaTime;
        if (_pressureRefreshTimer > 0f) return;
        _pressureRefreshTimer = PressureRefreshInterval;

        ResourceScarcity = 1f - Mathf.Clamp01(ChemicalNutrientPool.Sample(transform.position) * 4f);

        if (_spawner == null) return;
        const float ScanRadius = 8f;
        int total = 0, consumers = 0, sameCommunity = 0;
        foreach (var a in _spawner.ActiveAgents)
        {
            if (a == null || a == this) continue;
            if (Vector3.Distance(transform.position, a.transform.position) > ScanRadius) continue;
            total++;
            if (!a.IsProducer && a.HasMotility) consumers++;
            if (a.communityId == communityId) sameCommunity++;
        }
        PredationPressure = Mathf.Clamp01(total > 0 ? (float)consumers / total : 0f);
        PathogenPressure  = Mathf.Clamp01(total / 12f);
        // MateScarcity: high when same-community density is low (mate-search difficulty).
        // Cap at 6 same-community neighbors = no scarcity; 0 neighbors = maximum scarcity.
        MateScarcity = 1f - Mathf.Clamp01(sameCommunity / 6f);
    }

    /// Same hardiness/gasTolerance-style scaling: a resilient individual (high
    /// stressTolerance) takes a shallow extra penalty for a given accumulated StressLevel,
    /// a fragile one (low stressTolerance) a steep one. This is an ADDITIONAL layer on top
    /// of GetClimateStarvationMultiplier/GetAtmosphericFitnessMultiplier, not a replacement -
    /// it makes sustained adversity compound into something worse than the sum of any one
    /// instantaneous discomfort, which is the whole point of tracking it separately.
    private float GetStressFitnessMultiplier()
    {
        float stress01 = StressLevel / 100f;
        float effectiveRange = stressFitnessMultiplierRange * Mathf.Lerp(stressToleranceRangeMax, stressToleranceRangeMin, stressTolerance / 100f);
        float multiplier = 1f + stress01 * effectiveRange; // stress only ever adds penalty, never a bonus
        return Mathf.Clamp(multiplier, 1f, 2.5f);
    }

    /// Speciation: under sustained atmospheric stress, a stressed individual has a random
    /// chance per second (scaling with how far past the stress threshold it is) to re-lock
    /// its ideal mix to the CURRENT atmosphere and found a new lineage. Skipped when
    /// SpeciationManager is active — it owns the timing in that case.
    private void AttemptAtmosphericSpeciation()
    {
        if (SpeciationManager.Instance != null) return; // SpeciationManager owns speciation

        if (AtmosphereManager.Instance == null) return;
        float discomfort = GetAtmosphericDiscomfort();
        float excess = discomfort - speciationStressThreshold;
        if (excess <= 0f) return;

        float chancePerSecond = excess * speciationChanceScale;
        if (Random.value < chancePerSecond * Time.deltaTime)
        {
            _idealGasMix = AtmosphereManager.Instance.SnapshotMix();
            gasTolerance = Mathf.Clamp(PopulationStats.SampleDimension(gasTolerance, mutationStdDev), 0f, 100f);
            AtmoLineage = KingdomNameGenerator.Generate();

            lineageColor = Color.HSVToRGB(Random.value, Random.Range(0.65f, 0.95f), Random.Range(0.85f, 1f));
            ApplyLineageColor();

            Debug.Log($"[Speciation] {name} adapted to the shifted atmosphere -> new lineage '{AtmoLineage}'");
        }
    }

    /// Called by SpeciationManager when the SI formula selects this agent to found a new
    /// lineage. Re-locks ideal gas mix, assigns new lineage name/color, and drifts 1–2
    /// traits to represent niche divergence.
    public void TriggerSpeciation(string lineageName, Color color)
    {
        _idealGasMix = AtmosphereManager.Instance != null
            ? AtmosphereManager.Instance.SnapshotMix()
            : new Dictionary<string, float>();
        gasTolerance = Mathf.Clamp(PopulationStats.SampleDimension(gasTolerance, mutationStdDev), 0f, 100f);
        AtmoLineage = lineageName;
        lineageColor = color;
        ApplyLineageColor();

        // Drift 1 or 2 randomly chosen traits to represent ecological niche divergence.
        int driftCount = Random.Range(1, 3);
        float[] current = { visionTrait, speedTrait, strengthTrait, hardinessTrait, temperaturePreference, moisturePreference };
        // Shuffle indices using Fisher-Yates.
        int[] indices = { 0, 1, 2, 3, 4, 5 };
        for (int i = indices.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int tmp = indices[i]; indices[i] = indices[j]; indices[j] = tmp;
        }
        for (int k = 0; k < driftCount; k++)
            current[indices[k]] = Mathf.Clamp(PopulationStats.SampleDimension(current[indices[k]], mutationStdDev * 2f), 0f, 100f);

        SetTraits(current[0], current[1], current[2], current[3], current[4], current[5]);
    }

    private void UpdateConsumer()
    {
        // Layer 1 — osmotrophy/saprotrophy: absorb dissolved organics passively.
        // This is the consumer fallback that bridges pre-predation abiogenesis.
        float nutrients = ChemicalNutrientPool.Sample(transform.position);
        float osmotrophySlowdown = Mathf.Clamp01(nutrients * 0.45f); // up to 45% starvation reduction
        if (nutrients > 0.01f)
            ChemicalNutrientPool.Deplete(transform.position, 0.00008f * Time.deltaTime);

        // Layer 2 — active foraging: predation hard-requires motility per spec
        // (e1_heterotrophic_predation_emergence needs e1_motility_emergence).
        AgentController preyTarget = HasMotility ? FindPreyInRange() : null;
        CorpseItem corpseTarget = preyTarget == null ? FindNearestCorpseInRange() : null;

        if (HasMotility)
        {
            // Even predators flee stronger predators (apex predator hierarchy).
            float fleeWeight = RefreshFleeState(out Vector3 fleeDir);

            Vector3 desiredTangent;
            if (fleeWeight > 0.7f)
            {
                desiredTangent = fleeDir;
            }
            else
            {
                Vector3 huntTangent = preyTarget != null
                    ? ComputeMovementTangentToPrey(preyTarget)
                    : ComputeMovementTangentToCorpse(corpseTarget);
                desiredTangent = fleeWeight > 0f
                    ? (huntTangent * (1f - fleeWeight) + fleeDir * fleeWeight).normalized
                    : huntTangent;
            }

            _heading = Vector3.Slerp(_heading, desiredTangent, turnSpeed * Time.deltaTime).normalized;
            float eraMult = EraManager.Instance != null ? EraManager.Instance.MoveSpeedMultiplier : 1f;
            float fleeBoost = 1f + fleeWeight * 0.5f;
            Vector3 newPos = SphereSurface.MoveAlongSurface(transform.position, _heading,
                moveSpeed * eraMult * fleeBoost * Time.deltaTime, planetCenter, planetRadius);
            transform.position = newPos;
            AlignToSurface();
        }

        // Layer 3 — eat if in range.
        if (preyTarget != null && preyTarget.gameObject != null)
        {
            float dist = SphereSurface.SurfaceDistance(transform.position, preyTarget.transform.position,
                planetCenter, planetRadius);
            if (dist <= eatRadius)
            {
                if (preyTarget.IsProducer)
                {
                    GrazeOn(preyTarget); // herbivory: drain energy without killing
                }
                else
                {
                    // Strength-based combat: strong prey may escape or kill the predator.
                    // ResolvePredatorAttack() returns true only if kill succeeds.
                    // Note: if the predator was killed (counter-attack), 'this' is now dead
                    // and gameObject is destroyed — do not access 'this' after returning true
                    // from the counter-kill branch. The null-check on preyTarget guards enough.
                    if (preyTarget.ResolvePredatorAttack(this))
                    {
                        float energyGained = TransferBiomassFrom(preyTarget);
                        Destroy(preyTarget.gameObject);
                        OnEat(energyGained);
                    }
                    // else: prey escaped or this predator was killed — either way, no eat
                }
            }
        }
        else if (corpseTarget != null)
        {
            float dist = SphereSurface.SurfaceDistance(transform.position, corpseTarget.transform.position,
                planetCenter, planetRadius);
            if (dist <= eatRadius)
            {
                float corpseMass = corpseTarget.Consume();
                float energyGained = corpseMass * 0.8f * AssimilationEfficiency; // corpse = lower caloric density
                _chemEnergy = Mathf.Clamp(_chemEnergy + energyGained, 0f, maxSolarEnergy);
                OnEat(energyGained);
            }
        }

        // Starvation tick: non-climate pressures combined (Q10 handles temperature via ComputeDemand).
        float drain = GetNonClimateFitnessMultipliers() * (1f - osmotrophySlowdown);
        _timeSinceLastMeal += Time.deltaTime * drain;
        if (_timeSinceLastMeal >= starvationTime)
            Die();

        // Grow/shrink from net energy: consumers lose mass when starving, gain when fed.
        // Net is negative here (no continuous acquisition — onEat pulses _chemEnergy instead).
        float consumerNet = (_chemEnergy > maxSolarEnergy * 0.5f) ? ComputeDemand() * 0.1f : -ComputeDemand();
        ApplyGrowthShrinkage(consumerNet);
    }

    /// Herbivory: drain a producer's energy reserve without killing it.
    /// Models grazing — the producer is weakened but survives.
    private void GrazeOn(AgentController producer)
    {
        float drained = producer.DrainEnergy(maxSolarEnergy * 0.25f);
        if (drained > 0f)
        {
            // Grazing transfers drained energy × herbivore assimilation efficiency into consumer reserve.
            float gained = drained * AssimilationEfficiency * 0.5f; // lower efficiency for plant material
            _chemEnergy = Mathf.Clamp(_chemEnergy + gained, 0f, maxSolarEnergy);
            OnEat(gained);
        }
    }

    /// Compute energy transferred from prey to this predator on a successful kill (spec §7).
    private float TransferBiomassFrom(AgentController prey)
    {
        // Caloric density: producers are energy-rich (full reserve); consumers vary by reserve.
        float preyBiomass = prey._currentMass;
        float caloricDensity = prey.IsProducer ? 1.2f : 1.0f;
        float energyGained = preyBiomass * caloricDensity * AssimilationEfficiency;
        _chemEnergy = Mathf.Clamp(_chemEnergy + energyGained, 0f, maxSolarEnergy);
        return energyGained;
    }

    /// Drain up to <amount> from this organism's active energy reserve; returns amount actually drained.
    public float DrainEnergy(float amount)
    {
        if (Metabolism == MetabolismType.Phototrophic)
        {
            float d = Mathf.Min(_solarEnergy, amount);
            _solarEnergy -= d;
            return d;
        }
        else
        {
            float d = Mathf.Min(_chemEnergy, amount);
            _chemEnergy -= d;
            return d;
        }
    }

    public void ReceiveRelationshipBonus(float amount)
    {
        _chemEnergy = Mathf.Clamp(_chemEnergy + amount, 0f, maxSolarEnergy);
    }

    public void ReceiveRelationshipDrain(float amount) => DrainEnergy(amount);

    private void OnEat(float energyGained = 0f)
    {
        _timeSinceLastMeal = 0f;
        _eatsSinceReproduction++;
        _lifetimeEats++;

        if (_eatsSinceReproduction >= eatsToReproduce)
        {
            TryReproduce();
        }
    }

    /// Gate in front of Reproduce(): asexual agents reproduce immediately, same as before.
    /// Sexual agents need a mate in range first - if none is found, reproduction is simply
    /// delayed (the eats-since-reproduction counter is NOT reset) so the agent keeps trying
    /// on every subsequent qualifying tick/meal until a mate turns up.
    private void TryReproduce()
    {
        // Era-based population cap: don't reproduce if we're at or above the limit.
        if (EraManager.Instance != null && _spawner != null &&
            _spawner.ActiveAgents.Count >= EraManager.Instance.MaxPopulation)
            return;

        if (!IsSexual)
        {
            _eatsSinceReproduction = 0;
            Reproduce(null);
            return;
        }

        AgentController mate = FindMateInRange();
        if (mate == null) return; // blocked - try again next time the condition re-checks

        _eatsSinceReproduction = 0;
        Reproduce(mate);
    }

    private void UpdateProducer()
    {
        // Flee drive: dominates all other drives when a predator is close and this organism
        // is weaker. A strong producer (high strengthTrait) has a lower flee weight and
        // may stand its ground rather than abandoning a good feeding patch.
        float fleeWeight = RefreshFleeState(out Vector3 fleeDir);

        Vector3 desiredTangent;
        if (fleeWeight > 0.7f)
        {
            // Flee dominates: ignore comfort/sun, just run.
            desiredTangent = fleeDir;
        }
        else
        {
            // Normal producer movement, blended with partial flee if needed.
            Vector3 forageTangent = Metabolism == MetabolismType.Phototrophic
                ? ComputeProducerMovementTangent()
                : ComputeWanderTangent();
            // Social aggregation pull (zero if solitary)
            Vector3 socialBias = ComputeSocialAggregationBias();
            float socialWeight = Sociality == SocialityBaseline.GroupForming ? 0.25f
                               : Sociality == SocialityBaseline.Aggregating  ? 0.15f : 0f;
            Vector3 baseTangent = socialWeight > 0f && socialBias.sqrMagnitude > 0.01f
                ? (forageTangent * (1f - socialWeight) + socialBias * socialWeight).normalized
                : forageTangent;
            desiredTangent = fleeWeight > 0f
                ? (baseTangent * (1f - fleeWeight) + fleeDir * fleeWeight).normalized
                : baseTangent;
        }

        _heading = Vector3.Slerp(_heading, desiredTangent, turnSpeed * Time.deltaTime).normalized;

        // Speed boost when fleeing: adrenaline-analog — up to 1.5× normal speed at full flee weight.
        float eraMult = EraManager.Instance != null ? EraManager.Instance.MoveSpeedMultiplier : 1f;
        float fleeBoost = 1f + fleeWeight * 0.5f;
        Vector3 newPos = SphereSurface.MoveAlongSurface(transform.position, _heading,
            moveSpeed * eraMult * fleeBoost * Time.deltaTime, planetCenter, planetRadius);
        transform.position = newPos;
        AlignToSurface();

        switch (Metabolism)
        {
            case MetabolismType.Chemosynthetic: UpdateChemosyntheticMetabolism(); break;
            case MetabolismType.Phototrophic:   UpdateProducerMetabolism();       break;
            case MetabolismType.Mixotrophic:    UpdateMixotrophicMetabolism();    break;
        }
    }

    /// Aggregating/GroupForming sociality: bias heading toward the centroid of nearby
    /// same-community members. Blended with other drives — it nudges, not forces.
    private Vector3 ComputeSocialAggregationBias()
    {
        if (Sociality == SocialityBaseline.Solitary || _spawner == null) return Vector3.zero;
        float searchR = senseRadius * (Sociality == SocialityBaseline.GroupForming ? 3f : 2f);
        Vector3 centroid = Vector3.zero;
        int count = 0;
        foreach (var other in _spawner.ActiveAgents)
        {
            if (other == null || other == this || other.communityId != communityId) continue;
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist > searchR || dist < 0.5f) continue;
            centroid += other.transform.position;
            count++;
        }
        if (count == 0) return Vector3.zero;
        centroid /= count;
        Vector3 towardCentroid = (centroid - transform.position).normalized;
        Vector3 normal = (transform.position - planetCenter).normalized;
        return (towardCentroid - Vector3.Dot(towardCentroid, normal) * normal).normalized;
    }

    // Simple comfort-biased wander for chemosynthetic organisms seeking richer substrate.
    private Vector3 ComputeWanderTangent()
    {
        Vector3 normal = (transform.position - planetCenter).normalized;
        Vector3 current = (_heading - Vector3.Dot(_heading, normal) * normal).normalized;
        if (current.sqrMagnitude < 0.0001f) current = Vector3.Cross(normal, Random.onUnitSphere).normalized;
        float randomTurn = Random.Range(-wanderTurnRate, wanderTurnRate) * Time.deltaTime;
        return (Quaternion.AngleAxis(randomTurn, normal) * current).normalized;
    }

    /// Solar charging and reproduction logic shared between motile producers (UpdateProducer)
    /// and passive-drifting producers (UpdatePassiveDrift). Separated so energy metabolism
    /// works identically regardless of whether the organism can direct its own movement.
    private void UpdateProducerMetabolism()
    {
        DayNightCycle dayNight = DayNightCycle.Instance;
        float solar = 0f;
        if (dayNight != null)
        {
            Vector3 normal = (transform.position - planetCenter).normalized;
            solar = dayNight.SolarExposure(normal);
        }
        // Atmospheric transparency: thick atmospheres absorb stellar light before it reaches
        // surface-dwelling organisms (1 bar Earth-like = full; 2 bar = ~half; 0.5 bar = full).
        float atmosTransparency = AtmosphereManager.Instance != null
            ? Mathf.Clamp01(2f / Mathf.Max(AtmosphereManager.Instance.PressureBar, 0.1f))
            : 1f;
        float photoAcq = solarChargeRate * solar * WorldSolarFluxFactor * atmosTransparency
                         * PhotoEfficiency * PhotosyntheticSurfaceArea();
        float photoNet = photoAcq - ComputeDemand();
        _solarEnergy += photoNet * Time.deltaTime;
        _solarEnergy = Mathf.Clamp(_solarEnergy, 0f, maxSolarEnergy);
        ApplyGrowthShrinkage(photoNet);

        if (_solarEnergy <= 0f)
            Die();

        if (_solarEnergy >= maxSolarEnergy * 0.95f)
        {
            _solarEnergy = maxSolarEnergy * 0.5f;
            _eatsSinceReproduction++;
            _lifetimeEats++;
            if (_eatsSinceReproduction >= eatsToReproduce)
                TryReproduce();
        }
    }

    /// Chemosynthesis: absorb dissolved organic chemicals from the local substrate pool.
    /// This is the default pre-photosynthesis energy source for all early life.
    private void UpdateChemosyntheticMetabolism()
    {
        float poolNutrients = ChemicalNutrientPool.Sample(transform.position);
        // Hydrothermal vents supplement the background pool — take whichever source is richer.
        float ventEnergy = HydrothermalVentManager.Instance != null
            ? HydrothermalVentManager.Instance.GetVentEnergyAt(transform.position)
            : 0f;
        float nutrients = Mathf.Max(poolNutrients, ventEnergy);

        // Net energy: absorption (scales with local nutrient density) minus metabolic drain
        // (scales with all environmental stressors).
        float absorbRate = solarChargeRate * nutrients * ChemoEfficiency * GibbsFactor() * UptakeSurfaceArea();
        float chemoNet = absorbRate - ComputeDemand();
        _chemEnergy += chemoNet * Time.deltaTime;
        _chemEnergy  = Mathf.Clamp(_chemEnergy, 0f, maxSolarEnergy);
        ApplyGrowthShrinkage(chemoNet);

        // Deplete local chemical pool proportional to absorption.
        if (nutrients > 0.01f)
            ChemicalNutrientPool.Deplete(transform.position, absorbRate * 0.0008f * Time.deltaTime);

        if (_chemEnergy <= 0f)
            Die();

        if (_chemEnergy >= maxSolarEnergy * 0.95f)
        {
            _chemEnergy = maxSolarEnergy * 0.5f;
            _eatsSinceReproduction++;
            _lifetimeEats++;
            if (_eatsSinceReproduction >= eatsToReproduce)
                TryReproduce();
        }
    }

    /// Mixotrophy: blends photosynthesis + heterotrophy at 70% efficiency each.
    /// Flexible fallback when either light or food is scarce, but never excels at either.
    private void UpdateMixotrophicMetabolism()
    {
        // Solar component (70% of phototrophic gain)
        DayNightCycle dayNight = DayNightCycle.Instance;
        float solar = 0f;
        if (dayNight != null)
        {
            Vector3 normal = (transform.position - planetCenter).normalized;
            solar = dayNight.SolarExposure(normal);
        }
        float atmosTransparency = AtmosphereManager.Instance != null
            ? Mathf.Clamp01(2f / Mathf.Max(AtmosphereManager.Instance.PressureBar, 0.1f)) : 1f;
        float mixoPhotoAcq = solarChargeRate * 0.7f * solar * WorldSolarFluxFactor * atmosTransparency
                             * PhotoEfficiency * PhotosyntheticSurfaceArea();
        float mixoNet = mixoPhotoAcq - ComputeDemand() * 0.5f;
        _solarEnergy += mixoNet * Time.deltaTime;
        _solarEnergy = Mathf.Clamp(_solarEnergy, 0f, maxSolarEnergy);
        ApplyGrowthShrinkage(mixoNet);

        // Osmotrophic component (70% of heterotrophic absorption)
        float nutrients = ChemicalNutrientPool.Sample(transform.position);
        float osmotrophySlowdown = Mathf.Clamp01(nutrients * 0.35f);
        if (nutrients > 0.01f)
            ChemicalNutrientPool.Deplete(transform.position, 0.00005f * 0.7f * Time.deltaTime);
        _timeSinceLastMeal += Time.deltaTime * GetNonClimateFitnessMultipliers() * 0.5f * (1f - osmotrophySlowdown);

        // Survive from either source
        bool canSurviveSolar = _solarEnergy > 0f;
        bool canSurviveOsmo  = _timeSinceLastMeal < starvationTime;
        if (!canSurviveSolar && !canSurviveOsmo) { Die(); return; }

        // Reproduce when solar is full
        if (_solarEnergy >= maxSolarEnergy * 0.95f)
        {
            _solarEnergy = maxSolarEnergy * 0.5f;
            _eatsSinceReproduction++;
            _lifetimeEats++;
            if (_eatsSinceReproduction >= eatsToReproduce)
                TryReproduce();
        }
    }

    /// Osmotrophy: heterotrophs absorb dissolved organics passively at low efficiency.
    /// Called only for non-motile heterotrophs; motile heterotrophs get this as Layer 1
    /// of UpdateConsumer() instead, so no double-drain occurs.
    private void UpdateOsmotrophy()
    {
        float nutrients = ChemicalNutrientPool.Sample(transform.position);
        float osmotrophySlowdown = Mathf.Clamp01(nutrients * 0.35f);
        if (nutrients > 0.01f)
            ChemicalNutrientPool.Deplete(transform.position, 0.00005f * Time.deltaTime);

        _timeSinceLastMeal += Time.deltaTime * GetNonClimateFitnessMultipliers() * (1f - osmotrophySlowdown);
        if (_timeSinceLastMeal >= starvationTime)
            Die();
    }

    /// Aggregate of all independent fitness pressures. Used wherever a single drain
    /// multiplier is needed rather than one pressure at a time.
    // ── Activity Budget (spec §4) ────────────────────────────────────────────────────────
    // Shared pool with competing draws. Maintenance is a protected floor; non-Maintenance
    // categories are reduced proportionally if their sum would exceed the remaining budget.
    // Returns a total multiplier on BMR that replaces the old flat "activity multiplier."

    // Fraction of BMR budget allocated to each category (all unitless, sum is the total multiplier).
    private const float ActivityMaintenance  = 0.40f; // protected floor — always paid
    private const float ActivityLocoMax      = 0.45f; // active-mobile + chasing prey
    private const float ActivityVigilanceMax = 0.20f; // scales with PredationPressure
    // Era 2 additions (gated by gene events — hooks only, filled when events land):
    private const float ActivityCognitiveMax = 0.25f; // Cognitive/Neural (NeuralComplexity gate)
    private const float ActivitySocialMax    = 0.10f; // Social/Coordination (Sociality gate)

    // Cost-of-Transport multiplier per locomotion medium (Tucker/Taylor COT scaling):
    //   Swimming=1.0 (buoyancy offsets weight), Flying=1.4 (fast/unit-distance despite high power),
    //   Walking/Gliding=1.8 (weight-bearing adds cost).
    private float LocomotionCOTMultiplier() => LocomotionMedium switch
    {
        LocomotionMedium.Aquatic    => 1.0f,
        LocomotionMedium.Aerial     => 1.4f,
        LocomotionMedium.Gliding    => 1.8f,
        LocomotionMedium.Terrestrial=> 1.8f,
        _                           => 1.0f, // Sessile — no locomotion draw anyway
    };

    // Elevation climb cost added to demand each tick (mgh, positive only for ascent).
    // Aerial organisms gain altitude via COT, not discrete mgh events — excluded here.
    private float ComputeClimbCost()
    {
        if (!HasMotility || LocomotionMedium == LocomotionMedium.Aerial) return 0f;
        float currentElev = ClimateManager.GetElevation(transform.position);
        if (_lastElevation < 0f) { _lastElevation = currentElev; return 0f; }
        float deltaElev = Mathf.Max(0f, currentElev - _lastElevation);
        _lastElevation = currentElev;
        return _currentMass * 9.8f * deltaElev * TerrainHeightWorldUnits * ClimbEfficiencyPenalty;
    }

    private float ResolveActivityBudget()
    {
        // --- Locomotion/Foraging draw (scaled by medium Cost-of-Transport) ---
        float locoDraw = 0f;
        if (HasMotility)
        {
            float baseLocoDraw = (!IsProducer) ? ActivityLocoMax               // predators
                               : (Metabolism == MetabolismType.Phototrophic)   ? ActivityLocoMax * 0.2f
                               : ActivityLocoMax * 0.3f;
            locoDraw = baseLocoDraw * LocomotionCOTMultiplier();
        }

        // --- Vigilance/Predator-avoidance draw (scales with local PredationPressure) ---
        float vigilanceDraw = ActivityVigilanceMax * PredationPressure;

        // --- Era 2 cognitive draw (gated by NeuralComplexity trait) ---
        float cognitiveDraw = 0f;
        if (Era2Manager.Instance != null && Era2Manager.Instance.IsActive)
            cognitiveDraw = ActivityCognitiveMax * Mathf.Clamp01((float)NeuralComplexity / 3f);

        // --- Era 2 social draw (gated by Sociality trait) ---
        float socialDraw = 0f;
        if (Era2Manager.Instance != null && Era2Manager.Instance.IsActive)
            socialDraw = ActivitySocialMax * Mathf.Clamp01((float)Sociality / 3f);

        // --- Proportional reduction if non-Maintenance sum exceeds remaining budget ---
        float nonMaintTotal = locoDraw + vigilanceDraw + cognitiveDraw + socialDraw;
        float remaining = 1f - ActivityMaintenance; // budget available to non-Maintenance
        float scale = (nonMaintTotal > remaining) ? remaining / nonMaintTotal : 1f;

        return ActivityMaintenance + nonMaintTotal * scale;
    }

    // ── Kleiber BMR demand (§3 energy spec) ─────────────────────────────────────────────
    // k constant per backbone chemistry: tuned so a mid-era-scale agent (scale ≈ 0.15,
    // mass ≈ 0.0034) has a demand comparable to the old flat solarDrainRate (0.5 u/s).
    // Adjust during playtesting — only internal consistency across size range matters.
    private static readonly float[] KleiberK = { 0.6f, 0.7f, 0.65f, 0.55f, 0.75f, 0.6f, 0.5f, 0.65f };
    // Q10 per backbone (metabolic rate doubling per 10°C — real ectotherm range 2–3).
    private static readonly float[] BackboneQ10 = { 2.0f, 2.5f, 2.2f, 2.0f, 3.0f, 2.5f, 2.0f, 2.2f };
    // Reference temperature per backbone (°C, from solvent-tolerance optimum tables).
    private static readonly float[] BackboneRefTemp = { 20f, 15f, 25f, 30f, 10f, 20f, 35f, 20f };

    // ── Growth / shrinkage (spec §8) ────────────────────────────────────────────────────
    /// Called each tick after energy reserve is updated. Positive net surplus grows mass;
    /// sustained negative shrinks it. Visual scale tracks mass so the change is visible.
    private void ApplyGrowthShrinkage(float netBalance)
    {
        float massChange = 0f;
        if (netBalance > 0f)
            massChange =  MassGrowthRate * _spawnMass * Time.deltaTime;
        else if (netBalance < 0f)
            massChange = -MassShrinkRate * _spawnMass * Time.deltaTime;

        if (massChange == 0f) return;

        float eraMaxMass = _spawnMass * 2.5f; // don't grow beyond 2.5× spawn mass within an era
        _currentMass = Mathf.Clamp(_currentMass + massChange, _spawnMass * MassViabilityFloor, eraMaxMass);

        // Death if mass falls below viability floor.
        if (_currentMass <= _spawnMass * MassViabilityFloor)
        {
            Die();
            return;
        }

        // Sync visual scale — mass scales as volume, so linear scale = cbrt(mass/spawnMass) × spawnScale.
        float spawnScale = Mathf.Pow(_spawnMass, 1f / 3f);
        float newScale = Mathf.Pow(_currentMass, 1f / 3f);
        // Keep within era bounds so mass-growth doesn't override EraManager's visual scale.
        float eraScale = EraManager.Instance != null ? EraManager.Instance.AgentTargetScale : spawnScale;
        transform.localScale = Vector3.one * Mathf.Clamp(newScale, eraScale * 0.5f, eraScale * 1.5f);
    }

    // ── Acquisition surface-area helpers (spec §5.3, §6) ────────────────────────────────
    // Photosynthetic surface scales as mass^(2/3) — surface-to-volume law.
    // Shape modifier: flat/sessile organisms get a bonus; compact/spherical get none.
    private float PhotosyntheticSurfaceArea()
    {
        float shapeBonus = HasMotility ? 1.0f : 1.3f; // sessile = broader surface exposure
        return Mathf.Pow(Mathf.Max(_currentMass, 0.0001f), 2f / 3f) * shapeBonus;
    }

    // Uptake surface for chemosynthesis — same scaling, no shape bonus.
    private float UptakeSurfaceArea()
    {
        return Mathf.Pow(Mathf.Max(_currentMass, 0.0001f), 2f / 3f);
    }

    // Gibbs free energy scalar: a lineage-level constant that modulates raw vent flux.
    // In the absence of a per-vent reaction-type field, we derive a per-organism Gibbs
    // factor from backbone chemistry (proxy for which reaction pathway the lineage uses).
    // Range [0.10, 1.00] — sulfide-oxidising lineages get the highest yield.
    // When VentByproductKind is added to HydrothermalVentManager (§6.1 follow-up), replace
    // this with a per-vent lookup table keyed to reaction pair.
    private float GibbsFactor()
    {
        return Backbone switch
        {
            BackboneElement.Sulfur      => 1.00f, // H₂S oxidation — highest ΔG
            BackboneElement.Carbon      => 0.30f, // H₂ oxidation proxy
            BackboneElement.Tin        => 0.20f, // iron-analog oxidation
            BackboneElement.Silicon     => 0.15f, // manganese-analog
            BackboneElement.Phosphorus  => 0.18f, // anammox-analog
            BackboneElement.Nitrogen    => 0.10f, // methanogenesis — lowest ΔG
            _                           => 0.20f,
        };
    }

    /// Metabolic demand per second: Kleiber BMR × Q10 temperature scaling × surviving fitness multipliers.
    /// Replaces the old flat `solarDrainRate × GetAllFitnessMultipliers()` drain.
    public float ComputeDemand()
    {
        int bIdx = Mathf.Clamp((int)Backbone, 0, KleiberK.Length - 1);
        float bmr = KleiberK[bIdx] * Mathf.Pow(Mathf.Max(_currentMass, 0.0001f), 0.75f);

        float localTemp = ClimateManager.GetTemperature(transform.position);
        float q10 = BackboneQ10[bIdx];
        float refTemp = BackboneRefTemp[bIdx];
        float q10Mult = Mathf.Pow(q10, (localTemp - refTemp) / 10f);
        q10Mult = Mathf.Clamp(q10Mult, 0.25f, 4f); // prevent runaway at extreme temperatures

        // Activity Budget replaces flat activity multiplier; non-climate fitness penalties stack on top.
        // Climb cost (mgh) is additive — it is a discrete physics cost, not a budget fraction.
        return bmr * q10Mult * ResolveActivityBudget() * GetNonClimateFitnessMultipliers() + ComputeClimbCost();
    }

    /// All fitness multipliers EXCEPT climate-starvation — that is replaced by Q10 scaling above.
    private float GetNonClimateFitnessMultipliers()
    {
        return GetAtmosphericFitnessMultiplier()
             * GetStressFitnessMultiplier()
             * GetUVFitnessMultiplier()
             * GetPressureFitnessMultiplier()
             * GetThermalCycleFitnessMultiplier()
             * GetMediumFitnessMultiplier()
             * GetWrongLiquidMultiplier()
             * GetStrengthConstitutionBonus();
    }

    /// Legacy multiplier stack — retained for callers that still need it pending full migration.
    /// Climate-starvation term is REMOVED here to avoid double-counting with Q10.
    private float GetAllFitnessMultipliers() => GetNonClimateFitnessMultipliers();

    /// Penalty for being submerged in a different liquid chemistry than this lineage evolved in.
    /// Models osmotic stress, incompatible pH, wrong dissolved gases for the organism's biochemistry.
    private float GetWrongLiquidMultiplier()
    {
        if (string.IsNullOrEmpty(_requiredLiquidKind)) return 1f;
        if (CurrentMedium != HabitatMedium.Sea) return 1f;
        var curLiquid = FluidDynamicsManager.Instance?.CurrentLiquid;
        if (curLiquid == null || curLiquid.Name == _requiredLiquidKind) return 1f;
        return 3.5f;
    }

    /// Asphyxiation check. If the atmosphere's breathable gas fraction drops below the
    /// lineage's minimum, drain ramps up proportional to deficit. Below 2% → instant death.
    private void CheckGasSurvival()
    {
        if (AtmosphereManager.Instance == null || string.IsNullOrEmpty(_breathedGasName)) return;

        float fraction = 0f;
        foreach (var gas in AtmosphereManager.Instance.Gases)
            if (gas.Name == _breathedGasName) { fraction = gas.Fraction; break; }

        if (fraction < 0.02f) { Die(); return; }
        if (fraction >= _minBreathableFraction) return;

        // Extra metabolic drain proportional to how far below the minimum the gas has dropped.
        float deficit     = 1f - fraction / _minBreathableFraction;
        float extraDrain  = deficit * 0.8f * Time.deltaTime;

        if (!IsProducer)
            _timeSinceLastMeal += extraDrain;
        else
        {
            _solarEnergy -= extraDrain;
            _chemEnergy  -= extraDrain;
        }
    }

    /// Backbone-dependent lethal gas check. Some atmospheres are structurally destructive
    /// to non-carbon backbones: O2 fossilizes Si/Ge/Sn, ignites B/P; F2 destroys C/B/N/P/S.
    /// Checked once per second (throttled by _backboneGasCheckTimer) to keep per-frame cost low.
    private float _backboneGasCheckTimer;
    private void CheckBackboneGasTolerance()
    {
        _backboneGasCheckTimer -= Time.deltaTime;
        if (_backboneGasCheckTimer > 0f || AtmosphereManager.Instance == null) return;
        _backboneGasCheckTimer = 1f; // recheck every second

        foreach (var gas in AtmosphereManager.Instance.Gases)
        {
            if (gas.Fraction < 0.02f) continue; // trace amounts are not acutely lethal
            bool lethal = IsGasLethalForBackbone(gas.Name, Backbone);
            if (!lethal) continue;

            // Lethal gas: drain energy fast (less harsh than instant death to allow some
            // lineage to survive via speciation to a different atmosphere before it climbs too high).
            float drain = gas.Fraction * 2f * Time.deltaTime * 1f; // 1-second check, so × 1 not deltaTime
            if (!IsProducer)
                _timeSinceLastMeal += drain;
            else
            {
                _solarEnergy -= drain;
                _chemEnergy  -= drain;
            }
        }
    }

    private static bool IsGasLethalForBackbone(string gasName, BackboneElement backbone)
    {
        switch (gasName)
        {
            case "O2":
                return backbone == BackboneElement.Silicon   || backbone == BackboneElement.Germanium
                    || backbone == BackboneElement.Tin       || backbone == BackboneElement.Boron
                    || backbone == BackboneElement.Phosphorus;
            case "F2":
                return backbone == BackboneElement.Carbon    || backbone == BackboneElement.Boron
                    || backbone == BackboneElement.Nitrogen  || backbone == BackboneElement.Phosphorus
                    || backbone == BackboneElement.Sulfur;
            case "H2O":
                return backbone == BackboneElement.Silicon   || backbone == BackboneElement.Germanium
                    || backbone == BackboneElement.Tin       || backbone == BackboneElement.Boron
                    || backbone == BackboneElement.Phosphorus;
            case "NH3":
                return backbone == BackboneElement.Carbon; // corrosive at high conc for carbon
            case "PH3":
                return backbone == BackboneElement.Carbon; // disrupts Fe-S enzymes
            case "B2H6":
                return backbone == BackboneElement.Carbon; // highly toxic to organic tissue
            default:
                return false;
        }
    }

    /// Strong organisms have a more robust constitution: each 10 points of strength above
    /// 50 reduces all-multiplier drain by 2%, to a max 10% reduction at strength 100.
    /// Weakness below 50 adds a slight penalty (fragile physique). This makes strength
    /// matter even outside direct combat, without overshadowing specialized traits.
    private float GetStrengthConstitutionBonus()
    {
        float excess = (strengthTrait - 50f) / 50f; // -1 at str=0, 0 at str=50, +1 at str=100
        return Mathf.Clamp(1f - excess * 0.10f, 0.90f, 1.10f);
    }

    /// Medium mismatch penalty: aquatic organisms beached on land suffer desiccation;
    /// future terrestrial organisms submerged in liquid would drown (reverse penalty).
    /// In early eras (<=1) all life is aquatic — stranding on land is lethal over time.
    /// This naturally creates selection pressure to stay in or near the liquid.
    private float GetMediumFitnessMultiplier()
    {
        bool beached  = _isAquatic  && CurrentMedium == HabitatMedium.Land;
        bool drowning = !_isAquatic && CurrentMedium == HabitatMedium.Sea;
        if (!beached && !drowning) return 1f;
        // Mild energy penalty while in wrong medium — actual death handled by
        // ApplyMediumMismatchDrain's tolerance-band exposure timer (addendum §2.4).
        float fraction = Mathf.Clamp01(_mediumMismatchExposure / MismatchBaseDeadlineSecs);
        return Mathf.Lerp(1.2f, 2.0f, fraction); // ramps from mild to moderate as exposure grows
    }

    /// Sexual isolation extinction: if a sexual organism has no opposite-sex community member
    /// for longer than 3× starvationTime, the lineage is reproductively extinct and it dies.
    /// Throttled to scan the active agent list only every 5 seconds so the O(N) cost is trivial.
    private void CheckSexualIsolation()
    {
        if (!IsSexual || CanChangeSex) return; // hermaphrodites self-mate; not isolated
        _noMateCheckTimer -= Time.deltaTime;
        if (_noMateCheckTimer <= 0f)
        {
            _noMateCheckTimer = 5f;
            BiologicalSex opposite = Sex == BiologicalSex.Male ? BiologicalSex.Female : BiologicalSex.Male;
            bool mateExists = false;
            if (_spawner != null)
                foreach (var a in _spawner.ActiveAgents)
                {
                    if (a == null || a == this || a.communityId != communityId) continue;
                    if (a.Sex == opposite) { mateExists = true; break; }
                }
            if (!mateExists)
                _noMateTimer += 5f;
            else
                _noMateTimer = 0f;
        }
        if (_noMateTimer >= starvationTime * 3f) { Die(); }
    }

    // ── Medium mismatch — tolerance-band survivable-duration (addendum §2.4) ────────────
    // Physiological failure, NOT a cost multiplier. Models gill collapse / desiccation.
    // Accumulates exposure time; death when exposure exceeds the hardiness-scaled ceiling.
    // Distinct from the Activity Budget energy pathway — this fires regardless of movement.
    private float _mediumMismatchExposure; // seconds in wrong medium
    private const float MismatchBaseDeadlineSecs = 30f; // base survivable duration at hardiness=50
    private float _lastElevation = -1f; // normalized [0,1]; -1 = uninitialized
    private const float ClimbEfficiencyPenalty = 1.3f; // biological inefficiency of uphill movement
    private const float TerrainHeightWorldUnits = 5f; // world-unit scale for 1 unit of normalized elevation

    private void ApplyMediumMismatchDrain()
    {
        bool beached  = _isAquatic  && CurrentMedium == HabitatMedium.Land;
        bool drowning = !_isAquatic && CurrentMedium == HabitatMedium.Sea;

        if (!beached && !drowning)
        {
            // Recovery: exposure timer resets while in correct medium.
            _mediumMismatchExposure = Mathf.Max(0f, _mediumMismatchExposure - Time.deltaTime * 2f);
            return;
        }

        _mediumMismatchExposure += Time.deltaTime;

        // Survivable duration scales with hardiness: high hardiness = longer tolerance window.
        // Range: hardiness=0 → 15s ceiling; hardiness=100 → 45s; hardiness=200 → 90s.
        float ceiling = MismatchBaseDeadlineSecs * (0.5f + hardinessTrait / 100f);
        if (_mediumMismatchExposure >= ceiling)
            Die();
    }

    /// UV radiation damage. Day-side organisms without UV tolerance take faster metabolic
    /// drain. Pre-GOE (no ozone) this is the primary selective pressure for deep-water life.
    /// Liquid attenuates UV — submerged organisms receive only a fraction of surface UV.
    /// Attenuation depends on the liquid's density (denser/more opaque = more shielding):
    ///   Water (1000 kg/m³): 85% attenuation — deep ocean is nearly UV-free.
    ///   Ammonia (680): 75%, Hydrocarbon (450): 60%, MoltenSulfur (1820): 95%.
    private float GetUVFitnessMultiplier()
    {
        float uv = UVManager.GetUVExposure(transform.position, planetCenter, DayNightCycle.Instance);
        if (uv < 0.01f) return 1f; // night side: no UV

        // Liquid UV shielding: attenuate surface UV by density-derived factor when submerged.
        if (CurrentMedium == HabitatMedium.Sea && FluidDynamicsManager.Instance?.CurrentLiquid != null)
        {
            float density = FluidDynamicsManager.Instance.CurrentLiquid.DensityKgM3;
            // Attenuation 0.6-0.95 mapped from density range 450-1820 kg/m³.
            float attenuation = Mathf.Lerp(0.60f, 0.95f, Mathf.InverseLerp(450f, 1820f, density));
            uv *= (1f - attenuation);
        }

        // uvTolerance 0 = deep-water specialist (steep UV penalty); 100 = surface-adapted (shallow).
        float sensitivity = Mathf.Lerp(2.8f, 0.15f, uvTolerance / 100f);
        return Mathf.Clamp(1f + uv * sensitivity, 1f, 5f);
    }

    /// Atmospheric pressure discomfort. Organisms adapted to a different pressure band suffer.
    private float GetPressureFitnessMultiplier()
    {
        if (AtmosphereManager.Instance == null) return 1f;
        float current = AtmosphereManager.Instance.PressureBar;
        float mismatch = Mathf.Abs(current - pressurePreference) / Mathf.Max(current, 0.1f);
        float sensitivity = Mathf.Lerp(1.6f, 0.25f, pressureTolerance / 100f);
        return Mathf.Clamp(1f + mismatch * sensitivity, 1f, 3f);
    }

    /// Day/night thermal cycling stress. Night-side cold shock for warm-adapted organisms;
    /// day-side heat stress for cold-adapted ones. Uncorrelated with temperaturePreference
    /// mismatch (which handles average temperature) — this models the SWING.
    private float GetThermalCycleFitnessMultiplier()
    {
        DayNightCycle dayNight = DayNightCycle.Instance;
        if (dayNight == null) return 1f;
        Vector3 normal = (transform.position - planetCenter).normalized;
        float solar = dayNight.SolarExposure(normal);
        float nightCold = (1f - solar) * Mathf.Clamp01(temperaturePreference / 100f) * 0.7f;
        float dayHeat   = solar * Mathf.Clamp01((100f - temperaturePreference) / 100f) * 0.5f;
        float cyclePenalty = nightCold + dayHeat;
        float sensitivity = Mathf.Lerp(1.8f, 0.15f, thermalCycleTolerance / 100f);
        return Mathf.Clamp(1f + cyclePenalty * sensitivity, 1f, 3f);
    }

    private Vector3 ComputeProducerMovementTangent()
    {
        DayNightCycle dayNight = DayNightCycle.Instance;
        Vector3 normal = (transform.position - planetCenter).normalized;

        // Comfort drive (same as consumers).
        float discomfort = GetDiscomfort(transform.position);
        float comfortWeight = Mathf.Clamp01(Mathf.Sqrt(discomfort) * comfortSeekingStrength);
        Vector3 currentTangent = (_heading - Vector3.Dot(_heading, normal) * normal).normalized;
        if (currentTangent.sqrMagnitude < 0.0001f)
            currentTangent = Vector3.Cross(normal, Random.onUnitSphere).normalized;
        Vector3 comfortDir = FindMostComfortableDirection(currentTangent, normal, discomfort);

        // Sun-seeking drive: pulls toward sun when energy is low.
        float sunWeight = 0f;
        Vector3 sunDir = currentTangent;
        if (dayNight != null)
        {
            float solar = dayNight.SolarExposure(normal);
            sunWeight = Mathf.Clamp01(1f - _solarEnergy / maxSolarEnergy) * (1f - solar);
            // Project sun direction onto tangent plane.
            Vector3 toSun = dayNight.SunDirection;
            sunDir = (toSun - Vector3.Dot(toSun, normal) * normal).normalized;
            if (sunDir.sqrMagnitude < 0.0001f) sunDir = currentTangent;
        }

        float randomTurn = Random.Range(-wanderTurnRate, wanderTurnRate) * Time.deltaTime;
        Vector3 exploreDir = (Quaternion.AngleAxis(randomTurn, normal) * currentTangent).normalized;
        const float exploreWeight = 0.3f;

        Vector3 sum = exploreWeight * exploreDir + comfortWeight * comfortDir + sunWeight * sunDir;
        Vector3 proj = sum - Vector3.Dot(sum, normal) * normal;
        return proj.sqrMagnitude < 0.0001f ? exploreDir : proj.normalized;
    }

    /// Continuous per-tick selection pressure (Section 6a): an agent whose temperature/
    /// moisture preference matches its current location starves slower there; a mismatched
    /// agent starves faster. Applies every tick based on current position. Hardiness scales
    /// how steep this is per-individual: generalists (high hardiness) take a shallower
    /// penalty for the same mismatch; specialists (low hardiness) take a steeper one.
    private float GetClimateStarvationMultiplier()
    {
        float discomfort = GetDiscomfort(transform.position); // 0 (perfect match) .. 1 (max mismatch)
        float effectiveRange = climateFitnessMultiplierRange * Mathf.Lerp(hardinessRangeMax, hardinessRangeMin, hardinessTrait / 100f);
        float multiplier = 1f + (discomfort * 2f - 1f) * effectiveRange;
        return Mathf.Clamp(multiplier, 0.2f, 2f);
    }

    /// 0 = local climate exactly matches this agent's preference, 1 = maximally mismatched.
    private float GetDiscomfort(Vector3 position)
    {
        float temp = ClimateManager.GetTemperature(position);
        float moisture = ClimateManager.GetMoisture(position);
        float tempDiff = Mathf.Abs(temp - temperaturePreference) / 100f;
        float moistureDiff = Mathf.Abs(moisture - moisturePreference) / 100f;
        return Mathf.Clamp01((tempDiff + moistureDiff) / 2f);
    }

    /// e1_motility_emergence gene: organism develops directed locomotion (flagellar/ciliary
    /// analog). Until this fires the organism is a passive drifter; after it, self-directed
    /// movement and (eventually) predation become possible.
    public void BecomeMotile()
    {
        HasMotility = true;
        LocomotionMedium = _isAquatic ? LocomotionMedium.Aquatic : LocomotionMedium.Terrestrial;
        AcquiredGenes.Add("MotilityEmergence");
        Debug.Log($"[Motility] {name} developed directed locomotion.");
    }

    public void RemainSessile()
    {
        LocomotionMedium = LocomotionMedium.Sessile;
        AcquiredGenes.Add("MotilityEmergence");
        Debug.Log($"[Motility] {name} remains sessile (wind-dispersed).");
    }

    /// e2_gliding_adaptation: passive membrane extension enables controlled descent.
    public void BecomeGlider()
    {
        LocomotionMedium = LocomotionMedium.Gliding;
        AcquiredGenes.Add("GlidingAdaptation");
        Debug.Log($"[Gliding] {name} evolved gliding membranes.");
    }

    /// e2_aerial_locomotion_emergence: powered flight. Only callable when CurrentMass < FlightMassCeiling.
    public void BecomeAerial()
    {
        if (_currentMass >= FlightMassCeiling)
        {
            Debug.LogWarning($"[Flight] {name} tried to gain flight but mass {_currentMass:F5} >= ceiling {FlightMassCeiling:F5}.");
            return;
        }
        LocomotionMedium = LocomotionMedium.Aerial;
        AcquiredGenes.Add("AerialLocomotion");
        Debug.Log($"[Flight] {name} achieved powered flight (mass={_currentMass:F5}).");
    }

    /// PhotosynthesisEmergence gene: evolve the ability to harvest solar energy.
    /// Shifts metabolism from Chemosynthetic → Phototrophic; seeds solar energy from
    /// accumulated chemical reserve so there is no energy gap at the transition.
    public void BecomePhototrophic()
    {
        Metabolism = MetabolismType.Phototrophic;
        _solarEnergy = _chemEnergy; // carry over accumulated reserves
        AcquiredGenes.Add("PhotosynthesisEmergence");
        Debug.Log($"[Metabolism] {name} evolved photosynthesis.");
    }

    /// Kingdom Fork gene choice: stay as an autotroph (chemosynthetic or phototrophic).
    /// No-op if already a producer — just mints the kingdom name.
    public void BecomeProducer()
    {
        // Metabolism stays as whatever it already is (Chemosynthetic or Phototrophic).
        Kingdom = KingdomNameGenerator.Generate();
    }

    /// Kingdom Fork gene choice: shift to heterotrophy (osmotrophy → saprotrophy → predation).
    public void BecomeConsumer()
    {
        Metabolism = MetabolismType.Heterotrophic;
        _timeSinceLastMeal = 0f; // fresh starvation clock
        Kingdom = KingdomNameGenerator.Generate();
    }

    /// Mixotrophy gene: combine photosynthesis + heterotrophy at ~70% efficiency each.
    public void BecomeMixotrophic()
    {
        Metabolism = MetabolismType.Mixotrophic;
        _timeSinceLastMeal = 0f;
    }

    // ── Era 1 trait setters ───────────────────────────────────────────────────

    public void SetManipulation(ManipulationLevel level) => Manipulation = level;
    public void SetSociality(SocialityBaseline level)    => Sociality = level;
    public void SetNeuralComplexity(NeuralComplexityStage stage) => NeuralComplexity = stage;
    public void SetBodyPlan(BodyPlanType plan)  => BodyPlan = plan;
    public void SetGermLayers()                => HasGermLayers = true;
    public void SetAnoxicRefuge()              => IsAnoxicRefugeLineage = true;

    /// Reproductive Strategy Shift gene choice: remain Asexual (the current default -
    /// Reproduce() clones a single parent with mutation drift, no functional change).
    public void BecomeAsexual()
    {
        IsSexual = false;
    }

    /// Reproductive Strategy Shift gene choice: shift to Sexual reproduction. Reproduce()
    /// now requires finding an opposite-sex IsSexual mate in range and blends both parents'
    /// traits (see FindMateInRange / Reproduce).
    public void BecomeSexual()
    {
        IsSexual = true;
        Sex = Random.value < 0.5f ? BiologicalSex.Male : BiologicalSex.Female;
    }

    /// SequentialHermaphroditism gene: organism can switch sex in response to local
    /// population imbalance. Sets the ongoing capability; sex switching itself runs
    /// periodically in MaybeChangeSex().
    public void BecomeSequentialHermaphrodite()
    {
        CanChangeSex = true;
        AcquiredGenes.Add("SequentialHermaphroditism");
        Debug.Log($"[SexChange] {name} evolved sequential hermaphroditism (currently {Sex}).");
    }

    /// 0 = perfectly balanced (equal male/female nearby), 1 = entirely same-sex locally.
    /// Searches a 3× sense radius so it samples a meaningful local population, not just
    /// the immediate cluster. Returns 1 (max imbalance) if no opposite-sex agents nearby.
    public float LocalSexImbalance()
    {
        if (!IsSexual || _spawner == null) return 0f;
        int sameCount = 0, oppositeCount = 0;
        float searchR = senseRadius * 3f;
        foreach (var other in _spawner.ActiveAgents)
        {
            if (other == null || other == this || !other.IsSexual) continue;
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist > searchR) continue;
            if (other.Sex == Sex) sameCount++;
            else oppositeCount++;
        }
        if (sameCount + oppositeCount == 0) return 1f; // isolated — maximum imbalance
        return (float)sameCount / (sameCount + oppositeCount);
    }

    /// Periodic sex-switch check for organisms with CanChangeSex. Runs every 8-20 real
    /// seconds. If the local sex ratio is strongly skewed toward the organism's current sex
    /// (>65% same), there is a scaled chance to switch to the rarer sex, improving local
    /// mating availability. Chance scales linearly with excess imbalance so near-parity
    /// populations almost never trigger it but extreme imbalances (~90%+) trigger quickly.
    private void MaybeChangeSex()
    {
        if (!CanChangeSex || !IsSexual) return;
        _sexSwitchTimer -= Time.deltaTime;
        if (_sexSwitchTimer > 0f) return;
        _sexSwitchTimer = Random.Range(8f, 20f);

        float imbalance = LocalSexImbalance();
        const float threshold = 0.65f;
        float excess = imbalance - threshold;
        if (excess <= 0f) return;

        // Probability scales 0→1 over the excess range 0→0.35 (full certainty at 100% same-sex).
        if (Random.value < excess / (1f - threshold))
        {
            BiologicalSex prev = Sex;
            Sex = Sex == BiologicalSex.Male ? BiologicalSex.Female : BiologicalSex.Male;
            Debug.Log($"[SexChange] {name} switched {prev} → {Sex} (local imbalance {imbalance:P0})");
        }
    }

    /// Copies acquired genes and kingdom assignment from parent to offspring - genes are
    /// inherited once fixed, not re-rolled each generation.
    public void InheritGenesFrom(AgentController parent)
    {
        AcquiredGenes.Clear();
        foreach (var gene in parent.AcquiredGenes) AcquiredGenes.Add(gene);
        Kingdom = parent.Kingdom;
        IsSexual = parent.IsSexual;

        // Atmospheric adaptation is inherited from the parent's locked-in mix, NOT
        // resampled from the current atmosphere - only AttemptAtmosphericSpeciation
        // re-locks it, for either the parent or this child independently thereafter.
        HasMotility      = parent.HasMotility;
        LocomotionMedium = parent.LocomotionMedium;
        Metabolism       = parent.Metabolism;
        CanChangeSex = parent.CanChangeSex;
        // Sex itself is re-rolled at birth (see Reproduce) — only the capability is inherited.
        _idealGasMix = new Dictionary<string, float>(parent._idealGasMix);
        gasTolerance = parent.gasTolerance;
        AtmoLineage  = parent.AtmoLineage;

        // Environmental tolerances compound across generations — offspring inherit the
        // parent's adapted value rather than re-rolling from the spawn range.
        uvTolerance           = parent.uvTolerance;
        pressureTolerance     = parent.pressureTolerance;
        thermalCycleTolerance = parent.thermalCycleTolerance;
        pressurePreference    = parent.pressurePreference;

        // Survival needs are lineage-locked — offspring breathe the same gas and need
        // the same liquid chemistry as the parent line they descended from.
        _breathedGasName       = parent._breathedGasName;
        _minBreathableFraction = parent._minBreathableFraction;
        _requiredLiquidKind    = parent._requiredLiquidKind;

        // Era 1 evolved attributes — all inherited, not re-rolled.
        Backbone          = parent.Backbone;
        Manipulation      = parent.Manipulation;
        Sociality         = parent.Sociality;
        NeuralComplexity  = parent.NeuralComplexity;
        BodyPlan          = parent.BodyPlan;
        HasGermLayers     = parent.HasGermLayers;
        IsAnoxicRefugeLineage = parent.IsAnoxicRefugeLineage;

        // Efficiency traits are Tier 1 lineage traits — inherited directly.
        // SI drift (task #16) will mutate them at speciation events.
        PhotoEfficiency          = parent.PhotoEfficiency;
        ChemoEfficiency          = parent.ChemoEfficiency;
        AssimilationEfficiency   = parent.AssimilationEfficiency;

        if (_stressRegistered) PopulationStats.UnregisterStressTolerance(stressTolerance);
        stressTolerance = parent.stressTolerance;
        if (_stressRegistered) PopulationStats.RegisterStressTolerance(stressTolerance);
    }

    /// Asexual when mate == null (clones this parent's traits with mutation drift, exactly
    /// the pre-existing behavior). Sexual when mate is provided: each trait dimension is
    /// first averaged between both parents, THEN the same mutation drift is applied on top
    /// of the blended value - this blending is the actual genetic-diversity benefit the
    /// Reproductive Strategy Shift gene event is themed around.
    private void Reproduce(AgentController mate)
    {
        if (_spawner == null) return;
        if (EraManager.Instance != null && _spawner.ActiveAgents.Count >= EraManager.Instance.MaxPopulation) return;

        Vector3 offspringPos = SphereSurface.MoveAlongSurface(transform.position, _heading, offspringSpawnOffset, planetCenter, planetRadius);

        float baseVision = visionTrait;
        float baseSpeed = speedTrait;
        float baseStrength = strengthTrait;
        float baseHardiness = hardinessTrait;
        float baseTempPref = temperaturePreference;
        float baseMoisturePref = moisturePreference;

        if (mate != null)
        {
            baseVision = (visionTrait + mate.visionTrait) / 2f;
            baseSpeed = (speedTrait + mate.speedTrait) / 2f;
            baseStrength = (strengthTrait + mate.strengthTrait) / 2f;
            baseHardiness = (hardinessTrait + mate.hardinessTrait) / 2f;
            baseTempPref = (temperaturePreference + mate.temperaturePreference) / 2f;
            baseMoisturePref = (moisturePreference + mate.moisturePreference) / 2f;
        }

        float childVision = PopulationStats.SampleDimension(baseVision, mutationStdDev);
        float childSpeed = PopulationStats.SampleDimension(baseSpeed, mutationStdDev);
        float childStrength = PopulationStats.SampleDimension(baseStrength, mutationStdDev);
        float childHardiness = PopulationStats.SampleDimension(baseHardiness, mutationStdDev);

        // Offspring's climate preference drifts from the (blended) parent baseline, also
        // nudged toward the birth location's actual climate - regional adaptation compounds
        // across generations.
        float localTemp = ClimateManager.GetTemperature(offspringPos);
        float localMoisture = ClimateManager.GetMoisture(offspringPos);
        float childTempPref = PopulationStats.SampleDimension((baseTempPref + localTemp) / 2f, mutationStdDev);
        float childMoisturePref = PopulationStats.SampleDimension((baseMoisturePref + localMoisture) / 2f, mutationStdDev);

        AgentController child = _spawner.SpawnAgent(childVision, childSpeed, childStrength, childHardiness, childTempPref, childMoisturePref, offspringPos, communityId, lineageColor);
        child.InheritGenesFrom(this);
        // Assign offspring sex randomly if sexual — sex is not inherited, just the capability.
        // If local imbalance is extreme and the parent can change sex, bias offspring toward
        // the rarer sex (density-dependent sex determination, as in some real reptiles/fish).
        if (child.IsSexual)
        {
            float imbalance = LocalSexImbalance();
            float maleBias = (CanChangeSex && imbalance > 0.6f)
                ? (Sex == BiologicalSex.Male ? 0.25f : 0.75f) // bias toward rarer sex
                : 0.5f;                                         // 50:50 default
            child.Sex = Random.value < maleBias ? BiologicalSex.Male : BiologicalSex.Female;
        }
    }

    /// Returns the nearest OTHER IsSexual agent within sense range that this agent could
    /// reproduce with - same community only (lineage-compatible), excluding self. Mirrors
    /// FindPreyInRange's sense-range search pattern.
    private AgentController FindMateInRange()
    {
        if (_spawner == null) return null;

        AgentController nearest = null;
        float nearestDist = senseRadius;

        foreach (var other in _spawner.ActiveAgents)
        {
            if (other == null || other == this) continue;
            if (!other.IsSexual) continue;
            if (other.communityId != communityId) continue;
            // Require opposite biological sex. Asexual organisms can't mate sexually
            // (shouldn't reach here, but guard anyway).
            if (Sex == BiologicalSex.Asexual || other.Sex == BiologicalSex.Asexual) continue;
            if (other.Sex == Sex) continue; // same sex — skip
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = other;
            }
        }
        return nearest;
    }

    /// Returns the nearest huntable agent within sense range.
    ///
    /// Herbivory (target is a producer): any community, including own — eating plant-equivalents
    /// is not cannibalism and is how the food web forms before speciation creates rivals.
    ///
    /// Predation (target is a heterotroph): different community only — no intra-species warfare.
    ///
    /// This distinction is what allows a food chain to emerge within a single founding lineage:
    /// consumers eat same-lineage producers → creates selection pressure for producers to flee /
    /// invest in chemical defenses → eventually drives speciation along predator/prey lines.
    private AgentController FindPreyInRange()
    {
        if (_spawner == null) return null;

        AgentController nearest = null;
        float nearestDist = senseRadius;

        foreach (var other in _spawner.ActiveAgents)
        {
            if (other == null || other == this) continue;
            // Producers: targetable for herbivory regardless of community.
            // Heterotrophs: only hunt across community lines (no cannibalism).
            bool canTarget = other.IsProducer || other.communityId != communityId;
            if (!canTarget) continue;
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = other;
            }
        }
        return nearest;
    }

    private Vector3 ComputeMovementTangentToPrey(AgentController prey)
    {
        Vector3 normal = (transform.position - planetCenter).normalized;
        Vector3 currentTangent = (_heading - Vector3.Dot(_heading, normal) * normal).normalized;
        if (currentTangent.sqrMagnitude < 0.0001f)
            currentTangent = Vector3.Cross(normal, Random.onUnitSphere).normalized;

        float discomfort = GetDiscomfort(transform.position);
        float comfortWeight = Mathf.Clamp01(Mathf.Sqrt(discomfort) * comfortSeekingStrength);
        Vector3 comfortDir = FindMostComfortableDirection(currentTangent, normal, discomfort);

        float hungerWeight = Mathf.Clamp01(_timeSinceLastMeal / starvationTime);
        Vector3 hungerDir = SphereSurface.TangentDirectionTo(transform.position, prey.transform.position, planetCenter);

        float randomTurn = Random.Range(-wanderTurnRate, wanderTurnRate) * Time.deltaTime;
        Vector3 exploreDir = (Quaternion.AngleAxis(randomTurn, normal) * currentTangent).normalized;
        const float exploreWeight = 0.3f;

        Vector3 sum = exploreWeight * exploreDir + comfortWeight * comfortDir + hungerWeight * hungerDir;
        Vector3 proj = sum - Vector3.Dot(sum, normal) * normal;
        return proj.sqrMagnitude < 0.0001f ? exploreDir : proj.normalized;
    }

    private CorpseItem FindNearestCorpseInRange()
    {
        if (_corpseSpawner == null) return null;

        CorpseItem nearest = null;
        float nearestDist = senseRadius;

        foreach (var corpse in _corpseSpawner.ActiveCorpses)
        {
            if (corpse == null) continue;
            float dist = SphereSurface.SurfaceDistance(transform.position, corpse.transform.position, planetCenter, planetRadius);
            if (dist <= nearestDist)
            {
                nearestDist = dist;
                nearest = corpse;
            }
        }
        return nearest;
    }

    /// Utility-style drive blend: movement each tick is a weighted average of whichever
    /// drives are currently active, weighted by each drive's urgency (0-1). Adding a new
    /// drive later (thirst, safety, mate-seeking) means adding one more (weight, direction)
    /// term here - no other movement code needs to change.
    private Vector3 ComputeMovementTangentToCorpse(CorpseItem corpseTarget)
    {
        Vector3 normal = (transform.position - planetCenter).normalized;
        Vector3 currentTangent = (_heading - Vector3.Dot(_heading, normal) * normal).normalized;
        if (currentTangent.sqrMagnitude < 0.0001f)
        {
            currentTangent = Vector3.Cross(normal, Random.onUnitSphere).normalized;
        }

        // Baseline explore drive: always present at a fixed weight so the agent keeps
        // moving even when every other drive's urgency is near zero.
        float randomTurn = Random.Range(-wanderTurnRate, wanderTurnRate) * Time.deltaTime;
        Vector3 exploreDirection = (Quaternion.AngleAxis(randomTurn, normal) * currentTangent).normalized;
        const float exploreWeight = 0.3f;

        // Comfort drive: pulls toward whichever nearby direction best matches climate
        // preference, strength scaling with current discomfort.
        float discomfort = GetDiscomfort(transform.position);
        float comfortWeight = Mathf.Clamp01(Mathf.Sqrt(discomfort) * comfortSeekingStrength);
        Vector3 comfortDirection = FindMostComfortableDirection(currentTangent, normal, discomfort);

        // Hunger drive: pulls toward a sensed corpse, strength scaling with how close to
        // starving the agent is (so a well-fed agent ignores distant carrion and stays
        // near comfortable terrain; a near-starving one chases it regardless of biome).
        float hungerWeight = 0f;
        Vector3 hungerDirection = exploreDirection;
        if (corpseTarget != null)
        {
            hungerWeight = Mathf.Clamp01(_timeSinceLastMeal / starvationTime);
            hungerDirection = SphereSurface.TangentDirectionTo(transform.position, corpseTarget.transform.position, planetCenter);
        }

        Vector3 weightedSum = exploreWeight * exploreDirection + comfortWeight * comfortDirection + hungerWeight * hungerDirection;
        Vector3 tangentSum = weightedSum - Vector3.Dot(weightedSum, normal) * normal; // re-project onto tangent plane

        if (tangentSum.sqrMagnitude < 0.0001f) return exploreDirection;
        return tangentSum.normalized;
    }

    /// Samples a handful of candidate directions around the current heading and returns
    /// whichever reduces climate discomfort most (falls back to the current heading if
    /// nothing nearby is better, e.g. already standing in the best local spot).
    private Vector3 FindMostComfortableDirection(Vector3 currentTangent, Vector3 normal, float currentDiscomfort)
    {
        Vector3 bestDirection = currentTangent;
        float bestDiscomfort = currentDiscomfort;

        const int sampleCount = 5;
        for (int i = 0; i < sampleCount; i++)
        {
            float angle = (360f / sampleCount) * i;
            Quaternion sampleRot = Quaternion.AngleAxis(angle, normal);
            Vector3 candidateTangent = (sampleRot * currentTangent).normalized;
            Vector3 candidatePos = SphereSurface.MoveAlongSurface(transform.position, candidateTangent, comfortSampleDistance, planetCenter, planetRadius);

            float candidateDiscomfort = GetDiscomfort(candidatePos);
            if (candidateDiscomfort < bestDiscomfort)
            {
                bestDiscomfort = candidateDiscomfort;
                bestDirection = candidateTangent;
            }
        }

        return bestDirection;
    }

    // -------------------------------------------------------------------------
    // Flee / fight system
    // -------------------------------------------------------------------------

    /// Nearest motile heterotroph (predator) within threat range, or null.
    /// Search radius is 1.5× sense radius — organisms detect danger slightly
    /// further than they can hunt, giving time to start fleeing.
    private AgentController FindNearestPredatorInRange()
    {
        if (_spawner == null) return null;
        float threatRange = senseRadius * 1.5f;
        AgentController nearest = null;
        float nearestDist = threatRange;
        foreach (var other in _spawner.ActiveAgents)
        {
            if (other == null || other == this) continue;
            if (other.Metabolism != MetabolismType.Heterotrophic || !other.HasMotility) continue;
            if (other.communityId == communityId) continue; // own community never threatens
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist < nearestDist) { nearestDist = dist; nearest = other; }
        }
        return nearest;
    }

    /// Tangent direction directly away from the predator along the sphere surface.
    private Vector3 ComputeFleeDirection(AgentController predator)
    {
        Vector3 normal = (transform.position - planetCenter).normalized;
        Vector3 toward = SphereSurface.TangentDirectionTo(transform.position, predator.transform.position, planetCenter);
        Vector3 away = -toward;
        return (away - Vector3.Dot(away, normal) * normal).normalized;
    }

    /// Called by UpdateProducer / UpdatePassiveDrift to refresh flee target and return
    /// a flee drive weight (0 = no threat, 1 = immediate danger, strength-discounted
    /// for organisms that are strong enough to stand their ground).
    private float RefreshFleeState(out Vector3 fleeDir)
    {
        fleeDir = _heading;

        // Decay cooldown — keep fleeing briefly after losing sight of predator.
        if (_fleeCooldown > 0f) _fleeCooldown -= Time.deltaTime;

        AgentController threat = FindNearestPredatorInRange();
        if (threat != null)
        {
            _fleeTarget   = threat;
            _fleeCooldown = 3f; // continue fleeing for 3 s after threat leaves range
        }
        else if (_fleeCooldown <= 0f)
        {
            _fleeTarget = null;
        }

        if (_fleeTarget == null || !_fleeTarget.gameObject) return 0f;

        fleeDir = ComputeFleeDirection(_fleeTarget);

        // Flee weight: scales with proximity (closer = more urgent) and with how much
        // WEAKER this organism is vs the predator (equal strength → 0.5 baseline;
        // much weaker → 1.0; much stronger → fight, so low flee weight).
        float dist = SphereSurface.SurfaceDistance(transform.position, _fleeTarget.transform.position, planetCenter, planetRadius);
        float proximityUrgency = Mathf.Clamp01(1f - dist / (senseRadius * 1.5f));
        float strengthDisadvantage = Mathf.Clamp01(0.5f + (_fleeTarget.strengthTrait - strengthTrait) / 100f);
        return proximityUrgency * strengthDisadvantage;
    }

    /// Combat resolution when a predator tries to eat this organism.
    /// Returns true if predator succeeds (prey dies), false if prey escapes or fights back.
    /// Strong prey can counter-kill a weak predator.
    public bool ResolvePredatorAttack(AgentController attacker)
    {
        float diff = attacker.strengthTrait - strengthTrait; // positive = attacker stronger

        // Very strong prey can fight back and kill the attacker.
        if (strengthTrait > attacker.strengthTrait + 40f)
        {
            // Prey kills attacker — role reversal.
            attacker.Die();
            Debug.Log($"[Combat] {name} (str={strengthTrait:F0}) killed attacker {attacker.name} (str={attacker.strengthTrait:F0}).");
            return false; // prey survives
        }

        // Escape probability: 50% at equal strength, 0% when attacker is 50+ stronger,
        // 100% when prey is 50+ stronger (but below the counter-kill threshold).
        float escapeProbability = Mathf.Clamp01(0.5f - diff / 100f);

        if (Random.value < escapeProbability)
        {
            // Prey escapes: gets a brief speed burst and flee cooldown.
            _fleeCooldown = 5f;
            _fleeTarget   = attacker;
            Debug.Log($"[Combat] {name} escaped {attacker.name} (escape chance {escapeProbability:P0}).");
            return false; // prey survives
        }

        return true; // predator kills prey
    }

    private void AlignToSurface()
    {
        Vector3 normal = (transform.position - planetCenter).normalized;
        transform.up = normal;
        if (_heading.sqrMagnitude > 0.0001f)
        {
            Vector3 tangentHeading = (_heading - Vector3.Dot(_heading, normal) * normal).normalized;
            if (tangentHeading.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(tangentHeading, normal);
            }
        }
    }
}
