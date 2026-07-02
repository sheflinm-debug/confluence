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
        float elevationWorldScale, RockArchetypeDef archetype, float temperatureK = 400f)
    {
        List<Vector3> unitVerts = tectonics.UnitVerts;
        List<int> triangles = tectonics.Triangles;

        var flatVerts = new Vector3[triangles.Count];
        var flatColors = new Color[triangles.Count];
        var flatTriangles = new int[triangles.Count];

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];

            (Vector3 a, Color colorA) = VertexWorld(unitVerts[ia], tectonics, ia, radius, elevationWorldScale, archetype, temperatureK);
            (Vector3 b, Color colorB) = VertexWorld(unitVerts[ib], tectonics, ib, radius, elevationWorldScale, archetype, temperatureK);
            (Vector3 c, Color colorC) = VertexWorld(unitVerts[ic], tectonics, ic, radius, elevationWorldScale, archetype, temperatureK);

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
        const float shellLift = 0.08f; // small lift to beat z-fighting without creating edge spikes
        float shellRadius = radius + seaLevelElevation * elevationWorldScale + shellLift;

        var verts = new List<Vector3>(triangles.Count / 2);
        var colors = new List<Color>(triangles.Count / 2);
        var outTriangles = new List<int>(triangles.Count / 2);

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];
            float ea = tectonics.Elevation[ia], eb = tectonics.Elevation[ib], ec = tectonics.Elevation[ic];
            float avgElevation = (ea + eb + ec) / 3f;
            if (avgElevation >= seaLevelElevation) continue; // dry face - no liquid here

            // Pin each vertex: wet vertices sit at shellRadius; coastline-edge vertices
            // (elevation above seaLevel) are clamped to their terrain height + tiny lift
            // so the shell doesn't float above the land and create visible edge spikes.
            float rA = ea < seaLevelElevation ? shellRadius : radius + ea * elevationWorldScale + shellLift;
            float rB = eb < seaLevelElevation ? shellRadius : radius + eb * elevationWorldScale + shellLift;
            float rC = ec < seaLevelElevation ? shellRadius : radius + ec * elevationWorldScale + shellLift;

            int baseIndex = verts.Count;
            verts.Add(unitVerts[ia] * rA);
            verts.Add(unitVerts[ib] * rB);
            verts.Add(unitVerts[ic] * rC);
            colors.Add(liquidColor); colors.Add(liquidColor); colors.Add(liquidColor);
            outTriangles.Add(baseIndex); outTriangles.Add(baseIndex + 1); outTriangles.Add(baseIndex + 2);
        }

        return new MeshData { Vertices = verts.ToArray(), Colors = colors.ToArray(), Triangles = outTriangles.ToArray() };
    }

    /// Rebuildable variant driven by a live per-vertex liquid VOLUME array.
    /// The ocean surface is a gravitational equipotential — a sphere at constant radius —
    /// so ALL wet vertices sit at the same shell radius (mean water-surface height across
    /// the wet region). Depth variation is encoded purely in COLOR (dark = deep basin,
    /// pale = shallow shelf), never in vertex height. This prevents terrain-tracking
    /// artifacts, eliminates per-vertex volume steps showing as visible geometry, and
    /// lets the wave animation run on a properly flat base surface.
    public static MeshData BuildLiquidShellDataFromVolume(TectonicResult tectonics, float radius,
        float elevationWorldScale, float[] liquidVolume, float minVolumeToRender, LiquidDef liquid, float liquidTempK)
    {
        List<Vector3> unitVerts = tectonics.UnitVerts;
        List<int> triangles = tectonics.Triangles;
        Color liquidColor = liquid.ColorAt(liquidTempK);
        const float epsilon = 0.05f;

        // --- Compute effective sea level ---
        // Sea level = mean(terrain[v] + liquidVolume[v]) for all wet vertices.
        // This is the average water-surface height — the equipotential the ocean settles to.
        // Using mean rather than min/max avoids outliers from transient precipitation spikes.
        double sumSurface = 0.0;
        int wetCount = 0;
        for (int v = 0; v < liquidVolume.Length; v++)
        {
            if (v >= tectonics.Elevation.Length || liquidVolume[v] < minVolumeToRender) continue;
            sumSurface += tectonics.Elevation[v] + liquidVolume[v];
            wetCount++;
        }
        if (wetCount == 0) return new MeshData();
        float seaLevel = (float)(sumSurface / wetCount);
        float shellRadius = radius + seaLevel * elevationWorldScale + epsilon;

        // Shallow: close to sea level (terrain just below surface). Deep: far below.
        // Depth range for color: 0 = at sea level (shore), 1 = 1.5 elevation units below.
        Color.RGBToHSV(liquidColor, out float lh, out float ls, out float lv);
        Color shallowColor = Color.HSVToRGB(lh, ls * 0.35f, Mathf.Min(lv + 0.35f, 1f));
        shallowColor.a = liquidColor.a * 0.4f;

        var verts = new List<Vector3>(triangles.Count / 2);
        var colors = new List<Color>(triangles.Count / 2);
        var outTriangles = new List<int>(triangles.Count / 2);

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];
            float va = liquidVolume[ia], vb = liquidVolume[ib], vc = liquidVolume[ic];
            if (va < minVolumeToRender && vb < minVolumeToRender && vc < minVolumeToRender) continue;

            // Wet vertex → flat sea-level shell. Dry coastal vertex → base sphere (no lift).
            float rA = va >= minVolumeToRender ? shellRadius : radius + epsilon;
            float rB = vb >= minVolumeToRender ? shellRadius : radius + epsilon;
            float rC = vc >= minVolumeToRender ? shellRadius : radius + epsilon;

            int baseIndex = verts.Count;
            verts.Add(unitVerts[ia] * rA);
            verts.Add(unitVerts[ib] * rB);
            verts.Add(unitVerts[ic] * rC);

            // Depth = how far terrain sits below sea level. Color encodes the basin shape.
            colors.Add(DepthColor(seaLevel - tectonics.Elevation[ia], liquidColor, shallowColor));
            colors.Add(DepthColor(seaLevel - tectonics.Elevation[ib], liquidColor, shallowColor));
            colors.Add(DepthColor(seaLevel - tectonics.Elevation[ic], liquidColor, shallowColor));
            outTriangles.Add(baseIndex); outTriangles.Add(baseIndex + 1); outTriangles.Add(baseIndex + 2);
        }

        return new MeshData { Vertices = verts.ToArray(), Colors = colors.ToArray(), Triangles = outTriangles.ToArray() };
    }

    // depth = seaLevel - terrainElevation at vertex. 0 = shoreline, 1.5+ = deep basin.
    private static Color DepthColor(float depth, Color deepColor, Color shallowColor)
    {
        float depthT = Mathf.SmoothStep(0f, 1.5f, depth);
        return Color.Lerp(shallowColor, deepColor, depthT);
    }

    private static Vector3 ShellVertex(Vector3 unitVert, float terrainElevation, float liquidVolume,
        float radius, float elevationWorldScale, float minVolumeToRender, float epsilon)
    {
        if (liquidVolume < minVolumeToRender)
        {
            // Coastal vertex shared by a partially-wet triangle: clamp to base sphere.
            return unitVert * (radius + epsilon);
        }
        float shellRadius = radius + terrainElevation * elevationWorldScale + liquidVolume * elevationWorldScale + epsilon;
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
        Vector3 unitVert, TectonicResult tectonics, int vertexIndex, float radius, float elevationWorldScale, RockArchetypeDef archetype, float temperatureK = 400f)
    {
        float terrainElevation = tectonics.Elevation[vertexIndex];
        float worldRadius = radius + terrainElevation * elevationWorldScale;
        Vector3 worldPos = unitVert * worldRadius;
        Color color = RockColor(tectonics.RockTypeAtVertex[vertexIndex], archetype, terrainElevation, unitVert.y, temperatureK);
        return (worldPos, color);
    }

    private static Color RockColor(RockType type, RockArchetypeDef archetype, float elevation, float latitudeSin = 0f, float temperatureK = 400f)
    {
        // Base rock type colors shifted toward the archetype hue rather than pure gray/beige,
        // so each rock type reads as a DARKER or LIGHTER variant of the archetype rather than
        // an unrelated color that gets washed out by the blend.
        Color.RGBToHSV(archetype.Primary, out float ah, out float aS, out float aV);
        Color baseColor = type switch
        {
            // Mafic: dark, basaltic — always the darkest facies on this planet's palette.
            RockType.IgneousMafic   => Color.HSVToRGB(ah, Mathf.Min(aS + 0.25f, 1f), aV * 0.35f),
            // Felsic: light, granitic — always the brightest continental crust tone.
            RockType.IgneousFelsic  => Color.HSVToRGB(ah, aS * 0.45f, Mathf.Min(aV + 0.25f, 1f)),
            // Metamorphic: medium-dark, shifted toward the accent hue for variety.
            RockType.Metamorphic    => Color.Lerp(
                Color.HSVToRGB(ah, Mathf.Min(aS + 0.15f, 1f), aV * 0.55f),
                archetype.Accent, 0.35f),
            // Sedimentary: warm sandy tone tinted by archetype hue.
            RockType.Sedimentary    => Color.HSVToRGB(ah, aS * 0.60f, Mathf.Min(aV * 0.85f + 0.15f, 1f)),
            _                       => archetype.Primary,
        };

        // Archetype accent adds local mineral variation; primary sets the global hue.
        Color blended = Color.Lerp(baseColor, archetype.Primary, 0.40f);
        blended = Color.Lerp(blended, archetype.Accent, 0.20f);

        // Slight elevation tint: peaks read lighter (exposed/weathered rock), basins darker.
        float tint = Mathf.Clamp(elevation, -0.5f, 1f) * 0.08f;
        Color result = new Color(
            Mathf.Clamp01(blended.r + tint),
            Mathf.Clamp01(blended.g + tint),
            Mathf.Clamp01(blended.b + tint));

        float absLat = Mathf.Abs(latitudeSin);

        // Polar ice caps only on cold worlds. Starts at ~75° lat on a near-freezing world,
        // stronger/lower on colder worlds; absent entirely above 270K (no ice possible).
        if (temperatureK < 270f)
        {
            // iceStrength: 0 at 270K, 1 at ≤200K
            float iceStrength = Mathf.InverseLerp(270f, 200f, temperatureK);
            // iceStartLat: sin(lat) above which ice begins — expands toward equator on very cold worlds
            float iceStartLat = Mathf.Lerp(0.97f, 0.80f, iceStrength);
            // GLSL-style threshold smoothstep: 0 below iceStartLat, 1 above iceStartLat+0.10
            // (Mathf.SmoothStep is a lerp, NOT a threshold — must do this manually)
            float iceT = Mathf.Clamp01((absLat - iceStartLat) / 0.10f);
            float iceFraction = iceT * iceT * (3f - 2f * iceT) * iceStrength;
            if (iceFraction > 0f)
                result = Color.Lerp(result, new Color(0.88f, 0.92f, 1.0f), iceFraction);
        }

        // Hot worlds: ochre/rust tint (baked rock, dust, lava plains).
        if (temperatureK > 420f)
        {
            float hotStrength = Mathf.Clamp01((temperatureK - 420f) / 250f) * 0.18f;
            result = new Color(
                Mathf.Clamp01(result.r + hotStrength),
                Mathf.Clamp01(result.g - hotStrength * 0.2f),
                Mathf.Clamp01(result.b - hotStrength * 0.5f));
        }

        // Equatorial warmth band: subtle warm tint in tropical zone.
        float equatorialBand = Mathf.SmoothStep(0.25f, 0f, absLat);
        if (equatorialBand > 0f)
            result = Color.Lerp(result, new Color(Mathf.Clamp01(result.r * 1.04f), result.g, Mathf.Clamp01(result.b * 0.95f)), equatorialBand * 0.18f);

        return result;
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
