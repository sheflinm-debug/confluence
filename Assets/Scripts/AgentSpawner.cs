using System.Collections.Generic;
using UnityEngine;

/// Owns agent instantiation so agents can spawn offspring (reproduction) without
/// each one needing its own copy of prefab/parent/sphere setup.
public class AgentSpawner : MonoBehaviour
{
    public GameObject agentPrefab;
    public CorpseSpawner corpseSpawner;
    public Transform parent;
    public Vector3 planetCenter;
    public float planetRadius;

    public List<AgentController> ActiveAgents { get; } = new List<AgentController>();

    /// Spawns several small origin communities at random points around the sphere.
    /// Each member's vision/speed/strength/hardiness come from the global starting
    /// distribution, but its temperature/moisture preference is seeded from that
    /// community's local climate (with variance) - so members start out adapted to
    /// wherever their community began, not to an arbitrary global average. Each
    /// founding member gets a distinct hue, evenly spaced across the colony, that all
    /// of its descendants inherit unchanged until a future visual-speciation event.
    public void SpawnCommunities(int communityCount, int minMembers, int maxMembers,
        float visionMean, float visionStdDev, float speedMean, float speedStdDev,
        float strengthMean, float strengthStdDev, float hardinessMean, float hardinessStdDev,
        float preferenceVariance, Vector3? originOverride = null)
    {
        for (int c = 0; c < communityCount; c++)
        {
            // originOverride lets the caller place the founding colony somewhere
            // specific (e.g. inside a flooded liquid tile) instead of anywhere on the
            // sphere - only meaningful for the first community when communityCount==1.
            Vector3 origin = (c == 0 && originOverride.HasValue) ? originOverride.Value : SphereSurface.RandomPointOnSphere(planetCenter, planetRadius);
            float localTemp = ClimateManager.GetTemperature(origin);
            float localMoisture = ClimateManager.GetMoisture(origin);

            int memberCount = Random.Range(minMembers, maxMembers + 1);
            for (int i = 0; i < memberCount; i++)
            {
                float vision = PopulationStats.SampleDimension(visionMean, visionStdDev);
                float speed = PopulationStats.SampleDimension(speedMean, speedStdDev);
                float strength = PopulationStats.SampleDimension(strengthMean, strengthStdDev);
                float hardiness = PopulationStats.SampleDimension(hardinessMean, hardinessStdDev);
                float tempPref = PopulationStats.SampleDimension(localTemp, preferenceVariance);
                float moisturePref = PopulationStats.SampleDimension(localMoisture, preferenceVariance);

                Vector3 position = SphereSurface.RandomPointOnSphere(planetCenter, planetRadius);
                position = Vector3.Slerp((origin - planetCenter), (position - planetCenter), 0.05f) + planetCenter; // cluster near origin
                position = SphereSurface.ProjectToSurface(position, planetCenter, planetRadius);

                Color founderColor = Color.HSVToRGB((float)i / memberCount, 0.75f, 0.95f);
                SpawnAgent(vision, speed, strength, hardiness, tempPref, moisturePref, position, c, founderColor);
            }
        }
    }

    public AgentController SpawnAgent(float visionTrait, float speedTrait, float strengthTrait, float hardinessTrait,
        float temperaturePreference, float moisturePreference, Vector3 position, int communityId = -1, Color? color = null)
    {
        GameObject go = Instantiate(agentPrefab, parent);
        go.name = $"Agent_{ActiveAgents.Count}";
        go.transform.position = position;

        AgentController agent = go.GetComponent<AgentController>();
        if (agent == null) agent = go.AddComponent<AgentController>();

        agent.Init(planetCenter, planetRadius, corpseSpawner, this, visionTrait, speedTrait, strengthTrait, hardinessTrait, temperaturePreference, moisturePreference, communityId, color);
        ActiveAgents.Add(agent);
        return agent;
    }

    /// Spawns NPC communities (communityIds 1 through count) with evenly-spaced hues
    /// so each starts as a visually distinct lineage. Call after SpawnCommunities so
    /// community 0 (the player) is already placed and NPC ids start at 1.
    public void SpawnNPCCommunities(int count,
        float visionMean, float visionStdDev,
        float speedMean, float speedStdDev,
        float strengthMean, float strengthStdDev,
        float hardinessMean, float hardinessStdDev,
        float preferenceVariance)
    {
        for (int c = 0; c < count; c++)
        {
            int communityId = c + 1; // 0 is the player
            // Evenly-spaced hues, offset by 0.05 so they don't land on the same hue
            // as common player starting colors (which tend toward warm tones).
            float hue = ((float)c / count + 0.05f) % 1f;
            Color color = Color.HSVToRGB(hue, 0.80f, 0.95f);

            Vector3 origin = SphereSurface.RandomPointOnSphere(planetCenter, planetRadius);
            float localTemp = ClimateManager.GetTemperature(origin);
            float localMoisture = ClimateManager.GetMoisture(origin);

            // One founding organism per NPC species — same model as the player's single
            // origin cell. They'll grow through the same era/speciation system as the player.
            float vision    = PopulationStats.SampleDimension(visionMean,    visionStdDev);
            float speed     = PopulationStats.SampleDimension(speedMean,     speedStdDev);
            float strength  = PopulationStats.SampleDimension(strengthMean,  strengthStdDev);
            float hardiness = PopulationStats.SampleDimension(hardinessMean, hardinessStdDev);
            float tempPref  = PopulationStats.SampleDimension(localTemp,     preferenceVariance);
            float moistPref = PopulationStats.SampleDimension(localMoisture, preferenceVariance);

            Vector3 position = SphereSurface.ProjectToSurface(origin, planetCenter, planetRadius);
            SpawnAgent(vision, speed, strength, hardiness, tempPref, moistPref, position, communityId, color);
        }
    }

    public void Unregister(AgentController agent)
    {
        ActiveAgents.Remove(agent);
    }
}
