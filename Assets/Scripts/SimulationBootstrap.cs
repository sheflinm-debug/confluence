using UnityEngine;

/// Tier 2.5 entry point: rolls a rock archetype + atmosphere type (atmosphere_generator_
/// spec.docx), generates real plate tectonics (spherical_terrain_generation_spec.md),
/// pools a chemistry-appropriate liquid into the lowest terrain, then spawns the
/// founding colony. Attach to an empty GameObject in the scene; no manual scene setup
/// beyond this needed.
public class SimulationBootstrap : MonoBehaviour
{
    [Header("Planet")]
    public float planetRadius = 20f;
    public int planetMeshSubdivisions = 4; // higher = more, smaller terrain faces

    [Header("Climate (biome/comfort layer - independent of rendered rock/terrain color)")]
    public float climateNoiseScale = 0.15f; // smaller = larger biome patches

    [Header("Weather (wind field + storm events layered on top of the static climate)")]
    public bool enableWeather = true;
    [Tooltip("Number of prevailing-wind latitude bands per hemisphere (Hadley/Ferrel/Polar-like).")]
    public int windCellsPerHemisphere = 3;
    public float avgStormIntervalSeconds = 10f;
    public int maxActiveStorms = 6;

    [Header("Tectonics (visual terrain only - planetRadius stays a true sphere for movement/collision)")]
    public int plateCount = 10;
    [Tooltip("Graph-distance (in mesh hops) over which a boundary's elevation effect falls off.")]
    public float boundaryInfluence = 10f;
    public float noiseAmplitude = 0.25f;
    public int volcanoCount = 6;
    public float volcanoAmplitude = 0.5f;
    public int erosionIterations = 6;
    public float erosionMaxSlope = 0.15f;
    public float erosionTransferRate = 0.5f;
    [Tooltip("World-unit scale applied to the dimensionless tectonic elevation field.")]
    public float elevationWorldScale = 1.8f;

    [Header("Liquid (pools into the lowest terrain up to this elevation percentile)")]
    public bool enableLiquid = true;
    [Range(0f, 1f)] public float seaLevelPercentile = 0.35f;

    [Header("Fluid Dynamics (live flow/evaporation/precipitation simulation after genesis)")]
    [Tooltip("If false, the liquid shell stays exactly as the genesis flood-fill left it (old static behavior).")]
    public bool enableFluidDynamics = true;
    [Tooltip("Seconds between flow-diffusion ticks.")]
    public float fluidFlowTickInterval = 0.75f;
    [Tooltip("Liquid-volume difference between neighbors that flow tolerates without smoothing (a stable 'pool' tolerance).")]
    public float fluidFlowMaxSlope = 0.02f;
    [Tooltip("Fraction of over-threshold volume difference transferred per tick (degree-normalized for stability).")]
    public float fluidFlowTransferRate = 0.5f;
    [Tooltip("Base evaporation rate (volume/sec) from every wet vertex.")]
    public float fluidBaseEvaporationRate = 0.0008f;
    [Tooltip("Extra evaporation rate (volume/sec) at full day-side solar exposure.")]
    public float fluidSolarEvaporationRate = 0.0025f;
    [Tooltip("Reference temperature (K) above which extra evaporation kicks in.")]
    public float fluidTemperatureReferenceK = 280f;
    [Tooltip("Evaporation rate added per Kelvin above fluidTemperatureReferenceK.")]
    public float fluidEvaporationPerDegreeK = 0.00004f;
    [Tooltip("Fraction of the global moisture reservoir that precipitates back down per second.")]
    public float fluidPrecipitationRate = 0.02f;
    [Range(0f, 1f)] [Tooltip("How strongly precipitation favors already-wet (or wet-adjacent) vertices over arbitrary dry ones.")]
    public float fluidWetBias = 0.85f;
    [Tooltip("Seconds between liquid shell mesh rebuilds.")]
    public float fluidMeshRebuildInterval = 1f;
    [Tooltip("Minimum per-vertex liquid volume to be rendered as part of the shell mesh.")]
    public float fluidMinVolumeToRender = 0.01f;

