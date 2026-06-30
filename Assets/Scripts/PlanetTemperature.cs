using UnityEngine;

/// Global mean surface temperature (Kelvin). Initialized within the rolled atmosphere
/// type's stable band (atmosphere_generator_spec.docx Section 4), then drifts over time
/// based on a simplified greenhouse-gas coupling: CO2/CH4/SO2 fraction raises the
/// equilibrium target, everything else is neutral. Does not yet implement the full
/// extreme-transition/reclassification cascade from Section 5 - flagged as follow-up.
public class PlanetTemperature : MonoBehaviour
{
    public static PlanetTemperature Instance { get; private set; }

    public float CurrentK { get; private set; }
    public float BandMinK { get; private set; }
    public float BandMaxK { get; private set; }

    [Tooltip("How quickly CurrentK chases its greenhouse-driven target (K/sec).")]
    public float driftRate = 0.02f;
    [Tooltip("How many K above BandMaxK the greenhouse target can climb at 100% greenhouse-gas fraction.")]
    public float maxGreenhouseBoostK = 60f;

    private static readonly string[] GreenhouseGases = { "CO2", "CH4", "SO2", "H2O" };

    [Tooltip("Chance the genesis roll lands inside the liquid-compatible sub-range when one exists, instead of the type's full band.")]
    public float liquidBiasChance = 0.75f;

    public void Init(AtmosphereTypeDef type, LiquidDef liquidCandidate = null)
    {
        Instance = this;
        BandMinK = type.TempMinK;
        BandMaxK = type.TempMaxK;

        if (liquidCandidate != null)
        {
            float lo = Mathf.Max(BandMinK, liquidCandidate.MinK);
            float hi = Mathf.Min(BandMaxK, liquidCandidate.MaxK);
            if (lo < hi && Random.value < liquidBiasChance)
            {
                CurrentK = Random.Range(lo, hi);
                return;
            }
        }

        // No (or unlucky) liquid bias - fall back to centering within the full band.
        CurrentK = Mathf.Lerp(BandMinK, BandMaxK, Random.Range(0.35f, 0.65f));
    }

    void Awake() => Instance = this;

    void Update()
    {
        if (AtmosphereManager.Instance == null) return;

        float greenhouseFraction = 0f;
        foreach (var gas in AtmosphereManager.Instance.Gases)
        {
            foreach (var ghg in GreenhouseGases)
                if (gas.Name.StartsWith(ghg)) { greenhouseFraction += gas.Fraction; break; }
        }

        float target = Mathf.Lerp(BandMinK, BandMaxK, 0.5f) + greenhouseFraction * maxGreenhouseBoostK;

        // Layer the orbital seasonal flux swing (perihelion/aphelion) on top of the
        // greenhouse-driven target, additively - it oscillates around the same target
        // rather than replacing it, so greenhouse drift and seasons both stay visible.
        if (OrbitalSeasons.Instance != null)
            target += OrbitalSeasons.Instance.ApproxSeasonalDeltaK(target);

        CurrentK = Mathf.MoveTowards(CurrentK, target, driftRate * Time.deltaTime * 100f);
    }

    /// 0 = at band center (most stable), 1+ = at/beyond the band edge (extreme).
    public float StressFraction()
    {
        float mid = (BandMinK + BandMaxK) / 2f;
        float halfRange = (BandMaxK - BandMinK) / 2f;
        if (halfRange <= 0f) return 0f;
        return Mathf.Abs(CurrentK - mid) / halfRange;
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 300, 20), $"Surface temp: {CurrentK:F0} K (band {BandMinK:F0}-{BandMaxK:F0} K)");
        if (AtmosphereManager.Instance != null)
        {
            float p = AtmosphereManager.Instance.PressureBar;
            string pStr = p < 0.001f ? $"{p * 1e6f:F1} ubar" : p < 1f ? $"{p * 1000f:F1} mbar" : $"{p:F2} bar";
            GUI.Label(new Rect(10, 30, 300, 20), $"Surface pressure: {pStr} ({AtmosphereManager.Instance.RolledType.Name})");
        }
    }
}
