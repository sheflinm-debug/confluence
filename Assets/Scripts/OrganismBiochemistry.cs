using System.Collections.Generic;
using UnityEngine;

public enum BackboneElement
{
    Carbon,    // ★★★★ confirmed; electrochemical neural signaling, water solvent
    Silicon,   // ★★★  semiconductor signaling, fluorocarbon solvent, O2 lethal
    Germanium, // ★★   like silicon, narrower bandgap, thermally sensitive
    Tin,       // ★★   metallic/semi dual-phase, phase-change excitability
    Boron,     // ★★   proton/photonic signaling, anhydrous solvent
    Nitrogen,  // ★★   ammonium-gradient in liquid ammonia, very slow
    Phosphorus,// ★★   phosphorylation-cascade signaling, high metabolic cost
    Sulfur,    // ★★   redox-wave (disulfide cycling), hot volcanic worlds
}

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
            BreathedGas = "F2", ExpelledGas = "SiF4",
            CompatibleTypes = new[] { "Silicate / mineral vapor" } },

        new BiochemistryDef { Backbone = BackboneElement.Germanium, Name = "Germanium-based fluoride metabolism",
            BreathedGas = "F2", ExpelledGas = "GeF4",
            CompatibleTypes = new[] { "Silicate / mineral vapor" } },

        new BiochemistryDef { Backbone = BackboneElement.Tin, Name = "Tin-based fluoride metabolism",
            BreathedGas = "F2", ExpelledGas = "SnF4",
            CompatibleTypes = new[] { "Silicate / mineral vapor" } },

        new BiochemistryDef { Backbone = BackboneElement.Boron, Name = "Boron-based diborane metabolism",
            BreathedGas = "H2", ExpelledGas = "B2H6",
            CompatibleTypes = new[] { "CH4-N2 reducing" } },

        new BiochemistryDef { Backbone = BackboneElement.Nitrogen, Name = "Nitrogen-based ammonia metabolism",
            BreathedGas = "NH3", ExpelledGas = "N2",
            CompatibleTypes = new[] { "N2-CO2 (Titan-thick)" } },

        new BiochemistryDef { Backbone = BackboneElement.Phosphorus, Name = "Phosphorus-based phosphine metabolism",
            BreathedGas = "H2", ExpelledGas = "PH3",
            CompatibleTypes = new[] { "CH4-N2 reducing", "Carbon-rich (CO/CO2 reducing)" } },

        new BiochemistryDef { Backbone = BackboneElement.Sulfur, Name = "Sulfur-based H2S metabolism",
            BreathedGas = "H2S", ExpelledGas = "SO2",
            CompatibleTypes = new[] { "SO2-H2S volcanic" } },
    };

    /// Weighted roll informed by actual post-constraint gas composition.
    /// Entries whose CompatibleTypes matches the atmosphere type get a boost; entries
    /// whose backbone cannot survive the actual gas mix (per backbone_gas_effects_reference.md)
    /// are zeroed out entirely. Breathed-gas availability also scales the weight.
    public static BiochemistryDef Roll(AtmosphereTypeDef type, IReadOnlyList<GasDefinition> gases)
    {
        var weights = new List<(BiochemistryDef def, float w)>();
        foreach (var def in _table)
        {
            // Start with compatible-type soft bonus
            float w = 1f;
            foreach (var t in def.CompatibleTypes) if (t == type.Name) w = 12f;

            // Hard-zero if any lethal gas for this backbone exceeds its survivable threshold
            if (!IsViable(def.Backbone, gases))
            {
                w = 0f;
            }
            else
            {
                // Scale by breathed-gas availability (absent = small penalty, abundant = bonus)
                float breathedFrac = GasFraction(def.BreathedGas, gases);
                if (breathedFrac >= 0.05f)      w *= 2.0f;   // plentiful
                else if (breathedFrac >= 0.01f) w *= 1.2f;   // present but sparse
                else                            w *= 0.5f;   // absent — will be injected at genesis
            }

            weights.Add((def, w));
        }

        float total = 0f;
        foreach (var (_, w) in weights) total += w;

        // Fallback: if all weights zeroed (shouldn't happen on normal worlds), allow anything
        if (total <= 0f)
        {
            Debug.LogWarning("[Biochemistry] All candidates zeroed by viability filter — using unweighted fallback.");
            foreach (var def in _table) weights[weights.FindIndex(x => x.def == def)] = (def, 1f);
            total = _table.Length;
        }

        float pick = Random.Range(0f, total);
        foreach (var (def, w) in weights)
        {
            if (pick < w) return def;
            pick -= w;
        }
        return _table[0];
    }

    /// Returns false if the backbone cannot survive due to a lethal atmospheric gas
    /// exceeding its toxicity threshold (backbone_gas_effects_reference.md).
    private static bool IsViable(BackboneElement backbone, IReadOnlyList<GasDefinition> gases)
    {
        switch (backbone)
        {
            case BackboneElement.Silicon:
            case BackboneElement.Germanium:
            case BackboneElement.Tin:
                // O2 oxidizes/fossilizes; H2O hydrolyzes Si-Si bonds; Cl2 corrosive
                if (GasFraction("O2",  gases) > 0.01f) return false;
                if (GasFraction("H2O", gases) > 0.05f) return false;
                if (GasFraction("Cl2", gases) > 0.001f) return false;
                break;

            case BackboneElement.Boron:
                // Boranes pyrophoric in O2; water destroys boranes
                if (GasFraction("O2",  gases) > 0.01f) return false;
                if (GasFraction("H2O", gases) > 0.02f) return false;
                if (GasFraction("F2",  gases) > 0.001f) return false;
                if (GasFraction("Cl2", gases) > 0.001f) return false;
                break;

            case BackboneElement.Phosphorus:
                // White P pyrophoric in O2; reacts with H2O; halides corrosive
                if (GasFraction("O2",  gases) > 0.01f) return false;
                if (GasFraction("H2O", gases) > 0.05f) return false;
                if (GasFraction("F2",  gases) > 0.001f) return false;
                if (GasFraction("Cl2", gases) > 0.001f) return false;
                break;

            case BackboneElement.Carbon:
                // NH3 corrosive at high conc; halogens destroy organics
                if (GasFraction("NH3", gases) > 0.20f) return false;
                if (GasFraction("F2",  gases) > 0.001f) return false;
                if (GasFraction("Cl2", gases) > 0.001f) return false;
                break;

            case BackboneElement.Nitrogen:
                // NO2 / reactive nitrogen oxides lethal; halogens lethal
                if (GasFraction("NO2", gases) > 0.01f) return false;
                if (GasFraction("Cl2", gases) > 0.001f) return false;
                break;

            case BackboneElement.Sulfur:
                // Cl2 forms toxic sulfur chlorides
                if (GasFraction("Cl2", gases) > 0.001f) return false;
                break;
        }
        return true;
    }

    private static float GasFraction(string name, IReadOnlyList<GasDefinition> gases)
    {
        foreach (var g in gases) if (g.Name == name) return g.Fraction;
        return 0f;
    }
}