    [Header("Tidal Forces (visual liquid-shell bulge only - planetRadius never changes)")]
    public bool enableTides = true;
    [Tooltip("Overall tidal bulge height in world units. Kept small - this is atmospheric flavor, not a flood mechanic.")]
    public float tidalStrengthScale = 0.35f;
    [Tooltip("How often the liquid shell mesh is rebuilt with the new tidal offsets.")]
    public float tidalRebuildIntervalSeconds = 0.25f;
    [Tooltip("Solar tide strength relative to a notional close moon (no moon system exists yet in this codebase, so the star is currently the only tidal source - see TidalForceManager for the generalization path).")]
    public float starTidalRelativeStrength = 0.45f;

    [Header("Origin organism (single founder - lineages branch off it via speciation, not pre-seeded)")]
    public int communityCount = 1;
    public int minMembersPerCommunity = 1;
    public int maxMembersPerCommunity = 1;
    public float preferenceVariance = 8f; // spread of temp/moisture preference within the colony

    [Header("Starting trait distribution (Section 6a: 0-100, 50 = world-start average)")]
    public float visionMean = 50f;
    public float visionStdDev = 15f;
    public float speedMean = 50f;
    public float speedStdDev = 15f;
    public float strengthMean = 50f;
    public float strengthStdDev = 15f;
    public float hardinessMean = 50f;
    public float hardinessStdDev = 15f;

    [Header("Corpses (the only food source - decaying remains of the dead)")]
    public float corpseDecayTime = 12f;

    [Header("Prefabs (optional - primitives used if left empty)")]
    public GameObject agentPrefab;
    public GameObject corpsePrefab;

    private Vector3 _center;

