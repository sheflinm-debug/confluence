using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// Generic, data-driven gene-event engine (Section 6b). Replaces the old single
/// hardcoded vision/speed event - genes are registered as data (GeneCatalog), and this
/// class just evaluates eligibility (prerequisites + trigger condition) against whatever
/// genes are registered, queues a choice popup if the gene has one, or applies it
/// automatically if not. Adding a new gene never requires touching this file.
public class GeneEvolutionManager : MonoBehaviour
{
    private static readonly List<GeneDefinition> _catalog = new List<GeneDefinition>();
    private static readonly Queue<PendingChoice> _pending = new Queue<PendingChoice>();
    // Atmosphere events (Great Gas Event etc.) queue as narrative-only popups with no agent target.
    private static readonly Queue<string> _atmosphereEvents = new Queue<string>();
    // Genes that have already been presented to the player; subsequent eligible agents
    // get the gene auto-applied using the gene's DefaultAutoApply (or choice[last] if unset).
    private static readonly HashSet<string> _presentedGlobally = new HashSet<string>();

    // Community 0 is the "player's community" - only its agents get the choice popup.
    public static int PlayerCommunityId = 0;

    // Spawner reference so player gene choices can be broadcast to the whole community.
    private static AgentSpawner _agentSpawner;
    public static void SetSpawner(AgentSpawner s) { _agentSpawner = s; }

    // Minimum unscaled seconds between player-facing gene choice popups.
    // Only applies to the player community popup queue, not to NPC auto-evolution.
    private const float GeneCooldownSeconds = 7f;
    private static float _playerLastGeneUnscaledTime = float.NegativeInfinity;

    // Seconds before an unanswered player choice is auto-resolved by picking the highest-favorability
    // (green) available option — see AutoSelectBestChoice. NOT a random pick, and not merely the
    // gene's default: the same "best under current conditions" signal that colors the buttons.
    private const float AutoSelectSeconds = 15f;

    private struct PendingChoice
    {
        public AgentController Agent;
        public GeneDefinition Gene;
        public float ShowUnscaledTime; // set when the popup first appears
    }

    /// True while the gene event popup or atmosphere-crisis banner is on screen.
    /// InspectPopup checks this to avoid opening info boxes through the popup.
    public static bool IsShowingPopup => _pending.Count > 0 || _atmosphereEvents.Count > 0;

    // Unscaled seconds since GeneCatalog.BuildDefault() ran (≈ session start).
    // Used as a time-based fallback in gene eligibility so events always eventually fire
    // even when eat-count conditions can't be met (e.g. chemosynthetics on thin vent flux).
    private static float _sessionStartUnscaled;
    public static float SessionElapsed => Time.unscaledTime - _sessionStartUnscaled;

    public static void ResetCatalog()
    {
        _catalog.Clear();
        _pending.Clear();
        _presentedGlobally.Clear();
        _atmosphereEvents.Clear();
        _playerLastGeneUnscaledTime = float.NegativeInfinity;
        _sessionStartUnscaled = Time.unscaledTime;
    }

    public static void Register(GeneDefinition gene) => _catalog.Add(gene);

    /// appearance-generation-spec §3.4 (player-species historical record): read-only catalog access
    /// so other systems can query trait status without duplicating GeneCatalog.cs's eligibility
    /// logic. _presentedGlobally is the right "fired, ever, for this lineage" signal — unlike
    /// per-agent AcquiredGenes (which resets every generation, see AgentController), it's a
    /// session-persistent set that survives exactly as long as the player's lineage does.
    public static IReadOnlyList<GeneDefinition> Catalog => _catalog;
    public static IReadOnlyCollection<string> PresentedIds => _presentedGlobally;

    public enum TraitStatus { Fired, Available, NotYet }

    /// Fired = already presented to this lineage at some point (persists across generations).
    /// Available = prerequisites + IsEligible both pass for the CURRENT living representative right
    /// now. NotYet = neither — genuinely "not yet reachable," not a claim of permanent blockage,
    /// since an opaque IsEligible lambda can't be proven permanently false from the outside.
    public static TraitStatus GetTraitStatus(AgentController agent, GeneDefinition gene)
    {
        if (_presentedGlobally.Contains(gene.Id)) return TraitStatus.Fired;
        if (agent == null) return TraitStatus.NotYet;
        if (!PrerequisitesMet(agent, gene)) return TraitStatus.NotYet;
        if (gene.IsEligible != null && !gene.IsEligible(agent)) return TraitStatus.NotYet;
        return TraitStatus.Available;
    }

    /// Called by AtmosphereManager when a global threshold is crossed.
    public static void QueueAtmosphereEvent(string eventName) => _atmosphereEvents.Enqueue(eventName);

