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
///   2. Evaporation/precipitation: driven by Clausius-Clapeyron vapor pressure rather than
///      abstract rates. VaporPressureAt(T) / P_surface gives the thermodynamic evaporation
///      multiplier; near zero for cold stable liquids, >1 when boiling. Per-vertex temperature
///      is derived from PlanetTemperature.CurrentK ± latitudinalDeltaK * SolarExposure so
///      polar/night-side vertices can freeze while equatorial ones stay liquid.
///   3. Condensation: gases whose partial pressure exceeds their vapor pressure at the current
///      temperature condense back into liquid (dew-point calculation, Clausius-Clapeyron
///      inverted). Modifies AtmosphereManager gas fractions directly.
///   4. Melting/Freezing: minerals with a MeltingPointK drain richness → liquid volume when
///      T > MeltingPointK, and rebuild richness from liquid when T drops back below it.
///   5. Mesh rebuild: PlanetTileMesh.BuildLiquidShellDataFromVolume re-derives the translucent
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

    [Header("Evaporation (Clausius-Clapeyron)")]
    [Tooltip("Absolute evaporation rate (volume/sec) at the thermodynamic boiling point, i.e. when P_vap == P_surface. Scales up when boiling, toward zero when cold.")]
    public float baseEvaporationRate = 0.0008f;
    [Tooltip("Extra evaporation rate (volume/sec) added at full day-side solar exposure, scaled by the same vapor-pressure multiplier so it also vanishes at low temperatures.")]
    public float solarEvaporationRate = 0.0025f;
    [Tooltip("Maximum multiplier applied to baseEvaporationRate when boiling (P_vap >> P_surface). Caps runaway evaporation on very hot worlds.")]
    public float boilMultiplier = 8f;

    [Header("Precipitation")]
    [Tooltip("Fraction of the global moisture reservoir that precipitates back down per second.")]
    public float precipitationRate = 0.02f;
    [Tooltip("How strongly precipitation favors already-wet (or wet-adjacent) vertices over arbitrary dry ones. 0 = uniform random, 1 = fully wet-biased.")]
    [Range(0f, 1f)] public float wetBias = 0.85f;
    [Tooltip("Number of candidate vertices sampled per precipitation tick before weighting by wetBias.")]
    public int precipitationCandidatesPerTick = 24;

    [Header("Condensation (gas → liquid)")]
    [Tooltip("Fraction of a gas's atmospheric fraction removed per Kelvin below its dew point per second.")]
    public float condensationRatePerK = 0.0005f;
    [Tooltip("How much liquid volume one unit of condensed gas fraction produces. Tune so rain events are visually significant but don't flood the planet instantly.")]
    public float condensationToLiquidScale = 0.05f;

    [Header("Melting / Freezing")]
    [Tooltip("Fraction of mineral richness converted to liquid per Kelvin above the mineral's melting point per second.")]
    public float meltRatePerK = 0.001f;
    [Tooltip("How much liquid volume one unit of melted mineral richness produces.")]
    public float meltToLiquidVolumeScale = 0.3f;
    [Tooltip("Fraction of excess liquid volume refrozen back into mineral richness per Kelvin below the melting point per second.")]
    public float freezeRatePerK = 0.0008f;

    [Header("Spatial temperature variation")]
    [Tooltip("Half the pole-to-equator temperature difference in Kelvin. At 40K (Earth-like), equatorial vertices are +40K above mean and polar vertices are -40K. Drives ice caps and equatorial boiling zones.")]
    public float latitudinalDeltaK = 40f;

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
    public LiquidDef CurrentLiquid => _liquid;
    private System.Func<float> _getLiquidTempK;
    private DayNightCycle _dayNight;
    private Vector3 _planetCenter;
    private TidalForceManager _tidal;
    private MineralDepositLayer _mineralLayer;
    private IReadOnlyList<GasDefinition> _gases;

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

    // Three fixed great-circle directions for ocean wave superposition (set once in Init).
    private Vector3[] _waveDirections;
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

        // Share the live volume array with ClimateManager so ocean-proximity moisture
        // queries always reflect the current fluid state without a per-frame copy.
        ClimateManager.SetLiquidVolume(_liquidVolume);

        _prevVolume = new float[n];
        _targetVolume = (float[])_liquidVolume.Clone();
        _displayVolume = (float[])_liquidVolume.Clone();
        _lerpT = 1f;
        _lerpDuration = flowTickInterval;

        // Derive flow parameters from liquid's physical identity.
        // Surface tension (mN/m) scales flowMaxSlope: high tension (water 72) holds steeper
        // slopes before flowing; low tension (methane 14) spreads almost flat.
        // Range 14-72 → flowMaxSlope 0.003-0.05 (linear).
        if (liquid != null)
        {
            flowMaxSlope = Mathf.Lerp(0.003f, 0.05f,
                Mathf.InverseLerp(14f, 72f, liquid.SurfaceTensionMNm));

            // Viscosity (mPa·s) scales flowTransferRate on a log scale so the enormous
            // molten-sulfur outlier (1500) doesn't collapse the water/ammonia/methane range.
            // Higher viscosity → lower transfer rate (slower redistribution per tick).
            // Water=0.5, Ammonia≈0.54, Methane≈0.58, MoltenSulfur≈0.04.
            float logVisc = Mathf.Log(Mathf.Max(liquid.ViscosityMPas, 0.01f));
            float viscT = Mathf.InverseLerp(Mathf.Log(0.18f), Mathf.Log(1500f), logVisc);
            flowTransferRate = Mathf.Lerp(0.58f, 0.04f, viscT);
        }

        _flowTickTimer = 0f;
        _moistureReservoir = 0f;

        // Evenly-spread wave directions: three points on the unit sphere separated by
        // ~120° to avoid axis-aligned grid artifacts in the wave pattern.
        _waveDirections = new[]
        {
            new Vector3(1f,       0f,       0f).normalized,
            new Vector3(-0.5f,    0.866f,   0f).normalized,
            new Vector3(-0.5f,   -0.433f,   0.75f).normalized,
        };
        enabled = liquid != null; // nothing to simulate on a dry world
    }

    /// Called after Init() once the mineral deposit layer is available from world-gen.
    /// Provides the melt/freeze simulation with per-vertex deposit data.
    public void SetMineralLayer(MineralDepositLayer layer) => _mineralLayer = layer;

    /// Called after Init() to provide the live gas list from AtmosphereManager.
    /// FluidDynamicsManager holds the same list reference, so condensation fraction
    /// changes are visible to AtmosphereManager's next Update() normalization pass.
    public void SetGases(IReadOnlyList<GasDefinition> gases) => _gases = gases;

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
            StepCondensation(tickDt);
            StepMeltingAndFreezing(tickDt);
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

    /// Per-vertex effective temperature: global mean ± latitudinal gradient driven by
    /// solar exposure. Equatorial day-side vertices run hotter than mean; polar/night
    /// vertices run colder. This drives spatially-varying phase transitions (ice caps,
    /// equatorial boiling zones) without a full per-vertex climate simulation.
    private float VertexTempK(int v)
    {
        float meanK = PlanetTemperature.Instance != null ? PlanetTemperature.Instance.CurrentK : 280f;
        if (_dayNight == null) return meanK;
        float solar = _dayNight.SolarExposure(_tectonics.UnitVerts[v]); // 0=night/pole, 1=day/equator
        return meanK + (solar - 0.5f) * 2f * latitudinalDeltaK;
    }

    /// Clausius-Clapeyron vapor pressure evaporation. Replaces the former abstract
    /// baseEvaporationRate + linear-temperature-coefficient model. The vapor-pressure
    /// ratio P_vap(T)/P_surface drives the evaporation multiplier: near zero when the
    /// liquid is cold and stable, 1x at the boiling point, up to boilMultiplier when
    /// the temperature exceeds the boiling point (active boiling).
    private void StepEvaporationAndPrecipitation(float dt)
    {
        int n = _liquidVolume.Length;
        float pressureBar = AtmosphereManager.Instance != null ? AtmosphereManager.Instance.PressureBar : 1f;

        for (int v = 0; v < n; v++)
        {
            if (_liquidVolume[v] <= 0f) continue;

            float tempK    = VertexTempK(v);
            float pVap     = _liquid.VaporPressureAt(tempK);
            float vaporRatio = pressureBar > 0f ? pVap / pressureBar : 1f;
            // At ratio=0 (very cold): no evaporation.
            // At ratio=1 (boiling point): baseEvaporationRate applies.
            // At ratio>>1 (boiling): rate caps at boilMultiplier * baseEvaporationRate.
            float thermalRate = baseEvaporationRate * Mathf.Clamp(vaporRatio, 0f, boilMultiplier);

            float solar = 0.5f;
            if (_dayNight != null)
                solar = _dayNight.SolarExposure(_tectonics.UnitVerts[v]);

            // Solar contribution additive, but also scaled by vaporRatio so it vanishes
            // at very low temperatures (no point evaporating a frozen surface in sunlight).
            float rate = thermalRate + solar * solarEvaporationRate * Mathf.Clamp01(vaporRatio * 2f);

            float evaporated = Mathf.Min(_liquidVolume[v], rate * dt);
            _liquidVolume[v]  -= evaporated;
            _moistureReservoir += evaporated;
        }

        float toPrecipitate = Mathf.Min(_moistureReservoir, _moistureReservoir * precipitationRate * dt * 60f);
        // *60f compensates for precipitationRate being expressed as "fraction per second" while
        // dt here is a multi-second flow-tick interval, not a per-frame delta.
        if (toPrecipitate <= 0f) return;
        _moistureReservoir -= toPrecipitate;

        DistributePrecipitation(toPrecipitate);
    }

    /// Dew-point condensation: for each gas that can condense into this world's liquid,
    /// compute the dew-point temperature from the gas's partial pressure using the
    /// Clausius-Clapeyron equation. If CurrentK < T_dew, a fraction of the gas condenses
    /// and is added to the moisture reservoir (which DistributePrecipitation delivers back
    /// to the surface). Modifies GasDefinition.Fraction directly; AtmosphereManager's own
    /// Update() will normalize fractions on the next frame.
    private void StepCondensation(float dt)
    {
        if (_gases == null || AtmosphereManager.Instance == null) return;

        float tempK      = PlanetTemperature.Instance != null ? PlanetTemperature.Instance.CurrentK : 280f;
        float pressureBar = AtmosphereManager.Instance.PressureBar;

        foreach (var gas in _gases)
        {
            if (gas.CondensesTo == null)                          continue;
            if (gas.CondensesTo.Value != _liquid.Kind)            continue; // only condense into this world's liquid
            if (gas.Fraction          <= 0f)                      continue;
            if (gas.CondensationBoilingK <= 0f)                   continue;

            float partialPressure = gas.Fraction * pressureBar;
            if (partialPressure <= 0f) continue;

            // Clausius-Clapeyron inverted: T_dew = 1 / (1/T_boil − ln(P_partial) / (ΔHvap/R))
            // Where P_partial is in bar and P_ref = 1 bar.
            float logPartial = Mathf.Log(partialPressure);
            float invTDew    = 1f / gas.CondensationBoilingK - logPartial / gas.CondensationDHvapOverR;
            if (invTDew <= 0f) continue; // numerical guard (should never happen in sim range)
            float tDew = 1f / invTDew;

            if (tempK >= tDew) continue; // above dew point — no condensation

            float dewDeficit       = tDew - tempK; // Kelvin below dew point
            float condensedFraction = Mathf.Min(gas.Fraction, condensationRatePerK * dewDeficit * dt);
            gas.Fraction = Mathf.Max(0f, gas.Fraction - condensedFraction);

            _moistureReservoir += condensedFraction * condensationToLiquidScale;
        }
    }

    /// Mineral melting and freezing. For each vertex with a meltable mineral deposit whose
    /// MeltsToLiquid matches this world's liquid kind:
    ///  - If vertex temperature > MeltingPointK: drain RuntimeRichness → liquid volume.
    ///  - If vertex temperature < MeltingPointK and liquid is present: refreeze liquid → richness.
    /// Sublimation (solid → gas) is handled here too for minerals that have SublimationPointK set.
    private void StepMeltingAndFreezing(float dt)
    {
        if (_mineralLayer == null || _liquid == null) return;

        int n = Mathf.Min(_mineralLayer.ByVertex.Length, _liquidVolume.Length);

        for (int v = 0; v < n; v++)
        {
            VertexDeposit deposit = _mineralLayer.ByVertex[v];
            if (deposit == null || deposit.Mineral == null) continue;

            MineralDef mineral = deposit.Mineral;

            // --- Sublimation (solid → gas, skips liquid phase) ---
            if (mineral.SublimationPointK.HasValue && mineral.SublimatesTo != null &&
                AtmosphereManager.Instance != null)
            {
                float subT = VertexTempK(v);
                if (subT > mineral.SublimationPointK.Value && _mineralLayer.RuntimeRichness[v] > 0f)
                {
                    const float sublimationRate = 0.0003f;
                    float sublimed = Mathf.Min(_mineralLayer.RuntimeRichness[v], sublimationRate * dt);
                    _mineralLayer.RuntimeRichness[v] -= sublimed;
                    const float sublimationGasScale = 0.002f;
                    AtmosphereManager.Instance.AddGasFraction(mineral.SublimatesTo, sublimed * sublimationGasScale);
                }
            }

            // --- Melting and freezing ---
            if (!mineral.MeltingPointK.HasValue) continue;
            if (mineral.MeltsToLiquid != _liquid.Kind) continue; // mineral melts into a different liquid than this world has

            float meltK   = mineral.MeltingPointK.Value;
            float richness = _mineralLayer.RuntimeRichness[v];
            float vertexT = VertexTempK(v);

            if (vertexT > meltK && richness > 0f)
            {
                float melted = Mathf.Min(richness, meltRatePerK * (vertexT - meltK) * dt);
                _mineralLayer.RuntimeRichness[v] -= melted;
                _liquidVolume[v] += melted * meltToLiquidVolumeScale;
            }
            else if (vertexT < meltK && _liquidVolume[v] > 0f && richness < deposit.Richness)
            {
                float frozen = Mathf.Min(_liquidVolume[v], freezeRatePerK * (meltK - vertexT) * dt);
                _liquidVolume[v] -= frozen;
                _mineralLayer.RuntimeRichness[v] = Mathf.Min(deposit.Richness,
                    richness + frozen / Mathf.Max(meltToLiquidVolumeScale, 0.0001f));
            }
        }
    }

    /// Sample candidate vertices weighted toward wet/wet-adjacent ones and distribute
    /// precipitation across ALL candidates proportionally rather than winner-take-all.
    /// This prevents the entire tick's evaporation budget from crashing onto one vertex
    /// (which StepFlow can't smooth fast enough, causing geometry spikes at extreme temps).
    private void DistributePrecipitation(float amount)
    {
        int n = _liquidVolume.Length;
        if (n == 0) return;

        // Collect scored candidates.
        var candidates = new (int vertex, float score)[precipitationCandidatesPerTick];
        float totalScore = 0f;
        for (int i = 0; i < precipitationCandidatesPerTick; i++)
        {
            int candidate = Random.Range(0, n);
            float wetness = _liquidVolume[candidate] > 0f ? 1f : NeighborWetness(candidate);
            float score = Mathf.Max(0.01f, Mathf.Lerp(Random.value, wetness + Random.value * 0.01f, wetBias));
            candidates[i] = (candidate, score);
            totalScore += score;
        }

        if (totalScore <= 0f) return;

        // Spread rain proportionally across all candidates — no vertex gets more than
        // its score-weighted share, so no single basin accumulates the whole budget.
        for (int i = 0; i < precipitationCandidatesPerTick; i++)
            _liquidVolume[candidates[i].vertex] += amount * (candidates[i].score / totalScore);
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

        float tempK = _getLiquidTempK != null ? _getLiquidTempK() : 280f;
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
                // individual vertices into tall spikes. 0.08 wu ≈ 0.4% of planetRadius —
                // still a visible tide, not a geometry explosion.
                float tideDelta = Mathf.Clamp(_tidal.TidalHeightAt(dir), -0.08f, 0.08f);
                data.Vertices[i] = dir * (data.Vertices[i].magnitude + tideDelta);
            }
        }

        // Wave animation: superposition of three sine waves in fixed world-space great-
        // circle directions gives a natural rolling ocean swell visible from orbit.
        // Amplitudes are small (≤0.05 wu = 0.25% of planet radius) so they don't
        // conflict with tidal bulge or produce spikes near coastlines.
        {
            float t = Time.time;
            var wd = _waveDirections;
            for (int i = 0; i < data.Vertices.Length; i++)
            {
                Vector3 dir = data.Vertices[i].normalized;
                float w = Mathf.Sin(Vector3.Dot(dir, wd[0]) * 11f + t * 0.55f) * 0.045f
                        + Mathf.Sin(Vector3.Dot(dir, wd[1]) * 15f + t * 0.38f) * 0.028f
                        + Mathf.Sin(Vector3.Dot(dir, wd[2]) * 8f  + t * 0.70f) * 0.035f;
                data.Vertices[i] = dir * (data.Vertices[i].magnitude + w);
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

    /// Returns a copy of the current per-vertex liquid volume array. Used by
    /// ChemicalNutrientPool.Init to seed nutrient density near submerged vertices.
    public float[] GetLiquidVolumeSnapshot()
    {
        if (_liquidVolume == null) return null;
        float[] copy = new float[_liquidVolume.Length];
        System.Array.Copy(_liquidVolume, copy, _liquidVolume.Length);
        return copy;
    }

    /// Fraction of surface vertices that have liquid above minVolumeToRender (0–1).
    public float GetLiquidCoverageFraction()
    {
        if (_liquidVolume == null || _liquidVolume.Length == 0) return 0f;
        int wet = 0;
        for (int i = 0; i < _liquidVolume.Length; i++)
            if (_liquidVolume[i] >= minVolumeToRender) wet++;
        return (float)wet / _liquidVolume.Length;
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

    /// True when the nearest surface vertex has renderable liquid (depth ≥ minVolumeToRender).
    public bool IsSubmerged(Vector3 worldPos)
        => GetLiquidDepthNearPosition(worldPos) >= minVolumeToRender;

    /// World-space tangent vector of the liquid current at worldPos, derived from the local
    /// height-gradient (liquid flows from high combined-height toward low). Returns zero if
    /// the position is dry or if the fluid is in equilibrium at that vertex.
    /// Magnitude is scaled by the liquid's FlowSpeedFactor so high-viscosity liquids produce
    /// weaker currents (molten sulfur ≈ 2% of water current speed).
    public Vector3 GetLiquidCurrentAt(Vector3 worldPos)
    {
        if (_tectonics == null || _liquidVolume == null || _adjacency == null || _liquid == null)
            return Vector3.zero;

        Vector3 dir = (worldPos - _planetCenter).normalized;

        // Find nearest vertex.
        int best = -1;
        float bestDot = -2f;
        var verts = _tectonics.UnitVerts;
        for (int v = 0; v < verts.Count; v++)
        {
            float dot = Vector3.Dot(dir, verts[v]);
            if (dot > bestDot) { bestDot = dot; best = v; }
        }
        if (best < 0 || _liquidVolume[best] < minVolumeToRender) return Vector3.zero;

        // Gradient-descent direction: weighted sum of downhill neighbor offsets.
        float heightV = _tectonics.Elevation[best] + _liquidVolume[best];
        Vector3 flowSum = Vector3.zero;
        foreach (int nb in _adjacency[best])
        {
            float heightNb = _tectonics.Elevation[nb] + _liquidVolume[nb];
            float diff = heightV - heightNb;
            if (diff > flowMaxSlope)
            {
                Vector3 toNb = (verts[nb] - verts[best]);
                flowSum += toNb.normalized * diff;
            }
        }

        if (flowSum.sqrMagnitude < 0.0001f) return Vector3.zero;

        // Project onto tangent plane and scale by liquid identity.
        Vector3 tangent = (flowSum - Vector3.Dot(flowSum, dir) * dir).normalized;
        return tangent * _liquid.FlowSpeedFactor;
    }
}
