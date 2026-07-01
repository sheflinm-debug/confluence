using System.Collections.Generic;
using UnityEngine;

/// Purely visual layer on top of WeatherManager's storm simulation: for each active
/// StormCell this spawns (a) a transparent overlay mesh that darkens terrain faces
/// inside the storm radius (cloud-shadow feel) and (b) a ParticleSystem that rains
/// inward toward the planet center. Both are cheap - overlay meshes are rebuilt on
/// WeatherManager's coarse 0.5 s tick, particles are capped per storm.
///
/// Wire up via SimulationBootstrap's onComplete callback, same pattern as AtmosphereVisual.
/// Does NOT modify WeatherManager or ClimateManager - purely additive visuals.
public class StormVisualManager : MonoBehaviour
{
    public static StormVisualManager Instance { get; private set; }

    // How far above the terrain shell to float the cloud-shadow overlay (world units).
    // Enough to clear volcanic peaks while still reading as "on the surface."
    private const float OverlayShellOffset = 0.2f;

    // Max alpha at full storm intensity. Darker so cloud shadows are clearly visible
    // against the terrain even at a distance (raised from 0.42).
    private const float MaxOverlayAlpha = 0.65f;

    // How fast the per-storm alpha fades in/out (units per second).
    private const float AlphaFadeSpeed = 0.35f;

    private class StormVisual
    {
        public WeatherManager.StormCell Storm;
        public GameObject OverlayGo;
        public MeshFilter OverlayFilter;
        public GameObject ParticleGo;
        public ParticleSystem Particles;
        public float CurrentAlpha;
    }

    private TectonicResult _tectonics;
    private Vector3 _planetCenter;
    private float _planetRadius;
    private float _elevationWorldScale;
    private Transform _parent;
    private Material _overlayMaterial;
    private Material _particleMaterial;

    private readonly List<StormVisual> _visuals = new List<StormVisual>();
    private float _tickTimer;
    private const float TickInterval = 0.5f;

