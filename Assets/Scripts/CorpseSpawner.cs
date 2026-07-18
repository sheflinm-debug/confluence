using System.Collections.Generic;
using UnityEngine;

/// Spawns and tracks decaying corpses left behind by agents that die of natural
/// causes. Replaces the old standalone "food resource" - the only thing consumers
/// scavenge besides actively hunting live prey.
public class CorpseSpawner : MonoBehaviour
{
    public GameObject corpsePrefab;
    public float decayTime = 12f;
    public Transform parent;

    public List<CorpseItem> ActiveCorpses { get; } = new List<CorpseItem>();

    // Diagnostics so the trophic layer is verifiable in logs (it was invisible because neither
    // spawn nor consume emitted any log line). Logs the first spawn, then every 50th, plus a
    // running total — enough to confirm the system runs without flooding the log at every death.
    private int _totalSpawned;

    public void SpawnCorpseAt(Vector3 position, float bodyMass = 0.001f)
    {
        if (corpsePrefab == null)
        {
            if (_totalSpawned == 0) Debug.LogWarning("[Corpse] SpawnCorpseAt called but corpsePrefab is null — no corpses will spawn.");
            return;
        }

        GameObject go = Instantiate(corpsePrefab, parent);
        go.transform.position = position;

        CorpseItem corpse = go.GetComponent<CorpseItem>();
        if (corpse == null) corpse = go.AddComponent<CorpseItem>();
        corpse.spawner = this;
        corpse.decayTime = decayTime;
        corpse.BodyMass = bodyMass;

        ActiveCorpses.Add(corpse);

        _totalSpawned++;
        if (_totalSpawned == 1 || _totalSpawned % 50 == 0)
            Debug.Log($"[Corpse] SpawnCorpseAt fired — totalSpawned={_totalSpawned} activeCorpses={ActiveCorpses.Count} bodyMass={bodyMass:F4}");
    }

    public void Unregister(CorpseItem corpse)
    {
        ActiveCorpses.Remove(corpse);
    }
}
