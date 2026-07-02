using UnityEngine;

/// Drives a subtle, periodic "tidal bulge" on the liquid shell mesh, purely as a visual
/// modulation layered on TOP of the already-pooled sea level - planetRadius and the
/// liquid shell's base radius (set by PlanetTileMesh.BuildLiquidShellData) never change;
/// this only nudges each liquid vertex's radius by a small sinusoidal delta each frame,
/// same separation-of-concerns pattern as terrain elevation vs. liquid pooling described
/// in PlanetTileMesh.cs's header comment.
///
/// MOON INTEGRATION: SolarSystemDef.LifePlanetMoons (see StarSystemGenerator.cs) now
/// exists with exactly the RelativeMass/OrbitDistance/OrbitalPeriod fields this class's
/// generalization path originally called for, and SolarSystemRuntime spawns/orbits the
/// actual moon GameObjects. Init() below adds one TidalSource per life-planet moon,
/// reading its live world position back from SolarSystemRuntime each frame (real tidal
/// force ~ mass / distance^3, so a close moon can swamp the much more distant star - this
/// uses RelativeMass directly and folds in 1/distance^3 the same way, rather than the
/// star's simpler "always overhead, period = day length" treatment, since a moon's
/// position genuinely moves independent of the day/night light).
///
/// If a fluid dynamics system replaces the static shell, swap RebuildShell()'s direct
/// mesh-vertex write for whatever per-vertex liquid level hook that system exposes -
/// TidalHeightAt() below is already factored out as a standalone function for exactly that
/// handoff (see FluidDynamicsManager, which already calls it this way).
public class TidalForceManager : MonoBehaviour
{
    private struct TidalSource
    {
        public string Name;
        public float RelativeStrength; // arbitrary units, NOT physically calibrated mass/d^3
        public float OrbitalPeriodSeconds; // bulge re-peaks on this cadence
        public System.Func<Vector3> GetDirection; // world-space unit vector FROM planet center TO source
    }

    [Header("Tidal Forces (visual liquid-shell modulation only - planetRadius never changes)")]
    [Tooltip("Overall multiplier on every tidal bulge's height, in world units. Kept small so it reads as a tide, not a flood.")]
    public float tidalStrengthScale = 0.06f;
    [Tooltip("How often (seconds) the liquid shell mesh is actually rebuilt. Tides move slowly, so this doesn't need to be every frame.")]
    public float rebuildIntervalSeconds = 0.25f;

    [Header("Star Tide (always active - uses the rolled SolarSystemDef + DayNightCycle)")]
    [Tooltip("Solar tide relative strength vs. a notional close moon - real solar tides are roughly half of Earth's lunar tide, so this is deliberately the smaller secondary contributor per the task brief.")]
    public float starTidalRelativeStrength = 0.45f;

    private Vector3 _center;
    private float _baseShellRadius; // shellRadius baked into BuildLiquidShellData (radius + seaLevel*scale + epsilon)
    private Vector3[] _baseUnitDirs; // per-vertex unit direction from center, captured once from the baked shell
    private Mesh _liquidMesh;
    private Vector3[] _workingVerts;
    private DayNightCycle _dayNight;
    private float _timeSinceRebuild;
    private readonly System.Collections.Generic.List<TidalSource> _sources = new System.Collections.Generic.List<TidalSource>();

    /// When a FluidDynamicsManager is live, IT owns the liquid shell mesh (rebuilding its
    /// topology every tick from per-vertex liquid volume, folding TidalHeightAt() in itself
    /// via this same struct's source list) - so this manager's own Update-driven RebuildShell
    /// must stop writing to the mesh to avoid two systems fighting over (and desyncing on)
    /// vertex count. TidalHeightAt() stays available either way since FluidDynamicsManager
    /// calls it directly.
    public bool OwnedByFluidDynamics;

    /// Wires the manager up to an already-built liquid shell (see GenesisCinematic /
    /// SimulationBootstrap) - call once after the shell mesh exists. Safe to call with
    /// hasLiquid == false (manager just does nothing).
    public void Init(Vector3 planetCenter, Mesh liquidMesh, PlanetTileMesh.MeshData liquidShellData,
        float planetRadius, float seaLevelElevation, float elevationWorldScale, DayNightCycle dayNight, SolarSystemDef solarSystem)
    {
        _center = planetCenter;
        _dayNight = dayNight;

        if (liquidMesh == null || liquidShellData.Vertices == null || liquidShellData.Vertices.Length == 0)
            return; // no liquid on this world - nothing to tide

        _liquidMesh = liquidMesh;
        _baseShellRadius = planetRadius + seaLevelElevation * elevationWorldScale + 0.05f; // mirrors PlanetTileMesh.BuildLiquidShellData
        _baseUnitDirs = new Vector3[liquidShellData.Vertices.Length];
        _workingVerts = new Vector3[liquidShellData.Vertices.Length];
        for (int i = 0; i < liquidShellData.Vertices.Length; i++)
            _baseUnitDirs[i] = liquidShellData.Vertices[i].normalized;

        // Star tide: direction is the sun direction (planet -> star), period matches one
        // full day/night rotation (the sub-stellar point sweeps the planet once per spin,
        // producing the classic two-bulge-per-day pattern even with a "stationary" star,
        // exactly like Earth's real solar tide).
        float starPeriod = Mathf.Max(dayNight != null ? dayNight.dayLengthSeconds : 60f, 1f);
        float starStrength = StarRelativeMass(solarSystem) * starTidalRelativeStrength;
        _sources.Add(new TidalSource
        {
            Name = "Star",
            RelativeStrength = starStrength,
            OrbitalPeriodSeconds = starPeriod,
            GetDirection = () => _dayNight != null ? _dayNight.SunDirection : Vector3.up,
        });

        RebuildShell(force: true);
    }

