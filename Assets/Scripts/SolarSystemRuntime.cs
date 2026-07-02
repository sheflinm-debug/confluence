using System.Collections.Generic;
using UnityEngine;

/// Drives the solar system after GenesisCinematic hands off to live gameplay: real
/// orbital motion for the decorative OtherPlanets and all moons (life-planet's own moons
/// + each OtherPlanet's moons), plus the life-planet's own axial spin. Added by
/// SimulationBootstrap once GenesisCinematic's onComplete fires - reuses the already-built
/// star/planet primitives from GenesisCinematic rather than rebuilding them (consistent
/// with how the liquid shell is handed off to TidalForceManager at the same point).
///
/// === SCALE SCHEME (audit reasoning, ties together GenesisCinematic + here) ===
/// Real astronomical ratios are unusable directly at any single rendered scale: Jupiter's
/// orbit alone is ~5 AU while the inner planets sit under 1.5 AU, and a true-to-scale Sun
/// is ~109 Earth radii - rendering all of that consistently makes either the inner system
/// invisible or the planets sub-pixel. Three independent compressions are used, each
/// solving a different part of the problem and documented at its own definition site:
///   1. ORBIT DISTANCE: sqrt(AU) * VisualOrbitScale (GenesisCinematic.VisualOrbitScale).
///      sqrt() compresses the outer/giant orbits much more than the inner ones (since AU
///      values for giants are 5-30x the inner planets but sqrt() only stretches that to
///      ~2-5x), so the whole system fits in a legible camera frame without inner planets
///      bunching into a single point.
///   2. STAR RADIUS: luminosity^0.25 * fixed multiplier, clamped (GenesisCinematic.
///      StarSizeFromLuminosity). Ties the star's rendered size to its ROLLED class for the
///      first time - previously every star rendered at a single fixed size regardless of
///      whether the roll was a dim M-dwarf or a blazing O-star, which was the main
///      remaining "doesn't feel right" scale complaint once orbital compression already
///      existed. L^0.25 was chosen (rather than a linear or sqrt mapping) because
///      luminosity already enters the Teq formula as L^0.25 - reusing the same exponent
///      means a star that reads as "bigger" also reads as the same relative "hotter/
///      brighter" the orbit-distance back-solve already encodes, instead of introducing an
///      unrelated second scale.
///   3. MOON ORBITS/SIZES: linear, parent-radius-relative (MoonOrbitWorldUnits /
///      MoonDef.RelativeSize below). Moons orbit far too close to their parent, in true
///      AU terms, for the sqrt(AU) compression above to give them any visible separation
///      at all - so moon placement intentionally ignores the AU-based scheme entirely and
///      uses a separate "parent-radii" unit instead, which is the only relationship that
///      actually needs to read clearly (moon vs. its own parent), not moon-vs-star.
///
/// === ORBITAL MOTION TIMING ===
/// OtherPlanets and moons both walk a circular orbit (or, for the life-planet's own moons,
/// the SAME ellipse shape OrbitalSeasons.cs already rolled for the life-planet's star
/// orbit - see UseLifePlanetEccentricityForMoons) at a fixed angular rate derived from each
/// body's rolled OrbitalPeriodSeconds (for moons) or a period synthesized from OrbitAU for
/// decorative planets (see OtherPlanetPeriodSeconds). Real periods are absurdly long
/// (Jupiter's literal period is ~12 years) so a literal scaled-down mapping would make
/// nothing visibly move during a play session; instead periods are picked/clamped into a
/// 30-90-second band (per the task brief - "a moon completing a visible orbit every 30-90
/// seconds of real play reads better than a literal scaled-down realistic period"), with
/// farther-out bodies given longer (but still capped) periods so there's still a sense of
/// "outer orbits move slower," matching Kepler's third law in spirit without being
/// literally Keplerian.
///
/// === ROTATION / AGENT DESYNC ===
/// See GenesisCinematic.Run's comment at _lifePlanetGo's construction for the full
/// reasoning. Short version: AgentController positions agents via independent world-space
/// lat/long math (SphereSurface) against a plain planetCenter/planetRadius, NOT by
/// parenting agents under the planet's transform. So spinning `_lifePlanetGo` itself would
/// desync agents from the (then-rotated) rendered terrain under their feet. The fix:
/// GenesisCinematic now builds a separate "PlanetVisual" child (mesh + liquid shell) under
/// the unrotated collider root; this script only ever rotates THAT child's transform, and
/// derives day/night lighting from an explicit `LifePlanetRotationDeg` float (see
/// DayNightCycle integration below) rather than from transform.rotation directly, so any
/// other system that wants "the planet's current spin angle" has one canonical source
/// that's decoupled from whichever transform happens to be spinning.
public class SolarSystemRuntime : MonoBehaviour
{
    public static SolarSystemRuntime Instance { get; private set; }

