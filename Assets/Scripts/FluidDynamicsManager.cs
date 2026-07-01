using System.Collections.Generic;
using UnityEngine;

/// Ongoing LIVE liquid simulation that takes over once GenesisCinematic's one-time flood
/// fill has revealed the initial ocean. Three coupled mechanisms, each running on its own
/// coarse tick so a ~15k-vertex sphere stays cheap:
///   1. Flow: per-vertex liquid VOLUME diffuses toward lower neighbors across the same
///      mesh-adjacency graph TectonicPlanetGenerator builds for thermal erosion (reused via
///      TectonicPlanetGenerator.BuildVertexAdjacency), using the identical degree-normalized
///      Jacobi-style transfer pattern as ApplyThermalErosion so it can't blow up the same way
///      an un-normalized diffusion step did there.
///   2. Evaporation/precipitation: liquid evaporates off wet vertices faster on the day side
///      (DayNightCycle.SolarExposure) and at higher PlanetTemperature.CurrentK, accumulating
///      into a single global "atmospheric moisture" reservoir (no real cloud transport - out
///      of scope per the task). The reservoir precipitates back down stochastically, biased
///      toward vertices that are already wet (or adjacent to wet vertices) so deserts don't
///      get random ocean spawns but existing lakes/seas get rain back.
///   3. Mesh rebuild: PlanetTileMesh.BuildLiquidShellDataFromVolume re-derives the translucent
///      liquid shell mesh from current per-vertex volume on its own (coarser) timer.
///
/// Gameplay code can query current liquid depth at a world position via
/// GetLiquidDepthAtVertex/GetLiquidDepthNearPosition without depending on any of the above -
/// the per-vertex volume array is the single source of truth.
public class FluidDynamicsManager : MonoBehaviour
{
    public static FluidDynamicsManager Instance { get; private set; }

    [Header("Flow simulation")]
    [Tooltip("Seconds between flow-diffusion ticks. Coarser than per-frame, mirrors the thermal erosion pass's cost profile.")]
    public float flowTickInterval = 0.75f;
    [Tooltip("Max liquid-volume difference between neighbors that flow will NOT smooth out (a stable 'pool' tolerance), same role as erosionMaxSlope.")]
    public float flowMaxSlope = 0.02f;
    [Tooltip("Fraction of the over-threshold volume difference transferred per tick, divided by vertex degree for stability (same normalization as ApplyThermalErosion).")]
    public float flowTransferRate = 0.5f;

    [Header("Evaporation")]
    [Tooltip("Base evaporation rate (volume/sec) drawn from every wet vertex regardless of conditions.")]
    public float baseEvaporationRate = 0.0008f;
    [Tooltip("Extra evaporation rate (volume/sec) at full day-side solar exposure (SolarExposure == 1).")]
    public float solarEvaporationRate = 0.0025f;
    [Tooltip("Extra evaporation rate (volume/sec) per Kelvin above this reference temperature.")]
    public float temperatureReferenceK = 280f;
    [Tooltip("Evaporation rate added per Kelvin above temperatureReferenceK.")]
    public float evaporationPerDegreeK = 0.00004f;

    [Header("Precipitation")]
    [Tooltip("Fraction of the global moisture reservoir that precipitates back down per second.")]
    public float precipitationRate = 0.02f;
    [Tooltip("How strongly precipitation favors already-wet (or wet-adjacent) vertices over arbitrary dry ones. 0 = uniform random, 1 = fully wet-biased.")]
    [Range(0f, 1f)] public float wetBias = 0.85f;
    [Tooltip("Number of candidate vertices sampled per precipitation tick before weighting by wetBias.")]
    public int precipitationCandidatesPerTick = 24;

    [Header("Mesh rebuild")]
    [Tooltip("Seconds between liquid shell mesh rebuilds - independent of (and coarser than) the flow tick.")]
    public float meshRebuildInterval = 1f;
    [Tooltip("Minimum per-vertex liquid volume to be rendered as part of the shell mesh (filters out evaporation dust/noise).")]
    public float minVolumeToRender = 0.01f;

