using UnityEngine;

/// A dead organism's remains - the only "food" in the sim. Spawned wherever an agent
/// dies of natural causes (starvation, atmosphere crisis, etc - NOT direct predation,
/// since a predation kill is eaten immediately by the hunter). Decays after a fixed
/// time if nothing scavenges it first.
public class CorpseItem : MonoBehaviour
{
    [HideInInspector] public CorpseSpawner spawner;
    public float decayTime = 12f;
    /// Body mass of the original organism, set by AgentController on death. Used for
    /// biomass-transfer calculation when a scavenger eats this corpse (spec §7).
    public float BodyMass = 0.001f;
    private float _age;

    void Update()
    {
        _age += Time.deltaTime;
        // Gradually release body mass as dissolved organics so heterotrophs can absorb them.
        // Scale factor keeps nutrient units consistent with Deplete() amounts.
        ChemicalNutrientPool.Deposit(transform.position, BodyMass * Time.deltaTime / decayTime * 0.1f);
        if (_age >= decayTime) Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (spawner != null) spawner.Unregister(this);
    }

    /// Consume this corpse and return its body mass for biomass-transfer calculation.
    public float Consume()
    {
        float mass = BodyMass;
        Destroy(gameObject);
        return mass;
    }
}
