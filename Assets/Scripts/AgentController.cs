using System.Collections.Generic;
using UnityEngine;

public class AgentController : MonoBehaviour
{
    [Header("Sphere")]
    [HideInInspector] public Vector3 planetCenter;
    [HideInInspector] public float planetRadius;

    [Header("Movement")]
    public float turnSpeed = 8f;

    [Header("Sensing")]
    public float eatRadius = 0.5f;

    [Header("Wander")]
    public float wanderTurnRate = 60f; // max degrees/sec random heading change

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

    // Set by the Kingdom Fork gene (Photosynthesis vs Heterotroph) - see GeneCatalog.
    public string Kingdom { get; private set; }
    public bool IsProducer { get; private set; }

    // Set by the Reproductive Strategy Shift gene - see GeneCatalog. Asexual (false, the
    // default) clones a single parent with mutation drift; Sexual (true) requires finding
    // a compatible mate and blends both parents' traits before drift is applied.
    public bool IsSexual { get; private set; }

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

    private Vector3 _heading; // tangent direction, world space
    private CorpseSpawner _corpseSpawner;
    private AgentSpawner _spawner;
    private int _eatsSinceReproduction;
    private float _timeSinceLastMeal;
    private int _lifetimeEats;
    private bool _traitsRegistered;
    private float _solarEnergy; // producers use this instead of _timeSinceLastMeal

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
        _solarEnergy = maxSolarEnergy * 0.5f; // producers start at half charge
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

        Vector3 normal = (transform.position - planetCenter).normalized;
        _heading = Vector3.Cross(normal, Random.onUnitSphere).normalized;
        AlignToSurface();

        // Apply current era's visual scale so new agents (including offspring) always
        // spawn at the right size, not at the prefab's default size.
        if (EraManager.Instance != null)
            transform.localScale = Vector3.one * EraManager.Instance.AgentTargetScale;
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
        if (_corpseSpawner != null) _corpseSpawner.SpawnCorpseAt(transform.position);
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

        UpdateStressLevel();

        if (IsProducer)
            UpdateProducer();
        else
            UpdateConsumer();

        AttemptAtmosphericSpeciation();
        GeneEvolutionManager.CheckEligibleGenes(this);
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
        float climateDiscomfort = GetDiscomfort(transform.position); // 0..1
        float atmosphericDiscomfort = GetAtmosphericDiscomfort(); // 0..1
        float starvationProximity = IsProducer ? 0f : Mathf.Clamp01(_timeSinceLastMeal / starvationTime); // 0..1

        float adversity = (climateDiscomfort + atmosphericDiscomfort + starvationProximity) / 3f; // 0..1
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
        // Preferred target: an agent from a different community within sense range
        // (hunted alive). Falls back to scavenging a nearby decaying corpse.
        AgentController preyTarget = FindPreyInRange();
        CorpseItem corpseTarget = preyTarget == null ? FindNearestCorpseInRange() : null;

        Vector3 desiredTangent = preyTarget != null
            ? ComputeMovementTangentToPrey(preyTarget)
            : ComputeMovementTangentToCorpse(corpseTarget);

        _heading = Vector3.Slerp(_heading, desiredTangent, turnSpeed * Time.deltaTime).normalized;

        Vector3 newPos = SphereSurface.MoveAlongSurface(transform.position, _heading, moveSpeed * Time.deltaTime, planetCenter, planetRadius);
        transform.position = newPos;
        AlignToSurface();

        if (preyTarget != null && preyTarget.gameObject != null)
        {
            float dist = SphereSurface.SurfaceDistance(transform.position, preyTarget.transform.position, planetCenter, planetRadius);
            if (dist <= eatRadius)
            {
                // Direct predation kill - eaten alive, no corpse left behind.
                Destroy(preyTarget.gameObject);
                OnEat();
            }
        }
        else if (corpseTarget != null)
        {
            float dist = SphereSurface.SurfaceDistance(transform.position, corpseTarget.transform.position, planetCenter, planetRadius);
            if (dist <= eatRadius)
            {
                corpseTarget.Consume();
                OnEat();
            }
        }

