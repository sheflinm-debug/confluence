using UnityEngine;

/// Procedural wind field (v1/believable, not real atmospheric circulation): a few
/// large-scale latitude "cells" loosely modeled on real Hadley/Ferrel/Polar bands,
/// plus a slow global drift so the whole pattern creeps westward-to-eastward over
/// time the way real circulation bands do. Static + stateless like ClimateManager
/// (randomized once at world-gen via Randomize, queried by position thereafter) so
/// any system - WeatherManager, future seed/spore dispersal, agent movement drag -
/// can call WindManager.GetWind(worldPosition) the same way it calls
/// ClimateManager.GetTemperature(worldPosition).
public static class WindManager
{
    private static Vector3 _planetCenter;
    private static float _planetRadius;
    private static Vector3 _rotationAxis = Vector3.up;
    private static float _cellCount = 3f; // number of latitude bands per hemisphere
    private static float _driftSpeedDegPerSec = 1.5f;
    private static float _baseSpeed = 1f;
    private static float _turbulenceScale = 0.35f;
    private static Vector3 _turbulenceOffset;

    /// Re-rolls the wind field for a new playthrough. Call once at world-gen time,
    /// alongside ClimateManager.Randomize. rotationAxis should match whatever axis
    /// the planet conceptually spins on (DayNightCycle currently rotates the sun
    /// around Z in its demo rig, but the wind bands are defined relative to a
    /// world-space "up" pole by default - pass a different axis if needed).
    public static void Randomize(Vector3 planetCenter, float planetRadius, Vector3 rotationAxis, int cellsPerHemisphere = 3)
    {
        _planetCenter = planetCenter;
        _planetRadius = planetRadius;
        _rotationAxis = rotationAxis.normalized;
        _cellCount = Mathf.Max(1, cellsPerHemisphere);
        _turbulenceOffset = new Vector3(Random.Range(0f, 1000f), Random.Range(0f, 1000f), Random.Range(0f, 1000f));
    }

    /// Returns a tangent-space (perpendicular to the surface normal) wind vector at
    /// the given world position. Magnitude encodes speed; callers that just need
    /// direction can normalize. Zero vector is a valid result (dead calm at a cell
    /// boundary) - callers should guard against normalizing a zero vector.
    public static Vector3 GetWind(Vector3 worldPosition)
    {
        Vector3 normal = (worldPosition - _planetCenter).normalized;
        if (normal.sqrMagnitude < 0.0001f) return Vector3.zero;

        // Latitude band index: alternating prevailing-wind direction per band, like
        // real Hadley/Ferrel/Polar cells alternating easterlies/westerlies.
        float latitude = Mathf.Asin(Mathf.Clamp(Vector3.Dot(normal, _rotationAxis), -1f, 1f)); // -PI/2..PI/2
        float latitudeFrac = latitude / (Mathf.PI / 2f); // -1..1
        float bandPos = latitudeFrac * _cellCount;
        int band = Mathf.FloorToInt(bandPos);
        // Smooth-step across the band boundary instead of a hard flip, so wind
        // direction doesn't pop discontinuously as agents/storms cross a seam.
        float bandFrac = bandPos - band;
        float dirSign = (band % 2 == 0) ? 1f : -1f;
        float nextSign = ((band + 1) % 2 == 0) ? 1f : -1f;
        float blendedSign = Mathf.Lerp(dirSign, nextSign, Mathf.SmoothStep(0f, 1f, bandFrac));

        // Eastward/westward tangent direction at this point (cross of polar axis and
        // normal gives the local "around the pole" tangent).
        Vector3 zonal = Vector3.Cross(_rotationAxis, normal);
        if (zonal.sqrMagnitude < 0.0001f) zonal = Vector3.Cross(Vector3.right, normal); // near the poles, pick any tangent
        zonal.Normalize();

        // Slow global drift: the whole band pattern creeps over time (stand-in for
        // planetary rotation + day/night thermal differential dragging the bands),
        // expressed as an additional rotation of the zonal vector around the normal.
        float driftAngle = Time.time * _driftSpeedDegPerSec;
        Vector3 drifted = Quaternion.AngleAxis(driftAngle, normal) * zonal;

        // Light turbulence so wind isn't perfectly laminar - reuses the same kind of
        // 3D-Perlin-on-unit-sphere trick ClimateManager uses for its noise fields.
        float turb = Mathf.PerlinNoise(
            normal.x / _turbulenceScale + _turbulenceOffset.x + Time.time * 0.05f,
            normal.y / _turbulenceScale + _turbulenceOffset.y - normal.z / _turbulenceScale + _turbulenceOffset.z);
        float speed = _baseSpeed * (0.6f + turb * 0.8f);

        return drifted * blendedSign * speed;
    }

    /// Convenience: wind speed only (magnitude of GetWind), in arbitrary "wind units"
    /// (tunable via SetBaseSpeed) - not calibrated to a real m/s scale.
    public static float GetWindSpeed(Vector3 worldPosition) => GetWind(worldPosition).magnitude;

    public static void SetBaseSpeed(float speed) => _baseSpeed = Mathf.Max(0f, speed);

    public static Vector3 RotationAxis => _rotationAxis;
    public static Vector3 PlanetCenter => _planetCenter;
    public static float PlanetRadius => _planetRadius;
}
