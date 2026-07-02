using System.Collections.Generic;
using UnityEngine;

/// Places hydrothermal vents in deep ocean basins at planet genesis. Vents are the
/// primary energy source for chemosynthetic life — their spatial distribution creates
/// competition, clustering, and genuine selection pressure to stay near the vents.
///
/// Vent placement: N deepest submerged vertices (high ocean depth = low terrain + high
/// liquid volume), shuffled and sampled so vents are geographically spread rather than
/// all clustering in one basin.
///
/// Energy output: each vent radiates chemical energy (0-1) that falls off quadratically
/// with distance. Multiple vents stack additively, capped at 1. Chemosynthetic organisms
/// query GetVentEnergyAt() instead of — or in addition to — ChemicalNutrientPool.
public class HydrothermalVentManager : MonoBehaviour
{
    public static HydrothermalVentManager Instance { get; private set; }

    [Header("Vent placement")]
    [Tooltip("Minimum depth below sea level for a vent vertex (elevation units). Keeps vents off shallow shelves.")]
    public float minVentDepth = 0.25f;
    [Tooltip("How many vents to place. More = richer chemistry, less clustering pressure.")]
    public Vector2Int ventCountRange = new Vector2Int(3, 7);

    [Header("Vent properties")]
    [Tooltip("World-unit radius of each vent's chemical influence. Larger = more forgiving spatial requirement.")]
    public Vector2 ventRadiusRange = new Vector2(2.5f, 5.5f);
    [Tooltip("Peak chemical output at vent centre (0-1 nutrient density).")]
    public Vector2 ventIntensityRange = new Vector2(0.65f, 1.0f);
    [Tooltip("Fraction of vent intensity that persists at distance = radius (the background tail).")]
    [Range(0f, 0.3f)] public float ventTail = 0.05f;

    public struct VentData
    {
        public Vector3 WorldPosition;
        public float   Radius;
        public float   Intensity;
    }

    private readonly List<VentData> _vents = new List<VentData>();
    public IReadOnlyList<VentData> Vents => _vents;
    public int VentCount => _vents.Count;

    void Awake()  { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    /// Call after FluidDynamicsManager.Init() so liquid volume is populated.
    public void Init(TectonicResult tectonics, float[] liquidVolume, float minVolumeToRender,
                     float planetRadius, Vector3 planetCenter)
    {
        _vents.Clear();

        // Compute live sea level (same formula as BuildLiquidShellDataFromVolume).
        double sumSurf = 0.0; int wetN = 0;
        for (int v = 0; v < liquidVolume.Length; v++)
        {
            if (v >= tectonics.Elevation.Length || liquidVolume[v] < minVolumeToRender) continue;
            sumSurf += tectonics.Elevation[v] + liquidVolume[v];
            wetN++;
        }
        if (wetN == 0) { Debug.Log("[Vents] No liquid — no hydrothermal vents placed."); return; }
        float seaLevel = (float)(sumSurf / wetN);

        // Collect deep-basin eligible vertices.
        var deep = new List<(int idx, float depth)>();
        for (int v = 0; v < liquidVolume.Length; v++)
        {
            if (v >= tectonics.Elevation.Length || liquidVolume[v] < minVolumeToRender) continue;
            float depth = seaLevel - tectonics.Elevation[v];
            if (depth >= minVentDepth) deep.Add((v, depth));
        }

        if (deep.Count == 0) { Debug.Log("[Vents] No deep-basin vertices — no vents placed."); return; }

        // Sort by depth descending so we pick the deepest candidates first,
        // then choose randomly among them to spread vents geographically.
        deep.Sort((a, b) => b.depth.CompareTo(a.depth));

        // Keep only top 40% deepest candidates for placement.
        int pool = Mathf.Max(1, deep.Count * 2 / 5);
        deep.RemoveRange(pool, deep.Count - pool);

        // Shuffle pool.
        for (int i = deep.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            var tmp = deep[i]; deep[i] = deep[j]; deep[j] = tmp;
        }

        int ventCount = Mathf.Min(Random.Range(ventCountRange.x, ventCountRange.y + 1), deep.Count);
        for (int i = 0; i < ventCount; i++)
        {
            int v = deep[i].idx;
            _vents.Add(new VentData
            {
                WorldPosition = planetCenter + tectonics.UnitVerts[v] * planetRadius,
                Radius        = Random.Range(ventRadiusRange.x,    ventRadiusRange.y),
                Intensity     = Random.Range(ventIntensityRange.x, ventIntensityRange.y),
            });
        }

        Debug.Log($"[Vents] Placed {_vents.Count} hydrothermal vents (sea level {seaLevel:F3}, " +
                  $"deep pool {deep.Count} verts, deepest {deep[0].depth:F3} below sea level).");
    }

    /// Chemical energy density at worldPos contributed by all vents (0-1).
    /// Falls off quadratically from vent centre, stacks additively up to 1.
    public float GetVentEnergyAt(Vector3 worldPos)
    {
        float total = 0f;
        foreach (var v in _vents)
        {
            float dist = Vector3.Distance(worldPos, v.WorldPosition);
            if (dist >= v.Radius) continue;
            float t = 1f - (dist / v.Radius);         // 1 at centre, 0 at edge
            total += (ventTail + (1f - ventTail) * t * t) * v.Intensity;
        }
        return Mathf.Clamp01(total);
    }
}
