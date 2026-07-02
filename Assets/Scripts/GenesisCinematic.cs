using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// Placeholder prologue cinematic matching game-eras-spec.md's Intro (18s) + Abiotic/
/// Tectonic (20s) sections exactly - 8 sub-phases, each timed from EraTimeline so the
/// on-screen DeepTimeClock caption never drifts out of sync with what's animating.
/// Camera/primitive moves stand in for true astrophysical rendering, same placeholder
/// scoping discussed earlier; the planet's REAL tectonic terrain (computed up front by
/// TectonicPlanetGenerator) is revealed via lerps between precomputed snapshots, not
/// re-simulated live. Hands off to SimulationBootstrap's colony spawn when done.
public class GenesisCinematic : MonoBehaviour
{
    // The life planet's mesh is always baked at full gameplay `planetRadius`, but during
    // the wide system-view shots (Big Bang through System settles) it's rendered at this
    // much smaller localScale so it reads correctly as "a tiny world next to its star"
    // instead of dwarfing the star and nearly touching it (the original bug). It grows
    // to full scale (1.0) only once the camera is already close, at the start of the
    // Molten Bombardment phase.
    //
    // SCALE SCHEME (see SolarSystemRuntime.cs header for the full writeup): orbital
    // distance compresses with sqrt(AU) so outer planets don't fly off-screen relative to
    // the life-planet's orbit; star RADIUS additionally scales with the star's rolled
    // luminosity^0.25 (StarSizeFromLuminosity below) instead of a single fixed size, so an
    // M-dwarf and an O-star no longer render identically - that mismatch (a star's
    // apparent size carrying no relationship to its rolled class) was the main remaining
    // "scale is still off" complaint once orbital compression was already in place.
    private const float IntroDisplayRadius = 1.2f;
    internal const float VisualOrbitScale = 22f; // world units per sqrt(AU) of true orbital distance

    /// Star radius (in IntroDisplayRadius units) as a function of rolled luminosity.
    /// Real stellar radius doesn't follow a clean power law of luminosity alone (depends on
    /// temperature too via R ~ sqrt(L)/T^2), but for a purely visual diagnostic a gentle
    /// L^0.25 power law gives a monotonic, clamped spread that still reads as "small dim
    /// dwarf" through "huge bright giant" without the outliers (O-class, L up to 5000)
    /// blowing the scene up to absurd size.
    internal static float StarSizeFromLuminosity(float luminositySolar)
    {
        float raw = Mathf.Pow(Mathf.Max(luminositySolar, 0.0001f), 0.25f);
        return Mathf.Clamp(raw, 0.5f, 3.2f) * IntroDisplayRadius * 6f;
    }

    private GameObject _starGo;
    private readonly List<GameObject> _otherPlanetGos = new List<GameObject>();
    private GameObject _lifePlanetGo;
    private Mesh _lifePlanetMesh;
    private GameObject _liquidGo;
    private Mesh _liquidMesh;
    private PlanetTileMesh.MeshData _liquidShellData;
    private bool _hasLiquid;
    private float _seaLevel;
    private float _elevationWorldScale;

    /// Exposed so SimulationBootstrap can hand the baked liquid shell off to
    /// TidalForceManager once the cinematic completes - tidal bulging is a post-genesis
    /// runtime effect layered on top of this already-built mesh, not part of the reveal.
    public GameObject LiquidGo => _liquidGo;
    public Mesh LiquidMesh => _liquidMesh;
    public PlanetTileMesh.MeshData LiquidShellData => _liquidShellData;
    public bool HasLiquid => _hasLiquid;
    public float SeaLevel => _seaLevel;
    public float ElevationWorldScale => _elevationWorldScale;

    /// Exposed so SolarSystemRuntime can take over animating these same GameObjects
    /// (real orbital motion + life-planet spin) once the cinematic hands off to live
    /// gameplay - it reuses the already-built star/planet primitives rather than
    /// rebuilding them, same "don't destroy, just keep driving" pattern as the liquid
    /// shell handoff to TidalForceManager above.
    public GameObject StarGo => _starGo;
    public IReadOnlyList<GameObject> OtherPlanetGos => _otherPlanetGos;
    public GameObject LifePlanetGo => _lifePlanetGo;
    public GameObject LifePlanetVisualGo { get; private set; }
    public float PlanetRadius => _planetRadius;

