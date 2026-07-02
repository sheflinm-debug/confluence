using UnityEngine;

/// atmosphere_generator_spec.docx Section 7: most pure gases are colorless - visible
/// atmosphere color mostly comes from haze/aerosols and a few genuinely colored trace
/// gases, not bulk gas identity. (color, typicalAlphaAtReferenceConcentration) per entry.
public static class AtmosphereColorTable
{
    private static readonly (string name, Color color, float alpha)[] _table =
    {
        ("N2",  Color.white, 0.03f),
        ("O2",  Color.white, 0.03f),
        ("H2",  Color.white, 0.03f),
        ("He",  Color.white, 0.03f),
        ("Ar",  Color.white, 0.03f),
        ("CO2", Color.white, 0.03f),
        ("CH4", new Color(0.65f, 0.85f, 0.78f), 0.22f),     // pale blue-green in thick columns
        ("NH3", new Color(0.95f, 0.92f, 0.75f), 0.30f),     // pale yellow-white haze
        ("SO2", new Color(0.85f, 0.75f, 0.40f), 0.25f),     // pale to brownish yellow
        ("H2S", new Color(0.90f, 0.85f, 0.60f), 0.15f),     // faint pale yellow
        ("H2O", new Color(0.85f, 0.90f, 0.95f), 0.10f),     // steam/vapor - near white haze
        ("NO2", new Color(0.55f, 0.30f, 0.20f), 0.18f),     // reddish-brown, strong absorber even at trace
        ("SiO", new Color(0.80f, 0.65f, 0.35f), 0.30f),     // silicate vapor byproduct - golden-bronze sheen
    };

    public static (Color color, float alpha) Get(string gasName)
    {
        foreach (var entry in _table)
            if (entry.name == gasName) return (entry.color, entry.alpha);
        return (Color.white, 0.03f); // unlisted gases default to colorless/near-invisible
    }

    /// Synthetic haze layers that aren't tied to a single tracked gas (Section 7's
    /// "Game Rendering Verdict" notes) - sulfuric acid deck, organic tholin haze, and
    /// hot silicate/metal vapor sheen. Keyed by atmosphere type name; null if none.
    public static (Color color, float alpha)? SyntheticHaze(AtmosphereTypeDef type, float pressureBar)
    {
        switch (type.Name)
        {
            case "CO2-dominant (Venus/Mars-type)" when pressureBar > type.OpaquePressureThreshold:
                return (new Color(0.90f, 0.85f, 0.55f), 0.38f); // sulfuric acid cloud deck — capped for playability
            case "N2-CO2 (Titan-thick)":
            case "CH4-N2 reducing":
                return (new Color(0.75f, 0.5f, 0.25f), 0.5f); // organic tholin haze
            case "Silicate / mineral vapor":
                return (new Color(0.80f, 0.65f, 0.35f), 0.7f); // golden-bronze mineral sheen
            default:
                return null;
        }
    }
}
