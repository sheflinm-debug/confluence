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

    // Seconds before an unanswered player choice is auto-resolved with a random pick.
    private const float AutoSelectSeconds = 10f;

    private struct PendingChoice
    {
        public AgentController Agent;
        public GeneDefinition Gene;
        public float ShowUnscaledTime; // set when the popup first appears
    }

    public static void ResetCatalog()
    {
        _catalog.Clear();
        _pending.Clear();
        _presentedGlobally.Clear();
        _atmosphereEvents.Clear();
        _playerLastGeneUnscaledTime = float.NegativeInfinity;
    }

    public static void Register(GeneDefinition gene) => _catalog.Add(gene);

    /// Called by AtmosphereManager when a global threshold is crossed.
    public static void QueueAtmosphereEvent(string eventName) => _atmosphereEvents.Enqueue(eventName);

    /// Call once per agent per tick (cheap - catalog is small). Checks every registered
    /// gene this agent hasn't already acquired; fires the first newly-eligible one whose
    /// prerequisites are met. Marks the gene acquired immediately (even if a choice is
    /// pending) so it can't be queued twice.
    public static void CheckEligibleGenes(AgentController agent)
    {
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
                agent.AcquiredGenes.Add(gene.Id);
                gene.AutoApply?.Invoke(agent);
                continue; // keep scanning; auto genes don't consume the slot
            }

            // --- choice genes: player gets popup, NPCs get DefaultAutoApply ---
            // Era 1 events that slipped past the era gate (e.g. player lineage missed them)
            // are silently auto-applied in Era 2 rather than flooding the player with stale prompts.
            bool suppressPlayerPopup = gene.IsEra1Event
                && Era2Manager.Instance != null && Era2Manager.Instance.IsActive;

            if (isPlayerCommunity && !_presentedGlobally.Contains(gene.Id) && !suppressPlayerPopup)
            {
                // Don't queue another popup yet if one is pending or cooldown active.
                if (playerGated) return;

                agent.AcquiredGenes.Add(gene.Id);
                _presentedGlobally.Add(gene.Id);
                _playerLastGeneUnscaledTime = nowUnscaled;
                _pending.Enqueue(new PendingChoice { Agent = agent, Gene = gene, ShowUnscaledTime = -1f });

                // One popup per call — let the player resolve this before the next fires.
                return;
            }
            else
            {
                // NPC or already-presented gene: auto-apply and keep scanning.
                agent.AcquiredGenes.Add(gene.Id);
                if (gene.DefaultAutoApply != null)
                    gene.DefaultAutoApply(agent);
                else
                    gene.Choices[gene.Choices.Length - 1].Apply(agent);
                // Continue loop — NPCs may acquire multiple genes in one call.
            }
        }
    }

    /// Called by Era2Manager.BeginEra2() to silently auto-apply any Era 1 genes that
    /// an agent's lineage never acquired. Skips IsEligible checks (era gate would block),
    /// honours prerequisite order by iterating the catalog in registration order.
    public static void ForceApplyOutstandingEra1Events(AgentController agent)
    {
        // Multiple passes in case prerequisites unlock other prerequisites.
        for (int pass = 0; pass < 4; pass++)
        {
            bool anyApplied = false;
            foreach (var gene in _catalog)
            {
                if (!gene.IsEra1Event) continue;
                if (agent.AcquiredGenes.Contains(gene.Id)) continue;
                if (!PrerequisitesMet(agent, gene)) continue;

                agent.AcquiredGenes.Add(gene.Id);
                anyApplied = true;

                if (gene.Choices == null || gene.Choices.Length == 0)
                    gene.AutoApply?.Invoke(agent);
                else if (gene.DefaultAutoApply != null)
                    gene.DefaultAutoApply(agent);
                else
                    gene.Choices[gene.Choices.Length - 1].Apply(agent);
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
                     _learnMoreStyle, _countdownStyle, _lmBtnStyle;

    // Pre-baked textures so MakeColorTex is called once, not per-frame.
    private Texture2D _btnNormalTex, _btnHoverTex, _btnHoverTexAlt;

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
        if (peeked.Agent == null) { _pending.Dequeue(); return; }
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
            var avail = new List<GeneChoice>();
            foreach (var c in pending.Gene.Choices)
                if (c.IsAvailable == null || c.IsAvailable(pending.Agent))
                    avail.Add(c);
            if (avail.Count > 0)
            {
                GeneChoice auto = avail[Random.Range(0, avail.Count)];
                auto.Apply(pending.Agent);
                BroadcastChoiceToPlayerCommunity(pending.Gene, auto, pending.Agent);
            }
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

        // ── Layout measurements ───────────────────────────────────────────────
        const float W        = 380f;
        const float PAD      = 14f;
        const float BTN_H    = 36f;
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
            var (choice, _) = visible[s];
            Rect btnRect = new Rect(cx, btnY + s * (BTN_H + BTN_GAP), W - PAD * 2, BTN_H);

            // Draw button background manually so we can overlay the icon
            bool hovered = btnRect.Contains(Event.current.mousePosition);
            GUI.DrawTexture(btnRect, hovered ? _btnHoverTex : _btnNormalTex);

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
}
