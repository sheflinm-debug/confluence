using UnityEngine;

/// population-energy-aggregation-spec.md §3/§4: cohort-level adaptations of AgentController's real
/// per-organism Kleiber BMR (ComputeDemand, AgentController.cs:2622) and producer-metabolism
/// (UpdateProducerMetabolism/UpdateChemosyntheticMetabolism, AgentController.cs:2174/2219) formulas.
///
/// Design decision (flagged, since the spec leaves the individual-to-aggregate bridge open):
/// Cohort.Biomass is denominated in spawn-scale-equivalent INDIVIDUALS, not kilograms — one unit of
/// biomass is "one organism at Traits.MeanSizeScale." That lets every per-individual rate below
/// (Kleiber, photo/chemo acquisition) compose directly with Biomass via simple multiplication,
/// exactly mirroring §4's own "civ_population_biomass × Kleiber_BMR_rate" formula, without a
/// separate headcount field the spec never asks for.
public static class CohortEnergyModel
{
    // Mirrors AgentController.KleiberK/BackboneQ10/BackboneRefTemp (AgentController.cs:2437-2441).
    // Duplicated rather than exposed from AgentController because those are private static readonly
    // arrays on a MonoBehaviour instance class — a cohort has no AgentController to read them from.
    private static readonly float[] KleiberK        = { 0.067f, 0.078f, 0.073f, 0.062f, 0.084f, 0.067f, 0.056f, 0.073f };
    private static readonly float[] BackboneQ10     = { 2.0f, 2.5f, 2.2f, 2.0f, 3.0f, 2.5f, 2.0f, 2.2f };
    private static readonly float[] BackboneRefTemp = { 20f, 15f, 25f, 30f, 10f, 20f, 35f, 20f };

    /// Per-individual-equivalent metabolic demand rate — multiply by Cohort.Biomass for the
    /// cohort's total upkeep. worldPos feeds ClimateManager's Q10 temperature scaling, same as the
    /// live per-agent formula.
    public static float ComputeDemandPerBiomass(CohortTraitSnapshot traits, Vector3 worldPos)
    {
        int bIdx = Mathf.Clamp((int)traits.Backbone, 0, KleiberK.Length - 1);
        float bmr = KleiberK[bIdx] * Mathf.Pow(Mathf.Max(traits.MeanSizeScale, 0.001f), 0.75f);

        float localTemp = ClimateManager.GetTemperature(worldPos);
        float q10 = BackboneQ10[bIdx];
        float refTemp = BackboneRefTemp[bIdx];
        float q10Mult = Mathf.Clamp(Mathf.Pow(q10, (localTemp - refTemp) / 10f), 0.25f, 4f);

        // No per-agent Activity Budget / fitness multipliers here — those are individual-behavior
        // terms (movement choices, trait-driven survival penalties) that don't have a cohort-level
        // analog; mean-field demand uses BMR × Q10 only.
        return bmr * q10Mult;
    }

    /// Per-individual-equivalent producer yield rate (Photosynthetic/Chemosynthetic/Mixotrophic
    /// only — Heterotrophic cohorts are MetabolicClass.Consumer and never call this). depth is the
    /// liquid depth for the Beer-Lambert photic attenuation term (AgentController.cs:2193); pass 0
    /// for a settlement/cell without per-agent depth tracking — a flagged simplification, since
    /// cohorts sit at one representative location rather than each individual's own depth.
    public static float ComputeProducerYieldPerBiomass(CohortTraitSnapshot traits, Vector3 worldPos, Vector3 planetCenter, float liquidDepth = 0f)
    {
        float photo = 0f, chemo = 0f;
        if (traits.Metabolism == MetabolismType.Phototrophic || traits.Metabolism == MetabolismType.Mixotrophic)
            photo = ComputePhotoYieldPerBiomass(traits, worldPos, planetCenter, liquidDepth);
        if (traits.Metabolism == MetabolismType.Chemosynthetic || traits.Metabolism == MetabolismType.Mixotrophic)
            chemo = ComputeChemoYieldPerBiomass(traits, worldPos);

        if (traits.Metabolism == MetabolismType.Mixotrophic)
            return photo * 0.7f + chemo * 0.7f; // mirrors AgentController's 70%/70% mixotrophic blend
        return photo + chemo; // exactly one of these is nonzero for a pure Photo/Chemo cohort
    }