    /// Call once per agent per tick (cheap - catalog is small). Checks every registered
    /// gene this agent hasn't already acquired; fires the first newly-eligible one whose
    /// prerequisites are met. Marks the gene acquired immediately (even if a choice is
    /// pending) so it can't be queued twice.
    /// Discards any queued (but not-yet-shown) gene-choice popups — called when Era 3 begins so stale
    /// Era 1/Era 2 events don't pop up over the civilization layer (especially after a debug skip).
    public static void ClearPendingPopups() => _pending.Clear();

    public static void CheckEligibleGenes(AgentController agent, float elapsedSeconds)
    {
        // Era 1/Era 2 gene evolution is over once the civilization layer (Era 3) is active — its
        // decisions are the d3_ nodes in Era3HUD, not these popups. Stop all further gene queuing.
        if (Era3Manager.Instance != null && Era3Manager.Instance.IsActive) return;

        // elapsedSeconds = real time since this agent last ran this scan (it's throttled to the
        // agent's sense cadence, not called every frame). All per-second probabilistic gates below
        // multiply by it so their chance/sec is identical to the old per-frame Time.deltaTime path.
        float nowUnscaled = Time.unscaledTime;
        bool isPlayerCommunity = agent.communityId == PlayerCommunityId;

        // For the player's community: don't queue a new popup while one is already
        // waiting, or while the cooldown between player choices hasn't elapsed.
        // NPC communities have NO popup cooldown — their genes auto-apply freely
        // whenever IsEligible conditions are met.
        bool playerGated = isPlayerCommunity
            && (_pending.Count > 0
                || nowUnscaled - _playerLastGeneUnscaledTime < GeneCooldownSeconds);

        foreach (var gene in _catalog)
        {
            if (agent.AcquiredGenes.Contains(gene.Id)) continue;
            if (!PrerequisitesMet(agent, gene)) continue;
            if (gene.IsEligible != null && !gene.IsEligible(agent)) continue;

            // d3_* decisions are handled entirely by Era3HUD tabs — skip popup flow.
            if (gene.Id.StartsWith("d3_")) continue;

            // --- auto-only genes (no choices) fire immediately for everyone ---
            if (gene.Choices == null || gene.Choices.Length == 0)
            {
                // Stochastic gate: ~10% chance per second per eligible agent, spreading
                // population-wide mutations over ~15s instead of a single frame-sweep.
                if (Random.value > 0.10f * elapsedSeconds) continue;
                agent.AcquiredGenes.Add(gene.Id);
                gene.AutoApply?.Invoke(agent);
                continue; // keep scanning; auto genes don't consume the slot
            }

            // --- choice genes: player gets popup, NPCs get DefaultAutoApply ---
            // Player always gets a popup for genes they haven't seen, regardless of era.
            // (Era 1 events that fire in Era 2 are still worth presenting — the player
            // may have missed them entirely due to the old short auto-select timer.)
            if (isPlayerCommunity && !_presentedGlobally.Contains(gene.Id))
            {
                // Don't queue another popup yet if one is pending or cooldown active.
                if (playerGated) return;

                agent.AcquiredGenes.Add(gene.Id);
                _presentedGlobally.Add(gene.Id);
                _playerLastGeneUnscaledTime = nowUnscaled;
                _pending.Enqueue(new PendingChoice { Agent = agent, Gene = gene, ShowUnscaledTime = -1f });
                Debug.Log($"[EvoSim] GENE QUEUED: {gene.Id} community={agent.communityId} elapsed={SessionElapsed:F0}s");

                // One popup per call — let the player resolve this before the next fires.
                return;
            }
            else
            {
                // NPC or already-presented gene: stochastic gate before auto-applying.
                // ~10% chance per second per eligible agent so mutations spread over ~15–30s
                // rather than sweeping the entire population in a single frame.
                if (Random.value > 0.10f * elapsedSeconds) continue;
                agent.AcquiredGenes.Add(gene.Id);
                if (gene.DefaultAutoApply != null)
                    gene.DefaultAutoApply(agent);
                else
                    gene.Choices[gene.Choices.Length - 1].Apply(agent);
                // Continue loop — NPCs may acquire multiple genes in one call.
            }
        }
    }

