using System.Collections.Generic;
using UnityEngine;

/// Rolls a weighted atmosphere type (atmosphere_generator_spec.docx Section 2),
/// reweighted by the planet's rock archetype, and generates its gas composition
/// PURELY as atmospheric chemistry - no biology involved yet. A compatible organism
/// biochemistry (OrganismBiochemistry.cs) is then rolled separately and its
/// Breathed/Expelled gases are layered onto that composition: the breathed gas must
/// already exist (added at a modest fraction if the roll didn't naturally include
/// it), but the expelled gas is a metabolic byproduct that may be entirely absent at
/// genesis - it starts at 0% and only appears as respiration produces it over time.
///
/// Respiration is continuous and population-driven: consumers breathe-in/exhale-out
/// every tick at `respirationRate`; producers run the exchange in reverse on the day
/// side (a photosynthesis-equivalent). There is no guaranteed equilibrium.
///
/// Survival pressure does NOT come from a fixed "poisonous gas" tag - it comes from
/// each agent's locked-in "ideal mix" (AgentController.idealGasMix) deviating from
/// the CURRENT composition as it drifts. See AgentController for the tolerance/
/// speciation mechanic that consumes this data.
public class AtmosphereManager : MonoBehaviour
{
    [Header("Respiration")]
    [Tooltip("Fraction of atmosphere exchanged per second, per agent, at full activity.")]
    public float respirationRate = 0.000015f;

    public static AtmosphereManager Instance { get; private set; }

    public bool GreatGasEventFired { get; private set; }
    public IReadOnlyList<GasDefinition> Gases => _gases;

    /// The Section 2 weighted-table type rolled for this world (see AtmosphereTypeTable).
    public AtmosphereTypeDef RolledType { get; private set; }
    /// Section 1 Step 4: reference surface pressure (bar), rolled within RolledType's band.
    public float PressureBar { get; private set; }
    /// The element-backbone metabolism rolled for the founding colony (see OrganismBiochemistry.cs).
    public BiochemistryDef RolledBiochemistry { get; private set; }

    private readonly List<GasDefinition> _gases = new List<GasDefinition>();
    private AgentSpawner _agentSpawner;
    private DayNightCycle _dayNight;
    private bool _expelledGlutFired;
    private float _gameTimeElapsed; // real seconds since Init() - guards early crisis events

    void Awake() => Instance = this;

    /// Section 1's generation algorithm, scoped to this sim: roll atmosphere type
    /// (reweighted by the planet's rock archetype, Section 6), roll dominant-species
    /// fractions within the type's band, add 1-3 trace species (Step 7). The type's
    /// first dominant species becomes Breathed, the second Expelled, for our existing
    /// respiration mechanic - a simplification of the spec's full composition table.
    public void Init(AgentSpawner agentSpawner, DayNightCycle dayNight, RockArchetypeDef archetype)
    {
        _agentSpawner = agentSpawner;
        _dayNight = dayNight;
        GenerateAtmosphere(archetype);
    }

    private void GenerateAtmosphere(RockArchetypeDef archetype)
    {
        _gases.Clear();
        RolledType = AtmosphereTypeTable.Roll(archetype);
        PressureBar = AtmosphereTypeTable.RollPressureBar(RolledType);

        // Pure atmospheric chemistry first - everything starts as Trace; no Breathed/
        // Expelled roles exist until a compatible organism biochemistry is rolled below.
        var (name0, min0, max0) = RolledType.DominantSpecies[0];
        var (name1, min1, max1) = RolledType.DominantSpecies[1];
        _gases.Add(GasDefinition.Make(name0, Random.Range(min0, max0)));
        _gases.Add(GasDefinition.Make(name1, Random.Range(min1, max1)));

        int traceCount = Random.Range(1, 4);
        var pool = new List<string>(RolledType.TracePool);
        for (int i = 0; i < traceCount && pool.Count > 0; i++)
        {
            int idx = Random.Range(0, pool.Count);
            string name = pool[idx];
            pool.RemoveAt(idx);
            if (name == name0 || name == name1) continue;
            _gases.Add(GasDefinition.Make(name, Random.Range(0.0001f, 0.02f)));
        }

        NormalizeFractions();

        RolledBiochemistry = OrganismBiochemistryTable.Roll(RolledType);
        AssignRespirationRoles(RolledBiochemistry);

        Debug.Log($"[Atmosphere] Type '{RolledType.Name}' | Biochemistry '{RolledBiochemistry.Name}' ({RolledBiochemistry.Backbone}-based): " +
            string.Join(" | ", _gases.ConvertAll(g => $"{g.Name} {g.Fraction * 100f:F1}% ({g.Role})")));
    }

