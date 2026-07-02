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
            // When a wet origin is provided all communities start in or near the same
            // liquid body — life originates from a single shared primordial soup, not
            // from scattered independent abiogenesis events on a dry sphere.
            // Each community gets a small angular offset so they don't stack exactly.
            Vector3 origin;
            if (originOverride.HasValue)
            {
                Vector3 randDir = SphereSurface.RandomPointOnSphere(planetCenter, planetRadius);
                Vector3 baseDir = (originOverride.Value - planetCenter).normalized;
                Vector3 offsetDir = Vector3.Slerp(baseDir, (randDir - planetCenter).normalized, 0.08f);
                origin = planetCenter + offsetDir * planetRadius;
            }
            else
            {
                origin = SphereSurface.RandomPointOnSphere(planetCenter, planetRadius);
            }
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

                // Cluster member within ~2° of community origin — tight enough to all start
                // in the same liquid body, loose enough that they don't stack exactly.
                Vector3 randDir = (SphereSurface.RandomPointOnSphere(planetCenter, planetRadius) - planetCenter).normalized;
                Vector3 originDir = (origin - planetCenter).normalized;
                Vector3 position = planetCenter + Vector3.Slerp(originDir, randDir, 0.02f) * planetRadius;
                position = SphereSurface.ProjectToSurface(position, planetCenter, planetRadius);

                // Founding organisms should stay near liquid — override moisture preference
                // to strongly prefer wet terrain regardless of the local noise sample.
                moisturePref = 85f;

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

    void Update()
    {
        SolarSystemRuntime sr = SolarSystemRuntime.Instance;
        if (sr == null || sr.planetRotationPeriodSeconds <= 0f) return;

        float degPerSec = 360f / sr.planetRotationPeriodSeconds;
        float deltaAngle = degPerSec * Time.deltaTime;
        if (Mathf.Approximately(deltaAngle, 0f)) return;

        // Co-rotate all agents with the planet's visual spin so they stay planted
        // on the terrain rather than drifting as the mesh rotates under them.
        Vector3 axis = WindManager.RotationAxis;
        Quaternion rot = Quaternion.AngleAxis(deltaAngle, axis);
        for (int i = ActiveAgents.Count - 1; i >= 0; i--)
        {
            AgentController a = ActiveAgents[i];
            if (a == null) continue;
            a.transform.position = planetCenter + rot * (a.transform.position - planetCenter);
        }
    }
}