    [Header("Moon orbit scale (parent-radii units, NOT AU - see header doc)")]
    [Tooltip("World units per 1.0 of MoonDef.OrbitDistance, multiplied by the parent's rendered radius.")]
    public float moonOrbitUnitScale = 1f;

    [Header("Decorative planet orbital period synthesis")]
    [Tooltip("Other-planet orbital periods are spread across this range (seconds), nearer-orbit planets faster.")]
    public float otherPlanetMinPeriodSeconds = 35f;
    public float otherPlanetMaxPeriodSeconds = 90f;

    [Header("Life-planet axial spin")]
    [Tooltip("Real-time seconds for one full rotation of the life-planet's VISUAL mesh+liquid shell (NOT the logical collider, which never rotates - see class header). Keep equal to DayNightCycle.dayLengthSeconds (default 60s) so the spin and the day/night light cycle are explicitly consistent.")]
    public float planetRotationPeriodSeconds = 60f;

    /// Current life-planet visual spin angle in degrees, the single canonical source other
    /// systems (DayNightCycle) should read instead of inspecting transform.rotation, since
    /// the rotating transform (PlanetVisual) is intentionally not the same transform
    /// agents/collider logic are measured against.
    public float LifePlanetRotationDeg { get; private set; }

    public SolarSystemDef SolarSystem => _solarSystem;
    private SolarSystemDef _solarSystem;
    private GenesisCinematic _cinematic;
    private Transform _visualTransform;

    private struct OrbitingBody
    {
        public Transform Tr;
        public Transform ParentTr; // orbit is centered on this transform's current position
        public float DistanceWorld;
        public float PeriodSeconds;
        public float PhaseRad; // current angle
        public float Eccentricity; // 0 = circular
        public Vector3 PlaneNormal; // orbit plane normal (randomized per body so moons don't all share one ring)
        public Vector3 PlaneTangent;
    }

    private readonly List<OrbitingBody> _orbiters = new List<OrbitingBody>();
    private readonly List<GameObject> _spawnedMoonGos = new List<GameObject>();

    /// (moonDef, liveTransform) pairs for the life-planet's own moons specifically -
    /// exposed so TidalForceManager can add a real per-moon tidal source using each
    /// moon's actual live world position (set by AdvanceOrbiters every frame) instead of
    /// re-deriving orbital position itself. OtherPlanets' moons aren't exposed here since
    /// they don't tidally affect the life-planet's liquid shell.
    public readonly List<(MoonDef Def, Transform Tr)> LifePlanetMoonTransforms = new List<(MoonDef, Transform)>();

    void Awake() => Instance = this;

    /// Wires this driver up to the already-completed cinematic's GameObjects and starts
    /// real orbital motion + life-planet spin. Call once, right after GenesisCinematic's
    /// onComplete fires (same handoff point OrbitalSeasons.BeginGameplay/TidalForceManager
    /// use).
    public void Init(SolarSystemDef solarSystem, GenesisCinematic cinematic)
    {
        Instance = this;
        _solarSystem = solarSystem;
        _cinematic = cinematic;
        _visualTransform = cinematic.LifePlanetVisualGo.transform;

        SpawnMoonsForLifePlanet();
        SpawnMoonsAndRegisterOtherPlanets();
    }