    public void Init(TectonicResult tectonics, Vector3 planetCenter, float planetRadius,
        float elevationWorldScale, Transform parent)
    {
        Instance = this;
        _tectonics = tectonics;
        _planetCenter = planetCenter;
        _planetRadius = planetRadius;
        _elevationWorldScale = elevationWorldScale;
        _parent = parent;

        Shader shader = Shader.Find("Custom/VertexColorTransparentURP");
        _overlayMaterial = new Material(shader);

        // Rain particle material: use a URP-compatible particle shader so it renders
        // translucent blue-gray instead of the magenta/pink that Unity's built-in
        // Default-Particle material produces under URP (wrong render pipeline path).
        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (particleShader == null) particleShader = Shader.Find("Sprites/Default");
        if (particleShader == null) particleShader = Shader.Find("Unlit/Color");
        _particleMaterial = particleShader != null ? new Material(particleShader) : null;
        if (_particleMaterial != null)
            _particleMaterial.color = new Color(0.55f, 0.75f, 0.95f, 0.7f);
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void Update()
    {
        if (WeatherManager.Instance == null) return;

        // Smooth alpha every frame regardless of tick.
        foreach (var v in _visuals)
        {
            float target = v.Storm.Intensity * MaxOverlayAlpha;
            v.CurrentAlpha = Mathf.MoveTowards(v.CurrentAlpha, target, AlphaFadeSpeed * Time.deltaTime);
        }

        _tickTimer += Time.deltaTime;
        if (_tickTimer < TickInterval) return;
        _tickTimer = 0f;

        SyncVisualSet();

        foreach (var v in _visuals)
        {
            RebuildOverlayMesh(v);
            SyncParticles(v);
        }
    }

    // Ensures _visuals matches the current WeatherManager.ActiveStorms list —
    // adds visuals for newly spawned storms, removes them for dead storms.
    private void SyncVisualSet()
    {
        var activeStorms = WeatherManager.Instance.ActiveStorms;

        // Remove dead storms.
        for (int i = _visuals.Count - 1; i >= 0; i--)
        {
            bool alive = false;
            for (int j = 0; j < activeStorms.Count; j++)
                if (activeStorms[j] == _visuals[i].Storm) { alive = true; break; }
            if (alive) continue;
            Destroy(_visuals[i].OverlayGo);
            Destroy(_visuals[i].ParticleGo);
            _visuals.RemoveAt(i);
        }

        // Add new storms.
        for (int j = 0; j < activeStorms.Count; j++)
        {
            var storm = activeStorms[j];
            bool exists = false;
            for (int i = 0; i < _visuals.Count; i++)
                if (_visuals[i].Storm == storm) { exists = true; break; }
            if (!exists)
                _visuals.Add(CreateVisual(storm));
        }
    }

    private StormVisual CreateVisual(WeatherManager.StormCell storm)
    {
        // --- Overlay mesh ---
        GameObject overlayGo = new GameObject("StormOverlay");
        overlayGo.transform.SetParent(_parent, worldPositionStays: true);
        MeshFilter mf = overlayGo.AddComponent<MeshFilter>();
        MeshRenderer mr = overlayGo.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _overlayMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mf.mesh = new Mesh();

        // --- Rain particle system ---
        GameObject particleGo = new GameObject("StormRain");
        particleGo.transform.SetParent(_parent, worldPositionStays: true);
        particleGo.transform.position = storm.Position;
        ParticleSystem ps = particleGo.AddComponent<ParticleSystem>();
        ConfigureParticleSystem(ps, storm);

        return new StormVisual
        {
            Storm = storm,
            OverlayGo = overlayGo,
            OverlayFilter = mf,
            ParticleGo = particleGo,
            Particles = ps,
            CurrentAlpha = 0f,
        };
    }

    // Rebuilds the overlay mesh from all terrain faces whose arc-distance from the storm
    // center is within the storm's radius. Uses the same approach as
    // PlanetTileMesh.BuildLiquidShellData: a separate mesh at a fixed shell radius above
    // the terrain so it never z-fights, with vertex-color alpha encoding the spatial falloff.
    private void RebuildOverlayMesh(StormVisual visual)
    {
        var storm = visual.Storm;
        var unitVerts = _tectonics.UnitVerts;
        var triangles = _tectonics.Triangles;

        // Cloud shadow color: dark gray-blue. Alpha baked per-vertex (falloff * current alpha).
        const float r = 0.16f, g = 0.20f, b = 0.34f;

        Vector3 stormNormal = (storm.Position - _planetCenter).normalized;

        var verts = new List<Vector3>();
        var colors = new List<Color>();
        var tris = new List<int>();

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];

            // Check face centroid (unit-sphere direction) against storm radius.
            Vector3 centroidDir = (unitVerts[ia] + unitVerts[ib] + unitVerts[ic]).normalized;
            float angRad = Mathf.Acos(Mathf.Clamp(Vector3.Dot(stormNormal, centroidDir), -1f, 1f));
            float arcDist = angRad * _planetRadius;
            if (arcDist >= storm.RadiusWorld) continue;

            float falloff = 1f - Mathf.SmoothStep(0f, 1f, arcDist / storm.RadiusWorld);
            float alpha = visual.CurrentAlpha * falloff;

            // Each face gets its own three vertices (flat-shaded, matching terrain mesh style).
            // Shell radius: above per-vertex terrain elevation + fixed offset.
            int base_ = verts.Count;
            verts.Add(unitVerts[ia] * ShellR(ia));
            verts.Add(unitVerts[ib] * ShellR(ib));
            verts.Add(unitVerts[ic] * ShellR(ic));

            Color c = new Color(r, g, b, alpha);
            colors.Add(c); colors.Add(c); colors.Add(c);
            tris.Add(base_); tris.Add(base_ + 1); tris.Add(base_ + 2);
        }

        Mesh mesh = visual.OverlayFilter.mesh;
        mesh.Clear();
        if (verts.Count == 0) return;

        mesh.indexFormat = verts.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    private float ShellR(int vertexIndex)
    {
        return _planetRadius + _tectonics.Elevation[vertexIndex] * _elevationWorldScale + OverlayShellOffset;
    }

