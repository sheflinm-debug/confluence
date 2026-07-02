using System.Collections.Generic;
using UnityEngine;

/// Renders a static vertex-color overlay mesh showing mineral deposit patches on the
/// terrain surface. Built once at world-gen from MineralDepositLayer; never rebuilt
/// during gameplay (minerals are geological, not dynamic). Uses the same transparent
/// overlay mesh pattern as PolarIceManager and StormVisualManager.
///
/// Wire up via SimulationBootstrap after TectonicPlanetGenerator.Generate() and
/// MineralDepositTable.Generate() have both run.
public class MineralOverlayManager : MonoBehaviour
{
    public static MineralOverlayManager Instance { get; private set; }

    // Raised above terrain to avoid z-fighting. Slightly more than PolarIceManager's
    // 0.08 offset so mineral patches are visible even under thin ice.
    private const float ShellOffset = 0.05f;

    private TectonicResult _tectonics;
    private MineralDepositLayer _deposits;
    private float _planetRadius;
    private float _elevationWorldScale;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    public void Init(TectonicResult tectonics, MineralDepositLayer deposits,
        float planetRadius, float elevationWorldScale, Transform parent)
    {
        Instance = this;
        _tectonics = tectonics;
        _deposits = deposits;
        _planetRadius = planetRadius;
        _elevationWorldScale = elevationWorldScale;

        Shader shader = Shader.Find("Custom/VertexColorTransparentURP");
        Material mat = new Material(shader);

        GameObject go = new GameObject("MineralOverlay");
        go.transform.SetParent(parent, worldPositionStays: true);

        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        mf.mesh = BuildMesh(deposits);
    }

    /// Returns the mineral deposit and current richness at the terrain vertex closest to
    /// worldPoint, plus the rock type at that vertex. Returns null mineral if the vertex
    /// has no deposit. Used by InspectPopup to show surface composition.
    public (MineralDef mineral, float richness, RockType rockType) GetSurfaceAt(Vector3 worldPoint)
    {
        if (_tectonics == null || _deposits == null)
            return (null, 0f, RockType.IgneousMafic);

        Vector3 dir = (worldPoint - Vector3.zero).normalized; // planet is at origin
        float bestDot = float.MinValue;
        int bestIdx = 0;
        var unitVerts = _tectonics.UnitVerts;
        for (int i = 0; i < unitVerts.Count; i++)
        {
            float d = Vector3.Dot(unitVerts[i], dir);
            if (d > bestDot) { bestDot = d; bestIdx = i; }
        }

        VertexDeposit dep = _deposits.ByVertex[bestIdx];
        float richness = dep != null ? _deposits.RuntimeRichness[bestIdx] : 0f;
        RockType rock = _tectonics.RockTypeAtVertex != null ? _tectonics.RockTypeAtVertex[bestIdx] : RockType.IgneousMafic;
        return (dep?.Mineral, richness, rock);
    }

    private Mesh BuildMesh(MineralDepositLayer deposits)
    {
        var unitVerts = _tectonics.UnitVerts;
        var triangles = _tectonics.Triangles;
        var byVertex = deposits.ByVertex;

        var verts  = new List<Vector3>();
        var colors = new List<Color>();
        var tris   = new List<int>();

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];
            VertexDeposit da = byVertex[ia], db = byVertex[ib], dc = byVertex[ic];

            // Include face if at least one vertex has a deposit.
            if (da == null && db == null && dc == null) continue;

            // Face color: weighted average of whichever vertices have deposits.
            // Vertices with no deposit contribute zero weight so they don't dilute
            // the deposit color — they're just the dry edge of a deposit patch.
            Color faceColor = BlendFaceColor(da, db, dc);
            if (faceColor.a < 0.01f) continue;

            int baseIdx = verts.Count;
            verts.Add(unitVerts[ia] * ShellR(ia));
            verts.Add(unitVerts[ib] * ShellR(ib));
            verts.Add(unitVerts[ic] * ShellR(ic));
            colors.Add(faceColor); colors.Add(faceColor); colors.Add(faceColor);
            tris.Add(baseIdx); tris.Add(baseIdx + 1); tris.Add(baseIdx + 2);
        }

        if (verts.Count == 0) return new Mesh();

        var mesh = new Mesh();
        mesh.indexFormat = verts.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private float ShellR(int vertexIndex)
    {
        return _planetRadius + _tectonics.Elevation[vertexIndex] * _elevationWorldScale + ShellOffset;
    }

    private static Color BlendFaceColor(VertexDeposit da, VertexDeposit db, VertexDeposit dc)
    {
        Color sum = Color.clear;
        float totalWeight = 0f;

        AddDeposit(da, ref sum, ref totalWeight);
        AddDeposit(db, ref sum, ref totalWeight);
        AddDeposit(dc, ref sum, ref totalWeight);

        if (totalWeight < 0.001f) return Color.clear;

        Color result = sum / totalWeight;
        // Scale alpha: face is fully opaque where all three vertices have deposits,
        // fades toward edges where only one or two vertices do.
        result.a = Mathf.Clamp01((totalWeight / 3f) * result.a);
        return result;
    }

    private static void AddDeposit(VertexDeposit d, ref Color sum, ref float totalWeight)
    {
        if (d == null) return;
        float w = d.Richness;
        Color c = d.Mineral.Color;
        c.a *= d.Richness; // richness drives opacity
        sum += c * w;
        totalWeight += w;
    }
}
