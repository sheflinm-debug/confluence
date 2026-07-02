using UnityEngine;

public enum LiquidKind { None, Water, Hydrocarbon, Ammonia, MoltenSulfur }

public class LiquidDef
{
    public LiquidKind Kind;
    public string Name;
    public float MinK;   // lower stability bound (≈ MeltingPointK) — kept for existing callers
    public float MaxK;   // upper stability bound (≈ BoilingPointK at 1 bar) — kept for existing callers
    public Color Color;

    // Phase transition constants (Clausius-Clapeyron / melting physics)
    public float MeltingPointK;       // solid → liquid at 1 bar
    public float BoilingPointK;       // liquid → gas at 1 bar
    public float DeltaHvapOverR;      // ΔHvap / R in Kelvin — Clausius-Clapeyron slope
                                      //   Water:       40700 J/mol / 8.314 = 4892 K
                                      //   Ammonia:     23400 / 8.314        = 2814 K
                                      //   Hydrocarbons (CH4 proxy): 8200 / 8.314 = 986 K
                                      //   MoltenSulfur: 45000 / 8.314       = 5411 K
    public float? SublimationPointK;  // solid → gas directly at very low pressure (null = doesn't sublime in sim range)
    public string SublimatesTo;       // gas name to inject into AtmosphereManager if sublimation occurs

    // Physical properties — identity-specific, used to derive flow simulation parameters
    // and organism-medium fitness multipliers.
    //   ViscosityMPas:     dynamic viscosity in mPa·s at typical stable temperature
    //     Water 1.0, Ammonia 0.26, Liquid Methane 0.18, Molten Sulfur ~1500 (near melting)
    //   SurfaceTensionMNm: surface tension in mN/m — governs how steeply liquid can pool
    //     Water 72, Molten Sulfur 60, Ammonia 23, Liquid Methane 14
    //   DensityKgM3:       density in kg/m³ at typical stable temperature
    //     Water 1000, Ammonia 680, Molten Sulfur 1820, Liquid Methane 450
    public float ViscosityMPas    = 1.0f;
    public float SurfaceTensionMNm = 72f;
    public float DensityKgM3      = 1000f;

    /// Normalized 0-1 flow speed factor derived from viscosity: lower viscosity → faster
    /// liquid currents and faster agent drift in the medium. Uses log scale so the extreme
    /// molten-sulfur outlier (1500 mPa·s) doesn't collapse the water/ammonia/methane range.
    /// Water=1.0, Ammonia≈0.94, Methane≈1.06, MoltenSulfur≈0.02.
    public float FlowSpeedFactor
    {
        get
        {
            // Reference: Water viscosity 1.0 mPa·s → factor 1.0
            // Log range: ln(0.18) ≈ -1.71, ln(1500) ≈ 7.31  → span 9.02
            const float logLow  = -1.71f; // ln(0.18) — liquid methane
            const float logHigh =  7.31f; // ln(1500) — molten sulfur
            float logV = Mathf.Log(Mathf.Max(ViscosityMPas, 0.01f));
            float t = Mathf.InverseLerp(logLow, logHigh, logV);
            return Mathf.Lerp(1.1f, 0.02f, t); // 1.1 at min viscosity, 0.02 at max
        }
    }

    /// Boiling point in Kelvin at the given pressure (bar), derived via inverted
    /// Clausius-Clapeyron: 1/T_bp = 1/BoilingPointK - ln(pressureBar) / DeltaHvapOverR.
    /// At near-vacuum (pressureBar <= 1e-7) the boiling point collapses to the triple
    /// point, so liquid is impossible — returns MeltingPointK as the guard value.
    public float BoilingPointAtPressureK(float pressureBar)
    {
        if (pressureBar <= 1e-7f) return MeltingPointK;
        float invT = 1f / BoilingPointK - Mathf.Log(pressureBar) / DeltaHvapOverR;
        return 1f / Mathf.Max(invT, 1e-6f);
    }

    /// Vapor pressure in bar at temperature tempK.
    /// Clausius-Clapeyron between the reference point (BoilingPointK, 1 bar) and (tempK, P_vap).
    /// Returns ≈ 0 at very low temperatures, 1 at BoilingPointK, > 1 when T exceeds boiling point.
    public float VaporPressureAt(float tempK)
    {
        float exponent = -DeltaHvapOverR * (1f / tempK - 1f / BoilingPointK);
        return Mathf.Exp(Mathf.Clamp(exponent, -30f, 10f));
    }

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
/// atmosphere type's dominant chemistry against the planet's current temperature.
/// Colors follow atmosphere_generator_spec.docx Section 8.
public static class LiquidChemistry
{
    private static readonly LiquidDef Water = new LiquidDef
    {
        Kind = LiquidKind.Water, Name = "Water",
        MinK = 273f, MaxK = 373f, Color = new Color(0.06f, 0.22f, 0.5f, 0.9f),
        MeltingPointK = 273f, BoilingPointK = 373f, DeltaHvapOverR = 4892f,
        ViscosityMPas = 1.0f, SurfaceTensionMNm = 72f, DensityKgM3 = 1000f,
    };

