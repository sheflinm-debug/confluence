using System.Collections.Generic;
using UnityEngine;

public enum PlateType { Oceanic, Continental }
public enum BoundaryType { None, Divergent, Convergent, Transform }
public enum RockType { IgneousMafic, IgneousFelsic, Metamorphic, Sedimentary }

public class TectonicPlate
{
    public int Id;
    public Vector3 SeedDirection;
    public Vector3 RotationAxis;
    public float AngularVelocity;
    public PlateType Type;
    public float Buoyancy;
}

/// Per-vertex tectonic data for the whole planet mesh (spherical_terrain_generation_spec.md).
public class TectonicResult
{
    public List<Vector3> UnitVerts;
    public List<int> Triangles;
    public int[] PlateId;
    public float[] Elevation;             // roughly -1..1.5, world units applied at mesh-build time
    public BoundaryType[] BoundaryTypeAtVertex;
    public float[] DistanceToBoundary;    // 0..1, normalized graph distance
    public float[] ErosionDelta;          // net deposition(+)/erosion(-) proxy from the smoothing pass
    public RockType[] RockTypeAtVertex;
    public TectonicPlate[] Plates;
}

/// Implements spherical_terrain_generation_spec.md Parts 1-6, 9, plus a thermal-erosion
/// (slope-relaxation) pass standing in for Part 7's full particle-based hydraulic
/// erosion - that is the one stage intentionally simplified in this v1; flagged in
/// the class doc so it's easy to find and swap in a real droplet simulation later.
/// Part 8 (isostasy) is skipped per the spec's own "optional/lower priority" note.
public static class TectonicPlanetGenerator
{
    public static TectonicResult Generate(
        int subdivisions, int plateCount, float boundaryInfluence,
        float noiseAmplitude, int volcanoCount, float volcanoAmplitude,
        int erosionIterations, float erosionMaxSlope, float erosionTransferRate)
    {
        IcosphereGenerator.Build(subdivisions, out List<Vector3> unitVerts, out List<int> triangles);
        List<int>[] adjacency = BuildAdjacency(unitVerts.Count, triangles);

        TectonicPlate[] plates = GeneratePlates(plateCount);
        int[] plateId = AssignVoronoi(unitVerts, plates);
        (BoundaryType[] boundaryType, float[] distanceToBoundary) =
            ClassifyBoundaries(unitVerts, adjacency, plateId, plates, boundaryInfluence);

        float[] elevation = ComputeBaseElevation(unitVerts, plateId, plates, boundaryType, distanceToBoundary);
        ApplyNoiseLayer(unitVerts, elevation, distanceToBoundary, noiseAmplitude);
        ApplyVolcanism(unitVerts, elevation, volcanoCount, volcanoAmplitude);

        float[] erosionDelta = ApplyThermalErosion(unitVerts, adjacency, elevation, erosionIterations, erosionMaxSlope, erosionTransferRate);
        RockType[] rockType = ClassifyRockType(plateId, plates, boundaryType, distanceToBoundary, erosionDelta, elevation);

        return new TectonicResult
        {
            UnitVerts = unitVerts,
            Triangles = triangles,
            PlateId = plateId,
            Elevation = elevation,
            BoundaryTypeAtVertex = boundaryType,
            DistanceToBoundary = distanceToBoundary,
            ErosionDelta = erosionDelta,
            RockTypeAtVertex = rockType,
            Plates = plates,
        };
    }

    // --- Part 1: adjacency -------------------------------------------------

    /// Public entry point so other systems (e.g. FluidDynamicsManager) can reuse the same
    /// per-vertex adjacency graph built here for thermal erosion, instead of re-deriving
    /// it from triangles a second time.
    public static List<int>[] BuildVertexAdjacency(int vertexCount, List<int> triangles) =>
        BuildAdjacency(vertexCount, triangles);

