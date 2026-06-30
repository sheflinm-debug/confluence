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
}
