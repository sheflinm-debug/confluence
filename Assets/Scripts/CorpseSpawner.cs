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

    public void SpawnCorpseAt(Vector3 position)
    {
        if (corpsePrefab == null) return;

        GameObject go = Instantiate(corpsePrefab, parent);
        go.transform.position = position;

        CorpseItem corpse = go.GetComponent<CorpseItem>();
        if (corpse == null) corpse = go.AddComponent<CorpseItem>();
        corpse.spawner = this;
        corpse.decayTime = decayTime;

        ActiveCorpses.Add(corpse);
    }

    public void Unregister(CorpseItem corpse)
    {
        ActiveCorpses.Remove(corpse);
    }
}
