using UnityEngine;

/// Drives the life planet's elliptical orbit (eccentricity) and axial tilt, producing a
/// real seasonal cycle: star-distance varies between perihelion/aphelion over the orbital
/// period, modulating received stellar flux (consumed by PlanetTemperature as an additive
/// seasonal delta layered on top of its existing greenhouse-gas drift), while axial tilt
/// makes per-latitude solar exposure swing between summer/winter extremes over the same
/// period (consumed by DayNightCycle.SolarExposure, which already gates AgentController
/// photosynthesis and AtmosphereManager gas exchange - no call sites needed to change).
///
/// Lifecycle: rolled (eccentricity/tilt) alongside StarSystemGenerator.Generate during
/// world gen in SimulationBootstrap.Start(), via Init(). Orbital phase does NOT advance
/// until BeginGameplay() is called from GenesisCinematic's onComplete callback - same
/// handoff point DeepTimeClock keeps ticking through, so the genesis cinematic itself
/// never sees a moving season.
public class OrbitalSeasons : MonoBehaviour
{
    public static OrbitalSeasons Instance { get; private set; }

    [Header("Timing")]
    [Tooltip("Real-time seconds for one full orbital period (a year) once gameplay begins. " +
             "Tuned so seasonal shifts are clearly visible within a few minutes of play.")]
    public float orbitalPeriodSeconds = 180f;

    public float Eccentricity { get; private set; }
    public float AxialTiltDeg { get; private set; }
    public float SemiMajorAxisAU { get; private set; }

    /// 0..1 fraction of the way through the current orbital period.
    public float OrbitalPhase01 { get; private set; }
    /// Current star distance in AU (perihelion..aphelion as the orbit progresses).
    public float CurrentDistanceAU { get; private set; }
    /// Multiplier on equilibrium temperature target from eccentricity-driven flux change
    /// relative to the semi-major-axis baseline (1 = baseline, >1 = closer/hotter, <1 = farther/colder).
    public float FluxMultiplier { get; private set; } = 1f;

    private bool _running;
    private float _t;

    void Awake() => Instance = this;

    /// Rolls/records orbital shape at world-gen time. Does not start ticking yet.
    public void Init(float semiMajorAxisAU, float eccentricity, float axialTiltDeg)
    {
        Instance = this;
        SemiMajorAxisAU = semiMajorAxisAU;
        Eccentricity = Mathf.Clamp(eccentricity, 0f, 0.9f);
        AxialTiltDeg = Mathf.Clamp(axialTiltDeg, 0f, 90f);
        OrbitalPhase01 = 0f;
        CurrentDistanceAU = semiMajorAxisAU;
        FluxMultiplier = 1f;
        _running = false;
        _t = 0f;
    }

    /// Starts the seasonal cycle ticking - called once live gameplay begins (post-cinematic).
    public void BeginGameplay()
    {
        _running = true;
        _t = 0f;
    }

    void Update()
    {
        if (!_running || orbitalPeriodSeconds <= 0f) return;

        _t += Time.deltaTime;
        if (_t >= orbitalPeriodSeconds) _t -= orbitalPeriodSeconds;
        OrbitalPhase01 = _t / orbitalPeriodSeconds;

        // True orbital-distance via the standard ellipse polar equation, parameterized by
        // true anomaly theta swept linearly over the period (a simplifying approximation -
        // skips solving Kepler's equation for non-uniform angular speed, which is fine for
        // a gameplay-visible seasonal wobble rather than an orrery-accurate sim).
        float theta = OrbitalPhase01 * Mathf.PI * 2f;
        float e = Eccentricity;
        CurrentDistanceAU = SemiMajorAxisAU * (1f - e * e) / (1f + e * Mathf.Cos(theta));

        // Flux ~ 1/d^2 relative to the semi-major-axis baseline distance.
        float ratio = SemiMajorAxisAU / Mathf.Max(CurrentDistanceAU, 0.0001f);
        FluxMultiplier = ratio * ratio;
    }

    /// Seasonal multiplier on a given latitude's solar exposure (1 = no seasonal effect,
    /// i.e. equator or zero tilt). Combines axial-tilt subsolar-latitude offset with the
    /// orbital phase so each hemisphere swings between summer/winter across the period.
    /// latitudeSin01 should be sin(latitude) in [-1, 1] (1 = north pole, -1 = south pole).
    public float SeasonalExposureMultiplier(float latitudeSin01)
    {
        if (AxialTiltDeg <= 0.0001f) return 1f;

        // Subsolar latitude oscillates sinusoidally with axial tilt over the orbital period
        // (mirrors Earth: +tilt at summer solstice, -tilt at winter solstice, 0 at equinoxes).
        float subsolarLatSin = Mathf.Sin(AxialTiltDeg * Mathf.Deg2Rad) * Mathf.Sin(OrbitalPhase01 * Mathf.PI * 2f);

        // Closer a latitude is to the current subsolar latitude, the more seasonal boost
        // it gets; the opposite hemisphere gets a corresponding dip. Centered at 1 so it
        // layers multiplicatively on top of the existing day/night dot-product term.
        float alignment = latitudeSin01 * subsolarLatSin; // -1..1
        return Mathf.Clamp(1f + alignment * 0.6f, 0.1f, 1.6f);
    }

    /// Human-readable season label for the OnGUI readout. Uses northern-hemisphere framing
    /// (matches the +tilt = north-summer convention used above).
    public string SeasonLabel()
    {
        if (AxialTiltDeg <= 0.0001f) return "No axial tilt (no seasons)";
        float theta = OrbitalPhase01;
        if (theta < 0.125f || theta >= 0.875f) return "N. Summer / S. Winter (solstice)";
        if (theta < 0.375f) return "N. Autumn / S. Spring (equinox)";
        if (theta < 0.625f) return "N. Winter / S. Summer (solstice)";
        return "N. Spring / S. Autumn (equinox)";
    }

    /// Approximate seasonal temperature delta (K) from flux variation alone, for the GUI
    /// readout - separate from PlanetTemperature's actual applied delta but uses the same
    /// derivation so the two stay consistent.
    public float ApproxSeasonalDeltaK(float baselineK)
    {
        // Teq scales with flux^0.25; delta relative to baseline equilibrium temp.
        return baselineK * (Mathf.Pow(FluxMultiplier, 0.25f) - 1f);
    }

    void OnGUI()
    {
        if (!_running || GameHUD.SuppressRawOverlays) return;
        float deltaK = ApproxSeasonalDeltaK(PlanetTemperature.Instance != null ? PlanetTemperature.Instance.CurrentK : 280f);
        GUI.Label(new Rect(10, 50, 420, 20),
            $"Orbit: {OrbitalPhase01 * 100f:F0}% (d={CurrentDistanceAU:F3} AU, e={Eccentricity:F2}, tilt={AxialTiltDeg:F0} deg)");
        GUI.Label(new Rect(10, 70, 420, 20),
            $"Season: {SeasonLabel()} | seasonal flux delta ~{deltaK:+0.0;-0.0} K");
    }
}