    private PlanetTileMesh.MeshData _molten, _terrainOnly;
    private Vector3[] _lerpVerts;
    private Color[] _lerpColors;
    private Camera _cam;
    private System.Action _onComplete;
    private float _planetRadius;

    public void Run(
        Vector3 center, float planetRadius, TectonicResult tectonics, RockArchetypeDef archetype,
        LiquidDef liquid, float seaLevel, float liquidTempK, float elevationWorldScale,
        SolarSystemDef solarSystem, Transform parent, System.Action onComplete)
    {
        _onComplete = onComplete;
        _planetRadius = planetRadius;
        _seaLevel = seaLevel;
        _elevationWorldScale = elevationWorldScale;

        _cam = Camera.main;
        if (_cam == null) _cam = FindAnyObjectByType<Camera>();
        if (_cam == null)
        {
            GameObject camGo = new GameObject("Main Camera");
            _cam = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
        }

        // Space background from frame 1 of the cinematic (Big Bang through Abiogenesis).
        // SetupOrbitCamera repeats this post-cinematic so it survives any camera reconfiguration.
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = Color.black;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.04f, 0.04f, 0.07f);

        _molten = BuildMoltenData(tectonics, planetRadius);
        _terrainOnly = PlanetTileMesh.BuildData(tectonics, planetRadius, elevationWorldScale, archetype, liquidTempK);

        _lerpVerts = new Vector3[_molten.Vertices.Length];
        _lerpColors = new Color[_molten.Colors.Length];

        // IMPORTANT (rotation/agent desync, see SolarSystemRuntime.cs header for the full
        // writeup): AgentController/SphereSurface position agents using independent
        // world-space lat/long math against a plain `planetCenter`/`planetRadius`, NOT by
        // parenting them under this planet's transform. So `_lifePlanetGo` itself - which
        // carries the SphereCollider and is the transform agents are implicitly measured
        // against - must NEVER be rotated once gameplay starts spinning the planet, or
        // agents would visually slide across the now-rotated terrain while staying at
        // their old (unrotated) logical positions. The actual rotating VISUAL (mesh +
        // liquid shell) lives on a separate child, "PlanetVisual", so SolarSystemRuntime
        // can spin that child freely while `_lifePlanetGo` (and its collider) stays fixed.
        // The child still inherits `_lifePlanetGo`'s intro-scale growth animation for free
        // since Unity scale is hierarchical, so PhaseAccretion/PhaseMoltenBombardment's
        // existing localScale lerps need no changes.
        _lifePlanetGo = new GameObject("Planet");
        _lifePlanetGo.transform.SetParent(parent);
        _lifePlanetGo.transform.position = center;
        _lifePlanetGo.transform.localScale = Vector3.zero; // accretes during the disk phase
        SphereCollider collider = _lifePlanetGo.AddComponent<SphereCollider>();
        collider.radius = planetRadius;

        GameObject visualGo = new GameObject("PlanetVisual");
        visualGo.transform.SetParent(_lifePlanetGo.transform);
        visualGo.transform.localPosition = Vector3.zero;
        visualGo.transform.localRotation = Quaternion.identity;
        visualGo.transform.localScale = Vector3.one;
        LifePlanetVisualGo = visualGo;

        _lifePlanetMesh = PlanetTileMesh.ToMesh(_molten);
        MeshFilter filter = visualGo.AddComponent<MeshFilter>();
        filter.mesh = _lifePlanetMesh;
        MeshRenderer renderer = visualGo.AddComponent<MeshRenderer>();
        renderer.material = new Material(Shader.Find("Custom/VertexColorOpaqueURP"));