    private TectonicResult _tectonics;
    private List<int>[] _adjacency;
    private float[] _liquidVolume;
    private float _radius;
    private float _elevationWorldScale;
    private LiquidDef _liquid;
    private System.Func<float> _getLiquidTempK;
    private DayNightCycle _dayNight;
    private Vector3 _planetCenter;
    private TidalForceManager _tidal;

    /// The liquid's current vertex color (temperature-evaluated). StormVisualManager reads
    /// this to tint rain particles so they match the fluid on the ground.
    public Color CurrentLiquidColor => _liquid != null && _getLiquidTempK != null
        ? _liquid.ColorAt(_getLiquidTempK())
        : new Color(0.06f, 0.22f, 0.5f, 0.9f);

    private GameObject _liquidGo;
    private MeshFilter _liquidFilter;
    private Mesh _liquidMesh;

    private float[] _prevVolume;
    private float[] _targetVolume;
    private float[] _displayVolume;
    private float _lerpT;
    private float _lerpDuration;

    private float _moistureReservoir;
    private float _flowTickTimer;

    /// Wires up the live simulation using the SAME tectonics/seaLevel/liquid data the genesis
    /// flood fill used, so the takeover is seamless (same wet vertices start wet). Call this
    /// from SimulationBootstrap's GenesisCinematic onComplete callback, after the static
    /// liquid shell has already been revealed. liquidGo/liquidFilter/liquidMesh are the SAME
    /// GameObject/MeshFilter/Mesh GenesisCinematic created, so this manager keeps animating
    /// the existing visual object instead of spawning a duplicate.
    public void Init(TectonicResult tectonics, float radius, float elevationWorldScale,
        float seaLevelElevation, LiquidDef liquid, System.Func<float> getLiquidTempK,
        DayNightCycle dayNight, Vector3 planetCenter,
        GameObject liquidGo, MeshFilter liquidFilter, Mesh liquidMesh,
        TidalForceManager tidal = null)
    {
        Instance = this;
        _tectonics = tectonics;
        _radius = radius;
        _elevationWorldScale = elevationWorldScale;
        _liquid = liquid;
        _getLiquidTempK = getLiquidTempK;
        _dayNight = dayNight;
        _planetCenter = planetCenter;
        _liquidGo = liquidGo;
        _liquidFilter = liquidFilter;
        _liquidMesh = liquidMesh;
        _tidal = tidal;

        _adjacency = TectonicPlanetGenerator.BuildVertexAdjacency(tectonics.UnitVerts.Count, tectonics.Triangles);

        int n = tectonics.Elevation.Length;
        _liquidVolume = new float[n];
        if (liquid != null)
        {
            // Seed per-vertex volume from the SAME flood criterion the genesis shell used:
            // any vertex below sea level starts with a depth proportional to how far below it is.
            for (int v = 0; v < n; v++)
            {
                float depth = seaLevelElevation - tectonics.Elevation[v];
                if (depth > 0f) _liquidVolume[v] = depth;
            }
        }

        _prevVolume = new float[n];
        _targetVolume = (float[])_liquidVolume.Clone();
        _displayVolume = (float[])_liquidVolume.Clone();
        _lerpT = 1f;
        _lerpDuration = flowTickInterval;

        _flowTickTimer = 0f;
        _moistureReservoir = 0f;
        enabled = liquid != null; // nothing to simulate on a dry world
    }

    void Awake() => Instance = this;

    void Update()
    {
        if (_liquid == null || _liquidVolume == null) return;

        float dt = Time.deltaTime;

        _flowTickTimer += dt;
        if (_flowTickTimer >= flowTickInterval)
        {
            float tickDt = _flowTickTimer;
            _lerpDuration = _flowTickTimer;
            _flowTickTimer = 0f;
            System.Array.Copy(_liquidVolume, _prevVolume, _liquidVolume.Length);
            StepFlow();
            StepEvaporationAndPrecipitation(tickDt);
            System.Array.Copy(_liquidVolume, _targetVolume, _liquidVolume.Length);
            _lerpT = 0f;
        }

        if (_lerpDuration > 0f)
            _lerpT = Mathf.Clamp01(_lerpT + dt / _lerpDuration);

        int n = _displayVolume.Length;
        for (int v = 0; v < n; v++)
            _displayVolume[v] = Mathf.Lerp(_prevVolume[v], _targetVolume[v], _lerpT);

        RebuildShellMesh();
    }

