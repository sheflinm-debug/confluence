using System.Collections.Generic;
using UnityEngine;

/// Tracks the live population distribution (mean + variance) for each trait dimension.
/// Per Section 6a: each dimension is a 0-100 scale, 50 = population average at world start.
/// Agents register/unregister their current trait value here so the population mean/variance
/// reflects who's actually alive right now (drift visible as agents are born/die).
public static class PopulationStats
{
    private static readonly List<float> _vision = new List<float>();
    private static readonly List<float> _speed = new List<float>();
    private static readonly List<float> _strength = new List<float>();
    private static readonly List<float> _hardiness = new List<float>();
    private static readonly List<float> _stress = new List<float>();
    private static readonly List<float> _stressTolerance = new List<float>();

    public static void Reset()
    {
        _vision.Clear();
        _speed.Clear();
        _strength.Clear();
        _hardiness.Clear();
        _stress.Clear();
        _stressTolerance.Clear();
    }

    public static void RegisterVision(float value) => _vision.Add(value);
    public static void UnregisterVision(float value) => _vision.Remove(value);

    public static void RegisterSpeed(float value) => _speed.Add(value);
    public static void UnregisterSpeed(float value) => _speed.Remove(value);

    public static void RegisterStrength(float value) => _strength.Add(value);
    public static void UnregisterStrength(float value) => _strength.Remove(value);

    public static void RegisterHardiness(float value) => _hardiness.Add(value);
    public static void UnregisterHardiness(float value) => _hardiness.Remove(value);

    // Stress is a STATE variable, not a fixed trait dimension - its registered value
    // changes every tick (see AgentController.StressLevel), so callers must
    // Unregister(old) + Register(new) each update rather than register once at spawn.
    public static void RegisterStress(float value) => _stress.Add(value);
    public static void UnregisterStress(float value) => _stress.Remove(value);

    public static void RegisterStressTolerance(float value) => _stressTolerance.Add(value);
    public static void UnregisterStressTolerance(float value) => _stressTolerance.Remove(value);

    public static (float mean, float variance) VisionStats => ComputeStats(_vision);
    public static (float mean, float variance) SpeedStats => ComputeStats(_speed);
    public static (float mean, float variance) StrengthStats => ComputeStats(_strength);
    public static (float mean, float variance) HardinessStats => ComputeStats(_hardiness);
    public static (float mean, float variance) StressStats => ComputeStats(_stress);
    public static (float mean, float variance) StressToleranceStats => ComputeStats(_stressTolerance);

    public static int PopulationCount => _vision.Count;

    private static (float mean, float variance) ComputeStats(List<float> values)
    {
        if (values.Count == 0) return (0f, 0f);

        float sum = 0f;
        foreach (float v in values) sum += v;
        float mean = sum / values.Count;

        float sqDiffSum = 0f;
        foreach (float v in values) sqDiffSum += (v - mean) * (v - mean);
        float variance = sqDiffSum / values.Count;

        return (mean, variance);
    }

    /// Draws a value from a normal distribution (Box-Muller), clamped to the 0-100 dimension range.
    public static float SampleDimension(float mean, float stdDev)
    {
        float u1 = 1f - Random.value;
        float u2 = Random.value;
        float standardNormal = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
        float value = mean + stdDev * standardNormal;
        return Mathf.Clamp(value, 0f, 100f);
    }
}