    void Start()
    {
        _center = transform.position;
        PopulationStats.Reset();

        // Starts ticking immediately (Big Bang, index 0) and keeps running through the
        // cinematic and on into Era 1's live-simulation stasis stretches - see EraTimeline.
        DeepTimeClock deepTimeClock = gameObject.AddComponent<DeepTimeClock>();
        deepTimeClock.StartFrom(0);

        // Single-celled organisms should read as tiny against the planet/atmosphere,
        // not capsule-sized giants - 0.6 was originally tuned for a multicellular-scale
        // creature and dwarfed the (too-thin) atmosphere shell; both are fixed together.
        if (corpsePrefab == null) corpsePrefab = BuildPrimitivePrefab(PrimitiveType.Cube, 0.2f, new Color(0.35f, 0.25f, 0.2f));
        // Scale starts at Abiogenesis microbe size (0.05); EraManager grows it over eras.
        if (agentPrefab == null) agentPrefab = BuildPrimitivePrefab(PrimitiveType.Capsule, 0.05f, Color.white);

        ClimateManager.Randomize(_center, planetRadius, climateNoiseScale);

        // Wind/weather is independent of the static climate noise field - it's a
        // dynamic perturbation layered on top, queryable the same way ClimateManager
        // is. Uses world-space up as the nominal rotation axis (no separate planet-
        // spin transform exists in this codebase to read an axis from).
        if (enableWeather)
        {
            WindManager.Randomize(_center, planetRadius, Vector3.up, windCellsPerHemisphere);
            WeatherManager weather = gameObject.AddComponent<WeatherManager>();
            weather.avgSpawnIntervalSeconds = avgStormIntervalSeconds;
            weather.maxActiveStorms = maxActiveStorms;
            weather.Init(_center, planetRadius);
        }

        CorpseSpawner corpseSpawner = gameObject.AddComponent<CorpseSpawner>();
        corpseSpawner.corpsePrefab = corpsePrefab;
        corpseSpawner.decayTime = corpseDecayTime;
        corpseSpawner.parent = transform;

        AgentSpawner agentSpawner = gameObject.AddComponent<AgentSpawner>();
        agentSpawner.agentPrefab = agentPrefab;
        agentSpawner.corpseSpawner = corpseSpawner;
        agentSpawner.parent = transform;
        agentSpawner.planetCenter = _center;
        agentSpawner.planetRadius = planetRadius;

        GeneCatalog.BuildDefault();
        gameObject.AddComponent<GeneEvolutionManager>();

        DayNightCycle dayNight = gameObject.AddComponent<DayNightCycle>();

        // Generation order matters: rock archetype first (it reweights the atmosphere
        // roll), then atmosphere (its type sets the temperature band), then temperature,
        // then liquid (needs both atmosphere chemistry and the rolled temperature),
        // then tectonics/mesh (liquid pools into the terrain), then the colony (which
        // snapshots the now-real atmosphere as its genesis "ideal mix").
        RockArchetypeDef archetype = RockArchetypeTable.Roll();

        AtmosphereManager atmosphere = gameObject.AddComponent<AtmosphereManager>();
        atmosphere.Init(agentSpawner, dayNight, archetype);

        LiquidDef liquidCandidate = enableLiquid ? LiquidChemistry.GetCandidate(atmosphere.RolledType) : null;

        PlanetTemperature temperature = gameObject.AddComponent<PlanetTemperature>();
        temperature.Init(atmosphere.RolledType, liquidCandidate);

        LiquidDef liquid = liquidCandidate != null ? LiquidChemistry.Determine(atmosphere.RolledType, temperature.CurrentK) : null;

        SolarSystemDef solarSystem = StarSystemGenerator.Generate(temperature.CurrentK);

        // Eccentricity/axial tilt are rolled now (alongside the rest of world gen) but
        // OrbitalSeasons doesn't start ticking the seasonal cycle until BeginGameplay()
        // is called from the cinematic's onComplete below - same handoff point
        // DeepTimeClock uses, so the genesis cinematic never sees a moving season.
        OrbitalSeasons orbitalSeasons = gameObject.AddComponent<OrbitalSeasons>();
        orbitalSeasons.Init(solarSystem.LifePlanetOrbitAU, solarSystem.LifePlanetEccentricity, solarSystem.LifePlanetAxialTiltDeg);

        TectonicResult tectonics = TectonicPlanetGenerator.Generate(
            planetMeshSubdivisions, plateCount, boundaryInfluence,
            noiseAmplitude, volcanoCount, volcanoAmplitude,
            erosionIterations, erosionMaxSlope, erosionTransferRate);

        float seaLevel = liquid != null ? PlanetTileMesh.ComputeSeaLevel(tectonics, seaLevelPercentile) : float.NegativeInfinity;

        Debug.Log($"[World] Star: {solarSystem.Star.SpectralClass}-class @ {solarSystem.LifePlanetOrbitAU:F2} AU " +
            $"(e={solarSystem.LifePlanetEccentricity:F2}, tilt={solarSystem.LifePlanetAxialTiltDeg:F0} deg) | " +
            $"HZ: {solarSystem.HabitableZoneInnerAU:F2}-{solarSystem.HabitableZoneOuterAU:F2} AU ({(solarSystem.LifePlanetInHabitableZone ? "in zone" : "out of zone")}) | " +
            $"Life moons: {solarSystem.LifePlanetMoons.Count} | " +
            $"Rock archetype: {archetype.Id} | Liquid: {(liquid != null ? liquid.Name : "none")} | Temp: {temperature.CurrentK:F0}K");

        // All world data is generated synchronously above (cheap, deterministic). The
        // cinematic only ANIMATES THE REVEAL of that already-computed data - it is not
        // a live re-simulation - then hands off to the normal Tier 2 colony spawn.
        GameObject cinematicGo = new GameObject("GenesisCinematic");
        GenesisCinematic cinematic = cinematicGo.AddComponent<GenesisCinematic>();
        cinematic.Run(_center, planetRadius, tectonics, archetype, liquid, seaLevel, temperature.CurrentK, elevationWorldScale,
            solarSystem, transform, () =>
            {
                // Live gameplay begins here - same handoff point DeepTimeClock keeps
                // running through, so the seasonal cycle starts ticking now too.
                orbitalSeasons.BeginGameplay();

                // Place background objects at proper deep-space distances so the star
                // reads as a distant sun and other planets as tiny bright specks —
                // not the cinematic's compressed solar-system scale.
                if (cinematic.StarGo != null)
                {
                    Vector3 starDir = (cinematic.StarGo.transform.position - _center).normalized;
                    // Star: 450 units away, diameter scales with rolled luminosity so
                    // a bright O-star fills more of the sky than a dim M-dwarf.
                    float starDiameter = Mathf.Max(GenesisCinematic.StarSizeFromLuminosity(solarSystem.Star.LuminositySolar) * 6f, 50f);
                    cinematic.StarGo.transform.localScale = Vector3.one * starDiameter;
                    cinematic.StarGo.transform.position = _center + starDir * 450f;
                }
                foreach (var pg in cinematic.OtherPlanetGos)
                {
                    if (pg == null) continue;
                    Vector3 pDir = (pg.transform.position - _center).normalized;
                    float pDist = Vector3.Distance(pg.transform.position, _center);
                    // Other planets: 2–3 unit dots placed 150–350 units away so they're
                    // visible as distant specks but never compete with the life-planet.
                    pg.transform.localScale = Vector3.one * 2.5f;
                    pg.transform.position = _center + pDir * Mathf.Clamp(pDist * 5f, 120f, 350f);
                }

                // Real orbital motion (decorative other-planets + every rolled moon) and
                // the life-planet's own axial spin both start now too - same handoff
                // point as everything else here, so the cinematic itself never shows
                // anything moving that live gameplay hasn't actually started yet.
                SolarSystemRuntime solarRuntime = gameObject.AddComponent<SolarSystemRuntime>();
                solarRuntime.planetRotationPeriodSeconds = dayNight.dayLengthSeconds;
                solarRuntime.Init(solarSystem, cinematic);

                // Tidal bulging starts once the liquid shell is fully revealed and gameplay
                // is live - no point animating tides during the cinematic's own liquid
                // fade-in. SolarSystemDef.LifePlanetMoons now exists, so TidalForceManager
                // gets real per-moon sources added via AddMoonTidalSources() below,
                // in addition to the star-only solar tide it always had.
                TidalForceManager tidal = null;
                if (enableTides && cinematic.HasLiquid)
                {
                    tidal = gameObject.AddComponent<TidalForceManager>();
                    tidal.tidalStrengthScale = tidalStrengthScale;
                    tidal.rebuildIntervalSeconds = tidalRebuildIntervalSeconds;
                    tidal.starTidalRelativeStrength = starTidalRelativeStrength;
                    tidal.Init(_center, cinematic.LiquidMesh, cinematic.LiquidShellData,
                        planetRadius, cinematic.SeaLevel, cinematic.ElevationWorldScale, dayNight, solarSystem);
                    // Now that moons are live (SolarSystemRuntime.Init ran just above),
                    // add a tidal source per life-planet moon - tidal forces accumulate
                    // from every massive body, and a close moon typically dominates the star.
                    tidal.AddMoonTidalSources(solarRuntime);
                }

                // Live fluid simulation takes over from the genesis cinematic's one-time
                // static flood-fill: per-vertex liquid volume now flows via mesh adjacency,
                // evaporates/precipitates over time, and rebuilds the same liquid shell
                // GameObject/mesh GenesisCinematic created (no duplicate visual object).
                if (enableFluidDynamics && cinematic.HasLiquid && liquid != null)
                {
                    if (tidal != null) tidal.OwnedByFluidDynamics = true;

                    GameObject fluidGo = new GameObject("FluidDynamicsManager");
                    fluidGo.transform.SetParent(transform);
                    FluidDynamicsManager fluid = fluidGo.AddComponent<FluidDynamicsManager>();
                    fluid.flowTickInterval = fluidFlowTickInterval;
                    fluid.flowMaxSlope = fluidFlowMaxSlope;
                    fluid.flowTransferRate = fluidFlowTransferRate;
                    fluid.baseEvaporationRate = fluidBaseEvaporationRate;
                    fluid.solarEvaporationRate = fluidSolarEvaporationRate;
                    fluid.temperatureReferenceK = fluidTemperatureReferenceK;
                    fluid.evaporationPerDegreeK = fluidEvaporationPerDegreeK;
                    fluid.precipitationRate = fluidPrecipitationRate;
                    fluid.wetBias = fluidWetBias;
                    fluid.meshRebuildInterval = fluidMeshRebuildInterval;
                    fluid.minVolumeToRender = fluidMinVolumeToRender;

                    MeshFilter liquidFilter = cinematic.LiquidGo != null ? cinematic.LiquidGo.GetComponent<MeshFilter>() : null;
                    fluid.Init(tectonics, planetRadius, cinematic.ElevationWorldScale, cinematic.SeaLevel,
                        liquid, () => PlanetTemperature.Instance != null ? PlanetTemperature.Instance.CurrentK : temperature.CurrentK,
                        dayNight, _center, cinematic.LiquidGo, liquidFilter, cinematic.LiquidMesh, tidal);
                }

                GameObject atmosphereVisualGo = new GameObject("AtmosphereVisual");
                AtmosphereVisual atmosphereVisual = atmosphereVisualGo.AddComponent<AtmosphereVisual>();
                atmosphereVisual.Build(planetRadius, _center, atmosphere.RolledType, atmosphere.PressureBar, transform);

                if (enableWeather)
                {
                    GameObject stormVisualGo = new GameObject("StormVisualManager");
                    stormVisualGo.transform.SetParent(transform);
                    StormVisualManager stormVisual = stormVisualGo.AddComponent<StormVisualManager>();
                    stormVisual.Init(tectonics, _center, planetRadius, elevationWorldScale, transform);
                }

                // Place the founding organism literally inside a flooded tile, not
                // anywhere on the sphere - it must originate in the liquid.
                Vector3? wetOrigin = null;
                if (liquid != null)
                {
                    int floodedVertex = PlanetTileMesh.PickRandomFloodedVertex(tectonics, seaLevel);
                    if (floodedVertex >= 0)
                        wetOrigin = _center + tectonics.UnitVerts[floodedVertex] * planetRadius;
                }

                // Community 0 = player's founding organism, always placed in liquid if available.
                agentSpawner.SpawnCommunities(communityCount, minMembersPerCommunity, maxMembersPerCommunity,
                    visionMean, visionStdDev, speedMean, speedStdDev, strengthMean, strengthStdDev,
                    hardinessMean, hardinessStdDev, preferenceVariance, wetOrigin);

                // Communities 1–7 = NPC species, each a single founding cell at a random
                // surface position, with a distinct hue. They evolve autonomously through
                // the same gene-event and speciation systems as the player's lineage.
                agentSpawner.SpawnNPCCommunities(7,
                    visionMean, visionStdDev, speedMean, speedStdDev, strengthMean, strengthStdDev,
                    hardinessMean, hardinessStdDev, preferenceVariance);

                SpeciationManager speciationManager = gameObject.AddComponent<SpeciationManager>();
                speciationManager.Init(agentSpawner);

                EraManager eraManager = gameObject.AddComponent<EraManager>();
                eraManager.Init(agentSpawner);

                PopulationStatsOverlay overlay = gameObject.AddComponent<PopulationStatsOverlay>();
                overlay.agentSpawner = agentSpawner;

                SetupOrbitCamera();
            });
    }

