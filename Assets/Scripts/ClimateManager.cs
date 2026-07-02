using System.Collections.Generic;
using UnityEngine;

/// Procedural climate model (Section 2/3 stand-in): continuous temperature and moisture
/// fields evaluated anywhere on the sphere surface via 3D noise, classified into biome
/// archetypes. Replaces the earlier abstract per-tile pressure label with an actual
/// generated climate, randomized per playthrough via Randomize().
public static class ClimateManager
{
    private static Vector3 _temperatureOffset;
    private static Vector3 _moistureOffset;
    private static float _noiseScale = 0.15f;
    private static Vector3 _planetCenter;
    private static float _planetRadius;

    // --- Terrain state (optional; null = no terrain effects) ---
    private static float[] _elevation;
    private static List<Vector3> _unitVerts;
    private static List<int>[] _adjacency;
    private static float[] _liquidVolume;

    // Cache for FindNearestVertexIndex: avoid re-scanning every call within same frame.
    private static Vector3 _cachedNearestPos = new Vector3(float.NaN, float.NaN, float.NaN);
    private static int _cachedNearestIdx = -1;

    /// Re-rolls the climate for a new playthrough. Call once at world-gen time.
    public static void Randomize(Vector3 planetCenter, float planetRadius, float noiseScale = 0.15f)
    {
        _planetCenter = planetCenter;
        _planetRadius = planetRadius;
        _noiseScale = noiseScale;

        // Large random offsets so each playthrough samples a different region of the
        // noise field, producing a different climate layout every time.
        _temperatureOffset = new Vector3(Random.Range(0f, 1000f), Random.Range(0f, 1000f), Random.Range(0f, 1000f));
        _moistureOffset = new Vector3(Random.Range(0f, 1000f), Random.Range(0f, 1000f), Random.Range(0f, 1000f));
    }

    /// Supply terrain data so orographic effects can be computed.
    /// Call after TectonicManager has finished generating the mesh.
    public static void SetTerrain(List<Vector3> unitVerts, float[] elevation, List<int>[] adjacency)
    {
        _unitVerts = unitVerts;
        _elevation = elevation;
        _adjacency = adjacency;
        // Invalidate nearest-vertex cache.
        _cachedNearestPos = new Vector3(float.NaN, float.NaN, float.NaN);
        _cachedNearestIdx = -1;
    }

    /// Returns normalized elevation [0,1] at the given world position.
    /// 0 = sea level, 1 = maximum terrain height. Returns 0 if terrain data is absent.
    public static float GetElevation(Vector3 worldPos)
    {
        if (_unitVerts == null || _elevation == null) return 0f;
        Vector3 unitPos = (worldPos - _planetCenter).normalized;
        int idx = FindNearestVertexIndex(unitPos);
        return idx < 0 ? 0f : _elevation[idx];
    }

    /// Supply liquid-volume data from FluidDynamicsManager for ocean-proximity moisture.
    /// May be called with null to clear the data.
    public static void SetLiquidVolume(float[] liquidVolume)
    {
        _liquidVolume = liquidVolume;
    }