    private static readonly LiquidDef Hydrocarbon = new LiquidDef
    {
        Kind = LiquidKind.Hydrocarbon, Name = "Liquid Hydrocarbons",
        MinK = 90f, MaxK = 120f, Color = new Color(0.78f, 0.65f, 0.35f, 0.75f),
        MeltingPointK = 91f, BoilingPointK = 112f, DeltaHvapOverR = 986f, // methane proxy
        ViscosityMPas = 0.18f, SurfaceTensionMNm = 14f, DensityKgM3 = 450f,
    };

    private static readonly LiquidDef Ammonia = new LiquidDef
    {
        Kind = LiquidKind.Ammonia, Name = "Liquid Ammonia",
        MinK = 195f, MaxK = 240f, Color = new Color(0.85f, 0.92f, 0.97f, 0.6f),
        MeltingPointK = 195f, BoilingPointK = 240f, DeltaHvapOverR = 2814f,
        ViscosityMPas = 0.26f, SurfaceTensionMNm = 23f, DensityKgM3 = 680f,
    };

    private static readonly LiquidDef MoltenSulfur = new LiquidDef
    {
        Kind = LiquidKind.MoltenSulfur, Name = "Molten Sulfur",
        MinK = 388f, MaxK = 718f, Color = new Color(0.95f, 0.85f, 0.25f, 0.95f),
        MeltingPointK = 388f, BoilingPointK = 718f, DeltaHvapOverR = 5411f,
        ViscosityMPas = 1500f, SurfaceTensionMNm = 60f, DensityKgM3 = 1820f,
    };

    /// Which liquid the rolled atmosphere type's dominant chemistry implies, BEFORE
    /// checking temperature - PlanetTemperature.Init uses this to bias its roll toward
    /// the liquid's stable sub-range (see that class).
    public static LiquidDef GetCandidate(AtmosphereTypeDef type) => type.Name switch
    {
        "N2-O2 (biotic)"                  => Water,
        "Abiotic-O2 false-positive"        => Water,
        "CO2-dominant (Venus/Mars-type)"   => Water,
        "N2-CO2 (Titan-thick)"             => Hydrocarbon,
        "CH4-N2 reducing"                  => Hydrocarbon,
        "Carbon-rich (CO/CO2 reducing)"    => Hydrocarbon,
        "SO2-H2S volcanic"                 => MoltenSulfur,
        _                                  => null,
    };

    /// Final check: does the candidate liquid's stable range actually contain the
    /// rolled temperature at the given surface pressure?
    public static LiquidDef Determine(AtmosphereTypeDef type, float planetTempK, float pressureBar = 1f)
    {
        LiquidDef candidate = GetCandidate(type);
        if (candidate == null) return null;
        float boilingK = candidate.BoilingPointAtPressureK(pressureBar);
        if (planetTempK >= candidate.MeltingPointK && planetTempK <= boilingK)
            return candidate;
        Debug.Log($"[Liquid] {candidate.Name} rejected — boils at {boilingK:F0}K at {pressureBar:G3} bar (planet is {planetTempK:F0}K)");
        return null;
    }

    /// Returns all known liquids ordered by how close the planet temperature falls to
    /// the middle of their stable range. Used as a fallback when the atmosphere type
    /// implies no liquid — the life planet should always have surface liquid.
    /// At extreme vacuum (pressureBar <= 0.001) nothing can remain liquid; returns null.
    public static LiquidDef BestForTemperature(float planetTempK, float pressureBar = 1f)
    {
        if (pressureBar <= 0.001f) return null;
        LiquidDef[] candidates = { Water, Hydrocarbon, Ammonia, MoltenSulfur };
        LiquidDef best = null;
        float bestDist = float.MaxValue;
        foreach (var ld in candidates)
        {
            float boilingK = ld.BoilingPointAtPressureK(pressureBar);
            if (planetTempK >= ld.MinK && planetTempK <= boilingK)
                return ld; // exact match — return immediately
            float mid = (ld.MinK + boilingK) * 0.5f;
            float dist = Mathf.Abs(planetTempK - mid);
            if (dist < bestDist) { bestDist = dist; best = ld; }
        }
        return best;
    }
}
