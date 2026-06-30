using System.Collections.Generic;
using UnityEngine;

/// Tier 3 dev overlay: shows live population mean/variance for tracked trait dimensions,
/// both globally and broken out per biome the agent is currently standing in, so regional
/// climate adaptation is visible. Uses OnGUI for simplicity - not final UI.
public class PopulationStatsOverlay : MonoBehaviour
{
    public AgentSpawner agentSpawner;

    private static readonly BiomeType[] AllBiomes =
    {
        BiomeType.Desert, BiomeType.Jungle, BiomeType.Forest,
        BiomeType.Grassland, BiomeType.Tundra, BiomeType.Wetland
    };

    void OnGUI()
    {
        var (visionMean, visionVariance) = PopulationStats.VisionStats;
        var (speedMean, speedVariance) = PopulationStats.SpeedStats;
        var (strengthMean, strengthVariance) = PopulationStats.StrengthStats;
        var (stressMean, stressVariance) = PopulationStats.StressStats;
        var (stressToleranceMean, stressToleranceVariance) = PopulationStats.StressToleranceStats;

        GUI.Box(new Rect(10, 10, 300, 160), "Population Stats (overall)");
        GUI.Label(new Rect(20, 35, 280, 20), $"Population: {PopulationStats.PopulationCount}");
        GUI.Label(new Rect(20, 55, 280, 20), $"Vision   mean {visionMean:F1}  var {visionVariance:F1}");
        GUI.Label(new Rect(20, 75, 280, 20), $"Speed    mean {speedMean:F1}  var {speedVariance:F1}");
        GUI.Label(new Rect(20, 95, 280, 20), $"Strength mean {strengthMean:F1}  var {strengthVariance:F1}");
        GUI.Label(new Rect(20, 115, 280, 20), $"Stress   mean {stressMean:F1}  var {stressVariance:F1}");
        GUI.Label(new Rect(20, 135, 280, 20), $"StressTol mean {stressToleranceMean:F1}  var {stressToleranceVariance:F1}");

        DrawPerBiomeStats();
    }

    private void DrawPerBiomeStats()
    {
        if (agentSpawner == null) return;

        var byBiome = new Dictionary<BiomeType, List<AgentController>>();
        foreach (var biome in AllBiomes) byBiome[biome] = new List<AgentController>();

        foreach (var agent in agentSpawner.ActiveAgents)
        {
            if (agent == null) continue;
            BiomeType biome = ClimateManager.GetBiome(agent.transform.position);
            byBiome[biome].Add(agent);
        }

        float boxY = 180f;
        float rowHeight = 70f;
        GUI.Box(new Rect(10, boxY, 300, rowHeight * AllBiomes.Length + 10), "Population Stats (by biome)");

        for (int i = 0; i < AllBiomes.Length; i++)
        {
            DrawBiomeRow(AllBiomes[i], byBiome[AllBiomes[i]], boxY + 20 + rowHeight * i);
        }
    }

    private void DrawBiomeRow(BiomeType biome, List<AgentController> agents, float y)
    {
        GUI.Label(new Rect(20, y, 280, 20), $"{biome} - count: {agents.Count}");

        if (agents.Count == 0)
        {
            GUI.Label(new Rect(20, y + 20, 280, 20), "(no agents currently here)");
            return;
        }

        var (tempMean, _) = ComputeStats(agents, a => a.temperaturePreference);
        var (moistureMean, _) = ComputeStats(agents, a => a.moisturePreference);

        GUI.Label(new Rect(20, y + 20, 280, 20), $"Temp pref mean {tempMean:F1}");
        GUI.Label(new Rect(20, y + 40, 280, 20), $"Moisture pref mean {moistureMean:F1}");
    }

    private (float mean, float variance) ComputeStats(List<AgentController> agents, System.Func<AgentController, float> selector)
    {
        float sum = 0f;
        foreach (var a in agents) sum += selector(a);
        float mean = sum / agents.Count;

        float sqDiffSum = 0f;
        foreach (var a in agents) sqDiffSum += (selector(a) - mean) * (selector(a) - mean);
        float variance = sqDiffSum / agents.Count;

        return (mean, variance);
    }
}
