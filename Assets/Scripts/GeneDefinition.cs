using System;

/// One option offered when a gene event fires with a player choice.
public class GeneChoice
{
    public string Label;
    public Action<AgentController> Apply;
    /// Optional: if set, the choice is hidden from the popup when this returns false.
    /// Lets gene events filter options by morphology (e.g. no Visual option without vision).
    public Func<AgentController, bool> IsAvailable;

    /// Optional: if set, autonomous fitness-driven paths (NPC DefaultAutoApply, per-reproduction
    /// mutation origination) will not pick this choice unless it returns true — an informed PLAYER
    /// can still pick it via the popup regardless (IsAvailable, not this, controls popup visibility).
    /// Use for choices that are only an actual fitness win under certain world conditions (e.g.
    /// photosynthesis is only viable where there's enough light) so evolution doesn't autonomously
    /// commit a lineage to a losing strategy.
    public Func<AgentController, bool> FitnessGate;
}

/// Data-driven description of a single Section 6b gene event. Adding a new gene means
/// adding one of these to GeneCatalog - no new trigger/queue/choice-application code.
public class GeneDefinition
{
    public string Id;

    /// Gene ids that must already be acquired before this one can become eligible.
    public string[] Prerequisites;

    /// Trigger condition, checked only once prerequisites are satisfied. Null = always
    /// eligible once prerequisites are met (used for automatic, non-choice gates).
    public Func<AgentController, bool> IsEligible;

    /// If non-empty, fires a player-choice popup. If null/empty, AutoApply runs immediately.
    public GeneChoice[] Choices;

    /// Used instead of Choices for genes with no player decision (e.g. a phase gate).
    public Action<AgentController> AutoApply;

    /// Applied to agents that become eligible AFTER the first player-choice popup has already
    /// fired globally. If null, falls back to Choices[last].Apply (the conservative branch).
    public Action<AgentController> DefaultAutoApply;

    /// True for Era 1 gene events. When Era 2 is active, player-community agents get
    /// DefaultAutoApply instead of a choice popup so Era 1 prompts don't bleed into Era 2.
    public bool IsEra1Event;

    /// Per-reproduction origination probability (appearance/gene-adoption spec §B). When > 0, an
    /// NPC offspring that meets this gene's prerequisites and eligibility has this per-birth chance
    /// to ORIGINATE the trait in itself — a single individual, not a synchronized population-wide
    /// flip — after which it spreads through the lineage by ordinary inheritance and differential
    /// fitness. This is additive to the existing eligibility system (a safety net remains), so it
    /// only ever makes adoption more per-lineage/staggered, never blocks a gene from appearing.
    /// 0 = disabled (only the existing eligibility path applies). TUNABLE per gene.
    public float BaseMutationProbability = 0f;
}