    private static List<int>[] BuildAdjacency(int vertexCount, List<int> triangles)
    {
        var sets = new HashSet<int>[vertexCount];
        for (int i = 0; i < vertexCount; i++) sets[i] = new HashSet<int>();

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            sets[a].Add(b); sets[a].Add(c);
            sets[b].Add(a); sets[b].Add(c);
            sets[c].Add(a); sets[c].Add(b);
        }

        var adjacency = new List<int>[vertexCount];
        for (int i = 0; i < vertexCount; i++) adjacency[i] = new List<int>(sets[i]);
        return adjacency;
    }

    // --- Part 2: plate generation -------------------------------------------

    private static TectonicPlate[] GeneratePlates(int plateCount)
    {
        var plates = new TectonicPlate[plateCount];
        Quaternion randomRotation = Random.rotation; // vary the Fibonacci pattern per playthrough
        float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));

        for (int i = 0; i < plateCount; i++)
        {
            float y = 1f - (i / (float)(plateCount - 1)) * 2f;
            float radiusAtY = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float theta = goldenAngle * i;
            Vector3 seed = randomRotation * new Vector3(Mathf.Cos(theta) * radiusAtY, y, Mathf.Sin(theta) * radiusAtY);

            bool continental = Random.value < 0.4f; // Earth-ish ~60/40 oceanic/continental
            plates[i] = new TectonicPlate
            {
                Id = i,
                SeedDirection = seed.normalized,
                RotationAxis = Random.onUnitSphere,
                AngularVelocity = Random.Range(0.3f, 1.5f),
                Type = continental ? PlateType.Continental : PlateType.Oceanic,
                Buoyancy = continental ? Random.Range(0.6f, 1f) : Random.Range(0f, 0.4f),
            };
        }
        return plates;
    }

    private static int[] AssignVoronoi(List<Vector3> unitVerts, TectonicPlate[] plates)
    {
        var plateId = new int[unitVerts.Count];
        for (int v = 0; v < unitVerts.Count; v++)
        {
            int best = 0;
            float bestDot = -2f;
            for (int p = 0; p < plates.Length; p++)
            {
                float dot = Vector3.Dot(unitVerts[v], plates[p].SeedDirection); // max dot = nearest great-circle
                if (dot > bestDot) { bestDot = dot; best = p; }
            }
            plateId[v] = best;
        }
        return plateId;
    }

    // --- Part 3: boundary classification ------------------------------------

    private static (BoundaryType[], float[]) ClassifyBoundaries(
        List<Vector3> unitVerts, List<int>[] adjacency, int[] plateId, TectonicPlate[] plates, float boundaryInfluence)
    {
        int n = unitVerts.Count;
        var boundaryType = new BoundaryType[n];
        var distance = new float[n];
        for (int i = 0; i < n; i++) distance[i] = float.MaxValue;

        var frontier = new Queue<int>();

        for (int v = 0; v < n; v++)
        {
            foreach (int nb in adjacency[v])
            {
                if (plateId[nb] == plateId[v]) continue;

                TectonicPlate plateA = plates[plateId[v]];
                TectonicPlate plateB = plates[plateId[nb]];
                Vector3 velA = Vector3.Cross(plateA.RotationAxis * plateA.AngularVelocity, unitVerts[v]);
                Vector3 velB = Vector3.Cross(plateB.RotationAxis * plateB.AngularVelocity, unitVerts[v]);
                Vector3 relVel = velB - velA;
                Vector3 boundaryDir = (unitVerts[nb] - unitVerts[v]).normalized;
                float approach = Vector3.Dot(relVel, boundaryDir); // >0 = B approaching A along this line

                const float transformThreshold = 0.15f;
                BoundaryType bt = approach > transformThreshold ? BoundaryType.Convergent
                                 : approach < -transformThreshold ? BoundaryType.Divergent
                                 : BoundaryType.Transform;

                if (boundaryType[v] == BoundaryType.None) boundaryType[v] = bt;
                if (distance[v] > 0f) { distance[v] = 0f; frontier.Enqueue(v); }
            }
        }

        // Part 3 (end): multi-source BFS graph-distance to nearest boundary vertex.
        while (frontier.Count > 0)
        {
            int v = frontier.Dequeue();
            foreach (int nb in adjacency[v])
            {
                if (distance[nb] > distance[v] + 1f)
                {
                    distance[nb] = distance[v] + 1f;
                    frontier.Enqueue(nb);
                }
            }
        }

        for (int i = 0; i < n; i++) distance[i] = Mathf.Clamp01(distance[i] / boundaryInfluence);
        return (boundaryType, distance);
    }

    // --- Part 4: base elevation ----------------------------------------------

    private static float[] ComputeBaseElevation(
        List<Vector3> unitVerts, int[] plateId, TectonicPlate[] plates, BoundaryType[] boundaryType, float[] distanceToBoundary)
    {
        int n = unitVerts.Count;
        var elevation = new float[n];

        for (int v = 0; v < n; v++)
        {
            TectonicPlate plate = plates[plateId[v]];
            // Wider ocean/continent split so basins are clearly distinct from land.
            // Continental +0.55, Oceanic -0.60 → ~1.15 unit gap before boundary effects.
            float baseline = plate.Type == PlateType.Continental ? 0.55f : -0.60f;
            float falloff = 1f - distanceToBoundary[v]; // 1 at boundary, 0 far away

            float boundaryEffect = boundaryType[v] switch
            {
                BoundaryType.Divergent  => plate.Type == PlateType.Oceanic ? 0.15f * falloff : -0.20f * falloff,
                BoundaryType.Convergent => plate.Type == PlateType.Continental ? 1.1f * falloff : -0.55f * falloff,
                BoundaryType.Transform  => 0.08f * falloff * (Random.value - 0.5f) * 2f,
                _ => 0f,
            };

            elevation[v] = baseline + boundaryEffect;
        }
        return elevation;
    }

    // --- Part 5: noise layer (fBm, higher amplitude near boundaries) --------

    private static void ApplyNoiseLayer(List<Vector3> unitVerts, float[] elevation, float[] distanceToBoundary, float amplitude)
    {
        Vector3 offset = new Vector3(Random.Range(0f, 1000f), Random.Range(0f, 1000f), Random.Range(0f, 1000f));
        const int octaves = 4;

        for (int v = 0; v < unitVerts.Count; v++)
        {
            float sum = 0f, amp = 1f, freq = 1f, ampTotal = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Sample3DNoise(unitVerts[v] * freq, offset) * amp;
                ampTotal += amp;
                amp *= 0.5f;
                freq *= 2.1f;
            }
            float noise = (sum / ampTotal) * 2f - 1f; // -1..1
            float localAmplitude = amplitude * Mathf.Lerp(0.3f, 1f, 1f - distanceToBoundary[v]);
            elevation[v] += noise * localAmplitude;
        }
    }

    private static float Sample3DNoise(Vector3 unitPos, Vector3 offset)
    {
        const float scale = 0.4f;
        float x = unitPos.x / scale, y = unitPos.y / scale, z = unitPos.z / scale;
        float n1 = Mathf.PerlinNoise(x + offset.x, y + offset.y);
        float n2 = Mathf.PerlinNoise(y + offset.y, z + offset.z);
        float n3 = Mathf.PerlinNoise(z + offset.z, x + offset.x);
        return (n1 + n2 + n3) / 3f;
    }

    // --- Part 6: volcanism (hotspot bumps, independent of boundaries) -------

    private static void ApplyVolcanism(List<Vector3> unitVerts, float[] elevation, int volcanoCount, float amplitude)
    {
        for (int h = 0; h < volcanoCount; h++)
        {
            Vector3 hotspot = Random.onUnitSphere;
            float sigma = Random.Range(0.08f, 0.2f);
            for (int v = 0; v < unitVerts.Count; v++)
            {
                float angularDist = Mathf.Acos(Mathf.Clamp(Vector3.Dot(hotspot, unitVerts[v]), -1f, 1f));
                if (angularDist > sigma * 3f) continue;
                float bump = amplitude * Mathf.Exp(-(angularDist * angularDist) / (2f * sigma * sigma));
                elevation[v] += bump;
            }
        }
    }

    // --- Part 7 (simplified): thermal erosion / slope relaxation -----------
    // Stands in for the spec's particle-based hydraulic erosion (droplets, sediment
    // capacity) which is NOT implemented in this pass - that is the one explicitly
    // deferred piece of the spec. This still produces the smoothed valleys/ridges
    // and a usable net erosion/deposition signal for rock classification (Part 9).

    private static float[] ApplyThermalErosion(
        List<Vector3> unitVerts, List<int>[] adjacency, float[] elevation,
        int iterations, float maxSlope, float transferRate)
    {
        int n = elevation.Length;
        var delta = new float[n];

        for (int iter = 0; iter < iterations; iter++)
        {
            var next = (float[])elevation.Clone();
            for (int v = 0; v < n; v++)
            {
                // Normalize by degree: an explicit (Jacobi-style) diffusion step where
                // EVERY over-threshold neighbor pushes/pulls independently is only
                // numerically stable if the total outflow from a vertex in one pass is
                // bounded below its own elevation difference - otherwise a degree-6
                // vertex can push out up to 6x transferRate of its diff in a single
                // iteration, which oscillates and blows up exponentially over a few
                // iterations (this was the cause of the "exploded into a spiky star"
                // artifact - a handful of vertices runaway to extreme radius while the
                // rest of the sphere stays normal).
                int degree = Mathf.Max(1, adjacency[v].Count);
                foreach (int nb in adjacency[v])
                {
                    float diff = elevation[v] - elevation[nb];
                    if (diff > maxSlope)
                    {
                        float transfer = (diff - maxSlope) * transferRate / degree;
                        next[v] -= transfer;
                        next[nb] += transfer;
                    }
                }
            }
            for (int v = 0; v < n; v++) { delta[v] += next[v] - elevation[v]; elevation[v] = next[v]; }
        }

        // Hard safety clamp regardless of cause - terrain should never read as a needle.
        for (int v = 0; v < n; v++) elevation[v] = Mathf.Clamp(elevation[v], -1.5f, 2.5f);

        return delta;
    }

    // --- Part 9: rock type classification -----------------------------------

    private static RockType[] ClassifyRockType(
        int[] plateId, TectonicPlate[] plates, BoundaryType[] boundaryType, float[] distanceToBoundary,
        float[] erosionDelta, float[] elevation)
    {
        int n = plateId.Length;
        var rock = new RockType[n];
        const float depositionThreshold = 0.01f;
        const float nearBoundary = 0.35f;

        for (int v = 0; v < n; v++)
        {
            TectonicPlate plate = plates[plateId[v]];
            bool isNearBoundary = distanceToBoundary[v] < nearBoundary;

            if (isNearBoundary && boundaryType[v] == BoundaryType.Divergent)
                rock[v] = RockType.IgneousMafic;
            else if (isNearBoundary && boundaryType[v] == BoundaryType.Convergent && plate.Type == PlateType.Continental)
                rock[v] = RockType.IgneousFelsic; // continental arc / orogenic granite
            else if (isNearBoundary && boundaryType[v] == BoundaryType.Convergent && plate.Type == PlateType.Oceanic)
                rock[v] = RockType.Metamorphic; // crude stand-in for subduction-zone metamorphism
            else
                rock[v] = plate.Type == PlateType.Oceanic ? RockType.IgneousMafic : RockType.IgneousFelsic;

            // Sedimentary OVERRIDES everything above where net deposition occurred (Part 9, step 4).
            if (erosionDelta[v] > depositionThreshold)
                rock[v] = RockType.Sedimentary;
        }
        return rock;
    }
}