    /// Layers the rolled biochemistry's Breathed/Expelled gases onto the already-
    /// generated composition. The breathed gas is added at a modest starting fraction
    /// if it wasn't naturally part of the roll (organisms need something to breathe
    /// from genesis); the expelled gas is added at exactly 0% if absent - it is a
    /// byproduct that respiration introduces over time, not a precondition for life.
    private void AssignRespirationRoles(BiochemistryDef biochem)
    {
        GasDefinition breathed = FindGas(biochem.BreathedGas);
        if (breathed == null)
        {
            breathed = GasDefinition.Make(biochem.BreathedGas, 0.05f);
            _gases.Add(breathed);
        }
        breathed.Role = GasRole.Breathed;
        breathed.CrisisLow = 0.10f;

        GasDefinition expelled = FindGas(biochem.ExpelledGas);
        if (expelled == null)
        {
            expelled = GasDefinition.Make(biochem.ExpelledGas, 0f);
            _gases.Add(expelled);
        }
        expelled.Role = GasRole.Expelled;
        expelled.CrisisHigh = 0.70f;

        NormalizeFractions();
    }

    private GasDefinition FindGas(string name)
    {
        foreach (var g in _gases) if (g.Name == name) return g;
        return null;
    }

    void Update()
    {
        if (_agentSpawner == null) return;

        // Update UV ozone shield and chemical nutrient pool each frame.
        UVManager.UpdateOzone(this);
        ChemicalNutrientPool.Replenish(Time.deltaTime);

        GasDefinition breathed = GetGasByRole(GasRole.Breathed);
        GasDefinition expelled = GetGasByRole(GasRole.Expelled);

        // Gas exchange is metabolism-specific:
        //   Chemosynthetic  → no atmospheric exchange (energy from ChemicalNutrientPool only)
        //   Phototrophic    → solar-dependent reverse exchange (consume Expelled, produce Breathed)
        //                     this is the driver of the Great Gas Event once photosynthesis evolves
        //   Heterotrophic   → consume Breathed, produce Expelled (standard respiration)
        int heterotrophCount = 0, phototrophCount = 0;
        float phototrophSolarSum = 0f;
        foreach (var agent in _agentSpawner.ActiveAgents)
        {
            if (agent == null) continue;
            switch (agent.Metabolism)
            {
                case MetabolismType.Phototrophic:
                    phototrophCount++;
                    if (_dayNight != null)
                    {
                        Vector3 normal = (agent.transform.position - agent.planetCenter).normalized;
                        phototrophSolarSum += _dayNight.SolarExposure(normal);
                    }
                    else phototrophSolarSum += 0.5f;
                    break;
                case MetabolismType.Heterotrophic:
                    heterotrophCount++;
                    break;
                // Chemosynthetic: no case — zero atmospheric exchange
            }
        }
        float phototrophSolarAvg = phototrophCount > 0 ? phototrophSolarSum / phototrophCount : 0f;

        float dt = Time.deltaTime;
        _gameTimeElapsed += dt;

        // Heterotrophs breathe Breathed gas and exhale Expelled.
        float consumerExchange = respirationRate * heterotrophCount * dt;
        if (breathed != null) breathed.Fraction -= consumerExchange;
        if (expelled != null) expelled.Fraction += consumerExchange;

        // Phototrophics run the exchange in reverse on the day side.
        float producerExchange = respirationRate * phototrophCount * dt * phototrophSolarAvg;
        if (expelled != null) expelled.Fraction -= producerExchange;
        if (breathed != null) breathed.Fraction += producerExchange;

        NormalizeFractions();

        // Guard: the Great Gas Event should only fire once the simulation is past
        // early Abiogenesis — it represents the GOE or equivalent crisis, which
        // biologically requires a population large enough to have already altered the
        // atmosphere. Require at least Era 1b (GOE phase = Era1StartIndex + 2) OR
        // a minimum of 45 real seconds of gameplay as a fallback when DeepTimeClock
        // hasn't advanced that far yet.
        bool gasEventAllowed = false;
        if (DeepTimeClock.Instance != null)
            gasEventAllowed = DeepTimeClock.Instance.CurrentPhaseIndex >= EraTimeline.Era1StartIndex + 2;
        if (!gasEventAllowed && _gameTimeElapsed >= 45f)
            gasEventAllowed = true;

        bool hasLife = _agentSpawner != null && _agentSpawner.ActiveAgents.Count > 0;
        if (!GreatGasEventFired && gasEventAllowed && hasLife && breathed != null && breathed.CrisisLow > 0f && breathed.Fraction < breathed.CrisisLow)
        {
            GreatGasEventFired = true;
            FireGreatGasEvent();
        }

        // After the event fires, hold the breathed gas at a permanently depleted level —
        // NormalizeFractions can let it creep back up via producers, but the world should
        // stay hostile until the population has actually evolved efficient respiration.
        if (GreatGasEventFired && breathed != null)
            breathed.Fraction = Mathf.Min(breathed.Fraction, 0.05f);

        if (_crisisFlashTimer > 0f)
            _crisisFlashTimer -= Time.deltaTime;
        // Same guard as GreatGasEvent: don't fire if we're still in the intro/genesis
        // phase or no life exists yet. ExpelledGlut requires exhaled-gas buildup from a
        // real population, not just the initial atmospheric composition.
        if (!_expelledGlutFired && gasEventAllowed
            && _agentSpawner != null && _agentSpawner.ActiveAgents.Count > 0
            && expelled != null && expelled.Fraction > expelled.CrisisHigh)
        {
            _expelledGlutFired = true;
            GeneEvolutionManager.QueueAtmosphereEvent("ExpelledGlutEvent");
        }

        // Jeans escape: light gases (H2, He) bleed off based on escape velocity vs
        // thermal velocity. Rate ∝ (v_thermal/v_escape)² — bound atmospheres lose very
        // little; thin/low-gravity ones lose light gas quickly. Magnetosphere presence
        // suppresses solar-wind stripping by 20×.
        if (PlanetGravity.Instance != null)
        {
            float vEsc    = PlanetGravity.Instance.EscapeVelocityMs;
            float tempK   = PlanetTemperature.Instance != null ? PlanetTemperature.Instance.CurrentK : 300f;
            float magMult = PlanetGravity.Instance.HasMagnetosphere ? 0.05f : 1.0f;
            float dtSec   = Time.deltaTime;
            bool stripped = false;
            foreach (var gas in _gases)
            {
                float molMassKg = gas.Name switch { "H2" => 3.34e-27f, "He" => 6.68e-27f, _ => 0f };
                if (molMassKg <= 0f) continue;
                float vThermal  = Mathf.Sqrt(2f * 1.38e-23f * tempK / molMassKg);
                float ratio     = vThermal / Mathf.Max(vEsc, 1f);
                float jeansRate = ratio * ratio * magMult * 0.05f; // gameplay-scaled; physically ordered
                gas.Fraction   -= jeansRate * gas.Fraction * dtSec;
                gas.Fraction    = Mathf.Max(0f, gas.Fraction);
                stripped = true;
            }
            if (stripped) NormalizeFractions();
        }
    }

