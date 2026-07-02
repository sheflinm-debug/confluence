using System.Collections.Generic;
using UnityEngine;

/// Six canonical interspecies relationship types (classical ecology sign table).
public enum InteractionType
{
    Neutralism,    // 0/0
    Mutualism,     // +/+
    Commensalism,  // +/0
    Parasitism,    // +/-
    Competition,   // -/-
    Amensalism,    // 0/-
}

/// Manages pairwise species relationships using a proximity × duration trigger model.
/// Relationships are tracked per community-ID pair, not per individual agent.
/// Call Tick() once per simulation Update from a suitable manager (e.g. EraManager/SimController).
public class SpeciesRelationshipManager : MonoBehaviour
{
    public static SpeciesRelationshipManager Instance { get; private set; }

    // How often (seconds) to run the relationship update scan.
    private const float ScanInterval = 2f;
    // Interaction range for proximity factor.
    private const float ProximityRange = 8f;
    // Minimum per-tick energy effect magnitude.
    private const float EffectMagnitude = 0.0005f;

    private AgentSpawner _spawner;
    private float _timer;

    // Pairwise contact accumulator: [idA,idB] → contact ticks
    private readonly Dictionary<(int, int), int> _contactTicks = new Dictionary<(int, int), int>();
    // Established relationships: [idA,idB] → type
    private readonly Dictionary<(int, int), InteractionType> _established = new Dictionary<(int, int), InteractionType>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Init(AgentSpawner spawner) => _spawner = spawner;

    private void Update()
    {
        if (_spawner == null) return;
        _timer += Time.deltaTime;
        if (_timer < ScanInterval) return;
        _timer = 0f;
        RunScan();
    }

    private void RunScan()
    {
        var agents = _spawner.ActiveAgents;
        // Find co-located community pairs
        var proximityPairs = new Dictionary<(int, int), float>(); // pair → closest distance

        for (int i = 0; i < agents.Count; i++)
        {
            for (int j = i + 1; j < agents.Count; j++)
            {
                var a = agents[i];
                var b = agents[j];
                if (a == null || b == null) continue;
                if (a.communityId == b.communityId) continue; // same community, not interspecies

                float dist = Vector3.Distance(a.transform.position, b.transform.position);
                if (dist > ProximityRange * 2f) continue;

                var key = MakeKey(a.communityId, b.communityId);
                if (!proximityPairs.TryGetValue(key, out float best) || dist < best)
                    proximityPairs[key] = dist;
            }
        }

        // Update contact ticks and attempt relationship establishment
        foreach (var kv in proximityPairs)
        {
            var key = kv.Key;
            float dist = kv.Value;
            float proximityFactor = ProximityFactor(dist);

            _contactTicks.TryGetValue(key, out int ticks);
            ticks = Mathf.Min(ticks + 1, 20);
            _contactTicks[key] = ticks;

            if (_established.ContainsKey(key)) continue; // already established

            float durationFactor = DurationFactor(ticks);
            float baseWeight = BaseWeight(key);
            float triggerProb = baseWeight * proximityFactor * durationFactor;

            if (Random.value < triggerProb * ScanInterval * 0.1f)
                _established[key] = RollType(key);
        }

        // Apply per-tick effects for established relationships
        foreach (var kv in _established)
        {
            ApplyEffects(kv.Key, kv.Value, agents);
        }
    }

    // ── Proximity/duration factors ────────────────────────────────────────────────

    private static float ProximityFactor(float dist)
    {
        if (dist > ProximityRange) return 0.3f;       // occasional edge contact
        if (dist > ProximityRange * 0.5f) return 0.7f; // frequent core overlap
        return 1.0f;                                   // constant cohabitation
    }

    private static float DurationFactor(int ticks)
    {
        if (ticks >= 15) return 1.0f;
        if (ticks >= 10) return 0.75f;
        if (ticks >= 5)  return 0.5f;
        return 0.2f;
    }

    // ── BaseWeight — categorical rules per compatibility factors ──────────────────

    private float BaseWeight((int idA, int idB) key)
    {
        // No per-pair agent lookup needed for base weight — use 0.4 as default.
        // In practice, full agents are found in ApplyEffects for the actual effect.
        return 0.4f;
    }

    // ── Type roll — weighted by trophic/backbone compatibility ───────────────────

