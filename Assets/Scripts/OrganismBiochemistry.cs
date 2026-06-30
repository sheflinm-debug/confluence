using System.Collections.Generic;
using UnityEngine;

public enum BackboneElement { Carbon, Silicon }

/// A candidate metabolism: backbone element + which gas it breathes in and which it
/// expels. Rolled SEPARATELY from the atmosphere composition and only loosely
/// compatible with it - the breathed gas should already be present in a plausible
/// world, but the expelled gas is a metabolic byproduct that may not exist in the
/// genesis atmosphere at all (see AtmosphereManager.AssignRespirationRoles).
public class BiochemistryDef
{
    public BackboneElement Backbone;
    public string Name;
    public string BreathedGas;
    public string ExpelledGas;
    /// AtmosphereTypeDef.Name entries this metabolism is well-suited to - a roll
    /// weight boost, not a hard requirement.
    public string[] CompatibleTypes;
}

public static class OrganismBiochemistryTable
{
    private static readonly BiochemistryDef[] _table =
    {
        new BiochemistryDef { Backbone = BackboneElement.Carbon, Name = "Carbon-based aerobic respiration",
            BreathedGas = "O2", ExpelledGas = "CO2",
            CompatibleTypes = new[] { "N2-O2 (biotic)", "Abiotic-O2 false-positive" } },

        new BiochemistryDef { Backbone = BackboneElement.Carbon, Name = "Carbon-based methanogenesis",
            BreathedGas = "H2", ExpelledGas = "CH4",
            CompatibleTypes = new[] { "CH4-N2 reducing", "N2-CO2 (Titan-thick)", "Carbon-rich (CO/CO2 reducing)" } },

        new BiochemistryDef { Backbone = BackboneElement.Carbon, Name = "Carbon-based sulfur respiration",
            BreathedGas = "SO2", ExpelledGas = "H2S",
            CompatibleTypes = new[] { "SO2-H2S volcanic" } },

        new BiochemistryDef { Backbone = BackboneElement.Carbon, Name = "Carbon-based carbon-fixing",
            BreathedGas = "CO2", ExpelledGas = "O2",
            CompatibleTypes = new[] { "CO2-dominant (Venus/Mars-type)" } },

        new BiochemistryDef { Backbone = BackboneElement.Silicon, Name = "Silicon-based vapor metabolism",
            BreathedGas = "CO2", ExpelledGas = "SiO",
            CompatibleTypes = new[] { "Silicate / mineral vapor" } },
    };

    /// Weighted roll: entries whose CompatibleTypes lists the rolled atmosphere type
    /// get a large boost (plausible pairing), everything else stays eligible but
    /// unlikely - so an exotic mismatch (e.g. methanogens on an oxygen world) can
    /// still occur rarely rather than being hard-excluded.
    public static BiochemistryDef Roll(AtmosphereTypeDef type)
    {
        var weights = new List<(BiochemistryDef def, float w)>();
        foreach (var def in _table)
        {
            float w = 1f;
            foreach (var t in def.CompatibleTypes) if (t == type.Name) w = 12f;
            weights.Add((def, w));
        }

        float total = 0f;
        foreach (var (_, w) in weights) total += w;
        float pick = Random.Range(0f, total);
        foreach (var (def, w) in weights)
        {
            if (pick < w) return def;
            pick -= w;
        }
        return _table[0];
    }
}
