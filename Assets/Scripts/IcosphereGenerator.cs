using System.Collections.Generic;
using UnityEngine;

/// Builds a unit-radius icosphere (subdivided icosahedron) - vertices + triangle indices
/// only, no colors/UVs. Used as the base shape for the flat-shaded, per-tile-colored
/// planet mesh so the surface reads as discrete terrain faces rather than a smooth ball.
public static class IcosphereGenerator
{
    public static void Build(int subdivisions, out List<Vector3> vertices, out List<int> triangles)
    {
        vertices = new List<Vector3>();
        triangles = new List<int>();

        float t = (1f + Mathf.Sqrt(5f)) / 2f;

        Vector3[] baseVerts =
        {
            new Vector3(-1,  t,  0), new Vector3( 1,  t,  0), new Vector3(-1, -t,  0), new Vector3( 1, -t,  0),
            new Vector3( 0, -1,  t), new Vector3( 0,  1,  t), new Vector3( 0, -1, -t), new Vector3( 0,  1, -t),
            new Vector3( t,  0, -1), new Vector3( t,  0,  1), new Vector3(-t,  0, -1), new Vector3(-t,  0,  1)
        };
        for (int i = 0; i < baseVerts.Length; i++) vertices.Add(baseVerts[i].normalized);

        int[] baseTris =
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
            1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
            4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
        };
        triangles.AddRange(baseTris);

        var midpointCache = new Dictionary<long, int>();

        for (int s = 0; s < subdivisions; s++)
        {
            var newTriangles = new List<int>();
            midpointCache.Clear();

            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];

                int ab = GetMidpoint(a, b, vertices, midpointCache);
                int bc = GetMidpoint(b, c, vertices, midpointCache);
                int ca = GetMidpoint(c, a, vertices, midpointCache);

                newTriangles.AddRange(new[] { a, ab, ca });
                newTriangles.AddRange(new[] { b, bc, ab });
                newTriangles.AddRange(new[] { c, ca, bc });
                newTriangles.AddRange(new[] { ab, bc, ca });
            }

            triangles = newTriangles;
        }
    }

    private static int GetMidpoint(int a, int b, List<Vector3> vertices, Dictionary<long, int> cache)
    {
        long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
        if (cache.TryGetValue(key, out int existing)) return existing;

        Vector3 midpoint = ((vertices[a] + vertices[b]) * 0.5f).normalized;
        vertices.Add(midpoint);
        int index = vertices.Count - 1;
        cache[key] = index;
        return index;
    }
}
