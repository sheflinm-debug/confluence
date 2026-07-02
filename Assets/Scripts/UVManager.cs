using UnityEngine;

/// Surface UV radiation model. UV scales with star luminosity; the atmosphere's
/// accumulated expelled gas (the O2-analog) builds an ozone-equivalent shield
/// over time. Early life on the unshielded surface receives lethal UV — the primary
/// selective pressure driving the deep-water origin of the first organisms.
///
/// Agents query GetUVExposure() each tick; it feeds into GetUVFitnessMultiplier()
/// in AgentController alongside climate and atmospheric discomfort.
public static class UVManager
{
    // Base UV from the star (0–1). Set once at Init from star luminosity.
    public static float BaseUVIntensity { get; private set; }

    // Current ozone attenuation (0–1). Updated each frame by UpdateOzone().
    public static float OzoneAttenuation { get; private set; }

    /// Call from SimulationBootstrap after the solar system is generated.
    /// starLuminositySolar = 1.0 for a Sol-equivalent star.
    public static void Init(float starLuminositySolar)
    {
        // UV flux scales roughly with luminosity^0.5 (hotter stars emit proportionally
        // more UV). A Sol-like star gives ~0.55; an M-dwarf gives ~0.25; an O-class ~1.0.
        BaseUVIntensity = Mathf.Clamp01(Mathf.Sqrt(starLuminositySolar) * 0.55f);
        OzoneAttenuation = 0f; // no ozone shield at genesis
    }

    /// Update ozone from the live atmosphere. The expelled gas (O2-analog) gradually
    /// accumulates an upper-atmosphere shield — UV exposure drops as the GOE progresses.
    public static void UpdateOzone(AtmosphereManager atmos)
    {
        if (atmos == null) { OzoneAttenuation = 0f; return; }

        float expelled = 0f;
        foreach (var g in atmos.Gases)
            if (g.Role == GasRole.Expelled) expelled += g.Fraction;

        // Shield builds meaningfully above ~5% expelled gas, saturates around 35%.
        OzoneAttenuation = Mathf.Clamp01((expelled - 0.05f) / 0.30f);
    }

    /// UV exposure at a surface position (0–1). Zero on the night side; full on the
    /// day side modulated by ozone. Organisms deep in liquid receive partial shielding
    /// (not modelled here — liquidDepth shielding is handled via nutrient density bias).
    public static float GetUVExposure(Vector3 worldPos, Vector3 planetCenter, DayNightCycle dayNight)
    {
        float solar = 1f;
        if (dayNight != null)
        {
            Vector3 normal = (worldPos - planetCenter).normalized;
            solar = dayNight.SolarExposure(normal);
        }
        return solar * BaseUVIntensity * (1f - OzoneAttenuation);
    }
}