    private void NormalizeFractions()
    {
        float total = 0f;
        foreach (var g in _gases) { g.Fraction = Mathf.Max(0f, g.Fraction); total += g.Fraction; }
        if (total > 0f) foreach (var g in _gases) g.Fraction /= total;
    }

    private GasDefinition GetGasByRole(GasRole role)
    {
        foreach (var g in _gases) if (g.Role == role) return g;
        return null;
    }

    /// Returns the current fraction of the named gas (0 if not present in this atmosphere).
    public float GetFraction(string gasName)
    {
        foreach (var g in _gases) if (g.Name == gasName) return g.Fraction;
        return 0f;
    }

    /// Adds `amount` to the named gas's fraction (creating the gas as Trace if absent).
    /// Called by FluidDynamicsManager when sublimation injects a solid directly into gas phase.
    public void AddGasFraction(string name, float amount)
    {
        GasDefinition gas = FindGas(name);
        if (gas == null)
        {
            gas = GasDefinition.Make(name, 0f, GasRole.Trace);
            _gases.Add(gas);
        }
        gas.Fraction = Mathf.Max(0f, gas.Fraction + amount);
        NormalizeFractions();
    }

    /// Snapshot of the current atmosphere composition, keyed by gas name. Used by
    /// AgentController to lock in a new "ideal mix" at genesis or speciation.
    public Dictionary<string, float> SnapshotMix()
    {
        var mix = new Dictionary<string, float>();
        foreach (var g in _gases) mix[g.Name] = g.Fraction;
        return mix;
    }

