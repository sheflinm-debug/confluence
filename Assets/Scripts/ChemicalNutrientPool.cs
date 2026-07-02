using UnityEngine;

/// Per-vertex dissolved-chemical nutrient pool representing the primordial ocean/vent
/// chemistry that the first life drew energy from. Chemosynthetic organisms deplete
/// their local vertex; the pool replenishes slowly from geothermal/atmospheric input.
///
/// This is the energy source for ALL life before photosynthesis evolves. Vertices
/// near liquid are rich (hydrothermal vent chemistry, dissolved organics); dry
/// highland tiles are sparse.
public static class ChemicalNutrientPool
{
    private static float[] _density;       // 0–1 per vertex
    private static Vector3[] _unitVerts;
    private static Vector3 _planetCenter;

    public static bool Initialized => _density != null;

    /// Seed nutrient density. Call after FluidDynamicsManager.Init so liquidVolume is ready.
    /// liquidVolume[i] > 0 means submerged; elevation[i] is normalized 0–1 terrain height.
    public static void Init(Vector3 planetCenter, Vector3[] unitVerts,
        float[] elevation, float[] liquidVolume)
    {
        _planetCenter = planetCenter;
        _unitVerts = unitVerts;
        _density = new float[unitVerts.Length];

        for (int i = 0; i < unitVerts.Length; i++)
        {
            bool submerged = liquidVolume != null && i < liquidVolume.Length && liquidVolume[i] > 0.05f;
            float elev = elevation != null && i < elevation.Length ? elevation[i] : 0f;

            if (submerged)
                // Hydrothermal ocean chemistry: rich in dissolved H2S, Fe2+, organics.
                _density[i] = Random.Range(0.65f, 1.0f);
            else if (elev < 0.2f)
                // Coastal / wet soil: moderate organic accumulation.
                _density[i] = Random.Range(0.25f, 0.55f);
            else
                // Dry highland: sparse minerals, little dissolved chemistry.
                _density[i] = Random.Range(0.02f, 0.18f);
        }
    }

    /// Nutrient density (0–1) at worldPos — samples the nearest vertex.
    public static float Sample(Vector3 worldPos)
    {
        if (_density == null) return 0.5f;
        return _density[NearestVertex(worldPos)];
    }

    /// Remove nutrients at worldPos (chemosynthesis consumes them).
    public static void Deplete(Vector3 worldPos, float amount)
    {
        if (_density == null) return;
        int i = NearestVertex(worldPos);
        _density[i] = Mathf.Max(0f, _density[i] - amount);
    }

    /// Slow replenishment: geothermal input + atmospheric dissolution. Call from a manager each frame.
    public static void Replenish(float dt)
    {
        if (_density == null) return;
        // Very slow recovery — nutrients are genuinely limited in early oceans.
        float rate = 0.0015f * dt;
        for (int i = 0; i < _density.Length; i++)
            _density[i] = Mathf.Min(1f, _density[i] + rate);
    }

    /// Amplify nutrient density near hydrothermal vents. Call after Init and vent placement.
    /// Near-vent vertices are raised to at least the vent energy value, creating genuine
    /// spatial heterogeneity that chemosynthetic organisms compete for.
    public static void ApplyVentBoost(HydrothermalVentManager vents, float planetRadius)
    {
        if (_density == null || vents == null) return;
        for (int i = 0; i < _unitVerts.Length; i++)
        {
            Vector3 worldPos = _planetCenter + _unitVerts[i] * planetRadius;
            float ventE = vents.GetVentEnergyAt(worldPos);
            if (ventE > _density[i]) _density[i] = ventE;
        }
    }

    private static int NearestVertex(Vector3 worldPos)
    {
        Vector3 dir = (worldPos - _planetCenter).normalized;
        int best = 0;
        float bestDot = float.MinValue;
        for (int i = 0; i < _unitVerts.Length; i++)
        {
            float d = Vector3.Dot(dir, _unitVerts[i]);
            if (d > bestDot) { bestDot = d; best = i; }
        }
        return best;
    }
}
