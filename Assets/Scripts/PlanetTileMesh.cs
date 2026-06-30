using System.Collections.Generic;
using UnityEngine;

/// Builds the flat-shaded planet mesh from a TectonicResult: vertex radius is bowed by
/// real per-vertex tectonic elevation (not a generic noise sphere), face color comes
/// from rock type blended with the planet's rock archetype. Terrain always shows its
/// TRUE elevation, including below sea level (a real lake/ocean bed) - liquid is a
/// SEPARATE translucent shell mesh (BuildLiquidShellData) layered above it, not a
/// recoloring/flattening of the terrain itself, so it actually reads as fluid.
public static class PlanetTileMesh
{
    /// Raw per-(duplicated)-vertex arrays, same topology/ordering for any two calls
    /// against the same TectonicResult - GenesisCinematic lerps between two MeshData
    /// snapshots (e.g. smooth sphere -> full terrain) frame by frame.
    public struct MeshData
    {
        public Vector3[] Vertices;
        public Color[] Colors;
        public int[] Triangles;
    }

    public static MeshData BuildData(TectonicResult tectonics, float radius,
        float elevationWorldScale, RockArchetypeDef archetype)
    {
        List<Vector3> unitVerts = tectonics.UnitVerts;
        List<int> triangles = tectonics.Triangles;

        var flatVerts = new Vector3[triangles.Count];
        var flatColors = new Color[triangles.Count];
        var flatTriangles = new int[triangles.Count];

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];

            (Vector3 a, Color colorA) = VertexWorld(unitVerts[ia], tectonics, ia, radius, elevationWorldScale, archetype);
            (Vector3 b, Color colorB) = VertexWorld(unitVerts[ib], tectonics, ib, radius, elevationWorldScale, archetype);
            (Vector3 c, Color colorC) = VertexWorld(unitVerts[ic], tectonics, ic, radius, elevationWorldScale, archetype);

            Color faceColor = (colorA + colorB + colorC) / 3f;

