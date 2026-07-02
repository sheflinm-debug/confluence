using UnityEngine;

/// Geology presets mapped to TectonicPlanetGenerator parameters.
public enum GeologyPreset
{
    Continents,  // Default — several mid-sized landmasses
    Pangea,      // Single supercontinent, vast shallow seas
    Islands,     // Archipelago — many small land patches, high sea level
    OceanWorld,  // Vast global ocean, rare volcanic island arcs
    Highlands,   // Rugged, tectonically active — deep rifts and tall peaks
    Random,      // Randomize all parameters
}

/// Player-selected world settings passed from the menu to SimulationBootstrap.
[System.Serializable]
public class PlanetConfig
{
    public GeologyPreset geology = GeologyPreset.Continents;

    // Fine-tune sliders (0-1 normalized, applied on top of preset defaults)
    [Range(0f, 1f)] public float seaLevel          = 0.45f; // percentile of terrain covered by liquid
    [Range(0f, 1f)] public float tectonicActivity  = 0.5f;  // drives plateCount + noiseAmplitude
    [Range(0f, 1f)] public float volcanism         = 0.5f;  // volcano count

    // Multiplayer seed (host rolls, client receives to ensure determinism)
    public int worldSeed = 0;

    // ── Derived parameter resolution ─────────────────────────────────────────

    public float SeaLevelPercentile => Mathf.Lerp(0.25f, 0.80f, seaLevel);

    public int PlateCount
    {
        get
        {
            int baseCount = geology switch
            {
                GeologyPreset.Pangea      => 3,
                GeologyPreset.Continents  => 8,
                GeologyPreset.Islands     => 18,
                GeologyPreset.OceanWorld  => 6,
                GeologyPreset.Highlands   => 12,
                GeologyPreset.Random      => Random.Range(3, 20),
                _                         => 8,
            };
            // Tectonic activity adjusts plate count ±50%
            return Mathf.RoundToInt(baseCount * Mathf.Lerp(0.5f, 1.5f, tectonicActivity));
        }
    }

    public float NoiseAmplitude => geology switch
    {
        GeologyPreset.Pangea      => 0.15f,
        GeologyPreset.Continents  => 0.35f,
        GeologyPreset.Islands     => 0.45f,
        GeologyPreset.OceanWorld  => 0.25f,
        GeologyPreset.Highlands   => 0.65f,
        GeologyPreset.Random      => Random.Range(0.15f, 0.65f),
        _                         => 0.35f,
    } * Mathf.Lerp(0.6f, 1.4f, tectonicActivity);

    public int VolcanoCount => Mathf.RoundToInt(Mathf.Lerp(0f, 16f, volcanism));

    public float BoundaryInfluence => geology switch
    {
        GeologyPreset.Pangea     => 18f,
        GeologyPreset.Highlands  => 6f,
        _                        => 10f,
    };

    /// Preset with sensible defaults.
    public static PlanetConfig Default() => new PlanetConfig
    {
        geology = GeologyPreset.Continents,
        seaLevel = 0.45f,
        tectonicActivity = 0.5f,
        volcanism = 0.5f,
        worldSeed = Random.Range(0, int.MaxValue),
    };
}