    private GameObject BuildPrimitivePrefab(PrimitiveType type, float scale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.transform.localScale = Vector3.one * scale;
        go.SetActive(false);
        Renderer r = go.GetComponent<Renderer>();
        if (r != null)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            r.material = mat;
        }
        go.SetActive(true);
        return go;
    }

    private void SetupOrbitCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) cam = FindAnyObjectByType<Camera>();
        if (cam == null)
        {
            GameObject camGo = new GameObject("Main Camera");
            cam = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
        }

        OrbitCamera orbit = cam.GetComponent<OrbitCamera>();
        if (orbit == null) orbit = cam.gameObject.AddComponent<OrbitCamera>();
        orbit.target = transform;
        orbit.distance = planetRadius * 4.5f;

        // Feed the planet's spin rate so the L-key lock knows how fast to co-rotate.
        SolarSystemRuntime sr = GetComponent<SolarSystemRuntime>();
        if (sr != null && sr.planetRotationPeriodSeconds > 0f)
            orbit.planetRotationDegPerSec = 360f / sr.planetRotationPeriodSeconds;

        // Force an immediate reposition outside the sphere so the very first frame
        // isn't rendered from the camera's old (possibly inside-the-sphere) location.
        orbit.SnapToTarget();

        // Space background: kill Unity's default sky-blue skybox and replace with
        // solid black so the scene reads as outer space rather than a surface environment.
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        RenderSettings.skybox = null;
        // Very faint deep-blue ambient so the unlit side of the planet isn't pitch black
        // (stars still provide trace illumination in real space).
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.04f, 0.04f, 0.07f);
    }
}
