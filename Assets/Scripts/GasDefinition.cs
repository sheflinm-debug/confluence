/// The functional role a gas plays in respiration, assigned at sim start based on
/// the randomly generated atmosphere composition (not fixed per gas).
public enum GasRole
{
    Breathed,  // the gas all organisms respire (consume) - the dominant gas at genesis
    Expelled,  // the gas organisms exhale as waste - random, distinct from Breathed, not itself breathable
    Trace,     // present but not part of the respiration cycle - still affects ideal-mix fitness
}

public class GasDefinition
{
    public string Name;
    public GasRole Role;
    public float Fraction; // current fraction of total atmosphere (0-1), all gases sum to 1

    public float CrisisLow;  // Breathed: below this fraction, Great Gas Event fires
    public float CrisisHigh; // Expelled: above this fraction, a glut event fires

    // Phase transition: condensation to liquid when T falls below dew point.
    // Null CondensesTo means this gas never condenses at any sim-relevant conditions.
    public LiquidKind? CondensesTo;           // which liquid this gas becomes when it condenses
    public float CondensationBoilingK;        // boiling point at 1 bar (used for dew point calculation)
    public float CondensationDHvapOverR;      // ΔHvap/R in Kelvin for Clausius-Clapeyron dew point

    /// Factory that creates a GasDefinition and auto-populates condensation data for
    /// known gases. Use instead of the object initializer anywhere a new gas is minted.
    public static GasDefinition Make(string name, float fraction, GasRole role = GasRole.Trace)
    {
        var g = new GasDefinition { Name = name, Fraction = fraction, Role = role };
        switch (name)
        {
            case "H2O":
                g.CondensesTo = LiquidKind.Water;
                g.CondensationBoilingK = 373f;
                g.CondensationDHvapOverR = 4892f;
                break;
            case "NH3":
                g.CondensesTo = LiquidKind.Ammonia;
                g.CondensationBoilingK = 240f;
                g.CondensationDHvapOverR = 2814f;
                break;
            case "CH4":
                g.CondensesTo = LiquidKind.Hydrocarbon;
                g.CondensationBoilingK = 112f;
                g.CondensationDHvapOverR = 986f;
                break;
            case "C2H6":
                g.CondensesTo = LiquidKind.Hydrocarbon;
                g.CondensationBoilingK = 185f;
                g.CondensationDHvapOverR = 1768f;
                break;
            case "SO2":
                // Gameplay simplification: SO2 condenses to MoltenSulfur rather than liquid SO2,
                // which only exists under pressure. Produces correct visual for volcanic worlds.
                g.CondensesTo = LiquidKind.MoltenSulfur;
                g.CondensationBoilingK = 263f;
                g.CondensationDHvapOverR = 3960f;
                break;
            // CO2 liquid only stable above 5.1 bar — excluded from sim range.
            // N2, O2, H2S, noble gases: don't condense at any sim-reachable temperature.
        }
        return g;
    }
}