    public float AtmosphericStress => AverageAtmosphericStress();
    private float AverageAtmosphericStress()
    {
        if (_agentSpawner == null || _agentSpawner.ActiveAgents.Count == 0) return 0f;
        float total = 0f;
        int count = 0;
        foreach (var agent in _agentSpawner.ActiveAgents)
        {
            if (agent == null) continue;
            total += agent.AtmosphericDiscomfort;
            count++;
        }
        return count > 0 ? total / count : 0f;
    }

    // Flash timer for the red-screen crisis overlay.
    private float _crisisFlashTimer;

    private void FireGreatGasEvent()
    {
        Debug.Log("[Atmosphere] GREAT GAS EVENT — breathed gas has collapsed! Mass die-off commencing.");
        GeneEvolutionManager.QueueAtmosphereEvent("GreatGasEvent");
        _crisisFlashTimer = 4f;

        if (_agentSpawner != null)
        {
            // Kill ~85% of all consumers immediately — this is an extinction-class event,
            // not a minor setback. Only a resistant tail survives and adapts.
            var toKill = new List<AgentController>();
            foreach (var agent in _agentSpawner.ActiveAgents)
                if (agent != null && !agent.IsProducer && Random.value < 0.85f) toKill.Add(agent);
            foreach (var agent in toKill)
                if (agent != null) agent.Die();
        }

        // Permanently clamp the breathed gas near-zero so the crisis doesn't quietly
        // reverse itself — survivors must evolve their way out, not just wait.
        GasDefinition breathed = GetGasByRole(GasRole.Breathed);
        if (breathed != null) breathed.CrisisLow = -1f; // disable re-trigger; lock handled in Update
    }

    void OnGUI()
    {
        // Crisis flash: full-screen red tint that fades out over 4 seconds.
        if (_crisisFlashTimer > 0f)
        {
            float alpha = Mathf.Clamp01(_crisisFlashTimer / 4f) * 0.6f;
            Color prev = GUI.color;
            GUI.color = new Color(0.9f, 0.05f, 0.05f, alpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.skin.label.fontSize = 28;
            GUI.Label(new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.42f, 440f, 60f),
                "⚠  BREATHABLE GAS COLLAPSED  ⚠");
            GUI.skin.label.fontSize = 0; // reset to default
            GUI.color = prev;
        }

        if (GameHUD.SuppressRawOverlays) return;

        float barWidth = 220f;
        float barHeight = 16f;
        float x = Screen.width - barWidth - 10f;
        float y = 40f; // leave room for AtmosphereVisual's on/off toggle button above

        GUI.Label(new Rect(x, y, barWidth, 18f), "Atmosphere");
        y += 18f;

        float avgStress = AverageAtmosphericStress();
        Color stressColor = Color.Lerp(new Color(0.3f, 0.9f, 0.3f), new Color(0.9f, 0.2f, 0.3f), avgStress);
        Color prevC = GUI.color;
        GUI.color = stressColor;
        GUI.Label(new Rect(x, y, barWidth, 16f), $"Avg population stress: {avgStress * 100f:F0}%");
        GUI.color = prevC;
        y += 16f;

        float cursor = x;
        foreach (var g in _gases)
        {
            float w = barWidth * g.Fraction;
            GUI.DrawTexture(new Rect(cursor, y, Mathf.Max(w, 1f), barHeight), Texture2D.whiteTexture,
                ScaleMode.StretchToFill, false, 0f, GasColor(g.Role), 0f, 0f);
            cursor += w;
        }
        y += barHeight + 2f;

        foreach (var g in _gases)
        {
            Color prev = GUI.color;
            GUI.color = GasColor(g.Role);
            GUI.Label(new Rect(x, y, barWidth, 16f), $"{g.Name}: {g.Fraction * 100f:F1}% ({g.Role})");
            GUI.color = prev;
            y += 15f;
        }
    }

    private static Color GasColor(GasRole role) => role switch
    {
        GasRole.Breathed => new Color(0.3f, 0.9f, 0.3f),
        GasRole.Expelled => new Color(0.85f, 0.85f, 0.3f),
        GasRole.Trace    => new Color(0.55f, 0.55f, 0.55f),
        _                => Color.white
    };
}