    /// Per-reproduction mutation origination (gene-adoption spec §B). Called from
    /// AgentController.Reproduce() for each NEW offspring. For each gene with a non-zero
    /// BaseMutationProbability whose prerequisites and eligibility are met and which the child
    /// doesn't already carry, rolls that probability once; on success the trait ORIGINATES in this
    /// single offspring (via DefaultAutoApply) and thereafter spreads by inheritance + fitness.
    /// This is additive to CheckEligibleGenes — it only ever makes adoption more staggered and
    /// per-lineage, never prevents a gene from appearing. Player community is skipped (it gets the
    /// choice-popup path instead, so the player still decides its own lineage's traits).
    public static void RollReproductionMutations(AgentController child)
    {
        if (child == null) return;
        if (child.communityId == PlayerCommunityId) return; // player keeps the choice path

        foreach (var gene in _catalog)
        {
            if (gene.BaseMutationProbability <= 0f) continue;
            if (child.AcquiredGenes.Contains(gene.Id)) continue;
            if (!PrerequisitesMet(child, gene)) continue;
            if (gene.IsEligible != null && !gene.IsEligible(child)) continue;
            if (Random.value >= gene.BaseMutationProbability) continue;

            child.AcquiredGenes.Add(gene.Id);
            // Origination applies the NOVEL trait (the first available choice — the innovation),
            // not the conservative default: a mutation introduces the new capability into this one
            // offspring, which then spreads by inheritance + fitness.
            GeneChoice chosen = null;
            if (gene.Choices != null)
                foreach (var c in gene.Choices)
                    if ((c.IsAvailable == null || c.IsAvailable(child))
                        && (c.FitnessGate == null || c.FitnessGate(child))) { chosen = c; break; }
            if (chosen != null) chosen.Apply(child);
            else gene.AutoApply?.Invoke(child);
            Debug.Log($"[EvoSim] MUTATION_ORIGIN gene={gene.Id} community={child.communityId} agent={child.name} (per-reproduction origination).");
        }
    }

    /// DEBUG: force-apply every eligible outstanding Era 1 gene to this agent, choosing a RANDOM
    /// available option for choice genes — for ALL communities including the player, with no popups.
    /// Used by the "Skip to Era 2/3" test buttons to fast-forward Era 1 without hand-answering popups.
    // Genes whose IsEligible gate is ENVIRONMENT-SENSITIVE in a way that's dangerous to bypass:
    // metabolism/respiration switches that would put the organism on a gas/energy source the world
    // doesn't have (forcing these onto an absent substrate kills the agent — the mass-death bug we
    // fixed in ForceApplyOutstandingEra1Events). For these, the debug skip STILL honors IsEligible.
    private static readonly HashSet<string> _debugKeepEligibility = new HashSet<string>
    {
        "Methanogenesis", "SulfurRespiration", "NitrogenRespiration", "PhosphineRespiration",
        "HalideMetalRespiration", "AerobicRespiration", "EfficientRespiration",
        "PhotosynthesisEmergence", "Mixotrophy",
    };

    public static void DebugForceEra1GenesRandom(AgentController agent)
    {
        // Guarantee the foundational prerequisites (normally force-granted on a timer in
        // AgentController.Update) so the whole Era 1 gene chain can resolve immediately on a skip.
        agent.AcquiredGenes.Add("Nucleus");
        agent.AcquiredGenes.Add("Multicellularity");

        for (int pass = 0; pass < 4; pass++)
        {
            bool any = false;
            foreach (var gene in _catalog)
            {
                if (!gene.IsEra1Event) continue;
                if (agent.AcquiredGenes.Contains(gene.Id)) continue;
                if (!PrerequisitesMet(agent, gene)) continue;
                // Bypass IsEligible for DEVELOPMENTAL genes so the species fully develops (motility,
                // appendages, sociality, sensory, kingdom, …) regardless of the moment-in-time
                // environmental triggers — but keep it for metabolism/respiration (see set above).
                if (_debugKeepEligibility.Contains(gene.Id)
                    && gene.IsEligible != null && !gene.IsEligible(agent)) continue;

                any = true;
                agent.AcquiredGenes.Add(gene.Id);
                _presentedGlobally.Add(gene.Id);

                if (gene.Choices == null || gene.Choices.Length == 0)
                {
                    gene.AutoApply?.Invoke(agent);
                    continue;
                }
                var avail = new List<GeneChoice>();
                foreach (var c in gene.Choices)
                    if ((c.IsAvailable == null || c.IsAvailable(agent))
                        && (c.FitnessGate == null || c.FitnessGate(agent)))
                        avail.Add(c);
                if (avail.Count == 0) foreach (var c in gene.Choices) avail.Add(c);
                avail[Random.Range(0, avail.Count)].Apply(agent);
            }
            if (!any) break;
        }
    }