    /// 0-100. Returns a stable, position-dependent value (not a discrete tile lookup),
    /// plus any transient WeatherManager storm effect (clamped so a storm can never
    /// push the result out of the 0-100 range or produce NaN - AgentController's
    /// discomfort/starvation logic reads this every tick and must always get a sane
    /// value even mid-storm).
    public static float GetTemperature(Vector3 worldPosition)
    {
        Vector3 p = (worldPosition - _planetCenter).normalized;
        float baseTemp = Sample3DNoise(p, _temperatureOffset) * 100f;

        // Latitude-based temperature gradient: equator (p.y=0) is warmest, poles
        // (|p.y|=1) are coldest. p.y = sin(latitude) assuming Y is the rotation axis.
        // Subtracts up to 45 temperature units at the poles so Tundra biome forms there
        // naturally and the freeze threshold for polar ice is reliably crossed.
        float latColdening = p.y * p.y * 45f;

        // Seasonal flux: OrbitalSeasons multiplies solar exposure by latitude + orbital
        // phase. When active, add a ±20-unit seasonal temperature swing so summer/winter
        // are visible and storms/ice shift with the season.
        float seasonalDelta = 0f;
        if (OrbitalSeasons.Instance != null && OrbitalSeasons.Instance.AxialTiltDeg > 0.001f)
        {
            float fluxMul = OrbitalSeasons.Instance.FluxMultiplier;
            float seasonalMul = OrbitalSeasons.Instance.SeasonalExposureMultiplier(p.y);
            seasonalDelta = (fluxMul * seasonalMul - 1f) * 20f;
        }

        float weatherDelta = WeatherManager.Instance != null ? WeatherManager.Instance.GetTemperatureModifier(worldPosition) : 0f;

        // Orographic effect: föhn warming on leeward side, cooling on windward side.
        float orographicDelta = 0f;
        if (_elevation != null && _unitVerts != null && _adjacency != null)
        {
            // Prevailing wind approximated as eastward (prograde rotation).
            // Cross(unitPos, up) points east at the equator and gracefully degrades at poles.
            Vector3 windDir = Vector3.Cross(p, Vector3.up).normalized;

            int centerIdx = FindNearestVertexIndex(p);
            if (centerIdx >= 0)
            {
                int upwindIdx = TraverseHops(centerIdx, -windDir, 2);
                int downwindIdx = TraverseHops(centerIdx, windDir, 2);

                float upwindElev = upwindIdx >= 0 ? _elevation[upwindIdx] : _elevation[centerIdx];
                float downwindElev = downwindIdx >= 0 ? _elevation[downwindIdx] : _elevation[centerIdx];
                float elevDiff = upwindElev - downwindElev;

                if (elevDiff > 0.05f)
                {
                    // Upwind is higher → we are on the leeward (downwind) side → föhn warming.
                    orographicDelta = 5f;
                }
                else if (elevDiff < -0.05f)
                {
                    // Downwind is higher → we are on the windward side → cooler, wetter.
                    orographicDelta = -3f;
                }
            }
        }

        return Mathf.Clamp(baseTemp - latColdening + seasonalDelta + weatherDelta + orographicDelta, 0f, 100f);
    }

    /// 0-100, plus any transient WeatherManager storm effect (clamped, see GetTemperature).
    public static float GetMoisture(Vector3 worldPosition)
    {
        Vector3 p = (worldPosition - _planetCenter).normalized;
        float baseMoisture = Sample3DNoise(p, _moistureOffset) * 100f;
        float weatherDelta = WeatherManager.Instance != null ? WeatherManager.Instance.GetMoistureModifier(worldPosition) : 0f;

        // Ocean-proximity moisture bonus.
        float moistureBonus = 0f;
        if (_liquidVolume != null && _unitVerts != null && _adjacency != null)
        {
            int centerIdx = FindNearestVertexIndex(p);
            if (centerIdx >= 0)
            {
                if (_liquidVolume[centerIdx] > 0.1f)
                {
                    // Directly over ocean.
                    moistureBonus = 35f;
                }
                else
                {
                    // Check within 3 hops for any ocean vertex.
                    if (HasLiquidWithinHops(centerIdx, 3))
                        moistureBonus = 25f;
                }
            }
        }

        return Mathf.Clamp(baseMoisture + weatherDelta + moistureBonus, 0f, 100f);
    }

    public static BiomeType GetBiome(Vector3 worldPosition)
    {
        float temp = GetTemperature(worldPosition);
        float moisture = GetMoisture(worldPosition);
        return ClassifyBiome(temp, moisture);
    }