        // Liquid is a SEPARATE translucent shell mesh, parented under the spinning visual
        // (not the logical collider root) so it rotates WITH the terrain it's pooled
        // into - not a recoloring of the terrain itself, so it actually reads as fluid
        // rather than "more terrain." Built now but only revealed during Condensation &
        // water arrival.
        _liquidShellData = liquid != null ? PlanetTileMesh.BuildLiquidShellData(tectonics, planetRadius, elevationWorldScale, seaLevel, liquid, liquidTempK) : default;
        _hasLiquid = liquid != null && _liquidShellData.Vertices != null && _liquidShellData.Vertices.Length > 0;
        if (_hasLiquid)
        {
            _liquidGo = new GameObject("LiquidShell");
            _liquidGo.transform.SetParent(visualGo.transform);
            _liquidGo.transform.localPosition = Vector3.zero;
            _liquidGo.transform.localRotation = Quaternion.identity;
            _liquidGo.transform.localScale = Vector3.one;
            _liquidMesh = PlanetTileMesh.ToMesh(_liquidShellData);
            MeshFilter liquidFilter = _liquidGo.AddComponent<MeshFilter>();
            liquidFilter.mesh = _liquidMesh;
            MeshRenderer liquidRenderer = _liquidGo.AddComponent<MeshRenderer>();
            liquidRenderer.material = new Material(Shader.Find("Custom/VertexColorTransparentURP"));
            _liquidGo.SetActive(false); // revealed during Condensation & water arrival
        }

