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

    // Technical safety valve only — NOT an ecological population cap. Real carrying capacity is
    // purely emergent from energy math (see AgentController.TryReproduce). This exists only to
    // protect frame rate in a pathological case and should rarely if ever actually bind.
    public const int MaxIndividualAgents = 1500;

    // Monotonic counter — never reused, never derived from population size.
    // This is the authoritative agent identity; displayName is cosmetic only.
    private static long _nextAgentId = 0;

    // ── Spatial hash grid ──────────────────────────────────────────────────────────────────
    // Every "who's near me" behavior an individual runs (separation, mate-search, prey/threat
    // detection, social clustering, dispersal-kin sampling) previously did `foreach (ActiveAgents)`
    // — an O(n) scan per agent per call, i.e. O(n²) system-wide. This grid is the actual fix:
    // agents are bucketed into cells once per frame (O(n)), and proximity queries only examine
    // nearby cells (O(k), k = local density) instead of the whole population. This is a pure
    // performance optimization — it does not change simulation outcomes. Every organism remains
    // individually simulated (its own energy, reproduction, death, genes); grouping behavior
    // (Aggregating/GroupForming/PairBonded/etc.) is expressed entirely through movement/formation
    // bias in AgentController, not by merging organisms into an aggregate entity.
    private const float GridCellSize = 6f; // matches the largest common scan radius used by callers
    private readonly Dictionary<(int, int, int), List<AgentController>> _grid = new();

    private (int, int, int) CellOf(Vector3 pos) => (
        Mathf.FloorToInt(pos.x / GridCellSize),
        Mathf.FloorToInt(pos.y / GridCellSize),
        Mathf.FloorToInt(pos.z / GridCellSize));

    private void RebuildGrid()
    {
        foreach (var list in _grid.Values) list.Clear();
        foreach (var a in ActiveAgents)
        {
            if (a == null) continue;
            var cell = CellOf(a.transform.position);
            if (!_grid.TryGetValue(cell, out var list)) { list = new List<AgentController>(); _grid[cell] = list; }
            list.Add(a);
        }
    }

    /// Fills `results` with active agents within `radius` of `position` — O(k) via the spatial grid
    /// instead of an O(n) scan of every active agent. `results` is cleared first; reuse a
    /// caller-owned buffer across calls to avoid per-call allocation.
    public void QueryNearby(Vector3 position, float radius, List<AgentController> results)
    {
        results.Clear();
        int cellRadius = Mathf.Max(1, Mathf.CeilToInt(radius / GridCellSize));
        var center = CellOf(position);
        for (int dx = -cellRadius; dx <= cellRadius; dx++)
        for (int dy = -cellRadius; dy <= cellRadius; dy++)
        for (int dz = -cellRadius; dz <= cellRadius; dz++)
        {
            var cell = (center.Item1 + dx, center.Item2 + dy, center.Item3 + dz);
            if (_grid.TryGetValue(cell, out var list))
                foreach (var a in list)
                    if (a != null && (a.transform.position - position).sqrMagnitude <= radius * radius)
                        results.Add(a);
        }
    }

    private static readonly List<AgentController> _absorbScratch = new List<AgentController>();

    /// Despawns every living agent within radius of a point that matches `eligible`, returning how
    /// many were absorbed. Used by Era 3 settlement founding/growth to fold nearby population into a
    /// settlement's abstract Population count instead of continuing to simulate each one individually
    /// — this is the actual fix for Era 3 lag (hundreds of agents belonging to a civilized lineage were
    /// otherwise still running full per-agent simulation for no purpose Era 3 actually reads). This is
    /// a silent removal (join, not death) — no corpse, no DEATH log, just PopulationStats/registry
    /// cleanup via AgentController.OnDestroy.
    public int AbsorbNearby(Vector3 center, float radius, System.Func<AgentController, bool> eligible)
    {
        QueryNearby(center, radius, _absorbScratch);
        int count = 0;
        foreach (var a in _absorbScratch)
        {
            if (a == null || !eligible(a)) continue;
            Destroy(a.gameObject);
            count++;
        }
        return count;
    }

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

                // Contrast-aware hue (PlanetPalette) instead of a raw i/memberCount spread — dodges
                // this world's ground/ocean colors so founders don't visually melt into the terrain.
                Color founderColor = Color.HSVToRGB(PlanetPalette.ContrastHueForIndex(i, memberCount), 0.75f, 0.95f);
                AgentController founder = SpawnAgent(vision, speed, strength, hardiness, tempPref, moisturePref, position, c, founderColor);
                founder.StaggerFounderAge(); // age-mix the founding population so it doesn't die in one synchronized wave
            }
        }
    }

    public AgentController SpawnAgent(float visionTrait, float speedTrait, float strengthTrait, float hardinessTrait,
        float temperaturePreference, float moisturePreference, Vector3 position, int communityId = -1, Color? color = null)
    {
        long id = _nextAgentId++;
        GameObject go = Instantiate(agentPrefab, parent);
        go.name = $"Agent_{id}";
        go.transform.position = position;

        AgentController agent = go.GetComponent<AgentController>();
        if (agent == null) agent = go.AddComponent<AgentController>();

        // Agents move purely by script (transform.position) and are never selected via physics
        // raycast (InspectPopup raycasts the planet, not organisms). Any Collider/Rigidbody the
        // prefab carries is dead weight — Unity runs a physics broadphase over hundreds of them for
        // nothing, and stray agent colliders can even intercept the planet-picking ray. Strip them.
        foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);

        agent.AgentId = id;
        agent.Init(planetCenter, planetRadius, corpseSpawner, this, visionTrait, speedTrait, strengthTrait, hardinessTrait, temperaturePreference, moisturePreference, communityId, color);
        go.SetActive(true);  // activate after Init so Awake/Start don't clobber Init's values
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
            // Contrast-aware hue (PlanetPalette) — evenly spaced across whatever's left of the hue
            // circle once ground/ocean bands are excluded, so NPC lineages stay legible against
            // this world's terrain instead of just avoiding each other's hues.
            float hue = PlanetPalette.ContrastHueForIndex(c, count);
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
            AgentController founder = SpawnAgent(vision, speed, strength, hardiness, tempPref, moistPref, position, communityId, color);
            founder.StaggerFounderAge();
        }
    }

    /// DEBUG: clones existing living members up to targetTotal, so an instant era-skip doesn't leave
    /// the population frozen at whatever small count it happened to be at the moment the button was
    /// pressed. A real Era 1/2 playthrough grows the population into the hundreds; this approximates
    /// that growth by cloning current members (same traits/genes/habitat as their "parent", with a
    /// touch of mutation drift) rather than leaving a 30-50-agent population that never got the chance
    /// to reproduce out. Distributes clones proportionally across existing communities.
    public void DebugBulkUpPopulation(int targetTotal)
    {
        var byCommunity = new Dictionary<int, List<AgentController>>();
        foreach (var a in ActiveAgents)
        {
            if (a == null) continue;
            if (!byCommunity.TryGetValue(a.communityId, out var list)) { list = new List<AgentController>(); byCommunity[a.communityId] = list; }
            list.Add(a);
        }
        if (byCommunity.Count == 0) return;

        int currentTotal = ActiveAgents.Count;
        int toAdd = Mathf.Min(targetTotal - currentTotal, MaxIndividualAgents - currentTotal);
        if (toAdd <= 0) return;

        foreach (var kv in byCommunity)
        {
            var members = kv.Value;
            int share = Mathf.CeilToInt(toAdd * (float)members.Count / currentTotal);
            for (int i = 0; i < share && ActiveAgents.Count < MaxIndividualAgents; i++)
            {
                AgentController parentAgent = members[Random.Range(0, members.Count)];
                float v = PopulationStats.SampleDimension(parentAgent.visionTrait, 3f);
                float s = PopulationStats.SampleDimension(parentAgent.speedTrait, 3f);
                float st = PopulationStats.SampleDimension(parentAgent.strengthTrait, 3f);
                float h = PopulationStats.SampleDimension(parentAgent.hardinessTrait, 3f);
                float tp = PopulationStats.SampleDimension(parentAgent.temperaturePreference, 3f);
                float mp = PopulationStats.SampleDimension(parentAgent.moisturePreference, 3f);

                AgentController clone = SpawnAgent(v, s, st, h, tp, mp, parentAgent.transform.position,
                    parentAgent.communityId, parentAgent.lineageColor);
                clone.InheritGenesFrom(parentAgent);
                clone.DebugAssignRandomSex();
                clone.DebugRelocateToMatchingMedium(); // land clones land near the parent, not necessarily IN the parent's exact wet/dry spot
            }
        }
        Debug.Log($"[DebugSkip] Population bulked up {currentTotal} -> {ActiveAgents.Count} to approximate normal Era 1/2 growth.");
    }

    public void Unregister(AgentController agent)
    {
        ActiveAgents.Remove(agent);
    }

    void Update()
    {
        GameLog.MaybeSnapshot(this);

        // Rebuilt once per frame, before any agent's own Update() runs its proximity queries this
        // frame (Unity script execution order isn't otherwise guaranteed, so queries may read
        // positions that are up to one frame stale — an acceptable tradeoff for O(n) instead of
        // O(n²), and irrelevant for behaviors like separation/mate-search that aren't frame-critical).
        RebuildGrid();

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
