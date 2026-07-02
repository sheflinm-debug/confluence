using System.Collections.Generic;
using UnityEngine;

/// One mineral type with its display color and the tectonic conditions that make it
/// eligible to form at a given vertex. All fields are set by MineralDepositTable.
public class MineralDef
{
    public string Name;
    public Color Color;

    // Which rock types this mineral can form in (any match is sufficient).
    public RockType[] EligibleRockTypes;

    // Optional boundary constraint: None = no preference, otherwise boundary must match.
    public BoundaryType RequiredBoundary; // None = don't care

    // distanceToBoundary must be BELOW this to be eligible (1f = any distance).
    public float MaxBoundaryDistance;

    // Plate type constraint: true = continental only, false = oceanic only, null = either.
    public bool? RequiresContinental;

    // erosionDelta must exceed this for sedimentary deposits (0 = no deposition required).
    public float MinDepositionDelta;

    // Base probability at an eligible vertex before noise clustering (0-1).
    // Kept low so deposits are sparse patches, not continuous coverage.
    public float BaseChance;

    // Phase transition: melting and sublimation. Null = inert solid in sim temperature range.
    public float? MeltingPointK;          // solid → liquid; requires MeltsToLiquid to match world liquid
    public LiquidKind MeltsToLiquid;      // which LiquidKind is produced when this mineral melts
    public float? SublimationPointK;      // solid → gas directly (only at very low surface pressure)
    public string SublimatesTo;           // gas name injected into AtmosphereManager if sublimed

    // Archetype abundance multipliers: keyed by RockArchetypeId, value is a multiplier
    // on BaseChance. Unlisted archetypes get 1x. Planet-wide bias per spec Section 9.
    public Dictionary<RockArchetypeId, float> ArchetypeBoost;
}

/// Per-vertex deposit record computed at world-gen time and stored on TectonicResult
/// (via MineralDepositLayer). Null means no deposit at that vertex.
public class VertexDeposit
{
    public MineralDef Mineral;
    public float Richness; // 0-1, drives alpha in the overlay mesh
}

/// The full deposit layer for a planet, computed once at genesis.
public class MineralDepositLayer
{
    public VertexDeposit[] ByVertex;    // parallel to TectonicResult.UnitVerts
    public float[] RuntimeRichness;     // mutable copy of per-vertex richness; decays as minerals melt,
                                        // recovers as liquid refreezes. Parallel to ByVertex.
}

public static class MineralDepositTable
{
    // -----------------------------------------------------------------------
    // Mineral definitions — colors and tectonic eligibility rules.
    // Colors chosen to be distinct from each other and from the pale rock base.
    // -----------------------------------------------------------------------
    public static readonly MineralDef Iron = new MineralDef
    {
        Name = "Iron Ore",
        Color = new Color(0.55f, 0.15f, 0.08f, 0.90f), // dark rust-red
        EligibleRockTypes = new[] { RockType.IgneousMafic, RockType.Sedimentary },
        RequiredBoundary = BoundaryType.None,
        MaxBoundaryDistance = 1f,
        RequiresContinental = null,
        MinDepositionDelta = 0f,
        BaseChance = 0.20f,
        ArchetypeBoost = new Dictionary<RockArchetypeId, float>
        {
            { RockArchetypeId.OxidizedIronRich,   4.0f },
            { RockArchetypeId.MaficBasaltic,       2.5f },
            { RockArchetypeId.UltramaficReducing,  1.8f },
            { RockArchetypeId.MetalRichStripped,   3.0f },
            { RockArchetypeId.FelsicGranitic,      0.4f },
            { RockArchetypeId.IcySilicateMix,      0.5f },
        },
    };