    /// Called by Era2Manager.BeginEra2() / EraManager to catch up any Era 1 genes that
    /// an agent's lineage never acquired. For NPC communities, applies silently with the
    /// default choice. For the player's community, queues choice popups instead so the
    /// player still gets to decide — even if Era 2 has already started.
    public static void ForceApplyOutstandingEra1Events(AgentController agent)
    {
        bool isPlayer = agent.communityId == PlayerCommunityId;

        // Multiple passes in case prerequisites unlock other prerequisites.
        for (int pass = 0; pass < 4; pass++)
        {
            bool anyApplied = false;
            foreach (var gene in _catalog)
            {
                if (!gene.IsEra1Event) continue;
                if (agent.AcquiredGenes.Contains(gene.Id)) continue;
                if (!PrerequisitesMet(agent, gene)) continue;
                // CRITICAL: honor IsEligible, exactly like CheckEligibleGenes does. This "catch-up"
                // sweep must only grant genes the agent was ELIGIBLE for but hadn't reached yet — NOT
                // genes whose environmental preconditions are false. Skipping this force-applied
                // substrate-gated respiration genes for gases the world doesn't have (e.g. O2 or
                // sulfur on a Titan-type N2/CH4/H2 world), switching every organism's breathed gas to
                // a 0%-abundance gas at Era 2 onset — CheckGasSurvival then drained the entire
                // population to zero reserve in seconds (mass EnergyDepletion at the Era 1→2 boundary).
                if (gene.IsEligible != null && !gene.IsEligible(agent)) continue;

                anyApplied = true;

                if (gene.Choices == null || gene.Choices.Length == 0)
                {
                    // No-choice gene: always auto-apply.
                    agent.AcquiredGenes.Add(gene.Id);
                    gene.AutoApply?.Invoke(agent);
                }
                else if (isPlayer && !_presentedGlobally.Contains(gene.Id))
                {
                    // Player hasn't seen this choice yet — queue a popup.
                    agent.AcquiredGenes.Add(gene.Id);
                    _presentedGlobally.Add(gene.Id);
                    _playerLastGeneUnscaledTime = Time.unscaledTime;
                    _pending.Enqueue(new PendingChoice { Agent = agent, Gene = gene, ShowUnscaledTime = -1f });
                    // Queue one at a time; the popup loop will gate the rest.
                    return;
                }
                else
                {
                    // NPC or already-presented: silent default.
                    agent.AcquiredGenes.Add(gene.Id);
                    if (gene.DefaultAutoApply != null)
                        gene.DefaultAutoApply(agent);
                    else
                        gene.Choices[gene.Choices.Length - 1].Apply(agent);
                }
            }
            if (!anyApplied) break;
        }
    }

    private static bool PrerequisitesMet(AgentController agent, GeneDefinition gene)
    {
        if (gene.Prerequisites == null) return true;
        foreach (var prereq in gene.Prerequisites)
        {
            if (!agent.AcquiredGenes.Contains(prereq)) return false;
        }
        return true;
    }

    /// Dry-run the full catalog for one player agent and write a per-gene verdict to the
    /// game log. Called by GameLog.WritePeriodicSnapshot so gene eligibility is visible
    /// alongside the population data every 30 seconds.
    public static void DiagnoseGenes(AgentController agent, System.IO.StreamWriter writer)
    {
        if (writer == null) return;
        // Player-community extinction is the single most common reason "no popups appear": with no
        // community-0 agent alive, CheckEligibleGenes never queues anything and no popup can show —
        // it's a dead-lineage problem, not a popup-forwarding bug. Log it explicitly so the two
        // causes are distinguishable at a glance rather than this method silently emitting nothing.
        if (agent == null)
        {
            writer.WriteLine($"[t={Time.time,8:F1}] GENES    PLAYER COMMUNITY (id={PlayerCommunityId}) EXTINCT — " +
                             $"no player agent alive; no gene popups possible this interval " +
                             $"(pending={_pending.Count}, presented={_presentedGlobally.Count})");
            return;
        }
        float nowUnscaled = Time.unscaledTime;
        bool cooldownActive = nowUnscaled - _playerLastGeneUnscaledTime < GeneCooldownSeconds;

        writer.WriteLine(
            $"[t={Time.time,8:F1}] GENES    catalog={_catalog.Count} " +
            $"pending={_pending.Count} presented={_presentedGlobally.Count} " +
            $"sessionElapsed={SessionElapsed:F0}s cooldown={cooldownActive} " +
            $"acquired=[{string.Join(",", agent.AcquiredGenes)}]");

        foreach (var gene in _catalog)
        {
            // Skip choice-less auto genes — they fire silently and aren't the popup issue
            if (gene.Choices == null || gene.Choices.Length == 0) continue;

            string verdict;
            if (agent.AcquiredGenes.Contains(gene.Id))
                verdict = "SKIP:already_acquired";
            else if (!PrerequisitesMet(agent, gene))
            {
                var missing = new System.Collections.Generic.List<string>();
                if (gene.Prerequisites != null)
                    foreach (var p in gene.Prerequisites)
                        if (!agent.AcquiredGenes.Contains(p)) missing.Add(p);
                verdict = $"SKIP:prereq_missing=[{string.Join(",", missing)}]";
            }
            else if (gene.IsEligible != null && !gene.IsEligible(agent))
                verdict = "SKIP:IsEligible=false";
            else if (_presentedGlobally.Contains(gene.Id))
                verdict = "SKIP:already_presented(auto-applied)";
            else if (_pending.Count > 0)
                verdict = "BLOCKED:popup_already_pending";
            else if (cooldownActive)
                verdict = $"BLOCKED:cooldown({(GeneCooldownSeconds - (nowUnscaled - _playerLastGeneUnscaledTime)):F1}s left)";
            else
                verdict = ">>> ELIGIBLE_SHOULD_QUEUE <<<";

            writer.WriteLine($"[t={Time.time,8:F1}] GENE     {gene.Id,-40} {verdict}");
        }
    }

