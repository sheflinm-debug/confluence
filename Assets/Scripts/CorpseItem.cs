using UnityEngine;

/// A dead organism's remains - the only "food" in the sim. Spawned wherever an agent
/// dies of natural causes (starvation, atmosphere crisis, etc - NOT direct predation,
/// since a predation kill is eaten immediately by the hunter). Decays after a fixed
/// time if nothing scavenges it first.
public class CorpseItem : MonoBehaviour
{
    [HideInInspector] public CorpseSpawner spawner;
    public float decayTime = 12f;
    private float _age;

    void Update()
    {
        _age += Time.deltaTime;
        if (_age >= decayTime) Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (spawner != null) spawner.Unregister(this);
    }

    public void Consume() => Destroy(gameObject);
}
