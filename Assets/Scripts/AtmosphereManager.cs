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
        _gases.Add(new GasDefinition { Name = name0, Fraction = Random.Range(min0, max0), Role = GasRole.Trace });
        _gases.Add(new GasDefinition { Name = name1, Fraction = Random.Range(min1, max1), Role = GasRole.Trace });

        int traceCount = Random.Range(1, 4);
        var pool = new List<string>(RolledType.TracePool);
        for (int i = 0; i < traceCount && pool.Count > 0; i++)
        {
            int idx = Random.Range(0, pool.Count);
            string name = pool[idx];
            pool.RemoveAt(idx);
            if (name == name0 || name == name1) continue;
            _gases.Add(new GasDefinition { Name = name, Fraction = Random.Range(0.0001f, 0.02f), Role = GasRole.Trace });
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
            breathed = new GasDefinition { Name = biochem.BreathedGas, Fraction = 0.05f };
            _gases.Add(breathed);
        }
        breathed.Role = GasRole.Breathed;
        breathed.CrisisLow = 0.10f;

        GasDefinition expelled = FindGas(biochem.ExpelledGas);
        if (expelled == null)
        {
            expelled = new GasDefinition { Name = biochem.ExpelledGas, Fraction = 0f };
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

        GasDefinition breathed = GetGasByRole(GasRole.Breathed);
        GasDefinition expelled = GetGasByRole(GasRole.Expelled);

        int consumerCount = 0, producerCount = 0;
        float producerSolarSum = 0f;
        foreach (var agent in _agentSpawner.ActiveAgents)
        {
            if (agent == null) continue;
            if (agent.IsProducer)
            {
                producerCount++;
                if (_dayNight != null)
                {
                    Vector3 normal = (agent.transform.position - agent.planetCenter).normalized;
                    producerSolarSum += _dayNight.SolarExposure(normal);
                }
                else producerSolarSum += 0.5f;
            }
            else consumerCount++;
        }
        float producerSolarAvg = producerCount > 0 ? producerSolarSum / producerCount : 0f;

        float dt = Time.deltaTime;

        float consumerExchange = respirationRate * consumerCount * dt;
        if (breathed != null) breathed.Fraction -= consumerExchange;
        if (expelled != null) expelled.Fraction += consumerExchange;

        float producerExchange = respirationRate * producerCount * dt * producerSolarAvg;
        if (expelled != null) expelled.Fraction -= producerExchange;
        if (breathed != null) breathed.Fraction += producerExchange;

        NormalizeFractions();

        if (!GreatGasEventFired && breathed != null && breathed.Fraction < breathed.CrisisLow)
        {
            GreatGasEventFired = true;
            FireGreatGasEvent();
        }
        if (!_expelledGlutFired && expelled != null && expelled.Fraction > expelled.CrisisHigh)
        {
            _expelledGlutFired = true;
            GeneEvolutionManager.QueueAtmosphereEvent("ExpelledGlutEvent");
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

    /// Snapshot of the current atmosphere composition, keyed by gas name. Used by
    /// AgentController to lock in a new "ideal mix" at genesis or speciation.
    public Dictionary<string, float> SnapshotMix()
    {
        var mix = new Dictionary<string, float>();
        foreach (var g in _gases) mix[g.Name] = g.Fraction;
        return mix;
    }

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

    private void FireGreatGasEvent()
    {
        Debug.Log("[Atmosphere] Great Gas Event! Breathed gas has collapsed.");
        GeneEvolutionManager.QueueAtmosphereEvent("GreatGasEvent");

        if (_agentSpawner != null)
        {
            var toKill = new List<AgentController>();
            foreach (var agent in _agentSpawner.ActiveAgents)
                if (agent != null && !agent.IsProducer && Random.value < 0.35f) toKill.Add(agent);
            foreach (var agent in toKill)
                if (agent != null) agent.Die();
        }
    }

    void OnGUI()
    {
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