    /// Degree-normalized Jacobi-style diffusion of liquid volume toward lower neighbors -
    /// structurally identical to TectonicPlanetGenerator.ApplyThermalErosion's stable pattern
    /// (every vertex's total outflow in one pass is bounded so a high-degree vertex can't
    /// push out more volume than it actually holds and oscillate/blow up), just operating on
    /// _liquidVolume instead of elevation, and additionally clamped to never push a vertex's
    /// volume below zero (elevation has no such floor, liquid volume does).
    private void StepFlow()
    {
        int n = _liquidVolume.Length;
        var next = (float[])_liquidVolume.Clone();

        for (int v = 0; v < n; v++)
        {
            if (_liquidVolume[v] <= 0f) continue;
            int degree = Mathf.Max(1, _adjacency[v].Count);
            // Effective "height" liquid flows down is terrain elevation plus its own liquid
            // depth (a filled vertex effectively raises its local surface), matching how a
            // real water table would slosh toward lower combined ground+liquid height.
            float heightV = _tectonics.Elevation[v] + _liquidVolume[v];

            foreach (int nb in _adjacency[v])
            {
                float heightNb = _tectonics.Elevation[nb] + _liquidVolume[nb];
                float diff = heightV - heightNb;
                if (diff > flowMaxSlope)
                {
                    float transfer = (diff - flowMaxSlope) * flowTransferRate / degree;
                    transfer = Mathf.Min(transfer, _liquidVolume[v]); // never send more than we have
                    next[v] -= transfer;
                    next[nb] += transfer;
                }
            }
        }

        for (int v = 0; v < n; v++) _liquidVolume[v] = Mathf.Max(0f, next[v]);
    }

    /// Evaporates volume off wet vertices into the global moisture reservoir, biased by solar
    /// exposure and surface temperature, then precipitates a fraction of the reservoir back
    /// down onto a wet-biased random sample of vertices.
    private void StepEvaporationAndPrecipitation(float dt)
    {
        int n = _liquidVolume.Length;
        float tempK = PlanetTemperature.Instance != null ? PlanetTemperature.Instance.CurrentK : temperatureReferenceK;
        float tempExcess = Mathf.Max(0f, tempK - temperatureReferenceK);

        for (int v = 0; v < n; v++)
        {
            if (_liquidVolume[v] <= 0f) continue;

            float solar = 0.5f;
            if (_dayNight != null)
            {
                Vector3 normal = _tectonics.UnitVerts[v]; // unit vert IS the surface normal direction from planet center
                solar = _dayNight.SolarExposure(normal);
            }

            float rate = baseEvaporationRate + solar * solarEvaporationRate + tempExcess * evaporationPerDegreeK;
            float evaporated = Mathf.Min(_liquidVolume[v], rate * dt);
            _liquidVolume[v] -= evaporated;
            _moistureReservoir += evaporated;
        }

        float toPrecipitate = Mathf.Min(_moistureReservoir, _moistureReservoir * precipitationRate * dt * 60f);
        // *60f compensates for precipitationRate being expressed as "fraction per second" while
        // dt here is a multi-second flow-tick interval, not a per-frame delta.
        if (toPrecipitate <= 0f) return;
        _moistureReservoir -= toPrecipitate;

        DistributePrecipitation(toPrecipitate);
    }

    /// Sample a handful of candidate vertices, weight them toward ones that are already wet
    /// (or directly adjacent to a wet vertex), and rain the precipitation budget onto the
    /// single best-weighted candidate each call. Keeps existing lakes/seas topped up while
    /// keeping deserts dry except for the occasional unbiased drop (wetBias < 1).
    private void DistributePrecipitation(float amount)
    {
        int n = _liquidVolume.Length;
        if (n == 0) return;

        int bestVertex = -1;
        float bestScore = -1f;

        for (int i = 0; i < precipitationCandidatesPerTick; i++)
        {
            int candidate = Random.Range(0, n);
            float wetness = _liquidVolume[candidate] > 0f ? 1f : NeighborWetness(candidate);
            float score = Mathf.Lerp(Random.value, wetness + Random.value * 0.01f, wetBias);
            if (score > bestScore) { bestScore = score; bestVertex = candidate; }
        }

        if (bestVertex >= 0) _liquidVolume[bestVertex] += amount;
    }