    public static BiomeType ClassifyBiome(float temperature, float moisture)
    {
        if (moisture >= 80f) return BiomeType.Wetland;
        if (temperature <= 30f) return BiomeType.Tundra;
        if (temperature >= 60f && moisture < 40f) return BiomeType.Desert;
        if (temperature >= 60f && moisture >= 40f) return BiomeType.Jungle;
        if (moisture >= 55f) return BiomeType.Forest;
        return BiomeType.Grassland;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// Returns the index in _unitVerts whose direction is closest to unitPos.
    /// Caches the last result; repeated calls with the same position are O(1).
    private static int FindNearestVertexIndex(Vector3 unitPos)
    {
        if (_unitVerts == null || _unitVerts.Count == 0)
            return -1;

        // Cache check: same unit-position as last call?
        if (_cachedNearestIdx >= 0 && _cachedNearestPos == unitPos)
            return _cachedNearestIdx;

        int best = 0;
        float bestDot = float.MinValue;
        for (int i = 0; i < _unitVerts.Count; i++)
        {
            float d = Vector3.Dot(unitPos, _unitVerts[i]);
            if (d > bestDot)
            {
                bestDot = d;
                best = i;
            }
        }

        _cachedNearestPos = unitPos;
        _cachedNearestIdx = best;
        return best;
    }

    /// Walk the adjacency graph `hops` steps in the direction most aligned with `dir`
    /// from `startIdx`. Returns the final vertex index, or startIdx if adjacency is empty.
    private static int TraverseHops(int startIdx, Vector3 dir, int hops)
    {
        int current = startIdx;
        for (int h = 0; h < hops; h++)
        {
            if (_adjacency[current] == null || _adjacency[current].Count == 0)
                break;

            int best = current;
            float bestDot = float.MinValue;
            foreach (int neighbor in _adjacency[current])
            {
                if (neighbor < 0 || neighbor >= _unitVerts.Count)
                    continue;
                float d = Vector3.Dot(_unitVerts[neighbor], dir);
                if (d > bestDot)
                {
                    bestDot = d;
                    best = neighbor;
                }
            }

            if (best == current)
                break; // no progress

            current = best;
        }
        return current;
    }

    /// BFS up to `maxHops` in the adjacency graph; returns true if any vertex within
    /// that radius has liquidVolume > 0.1f.
    private static bool HasLiquidWithinHops(int startIdx, int maxHops)
    {
        // Simple BFS with a small visited set (graph is bounded by adjacency depth).
        // We avoid allocations by reusing a fixed-size frontier array sized for the
        // worst-case number of vertices reachable in 3 hops on a typical icosphere
        // (~1 + 6 + 12 + 18 = ~37 vertices). Use a small array + count rather than
        // a HashSet to keep GC pressure near zero.
        int capacity = 64;
        int[] visited = new int[capacity];
        int visitedCount = 0;

        int[] frontier = new int[capacity];
        int[] nextFrontier = new int[capacity];
        int frontierCount = 0;
        int nextCount = 0;

        visited[visitedCount++] = startIdx;
        frontier[frontierCount++] = startIdx;

        for (int hop = 0; hop < maxHops; hop++)
        {
            nextCount = 0;
            for (int fi = 0; fi < frontierCount; fi++)
            {
                int v = frontier[fi];
                if (_adjacency[v] == null)
                    continue;
                foreach (int neighbor in _adjacency[v])
                {
                    if (neighbor < 0 || neighbor >= _liquidVolume.Length)
                        continue;

                    // Check if already visited.
                    bool seen = false;
                    for (int vi = 0; vi < visitedCount; vi++)
                    {
                        if (visited[vi] == neighbor) { seen = true; break; }
                    }
                    if (seen) continue;

                    if (visitedCount < capacity) visited[visitedCount++] = neighbor;
                    if (nextCount < capacity) nextFrontier[nextCount++] = neighbor;

                    if (_liquidVolume[neighbor] > 0.1f)
                        return true;
                }
            }

            // Swap frontier arrays.
            int[] tmp = frontier;
            frontier = nextFrontier;
            nextFrontier = tmp;
            frontierCount = nextCount;

            if (frontierCount == 0)
                break;
        }

        return false;
    }

    /// Pseudo-3D Perlin noise (averaged across three axis-aligned 2D samples) so the
    /// field has no seams on the sphere, unlike a single 2D Perlin lookup would.
    private static float Sample3DNoise(Vector3 unitPos, Vector3 offset)
    {
        float scaledX = unitPos.x / _noiseScale;
        float scaledY = unitPos.y / _noiseScale;
        float scaledZ = unitPos.z / _noiseScale;

        float n1 = Mathf.PerlinNoise(scaledX + offset.x, scaledY + offset.y);
        float n2 = Mathf.PerlinNoise(scaledY + offset.y, scaledZ + offset.z);
        float n3 = Mathf.PerlinNoise(scaledZ + offset.z, scaledX + offset.x);

        return (n1 + n2 + n3) / 3f;
    }
}
