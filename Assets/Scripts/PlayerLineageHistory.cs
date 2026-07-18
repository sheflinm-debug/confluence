using System.Collections.Generic;

/// appearance-generation-spec §3.4: "the state vector persists across eras for the player's own
/// lineage." Player-only (community 0) by the spec's own scope — a static store because individual
/// AgentController instances don't survive generational turnover (or Era 3's settlement absorption),
/// but the lineage's history should. Produces the three things §3.4 asks for:
///   - Current form:    AppearanceDescriptor.Build(agent) directly — nothing new needed here.
///   - Historical gallery: GalleryEntry snapshots, recorded at each real Era 1 axis-setter call
///     (AgentController), not at every cosmetic ApplyMorphology refresh.
///   - Trait inventory:  GetTraitInventory below, using GeneEvolutionManager's real catalog/
///     eligibility engine rather than a duplicated, hand-maintained copy of its conditions.
public static class PlayerLineageHistory
{
    public struct GalleryEntry
    {
        public float Timestamp;
        public string EventLabel;
        public AppearanceDescriptor Snapshot;
    }

    private static readonly List<GalleryEntry> _gallery = new List<GalleryEntry>();
    public static IReadOnlyList<GalleryEntry> Gallery => _gallery;

    /// Called from AgentController's Era-1-axis setters (SetBodyPlan, SetSegmentation, etc.) for the
    /// player's own agent only. Cheap: one descriptor build per real morphological transition, not
    /// per tick — bounded by how many discrete Era 1 events a single lineage can ever fire.
    public static void RecordSnapshot(AgentController agent, string eventLabel)
    {
        if (agent == null || agent.communityId != 0) return;
        _gallery.Add(new GalleryEntry
        {
            Timestamp = UnityEngine.Time.time,
            EventLabel = eventLabel,
            Snapshot = AppearanceDescriptor.Build(agent),
        });
        UnityEngine.Debug.Log($"[LineageHistory] Gallery entry #{_gallery.Count}: {eventLabel} (t={UnityEngine.Time.time:F0}s)");
    }

    public struct TraitInventory
    {
        public List<string> Fired;
        public List<string> Available;
        public List<string> NotYet;
    }

    /// Live query against the current player representative — Fired persists across generations
    /// (GeneEvolutionManager._presentedGlobally), Available/NotYet reflect this specific individual
    /// right now (the same axis state it inherited, so this reads the same as it would for any
    /// same-generation sibling). Scoped to IsEra1Event entries — the appearance state vector this
    /// spec section is actually about, not Era 2/3 decisions.
    public static TraitInventory GetTraitInventory(AgentController playerAgent)
    {
        var inv = new TraitInventory
        {
            Fired = new List<string>(),
            Available = new List<string>(),
            NotYet = new List<string>(),
        };
        foreach (var gene in GeneEvolutionManager.Catalog)
        {
            if (!gene.IsEra1Event) continue;
            var status = GeneEvolutionManager.GetTraitStatus(playerAgent, gene);
            switch (status)
            {
                case GeneEvolutionManager.TraitStatus.Fired:     inv.Fired.Add(gene.Id); break;
                case GeneEvolutionManager.TraitStatus.Available: inv.Available.Add(gene.Id); break;
                default:                                         inv.NotYet.Add(gene.Id); break;
            }
        }
        return inv;
    }

    /// Test/scene-reload hook — mirrors GeneEvolutionManager.ResetCatalog's role for a fresh run.
    public static void Reset() => _gallery.Clear();
}