    // ── Icon cache ────────────────────────────────────────────────────────────
    private static readonly Dictionary<string, Texture2D> _iconCache
        = new Dictionary<string, Texture2D>();

    private static Texture2D GetIcon(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_iconCache.TryGetValue(name, out var tex) && tex != null) return tex;
        tex = Resources.Load<Texture2D>($"Icons/{name}");
        if (tex != null) _iconCache[name] = tex;
        return tex;
    }

    // Inline "Learn more" expand state.
    private bool _learnMoreExpanded;
    private string _learnMoreGeneId; // which gene's learn-more is open

    // ── GUIStyles (lazily built once) ─────────────────────────────────────────
    private GUIStyle _cardBg, _titleStyle, _dilemmaStyle, _btnStyle,
                     _learnMoreStyle, _countdownStyle, _lmBtnStyle, _previewStyle;

    // Pre-baked textures so MakeColorTex is called once, not per-frame.
    private Texture2D _btnNormalTex, _btnHoverTex, _btnHoverTexAlt;
    private Texture2D _tierGreenTex, _tierGreenHtex, _tierAmberTex, _tierAmberHtex, _tierRedTex, _tierRedHtex;

    /// Picks the favorability-tier button background for a choice. `goodness` is 0..1 (1 = most
    /// favorable under current local conditions) from GeneEventPreview.GetFavorability; `hovered`
    /// selects the brightened variant. Thresholds mirror the preview's own tier cutoffs.
    private Texture2D TierTex(float goodness, bool hovered) =>
        goodness >= 0.66f ? (hovered ? _tierGreenHtex : _tierGreenTex)
      : goodness >= 0.34f ? (hovered ? _tierAmberHtex : _tierAmberTex)
      :                     (hovered ? _tierRedHtex   : _tierRedTex);

    private void EnsureStyles()
    {
        if (_cardBg != null) return;

        _cardBg = new GUIStyle(GUI.skin.box)
        {
            normal  = { background = MakeColorTex(new Color(0.08f, 0.08f, 0.12f, 0.95f)) },
            border  = new RectOffset(6, 6, 6, 6),
            padding = new RectOffset(0, 0, 0, 0),
        };

        _titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 15,
            fontStyle = UnityEngine.FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
        };
        _titleStyle.normal.textColor = new Color(1f, 0.85f, 0.4f); // warm gold

        _dilemmaStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 12,
            wordWrap  = true,
            alignment = TextAnchor.MiddleLeft,
        };
        _dilemmaStyle.normal.textColor = new Color(0.88f, 0.88f, 0.88f);

        _btnNormalTex  = MakeColorTex(new Color(0.18f, 0.22f, 0.32f, 1f));
        _btnHoverTex   = MakeColorTex(new Color(0.28f, 0.38f, 0.60f, 1f));
        _btnHoverTexAlt = MakeColorTex(new Color(0.22f, 0.28f, 0.44f, 1f));

        // Favorability-tier button backgrounds. Each choice's whole button is colored by how
        // energetically favorable it is UNDER CURRENT LOCAL CONDITIONS (from GeneEventPreview) —
        // green = favorable now, amber = marginal, red = unfavorable now. Hover = brightened variant.
        // These are a "right here, right now" fitness read, NOT a long-term strategic verdict.
        _tierGreenTex  = MakeColorTex(new Color(0.16f, 0.42f, 0.20f, 1f));
        _tierGreenHtex = MakeColorTex(new Color(0.24f, 0.58f, 0.30f, 1f));
        _tierAmberTex  = MakeColorTex(new Color(0.46f, 0.36f, 0.10f, 1f));
        _tierAmberHtex = MakeColorTex(new Color(0.62f, 0.48f, 0.14f, 1f));
        _tierRedTex    = MakeColorTex(new Color(0.44f, 0.16f, 0.14f, 1f));
        _tierRedHtex   = MakeColorTex(new Color(0.60f, 0.22f, 0.19f, 1f));

        _btnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize  = 12,
            fontStyle = UnityEngine.FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            padding   = new RectOffset(8, 8, 0, 0),
        };
        _btnStyle.normal.background  = _btnNormalTex;
        _btnStyle.hover.background   = _btnHoverTex;
        _btnStyle.normal.textColor   = Color.white;
        _btnStyle.hover.textColor    = Color.white;
        _btnStyle.richText           = true;

        _learnMoreStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            wordWrap = true,
        };
        _learnMoreStyle.normal.textColor = new Color(0.72f, 0.72f, 0.72f);

        _lmBtnStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize  = 11,
            alignment = TextAnchor.MiddleLeft,
        };
        _lmBtnStyle.normal.textColor = new Color(0.5f, 0.75f, 1f);
        _lmBtnStyle.hover.textColor  = new Color(0.7f, 0.9f, 1f);
        _lmBtnStyle.normal.background = MakeColorTex(new Color(0f, 0f, 0f, 0f));
        _lmBtnStyle.hover.background  = MakeColorTex(new Color(0f, 0f, 0f, 0f));

        _countdownStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 11,
            alignment = TextAnchor.MiddleRight,
        };
        _countdownStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);

        _previewStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            wordWrap = false,
            fontStyle = FontStyle.Italic,
        };
        // Light neutral so the preview subtitle stays legible on any tier color (green/amber/red).
        _previewStyle.normal.textColor = new Color(0.92f, 0.94f, 0.92f);
    }

    private static Texture2D MakeColorTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    void OnGUI()
    {
        EnsureStyles();

        // ── Debug: always-visible gene queue status ───────────────────────────
        // Remove once popup delivery is confirmed working.
        {
            string dbg = _pending.Count > 0
                ? $"[GENE POPUP QUEUED: {_pending.Count}]"
                : $"[genes: 0 pending | elapsed {SessionElapsed:F0}s | catalog {_catalog.Count}]";
            GUIStyle dbgStyle = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            dbgStyle.normal.textColor = _pending.Count > 0
                ? new Color(1f, 0.4f, 0.1f)
                : new Color(0.55f, 0.55f, 0.55f);
            GUI.Label(new Rect(4, 60, 420, 18), dbg, dbgStyle);
        }

        // ── Atmosphere crisis banner (no gene pending) ────────────────────────
        if (_atmosphereEvents.Count > 0 && _pending.Count == 0)
        {
            string evtName = _atmosphereEvents.Peek();
            string msg = evtName switch
            {
                "GreatGasEvent"     => "GREAT GAS EVENT — The breathed gas has collapsed!\nConsumers are dying en masse. Survivors face a critical metabolic choice.",
                "ExpelledGlutEvent" => "RESPIRATORY WASTE GLUT — Exhaled gas is crowding out breathable gas.",
                "ToxicityEvent"     => "TOXICITY CRISIS — A poisonous gas has reached lethal concentration!",
                _                   => evtName
            };

            float w = 460f, h = 84f;
            Rect r = new Rect((Screen.width - w) / 2f, Screen.height * 0.15f, w, h);
            GUI.Box(r, "", _cardBg);

            // Crisis icon
            Texture2D alertTex = GetIcon("alert-triangle");
            if (alertTex != null)
                GUI.DrawTexture(new Rect(r.x + 12, r.y + 12, 24, 24), alertTex, ScaleMode.ScaleToFit);

            GUI.Label(new Rect(r.x + 44, r.y + 8, w - 60, 48), msg, _dilemmaStyle);
            if (GUI.Button(new Rect(r.x + w - 72, r.y + h - 32, 60, 24), "OK", _btnStyle))
                _atmosphereEvents.Dequeue();
            return;
        }

        if (_pending.Count == 0) return;

        // ── Stamp show time on first render ───────────────────────────────────
        var peeked = _pending.Peek();
        if (peeked.Agent == null)
        {
            // Triggering agent died — try to hand off to another living community 0 member
            // rather than silently dropping the event.
            AgentController replacement = null;
            if (_agentSpawner != null)
                foreach (var a in _agentSpawner.ActiveAgents)
                    if (a != null && a.communityId == PlayerCommunityId) { replacement = a; break; }
            if (replacement == null) { _pending.Dequeue(); return; } // community extinct
            var reassigned = peeked;
            reassigned.Agent = replacement;
            _pending.Dequeue();
            _pending.Enqueue(reassigned);
            peeked = _pending.Peek();
        }
        if (peeked.ShowUnscaledTime < 0f)
        {
            var stamped = peeked;
            stamped.ShowUnscaledTime = Time.unscaledTime;
            _pending.Dequeue();
            _pending.Enqueue(stamped);
            peeked = _pending.Peek();
        }

        PendingChoice pending = peeked;

        // ── Auto-select after timeout ─────────────────────────────────────────
        float elapsed  = Time.unscaledTime - pending.ShowUnscaledTime;
        if (elapsed >= AutoSelectSeconds)
        {
            AutoSelectBestChoice(pending);
            _pending.Dequeue();
            _learnMoreExpanded = false;
            return;
        }

        int secsLeft = Mathf.CeilToInt(AutoSelectSeconds - elapsed);

        // ── Collect visible choices ───────────────────────────────────────────
        var visible = new List<(GeneChoice choice, int originalIdx)>();
        for (int i = 0; i < pending.Gene.Choices.Length; i++)
        {
            var c = pending.Gene.Choices[i];
            if (c.IsAvailable == null || c.IsAvailable(pending.Agent))
                visible.Add((c, i));
        }

        // ── Look up rich UI data ──────────────────────────────────────────────
        bool hasUI = GeneEventUIData.TryGet(pending.Gene.Id, out var ui);

        string title   = hasUI ? ui.Title   : pending.Gene.Id;
        string dilemma = hasUI ? ui.Dilemma : "Choose a direction:";
        bool   hasLM   = hasUI && !string.IsNullOrEmpty(ui.LearnMore);

        // ── Outcome previews + per-choice favorability (for button color) ────
        string[] previews = GeneEventPreview.Get(pending.Gene.Id, pending.Agent);
        float[]  favor    = GeneEventPreview.GetFavorability(pending.Gene.Id, pending.Agent);

        // ── Layout measurements ───────────────────────────────────────────────
        const float W        = 380f;
        const float PAD      = 14f;
        float BTN_H          = previews != null ? 50f : 36f;
        const float BTN_GAP  = 6f;
        const float ICON_SZ  = 20f;
        const float HEADER_H = 52f;  // topic icon + title + dilemma
        const float LM_BTN_H = 22f;
        const float LM_TXT_H = 48f;

        float btnBlock = visible.Count * (BTN_H + BTN_GAP);
        float lmBlock  = hasLM ? LM_BTN_H + (_learnMoreExpanded && _learnMoreGeneId == pending.Gene.Id ? LM_TXT_H + 4f : 0f) : 0f;
        float H        = PAD + HEADER_H + PAD + btnBlock + PAD + lmBlock + PAD;

        Rect card = new Rect((Screen.width - W) / 2f, Screen.height * 0.22f, W, H);
        GUI.Box(card, "", _cardBg);

        float cx = card.x + PAD;
        float cy = card.y + PAD;

        // ── Topic icon + title ────────────────────────────────────────────────
        if (hasUI)
        {
            Texture2D topicTex = GetIcon(ui.TopicIcon);
            if (topicTex != null)
                GUI.DrawTexture(new Rect(cx, cy + 1, 20, 20), topicTex, ScaleMode.ScaleToFit);
        }
        GUI.Label(new Rect(cx + 26, cy, W - PAD * 2 - 26 - 60, 22), title, _titleStyle);

        // Countdown (top-right)
        GUI.Label(new Rect(card.x + W - 68, cy, 60, 22), $"auto {secsLeft}s", _countdownStyle);

        // ── Dilemma line ──────────────────────────────────────────────────────
        GUI.Label(new Rect(cx, cy + 24, W - PAD * 2, 24), dilemma, _dilemmaStyle);

        float btnY = cy + HEADER_H + PAD;
        var keyboard = Keyboard.current;
        bool resolved = false;

        for (int s = 0; s < visible.Count && !resolved; s++)
        {
            var (choice, origIdx) = visible[s];
            Rect btnRect = new Rect(cx, btnY + s * (BTN_H + BTN_GAP), W - PAD * 2, BTN_H);

            // Draw button background manually so we can overlay the icon. When a favorability read
            // exists for this choice, color the whole button by its tier (green/amber/red); otherwise
            // fall back to the neutral blue background.
            bool hovered = btnRect.Contains(Event.current.mousePosition);
            Texture2D bgTex = (favor != null && origIdx < favor.Length)
                ? TierTex(favor[origIdx], hovered)
                : (hovered ? _btnHoverTex : _btnNormalTex);
            GUI.DrawTexture(btnRect, bgTex);

            // Choice icon
            string iconName = (hasUI && s < ui.Choices.Length) ? ui.Choices[s].Icon : null;
            Texture2D choiceTex = GetIcon(iconName);
            if (choiceTex != null)
                GUI.DrawTexture(new Rect(btnRect.x + 8, btnRect.y + (BTN_H - ICON_SZ) / 2f, ICON_SZ, ICON_SZ), choiceTex, ScaleMode.ScaleToFit);

            // Choice label + terse parenthetical hint
            string shortLabel = (hasUI && s < ui.Choices.Length) ? ui.Choices[s].Label : null;
            string hint       = (hasUI && s < ui.Choices.Length) ? ui.Choices[s].Hint  : null;
            string baseText   = string.IsNullOrEmpty(shortLabel) ? $"{s + 1}: {choice.Label}" : $"{s + 1}: {shortLabel}";
            string btnText    = string.IsNullOrEmpty(hint)
                ? baseText
                : $"{baseText}  <color=#888888><size=10>({hint})</size></color>";
            GUI.Label(new Rect(btnRect.x + 34, btnRect.y, btnRect.width - 40, BTN_H), btnText, _btnStyle);

            if (previews != null && origIdx < previews.Length && !string.IsNullOrEmpty(previews[origIdx]))
                GUI.Label(new Rect(btnRect.x + 34, btnRect.y + 28f, btnRect.width - 40, 16f), previews[origIdx], _previewStyle);

            bool clicked    = GUI.Button(btnRect, "", GUIStyle.none);
            bool keyPressed = false;
            if (keyboard != null && s < 9)
            {
                var key = keyboard[(Key)((int)Key.Digit1 + s)];
                if (key != null && key.wasPressedThisFrame) keyPressed = true;
            }

            if (clicked || keyPressed)
            {
                choice.Apply(pending.Agent);
                BroadcastChoiceToPlayerCommunity(pending.Gene, choice, pending.Agent);
                _pending.Dequeue();
                _learnMoreExpanded = false;
                resolved = true;
            }
        }

        if (resolved) return;

        // ── Learn more inline expand ───────────────────────────────────────────
        if (hasLM)
        {
            float lmY = btnY + visible.Count * (BTN_H + BTN_GAP) + PAD;
            bool expanded = _learnMoreExpanded && _learnMoreGeneId == pending.Gene.Id;
            string lmLabel = expanded ? "▲ Less" : "▼ Learn more";
            if (GUI.Button(new Rect(cx, lmY, 100, LM_BTN_H), lmLabel, _lmBtnStyle))
            {
                _learnMoreExpanded = !expanded;
                _learnMoreGeneId   = pending.Gene.Id;
            }

            if (expanded)
                GUI.Label(new Rect(cx, lmY + LM_BTN_H + 4, W - PAD * 2, LM_TXT_H), ui.LearnMore, _learnMoreStyle);
        }
    }

    /// Applies the player's gene choice to every agent in the player community that has
    /// already been marked as having acquired the gene (i.e. they were eligible but the
    /// presentation was suppressed because only the first agent gets the popup).
    private static void BroadcastChoiceToPlayerCommunity(GeneDefinition gene, GeneChoice choice,
        AgentController alreadyApplied)
    {
        if (_agentSpawner == null) return;
        foreach (var agent in _agentSpawner.ActiveAgents)
        {
            if (agent == null) continue;
            if (agent == alreadyApplied) continue;
            if (agent.communityId != PlayerCommunityId) continue;
            if (!agent.AcquiredGenes.Contains(gene.Id)) continue;
            choice.Apply(agent);
        }
    }

    /// Same propagation as BroadcastChoiceToPlayerCommunity, but for the autonomous timeout path:
    /// applies the gene's fitness-aware DefaultAutoApply to the rest of the player community so an
    /// unanswered popup resolves the whole lineage consistently (mirroring how NPC lineages all share
    /// the DefaultAutoApply outcome), rather than leaving community members split.
    /// Resolves an unanswered player popup by picking the SAME choice the player would see flagged
    /// green — the highest-favorability available option per GeneEventPreview (the exact signal that
    /// colors the buttons). An idle/observing player's species therefore auto-evolves along the
    /// optimal-under-current-conditions path, never the yellow/red one. Falls back to the gene's
    /// fitness-aware DefaultAutoApply (then the conservative last branch) only when a gene has no
    /// favorability preview defined, so nothing regresses to a random pick.
    private static void AutoSelectBestChoice(PendingChoice pending)
    {
        GeneDefinition gene = pending.Gene;
        AgentController agent = pending.Agent;

        // Candidate = visible (IsAvailable) AND not flagged as a conditional-loser (FitnessGate).
        int bestIdx = -1;
        float bestFav = float.NegativeInfinity;
        float[] favor = GeneEventPreview.GetFavorability(gene.Id, agent);
        if (favor != null)
        {
            for (int i = 0; i < gene.Choices.Length && i < favor.Length; i++)
            {
                var c = gene.Choices[i];
                if (c.IsAvailable != null && !c.IsAvailable(agent)) continue;
                if (c.FitnessGate != null && !c.FitnessGate(agent)) continue;
                if (favor[i] > bestFav) { bestFav = favor[i]; bestIdx = i; }
            }
        }

        if (bestIdx >= 0)
        {
            gene.Choices[bestIdx].Apply(agent);
            BroadcastChoiceToPlayerCommunity(gene, gene.Choices[bestIdx], agent);
            return;
        }

        // No favorability signal for this gene — use the fitness-aware default.
        if (gene.DefaultAutoApply != null)
        {
            gene.DefaultAutoApply(agent);
            BroadcastDefaultToPlayerCommunity(gene, agent);
            return;
        }

        // Last resort: conservative last available branch (per GeneDefinition.DefaultAutoApply doc).
        GeneChoice fallback = null;
        foreach (var c in gene.Choices)
            if ((c.IsAvailable == null || c.IsAvailable(agent))
                && (c.FitnessGate == null || c.FitnessGate(agent)))
                fallback = c;
        if (fallback != null)
        {
            fallback.Apply(agent);
            BroadcastChoiceToPlayerCommunity(gene, fallback, agent);
        }
    }

    private static void BroadcastDefaultToPlayerCommunity(GeneDefinition gene, AgentController alreadyApplied)
    {
        if (_agentSpawner == null || gene.DefaultAutoApply == null) return;
        foreach (var agent in _agentSpawner.ActiveAgents)
        {
            if (agent == null) continue;
            if (agent == alreadyApplied) continue;
            if (agent.communityId != PlayerCommunityId) continue;
            if (!agent.AcquiredGenes.Contains(gene.Id)) continue;
            gene.DefaultAutoApply(agent);
        }
    }
}