    private float NeighborWetness(int vertex)
    {
        if (_adjacency == null) return 0f;
        foreach (int nb in _adjacency[vertex])
            if (_liquidVolume[nb] > 0f) return 1f;
        return 0f;
    }

    private void RebuildShellMesh()
    {
        if (_liquidGo == null || _liquidMesh == null || _liquid == null) return;

        float tempK = _getLiquidTempK != null ? _getLiquidTempK() : temperatureReferenceK;
        PlanetTileMesh.MeshData data = PlanetTileMesh.BuildLiquidShellDataFromVolume(
            _tectonics, _radius, _elevationWorldScale, _displayVolume, minVolumeToRender, _liquid, tempK);

        bool hasAnyLiquid = data.Vertices != null && data.Vertices.Length > 0;
        if (!_liquidGo.activeSelf && hasAnyLiquid) _liquidGo.SetActive(true);
        if (_liquidGo.activeSelf && !hasAnyLiquid) _liquidGo.SetActive(false);
        if (!hasAnyLiquid) return;

        // Since FluidDynamicsManager now owns and rebuilds this mesh's topology every tick
        // (vertex count changes as liquid volume changes, unlike TidalForceManager's fixed-
        // topology assumption), tidal bulge is folded in HERE as an additive per-vertex
        // radius delta rather than left to TidalForceManager's own separate rebuild, which
        // would otherwise stomp on a stale vertex array of the wrong length.
        if (_tidal != null)
        {
            for (int i = 0; i < data.Vertices.Length; i++)
            {
                Vector3 dir = data.Vertices[i].normalized;
                // Clamp so a large tidalStrengthScale or close heavy moon can't push
                // individual vertices into tall spikes. 0.5 wu ≈ 2.5% of planetRadius.
                float tideDelta = Mathf.Clamp(_tidal.TidalHeightAt(dir), -0.5f, 0.5f);
                data.Vertices[i] = dir * (data.Vertices[i].magnitude + tideDelta);
            }
        }

        _liquidMesh.Clear();
        _liquidMesh.indexFormat = data.Vertices.Length > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        _liquidMesh.SetVertices(data.Vertices);
        _liquidMesh.SetColors(data.Colors);
        _liquidMesh.SetTriangles(data.Triangles, 0);
        _liquidMesh.RecalculateNormals();
        _liquidMesh.RecalculateBounds();
    }

    // --- Gameplay query API --------------------------------------------------

    /// Current liquid depth (dimensionless volume units, same scale as tectonic elevation) at
    /// a given mesh vertex index. 0 if dry, out of range, or no liquid simulation running.
    public float GetLiquidDepthAtVertex(int vertexIndex)
    {
        if (_liquidVolume == null || vertexIndex < 0 || vertexIndex >= _liquidVolume.Length) return 0f;
        return _liquidVolume[vertexIndex];
    }

    /// Liquid depth near an arbitrary world position, found via nearest unit-vertex lookup.
    /// O(n) linear scan - fine for occasional gameplay queries (e.g. one agent decision per
    /// frame) at ~15k vertices, but callers doing this every agent every frame should cache
    /// or batch; not optimized with a spatial index since no caller currently needs that.
    public float GetLiquidDepthNearPosition(Vector3 worldPos)
    {
        if (_tectonics == null || _liquidVolume == null) return 0f;
        Vector3 dir = (worldPos - _planetCenter).normalized;

        int best = -1;
        float bestDot = -2f;
        List<Vector3> unitVerts = _tectonics.UnitVerts;
        for (int v = 0; v < unitVerts.Count; v++)
        {
            float dot = Vector3.Dot(dir, unitVerts[v]);
            if (dot > bestDot) { bestDot = dot; best = v; }
        }
        return best >= 0 ? _liquidVolume[best] : 0f;
    }
}
