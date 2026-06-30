using UnityEngine;

/// Rotates a directional light around the planet to simulate a day/night cycle.
/// Exposes IsDaySide() so agents and AtmosphereManager can query solar exposure.
public class DayNightCycle : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Real-time seconds per full day/night cycle.")]
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