        StartCoroutine(RunSequence(center, planetRadius, solarSystem, parent));
    }

    /// Same vertex positions as the smooth sphere, but lava-colored (dark red -> bright
    /// orange via cheap per-vertex hash noise) - the crust hasn't solidified yet, so
    /// real rock-type coloring would be premature here.
    private static PlanetTileMesh.MeshData BuildMoltenData(TectonicResult tectonics, float radius)
    {
        int count = tectonics.Triangles.Count;
        var verts = new Vector3[count];
        var colors = new Color[count];
        var triangles = new int[count];

        for (int i = 0; i < count; i++)
        {
            Vector3 unit = tectonics.UnitVerts[tectonics.Triangles[i]];
            verts[i] = unit * radius;
            float n = (Mathf.PerlinNoise(unit.x * 4f + 17f, unit.y * 4f + 5f) + Mathf.PerlinNoise(unit.y * 4f, unit.z * 4f) + Mathf.PerlinNoise(unit.z * 4f, unit.x * 4f)) / 3f;
            colors[i] = Color.Lerp(new Color(0.35f, 0.04f, 0.0f), new Color(1f, 0.55f, 0.05f), n);
            triangles[i] = i;
        }
        return new PlanetTileMesh.MeshData { Vertices = verts, Colors = colors, Triangles = triangles };
    }

    private IEnumerator RunSequence(Vector3 center, float planetRadius, SolarSystemDef solarSystem, Transform parent)
    {
        yield return PhaseBigBang();
        yield return PhaseStarFormation(center, solarSystem, parent);
        yield return PhaseAccretion(center, planetRadius, solarSystem, parent);
        yield return PhaseSystemSettles(center, planetRadius, solarSystem);
        yield return PhaseMoltenBombardment(center);
        yield return LerpPhase(EraTimeline.AbioticStartIndex + 1, _molten, _terrainOnly, center); // Crust formation & cooling
        yield return PhaseCondensation(EraTimeline.AbioticStartIndex + 2, center);                 // Condensation & water arrival
        yield return PhaseTopographyReveal(center, planetRadius);
        yield return PhaseFirstLifeEmerges(center, planetRadius);

        // Star and decorative planets are left in the scene as permanent distant
        // background dressing (per the user's note: focus stays on the life-planet, but
        // "we can just see the other stuff") - they are NOT destroyed.
        _onComplete?.Invoke();
    }

    private IEnumerator PhaseBigBang()
    {
        float duration = EraTimeline.Phases[EraTimeline.IntroStartIndex + 0].DurationSeconds;

        _cam.transform.position = new Vector3(0f, 0f, -15f);
        _cam.transform.LookAt(Vector3.zero);

        // Central flash sphere.
        GameObject burst = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(burst.GetComponent<Collider>());
        burst.transform.position = Vector3.zero;
        Material burstMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        burstMat.color = Color.white;
        burstMat.EnableKeyword("_EMISSION");
        burst.GetComponent<Renderer>().material = burstMat;

        // Ejecta particles — small spheres launched radially outward.
        const int ParticleCount = 32;
        var particles = new (GameObject go, Vector3 dir, float speed, float size)[ParticleCount];
        Material particleMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        particleMat.EnableKeyword("_EMISSION");
        for (int i = 0; i < ParticleCount; i++)
        {
            GameObject p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(p.GetComponent<Collider>());
            p.transform.position = Vector3.zero;
            p.GetComponent<Renderer>().material = particleMat;
            float sz = Random.Range(0.08f, 0.35f);
            p.transform.localScale = Vector3.zero;
            particles[i] = (p, Random.onUnitSphere, Random.Range(2.5f, 8f), sz);
        }

        // Two shock-ring discs (flat horizontal rings expanding outward).
        GameObject ring1 = BuildRingGo(Vector3.zero, 0f, new Color(1f, 0.9f, 0.6f, 0.7f));
        GameObject ring2 = BuildRingGo(Vector3.zero, 0f, new Color(0.7f, 0.85f, 1f, 0.5f));

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = t / duration; // 0→1 over full duration

            // Central burst: bright flash → dims and expands.
            float flashK = Mathf.Clamp01(k * 8f);         // peaks at 1/8 of duration
            float emStr  = Mathf.Lerp(12f, 0.2f, flashK);
            Color flashCol = Color.Lerp(Color.white, new Color(1f, 0.7f, 0.2f), k * 0.6f);
            burstMat.color = flashCol;
            burstMat.SetColor("_EmissionColor", flashCol * emStr);
            burst.transform.localScale = Vector3.one * Mathf.SmoothStep(0f, 5f, k);

            // Particles: grow in then fade as they speed outward.
            for (int i = 0; i < ParticleCount; i++)
            {
                var (go, dir, spd, sz) = particles[i];
                go.transform.position   = dir * spd * k * duration * 0.35f;
                float pLife = Mathf.Clamp01(k * 3f);
                float pFade = 1f - Mathf.Clamp01((k - 0.4f) / 0.6f);
                go.transform.localScale = Vector3.one * sz * pLife * pFade;
                Color pCol = Color.Lerp(new Color(1f, 0.9f, 0.5f), new Color(0.4f, 0.5f, 1f), k);
                particleMat.SetColor("_EmissionColor", pCol * Mathf.Lerp(3f, 0.2f, k));
                particleMat.color = pCol;
            }

            // Shock rings: expand fast and fade.
            float ringK1 = Mathf.Clamp01(k * 4f);
            float ringK2 = Mathf.Clamp01((k - 0.1f) * 4f);
            ring1.transform.localScale = Vector3.one * Mathf.Lerp(0.1f, 14f, Mathf.SmoothStep(0f, 1f, ringK1));
            ring2.transform.localScale = Vector3.one * Mathf.Lerp(0.1f, 20f, Mathf.SmoothStep(0f, 1f, ringK2));
            SetRingAlpha(ring1, Mathf.Lerp(1f, 0f, ringK1 * ringK1));
            SetRingAlpha(ring2, Mathf.Lerp(0.7f, 0f, ringK2 * ringK2));

            // Subtle camera shake on the flash peak.
            float shake = Mathf.Max(0f, 0.12f - k * 0.5f);
            _cam.transform.position = new Vector3(
                Random.Range(-shake, shake),
                Random.Range(-shake, shake),
                -15f + Random.Range(-shake * 0.5f, shake * 0.5f));
            _cam.transform.LookAt(Vector3.zero);

            yield return null;
        }

        Destroy(burst);
        for (int i = 0; i < ParticleCount; i++) Destroy(particles[i].go);
        Destroy(ring1);
        Destroy(ring2);

        // Restore camera.
        _cam.transform.position = new Vector3(0f, 0f, -15f);
        _cam.transform.LookAt(Vector3.zero);
    }

    // Builds a flat disc GameObject (ring) with a transparent unlit material.
    private static GameObject BuildRingGo(Vector3 center, float initialScale, Color color)
    {
        const int Segs = 48;
        const float InnerR = 0.35f, OuterR = 0.5f;
        var verts = new Vector3[Segs * 2];
        var tris  = new int[Segs * 6];
        var cols  = new Color[Segs * 2];
        for (int i = 0; i < Segs; i++)
        {
            float a = (float)i / Segs * Mathf.PI * 2f;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            verts[i * 2]     = new Vector3(c * InnerR, 0f, s * InnerR);
            verts[i * 2 + 1] = new Vector3(c * OuterR, 0f, s * OuterR);
            cols[i * 2] = cols[i * 2 + 1] = color;
            int next = (i + 1) % Segs, b = i * 6;
            tris[b]   = i*2;     tris[b+1] = next*2;   tris[b+2] = i*2+1;
            tris[b+3] = next*2;  tris[b+4] = next*2+1; tris[b+5] = i*2+1;
        }
        var mesh = new Mesh { vertices = verts, triangles = tris, colors = cols };
        mesh.RecalculateNormals();

        var go  = new GameObject("ShockRing");
        var mf  = go.AddComponent<MeshFilter>();
        var mr  = go.AddComponent<MeshRenderer>();
        mf.mesh = mesh;
        mr.material = new Material(Shader.Find("Custom/VertexColorTransparentURP"));
        go.transform.position   = center;
        go.transform.localScale = Vector3.one * initialScale;
        return go;
    }

    private static void SetRingAlpha(GameObject ring, float alpha)
    {
        var mr  = ring.GetComponent<MeshRenderer>();
        var mf  = ring.GetComponent<MeshFilter>();
        if (mr == null || mf == null) return;
        Color[] cols = mf.mesh.colors;
        for (int i = 0; i < cols.Length; i++) cols[i].a = alpha;
        mf.mesh.colors = cols;
    }

    private IEnumerator PhaseStarFormation(Vector3 center, SolarSystemDef solarSystem, Transform parent)
    {
        float duration = EraTimeline.Phases[EraTimeline.IntroStartIndex + 1].DurationSeconds;
        // Separation is generous and independent of the planet's eventual full size -
        // the planet is still IntroDisplayRadius-scale here, so a star many times that
        // size with real clearance reads correctly instead of looking "too close."
        float lifeDist = Mathf.Sqrt(Mathf.Max(solarSystem.LifePlanetOrbitAU, 0.01f)) * VisualOrbitScale;
        float starSeparation = Mathf.Max(lifeDist + 40f, IntroDisplayRadius * 25f);

        _starGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _starGo.name = "Star";
        _starGo.transform.SetParent(parent);
        Destroy(_starGo.GetComponent<Collider>());
        _starGo.transform.position = center + Vector3.left * starSeparation;
        _starGo.transform.localScale = Vector3.zero;
        Material starMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        starMat.color = solarSystem.Star.Color;
        starMat.EnableKeyword("_EMISSION");
        starMat.SetColor("_EmissionColor", solarSystem.Star.Color * 2f);
        _starGo.GetComponent<Renderer>().material = starMat;

        _cam.transform.position = _starGo.transform.position + Vector3.back * 30f;
        _cam.transform.LookAt(_starGo.transform.position);

        // Star reads clearly larger than the intro-scale planet, and now actually scales
        // with the rolled star's luminosity instead of a single fixed size for every class.
        Vector3 starFinalScale = Vector3.one * StarSizeFromLuminosity(solarSystem.Star.LuminositySolar);
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / duration);
            _starGo.transform.localScale = starFinalScale * k;
            yield return null;
        }
    }

    private IEnumerator PhaseAccretion(Vector3 center, float planetRadius, SolarSystemDef solarSystem, Transform parent)
    {
        float duration = EraTimeline.Phases[EraTimeline.IntroStartIndex + 2].DurationSeconds;

        Vector3 starPos     = _starGo.transform.position;
        Vector3 starToCenter = (center - starPos).normalized;

        // Protoplanetary disk: a flat ring centred on the star in the plane of the solar system.
        float diskOuter = Mathf.Sqrt(Mathf.Max(solarSystem.LifePlanetOrbitAU, 0.01f)) * VisualOrbitScale * 1.5f;
        float diskInner = diskOuter * 0.08f;
        GameObject diskGo = BuildDiskGo(starPos, diskInner, diskOuter, 72,
            new Color(0.72f, 0.48f, 0.18f, 0.7f), new Color(0.45f, 0.30f, 0.10f, 0.0f));
        // Tilt the disk ~15° from horizontal so it has visual depth.
        diskGo.transform.rotation = Quaternion.Euler(15f, 0f, 0f);

        var planetFinalScales = new List<Vector3>();
        foreach (var p in solarSystem.OtherPlanets)
        {
            float d = Mathf.Sqrt(Mathf.Max(p.OrbitAU, 0.01f)) * VisualOrbitScale;
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"Planet_{p.Kind}";
            go.transform.SetParent(parent);
            Destroy(go.GetComponent<Collider>());
            go.transform.position = starPos + starToCenter * d
                + Vector3.up * Random.Range(-3f, 3f) + Vector3.forward * Random.Range(-10f, 10f);
            Vector3 finalScale = Vector3.one * p.RelativeSize * IntroDisplayRadius * 0.6f;
            go.transform.localScale = Vector3.zero;
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = p.Color;
            go.GetComponent<Renderer>().material = mat;
            _otherPlanetGos.Add(go);
            planetFinalScales.Add(finalScale);
        }

        Vector3 lifePlanetIntroScale = Vector3.one * (IntroDisplayRadius / _planetRadius);
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / duration);

            // Disk: bright early then fades as planets coalesce.
            float diskFade = 1f - Mathf.Clamp01((k - 0.4f) / 0.6f);
            SetDiskAlpha(diskGo, diskFade * 0.7f, diskFade * 0f);
            diskGo.transform.Rotate(Vector3.up, 8f * Time.deltaTime); // slow spin

            for (int i = 0; i < _otherPlanetGos.Count; i++)
                _otherPlanetGos[i].transform.localScale = planetFinalScales[i] * Mathf.Clamp01(k * 1.3f - 0.1f * i);
            _lifePlanetGo.transform.localScale = lifePlanetIntroScale * k;
            yield return null;
        }
        _lifePlanetGo.transform.localScale = lifePlanetIntroScale;
        Destroy(diskGo);
    }

    /// Builds a flat shaded disc mesh (inner → outer gradient) as a new GameObject.
    private static GameObject BuildDiskGo(Vector3 center, float inner, float outer, int segs,
        Color innerColor, Color outerColor)
    {
        var verts = new Vector3[segs * 2];
        var tris  = new int[segs * 6];
        var cols  = new Color[segs * 2];
        for (int i = 0; i < segs; i++)
        {
            float a = (float)i / segs * Mathf.PI * 2f;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            verts[i * 2]     = new Vector3(c * inner, 0f, s * inner);
            verts[i * 2 + 1] = new Vector3(c * outer, 0f, s * outer);
            cols[i * 2]      = innerColor;
            cols[i * 2 + 1]  = outerColor;
            int next = (i + 1) % segs, b = i * 6;
            tris[b]   = i*2;     tris[b+1] = next*2;   tris[b+2] = i*2+1;
            tris[b+3] = next*2;  tris[b+4] = next*2+1; tris[b+5] = i*2+1;
        }
        var mesh = new Mesh { vertices = verts, triangles = tris, colors = cols };
        mesh.RecalculateNormals();

        var go = new GameObject("ProtoplanetaryDisk");
        go.AddComponent<MeshFilter>().mesh = mesh;
        go.AddComponent<MeshRenderer>().material =
            new Material(Shader.Find("Custom/VertexColorTransparentURP"));
        go.transform.position = center;
        return go;
    }

    private static void SetDiskAlpha(GameObject diskGo, float innerAlpha, float outerAlpha)
    {
        var mf = diskGo.GetComponent<MeshFilter>();
        if (mf == null) return;
        Color[] cols = mf.mesh.colors;
        for (int i = 0; i < cols.Length; i++)
            cols[i].a = (i % 2 == 0) ? innerAlpha : outerAlpha;
        mf.mesh.colors = cols;
    }

    private IEnumerator PhaseSystemSettles(Vector3 center, float planetRadius, SolarSystemDef solarSystem)
    {
        float duration = EraTimeline.Phases[EraTimeline.IntroStartIndex + 3].DurationSeconds;
        float lifeDist = Mathf.Sqrt(Mathf.Max(solarSystem.LifePlanetOrbitAU, 0.01f)) * VisualOrbitScale;

        Vector3 wideShot = center + Vector3.back * (lifeDist * 1.4f + 60f) + Vector3.up * 20f;
        // Close shot still frames the small INTRO-scale planet, not the eventual full size.
        Vector3 closeShot = center + Vector3.back * (IntroDisplayRadius * 6f) + Vector3.up * (IntroDisplayRadius * 1.5f);

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float half = duration * 0.5f;
            if (t < half)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / half);
                _cam.transform.position = Vector3.Lerp(_cam.transform.position, wideShot, k * 0.3f + 0.05f);
            }
            else
            {
                float k = Mathf.SmoothStep(0f, 1f, (t - half) / half);
                _cam.transform.position = Vector3.Lerp(wideShot, closeShot, k);
            }
            _cam.transform.LookAt(center);
            yield return null;
        }
        _cam.transform.position = closeShot;
        _cam.transform.LookAt(center);
    }

    private IEnumerator PhaseMoltenBombardment(Vector3 center)
    {
        float duration = EraTimeline.Phases[EraTimeline.AbioticStartIndex + 0].DurationSeconds;

        // Keep the planet at IntroDisplayRadius scale for the entire cinematic — it only
        // jumps to Vector3.one (game scale) in onComplete, after the camera has already
        // transitioned to the orbit-camera. Growing to full scale here made the planet
        // 20-unit radius while the cinematic star was only ~7 units, so the planet
        // dwarfed its own star for the rest of the cinematic.
        const float growDuration = 0.08f;
        Vector3 startScale = _lifePlanetGo.transform.localScale;
        Vector3 fullScale = Vector3.one * (IntroDisplayRadius / _planetRadius); // stay at intro scale
        Vector3 startCamPos = _cam.transform.position;
        // Camera distance also in intro-scale space so the planet fills the frame the same
        // way it did before, just at the smaller visual radius.
        Vector3 closeCamPos = center + Vector3.back * (IntroDisplayRadius * 2.5f) + Vector3.up * (IntroDisplayRadius * 0.5f);

        float gt = 0f;
        while (gt < growDuration)
        {
            gt += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, gt / growDuration);
            _lifePlanetGo.transform.localScale = Vector3.Lerp(startScale, fullScale, k);
            _cam.transform.position = Vector3.Lerp(startCamPos, closeCamPos, k);
            _cam.transform.LookAt(center);
            yield return null;
        }
        _lifePlanetGo.transform.localScale = fullScale;

        float t = 0f;
        float nextFlash = Random.Range(0.5f, 1.5f);
        duration = Mathf.Max(0f, duration - growDuration);
        while (t < duration)
        {
            t += Time.deltaTime;
            if (t >= nextFlash)
            {
                nextFlash = t + Random.Range(0.6f, 1.6f);
                StartCoroutine(ImpactFlash(center));
            }
            _cam.transform.RotateAround(center, Vector3.up, 6f * Time.deltaTime);
            _cam.transform.LookAt(center);
            yield return null;
        }
    }

    private IEnumerator ImpactFlash(Vector3 center)
    {
        GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(flash.GetComponent<Collider>());
        Vector3 dir = Random.onUnitSphere;
        flash.transform.position = center + dir * (IntroDisplayRadius * 1.6f);
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = Color.white;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(1f, 0.6f, 0.2f) * 4f);
        flash.GetComponent<Renderer>().material = mat;

        float t = 0f;
        const float dur = 0.4f;
        while (t < dur)
        {
            t += Time.deltaTime;
            flash.transform.localScale = Vector3.one * Mathf.Lerp(2f, 0f, t / dur);
            yield return null;
        }
        Destroy(flash);
    }

    /// Lerps the planet mesh between two precomputed snapshots over one EraTimeline
    /// phase's duration - used for "Crust formation & cooling" (molten -> terrain) and
    /// "Condensation & water arrival" (terrain -> flooded).
    private IEnumerator LerpPhase(int phaseIndex, PlanetTileMesh.MeshData from, PlanetTileMesh.MeshData to, Vector3 center)
    {
        float duration = EraTimeline.Phases[phaseIndex].DurationSeconds;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            LerpMeshDataInto(from, to, k);
            _lifePlanetMesh.SetVertices(_lerpVerts);
            _lifePlanetMesh.SetColors(_lerpColors);
            _lifePlanetMesh.RecalculateNormals();
            _lifePlanetMesh.RecalculateBounds();

            _cam.transform.RotateAround(center, Vector3.up, 6f * Time.deltaTime);
            _cam.transform.LookAt(center);
            yield return null;
        }
        _lifePlanetMesh.SetVertices(to.Vertices);
        _lifePlanetMesh.SetColors(to.Colors);
        _lifePlanetMesh.RecalculateNormals();
        _lifePlanetMesh.RecalculateBounds();
    }

    /// Fades the liquid shell in from fully transparent to its real opacity - basins
    /// fill, first seas appear - while the camera keeps orbiting to show it pooling.
    private IEnumerator PhaseCondensation(int phaseIndex, Vector3 center)
    {
        float duration = EraTimeline.Phases[phaseIndex].DurationSeconds;

        if (!_hasLiquid)
        {
            float wt = 0f;
            while (wt < duration) { wt += Time.deltaTime; _cam.transform.RotateAround(center, Vector3.up, 6f * Time.deltaTime); _cam.transform.LookAt(center); yield return null; }
            yield break;
        }

        _liquidGo.SetActive(true);
        Color[] fullColors = _liquidShellData.Colors;
        var fadeColors = new Color[fullColors.Length];

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            for (int i = 0; i < fullColors.Length; i++)
            {
                Color c = fullColors[i];
                c.a *= k;
                fadeColors[i] = c;
            }
            _liquidMesh.SetColors(fadeColors);

            _cam.transform.RotateAround(center, Vector3.up, 6f * Time.deltaTime);
            _cam.transform.LookAt(center);
            yield return null;
        }
        _liquidMesh.SetColors(fullColors);
    }

    private IEnumerator PhaseTopographyReveal(Vector3 center, float planetRadius)
    {
        float duration = EraTimeline.Phases[EraTimeline.AbioticStartIndex + 3].DurationSeconds;
        Vector3 startPos = _cam.transform.position;
        // Use IntroDisplayRadius not planetRadius — planet is still at intro scale here.
        Vector3 revealPos = center + Vector3.back * (IntroDisplayRadius * 2.2f) + Vector3.up * (IntroDisplayRadius * 0.9f);

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / duration);
            _cam.transform.position = Vector3.Lerp(startPos, revealPos, k);
            _cam.transform.LookAt(center);
            yield return null;
        }
    }

    private void LerpMeshDataInto(PlanetTileMesh.MeshData a, PlanetTileMesh.MeshData b, float t)
    {
        t = Mathf.Clamp01(t);
        for (int i = 0; i < _lerpVerts.Length; i++)
        {
            _lerpVerts[i] = Vector3.Lerp(a.Vertices[i], b.Vertices[i], t);
            _lerpColors[i] = Color.Lerp(a.Colors[i], b.Colors[i], t);
        }
    }

    // ── Cell Division Cinematic ───────────────────────────────────────────────
    // Plays at the very end of the genesis montage: camera zooms in to the primordial
    // ocean surface and we watch a single ancestor cell divide 8 times to produce 8
    // founding communities, all at the same location (no locomotion yet). Each of the
    // 8 then divides 2-3 more times so the communities are visually seeded before
    // SimulationBootstrap spawns the real agents.
    //
    // Planet is still at IntroDisplayRadius scale here (not full gameplay scale).

    /// Brief camera orbit over the planet surface. Calls _onComplete after, which triggers
    /// SimulationBootstrap to spawn the real game agents — so no fake cells are needed.
    private IEnumerator PhaseFirstLifeEmerges(Vector3 center, float planetRadius)
    {
        const float duration    = 5f;
        const float orbitRadius = 1.6f; // multiples of IntroDisplayRadius
        float introR = IntroDisplayRadius;

        // Pull camera to a low orbit angle above the surface.
        Vector3 camStart = _cam.transform.position;
        Vector3 orbitStart = center + new Vector3(0f, introR * 0.4f, -introR * orbitRadius);
        float pullT = 0f;
        const float pullDuration = 1.2f;
        while (pullT < pullDuration)
        {
            pullT += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, pullT / pullDuration);
            _cam.transform.position = Vector3.Lerp(camStart, orbitStart, k);
            _cam.transform.LookAt(center);
            yield return null;
        }

        // Gentle orbit around the planet — real organisms will appear here after _onComplete.
        float orbitT = 0f;
        float remainingDuration = duration - pullDuration;
        while (orbitT < remainingDuration)
        {
            orbitT += Time.deltaTime;
            float angle = orbitT / remainingDuration * 60f * Mathf.Deg2Rad; // 60° arc
            float x = Mathf.Sin(angle) * introR * orbitRadius;
            float z = -Mathf.Cos(angle) * introR * orbitRadius;
            _cam.transform.position = center + new Vector3(x, introR * 0.35f, z);
            _cam.transform.LookAt(center);
            yield return null;
        }
    }

}
