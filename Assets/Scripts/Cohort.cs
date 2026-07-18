using UnityEngine;

/// population-energy-aggregation-spec.md §2: replaces the individual agent as the unit of
/// simulation for a lineage once its per-agent population is cut off (AgentController.Reproduce's
/// CivHasSettlement branch). Lives at Settlement or TerritoryCell level (§2.0) — never at civ level
/// directly; civ.Roster is a computed rollup over these, not a parallel structure.
// CivPopulation = this civ's own people. Resource = anything else (a wild/LLFP/domesticated
// population the civ can extract from) — CohortManagementTier carries the wild/LLFP/domesticated
// distinction for those; Role only needs to separate "us" from "a resource," not duplicate it.
public enum CohortRole { CivPopulation, Resource }

/// wild/LLFP/domesticated extraction tiers (§4.3). N/A (stays Wild) for CivPopulation-role cohorts —
/// a civ's own population isn't "extracted."
public enum CohortManagementTier { Wild, LLFP, Domesticated }

public enum CohortMetabolicClass { Producer, Consumer }

/// §2.1 seeding: mean + variance per axis needed by the energy/reproduction math, plus enough of
/// the morphological state to feed appearance-generation-spec's descriptor for civ-population
/// cohorts specifically (§2.0: "carry the lineage's trait_snapshot forward... same data, not a
/// duplicate record"). Seeded from a real statistical snapshot of the live agent population at the
/// moment per-agent simulation is cut off, then nudged (not reset) by later seed/absorb events.
public class CohortTraitSnapshot
{
    public float MeanSizeScale = 0.05f;     // mirrors AgentController.transform.localScale.x at spawn scale
    public float VarianceSizeScale = 0f;
    public MetabolismType Metabolism = MetabolismType.Heterotrophic;
    public BackboneElement Backbone = BackboneElement.Carbon;
    public float MeanPhotoEfficiency = 0.5f;  // mirrors AgentController.PhotoEfficiency
    public float MeanChemoEfficiency = 0.5f;  // mirrors AgentController.ChemoEfficiency

    /// Running mean update — Welford-style single-pass nudge, not a full recompute (cohorts don't
    /// keep the underlying sample list around). n is the effective sample count seen so far,
    /// capped so a long-lived cohort doesn't become totally inert to new seed events.
    public void Nudge(float sizeScale, MetabolismType metabolism, BackboneElement backbone, float photoEff, float chemoEff, ref int n)
    {
        n = Mathf.Min(n + 1, 200); // cap — see class comment
        float w = 1f / n;
        float dSize = sizeScale - MeanSizeScale;
        MeanSizeScale += dSize * w;
        VarianceSizeScale = Mathf.Max(0f, VarianceSizeScale + (dSize * dSize * (1f - w) - VarianceSizeScale) * w);
        MeanPhotoEfficiency += (photoEff - MeanPhotoEfficiency) * w;
        MeanChemoEfficiency += (chemoEff - MeanChemoEfficiency) * w;
        Metabolism = metabolism; // categorical — last-seeded wins, no meaningful "mean" over an enum
        Backbone = backbone;
    }

    /// Value copy — needed anywhere a second cohort seeds from an existing one's snapshot (e.g. a
    /// zone-based TerritoryCell cohort seeding from its civ's settlement-core cohort). Without this,
    /// assigning .Traits directly would alias the same instance, so nudging one cohort's traits would
    /// silently mutate the other's.
    public CohortTraitSnapshot Clone() => new CohortTraitSnapshot
    {
        MeanSizeScale = MeanSizeScale, VarianceSizeScale = VarianceSizeScale,
        Metabolism = Metabolism, Backbone = Backbone,
        MeanPhotoEfficiency = MeanPhotoEfficiency, MeanChemoEfficiency = MeanChemoEfficiency,
    };
}

public class Cohort
{
    public int LineageId;              // founding communityId this cohort originated from
    public int LocationProxy;          // Settlement.Id, or TerritoryCell.CellId for zone-based tracks
    public bool IsZoneBased;           // true => LocationProxy indexes TerritoryCells, false => Settlements
    public CohortRole Role;
    public CohortManagementTier ManagementTier = CohortManagementTier.Wild;
    public float Biomass;
    public CohortTraitSnapshot Traits = new CohortTraitSnapshot();
    // Computed, not stored: Producer/Consumer is fully determined by Traits.Metabolism already
    // (Phototrophic/Chemosynthetic/Mixotrophic all capture energy from the environment; only
    // Heterotrophic doesn't). A separate settable field here would just be the same duplicated-state
    // hazard as the CohortRole/CohortManagementTier overlap this file already avoids elsewhere — every
    // construction site would need to remember to set it correctly, and nothing would catch drift if
    // Traits.Metabolism changed later without updating it too.
    public CohortMetabolicClass MetabolicClass =>
        Traits.Metabolism == MetabolismType.Heterotrophic ? CohortMetabolicClass.Consumer : CohortMetabolicClass.Producer;
    public int? ManagedByCivId;        // null if wild
    private int _sampleCount;          // backs Traits.Nudge's running-mean cap

    public void SeedOrNudge(float sizeScale, MetabolismType metabolism, BackboneElement backbone, float photoEff, float chemoEff, float biomassContribution)
    {
        Traits.Nudge(sizeScale, metabolism, backbone, photoEff, chemoEff, ref _sampleCount);
        Biomass += biomassContribution;
    }
}

/// appearance-generation-spec §4.5/population-energy-aggregation-spec §3.1: real per-place storage
/// for zone-based tracks (Terraformer/Bloom Front), which have no Settlement concept. Additive to
/// Era3VisualManager.TierClaimRadius, not a replacement — the radius still decides which cells
/// currently count as a civ's territory each frame; this is the persistent biological state
/// underneath that boundary. cell_id is a real icosahedral grid index: this codebase doesn't have
/// a separate "planetary-grid-spec.md" (checked — no such file exists), so CellId is defined here as
/// the TectonicResult.UnitVerts vertex index, the one real icosahedral mesh already in code and
/// already what Era3VisualManager reads per-vertex for territory ownership.
public class TerritoryCell
{
    public int CellId;
    public int OwningCivId = -1;
    public readonly System.Collections.Generic.List<Cohort> Cohorts = new System.Collections.Generic.List<Cohort>();

    // era3-sovereignty-interaction-gaps-spec.md §3: claim_strength (the angular closeness already
    // driving ownership in Era3VisualManager.RebuildTerritory, exposed as a real named value rather
    // than an anonymous local) and whether another civ's claim radius also reaches this cell.
    public float ClaimStrength;
    public bool  IsContested;
}
