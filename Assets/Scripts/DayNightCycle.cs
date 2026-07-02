using UnityEngine;

/// Rotates a directional light around the planet to simulate a day/night cycle.
/// Exposes IsDaySide() so agents and AtmosphereManager can query solar exposure.
///
/// ROTATION CONSISTENCY (task: "real planetary rotation"): the life-planet's VISUAL mesh
/// (GenesisCinematic's "PlanetVisual" child) now actually spins on its axis, driven by
/// SolarSystemRuntime.LifePlanetRotationDeg - see that class's header for why it's a
/// separate child transform rather than the logical planet root (agent/collider desync
/// avoidance). This script deliberately keeps the OLD "sun-rotates-light" approach instead
/// of switching to "derive lighting from planet rotation" - they are mathematically
/// equivalent (a sun appearing to circle a stationary planet once per day looks identical
/// to a planet spinning once per day under a fixed-direction sun), and this approach is
/// already decoupled from any particular transform's rotation (SolarExposure only compares
/// world-space SunDirection against a world-space surface normal, neither of which reads
/// transform.rotation off the planet mesh). The two are kept EXPLICITLY consistent by
/// convention rather than by shared code: `dayLengthSeconds` here should be set equal to
/// SolarSystemRuntime.planetRotationPeriodSeconds so the visible terrain spin and the
/// light's circuit complete at the same rate (one full mesh rotation == one day/night
/// cycle, matching how an external observer would actually see a spinning lit planet).
public class DayNightCycle : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Real-time seconds per full day/night cycle. Keep equal to SolarSystemRuntime.planetRotationPeriodSeconds - see class header.")]
    public float dayLengthSeconds = 60f;

    [Header("References")]
    public Light sunLight;

    public static DayNightCycle Instance { get; private set; }

    /// Direction the sun is currently shining FROM (world space unit vector).
    public Vector3 SunDirection { get; private set; } = Vector3.up;

    void Awake() => Instance = this;

    void Start()
    {
        if (sunLight == null)
        {
            GameObject sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform);
            sunLight = sunGo.AddComponent<Light>();
            sunLight.type = LightType.Directional;
            sunLight.intensity = 1.2f;
            sunLight.color = new Color(1f, 0.95f, 0.8f);
        }

        SunDirection = Vector3.up;
    }

    void Update()
    {
        float angle = (Time.time / dayLengthSeconds) * 360f;
        SunDirection = Quaternion.Euler(0f, 0f, angle) * Vector3.up;
        sunLight.transform.rotation = Quaternion.LookRotation(-SunDirection);
    }

    /// Returns 0..1: 1 = facing directly toward the sun, 0 = facing directly away.
    /// Values below 0.5 are night-side; above 0.5 are day-side. When OrbitalSeasons is
    /// active, the day/night rotation term is additionally scaled by a per-latitude
    /// seasonal multiplier (axial tilt + orbital phase), so a given latitude's exposure
    /// also swings between summer/winter over the orbital period, not just day/night.
    public float SolarExposure(Vector3 surfaceNormal)
    {
        float dayNightTerm = Mathf.Clamp01(Vector3.Dot(surfaceNormal, SunDirection));

        if (OrbitalSeasons.Instance != null)
        {
            // Rotation axis is assumed to be the planet's local up (Y) - latitude is the
            // surface normal's component along that axis, i.e. sin(latitude) in [-1, 1].
            float latitudeSin01 = Mathf.Clamp(surfaceNormal.y, -1f, 1f);
            float seasonalMul = OrbitalSeasons.Instance.SeasonalExposureMultiplier(latitudeSin01);
            dayNightTerm = Mathf.Clamp01(dayNightTerm * seasonalMul);
        }

        return dayNightTerm;
    }

    public bool IsDaySide(Vector3 worldPos, Vector3 planetCenter)
    {
        Vector3 normal = (worldPos - planetCenter).normalized;
        return SolarExposure(normal) > 0.1f; // thin twilight band counts as day
    }
}
