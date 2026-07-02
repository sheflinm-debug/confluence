using UnityEngine;

/// Global mean surface temperature (Kelvin). Initialized within the rolled atmosphere
/// type's stable band (atmosphere_generator_spec.docx Section 4), then converges toward
/// a radiative energy-balance equilibrium driven by stellar luminosity, orbital distance,
/// and greenhouse-gas composition. Does not yet implement the full
/// extreme-transition/reclassification cascade from Section 5 - flagged as follow-up.
public class PlanetTemperature : MonoBehaviour
{
    public static PlanetTemperature Instance { get; private set; }

    public float CurrentK { get; private set; }
    public float BandMinK { get; private set; }
    public float BandMaxK { get; private set; }

    [Tooltip("How quickly CurrentK chases its radiative-equilibrium target (K/sec).")]
    public float driftRate = 0.02f;

    [Tooltip("Chance the genesis roll lands inside the liquid-compatible sub-range when one exists, instead of the type's full band.")]
    public float liquidBiasChance = 0.75f;

    private float _luminositySolar = 0f;
    private float _orbitAU = 0f;

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

    /// <summary>
    /// Called by SimulationBootstrap after the solar system is generated.
    /// Stores orbital parameters so Update() can derive a physics-based equilibrium temperature.
    /// </summary>
    public void SetOrbit(float luminositySolar, float orbitAU)
    {
        _luminositySolar = luminositySolar;
        _orbitAU = orbitAU;

        float T_eq = 278f * Mathf.Pow(_luminositySolar, 0.25f) / Mathf.Sqrt(Mathf.Max(_orbitAU, 0.001f));
        Debug.Log($"[Temperature] T_eq={T_eq:F0}K at {orbitAU:F2}AU (L={luminositySolar:F2}L☉) — greenhouse will add ΔT based on atmosphere");
    }

    void Awake() => Instance = this;

    void Update()
    {
        float target;

        if (_luminositySolar <= 0f)
        {
            // SetOrbit not yet called (cinematic/pre-bootstrap phase) — use band centre as fallback.
            target = Mathf.Lerp(BandMinK, BandMaxK, 0.5f);
        }
        else
        {
            // Radiative equilibrium: T_eq = 278 * L^0.25 / sqrt(d)
            float T_eq = 278f * Mathf.Pow(_luminositySolar, 0.25f) / Mathf.Sqrt(Mathf.Max(_orbitAU, 0.001f));

            // Greenhouse contribution: real gases that absorb IR
            // CO2, CH4, SO2, H2O are greenhouse gases; their effect scales with fraction * pressure
            float greenhouseDeltaK = 0f;
            if (AtmosphereManager.Instance != null)
            {
                float pressureBar = AtmosphereManager.Instance.PressureBar;
                foreach (var gas in AtmosphereManager.Instance.Gases)
                {
                    float ghStrength = gas.Name switch {
                        "CO2" => 80f,
                        "CH4" => 25f,
                        "SO2" => 15f,
                        "H2O" => 40f,
                        "NH3" => 30f,
                        _ => 0f
                    };
                    greenhouseDeltaK += ghStrength * gas.Fraction * Mathf.Sqrt(Mathf.Clamp(pressureBar, 0f, 10f));
                }
            }

            target = T_eq + greenhouseDeltaK;
        }

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
        if (GameHUD.SuppressRawOverlays) return;
        GUI.Label(new Rect(10, 10, 300, 20), $"Surface temp: {CurrentK:F0} K (band {BandMinK:F0}-{BandMaxK:F0} K)");
        if (AtmosphereManager.Instance != null)
        {
            float p = AtmosphereManager.Instance.PressureBar;
            string pStr = p < 0.001f ? $"{p * 1e6f:F1} ubar" : p < 1f ? $"{p * 1000f:F1} mbar" : $"{p:F2} bar";
            GUI.Label(new Rect(10, 30, 300, 20), $"Surface pressure: {pStr} ({AtmosphereManager.Instance.RolledType.Name})");
        }
    }
}
