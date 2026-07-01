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
    public float rebuildInterval = 12f;

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

        var unitVerts = _tectonics.UnitVerts;
        var triangles = _tectonics.Triangles;

        var verts  = new List<Vector3>();
        var colors = new List<Color>();
        var tris   = new List<int>();

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];

            // Evaluate temperature at face centroid (world position on sphere surface).
            Vector3 centroidUnit = (unitVerts[ia] + unitVerts[ib] + unitVerts[ic]).normalized;
            Vector3 centroidWorld = _planetCenter + centroidUnit * _planetRadius;
            float temp = ClimateManager.GetTemperature(centroidWorld);
            if (temp >= freezeThreshold) continue;

            // Fade opacity as temperature approaches the threshold, so the ice-line
            // has a soft edge rather than a hard geometric cut.
            float freeze = 1f - Mathf.SmoothStep(0f, freezeThreshold, temp);
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