    public static readonly MineralDef Nickel = new MineralDef
    {
        Name = "Nickel",
        Color = new Color(0.45f, 0.55f, 0.38f, 0.85f), // muted olive-green
        EligibleRockTypes = new[] { RockType.IgneousMafic },
        RequiredBoundary = BoundaryType.Divergent,
        MaxBoundaryDistance = 0.40f,
        RequiresContinental = false, // oceanic plates
        MinDepositionDelta = 0f,
        BaseChance = 0.14f,
        ArchetypeBoost = new Dictionary<RockArchetypeId, float>
        {
            { RockArchetypeId.UltramaficReducing,  3.5f },
            { RockArchetypeId.MaficBasaltic,       2.0f },
            { RockArchetypeId.MetalRichStripped,   2.5f },
        },
    };

    public static readonly MineralDef Chromium = new MineralDef
    {
        Name = "Chromium",
        Color = new Color(0.28f, 0.42f, 0.25f, 0.85f), // deep chrome-green
        EligibleRockTypes = new[] { RockType.IgneousMafic },
        RequiredBoundary = BoundaryType.Divergent,
        MaxBoundaryDistance = 0.30f,
        RequiresContinental = null,
        MinDepositionDelta = 0f,
        BaseChance = 0.10f,
        ArchetypeBoost = new Dictionary<RockArchetypeId, float>
        {
            { RockArchetypeId.UltramaficReducing,  4.0f },
            { RockArchetypeId.MaficBasaltic,       2.0f },
        },
    };

    // Porphyry copper forms at subduction-zone arc volcanics — convergent felsic.
    public static readonly MineralDef Copper = new MineralDef
    {
        Name = "Copper",
        Color = new Color(0.18f, 0.52f, 0.42f, 0.90f), // malachite blue-green
        EligibleRockTypes = new[] { RockType.IgneousFelsic, RockType.Metamorphic },
        RequiredBoundary = BoundaryType.Convergent,
        MaxBoundaryDistance = 0.45f,
        RequiresContinental = null,
        MinDepositionDelta = 0f,
        BaseChance = 0.15f,
        ArchetypeBoost = new Dictionary<RockArchetypeId, float>
        {
            { RockArchetypeId.FelsicGranitic,      2.5f },
            { RockArchetypeId.MaficBasaltic,       1.5f },
            { RockArchetypeId.SulfideSulfurRich,   2.0f },
        },
    };

    // Tin granites: felsic intrusions at continental arcs, deeper/older than copper.
    public static readonly MineralDef Tin = new MineralDef
    {
        Name = "Tin",
        Color = new Color(0.72f, 0.72f, 0.65f, 0.80f), // silver-gray
        EligibleRockTypes = new[] { RockType.IgneousFelsic },
        RequiredBoundary = BoundaryType.Convergent,
        MaxBoundaryDistance = 0.50f,
        RequiresContinental = true,
        MinDepositionDelta = 0f,
        BaseChance = 0.10f,
        ArchetypeBoost = new Dictionary<RockArchetypeId, float>
        {
            { RockArchetypeId.FelsicGranitic,      3.0f },
            { RockArchetypeId.CarbonateEvaporite,  1.5f },
        },
    };

    // Gold: hydrothermal, concentrated near any boundary type.
    public static readonly MineralDef Gold = new MineralDef
    {
        Name = "Gold",
        Color = new Color(0.85f, 0.70f, 0.08f, 0.90f), // rich gold
        EligibleRockTypes = new[] { RockType.IgneousFelsic, RockType.Metamorphic, RockType.IgneousMafic },
        RequiredBoundary = BoundaryType.None,
        MaxBoundaryDistance = 0.25f, // only near boundaries (hydrothermal)
        RequiresContinental = null,
        MinDepositionDelta = 0f,
        BaseChance = 0.06f, // rare
        ArchetypeBoost = new Dictionary<RockArchetypeId, float>
        {
            { RockArchetypeId.FelsicGranitic,      2.0f },
            { RockArchetypeId.MetalRichStripped,   2.5f },
            { RockArchetypeId.OxidizedIronRich,    1.5f },
        },
    };

