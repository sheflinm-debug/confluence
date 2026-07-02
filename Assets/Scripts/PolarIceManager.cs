using System.Collections.Generic;
using UnityEngine;

/// Renders an ice/frost overlay on terrain faces where ClimateManager returns a
/// temperature below the freeze threshold — naturally cold at the poles after
/// the latitude-gradient fix, and shifting with OrbitalSeasons when axial tilt is
/// non-zero. Uses the same vertex-color overlay pattern as StormVisualManager so
/// no new shaders are needed.
///
/// Rebuilt on a slow tick (every 12 real seconds) because seasonal temperature
/// shifts are gradual; no need to rebuild every frame.
public class PolarIceManager : MonoBehaviour
{
    public static PolarIceManager Instance { get; private set; }

    [Tooltip("ClimateManager temperature (0-100) below which a face is considered frozen.")]
    public float freezeThreshold = 25f;

    [Tooltip("How much the ice overlay is raised above the terrain surface (world units).")]
    public float iceShellOffset = 0.08f;

    [Tooltip("Real-time seconds between ice-cap mesh rebuilds. Seasonal shift is slow.")]
    public float rebuildInterval = 45f;

    private TectonicResult _tectonics;
    private Vector3 _planetCenter;
    private float _planetRadius;
    private float _elevationWorldScale;
    private Transform _parent;

    private GameObject _iceGo;
    private MeshFilter _iceMeshFilter;
    private Mesh _iceMesh;
    private float _rebuildTimer;

    // Glacier white with a faint blue tint, slightly transparent.
    private static readonly Color IceColor = new Color(0.92f, 0.96f, 1.0f, 0.88f);

    // Actual planetary temperature in Kelvin, set at Init from PlanetTemperature.
    // Ice is only possible below this cutoff — hot volcanic worlds have none.
    // Above 340K: too warm for water ice even at the poles (slight margin above 273K
    // to account for pressure/composition). Below 200K: maximum ice coverage.
    private float _planetTempK = 280f;

    public void Init(TectonicResult tectonics, Vector3 planetCenter, float planetRadius,
        float elevationWorldScale, Transform parent, float planetTempK = 280f)
    {
        Instance = this;
        _tectonics = tectonics;
        _planetCenter = planetCenter;
        _planetRadius = planetRadius;
        _elevationWorldScale = elevationWorldScale;
        _parent = parent;
        _planetTempK = planetTempK;

        Shader shader = Shader.Find("Custom/VertexColorTransparentURP");
        Material mat = new Material(shader);

        _iceGo = new GameObject("PolarIce");
        _iceGo.transform.SetParent(_parent, worldPositionStays: true);
        _iceMeshFilter = _iceGo.AddComponent<MeshFilter>();
        MeshRenderer mr = _iceGo.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        _iceMesh = new Mesh();
        _iceMeshFilter.mesh = _iceMesh;

        // Build immediately so ice caps appear at game start.
        RebuildIceMesh();
        _rebuildTimer = rebuildInterval;
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void Update()
    {
        _rebuildTimer += Time.deltaTime;
        if (_rebuildTimer >= rebuildInterval)
        {
            _rebuildTimer = 0f;
            RebuildIceMesh();
        }
    }

    private void RebuildIceMesh()
    {
        if (_tectonics == null || _iceMesh == null) return;

        // Scale the abstract freeze threshold by actual planetary temperature.
        // Above 340K: no ice possible (too hot for water ice even at poles).
        // Below 200K: full threshold — arctic world, ice forms readily.
        // Between: linear scale so warmer worlds have thinner/smaller polar caps.
        float tempFactor = Mathf.Clamp01(1f - (_planetTempK - 200f) / 140f);
        float effectiveThreshold = freezeThreshold * tempFactor;
        if (effectiveThreshold <= 0.5f)
        {
            // Planet is too hot for any ice — hide the overlay entirely.
            _iceGo.SetActive(false);
            return;
        }

        var unitVerts = _tectonics.UnitVerts;
        var triangles = _tectonics.Triangles;

        var verts  = new List<Vector3>();
        var colors = new List<Color>();
        var tris   = new List<int>();

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];

            // Use pure latitude for ice boundary — ignore weather/storm temperature
            // modifiers so storms can't make polar caps visibly pulse. Ice is a
            // geological-timescale feature, not a weather feature.
            Vector3 centroidUnit = (unitVerts[ia] + unitVerts[ib] + unitVerts[ic]).normalized;
            float latitudeSin = Mathf.Abs(centroidUnit.y); // 0=equator, 1=pole
            // Map latitude to abstract 0-100 temperature: equator ~noise-driven,
            // poles cold by the same 45-unit gradient ClimateManager uses —
            // but without the weather noise so the boundary stays stable.
            float latTemp = 50f - latitudeSin * latitudeSin * 45f;

            // Add only seasonal orbital flux (slow, planet-scale) — not storm noise.
            float seasonalDelta = 0f;
            if (OrbitalSeasons.Instance != null && OrbitalSeasons.Instance.AxialTiltDeg > 0.001f)
            {
                float fluxMul = OrbitalSeasons.Instance.FluxMultiplier;
                float seasonalMul = OrbitalSeasons.Instance.SeasonalExposureMultiplier(centroidUnit.y);
                seasonalDelta = (fluxMul * seasonalMul - 1f) * 20f;
            }

            float temp = Mathf.Clamp(latTemp + seasonalDelta, 0f, 100f);
            if (temp >= effectiveThreshold) continue;

            // Fade opacity as temperature approaches the threshold.
            float freeze = 1f - Mathf.SmoothStep(0f, effectiveThreshold, temp);
            Color c = new Color(IceColor.r, IceColor.g, IceColor.b, IceColor.a * freeze);

            // Shell slightly above terrain to avoid z-fighting with the terrain mesh.
            int baseIdx = verts.Count;
            verts.Add(unitVerts[ia] * ShellR(ia));
            verts.Add(unitVerts[ib] * ShellR(ib));
            verts.Add(unitVerts[ic] * ShellR(ic));
            colors.Add(c); colors.Add(c); colors.Add(c);
            tris.Add(baseIdx); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
        }

        _iceMesh.Clear();
        if (verts.Count == 0) { _iceGo.SetActive(false); return; }
        _iceGo.SetActive(true);

        _iceMesh.indexFormat = verts.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        _iceMesh.SetVertices(verts);
        _iceMesh.SetColors(colors);
        _iceMesh.SetTriangles(tris, 0);
        _iceMesh.RecalculateNormals();
        _iceMesh.RecalculateBounds();
    }

    private float ShellR(int vertexIndex)
    {
        return _planetRadius + _tectonics.Elevation[vertexIndex] * _elevationWorldScale + iceShellOffset;
    }
}
