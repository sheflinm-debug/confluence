using System.Collections.Generic;
using UnityEngine;

/// Tracks this planet's environment colors (ground rock archetype + current ocean/liquid color)
/// and hands out lineage/settlement hues that stay visually legible against them — previously
/// organism and settlement colors were pure random/golden-ratio hue spreads with no relationship
/// to the terrain, so a lineage could easily land on (say) sandy tan on sandy-tan ground, or blue
/// against a blue ocean, and become hard to spot. Set once at genesis (SimulationBootstrap, right
/// after the rock archetype rolls); ocean color is read live from FluidDynamicsManager since it can
/// shift with liquid temperature.
public static class PlanetPalette
{
    public static Color GroundPrimary = new Color(0.55f, 0.45f, 0.35f);
    public static Color GroundAccent  = new Color(0.55f, 0.45f, 0.35f);

    public static void SetGround(RockArchetypeDef archetype)
    {
        if (archetype == null) return;
        GroundPrimary = archetype.Primary;
        GroundAccent  = archetype.Accent;
    }

    public static Color OceanColor => FluidDynamicsManager.Instance != null
        ? FluidDynamicsManager.Instance.CurrentLiquidColor
        : new Color(0.25f, 0.55f, 0.9f); // matches GameHUD's ocean-bar fallback before liquid exists

    private const float ExclusionMargin = 0.07f; // hue-circle fraction blocked around each env color
    private const int   Resolution      = 720;   // sampling density for the exclusion search below

    /// Every hue-circle sample point (0..Resolution) NOT within ExclusionMargin of a ground/ocean
    /// hue. Recomputed on demand rather than cached — only called at spawn/founding time, never
    /// per-frame, and ocean color can change (liquid temperature drifts), so a cache would go stale.
    private static List<int> AllowedHueSamples()
    {
        Color.RGBToHSV(GroundPrimary, out float hg1, out _, out _);
        Color.RGBToHSV(GroundAccent,  out float hg2, out _, out _);
        Color.RGBToHSV(OceanColor,    out float ho,  out _, out _);
        var excluded = new (float lo, float span)[]
        {
            (hg1 - ExclusionMargin, ExclusionMargin * 2f),
            (hg2 - ExclusionMargin, ExclusionMargin * 2f),
            (ho  - ExclusionMargin, ExclusionMargin * 2f),
        };

        var allowed = new List<int>(Resolution);
        for (int i = 0; i < Resolution; i++)
        {
            float h = (float)i / Resolution;
            bool blocked = false;
            foreach (var (lo, span) in excluded)
            {
                float d = h - lo; d -= Mathf.Floor(d); // wrap distance into [0,1)
                if (d <= span) { blocked = true; break; }
            }
            if (!blocked) allowed.Add(i);
        }
        return allowed;
    }

    /// The `index`-th of `count` evenly-spaced hues, confined to whatever's left of the hue circle
    /// once ground/ocean bands are excluded — used when spawning a known-size batch (a founding
    /// population, an NPC community roster) so members/communities stay both distinct from each
    /// other AND legible against the terrain.
    public static float ContrastHueForIndex(int index, int count)
    {
        var allowed = AllowedHueSamples();
        if (allowed.Count == 0) return ((float)index / Mathf.Max(1, count) % 1f + 1f) % 1f; // degenerate fallback (extreme world palette)
        int pick = Mathf.FloorToInt((float)index / Mathf.Max(1, count) * allowed.Count) % allowed.Count;
        return (float)allowed[pick] / Resolution;
    }

    /// Golden-ratio-spaced hue keyed on an arbitrary stable id (a civId, not a bounded index) —
    /// same exclusion logic as ContrastHueForIndex, for callers that don't know the total count
    /// up front (Era3VisualManager.CivColor is called per-civ independently, not as a batch).
    public static float ContrastHueForId(int id)
    {
        var allowed = AllowedHueSamples();
        if (allowed.Count == 0) return ((id * 0.61803398875f) % 1f + 1f) % 1f; // degenerate fallback
        int idx = Mathf.Abs(Mathf.RoundToInt(id * 0.61803398875f * allowed.Count)) % allowed.Count;
        return (float)allowed[idx] / Resolution;
    }

    /// Keeps a branded/preferred hue (e.g. the player's gold) as-is UNLESS it actually clashes with
    /// this world's ground/ocean — in which case it nudges to the nearest still-legible hue instead
    /// of jumping to a totally different scheme. So the player stays gold on ordinary worlds, but a
    /// gold/sandy-toned planet doesn't leave "my own civilization" blending into the terrain.
    public static float ContrastHueNear(float preferredHue)
    {
        var allowed = AllowedHueSamples();
        if (allowed.Count == 0) return preferredHue; // degenerate fallback
        int preferredSample = Mathf.RoundToInt(((preferredHue % 1f + 1f) % 1f) * Resolution) % Resolution;

        int best = allowed[0], bestDist = int.MaxValue;
        foreach (int s in allowed)
        {
            int d = Mathf.Abs(s - preferredSample);
            d = Mathf.Min(d, Resolution - d); // wrap distance
            if (d < bestDist) { bestDist = d; best = s; }
        }
        return (float)best / Resolution;
    }
}