    // Sulfur: volcanic, near boundaries, especially on sulfide-archetype worlds.
    public static readonly MineralDef Sulfur = new MineralDef
    {
        Name = "Sulfur",
        Color = new Color(0.88f, 0.82f, 0.08f, 0.88f), // bright brimstone-yellow
        EligibleRockTypes = new[] { RockType.IgneousMafic, RockType.IgneousFelsic },
        RequiredBoundary = BoundaryType.None,
        MaxBoundaryDistance = 0.35f,
        RequiresContinental = null,
        MinDepositionDelta = 0f,
        BaseChance = 0.12f,
        ArchetypeBoost = new Dictionary<RockArchetypeId, float>
        {
            { RockArchetypeId.SulfideSulfurRich,   5.0f },
            { RockArchetypeId.UltramaficReducing,  2.0f },
            { RockArchetypeId.MaficBasaltic,       1.5f },
        },
        MeltingPointK  = 388f,
        MeltsToLiquid  = LiquidKind.MoltenSulfur,
        // Sublimation only relevant at very low pressure (< 0.0002 bar); de-prioritized.
    };

    // Evaporite salt/gypsum: shallow basins with net sedimentary deposition, continental.
    public static readonly MineralDef Evaporite = new MineralDef
    {
        Name = "Evaporite (Salt/Gypsum)",
        Color = new Color(0.96f, 0.94f, 0.88f, 0.75f), // white salt flat
        EligibleRockTypes = new[] { RockType.Sedimentary },
        RequiredBoundary = BoundaryType.None,
        MaxBoundaryDistance = 1f,
        RequiresContinental = true,
        MinDepositionDelta = 0.02f,
        BaseChance = 0.22f,
        ArchetypeBoost = new Dictionary<RockArchetypeId, float>
        {
            { RockArchetypeId.CarbonateEvaporite,  4.0f },
            { RockArchetypeId.FelsicGranitic,      1.5f },
            { RockArchetypeId.OxidizedIronRich,    1.8f },
        },
    };

    // Coal precursor: organic-rich continental sedimentary deposition.
    public static readonly MineralDef Coal = new MineralDef
    {
        Name = "Coal",
        Color = new Color(0.12f, 0.10f, 0.08f, 0.90f), // near-black
        EligibleRockTypes = new[] { RockType.Sedimentary },
        RequiredBoundary = BoundaryType.None,
        MaxBoundaryDistance = 1f,
        RequiresContinental = true,
        MinDepositionDelta = 0.03f,
        BaseChance = 0.16f,
        ArchetypeBoost = new Dictionary<RockArchetypeId, float>
        {
            { RockArchetypeId.CarbonRichReduced,   4.0f },
            { RockArchetypeId.FelsicGranitic,      1.5f },
            { RockArchetypeId.CarbonateEvaporite,  2.0f },
        },
    };

    // Graphite/carbon: reduced carbon-archetype worlds, near-surface metamorphic zones.
    public static readonly MineralDef Graphite = new MineralDef
    {
        Name = "Graphite",
        Color = new Color(0.22f, 0.22f, 0.20f, 0.85f), // dark charcoal
        EligibleRockTypes = new[] { RockType.Metamorphic, RockType.IgneousMafic },
        RequiredBoundary = BoundaryType.None,
        MaxBoundaryDistance = 1f,
        RequiresContinental = null,
        MinDepositionDelta = 0f,
        BaseChance = 0.12f,
        ArchetypeBoost = new Dictionary<RockArchetypeId, float>
        {
            { RockArchetypeId.CarbonRichReduced,   5.0f },
            { RockArchetypeId.GlassyVitrified,     2.0f },
            { RockArchetypeId.UltramaficReducing,  1.5f },
        },
    };

    public static readonly MineralDef[] All = {
        Iron, Nickel, Chromium, Copper, Tin, Gold, Sulfur, Evaporite, Coal, Graphite
    };

    // -----------------------------------------------------------------------
    // Deposit generation — called once at world-gen, stores per-vertex results.
    // -----------------------------------------------------------------------