    private void SpawnMoonsForLifePlanet()
    {
        // Life-planet's mesh is baked at true `planetRadius` world units (its localScale is
        // 1 post-cinematic), so parentRenderedRadius is simply planetRadius here - unlike
        // the decorative OtherPlanets below, which are scaled-down primitives whose
        // rendered radius has to be read back from their transform.
        SpawnMoons(_solarSystem.LifePlanetMoons, _cinematic.LifePlanetGo.transform, _cinematic.PlanetRadius,
            useLifePlanetEccentricity: true, isLifePlanetMoons: true);
    }

    private void SpawnMoonsAndRegisterOtherPlanets()
    {
        IReadOnlyList<GameObject> planetGos = _cinematic.OtherPlanetGos;
        for (int i = 0; i < _solarSystem.OtherPlanets.Count && i < planetGos.Count; i++)
        {
            DecorativePlanetDef def = _solarSystem.OtherPlanets[i];
            GameObject go = planetGos[i];
            if (go == null) continue;

            // Decorative planets orbit the star. Nearer orbits move faster (visually
            // mimics Kepler's third law) within the 30-90s band the task brief calls out.
            float orbitT = Mathf.InverseLerp(0.05f, 40f, def.OrbitAU);
            float period = Mathf.Lerp(otherPlanetMinPeriodSeconds, otherPlanetMaxPeriodSeconds, Mathf.Clamp01(orbitT));
            float currentDist = Vector3.Distance(go.transform.position, _cinematic.StarGo.transform.position);

            _orbiters.Add(new OrbitingBody
            {
                Tr = go.transform,
                ParentTr = _cinematic.StarGo.transform,
                DistanceWorld = currentDist,
                PeriodSeconds = period,
                PhaseRad = Random.Range(0f, Mathf.PI * 2f),
                Eccentricity = 0f, // decorative planets keep circular orbits - only the life-planet's own star orbit has real eccentricity data (OrbitalSeasons)
                PlaneNormal = Vector3.up,
                PlaneTangent = Vector3.forward,
            });

            // This planet's rendered radius in world units, used to scale its moons.
            float rendered = go.transform.localScale.x * 0.5f; // sphere primitive: localScale.x == diameter
            SpawnMoons(def.Moons, go.transform, rendered, useLifePlanetEccentricity: false);
        }
    }

    /// Spawns small sphere primitives for each MoonDef orbiting `parentTr`, and registers
    /// them as OrbitingBody entries this script advances every frame.
    private void SpawnMoons(List<MoonDef> moons, Transform parentTr, float parentRenderedRadius,
        bool useLifePlanetEccentricity, bool isLifePlanetMoons = false)
    {
        if (moons == null) return;
        for (int i = 0; i < moons.Count; i++)
        {
            MoonDef m = moons[i];
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"Moon_{parentTr.name}_{i}";
            go.transform.SetParent(transform); // flat hierarchy under this runtime, NOT under the parent planet - position is computed in world space each frame instead, so a spinning/orbiting parent doesn't double-transform the moon
            Destroy(go.GetComponent<Collider>());
            float radius = Mathf.Max(0.05f, parentRenderedRadius * m.RelativeSize);
            go.transform.localScale = Vector3.one * radius * 2f; // sphere primitive diameter == localScale
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = m.Color;
            go.GetComponent<Renderer>().material = mat;
            _spawnedMoonGos.Add(go);

            if (isLifePlanetMoons)
                LifePlanetMoonTransforms.Add((m, go.transform));

            float distance = Mathf.Max(parentRenderedRadius * 1.5f, parentRenderedRadius * m.OrbitDistance * moonOrbitUnitScale);

            // Randomized orbit-plane orientation per moon so a multi-moon system doesn't
            // read as a single flat ring (purely a visual-variety choice, not physically
            // derived from anything rolled).
            Vector3 planeNormal = Random.onUnitSphere;
            if (Vector3.Dot(planeNormal, Vector3.up) > 0.97f) planeNormal = Vector3.forward; // avoid degenerate near-parallel case
            Vector3 tangent = Vector3.Cross(planeNormal, Vector3.up).normalized;
            if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.Cross(planeNormal, Vector3.right).normalized;
            Vector3 bitangent = Vector3.Cross(planeNormal, tangent).normalized;

            _orbiters.Add(new OrbitingBody
            {
                Tr = go.transform,
                ParentTr = parentTr,
                DistanceWorld = distance,
                PeriodSeconds = Mathf.Max(5f, m.OrbitalPeriodSeconds),
                PhaseRad = m.StartAngleRad,
                Eccentricity = useLifePlanetEccentricity ? _solarSystem.LifePlanetEccentricity : 0f,
                PlaneNormal = tangent, // reuse fields: store the two in-plane basis vectors in PlaneNormal/PlaneTangent, true normal isn't needed at update time
                PlaneTangent = bitangent,
            });
        }
    }

