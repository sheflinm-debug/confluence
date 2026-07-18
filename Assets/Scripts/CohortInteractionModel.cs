using UnityEngine;

/// era3-sovereignty-interaction-gaps-spec.md §4: Cohort-level port of
/// SpeciesRelationshipManager.RollType's weight table (SpeciesRelationshipManager.cs) — same six-type
/// taxonomy (InteractionType, defined there and reused here directly, no redefinition), same
/// Producer/Producer vs Consumer/Consumer vs mixed branching and backbone-compatibility shape,
/// operating on CohortTraitSnapshot instead of live AgentController fields. Sociality is omitted — no
/// cohort-level analog exists in CohortTraitSnapshot, the same class of omission as
/// CohortEnergyModel's ContestUptakeMultiplier/RespirationTier terms (flagged there, same reasoning
/// here: a real per-agent behavioral trait with nothing to aggregate it into at cohort granularity).
public static class CohortInteractionModel
{
    public static InteractionType RollType(Cohort a, Cohort b)
    {
        bool aProducer = a.MetabolicClass == CohortMetabolicClass.Producer;
        bool bProducer = b.MetabolicClass == CohortMetabolicClass.Producer;
        bool sameBackbone = a.Traits.Backbone == b.Traits.Backbone;

        // [Neutralism, Mutualism, Commensalism, Parasitism, Competition, Amensalism] — identical
        // shape/values to SpeciesRelationshipManager.RollType's weight table.
        float[] w = new float[6];
        w[0] = 1f;

        if (aProducer && bProducer)
        {
            w[4] = 3f; w[5] = 1f; w[3] = 0.1f;
        }
        else if (!aProducer && !bProducer)
        {
            w[4] = 2f; w[3] = 2f; w[1] = 0.8f; w[5] = 0.5f;
        }
        else
        {
            w[3] = 2.5f; w[2] = 1.5f; w[1] = 1.2f; w[4] = 0.5f;
        }

        if (sameBackbone) { w[3] *= 1.5f; w[4] *= 1.2f; }
        else { w[3] *= 0.4f; w[0] *= 1.5f; w[2] *= 1.3f; }

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
}