    /// Generates the full mineral deposit layer from tectonic data and the rolled
    /// rock archetype. Each vertex gets at most ONE mineral (the highest-scoring
    /// eligible one after noise, to avoid visual clutter from overlapping deposits).
    public static MineralDepositLayer Generate(TectonicResult tectonics, RockArchetypeDef archetype)
    {
        int n = tectonics.UnitVerts.Count;
        var deposits = new VertexDeposit[n];

        // Three fixed noise directions for clustered deposit placement — same approach
        // as the ocean wave directions: evenly spaced to avoid grid artifacts.
        // Each mineral uses a different phase offset so deposits of different types
        // don't all cluster in the same locations.
        var noiseAxes = new Vector3[]
        {
            new Vector3(0.577f, 0.577f, 0.577f).normalized,
            new Vector3(-0.707f, 0.707f, 0f).normalized,
            new Vector3(0.408f, -0.408f, 0.816f).normalized,
            new Vector3(-0.577f, -0.577f, 0.577f).normalized,
        };

        for (int v = 0; v < n; v++)
        {
            Vector3 dir = tectonics.UnitVerts[v];
            RockType rock = tectonics.RockTypeAtVertex[v];
            BoundaryType boundary = tectonics.BoundaryTypeAtVertex[v];
            float distBound = tectonics.DistanceToBoundary[v];
            float erosion = tectonics.ErosionDelta[v];
            bool isContinental = tectonics.Plates[tectonics.PlateId[v]].Type == PlateType.Continental;

            MineralDef best = null;
            float bestScore = 0f;

            for (int m = 0; m < All.Length; m++)
            {
                MineralDef mineral = All[m];

                // --- Tectonic eligibility checks ---
                bool rockOk = false;
                foreach (var rt in mineral.EligibleRockTypes)
                    if (rt == rock) { rockOk = true; break; }
                if (!rockOk) continue;

                if (mineral.RequiredBoundary != BoundaryType.None && boundary != mineral.RequiredBoundary)
                    continue;

                if (distBound > mineral.MaxBoundaryDistance) continue;

                if (mineral.RequiresContinental.HasValue &&
                    mineral.RequiresContinental.Value != isContinental) continue;

                if (erosion < mineral.MinDepositionDelta) continue;

                // --- Archetype abundance multiplier ---
                float archetypeBoost = mineral.ArchetypeBoost != null &&
                    mineral.ArchetypeBoost.TryGetValue(archetype.Id, out float b) ? b : 1f;

                // --- Clustered noise: sum of 2 sinusoids at different frequencies for
                //     this mineral, offset by mineral index so deposits don't all align.
                float phaseOffset = m * 1.3f; // different clustering per mineral
                float noise =
                    Mathf.Sin(Vector3.Dot(dir, noiseAxes[m % 4]) * 12f + phaseOffset) * 0.5f +
                    Mathf.Sin(Vector3.Dot(dir, noiseAxes[(m + 1) % 4]) * 7f + phaseOffset * 0.7f) * 0.5f;
                // noise is -1..1; remap to 0..1 so it acts as a probability gate.
                float noiseFactor = (noise + 1f) * 0.5f;

                float score = mineral.BaseChance * archetypeBoost * noiseFactor;

                // Only include this vertex in the deposit if score clears a threshold,
                // keeping deposits as localized patches rather than a continuous wash.
                const float threshold = 0.18f;
                if (score <= threshold) continue;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = mineral;
                }
            }

            if (best != null)
            {
                // Richness: how far above threshold the score is, clamped 0-1.
                float richness = Mathf.Clamp01((bestScore - 0.18f) / 0.40f);
                deposits[v] = new VertexDeposit { Mineral = best, Richness = richness };
            }
        }

        var layer = new MineralDepositLayer { ByVertex = deposits };

        // RuntimeRichness starts as a copy of the genesis richness. FluidDynamicsManager
        // drains it as minerals melt and refills it as liquid refreezes.
        layer.RuntimeRichness = new float[n];
        for (int v = 0; v < n; v++)
            layer.RuntimeRichness[v] = deposits[v] != null ? deposits[v].Richness : 0f;

        return layer;
    }
}
