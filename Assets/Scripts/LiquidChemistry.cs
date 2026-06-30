using UnityEngine;

public enum LiquidKind { None, Water, Hydrocarbon, Ammonia, MoltenSulfur }

public class LiquidDef
{
    public LiquidKind Kind;
    public string Name;
    public float MinK;
    public float MaxK;
    public Color Color;

    /// Vertex color to use when this liquid pools in the planet mesh, evaluated at
    /// the given temperature - only MoltenSulfur is actually temperature-dependent
    /// (Section 8: pale yellow near melting, dark red-brown above ~200C/473K); every
    /// other liquid just returns its fixed Color.
    public Color ColorAt(float tempK)
    {
        if (Kind != LiquidKind.MoltenSulfur) return Color;
        float t = Mathf.InverseLerp(MinK, 473f, tempK);
        return Color.Lerp(new Color(0.95f, 0.85f, 0.25f, 0.95f), new Color(0.45f, 0.15f, 0.08f, 0.97f), Mathf.Clamp01(t));
    }
}

/// Picks which liquid (if any) is stable on this world, cross-referencing the rolled
/// atmosphere type's dominant chemistry against the planet's current temperature -
/// a small lookup table rather than a full thermodynamics simulation, per the
/// "real logic, procedural specifics" scoping the project has used elsewhere.
/// Colors follow atmosphere_generator_spec.docx Section 8: most simple liquids are
/// near-colorless and take their tint from bulk depth (water) or stay essentially
/// clear (hydrocarbons, ammonia); only liquid sulfur is genuinely temperature-tinted.
public static class LiquidChemistry
{
    private static readonly LiquidDef Water = new LiquidDef { Kind = LiquidKind.Water, Name = "Water", MinK = 273f, MaxK = 373f, Color = new Color(0.06f, 0.22f, 0.5f, 0.9f) };
    private static readonly LiquidDef Hydrocarbon = new LiquidDef { Kind = LiquidKind.Hydrocarbon, Name = "Liquid Hydrocarbons", MinK = 90f, MaxK = 120f, Color = new Color(0.78f, 0.65f, 0.35f, 0.75f) };
    private static readonly LiquidDef Ammonia = new LiquidDef { Kind = LiquidKind.Ammonia, Name = "Liquid Ammonia", MinK = 195f, MaxK = 240f, Color = new Color(0.85f, 0.92f, 0.97f, 0.6f) };
    private static readonly LiquidDef MoltenSulfur = new LiquidDef { Kind = LiquidKind.MoltenSulfur, Name = "Molten Sulfur", MinK = 388f, MaxK = 718f, Color = new Color(0.95f, 0.85f, 0.25f, 0.95f) };

    /// Which liquid the rolled atmosphere type's dominant chemistry implies, BEFORE
    /// checking temperature - PlanetTemperature.Init uses this to bias its roll toward
    /// the liquid's stable sub-range (see that class), since otherwise the temperature
    /// is sampled across the type's full band independent of the liquid's much
    /// narrower window and rarely lands inside it by chance (this was the original bug:
    /// CO2-dominant - the single highest-weighted type - spans 180-750K, whose center
    /// sits well above water's 373K ceiling, so liquid water failed almost every time).
    public static LiquidDef GetCandidate(AtmosphereTypeDef type) => type.Name switch
    {
        "N2-O2 (biotic)" => Water,
        "Abiotic-O2 false-positive" => Water,
        "CO2-dominant (Venus/Mars-type)" => Water,
        "N2-CO2 (Titan-thick)" => Hydrocarbon,
        "CH4-N2 reducing" => Hydrocarbon,
        "Carbon-rich (CO/CO2 reducing)" => Hydrocarbon,
        "SO2-H2S volcanic" => MoltenSulfur,
        _ => null,
    };

    /// Final check: does the candidate liquid's stable range actually contain the
    /// rolled temperature? (Usually yes now that PlanetTemperature biases toward it,
    /// but not guaranteed - an unlucky roll can still leave a world dry/frozen.)
    public static LiquidDef Determine(AtmosphereTypeDef type, float planetTempK)
    {
        LiquidDef candidate = GetCandidate(type);
        if (candidate == null) return null;
        return (planetTempK >= candidate.MinK && planetTempK <= candidate.MaxK) ? candidate : null;
    }
}
