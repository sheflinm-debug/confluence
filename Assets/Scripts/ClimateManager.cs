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

    /// 0-100. Returns a stable, position-dependent value (not a discrete tile lookup),
    /// plus any transient WeatherManager storm effect (clamped so a storm can never
    /// push the result out of the 0-100 range or produce NaN - AgentController's
    /// discomfort/starvation logic reads this every tick and must always get a sane
    /// value even mid-storm).
    public static float GetTemperature(Vector3 worldPosition)
    {
        Vector3 p = (worldPosition - _planetCenter).normalized;
        float baseTemp = Sample3DNoise(p, _temperatureOffset) * 100f;
        float weatherDelta = WeatherManager.Instance != null ? WeatherManager.Instance.GetTemperatureModifier(worldPosition) : 0f;
        return Mathf.Clamp(baseTemp + weatherDelta, 0f, 100f);
    }

    /// 0-100, plus any transient WeatherManager storm effect (clamped, see GetTemperature).
    public static float GetMoisture(Vector3 worldPosition)
    {
        Vector3 p = (worldPosition - _planetCenter).normalized;
        float baseMoisture = Sample3DNoise(p, _moistureOffset) * 100f;
        float weatherDelta = WeatherManager.Instance != null ? WeatherManager.Instance.GetMoistureModifier(worldPosition) : 0f;
        return Mathf.Clamp(baseMoisture + weatherDelta, 0f, 100f);
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