        _timeSinceLastMeal += Time.deltaTime * GetClimateStarvationMultiplier() * GetAtmosphericFitnessMultiplier() * GetStressFitnessMultiplier();
        if (_timeSinceLastMeal >= starvationTime)
            Die();
    }

    private void OnEat()
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
        // Producers move toward the day side when on solar energy reserves; they don't eat.
        Vector3 desiredTangent = ComputeProducerMovementTangent();
        _heading = Vector3.Slerp(_heading, desiredTangent, turnSpeed * Time.deltaTime).normalized;

        Vector3 newPos = SphereSurface.MoveAlongSurface(transform.position, _heading, moveSpeed * Time.deltaTime, planetCenter, planetRadius);
        transform.position = newPos;
        AlignToSurface();

        // Solar energy: charge on day side, drain at night.
        DayNightCycle dayNight = DayNightCycle.Instance;
        float solar = 0f;
        if (dayNight != null)
        {
            Vector3 normal = (transform.position - planetCenter).normalized;
            solar = dayNight.SolarExposure(normal);
        }
        _solarEnergy += (solarChargeRate * solar - solarDrainRate * GetAtmosphericFitnessMultiplier() * GetStressFitnessMultiplier()) * Time.deltaTime;
        _solarEnergy = Mathf.Clamp(_solarEnergy, 0f, maxSolarEnergy);

        if (_solarEnergy <= 0f)
            Die();

        // Reproduce on full solar charge.
        if (_solarEnergy >= maxSolarEnergy * 0.95f)
        {
            _solarEnergy = maxSolarEnergy * 0.5f;
            _eatsSinceReproduction++;
            _lifetimeEats++;
            if (_eatsSinceReproduction >= eatsToReproduce)
            {
                TryReproduce();
            }
        }
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

    /// Kingdom Fork gene choice: become a Producer (Section 14b/photosynthesis-equivalent).
    /// Labeling/flagging only for now - actual autotroph nutrition (generating biomass
    /// instead of eating) is not implemented yet, flagged as a follow-up.
    public void BecomeProducer()
    {
        IsProducer = true;
        Kingdom = KingdomNameGenerator.Generate();
    }

    /// Kingdom Fork gene choice: remain Heterotrophic (the current default diet/behavior).
    public void BecomeConsumer()
    {
        IsProducer = false;
        Kingdom = KingdomNameGenerator.Generate();
    }

    /// Reproductive Strategy Shift gene choice: remain Asexual (the current default -
    /// Reproduce() clones a single parent with mutation drift, no functional change).
    public void BecomeAsexual()
    {
        IsSexual = false;
    }

    /// Reproductive Strategy Shift gene choice: shift to Sexual reproduction. Reproduce()
    /// now requires finding a compatible IsSexual mate in range and blends both parents'
    /// traits (see FindMateInRange / Reproduce).
    public void BecomeSexual()
    {
        IsSexual = true;
    }

    /// Copies acquired genes and kingdom assignment from parent to offspring - genes are
    /// inherited once fixed, not re-rolled each generation.
    public void InheritGenesFrom(AgentController parent)
    {
        AcquiredGenes.Clear();
        foreach (var gene in parent.AcquiredGenes) AcquiredGenes.Add(gene);
        IsProducer = parent.IsProducer;
        Kingdom = parent.Kingdom;
        IsSexual = parent.IsSexual;

        // Atmospheric adaptation is inherited from the parent's locked-in mix, NOT
        // resampled from the current atmosphere - only AttemptAtmosphericSpeciation
        // re-locks it, for either the parent or this child independently thereafter.
        _idealGasMix = new Dictionary<string, float>(parent._idealGasMix);
        gasTolerance = parent.gasTolerance;
        AtmoLineage = parent.AtmoLineage;

        // Stress tolerance is inherited with the same drift treatment as gasTolerance -
        // offspring start from the parent's value, not a fresh spawn-range roll, so
        // resilience (or fragility) compounds across generations under selection. Init()
        // already registered a freshly-rolled value with PopulationStats; swap it for the
        // inherited one so the population mean isn't left tracking a discarded roll.
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
            if (other.communityId != communityId) continue; // only mate within the same lineage/community
            float dist = SphereSurface.SurfaceDistance(transform.position, other.transform.position, planetCenter, planetRadius);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = other;
            }
        }
        return nearest;
    }

    /// Returns the nearest agent from a DIFFERENT community within sense range.
    /// Prefers other-community prey heavily; same-community is never targeted unless
    /// a "Cannibalism" gene is acquired (not yet implemented - always returns null for own community).
    private AgentController FindPreyInRange()
    {
        if (_spawner == null) return null;

        AgentController nearest = null;
        float nearestDist = senseRadius;

        foreach (var other in _spawner.ActiveAgents)
        {
            if (other == null || other == this) continue;
            if (other.communityId == communityId) continue; // never eat own community
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