    private static float ComputePhotoYieldPerBiomass(CohortTraitSnapshot traits, Vector3 worldPos, Vector3 planetCenter, float liquidDepth)
    {
        DayNightCycle dayNight = DayNightCycle.Instance;
        float solar = 0f;
        if (dayNight != null)
        {
            // Mirrors AgentController.UpdateProducerMetabolism's (transform.position - planetCenter)
            // normal (AgentController.cs:2180) — worldPos alone is NOT planet-centered in this
            // codebase (planetCenter is an arbitrary, explicitly-passed-around Vector3, not the origin).
            Vector3 normal = (worldPos - planetCenter).normalized;
            solar = dayNight.SolarExposure(normal);
        }
        float atmosTransparency = AtmosphereManager.Instance != null
            ? Mathf.Clamp01(2f / Mathf.Max(AtmosphereManager.Instance.PressureBar, 0.1f)) : 1f;
        // photoSizeScale (AgentController.cs:2188) folds into MeanSizeScale-based sizing implicitly:
        // a cohort's per-individual yield still scales with its representative organism's surface
        // area, same Pow(scale/0.05, 0.8) law.
        float photoSizeScale = Mathf.Pow(Mathf.Max(traits.MeanSizeScale, 0.001f) / 0.05f, 0.80f);
        float photicAttenuation = Mathf.Exp(-0.10f * liquidDepth); // PhoticExtinctionCoeff default (AgentController.cs:255)
        const float solarChargeRate = 2f; // AgentController.cs:251 default
        return solarChargeRate * solar * AgentController.WorldSolarFluxFactor * atmosTransparency
             * traits.MeanPhotoEfficiency * photoSizeScale * photicAttenuation;
    }

    private static float ComputeChemoYieldPerBiomass(CohortTraitSnapshot traits, Vector3 worldPos)
    {
        float poolNutrients = ChemicalNutrientPool.Sample(worldPos);
        float ventEnergy = HydrothermalVentManager.Instance != null
            ? HydrothermalVentManager.Instance.GetVentEnergyAt(worldPos) : 0f;
        float nutrients = Mathf.Max(poolNutrients, ventEnergy);
        float chemoSizeScale = Mathf.Pow(Mathf.Max(traits.MeanSizeScale, 0.001f) / 0.05f, 0.80f);
        const float solarChargeRate = 2f; // AgentController.cs:251 default — same constant, misleadingly named upstream
        // ContestUptakeMultiplier (AgentController.cs:2426) omitted: it depends on contestPropensity,
        // an individual behavioral trait with no cohort-level analog — neutral 1.0 here.
        return solarChargeRate * nutrients * traits.MeanChemoEfficiency * GibbsFactor(traits.Backbone) * chemoSizeScale;
    }

    /// Mirrors AgentController.GibbsFactor()'s backbone-chemistry term (AgentController.cs:2526-2537)
    /// — pure function of backbone, so directly reusable at cohort level without any per-agent state.
    /// Omits the per-agent RespirationTier tierFactor multiplier that follows it in AgentController
    /// (AgentController.cs:2544) — RespirationTier isn't tracked in CohortTraitSnapshot, so cohort
    /// yield assumes the fully-adapted tier rather than modeling the primitive/anaerobic penalty.
    private static float GibbsFactor(BackboneElement backbone) => backbone switch
    {
        BackboneElement.Sulfur     => 1.00f,
        BackboneElement.Carbon     => 0.30f,
        BackboneElement.Tin        => 0.20f,
        BackboneElement.Silicon    => 0.15f,
        BackboneElement.Phosphorus => 0.18f,
        BackboneElement.Nitrogen   => 0.10f,
        _ => 0.20f,
    };
}
