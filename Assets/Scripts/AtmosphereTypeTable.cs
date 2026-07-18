using System.Collections.Generic;
using UnityEngine;

/// Section 7's three practical render verdicts. CO2-dominant is the one type whose
/// verdict isn't fixed - it branches on the rolled pressure (Mars-thin vs Venus-thick).
public enum AtmosphereRenderVerdict { Transparent, Translucent, Opaque, BranchOnPressure }

/// One row of the atmosphere_generator_spec.docx Section 2 weighted table, scoped to
/// rocky/terrestrial worlds (this sim has no gas-giant world class, so H2-He primordial
/// and He-dominant stripped - giant/sub-Neptune only - are excluded from the roll).
public class AtmosphereTypeDef
{
    public string Name;
    public int Weight;
    /// (gas name, min fraction, max fraction) for the type's dominant species. The first
    /// entry is treated as the Breathed gas, the second as Expelled, for our respiration
    /// mechanic - a simplification of the spec's pure-composition table.
    public (string name, float min, float max)[] DominantSpecies;
    public float TempMinK;
    public float TempMaxK;
    public string[] TracePool;

    // Section 3 pressure band + Section 7 render verdict.
    public float PressureMinBar;
    public float PressureMaxBar;
    public AtmosphereRenderVerdict RenderVerdict;
    /// Pressure (bar) above which a BranchOnPressure type renders opaque instead of translucent.
    public float OpaquePressureThreshold;
}

public static class AtmosphereTypeTable
{
    public static readonly AtmosphereTypeDef[] Table =
    {
        new AtmosphereTypeDef { Name = "N2-O2 (biotic)", Weight = 11,
            DominantSpecies = new (string,float,float)[] { ("O2",0.10f,0.30f), ("N2",0.70f,0.85f) },
            TempMinK = 250, TempMaxK = 320, TracePool = new[] { "CO2", "Ar", "H2O" },
            PressureMinBar = 0.3f, PressureMaxBar = 3f, RenderVerdict = AtmosphereRenderVerdict.Transparent },

        new AtmosphereTypeDef { Name = "N2-CO2 (Titan-thick)", Weight = 10,
            DominantSpecies = new (string,float,float)[] { ("N2",0.60f,0.98f), ("CH4",0.01f,0.15f) },
            TempMinK = 80, TempMaxK = 200, TracePool = new[] { "CO2", "Ar" },
            PressureMinBar = 0.3f, PressureMaxBar = 3f, RenderVerdict = AtmosphereRenderVerdict.Opaque },

        new AtmosphereTypeDef { Name = "CO2-dominant (Venus/Mars-type)", Weight = 9,
            DominantSpecies = new (string,float,float)[] { ("CO2",0.90f,0.97f), ("N2",0.02f,0.08f) },
            TempMinK = 180, TempMaxK = 750, TracePool = new[] { "SO2", "Ar", "H2O" },
            PressureMinBar = 0.006f, PressureMaxBar = 90f, RenderVerdict = AtmosphereRenderVerdict.BranchOnPressure, OpaquePressureThreshold = 1.5f },

        new AtmosphereTypeDef { Name = "Steam / supercritical H2O", Weight = 8,
            DominantSpecies = new (string,float,float)[] { ("H2O",0.50f,0.99f), ("CO2",0.01f,0.30f) },
            TempMinK = 650, TempMaxK = 1500, TracePool = new[] { "N2" },
            PressureMinBar = 10f, PressureMaxBar = 300f, RenderVerdict = AtmosphereRenderVerdict.Opaque },

        new AtmosphereTypeDef { Name = "Silicate / mineral vapor", Weight = 4,
            DominantSpecies = new (string,float,float)[] { ("O2",0.30f,0.60f), ("CO2",0.10f,0.30f) },
            TempMinK = 1800, TempMaxK = 3000, TracePool = new[] { "SO2" },
            PressureMinBar = 0.001f, PressureMaxBar = 0.1f, RenderVerdict = AtmosphereRenderVerdict.Opaque },

        new AtmosphereTypeDef { Name = "SO2-H2S volcanic", Weight = 6,
            DominantSpecies = new (string,float,float)[] { ("SO2",0.20f,0.50f), ("H2S",0.10f,0.30f) },
            TempMinK = 200, TempMaxK = 500, TracePool = new[] { "CO2", "N2" },
            PressureMinBar = 0.01f, PressureMaxBar = 5f, RenderVerdict = AtmosphereRenderVerdict.Translucent },

        new AtmosphereTypeDef { Name = "CH4-N2 reducing", Weight = 9,
            DominantSpecies = new (string,float,float)[] { ("N2",0.50f,0.90f), ("CH4",0.10f,0.50f) },
            TempMinK = 60, TempMaxK = 110, TracePool = new[] { "H2" },
            PressureMinBar = 0.3f, PressureMaxBar = 3f, RenderVerdict = AtmosphereRenderVerdict.Translucent },

        new AtmosphereTypeDef { Name = "Carbon-rich (CO/CO2 reducing)", Weight = 6,
            DominantSpecies = new (string,float,float)[] { ("CO2",0.30f,0.70f), ("H2S",0.05f,0.20f) },
            TempMinK = 300, TempMaxK = 900, TracePool = new[] { "CH4" },
            PressureMinBar = 0.1f, PressureMaxBar = 10f, RenderVerdict = AtmosphereRenderVerdict.Translucent },

        new AtmosphereTypeDef { Name = "Thin exosphere / negligible", Weight = 9,
            DominantSpecies = new (string,float,float)[] { ("Ar",0.40f,0.70f), ("N2",0.20f,0.50f) },
            TempMinK = 100, TempMaxK = 700, TracePool = new[] { "O2" },
            PressureMinBar = 0.000001f, PressureMaxBar = 0.0001f, RenderVerdict = AtmosphereRenderVerdict.Transparent },

        new AtmosphereTypeDef { Name = "Abiotic-O2 false-positive", Weight = 5,
            DominantSpecies = new (string,float,float)[] { ("O2",0.05f,0.40f), ("CO2",0.05f,0.20f) },
            TempMinK = 260, TempMaxK = 380, TracePool = new[] { "N2", "H2O" },
            PressureMinBar = 0.3f, PressureMaxBar = 2f, RenderVerdict = AtmosphereRenderVerdict.Transparent },
    };

    /// Section 1 Step 3: roll weighted type, reweighted (not dictated) by the rolled
    /// rock archetype's AtmoBoosts, then renormalized.
    public static AtmosphereTypeDef Roll(RockArchetypeDef archetype)
    {
        float total = 0f;
        var weights = new float[Table.Length];
        for (int i = 0; i < Table.Length; i++)
        {
            float w = Table[i].Weight;
            if (archetype.AtmoBoosts.TryGetValue(Table[i].Name, out float boost)) w *= boost;
            weights[i] = w;
            total += w;
        }

        float pick = Random.Range(0f, total);
        for (int i = 0; i < Table.Length; i++)
        {
            if (pick < weights[i]) return Table[i];
            pick -= weights[i];
        }
        return Table[0];
    }

    /// Section 1 Step 4: roll a reference pressure within the type's band (log-uniform,
    /// since several bands span multiple orders of magnitude).
    public static float RollPressureBar(AtmosphereTypeDef type)
    {
        float logMin = Mathf.Log10(Mathf.Max(type.PressureMinBar, 1e-7f));
        float logMax = Mathf.Log10(Mathf.Max(type.PressureMaxBar, type.PressureMinBar * 1.01f));
        return Mathf.Pow(10f, Random.Range(logMin, logMax));
    }
}