    // Moves the particle emitter to the storm's current surface position and updates
    // emission rate + inward gravity to match storm intensity and direction.
    private void SyncParticles(StormVisual visual)
    {
        var storm = visual.Storm;

        // Position emitter slightly above surface in the storm's outward direction.
        Vector3 outward = (storm.Position - _planetCenter).normalized;
        visual.ParticleGo.transform.position = storm.Position + outward * 0.3f;

        // Scale emission rate with intensity: 0 at birth/death, up to 35 drops/sec at peak.
        var emission = visual.Particles.emission;
        emission.rateOverTime = storm.Intensity * 60f;

        // Keep particle color in sync with the fluid (temperature can shift it over time).
        ApplyLiquidColorToParticles(visual.Particles);

        // Pull particles inward toward planet center ("rain falls down toward the planet").
        // Updated each tick so it tracks the storm as it drifts around the sphere.
        Vector3 inward = -outward;
        var force = visual.Particles.forceOverLifetime;
        force.x = new ParticleSystem.MinMaxCurve(inward.x * 6f);
        force.y = new ParticleSystem.MinMaxCurve(inward.y * 6f);
        force.z = new ParticleSystem.MinMaxCurve(inward.z * 6f);
    }

    private void ConfigureParticleSystem(ParticleSystem ps, WeatherManager.StormCell storm)
    {
        // Main: small, short-lived, translucent streaks.
        var main = ps.main;
        main.maxParticles = 120;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
        // startSpeed is positive = launches in the EMISSION direction. The emitter
        // disc is oriented so its normal points INWARD (toward planet center), so
        // particles immediately fall toward the surface rather than erupting outward.
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
        // Larger particles so they're visible from orbit-camera distance (~90 units).
        main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.26f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.55f, 0.70f, 0.95f, 0.60f),
            new Color(0.80f, 0.90f, 1.0f, 0.90f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f; // handled via forceOverLifetime

        // Emission: set to 0 initially, SyncParticles drives it from intensity.
        var emission = ps.emission;
        emission.rateOverTime = 0f;

        // Emitter disc is above the storm surface, oriented so its normal points
        // INWARD (toward planet center). startSpeed launches particles directly
        // toward the surface — no outward burst, no fountain/volcano effect.
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = Mathf.Max(0.5f, storm.RadiusWorld * 0.35f);
        shape.radiusThickness = 1f;
        // LookRotation(inward) → emitter normal faces planet center → particles launch inward.
        Vector3 outward = (storm.Position - _planetCenter).normalized;
        shape.rotation = Quaternion.LookRotation(-outward, Vector3.up).eulerAngles;

        // ForceOverLifetime: extra inward acceleration (gravity), updated each tick.
        var force = ps.forceOverLifetime;
        force.enabled = true;
        force.space = ParticleSystemSimulationSpace.World;

        // Stretch particles along their velocity to look like rain streaks.
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.12f;
        renderer.lengthScale = 1.2f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Apply URP-compatible particle material (built in Init). The built-in
        // Default-Particle material renders magenta/pink under URP, so we must
        // explicitly assign one that uses a URP particle shader.
        if (_particleMaterial != null)
            renderer.sharedMaterial = _particleMaterial;

        // Seed the startColor from the current fluid color (SyncParticles keeps it live).
        ApplyLiquidColorToParticles(ps);
    }

    /// Tints rain particles to match the world's fluid color, slightly brightened so
    /// droplets read as lighter/airborne rather than the same saturation as the pooled sea.
    private void ApplyLiquidColorToParticles(ParticleSystem ps)
    {
        Color fluidColor = FluidDynamicsManager.Instance != null
            ? FluidDynamicsManager.Instance.CurrentLiquidColor
            : new Color(0.06f, 0.22f, 0.5f, 0.9f);

        Color.RGBToHSV(fluidColor, out float h, out float s, out float v);
        Color rainLow  = Color.HSVToRGB(h, s * 0.55f, Mathf.Min(v + 0.25f, 1f));
        Color rainHigh = Color.HSVToRGB(h, s * 0.35f, Mathf.Min(v + 0.45f, 1f));
        rainLow.a  = 0.55f;
        rainHigh.a = 0.85f;

        var main = ps.main;
        main.startColor = new ParticleSystem.MinMaxGradient(rainLow, rainHigh);

        if (_particleMaterial != null)
            _particleMaterial.color = Color.HSVToRGB(h, s * 0.45f, Mathf.Min(v + 0.35f, 1f));
    }

    // TODO wind visualization: sample WindManager.GetWind at ~20 surface positions and draw
    // short line-segment arrows that drift in wind direction. Skip for now — arrow rendering
    // would need a procedural line mesh or Debug.DrawLine (editor-only). Implement when
    // a LineRenderer-based approach is confirmed to be cheap enough at 20 sample points.
}