    private InteractionType RollType((int idA, int idB) key)
    {
        // Find representative agents for each community to read compatibility factors
        AgentController repA = null, repB = null;
        if (_spawner != null)
        {
            foreach (var a in _spawner.ActiveAgents)
            {
                if (a == null) continue;
                if (a.communityId == key.Item1 && repA == null) repA = a;
                if (a.communityId == key.Item2 && repB == null) repB = a;
                if (repA != null && repB != null) break;
            }
        }

        if (repA == null || repB == null) return InteractionType.Neutralism;

        bool sameBackbone = repA.Backbone == repB.Backbone;
        bool aIsProducer = repA.IsProducer;
        bool bIsProducer = repB.IsProducer;

        // Build weight table [Neutralism, Mutualism, Commensalism, Parasitism, Competition, Amensalism]
        float[] w = new float[6];
        w[0] = 1f; // Neutralism always has some weight

        if (aIsProducer && bIsProducer)
        {
            // Both autotrophs: shared resources → competition; parasitism near zero
            w[4] = 3f; // Competition
            w[5] = 1f; // Amensalism (allelopathic chemical suppression)
            w[3] = 0.1f; // Parasitism very unlikely
        }
        else if (!aIsProducer && !bIsProducer)
        {
            // Both heterotrophs: competition, predation, or opportunistic mutualism
            w[4] = 2f; // Competition
            w[3] = 2f; // Parasitism/Predation
            w[1] = 0.8f; // Mutualism (cooperative hunting or shared territory)
            w[5] = 0.5f; // Amensalism
        }
        else
        {
            // Heterotroph × autotroph: classic exploitation or mutualism range
            w[3] = 2.5f; // Parasitism/Predation
            w[2] = 1.5f; // Commensalism (e.g. epiphyte using host structure)
            w[1] = 1.2f; // Mutualism (pollination analog)
            w[4] = 0.5f; // Competition (for different resources)
        }

        // Same backbone: tissue exploitability → boost parasitism; structural competition
        if (sameBackbone)
        {
            w[3] *= 1.5f;
            w[4] *= 1.2f;
        }
        else
        {
            // Incompatible backbone: parasite can't efficiently exploit host tissue
            w[3] *= 0.4f;
            // More likely benign
            w[0] *= 1.5f;
            w[2] *= 1.3f;
        }

        // High sociality agents → more mutualistic range
        float socialA = (float)repA.Sociality / 3f;
        float socialB = (float)repB.Sociality / 3f;
        float socialBonus = (socialA + socialB) * 0.5f;
        w[1] += socialBonus;
        w[2] += socialBonus * 0.5f;

        // Weighted roll
        float total = 0f;
        for (int i = 0; i < w.Length; i++) total += w[i];
        float roll = Random.value * total;
        float cumul = 0f;
        for (int i = 0; i < w.Length; i++)
        {
            cumul += w[i];
            if (roll <= cumul) return (InteractionType)i;
        }
        return InteractionType.Neutralism;
    }

    // ── Per-tick effect application ───────────────────────────────────────────────

    private void ApplyEffects((int idA, int idB) key, InteractionType type, List<AgentController> agents)
    {
        if (type == InteractionType.Neutralism) return;

        // Find all agents in proximity for each community
        var listA = new List<AgentController>();
        var listB = new List<AgentController>();
        foreach (var a in agents)
        {
            if (a == null) continue;
            if (a.communityId == key.Item1) listA.Add(a);
            else if (a.communityId == key.Item2) listB.Add(a);
        }

        // Only apply if communities are actually near each other
        bool near = false;
        foreach (var a in listA)
        {
            foreach (var b in listB)
            {
                if (Vector3.Distance(a.transform.position, b.transform.position) < ProximityRange * 2f)
                { near = true; break; }
            }
            if (near) break;
        }
        if (!near) return;

        float dt = ScanInterval; // effects applied per scan tick
        float amount = EffectMagnitude * dt;

        switch (type)
        {
            case InteractionType.Mutualism:
                foreach (var a in listA) a.ReceiveRelationshipBonus(amount);
                foreach (var b in listB) b.ReceiveRelationshipBonus(amount);
                break;
            case InteractionType.Commensalism:
                foreach (var a in listA) a.ReceiveRelationshipBonus(amount);
                // B unaffected
                break;
            case InteractionType.Parasitism:
                // A drains from B
                foreach (var b in listB) b.ReceiveRelationshipDrain(amount);
                foreach (var a in listA) a.ReceiveRelationshipBonus(amount * 0.5f); // assimilation loss
                break;
            case InteractionType.Competition:
                foreach (var a in listA) a.ReceiveRelationshipDrain(amount * 0.5f);
                foreach (var b in listB) b.ReceiveRelationshipDrain(amount * 0.5f);
                break;
            case InteractionType.Amensalism:
                foreach (var b in listB) b.ReceiveRelationshipDrain(amount);
                // A unaffected
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static (int, int) MakeKey(int a, int b) => a < b ? (a, b) : (b, a);

    /// Returns the established relationship type between two communities, or Neutralism if none.
    public InteractionType GetRelationship(int communityA, int communityB)
    {
        var key = MakeKey(communityA, communityB);
        return _established.TryGetValue(key, out var type) ? type : InteractionType.Neutralism;
    }
}