    void Update()
    {
        AdvanceOrbiters();
        AdvanceLifePlanetRotation();
    }

    private void AdvanceOrbiters()
    {
        for (int i = 0; i < _orbiters.Count; i++)
        {
            OrbitingBody b = _orbiters[i];
            if (b.Tr == null || b.ParentTr == null) continue;

            float angularSpeed = (Mathf.PI * 2f) / Mathf.Max(0.01f, b.PeriodSeconds);
            b.PhaseRad += angularSpeed * Time.deltaTime;
            if (b.PhaseRad > Mathf.PI * 2f) b.PhaseRad -= Mathf.PI * 2f;

            // Elliptical radius via the same polar ellipse equation OrbitalSeasons uses for
            // the life-planet's own star orbit, so a life-planet moon's path is consistent
            // with the same eccentricity data rather than an unrelated wobble.
            float e = b.Eccentricity;
            float r = b.DistanceWorld * (1f - e * e) / (1f + e * Mathf.Cos(b.PhaseRad));

            Vector3 offset = (Mathf.Cos(b.PhaseRad) * b.PlaneNormal + Mathf.Sin(b.PhaseRad) * b.PlaneTangent) * r;
            b.Tr.position = b.ParentTr.position + offset;

            _orbiters[i] = b;
        }
    }

    /// Spins the life-planet's VISUAL child only (never the logical collider root - see
    /// class header). Day/night lighting is derived from this same angle by DayNightCycle
    /// when StarSystemRuntime.Instance is present (see DayNightCycle.Update), so the two
    /// stay explicitly consistent instead of fighting each other.
    private void AdvanceLifePlanetRotation()
    {
        if (_visualTransform == null || planetRotationPeriodSeconds <= 0f) return;
        float degPerSec = 360f / planetRotationPeriodSeconds;
        LifePlanetRotationDeg += degPerSec * Time.deltaTime;
        if (LifePlanetRotationDeg > 360f) LifePlanetRotationDeg -= 360f;
        _visualTransform.localRotation = Quaternion.Euler(0f, LifePlanetRotationDeg, 0f);
    }

    /// Habitable-zone diagnostic readout (task part 5) - shows the rolled star's HZ bounds
    /// and where the life-planet's orbit actually fell relative to them. Purely
    /// informational, same OnGUI pattern as PlanetTemperature/DeepTimeClock.
    void OnGUI()
    {
        if (_solarSystem == null || GameHUD.SuppressRawOverlays) return;
        string zone = _solarSystem.LifePlanetInHabitableZone ? "INSIDE habitable zone" : "OUTSIDE habitable zone";
        GUI.Label(new Rect(10, 90, 460, 20),
            $"HZ: {_solarSystem.HabitableZoneInnerAU:F2}-{_solarSystem.HabitableZoneOuterAU:F2} AU " +
            $"({_solarSystem.Star.SpectralClass}-class, L={_solarSystem.Star.LuminositySolar:F3} sol) | " +
            $"orbit {_solarSystem.LifePlanetOrbitAU:F2} AU - {zone}");
    }
}
