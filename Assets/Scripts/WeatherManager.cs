using System.Collections.Generic;
using UnityEngine;

/// V1/believable precipitation layer (not real fluid dynamics): periodically spawns
/// spatially-localized storm cells that drift across the sphere surface following
/// WindManager's field, and temporarily perturb ClimateManager-style moisture/
/// temperature readings under their footprint while they're alive. Coarse-ticked
/// (storms advance on a timer, not every frame) to stay cheap against the ~15k-vertex
/// planet mesh, following the same "real logic, procedural specifics" scoping used by
/// AtmosphereManager/TectonicPlanetGenerator elsewhere in this project.
///
/// No fluid/liquid-pooling simulation exists yet in this codebase (LiquidChemistry +
/// PlanetTileMesh.BuildLiquidShellData only place a single static liquid shell once,
/// at world-gen) - so for now storms only perturb the abstract moisture/temperature
/// fields ClimateManager already exposes. If/when a real fluid-pooling system lands,
/// StormCell's per-tick deposit should be redirected to add liquid volume there
/// instead of (or in addition to) the moisture-field bump; search this file for
/// "TODO fluid" for the single integration point.
public class WeatherManager : MonoBehaviour
{
    [Header("Spawning")]
    [Tooltip("Average real-time seconds between new storm cells spawning somewhere on the planet.")]
    public float avgSpawnIntervalSeconds = 18f;
    [Tooltip("Max simultaneous storm cells - keeps the coarse update loop bounded.")]
    public int maxActiveStorms = 6;

    [Header("Storm shape/lifetime")]
    public float minRadiusFraction = 0.08f; // fraction of planetRadius
    public float maxRadiusFraction = 0.22f;
    public float minLifetimeSeconds = 20f;
    public float maxLifetimeSeconds = 50f;

    [Header("Effect strength")]
    [Tooltip("How much a storm at full intensity raises local moisture (0-100 scale, same units as ClimateManager).")]
    public float moistureBoost = 45f;
    [Tooltip("How much a storm at full intensity lowers local temperature (0-100 scale) - rain/overcast cooling.")]
    public float temperatureDrop = 12f;
    [Tooltip("How fast a storm's footprint moves across the sphere, as a multiplier on WindManager's local wind speed.")]
    public float advectionMultiplier = 0.6f;

    [Header("Tick rate")]
    [Tooltip("How often (seconds) storm positions/lifetimes are advanced. Coarser = cheaper, per AtmosphereManager's existing per-tick pattern.")]
    public float tickInterval = 0.5f;

    public static WeatherManager Instance { get; private set; }
    public IReadOnlyList<StormCell> ActiveStorms => _storms;

    private readonly List<StormCell> _storms = new List<StormCell>();
    private Vector3 _planetCenter;
    private float _planetRadius;
    private float _tickTimer;
    private float _spawnTimer;

    public class StormCell
    {
        public Vector3 Position; // world-space point ON the sphere surface
        public float RadiusWorld;
        public float Age;
        public float Lifetime;
        public float Intensity => Mathf.Clamp01(Mathf.Min(Age / 3f, (Lifetime - Age) / 5f)); // quick ramp-up, slower fade-out
    }

    public void Init(Vector3 planetCenter, float planetRadius)
    {
        Instance = this;
        _planetCenter = planetCenter;
        _planetRadius = planetRadius;
        _spawnTimer = Random.Range(0f, avgSpawnIntervalSeconds);
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void Update()
    {
        _tickTimer += Time.deltaTime;
        if (_tickTimer < tickInterval) return;
        float dt = _tickTimer;
        _tickTimer = 0f;

        AdvanceStorms(dt);
        HandleSpawning(dt);
    }

    private void AdvanceStorms(float dt)
    {
        for (int i = _storms.Count - 1; i >= 0; i--)
        {
            StormCell storm = _storms[i];
            storm.Age += dt;
            if (storm.Age >= storm.Lifetime)
            {
                _storms.RemoveAt(i);
                continue;
            }

            // Advect along the wind field at this storm's current position - this is
            // what makes precipitation "move across the planet surface" rather than
            // sitting static, per the v1 spec (driven by the wind field, not its own
            // independent motion model).
            Vector3 wind = WindManager.GetWind(storm.Position);
            Vector3 normal = (storm.Position - _planetCenter).normalized;
            Vector3 newPos = storm.Position + wind * advectionMultiplier * dt;
            // Re-project onto the sphere so the storm stays on the surface as it moves.
            newPos = _planetCenter + (newPos - _planetCenter).normalized * _planetRadius;
            storm.Position = newPos;

            // TODO fluid: once a liquid-pooling system exists, deposit storm.Intensity
            // worth of liquid volume into terrain under storm.Position here each tick,
            // in addition to (or instead of) the moisture-field perturbation below.
        }
    }

    private void HandleSpawning(float dt)
    {
        _spawnTimer -= dt;
        if (_spawnTimer > 0f) return;
        _spawnTimer = avgSpawnIntervalSeconds * Random.Range(0.6f, 1.4f);

        if (_storms.Count >= maxActiveStorms) return;

        Vector3 randomPoint = Random.onUnitSphere;
        var storm = new StormCell
        {
            Position = _planetCenter + randomPoint * _planetRadius,
            RadiusWorld = _planetRadius * Random.Range(minRadiusFraction, maxRadiusFraction),
            Age = 0f,
            Lifetime = Random.Range(minLifetimeSeconds, maxLifetimeSeconds),
        };
        _storms.Add(storm);
    }

    /// Additive moisture perturbation (can be negative-free, storms only add
    /// moisture) at worldPosition, summed across all overlapping storm footprints.
    /// ClimateManager.GetMoisture adds this on top of its static noise field.
    public float GetMoistureModifier(Vector3 worldPosition)
    {
        float total = 0f;
        foreach (var storm in _storms)
        {
            float falloff = Footprint(storm, worldPosition);
            if (falloff <= 0f) continue;
            total += moistureBoost * storm.Intensity * falloff;
        }
        return total;
    }

    /// Additive temperature perturbation (storms cool things down - overcast/rain) at
    /// worldPosition. Negative or zero; ClimateManager.GetTemperature adds this on
    /// top of its static noise field and clamps the result so agents never see NaN
    /// or out-of-range values.
    public float GetTemperatureModifier(Vector3 worldPosition)
    {
        float total = 0f;
        foreach (var storm in _storms)
        {
            float falloff = Footprint(storm, worldPosition);
            if (falloff <= 0f) continue;
            total -= temperatureDrop * storm.Intensity * falloff;
        }
        return total;
    }

    /// 0..1 falloff from storm center using great-circle (geodesic) distance on the
    /// sphere, smoothstepped to zero at the storm's radius.
    private float Footprint(StormCell storm, Vector3 worldPosition)
    {
        Vector3 a = (storm.Position - _planetCenter).normalized;
        Vector3 b = (worldPosition - _planetCenter).normalized;
        float angularDist = Vector3.Angle(a, b) * Mathf.Deg2Rad;
        float arcDist = angularDist * _planetRadius;
        if (arcDist >= storm.RadiusWorld) return 0f;
        return 1f - Mathf.SmoothStep(0f, 1f, arcDist / storm.RadiusWorld);
    }

    void OnGUI()
    {
        if (_storms.Count == 0) return;
        float x = 10f, y = 60f;
        GUI.Label(new Rect(x, y, 260f, 18f), $"Active storms: {_storms.Count}");
    }
}