    /// Adds a separate TidalSource for each of the life-planet's rolled moons (if any
    /// exist) via the live transforms SolarSystemRuntime maintains every frame. Must be
    /// called AFTER both Init() and SolarSystemRuntime.Init() have already run, so moon
    /// transforms are populated and positioned.
    public void AddMoonTidalSources(SolarSystemRuntime solarRuntime)
    {
        if (solarRuntime == null || solarRuntime.LifePlanetMoonTransforms == null) return;
        foreach (var (def, tr) in solarRuntime.LifePlanetMoonTransforms)
        {
            if (tr == null) continue;
            Transform captured = tr;
            float mass = Mathf.Max(0.001f, def.RelativeMass);
            float orbitDist = Mathf.Max(0.1f, def.OrbitDistance);
            // Tidal force ~ mass / distance^3; scale so that the total lunar tidal
            // contribution stays in a visually reasonable range relative to the star tide.
            float baseStrength = mass / (orbitDist * orbitDist * orbitDist);
            _sources.Add(new TidalSource
            {
                Name = $"Moon_{captured.name}",
                RelativeStrength = baseStrength,
                OrbitalPeriodSeconds = def.OrbitalPeriodSeconds,
                GetDirection = () =>
                {
                    if (captured == null) return Vector3.up;
                    return (captured.position - _center).normalized;
                },
            });
        }
    }

    /// Crude relative-mass proxy from the rolled star's luminosity (no real stellar mass
    /// data exists on StarDef) - brighter/hotter classes correlate with higher mass on the
    /// main sequence, so luminosity^~0.3 is a believable stand-in, not a real M-L relation.
    private static float StarRelativeMass(SolarSystemDef solarSystem)
    {
        if (solarSystem?.Star == null) return 1f;
        return Mathf.Pow(Mathf.Max(solarSystem.Star.LuminositySolar, 0.0001f), 0.3f);
    }

    void Update()
    {
        if (OwnedByFluidDynamics || _liquidMesh == null || _baseUnitDirs == null) return;

        _timeSinceRebuild += Time.deltaTime;
        if (_timeSinceRebuild >= rebuildIntervalSeconds)
        {
            _timeSinceRebuild = 0f;
            RebuildShell(force: false);
        }
    }

    private void RebuildShell(bool force)
    {
        if (_liquidMesh == null) return;

        for (int i = 0; i < _baseUnitDirs.Length; i++)
        {
            Vector3 dir = _baseUnitDirs[i];
            float delta = TidalHeightAt(dir);
            _workingVerts[i] = dir * (_baseShellRadius + delta);
        }

        _liquidMesh.SetVertices(_workingVerts);
        _liquidMesh.RecalculateNormals();
        _liquidMesh.RecalculateBounds();
    }

    /// Standalone per-position tidal height delta (world units), summed across every
    /// active tidal source - factored out so a future fluid-dynamics system can call this
    /// directly as a per-vertex liquid-level modifier instead of going through the static
    /// mesh-rebuild path above.
    public float TidalHeightAt(Vector3 unitDirFromCenter)
    {
        float total = 0f;
        for (int s = 0; s < _sources.Count; s++)
        {
            TidalSource src = _sources[s];
            Vector3 toSource = src.GetDirection().normalized;
            // cos(angle) between this point and the sub-source point: +1 directly under
            // the source, -1 at the antipode. Squaring gives the classic two-bulge tidal
            // pattern (high tide both under the source AND at its antipode), then we
            // re-center around the spherical mean so the net liquid volume stays roughly
            // conserved (this rises here, dips there - it doesn't just inflate the world).
            float cosAngle = Vector3.Dot(unitDirFromCenter, toSource);
            float bulge = cosAngle * cosAngle - (1f / 3f); // mean of cos^2 over a sphere is 1/3
            total += bulge * src.RelativeStrength;
        }
        return total * tidalStrengthScale;
    }
}
