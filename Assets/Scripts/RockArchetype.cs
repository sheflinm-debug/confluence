using System.Collections.Generic;
using UnityEngine;

/// One planet-wide rock/mantle archetype (atmosphere_generator_spec.docx Section 6).
/// Rolled once at world-gen, before the atmosphere type roll - its AtmoBoosts table
/// reweights (not dictates) which atmosphere types are likely, and its colors drive
/// terrain rendering instead of a flat per-biome green.
public enum RockArchetypeId
{
    UltramaficReducing, MaficBasaltic, FelsicGranitic, OxidizedIronRich,
    CarbonateEvaporite, SulfideSulfurRich, MetalRichStripped, IcySilicateMix,
    CarbonRichReduced, GlassyVitrified
}

public class RockArchetypeDef
{
    public RockArchetypeId Id;
    public int Weight;
    public Color Primary;
    public Color Accent;
    /// Multiplier applied to matching AtmosphereTypeDef.Name entries when reweighting
    /// the Section 2 table (see AtmosphereTypeTable.RollType). Unlisted types get 1x.
    public Dictionary<string, float> AtmoBoosts;
}

public static class RockArchetypeTable
{
    private static readonly RockArchetypeDef[] _table =
    {
        new RockArchetypeDef { Id = RockArchetypeId.UltramaficReducing, Weight = 8,
            Primary = new Color32(0x2F,0x3B,0x26,0xFF), Accent = new Color32(0x1A,0x1A,0x1A,0xFF),
            AtmoBoosts = new Dictionary<string, float> { {"CH4-N2 reducing",2.5f}, {"Carbon-rich (CO/CO2 reducing)",2.5f}, {"SO2-H2S volcanic",2f}, {"N2-O2 (biotic)",0.2f}, {"Abiotic-O2 false-positive",0.2f} } },

        new RockArchetypeDef { Id = RockArchetypeId.MaficBasaltic, Weight = 14,
            Primary = new Color32(0x36,0x45,0x4F,0xFF), Accent = new Color32(0x5C,0x40,0x33,0xFF),
            AtmoBoosts = new Dictionary<string, float>() },

        new RockArchetypeDef { Id = RockArchetypeId.FelsicGranitic, Weight = 9,
            Primary = new Color32(0xD8,0xC7,0xB8,0xFF), Accent = new Color32(0xE8,0xE0,0xD5,0xFF),
            AtmoBoosts = new Dictionary<string, float> { {"N2-O2 (biotic)",2.5f}, {"Abiotic-O2 false-positive",2f}, {"CO2-dominant (Venus/Mars-type)",1.5f} } },

        new RockArchetypeDef { Id = RockArchetypeId.OxidizedIronRich, Weight = 10,
            Primary = new Color32(0x9C,0x4A,0x1A,0xFF), Accent = new Color32(0xB8,0x5C,0x38,0xFF),
            AtmoBoosts = new Dictionary<string, float> { {"CO2-dominant (Venus/Mars-type)",2f}, {"Thin exosphere / negligible",2f}, {"CH4-N2 reducing",0.3f} } },

        new RockArchetypeDef { Id = RockArchetypeId.CarbonateEvaporite, Weight = 5,
            Primary = new Color32(0xED,0xE6,0xD6,0xFF), Accent = new Color32(0xC9,0xBF,0xA5,0xFF),
            AtmoBoosts = new Dictionary<string, float> { {"CO2-dominant (Venus/Mars-type)",2f}, {"SO2-H2S volcanic",2f} } },

        new RockArchetypeDef { Id = RockArchetypeId.SulfideSulfurRich, Weight = 6,
            Primary = new Color32(0xD4,0xAC,0x0D,0xFF), Accent = new Color32(0x2B,0x2B,0x2B,0xFF),
            AtmoBoosts = new Dictionary<string, float> { {"SO2-H2S volcanic",3f} } },

        new RockArchetypeDef { Id = RockArchetypeId.MetalRichStripped, Weight = 4,
            Primary = new Color32(0x8C,0x8C,0x8C,0xFF), Accent = new Color32(0xA0,0x52,0x2D,0xFF),
            AtmoBoosts = new Dictionary<string, float> { {"Thin exosphere / negligible",3f}, {"Silicate / mineral vapor",1.5f} } },

        new RockArchetypeDef { Id = RockArchetypeId.IcySilicateMix, Weight = 10,
            Primary = new Color32(0xDC,0xE6,0xE8,0xFF), Accent = new Color32(0xA9,0xC4,0xCA,0xFF),
            AtmoBoosts = new Dictionary<string, float> { {"N2-CO2 (Titan-thick)",2f}, {"CH4-N2 reducing",2f}, {"N2-O2 (biotic)",1.5f} } },

        new RockArchetypeDef { Id = RockArchetypeId.CarbonRichReduced, Weight = 4,
            Primary = new Color32(0x1C,0x1C,0x1C,0xFF), Accent = new Color32(0x4A,0x4A,0x4A,0xFF),
            AtmoBoosts = new Dictionary<string, float> { {"Carbon-rich (CO/CO2 reducing)",3f}, {"CH4-N2 reducing",2f}, {"CO2-dominant (Venus/Mars-type)",0.2f}, {"N2-O2 (biotic)",0.2f} } },

        new RockArchetypeDef { Id = RockArchetypeId.GlassyVitrified, Weight = 3,
            Primary = new Color32(0x0B,0x0B,0x0B,0xFF), Accent = new Color32(0x5A,0x5A,0x5A,0xFF),
            AtmoBoosts = new Dictionary<string, float>() },
    };

    public static RockArchetypeDef Roll()
    {
        int total = 0;
        foreach (var a in _table) total += a.Weight;
        int pick = Random.Range(0, total);
        foreach (var a in _table)
        {
            if (pick < a.Weight) return a;
            pick -= a.Weight;
        }
        return _table[0];
    }
}