            flatVerts[i] = a; flatVerts[i + 1] = b; flatVerts[i + 2] = c;
            flatColors[i] = faceColor; flatColors[i + 1] = faceColor; flatColors[i + 2] = faceColor;
            flatTriangles[i] = i; flatTriangles[i + 1] = i + 1; flatTriangles[i + 2] = i + 2;
        }

        return new MeshData { Vertices = flatVerts, Colors = flatColors, Triangles = flatTriangles };
    }

    /// Builds a SEPARATE mesh covering only the faces below sea level, flattened to a
    /// constant sea-level radius (plus a tiny epsilon so it doesn't z-fight with the
    /// terrain dipping below it) and colored with the liquid's translucent color - a
    /// real lake/ocean surface hovering over a submerged lake-bed, not a recolored patch
    /// of the same opaque terrain mesh. This is the one-time genesis build (static flood
    /// fill at a single percentile sea level) - see BuildLiquidShellDataFromVolume for the
    /// rebuildable, per-vertex-volume-driven version used by FluidDynamicsManager.
    public static MeshData BuildLiquidShellData(TectonicResult tectonics, float radius,
        float elevationWorldScale, float seaLevelElevation, LiquidDef liquid, float liquidTempK)
    {
        List<Vector3> unitVerts = tectonics.UnitVerts;
        List<int> triangles = tectonics.Triangles;
        Color liquidColor = liquid.ColorAt(liquidTempK);
        float shellRadius = radius + seaLevelElevation * elevationWorldScale + 0.05f;

        var verts = new List<Vector3>(triangles.Count / 2);
        var colors = new List<Color>(triangles.Count / 2);
        var outTriangles = new List<int>(triangles.Count / 2);

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];
            float avgElevation = (tectonics.Elevation[ia] + tectonics.Elevation[ib] + tectonics.Elevation[ic]) / 3f;
            if (avgElevation >= seaLevelElevation) continue; // dry face - no liquid here

            int baseIndex = verts.Count;
            verts.Add(unitVerts[ia] * shellRadius);
            verts.Add(unitVerts[ib] * shellRadius);
            verts.Add(unitVerts[ic] * shellRadius);
            colors.Add(liquidColor); colors.Add(liquidColor); colors.Add(liquidColor);
            outTriangles.Add(baseIndex); outTriangles.Add(baseIndex + 1); outTriangles.Add(baseIndex + 2);
        }

        return new MeshData { Vertices = verts.ToArray(), Colors = colors.ToArray(), Triangles = outTriangles.ToArray() };
    }

    /// Rebuildable variant driven by a live per-vertex liquid VOLUME array (FluidDynamicsManager's
    /// simulation state) instead of a one-time static sea-level percentile. A face is included
    /// whenever at least one of its vertices holds liquid above `minVolumeToRender`; each
    /// included vertex's shell radius is the terrain radius at that vertex PLUS its own liquid
    /// depth (volume scaled by elevationWorldScale), so puddles/lakes/oceans read as locally
    /// varying depth rather than one flat global sea level. Vertices below the volume threshold
    /// that still belong to an included face are pinned to the terrain radius (dry edge of a
    /// wet face), avoiding a floating shell over dry land.
    public static MeshData BuildLiquidShellDataFromVolume(TectonicResult tectonics, float radius,
        float elevationWorldScale, float[] liquidVolume, float minVolumeToRender, LiquidDef liquid, float liquidTempK)
    {
        List<Vector3> unitVerts = tectonics.UnitVerts;
        List<int> triangles = tectonics.Triangles;
        Color liquidColor = liquid.ColorAt(liquidTempK);
        const float epsilon = 0.05f;

        var verts = new List<Vector3>(triangles.Count / 2);
        var colors = new List<Color>(triangles.Count / 2);
        var outTriangles = new List<int>(triangles.Count / 2);

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];
            float va = liquidVolume[ia], vb = liquidVolume[ib], vc = liquidVolume[ic];
            if (va < minVolumeToRender && vb < minVolumeToRender && vc < minVolumeToRender) continue; // fully dry face

            int baseIndex = verts.Count;
            verts.Add(ShellVertex(unitVerts[ia], tectonics.Elevation[ia], va, radius, elevationWorldScale, minVolumeToRender, epsilon));
            verts.Add(ShellVertex(unitVerts[ib], tectonics.Elevation[ib], vb, radius, elevationWorldScale, minVolumeToRender, epsilon));
            verts.Add(ShellVertex(unitVerts[ic], tectonics.Elevation[ic], vc, radius, elevationWorldScale, minVolumeToRender, epsilon));
            colors.Add(liquidColor); colors.Add(liquidColor); colors.Add(liquidColor);
            outTriangles.Add(baseIndex); outTriangles.Add(baseIndex + 1); outTriangles.Add(baseIndex + 2);
        }

        return new MeshData { Vertices = verts.ToArray(), Colors = colors.ToArray(), Triangles = outTriangles.ToArray() };
    }

    private static Vector3 ShellVertex(Vector3 unitVert, float terrainElevation, float liquidVolume,
        float radius, float elevationWorldScale, float minVolumeToRender, float epsilon)
    {
        float depth = liquidVolume >= minVolumeToRender ? liquidVolume : 0f;
        float shellRadius = radius + terrainElevation * elevationWorldScale + depth * elevationWorldScale + epsilon;
        return unitVert * shellRadius;
    }

    public static Mesh ToMesh(MeshData data)
    {
        var mesh = new Mesh();
        mesh.indexFormat = data.Vertices.Length > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(data.Vertices);
        mesh.SetColors(data.Colors);
        mesh.SetTriangles(data.Triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Mesh Build(TectonicResult tectonics, float radius, Vector3 planetCenter,
        float elevationWorldScale, RockArchetypeDef archetype)
    {
        return ToMesh(BuildData(tectonics, radius, elevationWorldScale, archetype));
    }

    private static (Vector3 worldPos, Color color) VertexWorld(
        Vector3 unitVert, TectonicResult tectonics, int vertexIndex, float radius, float elevationWorldScale, RockArchetypeDef archetype)
    {
        float terrainElevation = tectonics.Elevation[vertexIndex];
        float worldRadius = radius + terrainElevation * elevationWorldScale;
        Vector3 worldPos = unitVert * worldRadius;
        Color color = RockColor(tectonics.RockTypeAtVertex[vertexIndex], archetype, terrainElevation);
        return (worldPos, color);
    }

    private static Color RockColor(RockType type, RockArchetypeDef archetype, float elevation)
    {
        Color baseColor = type switch
        {
            RockType.IgneousMafic => new Color(0.16f, 0.17f, 0.19f),
            RockType.IgneousFelsic => new Color(0.72f, 0.68f, 0.62f),
            RockType.Metamorphic => new Color(0.45f, 0.4f, 0.48f),
            RockType.Sedimentary => new Color(0.78f, 0.66f, 0.48f),
            _ => Color.gray,
        };

        Color blended = Color.Lerp(baseColor, archetype.Primary, 0.2f);
        blended = Color.Lerp(blended, archetype.Accent, 0.12f);

        // Slight elevation tint: peaks read lighter (exposed/weathered rock), basins darker.
        float tint = Mathf.Clamp(elevation, -0.5f, 1f) * 0.08f;
        return new Color(
            Mathf.Clamp01(blended.r + tint),
            Mathf.Clamp01(blended.g + tint),
            Mathf.Clamp01(blended.b + tint));
    }

    /// Returns the elevation value below which `percentile` fraction of vertices fall -
    /// used as the flood level so liquids pool into the lowest terrain rather than an
    /// arbitrary fixed height.
    public static float ComputeSeaLevel(TectonicResult tectonics, float percentile)
    {
        var sorted = (float[])tectonics.Elevation.Clone();
        System.Array.Sort(sorted);
        int index = Mathf.Clamp(Mathf.RoundToInt(sorted.Length * percentile), 0, sorted.Length - 1);
        return sorted[index];
    }

    /// Picks a random vertex index whose elevation is below sea level - used to place
    /// the founding colony literally inside a flooded region instead of anywhere on
    /// the sphere. Returns -1 if no vertex qualifies (shouldn't happen when liquid != null,
    /// since seaLevelElevation is itself derived from the elevation distribution).
    public static int PickRandomFloodedVertex(TectonicResult tectonics, float seaLevelElevation)
    {
        var candidates = new List<int>();
        for (int v = 0; v < tectonics.Elevation.Length; v++)
            if (tectonics.Elevation[v] < seaLevelElevation) candidates.Add(v);
        if (candidates.Count == 0) return -1;
        return candidates[Random.Range(0, candidates.Count)];
    }
}
