using System;
using System.Collections.Generic;
using UnityEngine;

/// Era 3 HUD — Five-channel tabbed control panel (addendum §3, §4).
///
/// Tabs: Economic | Genetic/Bio | Informational | Existential | Coercive
///
/// Each tab has architecture-differentiated content:
///   Dials  — always-visible sliders, gated by sub-track values
///   Cards  — discrete decision prompts, appear when eligible
///
/// Sub-track gating follows §4 content matrix exactly:
///   Individuated A1/A2/A3 sub-tracks gate dial rows and card types.
///   Distributed  NetworkConnectivityTier / SignalBandwidthTier gate partner slots / disinfo cards.
///   Collective   CasteDifferentiation / ReproductiveMode / DecisionVelocity gate caste rows / crisis cards.
///
/// Representative framing (addendum §3.3) on Coercive + Existential tabs for
/// non-Individuated civs — the avatar is the NPC's Representative, not the civ.
public class Era3HUD : MonoBehaviour
{
    // ── Layout constants ───────────────────────────────────────────────────────
    private const float PanelW  = 460f; // narrowed per user request — was 560f (widened for 9 tabs; see BuildStyles' tab-font comment)
    private const float PanelH  = 560f;
    private const float TabH    = 26f;
    private const float PadX    = 12f;
    private const float PadY    = 8f;
    private const float Row     = 21f;
    private const float SliderW = 190f;
    private const float LabelW  = 160f;
    private const float BarH    = 7f;

    // ── State ──────────────────────────────────────────────────────────────────
    private bool  _open      = false;
    private int   _activeTab = 0;
    private int   _selectedDiplomacyCivId = -1; // -1 = showing the Relations list; else the open per-civ diplomacy screen
    private readonly HashSet<string> _dismissed = new HashSet<string>();
    private OrbitCamera _orbitCam; // lazily resolved on first settlement-row click
    private Vector2 _scrollPos;
    private readonly float[] _tabContentHeight = new float[11]; // one per tab (5 main + Polity + Tech/Ideas + Eco Policy + PolCat + Settlements + Home)

    // Mirrors GameHUD's IsOpenAndContains/IsScrollBlockedAtScreenPos pattern — without this,
    // OrbitCamera's scroll-wheel zoom only checked GameHUD's panel rect, so scrolling this panel's
    // list (e.g. a long Settlements tab) also zoomed the camera out underneath it at the same time.
    private static Rect _lastPanelRect;
    private static bool _lastOpen;
    public static bool IsScrollBlockedAtScreenPos(Vector2 screenPos)
    {
        Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
        return _lastOpen && _lastPanelRect.Contains(guiPos);
    }

    // ── Styles ─────────────────────────────────────────────────────────────────
    private GUIStyle _panel, _tabOn, _tabOff, _hdr, _sub, _lbl, _dim,
                     _card, _choiceBtn, _repStyle, _crisisCard, _dimBtn, _nodeLabel;
    private bool _stylesReady;

    // Every civ gets every tab — the channel-investment dials (InvestEconomic/Biological/
    // Information/Religion/Coercive), StructureInvest, and war domains are mechanically live for
    // ALL seven tracks regardless of path (Era3TechTree's acquisition engine reads these same dials
    // for ecological-path civs too — Tech applies to every track per era3-tech-idea-trees-spec §0),
    // so hiding those tabs for non-CommerceEngine civs previously cut off real control surface, not
    // just cosmetic ones. An earlier pass over-applied the ecological-paths-spec's "mediation
    // spectrum" (§1: no Cards/treaties/Representative for the three ecological paths) to mean "no
    // tabs at all" for those civs, collapsing an 8-tab panel down to 2 without flagging it clearly —
    // that was a real regression, fixed here: the spectrum still holds (see DrawPendingCardPopup's
    // CommerceEngine-only gate and Eco Policy's placeholder below for non-eco civs), it just no
    // longer removes access to dials that are still doing real work under the hood.
    private static readonly string[] TabNames =
        { "Econ", "Bio", "Info", "Exist", "Coerc", "Polity", "Tech", "EcoPol", "PolCat", "Towns", "Home" };
    // Home (index 10) appended at the end rather than renumbering the whole file — every existing
    // Card's `Tab = N` field and every DrawCards(N, ...) call site is a raw integer literal keyed to
    // these positions; inserting Home at the front would mean finding and incrementing every one of
    // those across the file, a large mechanical change with real risk of silently misfiling a card
    // under the wrong tab. Appending keeps every existing index stable. Reposition later if wanted.

    // ── Card definition ────────────────────────────────────────────────────────
    private struct Card
    {
        public string   Id;
        public int      Tab;
        public bool     IsCrisis;             // crisis cards render with red accent
        public string   Title;
        public string   Dilemma;
        public string[] ChoiceLabels;
        public string[] ChoiceHints;
        public Func<CivilizationState, bool>  IsEligible;
        public Action<Era3Manager, int>       Apply;
        // era3-track-parity-gating-spec §0: "gate the upgrade, not the floor" — per-choice
        // availability, so a card's regressive/status-quo option can stay always-available while
        // its sophisticated option(s) pick up a Tech/Idea/Adaptation prerequisite. Null = every
        // choice always available (all pre-existing cards keep exact prior behavior unchanged).
        public Func<CivilizationState, int, bool> ChoiceGate;
    }

    private static bool ChoiceAvailable(Card card, CivilizationState civ, int i)
        => card.ChoiceGate == null || card.ChoiceGate(civ, i);

    private List<Card> _cards;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    void Start() => BuildCards();

    void OnGUI()
    {
        if (!_stylesReady) BuildStyles();
        if (Era3Manager.Instance == null || !Era3Manager.Instance.IsActive) return;

        var mgr = Era3Manager.Instance;
        var civ = mgr.PlayerCiv;

        // Screen-center popup + fading event banner render REGARDLESS of whether the Civilization
        // panel is open — previously a new decision could only ever be seen/resolved by having the
        // panel open on exactly the right tab, and auto-events surfaced only as a line in a scrolling
        // log the player might never open. Both now behave like Era 1/2's gene-choice popups: hard to
        // miss, resolvable right where they appear.
        DrawPendingCardPopup(civ, mgr);
        DrawEventFlashBanner(mgr);

        float btnX = 8f; // left side, so it doesn't overlap the species/ranking panel on the right
        if (GUI.Button(new Rect(btnX, 8f, 132f, 24f),
            _open ? "▼ Civilization" : "▲ Civilization"))
            _open = !_open;

        _lastOpen = _open;
        if (!_open) { _lastPanelRect = new Rect(btnX, 8f, 132f, 24f); return; }

        float px = 8f;
        float py = 38f;
        _lastPanelRect = new Rect(px, py, PanelW, PanelH);
        GUI.Box(new Rect(px, py, PanelW, PanelH), GUIContent.none, _panel);

        // Tab bar.
        float tw = PanelW / TabNames.Length;
        for (int i = 0; i < TabNames.Length; i++)
        {
            if (GUI.Button(new Rect(px + i * tw, py, tw, TabH),
                    TabNames[i], i == _activeTab ? _tabOn : _tabOff))
            {
                if (_activeTab != i) _scrollPos = Vector2.zero; // fresh tab starts scrolled to top
                _activeTab = i;
            }
        }

        // Content area: SCROLLABLE, not just clipped — a growing settlement list or a busy
        // Coercive/Existential tab can easily exceed the fixed panel height, and a plain
        // GUI.BeginGroup silently clips anything past it with no way to reach it at all.
        // IMGUI needs the content height up front, which we don't know until after drawing, so this
        // uses the standard one-frame-lag trick: size the virtual content rect from LAST frame's
        // measured height (per tab, so switching tabs doesn't reuse a mismatched height), then
        // remeasure after drawing for next frame. Stabilizes immediately in practice.
        float cx = px + PadX;
        float cy = py + TabH + PadY;
        float cw = PanelW - PadX * 2f;
        float ch = PanelH - TabH - PadY * 2f;
        const float ScrollbarAllowance = 18f;
        float contentW = cw - ScrollbarAllowance;
        Rect viewRect = new Rect(cx, cy, cw, ch);
        Rect contentRect = new Rect(0f, 0f, contentW, Mathf.Max(ch, _tabContentHeight[_activeTab]));

        _scrollPos = GUI.BeginScrollView(viewRect, _scrollPos, contentRect);
        float y = 0f;
        DrawTab(_activeTab, civ, mgr, contentW, ref y);
        DrawEventLog(mgr, contentW, ref y);
        _tabContentHeight[_activeTab] = y;
        GUI.EndScrollView();
    }

    // ── Ecological paths (no-mediation tier: dials only, no Cards) ─────────────────────────────
    private void DrawEcologicalPolicy(CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        Header($"{civ.Name} — {civ.Path}", w, ref y);
        Readout("Mediation", civ.Path == Era3Path.LivingReef
            ? "Light — no treaties by default" : "None — resolved automatically each tick, no negotiation", w, ref y);

        // Identification block — this path has no Cards/tabs to inspect elsewhere (deliberate, per
        // the mediation spectrum: a solitary Bloom Front has no treaty layer to build), so this is
        // the only place to see what the species/civ actually IS beyond its name.
        int settlementCount = 0; float pop = 0f;
        foreach (var s in mgr.Settlements)
            if (s.OwnerCivId == civ.CommunityId) { settlementCount++; pop += s.Population; }
        Readout("Population",  $"{pop:F0}  across {settlementCount} settlement(s)", w, ref y);
        Readout("Resilience",  $"{civ.Resilience:P0}{(civ.HasCollapsed ? "  — COLLAPSED" : "")}", w, ref y);
        Readout("Kingdom",     $"{civ.FounderKingdom}  ({civ.FounderMetabolism})", w, ref y);
        Readout("Backbone",    civ.FounderBackbone, w, ref y);
        var rec = Era2Manager.Instance != null ? Era2Manager.Instance.GetRecord(civ.CommunityId) : null;
        if (rec != null) Readout("Intel. Index", $"{rec.II:F2}", w, ref y);

        // era3-adaptation-trees-spec §2.4 — the exact gating table, resolved as lock-checks per
        // (path, option index) rather than a second parallel data structure, since these three rows
        // are read directly off Era3EcologicalPaths' existing catalogs.
        string ConflictGate(int i) => civ.Path switch
        {
            Era3Path.LivingReef  => i == 1 ? "A2c" : null,                    // Chemical Defense
            Era3Path.Terraformer => i == 0 ? "A3b" : i == 1 ? "A2c" : null,    // Biochemical Warfare / Niche Hoarding
            Era3Path.BloomFront  => i == 0 ? "A2c" : i == 1 ? "A3b" : null,    // Shade-Out / Toxic Bloom
            _ => null,
        };
        string OrgGate(int i) => civ.Path switch
        {
            Era3Path.LivingReef   => i == 0 ? "A2a" : i == 2 ? "A4a" : null,   // Polymorphic Castes / Sacrificial Specialists
            Era3Path.Terraformer  => i == 1 ? "A4b" : null,                    // Planetary Engineering
            Era3Path.BloomFront   => i == 0 ? "A1a" : i == 1 ? "A3a" : null,   // Wide Scatter / Concentrated Fronts
            Era3Path.ApexPredator => (i == 0 || i == 1) ? "A1a" : null,        // Nomadic Hunting / Fixed Territory
            _ => null,
        };
        DrawOptionRow("Resource Policy",  Era3EcologicalPaths.ResourcePolicy,  civ.Path, ref civ.EcoResourcePolicy,  w, ref y, civ);
        DrawOptionRow("Conflict Posture", Era3EcologicalPaths.ConflictPosture, civ.Path, ref civ.EcoConflictPosture, w, ref y, civ, ConflictGate);
        DrawOptionRow("Organization",     Era3EcologicalPaths.Organization,    civ.Path, ref civ.EcoOrganization,    w, ref y, civ, OrgGate);

        if (civ.Path == Era3Path.LivingReef && civ.EcoResourcePolicy == 2) // Symbiotic Integration
        {
            y += 4f;
            Readout("Trade access", "Symbiotic Integration active — biological-market access with neighbors", w, ref y);
        }

        if (civ.Path == Era3Path.Terraformer)
        {
            y += 4f;
            Header("Runaway Risk", w, ref y);
            Readout("Exposure", $"{civ.RunawayExposure:F0}s sustained at extremity", w, ref y);
            Readout("Resilience", $"{civ.Resilience:P0}{(civ.HasCollapsed ? "  — COLLAPSED" : "")}", w, ref y);
        }

        y += 4f;
        Header("Adaptations  (evolved, not learned — era3-adaptation-trees-spec §2)", w, ref y);
        Readout("Selection pressure", "resource scarcity/crowding/conflict — a node accrues ZERO progress with none", w, ref y);
        foreach (var n in Era3AdaptationTree.Nodes)
        {
            if (!Era3AdaptationTree.IsApplicable(civ, n)) continue;
            string name = Era3AdaptationTree.GetNodeName(n.Id, civ);
            if (civ.UnlockedAdaptations.Contains(n.Id)) { Readout(name, "evolved", w, ref y); continue; }
            bool ready = Era3AdaptationTree.PrereqsUnlocked(civ, n);
            civ.AdaptationProgress.TryGetValue(n.Id, out float prog);
            float cost = Era3AdaptationTree.ResearchCost(n.Tier);
            string status = !ready ? "locked — prereqs needed" : $"{Mathf.Clamp01(prog / cost):P0}  (T{n.Tier})";
            Readout(name, status, w, ref y);
        }
    }

    /// Draws one row of always-reselectable named option buttons (NOT one-shot Cards — clicking a
    /// different option at any time just changes the posture; that's the point of "dials only").
    private void DrawOptionRow(string label, System.Collections.Generic.Dictionary<Era3Path, Era3EcologicalPaths.OptionRow> table,
        Era3Path path, ref int selected, float w, ref float y, CivilizationState civ = null, System.Func<int, string> lockGate = null)
    {
        if (!table.TryGetValue(path, out var row)) return;
        Header(label, w, ref y);
        for (int i = 0; i < row.Labels.Length; i++)
        {
            // era3-adaptation-trees-spec §2.4: options this codebase previously let a player select
            // for free are now earned via the Adaptation tree (or, for T3c-flavored ones, the Tech
            // tree) — a locked option renders disabled with its requirement named, exactly like
            // Era3PolicyCatalog's own gate rendering.
            string gate = lockGate?.Invoke(i);
            bool locked = gate != null && civ != null
                && !civ.UnlockedAdaptations.Contains(gate) && !civ.UnlockedNodes.Contains(gate) && !civ.Has(gate);
            bool isSelected = selected == i;
            string lockSuffix = locked ? $"  [Requires: {gate}]" : "";
            string text = row.Labels[i] + (string.IsNullOrEmpty(row.Hints[i]) ? "" : $"  <color=#5a6a7a>({row.Hints[i]})</color>") + lockSuffix;
            if (GUI.Button(new Rect(0f, y, w, Row), text, locked ? _dimBtn : isSelected ? _tabOn : _choiceBtn) && !locked)
                selected = i;
            y += Row;
        }
    }

    // ── Tab dispatch ───────────────────────────────────────────────────────────

    private void DrawTab(int tab, CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        switch (tab)
        {
            case 0: DrawEconomic    (civ, mgr, w, ref y); break;
            case 1: DrawBio         (civ, mgr, w, ref y); break;
            case 2: DrawInformational(civ, mgr, w, ref y); break;
            case 3: DrawExistential (civ, mgr, w, ref y); break;
            case 4: DrawCoercive    (civ, mgr, w, ref y); break;
            case 5: DrawPolity      (civ, mgr, w, ref y); break;
            case 6: DrawTechIdea    (civ, mgr, w, ref y); break;
            case 7: DrawEcoPolicyTab(civ, mgr, w, ref y); break;
            case 8: DrawPolicyCatalog(civ, mgr, w, ref y); break;
            case 9: DrawSettlements (civ, mgr, w, ref y); break;
            case 10: DrawHomeTab    (civ, mgr, w, ref y); break;
        }
    }

    /// The "*" home tab — civ identity/summary at a glance. Deliberately NOT where the five channel
    /// sliders live yet: the user asked for this tab built first, without moving or connecting those
    /// dials — that's a separate follow-up. Everything here is read-only, consolidating identity/
    /// status info that's currently scattered one-per-tab into a single overview.
    private void DrawHomeTab(CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        Header($"{civ.Name}", w, ref y);
        Readout("Track", $"{civ.Path}  ({civ.Architecture})", w, ref y);
        Readout("Resilience", $"{civ.Resilience:P0}{(civ.HasCollapsed ? "  — COLLAPSED" : "")}", w, ref y);
        if (civ.SuzerainId >= 0)
        {
            var suzerain = mgr.GetCiv(civ.SuzerainId);
            Readout("Status", $"Vassal of {suzerain?.Name ?? $"Civ {civ.SuzerainId}"}  (loyalty {civ.VassalLoyalty:P0})", w, ref y);
        }

        Header("Population & Settlements", w, ref y);
        int settlementCount = 0; float pop = 0f;
        foreach (var s in mgr.Settlements)
            if (s.OwnerCivId == civ.CommunityId) { settlementCount++; pop += s.Population; }
        Readout("Settlements", settlementCount.ToString(), w, ref y);
        Readout("Population", pop.ToString("F0"), w, ref y);
        if (civ.Roster.Count > 1)
            Readout("Roster diversity", $"{civ.Roster.Count} communities  (Shannon {Era3Polity.RosterShannonDiversity(civ.Roster):F2})", w, ref y);
        Readout("Administrative reach", $"{civ.AdministrativeReach:F1}  (splinter pressure {civ.SplinterPressure:P0})", w, ref y);

        Header("Technology", w, ref y);
        Readout("Tech tier", Era3TechTree.GetTechTier(civ).ToString(), w, ref y);
        int unlockedTech = 0, totalTech = 0;
        foreach (var n in Era3TechTree.Nodes)
        {
            if (n.IsIdea || !Era3TechTree.IsApplicable(civ, n)) continue;
            totalTech++;
            if (civ.UnlockedNodes.Contains(n.Id)) unlockedTech++;
        }
        Readout("Tech unlocked", $"{unlockedTech} / {totalTech}", w, ref y);
        if (civ.Path == Era3Path.CommerceEngine)
        {
            int unlockedIdea = 0, totalIdea = 0;
            foreach (var n in Era3TechTree.Nodes)
            {
                if (!n.IsIdea || !Era3TechTree.IsApplicable(civ, n)) continue;
                totalIdea++;
                if (civ.UnlockedNodes.Contains(n.Id)) unlockedIdea++;
            }
            Readout("Ideas unlocked", $"{unlockedIdea} / {totalIdea}", w, ref y);
        }
        else
        {
            int unlockedAdapt = 0, totalAdapt = 0;
            foreach (var n in Era3AdaptationTree.Nodes)
            {
                if (!Era3AdaptationTree.IsApplicable(civ, n)) continue;
                totalAdapt++;
                if (civ.UnlockedAdaptations.Contains(n.Id)) unlockedAdapt++;
            }
            Readout("Adaptations unlocked", $"{unlockedAdapt} / {totalAdapt}", w, ref y);
        }

        Header("Economy", w, ref y);
        // Stockpile readout removed (era3-systems-implementation-spec §6) — retired entirely; GDP below is the real wealth readout now.
        if (civ.Economy != null)
            Readout("GDP", civ.Economy.GDP.ToString("F2"), w, ref y);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // POLICY CATALOG (tab 8)  era3-policy-catalog-spec
    // ══════════════════════════════════════════════════════════════════════════

    private static readonly string[] SlotLabels =
    {
        "Production Doctrine (Econ, domestic)",   "Trade Posture (Econ, foreign)",
        "Propagation Doctrine (Bio, domestic)",   "Biosecurity Posture (Bio, foreign)",
        "Knowledge Doctrine (Info, domestic)",    "Openness Posture (Info, foreign)",
        "Cohesion Doctrine (Exist, domestic)",    "Conversion Posture (Exist, foreign)",
        "Order Doctrine (Coercive, domestic)",    "Diplomatic/Conflict Posture (Coercive, foreign)",
    };

    private void DrawPolicyCatalog(CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        Header("Policies  (ten standing-stance slots — one per channel × domestic/foreign, track-dependent)", w, ref y);
        Readout("Note", "a policy authorizes/buffs a class of maneuver — it never itself targets anyone", w, ref y);

        foreach (var slot in Era3PolicyCatalog.SlotsForTrack(civ))
        {
            y += 4f;
            Header(SlotLabels[(int)slot], w, ref y);

            if (!civ.PolicySlots.TryGetValue(slot, out var state) || state.ActiveId == null)
            {
                Readout("Status", "not yet initialized (ticks momentarily)", w, ref y);
                continue;
            }

            foreach (var opt in Era3PolicyCatalog.OptionsForSlot(civ, slot))
            {
                bool active = state.ActiveId == opt.Id;
                bool unlocked = Era3PolicyCatalog.IsUnlocked(civ, opt.Id);
                string gateText = string.IsNullOrEmpty(opt.Gate) ? "" : $"  [Requires: {opt.Gate}{(string.IsNullOrEmpty(opt.Gate2) ? "" : "+" + opt.Gate2)}]";
                string label = $"{opt.Name}  —  {opt.Hint}{gateText}";

                var style = active ? _tabOn : unlocked ? _choiceBtn : _dimBtn;
                if (GUI.Button(new Rect(0f, y, w, Row), label, style) && unlocked && !active && civ.IsPlayer)
                    mgr.SwitchPolicy(civ, slot, opt.Id);
                y += Row;
            }

            if (state.LockoutTicksRemaining > 0)
                ReadoutColored("Settling in", $"{state.LockoutTicksRemaining} ticks before next switch", new Color(0.9f, 0.7f, 0.3f), w, ref y);
        }
    }

    /// The three ecological paths' policy dials (Resource Policy/Conflict Posture/Organization),
    /// resolved automatically each tick rather than through Cards/treaties (mediation spectrum,
    /// era3-ecological-paths-spec §1) — but as an ADDITIONAL tab now, not a replacement for the
    /// rest of the panel (see TabNames' comment for why that changed).
    private void DrawEcoPolicyTab(CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        if (civ.Path == Era3Path.CommerceEngine)
        {
            Readout("Status", "N/A — this civ uses full Cards/treaty mediation instead (see the other tabs)", w, ref y);
            return;
        }
        DrawEcologicalPolicy(civ, mgr, w, ref y);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // TECH / IDEAS (tab 6)  era3-tech-idea-trees-spec §7
    // ══════════════════════════════════════════════════════════════════════════

    // ── Visual tech/idea/adaptation graph ───────────────────────────────────────
    // Civilization-series-style node graph: icons connected by prerequisite lines. Tech always
    // applies (era3-tech-idea-trees-spec §0); the second tree is Idea for CommerceEngine (+ Living
    // Reef's thin Coercive-only slice, handled naturally by Era3TechTree.IsApplicable) or Adaptation
    // for the three ecological tracks — same visual format either way, matching whichever tree(s)
    // are actually applicable to this civ's Track.
    private bool _techTreeExpanded;
    private bool _secondTreeExpanded;

    private struct GraphNodeVM
    {
        public string Id;
        public int Tier;
        public string[] Prereqs;
        public string Name;
        public bool Unlocked;
        public bool Ready;
        public float ProgressFrac;
        public bool Patronized;
    }

    private List<GraphNodeVM> BuildTechGraphNodes(CivilizationState civ)
    {
        var list = new List<GraphNodeVM>();
        foreach (var n in Era3TechTree.Nodes)
        {
            if (n.IsIdea || !Era3TechTree.IsApplicable(civ, n)) continue;
            civ.ResearchProgress.TryGetValue(n.Id, out float prog);
            list.Add(new GraphNodeVM
            {
                Id = n.Id, Tier = n.Tier, Prereqs = n.Prereqs, Name = Era3TechTree.GetNodeName(n.Id, civ),
                Unlocked = civ.UnlockedNodes.Contains(n.Id), Ready = Era3TechTree.PrereqsUnlocked(civ, n),
                ProgressFrac = Mathf.Clamp01(prog / Era3TechTree.ResearchCost(n.Tier)),
                Patronized = civ.PatronageNodeId == n.Id,
            });
        }
        return list;
    }

    private List<GraphNodeVM> BuildIdeaGraphNodes(CivilizationState civ)
    {
        var list = new List<GraphNodeVM>();
        foreach (var n in Era3TechTree.Nodes)
        {
            if (!n.IsIdea || !Era3TechTree.IsApplicable(civ, n)) continue;
            civ.ResearchProgress.TryGetValue(n.Id, out float prog);
            list.Add(new GraphNodeVM
            {
                Id = n.Id, Tier = n.Tier, Prereqs = n.Prereqs, Name = Era3TechTree.GetNodeName(n.Id, civ),
                Unlocked = civ.UnlockedNodes.Contains(n.Id), Ready = Era3TechTree.PrereqsUnlocked(civ, n),
                ProgressFrac = Mathf.Clamp01(prog / Era3TechTree.ResearchCost(n.Tier)),
                Patronized = civ.PatronageNodeId == n.Id,
            });
        }
        return list;
    }

    private List<GraphNodeVM> BuildAdaptationGraphNodes(CivilizationState civ, Era3Manager mgr)
    {
        var list = new List<GraphNodeVM>();
        foreach (var n in Era3AdaptationTree.Nodes)
        {
            if (!Era3AdaptationTree.IsApplicable(civ, n)) continue;
            civ.AdaptationProgress.TryGetValue(n.Id, out float prog);
            list.Add(new GraphNodeVM
            {
                Id = n.Id, Tier = n.Tier, Prereqs = n.Prereqs, Name = Era3AdaptationTree.GetNodeName(n.Id, civ),
                Unlocked = civ.UnlockedAdaptations.Contains(n.Id), Ready = Era3AdaptationTree.PrereqsUnlocked(civ, n),
                ProgressFrac = Mathf.Clamp01(prog / Era3AdaptationTree.ResearchCost(n.Tier)),
                Patronized = false, // Adaptation tree has no patronage mechanic — evolved, not sponsored
            });
        }
        return list;
    }

    private void DrawTechIdea(CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        var techNodes = BuildTechGraphNodes(civ);
        DrawTreeSection("Tech Tree", techNodes, supportsPatronage: true, civ, mgr, ref _techTreeExpanded, w, ref y);

        y += 6f;
        if (civ.Path == Era3Path.CommerceEngine)
        {
            var ideaNodes = BuildIdeaGraphNodes(civ);
            DrawTreeSection("Idea Tree", ideaNodes, supportsPatronage: true, civ, mgr, ref _secondTreeExpanded, w, ref y);
        }
        else
        {
            var adaptNodes = BuildAdaptationGraphNodes(civ, mgr);
            DrawTreeSection("Adaptation Tree", adaptNodes, supportsPatronage: false, civ, mgr, ref _secondTreeExpanded, w, ref y);
        }
    }

    /// Collapsed: the currently-patronized (or, failing that, the most-progressed not-yet-unlocked)
    /// node shown as one clickable icon + a summary line. Expanded: the full node graph. Clicking the
    /// collapsed icon expands; a "Collapse" button in the expanded header returns to the icon.
    private void DrawTreeSection(string title, List<GraphNodeVM> nodes, bool supportsPatronage,
                                  CivilizationState civ, Era3Manager mgr, ref bool expanded, float w, ref float y)
    {
        int unlockedCount = 0;
        foreach (var n in nodes) if (n.Unlocked) unlockedCount++;

        Header($"{title}  ({unlockedCount}/{nodes.Count} unlocked)", w, ref y);

        if (!expanded)
        {
            GraphNodeVM? featured = null;
            foreach (var n in nodes) if (n.Patronized) { featured = n; break; }
            if (featured == null)
            {
                float best = -1f;
                foreach (var n in nodes)
                    if (!n.Unlocked && n.Ready && n.ProgressFrac > best) { best = n.ProgressFrac; featured = n; }
            }

            const float iconSize = 48f;
            Rect iconRect = new Rect(0f, y, iconSize, iconSize);
            if (featured.HasValue)
            {
                DrawNodeIcon(iconRect, featured.Value);
                if (GUI.Button(iconRect, GUIContent.none, GUIStyle.none)) expanded = true;
                GUI.Label(new Rect(iconSize + 8f, y, w - iconSize - 84f, iconSize),
                    $"{featured.Value.Name}\nT{featured.Value.Tier} — {featured.Value.ProgressFrac:P0}"
                    + (featured.Value.Patronized ? "  ★ patronized" : ""), _dim);
            }
            else
            {
                GUI.Label(new Rect(iconSize + 8f, y, w - iconSize - 84f, iconSize), "Nothing in progress", _dim);
            }
            if (GUI.Button(new Rect(w - 76f, y + (iconSize - 20f) * 0.5f, 76f, 20f), "View Tree", _dimBtn))
                expanded = true;
            y += iconSize + 6f;
        }
        else
        {
            if (GUI.Button(new Rect(w - 76f, y, 76f, 18f), "Collapse", _dimBtn)) expanded = false;
            y += 22f;
            DrawNodeGraph(nodes, supportsPatronage, civ, mgr, w, ref y);
        }
    }

    private static Color NodeColor(GraphNodeVM n) =>
        n.Unlocked ? new Color(0.35f, 0.8f, 0.5f)
        : n.Patronized ? new Color(0.95f, 0.75f, 0.25f)
        : n.Ready ? new Color(0.4f, 0.55f, 0.85f)
        : new Color(0.35f, 0.35f, 0.4f);

    private void DrawNodeIcon(Rect r, GraphNodeVM n)
    {
        Color prev = GUI.color;
        GUI.color = NodeColor(n);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = prev;
        if (!n.Unlocked && n.ProgressFrac > 0f)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.DrawTexture(new Rect(r.x, r.yMax - r.height * n.ProgressFrac, r.width, r.height * n.ProgressFrac), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }

    /// Draws a straight line between two screen-space points using a rotated blank-texture rect —
    /// Unity's IMGUI has no native line primitive, and this (rotate the GUI matrix around the start
    /// point, blit a 1px-tall rect the right length) is the standard runtime-safe way to do it without
    /// GL calls, which are finicky to get pixel-aligned correctly inside OnGUI.
    private static void DrawLine(Vector2 from, Vector2 to, float thickness, Color color)
    {
        Vector2 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.01f) return;
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        Matrix4x4 prevMatrix = GUI.matrix;
        Color prevColor = GUI.color;
        GUI.color = color;
        GUIUtility.RotateAroundPivot(angle, from);
        GUI.DrawTexture(new Rect(from.x, from.y - thickness * 0.5f, length, thickness), Texture2D.whiteTexture);
        GUI.matrix = prevMatrix;
        GUI.color = prevColor;
    }

    private const float GraphBoxW = 92f;
    private const float GraphBoxH = 46f;
    private const float GraphRowGap = 12f;

    /// The actual node graph: one column per Tier, nodes stacked within a column in source-file
    /// order, prerequisite lines drawn from each prereq's right edge to the dependent's left edge.
    private void DrawNodeGraph(List<GraphNodeVM> nodes, bool supportsPatronage, CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        int maxTier = 1;
        foreach (var n in nodes) if (n.Tier > maxTier) maxTier = n.Tier;
        float colW = w / maxTier;
        float graphTop = y;

        var positions = new Dictionary<string, Rect>();
        var byTier = new List<GraphNodeVM>[maxTier + 1];
        for (int t = 0; t <= maxTier; t++) byTier[t] = new List<GraphNodeVM>(); // t=0 included defensively — no Tier-0 node exists today, but this avoids an NRE if one's ever added
        foreach (var n in nodes) byTier[Mathf.Clamp(n.Tier, 0, maxTier)].Add(n);

        float maxColHeight = 0f;
        for (int t = 1; t <= maxTier; t++)
        {
            float colX = (t - 1) * colW + (colW - GraphBoxW) * 0.5f;
            for (int i = 0; i < byTier[t].Count; i++)
            {
                float boxY = graphTop + i * (GraphBoxH + GraphRowGap);
                positions[byTier[t][i].Id] = new Rect(colX, boxY, GraphBoxW, GraphBoxH);
            }
            float colHeight = byTier[t].Count * (GraphBoxH + GraphRowGap);
            if (colHeight > maxColHeight) maxColHeight = colHeight;
        }

        // Prerequisite lines first, so node boxes render on top of the line endpoints.
        foreach (var n in nodes)
        {
            if (!positions.TryGetValue(n.Id, out Rect toRect)) continue;
            foreach (var prereqId in n.Prereqs)
            {
                if (!positions.TryGetValue(prereqId, out Rect fromRect)) continue; // prereq from the other tree, or not applicable to this civ
                bool active = n.Unlocked || n.Ready; // prereqId already confirmed positioned above
                Color lineColor = n.Unlocked ? new Color(0.45f, 0.75f, 0.5f, 0.9f) : new Color(0.5f, 0.5f, 0.55f, 0.6f);
                DrawLine(new Vector2(fromRect.xMax, fromRect.center.y), new Vector2(toRect.xMin, toRect.center.y), active ? 2.5f : 1.5f, lineColor);
            }
        }

        foreach (var n in nodes)
        {
            Rect r = positions[n.Id];
            DrawNodeIcon(r, n);
            GUI.Label(r, n.Name, _nodeLabel);
            if (n.Patronized)
                GUI.Label(new Rect(r.xMax - 16f, r.y - 2f, 16f, 14f), "★", _dim);

            bool clickable = supportsPatronage && n.Ready && !n.Unlocked && civ.IsPlayer;
            if (clickable && GUI.Button(r, GUIContent.none, GUIStyle.none))
                mgr.SetPatronageTarget(civ, n.Id);
        }

        y = graphTop + maxColHeight + 8f;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // POLITY (tab 5)  era3-polity-model-spec §2-§4
    // ══════════════════════════════════════════════════════════════════════════

    private void DrawPolity(CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        Header("Administrative Reach", w, ref y);
        Readout("Reach capacity", $"{civ.AdministrativeReach:F1}", w, ref y);
        Color pressureClr = civ.SplinterPressure > 0.6f ? new Color(1f, 0.4f, 0.3f)
                            : civ.SplinterPressure > 0.3f ? new Color(0.95f, 0.8f, 0.3f)
                            : new Color(0.4f, 0.85f, 0.45f);
        ReadoutColored("Splinter pressure", $"{civ.SplinterPressure:P0}", pressureClr, w, ref y);
        if (civ.DecentralizeBonus != 0f)
            Readout("Decentralization", $"{civ.DecentralizeBonus:+0%;-0%} reach", w, ref y);

        y += 4f;
        Header("Population Roster", w, ref y);
        if (civ.Roster.Count == 0)
        {
            Readout("Composition", "no settlement data yet", w, ref y);
        }
        else
        {
            foreach (var e in civ.Roster)
            {
                string who = e.CommunityId == civ.CommunityId ? $"{civ.Name} (founding)" : $"Community {e.CommunityId}";
                Readout(who, $"{e.Fraction:P0}", w, ref y);
            }
            if (civ.Roster.Count > 1)
                Readout("Diversity (Shannon)", $"{Era3Polity.RosterShannonDiversity(civ.Roster):F2}", w, ref y);
        }

        y += 4f;
        // era3-civilization-tracks-spec §1/§2 Mediation Spectrum: Propose Alliance, Joint Research,
        // Gift, Insult, Steal Tech, Declare War/Sue for Peace, Vassalize, and Federate are all
        // Representative-mediated "formal" actions — Cards + Representative are CommerceEngine-only,
        // same restriction DrawPendingCardPopup already enforces. Ecological-path civs (and Living
        // Reef unless it's running Symbiotic Integration) don't get a proposal/response layer at all;
        // they still trade — see era3-civilization-tracks-spec_1 §2.1's Tacit Exchange, which requires
        // NO Representative and already runs for everyone unconditionally in TickTradeEngine — and
        // their only "conflict" lever is the Conflict Posture dial on the Eco Policy tab.
        bool hasFormalMediation = civ.Path == Era3Path.CommerceEngine;

        // Click-through diplomacy screen: the Relations list shows every contacted civ as a row;
        // clicking one opens a dedicated per-civ screen (Civilization-series style) holding every
        // action that used to render inline for every civ at once.
        var selected = _selectedDiplomacyCivId >= 0 ? mgr.GetCiv(_selectedDiplomacyCivId) : null;
        if (selected != null && selected.HasCollapsed) { selected = null; _selectedDiplomacyCivId = -1; }

        if (selected == null)
        {
            Header("Relations  (select a civilization to open diplomacy)", w, ref y);
            foreach (var npc in mgr.NpcCivs)
            {
                if (npc.HasCollapsed) continue;
                float pr = mgr.GetPolityRelation(civ.CommunityId, npc.CommunityId);
                Color clr = pr > 0.25f ? new Color(0.4f, 0.85f, 0.45f) : pr < -0.25f ? new Color(1f, 0.4f, 0.3f) : new Color(0.8f, 0.8f, 0.8f);
                Rect row = new Rect(0f, y, w, Row);
                if (GUI.Button(row, "", GUIStyle.none)) _selectedDiplomacyCivId = npc.CommunityId;
                ReadoutColored(npc.Name, $"relation {pr:+0.00;-0.00}", clr, w, ref y);
            }
        }
        else
        {
            if (GUI.Button(new Rect(0f, y, 70f, Row), "← Back", _dimBtn)) _selectedDiplomacyCivId = -1;
            y += Row + 4f;
            DrawDiplomacyScreen(civ, mgr, selected, hasFormalMediation, w, ref y);
        }

        if (civ.IsPlayer && civ.Architecture == CognitiveArchitecture.Collective && mgr.CanUseHostGuestRelation(civ))
        {
            y += 2f;
            bool newVal = GUI.Toggle(new Rect(0f, y, w, Row), civ.SeekSymbioticHosts, "  Seek Symbiotic Hosts (AI evaluates on your behalf)");
            if (newVal != civ.SeekSymbioticHosts) civ.SeekSymbioticHosts = newVal;
            y += Row;
            if (civ.SeekSymbioticHosts)
            {
                Readout("Intensity", $"{civ.SeekSymbioticHostsIntensity:P0}", w, ref y);
                civ.SeekSymbioticHostsIntensity = GUI.HorizontalSlider(new Rect(0f, y, w, Row), civ.SeekSymbioticHostsIntensity, 0.05f, 1f);
                y += Row;
            }
        }
        if (!hasFormalMediation)
            Readout("Mediation", "none — trades tacitly with everyone automatically; conflict is the Eco Policy tab's Conflict Posture dial", w, ref y);

        if (hasFormalMediation)
        {
            y += 4f;
            Header("Vassals", w, ref y);
            bool anyVassal = false;
            foreach (var other in mgr.AllCivsView)
            {
                if (other.SuzerainId != civ.CommunityId) continue;
                anyVassal = true;
                Readout(other.Name ?? $"Civ {other.CommunityId}", $"loyalty {other.VassalLoyalty:P0}", w, ref y);
            }
            if (!anyVassal) Readout("Status", "none", w, ref y);

            // Federation only meaningful for the player civ — mirrors TryVassalize's player-only scope.
            if (civ.IsPlayer)
            {
                y += 4f;
                Header("Federation  (full merge — unlike Vassalization, no suzerain/tribute; rosters combine)", w, ref y);
                foreach (var npc in mgr.NpcCivs)
                {
                    if (npc.HasCollapsed || npc.SuzerainId >= 0) continue;
                    // Mirrors the real gate in Era3Manager.TryFederate — accept_probability, not a raw
                    // disposition threshold (era3-diplomacy-ai-spec §3).
                    float accept = Era3Diplomacy.AcceptProbability(mgr, npc, civ, Era3Diplomacy.ActionType.CollectiveSecurityAlliance);
                    bool eligible = accept >= 0.5f;
                    Rect row = new Rect(0f, y, w, Row);
                    string label = $"{npc.Name}  (accept chance {accept:P0})";
                    if (GUI.Button(row, label, eligible ? _choiceBtn : _dimBtn) && eligible)
                        mgr.TryFederate(npc.CommunityId);
                    y += Row;
                }
            }
        }

        DrawCards(5, civ, mgr, w, ref y);
    }

    private const float GiftAmount = 0.3f;

    /// The dedicated per-civ diplomacy screen opened by clicking a Relations row — everything that
    /// used to render inline for every contacted civ at once now lives here, scoped to just `npc`.
    private void DrawDiplomacyScreen(CivilizationState civ, Era3Manager mgr, CivilizationState npc, bool hasFormalMediation, float w, ref float y)
    {
        float pr = mgr.GetPolityRelation(civ.CommunityId, npc.CommunityId);
        float sd = mgr.GetSpeciesDisposition(civ.CommunityId, npc.CommunityId);
        Header(npc.Name ?? $"Civ {npc.CommunityId}", w, ref y);
        Readout("Relation", $"{pr:+0.00;-0.00}", w, ref y);
        Readout("Species disposition", $"{sd:+0.00;-0.00}", w, ref y);
        if (npc.SuzerainId == civ.CommunityId)
            Readout("Status", $"Your vassal  (loyalty {npc.VassalLoyalty:P0})", w, ref y);
        else if (npc.SuzerainId >= 0)
            Readout("Status", $"Vassal of civ {npc.SuzerainId}", w, ref y);

        y += 4f;
        if (civ.IsPlayer && hasFormalMediation) DrawDiplomacyRow(civ, mgr, npc, w, ref y);

        // host-guest-trigger-spec.md §3: independent of hasFormalMediation (which excludes Living
        // Reef even with Symbiotic Integration — see CanUseHostGuestRelation's own comment).
        // Collective gets no per-target row here — see the "Seek Symbiotic Hosts" dial instead.
        if (civ.IsPlayer && civ.Architecture != CognitiveArchitecture.Collective
            && mgr.CanUseHostGuestRelation(civ) && mgr.CanUseHostGuestRelation(npc))
            DrawHostGuestRow(civ, mgr, npc, w, ref y);

        if (!civ.IsPlayer || !hasFormalMediation)
            Readout("Mediation", "no direct action available for this civ/track", w, ref y);
    }

    /// One row of buttons covering EVERY per-civ diplomatic/war action against `npc` — the
    /// consolidated "diplomacy screen" for a specific polity: Declare War / Sue for Peace are just
    /// two entries here alongside Propose Alliance, Joint Research, Gift, Insult, Steal Tech, and
    /// Covert Strike, not a separate flow. Buttons that don't apply right now (e.g. "Sue for Peace"
    /// while at peace) simply don't render, rather than showing an always-visible fixed action set.
    private void DrawDiplomacyRow(CivilizationState civ, Era3Manager mgr, CivilizationState npc, float w, ref float y)
    {
        bool atWar = mgr.IsAtWar(civ.CommunityId, npc.CommunityId);
        float bw = w / 3f;
        int col = 0;
        // Local functions can't capture a `ref` parameter of the enclosing method (CS1628) — mirror
        // it into a plain local for Btn to close over, then write the final value back to y below.
        float localY = y;
        void Btn(string label, System.Action apply)
        {
            if (col == 3) { localY += Row; col = 0; }
            if (GUI.Button(new Rect(col * bw, localY, bw - 2f, Row - 2f), label, _dimBtn)) apply();
            col++;
        }

        if (atWar)
        {
            Btn("Sue for Peace", () => mgr.ProposePeace(civ.CommunityId, npc.CommunityId));
            // era3-sovereignty-interaction-gaps-spec.md §1.2: additional peace term, coexists with
            // plain peace above — only meaningful for the player (mirrors TryVassalize's own scope).
            if (civ.IsPlayer && npc.SuzerainId < 0)
                Btn("Peace: Impose Vassalage", () => mgr.ProposeVassalagePeace(npc.CommunityId));
        }
        else
        {
            Btn("Declare War", () => mgr.DeclareWar(civ, npc.CommunityId));
            Btn("Covert Strike", () => mgr.CovertStrike(civ, npc.CommunityId, civ.WarTargetSubsystem));
            Btn("Propose Alliance", () => mgr.ProposeAlliance(civ, npc.CommunityId));
            Btn("Joint Research", () => mgr.ProposeJointResearch(civ, npc.CommunityId));
            Btn($"Gift ({GiftAmount:F1})", () => mgr.SendGift(civ, npc.CommunityId, GiftAmount));
            Btn("Insult", () => mgr.SendInsult(civ, npc.CommunityId));
            Btn("Steal Tech", () => mgr.TryStealTech(civ, npc.CommunityId));
        }
        y = localY + Row + 4f;
    }

    /// host-guest-trigger-spec.md §3 Trigger Path A: "Offer Territory" (Host role) and "Request
    /// Territory" (Guest role), same immediate-action button convention as DrawDiplomacyRow rather
    /// than a multi-step allocation-adjustment wizard — every other diplomatic action in this row
    /// fires immediately on click with a sensible default, and this follows suit rather than
    /// introducing the only multi-step flow in the panel.
    private void DrawHostGuestRow(CivilizationState civ, Era3Manager mgr, CivilizationState npc, float w, ref float y)
    {
        bool atWar = mgr.IsAtWar(civ.CommunityId, npc.CommunityId);
        bool alreadyRelated = mgr.GetHostGuestRelation(civ.CommunityId, npc.CommunityId) != null
                            || mgr.GetHostGuestRelation(npc.CommunityId, civ.CommunityId) != null;
        if (atWar || alreadyRelated) return; // nothing to offer/request right now — row simply doesn't render

        bool distributedNoise = civ.Architecture == CognitiveArchitecture.Distributed;
        float bw = w / 2f;

        // §3: "Offer Territory" only available with real headroom (default 0.7) — isn't shown, not
        // just disabled, when the proposer is already tight on space.
        bool hasHeadroom = mgr.SlotCapacityUtilization(civ) < 0.7f;
        if (hasHeadroom && GUI.Button(new Rect(0f, y, bw - 2f, Row - 2f), "Offer Territory", _dimBtn))
        {
            float allocation = 0.5f;
            // Distributed: "the actual target/allocation sent may drift slightly from what the
            // player set" — the one concrete numeric knob here (allocation) gets Representative jitter.
            if (distributedNoise) allocation = Mathf.Clamp01(allocation + UnityEngine.Random.Range(-0.15f, 0.15f));
            mgr.SubmitHostGuestProposal(new Era3Manager.HostGuestProposal
            {
                ProposerCivId = civ.CommunityId, ProposerRole = Era3Manager.HostGuestProposalRole.Host,
                TargetCivId = npc.CommunityId, InitialAllocationLevel = allocation,
            });
        }
        // "Request Territory" (Guest role): no headroom precondition — pressure only affects whether
        // the TARGET accepts (via GuestPressureReliefBonus), not whether the player can ask.
        if (GUI.Button(new Rect(bw, y, bw - 2f, Row - 2f), "Request Territory", _dimBtn))
        {
            mgr.SubmitHostGuestProposal(new Era3Manager.HostGuestProposal
            {
                ProposerCivId = civ.CommunityId, ProposerRole = Era3Manager.HostGuestProposalRole.Guest,
                TargetCivId = npc.CommunityId,
            });
        }
        y += Row;
    }

    /// Lists ONLY this civ's own settlements (this tab is opened from a specific civ's panel — it
    /// should show that civ's holdings, not the whole world's; the "who else is settling the planet"
    /// view belongs on Ranks/Global, not here).
    private void DrawSettlements(CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        Header($"{civ.Name} Settlements", w, ref y);

        var owned = new List<Era3Manager.Settlement>();
        foreach (var s in mgr.Settlements) if (s.OwnerCivId == civ.CommunityId) owned.Add(s);
        Readout("Total owned", owned.Count.ToString(), w, ref y);

        if (owned.Count == 0)
        {
            Readout("Status", "none founded yet", w, ref y);
            return;
        }

        owned.Sort((a, b) => b.Tier.CompareTo(a.Tier)); // biggest first

        Header("Settlements  (click a row to jump the camera there)", w, ref y);
        foreach (var s in owned)
        {
            string tierLabel = Era3Manager.SettlementTierLabel(mgr.GetCivPath(civ.CommunityId), s.Tier);
            string multiTag = s.ContributingCommunities.Count > 1 ? $" [x{s.ContributingCommunities.Count} species]" : "";
            string occupiedTag = s.IsOccupied ? " [OCCUPIED]" : "";
            bool underAttack = mgr.RecentAttackFlash.TryGetValue(s.Id, out float expiry) && Time.time < expiry;
            string attackTag = underAttack ? " [UNDER ATTACK]" : "";

            // Whole row is clickable (mirrors GameHUD's Ranks-page pattern) — clicking swings the
            // camera to the settlement, which is the direct fix for "I can't find my settlement":
            // it may simply be on the far side of the planet right now, or hard to spot at a glance
            // against similarly-coloured terrain/liquid.
            Rect rowRect = new Rect(0f, y, w, Row);
            if (GUI.Button(rowRect, "", GUIStyle.none)) FocusOnSettlement(s, mgr);
            Readout($"{s.Name}", $"{tierLabel} · pop {s.Population:F0}{multiTag}{occupiedTag}{attackTag}", w, ref y);

            // era3-sovereignty-interaction-gaps-spec.md §4: Cull — only shown when there's a real
            // non-civ cohort here to suppress, drawn as its own row below the click-to-focus row
            // above rather than sharing its rect (avoids overlapping hit-test regions).
            if (civ.IsPlayer && mgr.HasCullableCohort(s))
            {
                if (GUI.Button(new Rect(0f, y, w, Row - 4f), "Cull Wild Cohort", _dimBtn))
                    mgr.CullCohortAtSettlement(civ, s);
                y += Row;
            }
        }
    }

    /// Swings the orbit camera to look directly at a settlement's world position — same mechanism
    /// GameHUD's Ranks-page community rows use (OrbitCamera.FocusOnDirection), so behavior is familiar.
    private void FocusOnSettlement(Era3Manager.Settlement s, Era3Manager mgr)
    {
        if (_orbitCam == null) _orbitCam = FindAnyObjectByType<OrbitCamera>();
        if (_orbitCam == null) return;
        // s.Position is a founding-time WORLD snapshot that goes stale as the planet rotates — the
        // marker itself compensates for the spin every frame, s.Position does not. Using it directly
        // aimed the camera at where the settlement WAS at founding, not where it is now, landing the
        // target off to the side after the planet had turned. GetCurrentWorldPosition reads the
        // marker's actual live transform instead.
        Vector3 currentPos = Era3VisualManager.Instance != null
            ? Era3VisualManager.Instance.GetCurrentWorldPosition(s) : s.Position;
        Vector3 dir = (currentPos - mgr.PlanetCenter).normalized;
        if (dir == Vector3.zero) return;
        // Preserve whatever zoom the player already had rather than snapping in close — a fixed
        // zoomDistance here made every focus-click yank the camera to the same tight framing
        // regardless of where the player had it, which felt jarring and too close.
        _orbitCam.FocusOnDirection(dir, _orbitCam.distance);
        _orbitCam.EnablePlanetLock();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // ECONOMIC (tab 0)  §4.1
    // ══════════════════════════════════════════════════════════════════════════

    private void DrawEconomic(CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        Header("Investment", w, ref y);
        civ.InvestEconomic = Dial("Economic channel", civ.InvestEconomic, w, ref y);

        switch (civ.Architecture)
        {
            // ── Individuated ─────────────────────────────────────────────────
            case CognitiveArchitecture.Individuated:
                Header("Sector Allocation", w, ref y);
                // era3-systems-implementation-spec §2: the old 3rd leg (SectorMilitary/"Craft /
                // specialisation") deleted — superseded by real Economy.Allocation["Military"] in the
                // "Labor Allocation" block below (renamed from "Policy Sectors"), which already covers
                // Military for every architecture. Down to a real 2-way Production/Culture split.
                bool massLabourCapped = civ.Subtrack == IndividuatedSubTrack.A2_SolitaryManipulative;
                if (massLabourCapped)
                {
                    Readout("Agriculture", "capped (solitary lineage)", w, ref y);
                    civ.SectorCulture = Dial("Trade / services", civ.SectorCulture, w, ref y);
                }
                else
                {
                    var sectors = new[] { civ.SectorProduction, civ.SectorCulture };
                    AllocationDial(civ, "sector", "Agriculture / industry", w, ref y, sectors, 0);
                    AllocationDial(civ, "sector", "Trade / services",       w, ref y, sectors, 1);
                    civ.SectorProduction = sectors[0]; civ.SectorCulture = sectors[1];
                }

                // Tariff Rate retired (era3-adaptation-trees-spec §1.1) — was never consumed by any
                // formula, only its own slider; superseded by the gated Trade Posture policy slot
                // (PolCat tab), which drives ConnectionStrength for real.
                Header("Foreign Posture", w, ref y);
                civ.ForeignOpenness = Dial("Foreign openness",  civ.ForeignOpenness, w, ref y);
                Readout("Tariffs/embargo", "see PolCat > Trade Posture", w, ref y);
                break;

            // ── Distributed ──────────────────────────────────────────────────
            case CognitiveArchitecture.Distributed:
                Header("Routing & Exchange", w, ref y);
                // NetworkConnectivityTier caps partner-choice slot rows.
                int slots = civ.NetworkConnectivityTier + 1;
                Readout("Active trade links", $"{slots} (connectivity tier {civ.NetworkConnectivityTier})", w, ref y);
                // Exchange Posture retired — same reasoning as Tariff Rate above.
                Readout("Sanction/reward posture", "see PolCat > Trade Posture", w, ref y);
                // "Boom/crash target stockpile" dial deleted (era3-systems-implementation-spec §6) —
                // Stockpile itself retired; real accumulated capacity is now Economy.Stock, shown in
                // the Labor Allocation block below.
                break;

            // ── Collective ───────────────────────────────────────────────────
            case CognitiveArchitecture.Collective:
                // era3-systems-implementation-spec §2/§6: Caste Allocation (Forager/Builder/Soldier/
                // Trader) and Biomass Target (StockpileTarget) both deleted — redundant with real
                // Policy Sectors ("Labor Allocation" below), which already applies to Collective same
                // as every other architecture; StockpileTarget moot with Stockpile itself retired.
                break;
        }

        // Trade partner summary (all archs).
        Header("Trade Partners", w, ref y);
        foreach (var npc in mgr.NpcCivs)
        {
            float h     = civ.TradeHealth.TryGetValue(npc.CommunityId, out float hv) ? hv : 0.5f;
            var   label = civ.GetTradeLabel(npc.CommunityId);
            Color clr   = label switch
            {
                TradeHealthLabel.Mutualism  => new Color(0.25f, 0.85f, 0.35f),
                TradeHealthLabel.Parasitism => new Color(1f,    0.3f,  0.3f),
                _                           => new Color(0.72f, 0.72f, 0.72f),
            };
            ReadoutColored(npc.Name, $"{label}  {h:P0}", clr, w, ref y);
        }

        // ── Policy Sector Allocation (policy-allocation-spec §2) ────────────────
        if (civ.Economy != null)
        {
            var eco = civ.Economy;
            Header("Labor Allocation", w, ref y); // renamed from "Policy Sectors" (era3-systems-implementation-spec §4) — inherits the name from the deleted d3_caste_labor card
            Readout("GDP index", $"{eco.GDP:F2}  |  Mob.drag {eco.MobilizationDrag:P0}  |  Ext.tax {eco.ExtractionTax:F3}", w, ref y);
            if (eco.WarWeariness > 0.05f)
                ReadoutColored("War weariness", $"{eco.WarWeariness:P0}", new Color(1f, 0.45f, 0.2f), w, ref y);

            // Mechanically this was already fine — Economy.Tick normalizes each sector's effective
            // share (slider ÷ sum of all sliders) internally regardless of raw magnitude — but the
            // raw sliders themselves didn't visually reflect that, so a player dragging one had no
            // feedback that the others were about to lose share. Now uses the same proportional-
            // rescale AllocationDial as Sector/Caste Allocation, for consistent behavior everywhere:
            // the slider position IS the real share, not a number you have to read off separately.
            var sectorKeys = new List<string>(CivilizationEconomy.AllSectors);
            var allocs = new float[sectorKeys.Count];
            for (int i = 0; i < sectorKeys.Count; i++)
                eco.Allocation.TryGetValue(sectorKeys[i], out allocs[i]);

            for (int i = 0; i < sectorKeys.Count; i++)
            {
                string key = sectorKeys[i];
                if (!eco.Stock.TryGetValue(key, out float stock)) continue;
                AllocationDial(civ, "policySector", eco.GetLabel(key), w, ref y, allocs, i);

                // Stock bar: green at low, gold at mid, shows accumulated capacity.
                Color stockClr = stock < 0.3f ? new Color(0.45f, 0.75f, 0.5f)
                               : stock < 0.8f ? new Color(0.85f, 0.75f, 0.25f)
                                              : new Color(0.35f, 0.65f, 1f);
                ColorBar($"  stock", Mathf.Clamp01(stock / 1.5f), stockClr, w, ref y);
            }
            for (int i = 0; i < sectorKeys.Count; i++) eco.Allocation[sectorKeys[i]] = allocs[i];
        }

        // ── Emerged Idea cards (idea-emergence-spec §3.5) ────────────────────
        DrawIdeaCards(civ, mgr, w, ref y);

        DrawCards(0, civ, mgr, w, ref y);
    }

    private void DrawIdeaCards(CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        var pending = mgr.PendingIdeas;
        if (pending.Count == 0) return;

        y += 4f;
        Sub("Emerged Ideas", w, ref y);

        foreach (var (pc, idea) in pending)
        {
            if (pc.CommunityId != civ.CommunityId) continue;

            float cardH = Row + Row + Row * 2f + 6f;
            GUI.Box(new Rect(-2f, y - 2f, w + 4f, cardH + 8f), GUIContent.none, _card);

            GUI.Label(new Rect(0f, y, w, Row), $"💡 {idea.DisplayName}", _sub);
            y += Row;
            GUI.Label(new Rect(0f, y, w, Row * 2f), idea.Description, _dim);
            y += Row * 2f + 2f;

            if (GUI.Button(new Rect(0f, y, w * 0.48f, Row), "Invest & Adopt", _choiceBtn))
                mgr.ResolvePendingIdea(idea.Id, adopted: true);
            if (GUI.Button(new Rect(w * 0.52f, y, w * 0.48f, Row), "Not now", _dimBtn))
                mgr.ResolvePendingIdea(idea.Id, adopted: false);
            y += Row + 6f;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // GENETIC / BIOLOGICAL (tab 1)  §4.2
    // ══════════════════════════════════════════════════════════════════════════

    private void DrawBio(CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        Header("Investment", w, ref y);
        civ.InvestBiological = Dial("Biological channel", civ.InvestBiological, w, ref y);

        // Resilience readout (all archs).
        Header("Resilience", w, ref y);
        Color rClr = civ.Resilience >= 0.6f ? new Color(0.2f, 0.85f, 0.3f)
                   : civ.Resilience >= 0.3f ? new Color(1f,   0.8f,  0.15f)
                   :                          new Color(1f,   0.25f, 0.25f);
        ColorBar("Resilience", civ.Resilience, rClr, w, ref y);
        if (civ.HasCollapsed)
            ReadoutColored("Status", "COLLAPSED", new Color(1f, 0.15f, 0.15f), w, ref y);

        switch (civ.Architecture)
        {
            // ── Individuated ─────────────────────────────────────────────────
            case CognitiveArchitecture.Individuated:
                Header("Health & Reproduction", w, ref y);
                // Public Health Investment retired as a free slider — see PolCat > Propagation
                // Doctrine's gated Public Health Investment policy (drives GenDMin for real).
                Readout("Plague resistance", "see PolCat > Propagation Doctrine", w, ref y);

                // A1 (social foraging): parental investment dial present.
                if (civ.Subtrack == IndividuatedSubTrack.A1_SocialForaging)
                    civ.ParentalInvestment = Dial("Parental investment", civ.ParentalInvestment, w, ref y);
                // A2 (solitary/manipulative): no family dials — tab is mostly cards.
                else if (civ.Subtrack == IndividuatedSubTrack.A2_SolitaryManipulative)
                    Readout("Family dials", "absent — solitary lineage (cards only)", w, ref y);
                // A3 (bulk-brain/pod): pod-bonding dial instead of family.
                else if (civ.Subtrack == IndividuatedSubTrack.A3_BulkBrain)
                    civ.ParentalInvestment = Dial("Pod-bonding investment", civ.ParentalInvestment, w, ref y);

                // Monogyne fragility indicator is open item for Individuated — omitted.
                break;

            // ── Distributed ──────────────────────────────────────────────────
            case CognitiveArchitecture.Distributed:
                Header("Graft & Compartmentalisation", w, ref y);
                // 0=tight (low infection risk) ↔ 1=permissive (high exchange).
                civ.GraftCompatThreshold = Dial("Graft-compat threshold  (tight ↔ open)",
                    civ.GraftCompatThreshold, w, ref y);
                civ.CompartmentInvest    = Dial("CODIT compartmentalisation",
                    civ.CompartmentInvest, w, ref y);
                break;

            // ── Collective ───────────────────────────────────────────────────
            case CognitiveArchitecture.Collective:
                Header("Reproduction & Immune Caste", w, ref y);
                civ.ReproductiveSuppressRatio = Dial("Reproductive suppression",
                    civ.ReproductiveSuppressRatio, w, ref y);
                // ImmuneCasteInvest dial deleted (era3-systems-implementation-spec §2) — real effect
                // already delivered by a Policy Catalog option on GenDMin.

                // Monogyne: persistent fragility indicator.
                if (civ.RepMode == ReproductiveMode.Monogyne)
                {
                    y += 3f;
                    ReadoutColored("Reproductive bottleneck", "MONOGYNE — queen loss is existential",
                        new Color(1f, 0.65f, 0.2f), w, ref y);
                }
                break;
        }

        DrawCards(1, civ, mgr, w, ref y);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // INFORMATIONAL (tab 2)  §4.3
    // ══════════════════════════════════════════════════════════════════════════

    private void DrawInformational(CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        Header("Investment", w, ref y);
        civ.InvestInformation = Dial("Information channel", civ.InvestInformation, w, ref y);

        switch (civ.Architecture)
        {
            // ── Individuated ─────────────────────────────────────────────────
            case CognitiveArchitecture.Individuated:
                Header("Education & Infrastructure", w, ref y);
                civ.CommInfraInvest = Dial("Communication infrastructure", civ.CommInfraInvest, w, ref y);
                // Censorship Level retired as a free slider — see PolCat > Knowledge Doctrine's
                // State Doctrine Control/Open Academy policies (drive SignalLegibility for real).
                Readout("Censorship / state control", "see PolCat > Knowledge Doctrine", w, ref y);

                // Communication Medium from Era 2 gates Idea card types.
                Header("Idea Medium", w, ref y);
                string medStr = civ.CommMedium switch
                {
                    CommunicationMedium.VocalAuditory  => "Vocal — oral tradition cards unlocked",
                    CommunicationMedium.VisualGestural => "Visual — proto-writing / art cards unlocked",
                    CommunicationMedium.ChemicalPheromonal       => "Chemical — inherits Distributed info cards",
                    CommunicationMedium.BioluminescentElectrical => "Bioelectric — specialist cards only",
                    _                                  => "Unset — complete Era 2 comm gene first",
                };
                Readout("Comm medium", medStr, w, ref y);
                break;

            // ── Distributed ──────────────────────────────────────────────────
            case CognitiveArchitecture.Distributed:
                Header("Signal Posture", w, ref y);
                // Signal legibility dial deleted (era3-systems-implementation-spec §2) — distinct
                // from, and superseded by, the real Era3PolicyCatalog.Var.SignalLegibility (already
                // fully computed from State Doctrine Control/Open Academy-style policy choices).
                // 0=disinfo ↔ 1=honest.
                civ.HonestSignalWeight = Dial("Honest-signal weight  (disinfo ↔ honest)",
                    civ.HonestSignalWeight, w, ref y);

                Header("Signal Bandwidth", w, ref y);
                Readout("Bandwidth tier", $"{civ.SignalBandwidthTier}  (gates disinfo/eavesdrop cards)", w, ref y);
                ReadoutColored("Disinfo capacity",   $"{civ.DisinfoCapability:P0}",   Color.white, w, ref y);
                ReadoutColored("Detection capacity", $"{civ.DetectionCapability:P0}", Color.white, w, ref y);
                break;

            // ── Collective ───────────────────────────────────────────────────
            case CognitiveArchitecture.Collective:
                Header("Stigmergic Bandwidth", w, ref y);
                civ.StigmergicBandwidth = Dial("Pheromone-channel richness", civ.StigmergicBandwidth, w, ref y);
                // PheroMemoryInvest dial deleted (era3-systems-implementation-spec §2) —
                // RitualInvestment (Existential tab, below) already covers ritual/pheromone memory.

                // Decision Velocity gates cascade-error card.
                Header("Decision Velocity", w, ref y);
                Readout("Current setting", civ.DecVelocity.ToString()
                    + (civ.DecVelocity == DecisionVelocity.Slow
                       ? "  — cascade-error card may appear" : ""), w, ref y);
                break;
        }

        DrawCards(2, civ, mgr, w, ref y);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // EXISTENTIAL (tab 3)  §4.4
    // ══════════════════════════════════════════════════════════════════════════

    private void DrawExistential(CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        Header("Investment", w, ref y);
        civ.InvestReligion = Dial("Existential channel", civ.InvestReligion, w, ref y);

        // Ritual/pheromone memory is present for all archs at tier 1/2.
        Header("Ritual Cohesion", w, ref y);
        civ.RitualInvestment = Dial("Ritual / pheromone memory", civ.RitualInvestment, w, ref y);

        switch (civ.Architecture)
        {
            // ── Individuated ─────────────────────────────────────────────────
            case CognitiveArchitecture.Individuated:
                Header("Belief & Doctrine", w, ref y);
                string tierName = civ.BeliefTier switch
                {
                    0 => "None",
                    1 => "Tier 1 — Ritual / Superstition",
                    2 => "Tier 2 — Attachment / Trust-in-provider",
                    3 => "Tier 3 — Cosmological / Organised",
                    _ => "?",
                };
                Readout("Belief tier",        tierName,                                       w, ref y);
                Readout("Religion",           civ.HasOrganizedReligion ? "Organised" : "Folk", w, ref y);
                Readout("Kinship",            civ.Kinship == KinshipPolicy.Unset ? "—" : civ.Kinship.ToString(), w, ref y);

                // Orthodoxy and proselytising dials are present for Individuated only.
                civ.OrthodoxyLevel     = Dial("Orthodoxy  (pluralism ↔ orthodox)", civ.OrthodoxyLevel,     w, ref y);
                civ.ProselytizePosture = Dial("Proselytising posture",              civ.ProselytizePosture, w, ref y);
                break;

            // ── Distributed / Collective ──────────────────────────────────────
            default:
                Header("Belief Tier", w, ref y);
                Readout("Max tier", "Tier 2 — tier 3 unavailable (no theory-of-mind threshold)", w, ref y);
                Readout("Current", $"Tier {civ.BeliefTier}", w, ref y);
                // Representative framing (addendum §3.3) — shown when tab opens a card.
                DrawRepBlock(mgr, w, ref y);
                break;
        }

        DrawCards(3, civ, mgr, w, ref y);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // COERCIVE (tab 4)  §4.5
    // ══════════════════════════════════════════════════════════════════════════

    private void DrawCoercive(CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        Header("Investment", w, ref y);
        civ.InvestCoercive = Dial("Coercive channel", civ.InvestCoercive, w, ref y);

        switch (civ.Architecture)
        {
            // ── Individuated ─────────────────────────────────────────────────
            case CognitiveArchitecture.Individuated:
                Header("Military & Governance", w, ref y);
                // "Military budget" readout deleted along with SectorMilitary (era3-systems-
                // implementation-spec §2) — see Economic > Labor Allocation's real Military sector.
                // Domestic Security retired as a free slider — was unconsumed anywhere but its own
                // slider; see PolCat > Order Doctrine's Garrison State/Codified Legalism policies.
                Readout("Domestic security", "see PolCat > Order Doctrine", w, ref y);
                civ.DiplomaticPosture  = Dial("Diplomatic posture  (isolationist ↔ expansive)",
                    civ.DiplomaticPosture, w, ref y);
                Readout("Government",  civ.Government.ToString(), w, ref y);
                Readout("Social structure", civ.SocialStructure == SocialStructureType.Unset
                    ? "—" : civ.SocialStructure.ToString(), w, ref y);
                break;

            // ── Distributed ──────────────────────────────────────────────────
            case CognitiveArchitecture.Distributed:
                Header("Network Topology (= Government)", w, ref y);
                // Topology slider IS the government dial for Distributed.
                float prevTop = civ.NetworkTopologySlider;
                civ.NetworkTopologySlider = Dial("Mesh ↔ Hub-centralised", civ.NetworkTopologySlider, w, ref y);
                // Update GovernmentType from slider.
                civ.Government = civ.NetworkTopologySlider >= 0.6f
                    ? GovernmentType.HubNetwork : GovernmentType.MeshNetwork;
                Readout("Current topology", civ.Government.ToString(), w, ref y);
                Readout("Colony scale",
                    $"Tier {civ.NetworkConnectivityTier}  (caps available topology options)", w, ref y);

                // Representative framing (addendum §3.3).
                DrawRepBlock(mgr, w, ref y);
                break;

            // ── Collective ───────────────────────────────────────────────────
            case CognitiveArchitecture.Collective:
                Header("Command Structure (= Government)", w, ref y);
                civ.CommandCentralization = Dial("Nest-cluster ↔ Single-queen",
                    civ.CommandCentralization, w, ref y);
                civ.Government = civ.CommandCentralization >= 0.6f
                    ? GovernmentType.SingleQueen : GovernmentType.NestCluster;
                Readout("Current structure", civ.Government.ToString(), w, ref y);

                // Monogyne: queen succession is high-stakes recurring card.
                Readout("Reproductive mode", civ.RepMode.ToString()
                    + (civ.RepMode == ReproductiveMode.Monogyne
                       ? "  — queen succession is high-stakes" : "  — succession rarely existential"), w, ref y);

                // Representative framing.
                DrawRepBlock(mgr, w, ref y);
                break;
        }

        // Domain investment (all archs).
        Header("War Domains", w, ref y);
        ColorBar("Kinetic",       civ.DomainKinetic,       new Color(0.9f, 0.35f, 0.2f), w, ref y);
        ColorBar("Biochemical",   civ.DomainBiochemical,   new Color(0.4f, 0.85f, 0.3f), w, ref y);
        ColorBar("Informational", civ.DomainInformational, new Color(0.3f, 0.7f,  1f),   w, ref y);
        ColorBar("Economic",      civ.DomainEconomic,      new Color(1f,   0.8f,  0.2f), w, ref y);

        DrawWarfare(civ, mgr, w, ref y);

        DrawCards(4, civ, mgr, w, ref y);
    }

    // ── Warfare (era3-warfare-mechanics-spec) — conscious declare-war/peace, force/upkeep,
    // and target-subsystem selection, replacing the old fully-automatic war behavior ───────────
    private static readonly Era3Warfare.WarSubsystem[] SubsystemChoices =
        { Era3Warfare.WarSubsystem.Population, Era3Warfare.WarSubsystem.Military,
          Era3Warfare.WarSubsystem.Production, Era3Warfare.WarSubsystem.Structures };

    private void DrawWarfare(CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        y += 4f;
        Header("Warfare", w, ref y);
        Readout("Phase", Era3Warfare.IsStandingForcePhase(civ) ? "Standing Force (post-writing)" : "Levy (pre-writing — adjacency only)", w, ref y);
        Readout("Projection range", $"{civ.ProjectionRange:F1}  (~{civ.ProjectionRange * Era3Warfare.ProjectionRangeWorldScale:F0} world units)", w, ref y);
        if (Era3Warfare.IsStandingForcePhase(civ))
        {
            Readout("Standing force", $"{civ.StandingForce:F1}  /  max {Era3Warfare.ComputeMaxSustainableForce(civ):F1}", w, ref y);
            ReadoutColored("Upkeep", $"-{civ.UpkeepCost:F2} stockpile/tick", civ.UpkeepCost > 0.3f ? new Color(1f, 0.5f, 0.3f) : new Color(0.8f, 0.8f, 0.8f), w, ref y);
        }
        if (civ.WarVariationSuppression > 0.01f)
            ReadoutColored("Cultural cost", $"-{civ.WarVariationSuppression:P0} variation (armies conform)", new Color(1f, 0.6f, 0.3f), w, ref y);

        if (!civ.IsPlayer) return; // declare/peace/subsystem controls are player-only, mirrors TryVassalize's scope

        y += 2f;
        Header("Target Subsystem  (what a Declare War / Covert Strike action damages)", w, ref y);
        float bw = w / SubsystemChoices.Length;
        for (int i = 0; i < SubsystemChoices.Length; i++)
        {
            bool sel = civ.WarTargetSubsystem == SubsystemChoices[i];
            if (GUI.Button(new Rect(i * bw, y, bw, Row), SubsystemChoices[i].ToString(), sel ? _tabOn : _choiceBtn))
                civ.WarTargetSubsystem = SubsystemChoices[i];
        }
        y += Row + 4f;

        // Every per-civ action (Declare War, Sue for Peace, Covert Strike, and the rest of the
        // diplomatic toolkit) lives in ONE place — Polity > Relations — instead of scattered flows.
        int atWarCount = 0;
        foreach (var other in mgr.AllCivsView)
            if (other != civ && !other.HasCollapsed && mgr.IsAtWar(civ.CommunityId, other.CommunityId)) atWarCount++;
        Readout("Status", atWarCount == 0 ? "at peace" : $"at war with {atWarCount} civ(s)", w, ref y);
        Readout("Actions", "see Polity > Relations for Declare War / Sue for Peace / Covert Strike / etc.", w, ref y);
    }

    // ── Representative block (addendum §3.3) ──────────────────────────────────

    private void DrawRepBlock(Era3Manager mgr, float w, ref float y)
    {
        if (mgr.NpcCivs.Count == 0) return;
        y += 4f;
        Sub("Representatives", w, ref y);
        foreach (var npc in mgr.NpcCivs)
        {
            string rep = npc.Architecture switch
            {
                CognitiveArchitecture.Distributed => $"Locutus of {npc.Name}",
                CognitiveArchitecture.Collective  => $"Ambassador-caste of {npc.Name}",
                _                                 => npc.Name,
            };
            GUI.Label(new Rect(0f, y, w, Row), rep, _repStyle);
            y += Row;
        }
        y += 2f;
    }

    // ── Screen-center decision popup ────────────────────────────────────────────
    // The no-mediation ecological paths (Terraformer/BloomFront/ApexPredator, and LivingReef
    // outside Symbiotic Integration) have no Cards at all (era3-ecological-paths-spec §1) — nothing
    // to pop up for them; their choices live entirely in the always-reselectable Policy tab.
    private void DrawPendingCardPopup(CivilizationState civ, Era3Manager mgr)
    {
        if (civ.Path != Era3Path.CommerceEngine) return;

        Card? pendingNullable = null;
        foreach (var card in _cards)
        {
            if (civ.Has(card.Id)) continue;
            if (_dismissed.Contains(card.Id)) continue;
            if (!card.IsEligible(civ)) continue;
            pendingNullable = card;
            break;
        }
        if (pendingNullable == null) return;
        Card c = pendingNullable.Value;

        const float w = 460f, pad = 16f;
        float h = pad * 2f + Row + 4f + Row * 2f + 4f + c.ChoiceLabels.Length * (Row + 4f);
        Rect box = new Rect((Screen.width - w) / 2f, Screen.height * 0.28f, w, h);
        GUI.Box(box, GUIContent.none, c.IsCrisis ? _crisisCard : _card);

        float px = box.x + pad, py = box.y + pad;
        GUI.Label(new Rect(px, py, w - pad * 2f - 30f, Row), c.Title, _hdr);
        if (GUI.Button(new Rect(box.x + w - pad - 26f, py, 26f, Row), "×", _dimBtn))
        {
            _dismissed.Add(c.Id); // same dismissal the in-panel card already supports
            return;
        }
        py += Row + 4f;

        GUI.Label(new Rect(px, py, w - pad * 2f, Row * 2f), c.Dilemma, _dim);
        py += Row * 2f + 4f;

        for (int i = 0; i < c.ChoiceLabels.Length; i++)
        {
            bool available = ChoiceAvailable(c, civ, i);
            string hint = i < c.ChoiceHints.Length
                ? $"  <color=#5a6a7a>({c.ChoiceHints[i]})</color>" : "";
            string label = c.ChoiceLabels[i] + hint + (available ? "" : "  <color=#806040>[locked]</color>");
            if (GUI.Button(new Rect(px, py, w - pad * 2f, Row), label, available ? _choiceBtn : _dimBtn) && available)
            {
                c.Apply(mgr, i); // resolved right here — same Apply the in-panel button calls
                return;
            }
            py += Row + 4f;
        }
    }

    // ── Fading event announcement banner (mirrors EraManager's era-transition flash) ───────────
    private void DrawEventFlashBanner(Era3Manager mgr)
    {
        if (!mgr.EventFlashActive) return;
        var style = new GUIStyle(GUI.skin.label)
        { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        style.normal.textColor = new Color(1f, 0.92f, 0.75f, mgr.EventFlashAlpha);
        GUI.Label(new Rect(0f, 64f, Screen.width, 26f), $"── {mgr.LastEventFlashText} ──", style);
    }

    // ── Event log strip ────────────────────────────────────────────────────────

    private void DrawEventLog(Era3Manager mgr, float w, ref float y)
    {
        y += 4f;
        Sub("Recent Events", w, ref y);
        var log = mgr.EventLog;
        int start = Mathf.Max(0, log.Count - 4);
        for (int i = start; i < log.Count; i++)
        {
            var (t, msg) = log[i];
            GUI.Label(new Rect(0f, y, w, Row - 2f),
                $"<color=#666666>{t:F0}s</color>  {msg}", _dim);
            y += Row - 2f;
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CARD RENDERING
    // ══════════════════════════════════════════════════════════════════════════

    private void DrawCards(int tab, CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        bool anyCard = false;
        foreach (var card in _cards)
        {
            if (card.Tab != tab) continue;
            if (civ.Has(card.Id)) continue;
            if (_dismissed.Contains(card.Id)) continue;
            if (!card.IsEligible(civ)) continue;

            if (!anyCard) { y += 6f; Sub("Decisions", w, ref y); anyCard = true; }
            DrawCard(card, civ, mgr, w, ref y);
        }
    }

    private void DrawCard(Card card, CivilizationState civ, Era3Manager mgr, float w, ref float y)
    {
        float cardH = Row + 18f + card.ChoiceLabels.Length * (Row + 2f);
        var style = card.IsCrisis ? _crisisCard : _card;
        GUI.Box(new Rect(-2f, y - 2f, w + 4f, cardH + 8f), GUIContent.none, style);

        GUI.Label(new Rect(0f, y, w - 30f, Row), card.Title, _sub);
        if (GUI.Button(new Rect(w - 28f, y, 28f, 17f), "×", _dimBtn))
        {
            _dismissed.Add(card.Id);
            return;
        }
        y += Row;

        GUI.Label(new Rect(0f, y, w, Row - 2f), card.Dilemma, _dim);
        y += Row;

        for (int i = 0; i < card.ChoiceLabels.Length; i++)
        {
            bool available = ChoiceAvailable(card, civ, i);
            string hint  = i < card.ChoiceHints.Length
                ? $"  <color=#5a6a7a>({card.ChoiceHints[i]})</color>" : "";
            string label = card.ChoiceLabels[i] + hint + (available ? "" : "  <color=#806040>[locked]</color>");

            if (GUI.Button(new Rect(0f, y, w, Row), label, available ? _choiceBtn : _dimBtn) && available)
            {
                int idx = i;
                card.Apply(mgr, idx);
                _dismissed.Remove(card.Id);
            }
            y += Row + 2f;
        }
        y += 8f;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // CARD DEFINITIONS
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildCards()
    {
        _cards = new List<Card>
        {
            // ── ECONOMIC (0) ──────────────────────────────────────────────────

            // d3_trade_policy ("Trade Policy" card) deleted (era3-systems-implementation-spec §4) —
            // redundant with Policy Catalog's Economic-Foreign slot, which covers the same openness/
            // tariff axis through the gated system. SetTradePolicy itself is untouched — Large
            // Initiative's "Trade Expansion" choice still calls it directly.

            new Card
            {
                Id = "d3_settlement_admission_policy", Tab = 0,
                Title   = "Settlement Admission",
                Dilemma = "Keep settlements species-locked, or open them to other intelligent species?",
                ChoiceLabels = new[] { "Species-Locked Settlements", "Open Multispecies Settlements" },
                ChoiceHints  = new[] { "own species only — cohesive, slower growth", "any recognized species — faster growth, cohesion cost" },
                // era3-tech-idea-trees-spec §5: I4b (Federated Sovereignty) is named explicitly as
                // "fine-grained multi-species minority-status governance (population-roster
                // granularity)" — exactly this decision. Was entirely ungated before. I3c (Formal
                // Diplomacy Norms — the prereq immediately below I4b) is accepted as an earlier,
                // still-real fallback rather than making multispecies admission a pure Tier-4 wait.
                IsEligible = civ => civ.Has("e3_permanent_settlement") && !civ.Has("d3_settlement_admission_policy")
                    && (civ.UnlockedNodes.Contains("I4b") || civ.UnlockedNodes.Contains("I3c")),
                Apply = (mgr, i) => {
                    // MECHANICAL effect (not just flavor): this flag directly gates which organisms
                    // TickSettlementAbsorption is allowed to fold into this civ's settlements. Locked
                    // caps growth to the founding species' own local reproduction; Multispecies opens
                    // absorption to any nearby member of another recognized civ, a real population/
                    // growth-rate advantage — traded off against a cultural-cohesion cost below.
                    mgr.PlayerCiv.MultispeciesSettlements = (i == 1);
                    if (i == 0) mgr.PlayerCiv.InvestReligion = Mathf.Min(mgr.PlayerCiv.InvestReligion + 0.08f, 1f);
                    else
                    {
                        mgr.PlayerCiv.InvestEconomic = Mathf.Min(mgr.PlayerCiv.InvestEconomic + 0.10f, 1f);
                        mgr.PlayerCiv.InvestReligion = Mathf.Max(mgr.PlayerCiv.InvestReligion - 0.05f, 0f);
                    }
                    mgr.OnDecisionResolved("d3_settlement_admission_policy");
                }
            },

            // d3_formal_currency retired (era3-track-parity-gating-spec §1.2): its effect
            // (DomainEconomic += 0.15) is a strict subset of ind_prod_market, the Policy Catalog's
            // own I3d-gated option — no replacement needed, Individuated already gets the real payoff
            // through the gated system once it earns Currency (I3d).

            new Card  // Distributed — formalise graft-link.
            {
                Id = "d3_graft_link_treaty", Tab = 0,
                Title   = "Formalise Graft-Link",
                Dilemma = "Make the trade connection permanent?",
                ChoiceLabels = new[] { "Permanent graft-link treaty", "Keep informal contact" },
                ChoiceHints  = new[] { "exchange rate locked, resilience bond", "flexibility, easier to sever" },
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Distributed
                    && civ.Has("e3_trade_network"),
                // era3-track-parity-gating-spec §1.3: gate the upgrade (formalizing), not the floor
                // (tacit contact remains always available per the tacit-exchange-vs-formal-trade
                // principle). I3c is literally Formal Graft-Treaty Norms.
                ChoiceGate = (civ, i) => i != 0 || civ.UnlockedNodes.Contains("I3c"),
                Apply = (mgr, i) => {
                    // era3-systems-implementation-spec §2: GraftCompatThreshold (tight↔permissive)
                    // scales how strong a bond the formalized link forms — more permissive exchange
                    // integrates deeper, for a bigger resilience bond.
                    if (i == 0) { mgr.PlayerCiv.FormalTradeActive = true; mgr.PlayerCiv.RecoverResilience(0.03f + mgr.PlayerCiv.GraftCompatThreshold * 0.05f); }
                    mgr.OnDecisionResolved("d3_graft_link_treaty");
                }
            },

            // era3-systems-implementation-spec §8: Large Initiative rebuilt — now universal across
            // all five tracks (previously Commerce Engine + Living Reef only), gated by a shared
            // "Administrative Centralization" concept (I4b, track-flavored — same pattern already
            // used for domestication/host-guest-tolerance). Structure: click to commit, 30-year
            // (6-tick) duration with an ongoing cost each tick (Era3Manager.TickLargeInitiative +
            // the per-track sites it's wired into — CivilizationEconomy.Tick, TickCohortGroup,
            // Era3EcologicalPaths.TickRunawayRisk, Era3Warfare.ComputeMaxSustainableForce), then a
            // permanent bonus on completion. One card per track (civ.Path is fixed for the whole
            // game, so exactly one of these five is ever eligible for a given civ) — a single
            // Commit/Not-yet choice rather than a menu, since each track's initiative is a named,
            // fixed package now, not a player-chosen effect.
            new Card
            {
                Id = "d3_large_initiative_commerce", Tab = 0,
                Title   = "Large Initiative: Great Public Works",
                Dilemma = "Commit to a 30-year public works program? Economic output dips while it's underway, for a lasting boost once complete.",
                ChoiceLabels = new[] { "Commit", "Not yet" },
                ChoiceHints  = new[] { "-15% Economic output for 30 years, then +15% permanent", "no change" },
                IsEligible = civ => civ.Path == Era3Path.CommerceEngine
                    && civ.UnlockedNodes.Contains("I4b") && !civ.LargeInitiativeActive && !civ.LargeInitiativeCompleted,
                Apply = (mgr, i) => {
                    if (i == 0) mgr.TryStartLargeInitiative(0);
                    mgr.OnDecisionResolved("d3_large_initiative_commerce");
                }
            },
            new Card
            {
                Id = "d3_large_initiative_apex", Tab = 0,
                Title   = "Large Initiative: Coordinated Territory Network",
                Dilemma = "Commit the pack's territory to a coordinated network? Food reserves are taxed while it's underway, for a lasting mobilization boost once complete.",
                ChoiceLabels = new[] { "Commit", "Not yet" },
                ChoiceHints  = new[] { "biomass tax for 30 years, then +20% MaxSustainableForce permanent", "no change" },
                IsEligible = civ => civ.Path == Era3Path.ApexPredator
                    && civ.UnlockedNodes.Contains("I4b") && !civ.LargeInitiativeActive && !civ.LargeInitiativeCompleted,
                Apply = (mgr, i) => {
                    if (i == 0) mgr.TryStartLargeInitiative(0);
                    mgr.OnDecisionResolved("d3_large_initiative_apex");
                }
            },
            new Card
            {
                Id = "d3_large_initiative_reef", Tab = 0,
                Title   = "Large Initiative: Colony Fusion Event",
                Dilemma = "Commit to fusing colonial growth into one coordinated event? Carrying capacity growth slows while it's underway, for a lasting boost once complete.",
                ChoiceLabels = new[] { "Commit", "Not yet" },
                ChoiceHints  = new[] { "-15% K_effective growth for 30 years, then +15% permanent", "no change" },
                IsEligible = civ => civ.Path == Era3Path.LivingReef
                    && civ.UnlockedNodes.Contains("I4b") && !civ.LargeInitiativeActive && !civ.LargeInitiativeCompleted,
                Apply = (mgr, i) => {
                    if (i == 0) mgr.TryStartLargeInitiative(0);
                    mgr.OnDecisionResolved("d3_large_initiative_reef");
                }
            },
            new Card
            {
                Id = "d3_large_initiative_terraformer", Tab = 0,
                Title   = "Large Initiative: Planetary Chemistry Cascade",
                Dilemma = "Commit to a planet-scale chemistry cascade? Runaway risk accumulates twice as fast while it's underway, for a lasting Environment-sector boost once complete.",
                ChoiceLabels = new[] { "Commit", "Not yet" },
                ChoiceHints  = new[] { "2x RunawayExposure accumulation for 30 years, then permanent Environment output boost", "no change" },
                IsEligible = civ => civ.Path == Era3Path.Terraformer
                    && civ.UnlockedNodes.Contains("I4b") && !civ.LargeInitiativeActive && !civ.LargeInitiativeCompleted,
                Apply = (mgr, i) => {
                    if (i == 0) mgr.TryStartLargeInitiative(0);
                    mgr.OnDecisionResolved("d3_large_initiative_terraformer");
                }
            },
            new Card
            {
                Id = "d3_large_initiative_bloomfront", Tab = 0,
                Title   = "Large Initiative: Mass Synchronized Bloom",
                Dilemma = "Commit to a synchronized mass bloom? Reproduction slows while it's underway, for an immediate biomass surge plus a lasting carrying-capacity boost once complete.",
                ChoiceLabels = new[] { "Commit", "Not yet" },
                ChoiceHints  = new[] { "-20% PopGrowth for 30 years, then a biomass surge + small permanent K_effective boost", "no change" },
                IsEligible = civ => civ.Path == Era3Path.BloomFront
                    && civ.UnlockedNodes.Contains("I4b") && !civ.LargeInitiativeActive && !civ.LargeInitiativeCompleted,
                Apply = (mgr, i) => {
                    if (i == 0) mgr.TryStartLargeInitiative(0);
                    mgr.OnDecisionResolved("d3_large_initiative_bloomfront");
                }
            },

            // ── GENETIC/BIOLOGICAL (1) ────────────────────────────────────────

            // d3_caste_labor ("Labor Allocation" card) deleted (era3-systems-implementation-spec §4)
            // — wrote to SectorMilitary/CasteForager/Builder/Soldier, all now deleted; redundant with
            // the real Policy Sectors block, renamed "Labor Allocation" in the Economic tab to inherit
            // this card's name. Its GeneCatalog.cs duplicate (same Id) is one of the 8 confirmed-dead
            // orphaned entries, deleted separately.

            new Card  // Bring a local wild population into managed production.
            {
                Id = "d3_domesticate_species", Tab = 1,
                Title   = "Domesticate a Species",
                Dilemma = "Bring a wild species into managed production?",
                ChoiceLabels = new[] { "Domesticate (herd/crop)", "Leave wild" },
                ChoiceHints  = new[] { "cohort moves to the top extraction tier", "no change" },
                // domestication-spec.md §1: "two separate gates, same underlying mechanic" —
                // I_domestication (Commerce Engine, Era3TechTree) or A_domestication (Living Reef/
                // Terraformer/Bloom Front/Apex Predator, Era3AdaptationTree). Previously Individuated-
                // only and gated on T1c, which had nothing to do with domestication specifically —
                // broadened to every track via the real dedicated gate, per the spec's own dual-gate
                // table (§1).
                IsEligible = civ => civ.Has("e3_agriculture") && Era3Manager.HasDomesticationGate(civ),
                Apply = (mgr, i) => {
                    if (i == 0) {
                        var target = mgr.FindOrSeedDomesticationTarget(mgr.PlayerCiv);
                        if (target != null) mgr.DomesticateCohort(target, mgr.PlayerCiv.CommunityId);
                    }
                    mgr.OnDecisionResolved("d3_domesticate_species");
                }
            },

            // d3_symbiotic_defender retired (era3-track-parity-gating-spec §1.5): near-duplicate of
            // dis_prop_symbiotic (same DomainBiochemical effect, same T3a gate, both Distributed-
            // only) — its effect folds into that Policy Catalog option, no replacement needed.

            new Card  // Plague crisis — all archs, triggered by Era3Manager crisis system.
            {
                Id = "d3_plague_response", Tab = 1, IsCrisis = true,
                Title   = "⚠ Plague / Pandemic",
                Dilemma = "How does the civilisation respond?",
                ChoiceLabels = new[] { "Quarantine — restrict movement", "Treat — emergency response", "Ignore — accept losses" },
                ChoiceHints  = new[] { "isolates spread, trade penalty", "immediate resilience relief", "resilience drain continues" },
                IsEligible = civ => civ.Has("e3_plague_active"),
                Apply = (mgr, i) => {
                    var c = mgr.PlayerCiv;
                    switch (i) {
                        case 0: c.ForeignOpenness = Mathf.Max(c.ForeignOpenness - 0.20f, 0f); c.RecoverResilience(0.08f); break;
                        // Public Health Investment is retired as a directly-settable field (era3-
                        // adaptation-trees-spec §1.1) — ongoing plague resistance is now the gated
                        // Policy Catalog's job (GenDMin); this crisis-response choice still gives its
                        // own immediate one-time relief.
                        case 1: c.RecoverResilience(0.10f); break;
                        // case 2: no change — resilience drain from auto-events continues
                    }
                    mgr.OnDecisionResolved("d3_plague_response");
                }
            },

            // ── INFORMATIONAL (2) ─────────────────────────────────────────────

            new Card
            {
                Id = "d3_idea_patronage", Tab = 2,
                Title   = "Idea Patronage",
                Dilemma = "What does the ruling class invest in?",
                ChoiceLabels = new[] { "Culture", "Religion", "Science", "Military" },
                ChoiceHints  = new[] { "oral tradition, legitimacy", "tier-3 belief unlock", "+info investment", "+kinetic domain" },
                // era3-track-parity-gating-spec §2.3: gated on I2a ("someone has the standing to
                // direct patronage" — Chieftaincy/Hub-Node/Queen-Founder/Founder-Colony Precedence).
                // Confirmed safe via code audit: civ.IdeaPatronage has no other reader anywhere in
                // the codebase besides SetIdeaPatronage's own OnFire-style side effects, so there's
                // no unsafe hard-gate at risk of being stranded. I2a's channel is Coercive, which
                // Living Reef's thin Idea slice includes — Terraformer/BloomFront/ApexPredator can
                // never unlock ANY Idea-tree node (Idea tree doesn't apply to them at all), so this
                // gate alone already excludes them without a separate Path check.
                IsEligible = civ => civ.Has("e3_chiefdom") && civ.UnlockedNodes.Contains("I2a"),
                Apply = (mgr, i) => {
                    var t = i switch { 0 => IdeaPatronageType.Culture, 1 => IdeaPatronageType.Religion,
                                       2 => IdeaPatronageType.Science, _ => IdeaPatronageType.Military };
                    mgr.SetIdeaPatronage(0, t);
                    mgr.OnDecisionResolved("d3_idea_patronage");
                }
            },

            // d3_writing_adoption retired (era3-track-parity-gating-spec §1.7): this card's whole
            // effect was "you got Writing (I2b), here's the bonus" — Individuated, Distributed, and
            // Collective already have an I2b-gated Policy Catalog option (ind_know_scribal/
            // dis_know_protocol/col_know_encoded). The one-time bonus now folds directly into I2b
            // acquisition (see Era3Manager.OnNodeUnlocked's "I2b" case, Individuated-only).

            new Card  // Distributed: kin-recognition-breaking (SIGINT equivalent §6.3).
            {
                Id = "d3_kin_recognition_break", Tab = 2,
                Title   = "Kin-Recognition Disruption",
                Dilemma = "Develop chemical SIGINT against neighbours?",
                ChoiceLabels = new[] { "Develop KRB tech", "Abstain" },
                ChoiceHints  = new[] { "+detection cap, depletes neighbour trust", "no change" },
                // era3-track-parity-gating-spec §1.8: gated on I2d (Network-Kin Affinity — the direct
                // inverse of what this card breaks). Track-specific, no cross-track equivalent needed.
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Distributed
                    && civ.SignalBandwidthTier >= 1
                    && civ.Has("e3_trade_network")
                    && civ.UnlockedNodes.Contains("I2d"),
                Apply = (mgr, i) => {
                    if (i == 0) {
                        var c = mgr.PlayerCiv;
                        c.DetectionCapability = Mathf.Min(c.DetectionCapability + 0.20f, 1f);
                        // Lowers trade_health with all partners slightly (trust cost).
                        foreach (var npc in mgr.NpcCivs)
                            if (c.TradeHealth.ContainsKey(npc.CommunityId))
                                c.TradeHealth[npc.CommunityId] = Mathf.Max(c.TradeHealth[npc.CommunityId] - 0.10f, 0f);
                    }
                    mgr.OnDecisionResolved("d3_kin_recognition_break");
                }
            },

            new Card  // Collective: cascade-error risk mitigation (gated by DecisionVelocity.Slow).
            {
                Id = "d3_cascade_error_mitigation", Tab = 2,
                Title   = "Cascade-Error Risk",
                Dilemma = "Slow colony decisions create error cascades. Invest to mitigate?",
                ChoiceLabels = new[] { "Structural redundancy", "Accept the risk" },
                ChoiceHints  = new[] { "+stigmergic bandwidth, resilience buffer", "no investment" },
                // era3-track-parity-gating-spec §1.9: gated on T3d (Stigmergic Disruption) rather
                // than an Idea node — it directly matches the affected stat (StigmergicBandwidth) and
                // channel (Informational), and T3d already exists for Collective. Track-specific; no
                // cross-track equivalent needed (stigmergic communication is Collective's signature).
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Collective
                    && civ.DecVelocity == DecisionVelocity.Slow
                    && civ.UnlockedNodes.Contains("T3d"),
                Apply = (mgr, i) => {
                    if (i == 0) {
                        var c = mgr.PlayerCiv;
                        c.StigmergicBandwidth = Mathf.Min(c.StigmergicBandwidth + 0.15f, 1f);
                        c.RecoverResilience(0.06f);
                    }
                    mgr.OnDecisionResolved("d3_cascade_error_mitigation");
                }
            },

            // ── EXISTENTIAL (3) ───────────────────────────────────────────────

            new Card
            {
                Id = "d3_kinship_policy", Tab = 3,
                Title   = "Kinship Norms",
                Dilemma = "Tight households or broad kin coalitions?",
                ChoiceLabels = new[] { "Nuclear", "Extended", "Clan", "CrossLineage" },
                ChoiceHints  = new[] { "tight unit, internal cohesion", "broader kin, moderate openness", "coalitions, factionalism risk", "intermarriage, trade openness" },
                // era3-systems-implementation-spec follow-up correction: retargeted from the
                // timer-based e3_family_norms_emerge to I1a (Kinship Custom) — a real earned gate
                // instead of an elapsed-time one. The 4 choices themselves are untouched: each
                // drives a genuinely distinct value through Era3Diplomacy.KinBias (0.9/0.6/0.7/0.2),
                // a real working strategic axis, not a decorative flag — this was never eligible for
                // auto-apply-on-completion despite being filed there.
                IsEligible = civ => civ.UnlockedNodes.Contains("I1a"),
                Apply = (mgr, i) => {
                    var p = i switch { 0 => KinshipPolicy.Nuclear, 1 => KinshipPolicy.Extended,
                                       2 => KinshipPolicy.Clan,    _ => KinshipPolicy.CrossLineage };
                    mgr.SetKinship(0, p);
                    mgr.OnDecisionResolved("d3_kinship_policy");
                }
            },

            new Card  // Individuated only: found organised religion (tier 3).
            {
                Id = "d3_found_organized_religion", Tab = 3,
                Title   = "Found Organised Religion",
                Dilemma = "Formalise cosmological belief into institutions?",
                ChoiceLabels = new[] { "Establish church / temple", "Remain folk / diffuse" },
                ChoiceHints  = new[] { "tier-3 belief, theocracy path, proselytise unlocked", "lower legitimacy ceiling" },
                // era3-track-parity-gating-spec §2.4: gated on I2c (Cosmology) — Individuated's
                // Tier-2 Existential node, exactly "you now have a formalized belief structure."
                // Safe: d3_schism_response only fires once HasOrganizedReligion is already true, so
                // delaying founding until I2c doesn't strand the crisis chain.
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Individuated
                    && civ.BeliefTier >= 2 && !civ.HasOrganizedReligion
                    && civ.Has("e3_religion_organized")
                    && civ.UnlockedNodes.Contains("I2c"),
                Apply = (mgr, i) => {
                    if (i == 0) {
                        var c = mgr.PlayerCiv;
                        c.HasOrganizedReligion = true;
                        c.BeliefTier = 3;
                    }
                    mgr.OnDecisionResolved("d3_found_organized_religion");
                }
            },

            new Card  // Individuated: schism response (crisis, gated by HasOrganisedReligion).
            {
                Id = "d3_schism_response", Tab = 3, IsCrisis = true,
                Title   = "⚠ Religious Schism",
                Dilemma = "A competing doctrine has split your population.",
                ChoiceLabels = new[] { "Suppress the schism", "Accommodate — allow pluralism", "Embrace — shift doctrine" },
                ChoiceHints  = new[] { "+coercive, resilience risk, -openness", "-orthodoxy, +stability", "replaces dominant doctrine" },
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Individuated
                    && civ.HasOrganizedReligion && civ.Has("e3_schism_active"),
                Apply = (mgr, i) => {
                    var c = mgr.PlayerCiv;
                    switch (i) {
                        case 0: c.OrthodoxyLevel = Mathf.Min(c.OrthodoxyLevel + 0.20f, 1f); c.DrainResilience(0.05f); break;
                        case 1: c.OrthodoxyLevel = Mathf.Max(c.OrthodoxyLevel - 0.20f, 0f); c.RecoverResilience(0.04f); break;
                        case 2: c.InvestReligion = Mathf.Min(c.InvestReligion + 0.10f, 1f); break;
                    }
                    mgr.OnDecisionResolved("d3_schism_response");
                }
            },

            new Card  // Government transition (moved to Existential — kinship/polity overlap).
            {
                Id = "d3_government_transition", Tab = 3,
                Title   = "Government Form",
                Dilemma = "How does power concentrate or distribute?",
                ChoiceLabels = new[] { "Monarchy / Hub / Queen", "Oligarchy / Mesh / Cluster", "Democracy", "Theocracy" },
                ChoiceHints  = new[] { "concentrated, fast decisions", "distributed power", "broad vote, stability", "sacred authority — needs religion" },
                // era3-tech-idea-trees-spec §5: I3a (Codified Law) gates government-form choice — was
                // a genuine bug, not a design gap: this card is a SEPARATE definition from
                // GeneCatalog.cs's own "d3_government_transition" (same id, two independent
                // resolution paths), and only the GeneCatalog copy had ever been gated. This one,
                // with only the much-earlier e3_social_stratification check, was firing first and
                // handing out free government choice regardless. Same I3a/e3_state_formation
                // fallback as the GeneCatalog copy and every other tech-tree safety valve this session.
                IsEligible = civ => civ.Has("e3_social_stratification")
                    && (civ.UnlockedNodes.Contains("I3a") || civ.Has("e3_state_formation")),
                Apply = (mgr, i) => {
                    var arch = mgr.PlayerCiv.Architecture;
                    GovernmentType gov = i switch {
                        0 => arch == CognitiveArchitecture.Distributed ? GovernmentType.HubNetwork
                           : arch == CognitiveArchitecture.Collective  ? GovernmentType.SingleQueen
                           :                                              GovernmentType.Monarchy,
                        1 => arch == CognitiveArchitecture.Distributed ? GovernmentType.MeshNetwork
                           : arch == CognitiveArchitecture.Collective  ? GovernmentType.NestCluster
                           :                                              GovernmentType.Oligarchy,
                        2 => GovernmentType.Democracy,
                        _ => GovernmentType.Theocracy,
                    };
                    mgr.SetGovernment(0, gov);
                    mgr.OnDecisionResolved("d3_government_transition");
                }
            },

            // ── COERCIVE (4) ──────────────────────────────────────────────────

            new Card
            {
                Id = "d3_war_or_diplomacy", Tab = 4,
                Title   = "War or Diplomacy?",
                Dilemma = "Expand through force or alliance?",
                ChoiceLabels = new[] { "Organised Warfare", "Diplomacy" },
                ChoiceHints  = new[] { "opens domain investment, +coercive", "alliances, +openness, empire path" },
                IsEligible = civ => civ.Has("e3_state_formation"),
                // era3-systems-implementation-spec §4: Organized Warfare used to grant
                // e3_warfare_organized directly (SetWarPath) — a soft bypass of the real gate, same
                // pattern as the e3_writing/I2b fix earlier this session. Now requires I3a (Command-
                // Structure Codification) already unlocked; T2a's own independent OnNodeUnlocked grant
                // (Era3Manager.cs "T2a" case) is untouched — a real earned alternate path, not a bypass.
                ChoiceGate = (civ, i) => i != 0 || civ.UnlockedNodes.Contains("I3a"),
                Apply = (mgr, i) => {
                    if (i == 0) mgr.SetWarPath(0);
                    else        mgr.SetDiplomacyPath(0);
                    mgr.OnDecisionResolved("d3_war_or_diplomacy");
                }
            },

            new Card
            {
                Id = "d3_domain_investment", Tab = 4,
                Title   = "War Domain",
                Dilemma = "Where does doctrine focus?",
                ChoiceLabels = new[] { "Kinetic", "Biochemical", "Informational", "Economic" },
                ChoiceHints  = new[] { "+conventional force", "+plague/toxin doctrine", "+espionage/disinfo", "+sanctions/leverage" },
                IsEligible = civ => civ.Has("e3_warfare_organized"),
                Apply = (mgr, i) => {
                    float k=0f, b=0f, inf=0f, eco=0f;
                    switch(i){case 0:k=.25f;break;case 1:b=.25f;break;case 2:inf=.25f;break;default:eco=.25f;break;}
                    mgr.ApplyDomainInvestment(0, k, b, inf, eco);
                    mgr.OnDecisionResolved("d3_domain_investment");
                }
            },

            // d3_bioweapon_option retired (era3-track-parity-gating-spec §1.13): confirmed duplicate —
            // ind_bio_bioweapon/col_bio_bioweapon (both T3c) already cover Individuated/Collective;
            // dis_bio_mycotoxin (T3c) covers Distributed under a different name.

            new Card
            {
                Id = "d3_recognize_occupied_territory", Tab = 4,
                Title   = "Recognize Occupied Territory",
                Dilemma = "Your forces hold conquered ground — formalize it, or withdraw?",
                ChoiceLabels = new[] { "Formalize Annexation", "Withdraw" },
                ChoiceHints  = new[] { "occupied territory becomes permanent, solid on the map", "hand it back — dispute ends, no lasting claim" },
                // Note: like every decision in this system, this card resolves once and then stays
                // marked "Has" forever (civ.Has(card.Id) gates re-display) — a SECOND, later war that
                // produces fresh occupied territory won't get its own popup. Consistent with how every
                // other Era 3 decision already behaves (none of them are re-triggerable), not a special
                // limitation introduced here — flag if repeat wars turn out to need their own resolution.
                IsEligible = civ => Era3Manager.Instance != null && Era3Manager.Instance.HasOccupiedTerritory(civ.CommunityId),
                Apply = (mgr, i) => {
                    if (i == 0) mgr.FormalizeOccupiedTerritory(0);
                    else        mgr.WithdrawFromOccupiedTerritory(0);
                    mgr.OnDecisionResolved("d3_recognize_occupied_territory");
                }
            },

            new Card  // Individuated: negotiate treaty (post-war or independent).
            {
                Id = "d3_negotiate_treaty", Tab = 4,
                Title   = "Negotiate Treaty",
                Dilemma = "Formalise a peace or alliance?",
                ChoiceLabels = new[] { "Peace treaty — end hostilities", "Formal alliance", "Decline" },
                ChoiceHints  = new[] { "+trade health, -coercive drain", "permanent alliance active", "no change" },
                // era3-track-parity-gating-spec §2.1: repointed from e3_diplomacy (the flag
                // d3_war_or_diplomacy's Diplomacy choice sets, and the ONLY thing that ever set it)
                // to FormalAllianceActive, which has a real alternate path (Era3Manager.ProposeAlliance)
                // — closes the single-point-of-failure without gating d3_war_or_diplomacy itself.
                IsEligible = civ => civ.FormalAllianceActive && !civ.Has("d3_negotiate_treaty"),
                Apply = (mgr, i) => {
                    var c = mgr.PlayerCiv;
                    switch (i) {
                        case 0: c.RecoverResilience(0.06f); c.ForeignOpenness = Mathf.Min(c.ForeignOpenness + 0.15f, 1f); break;
                        case 1: mgr.SetDiplomacyPath(0); break;
                    }
                    mgr.OnDecisionResolved("d3_negotiate_treaty");
                }
            },

            new Card  // Distributed: sever/reject a graft-link (network war).
            {
                Id = "d3_sever_graft_link", Tab = 4,
                Title   = "Sever Graft-Link",
                Dilemma = "Unilaterally cut an infected or adversarial connection?",
                ChoiceLabels = new[] { "Sever the link", "Maintain — accept risk" },
                ChoiceHints  = new[] { "+resilience short-term, -exchange", "keeps trade, infection or war risk" },
                // era3-track-parity-gating-spec §1.14: gated on I3c — same node as
                // d3_graft_link_treaty's formalization gate; severing a formal link presupposes
                // having had the concept of one.
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Distributed
                    && civ.Has("e3_trade_network")
                    && civ.UnlockedNodes.Contains("I3c"),
                Apply = (mgr, i) => {
                    if (i == 0) {
                        var c = mgr.PlayerCiv;
                        // era3-systems-implementation-spec §2: GraftCompatThreshold's other side — a
                        // civ that was already tight/cautious recovers more by cutting a permissive
                        // link it was warier of holding onto.
                        c.RecoverResilience(0.08f + (1f - c.GraftCompatThreshold) * 0.06f);
                        c.ForeignOpenness = Mathf.Max(c.ForeignOpenness - 0.20f, 0f);
                    }
                    mgr.OnDecisionResolved("d3_sever_graft_link");
                }
            },

            new Card  // Collective: colony raid.
            {
                Id = "d3_colony_raid", Tab = 4,
                Title   = "Colony Raid",
                Dilemma = "Launch a raid on a neighbouring colony?",
                ChoiceLabels = new[] { "Raid — dulosis / resource seizure", "Hold back" },
                ChoiceHints  = new[] { "+stockpile, +kinetic domain, -trade health", "no change" },
                // era3-track-parity-gating-spec §1.15: gated on T2a (Soldier-Caste Doctrine) directly
                // rather than the e3_warfare_organized flag — T2a's own unlock already independently
                // grants that flag (Era3Manager.OnNodeUnlocked's "T2a" case) and is already the gate
                // for col_trade_dulosis/col_dipl_absorption, so this aligns with its Policy-layer
                // siblings instead of accepting the flag via the ungated d3_war_or_diplomacy path too.
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Collective
                    && civ.UnlockedNodes.Contains("T2a"),
                Apply = (mgr, i) => {
                    if (i == 0) {
                        var c = mgr.PlayerCiv;
                        // era3-systems-implementation-spec §6: redirected from Stockpile to Economic output.
                        if (c.Economy != null) c.Economy.Stock[CivilizationEconomy.Industry] += 0.4f;
                        c.DomainKinetic = Mathf.Min(c.DomainKinetic + 0.10f, 1f);
                        foreach (var npc in mgr.NpcCivs)
                            if (c.TradeHealth.ContainsKey(npc.CommunityId))
                                c.TradeHealth[npc.CommunityId] = Mathf.Max(c.TradeHealth[npc.CommunityId] - 0.15f, 0f);
                    }
                    mgr.OnDecisionResolved("d3_colony_raid");
                }
            },

            new Card  // Collective: queen succession crisis (monogyne — recurring).
            {
                Id = "d3_queen_succession", Tab = 4, IsCrisis = true,
                Title   = "⚠ Queen Succession Crisis",
                Dilemma = "The reproductive core is dying. Who succeeds?",
                ChoiceLabels = new[] { "Designated lineage — orderly succession", "Open competition — caste rebellion risk", "Absorb into network — switch to polygyne" },
                ChoiceHints  = new[] { "stable, slower adaptation", "faster adaptation, resilience gamble", "increases stability, lowers vel." },
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Collective
                    && civ.RepMode == ReproductiveMode.Monogyne
                    && civ.Has("e3_succession_active"),
                Apply = (mgr, i) => {
                    var c = mgr.PlayerCiv;
                    switch (i) {
                        case 0: c.RecoverResilience(0.04f); break;
                        case 1: c.DrainResilience(0.08f); c.DecVelocity = DecisionVelocity.Fast; break;
                        case 2: c.RepMode = ReproductiveMode.Polygyne; c.RecoverResilience(0.06f); break;
                    }
                    // Clears succession_active so it can re-trigger from Era3Manager crisis.
                    c.AcquiredEvents.Remove("e3_succession_active");
                    mgr.OnDecisionResolved("d3_queen_succession");
                }
            },

            // ── CRISIS: secession (Coercive, all archs) ───────────────────────
            new Card
            {
                Id = "d3_secession_crisis", Tab = 4, IsCrisis = true,
                Title   = "⚠ Fragmentation / Secession",
                Dilemma = "A peripheral region is breaking away.",
                ChoiceLabels = new[] { "Negotiate autonomy", "Crush it — military response", "Let it go" },
                ChoiceHints  = new[] { "+openness, moderate resilience cost", "+kinetic domain, -trade health", "permanent territory loss, resilience drain" },
                IsEligible = civ => civ.Has("e3_secession_active"),
                Apply = (mgr, i) => {
                    var c = mgr.PlayerCiv;
                    switch (i) {
                        case 0: c.ForeignOpenness = Mathf.Min(c.ForeignOpenness + 0.10f, 1f); c.DrainResilience(0.04f); break;
                        case 1: c.DomainKinetic = Mathf.Min(c.DomainKinetic + 0.10f, 1f); c.DrainResilience(0.07f); break;
                        case 2: c.DrainResilience(0.12f); break;
                    }
                    c.AcquiredEvents.Remove("e3_secession_active");
                    mgr.OnDecisionResolved("d3_secession_crisis");
                }
            },

            // ── CRISIS: succession/continuity (Coercive, all archs) ───────────
            new Card
            {
                Id = "d3_succession_crisis", Tab = 4, IsCrisis = true,
                Title   = "⚠ Succession / Continuity Crisis",
                Dilemma = "Leadership transition has destabilised the state.",
                ChoiceLabels = new[] { "Designate a successor now", "Coalition rule — share power", "Call a popular vote" },
                ChoiceHints  = new[] { "orderly but slow, +stability", "risk of factionalism", "risky, may unlock Democracy" },
                IsEligible = civ => civ.Has("e3_succession_active")
                    && civ.Architecture == CognitiveArchitecture.Individuated,
                Apply = (mgr, i) => {
                    var c = mgr.PlayerCiv;
                    switch (i) {
                        case 0: c.RecoverResilience(0.05f); break;
                        case 1: c.DrainResilience(0.03f); break;
                        case 2: if (UnityEngine.Random.value > 0.5f) mgr.SetGovernment(0, GovernmentType.Democracy); c.DrainResilience(0.06f); break;
                    }
                    c.AcquiredEvents.Remove("e3_succession_active");
                    mgr.OnDecisionResolved("d3_succession_crisis");
                }
            },

            // ── GOLDEN AGE card (Economic/Informational, Coercive tab for visibility) ──
            new Card
            {
                Id = "d3_golden_age_response", Tab = 4,
                Title   = "Golden Age Opportunity",
                Dilemma = "Sustained mutualism is creating a flourishing window — direct the surplus.",
                ChoiceLabels = new[] { "Art & institutions", "Infrastructure expansion", "Military build-up" },
                ChoiceHints  = new[] { "+info, +religion channels", "+economic domain, +stockpile", "+kinetic domain, +coercive" },
                IsEligible = civ => civ.Has("e3_golden_age_active"),
                Apply = (mgr, i) => {
                    var c = mgr.PlayerCiv;
                    switch (i) {
                        case 0: c.InvestInformation = Mathf.Min(c.InvestInformation + 0.10f, 1f); c.InvestReligion = Mathf.Min(c.InvestReligion + 0.08f, 1f); break;
                        case 1: c.DomainEconomic = Mathf.Min(c.DomainEconomic + 0.12f, 1f); if (c.Economy != null) c.Economy.Stock[CivilizationEconomy.Industry] += 0.5f; break; // era3-systems-implementation-spec §6: redirected from Stockpile
                        case 2: c.DomainKinetic = Mathf.Min(c.DomainKinetic + 0.12f, 1f); c.InvestCoercive = Mathf.Min(c.InvestCoercive + 0.08f, 1f); break;
                    }
                    c.AcquiredEvents.Remove("e3_golden_age_active");
                    mgr.OnDecisionResolved("d3_golden_age_response");
                }
            },

            // ── POLITY (5): Administrative Crisis — era3-polity-model-spec §2 ──
            new Card
            {
                Id = "d3_administrative_crisis", Tab = 5, IsCrisis = true,
                Title   = "⚠ Administrative Crisis",
                Dilemma = "Your polity has outgrown its ability to govern itself coherently.",
                ChoiceLabels = new[] { "Decentralize authority", "Reform administration", "Do nothing — risk fragmentation" },
                ChoiceHints  = new[] { "permanent +reach, -resilience", "-splinter pressure now, -stockpile", "pressure keeps building" },
                IsEligible = civ => civ.Has("e3_admin_crisis_active"),
                Apply = (mgr, i) => {
                    var c = mgr.PlayerCiv;
                    switch (i) {
                        case 0:
                            c.DecentralizeBonus += 0.20f; // durable — feeds Era3Polity.ComputeReachCapacity every tick
                            c.SplinterPressure = Mathf.Clamp01(c.SplinterPressure - 0.5f);
                            c.DrainResilience(0.06f);
                            break;
                        case 1:
                            c.SplinterPressure = Mathf.Clamp01(c.SplinterPressure - 0.4f);
                            // era3-systems-implementation-spec §6: redirected from Stockpile to Economic output.
                            if (c.Economy != null) c.Economy.Stock[CivilizationEconomy.Industry] = Mathf.Max(0f, c.Economy.Stock[CivilizationEconomy.Industry] - 0.4f);
                            c.InvestInformation = Mathf.Min(c.InvestInformation + 0.05f, 1f);
                            break;
                        case 2:
                            // No relief — SplinterPressure keeps rising next TickPolity and the crisis
                            // re-fires once it re-crosses AdminCrisisThreshold.
                            break;
                    }
                    c.AcquiredEvents.Remove("e3_admin_crisis_active");
                    mgr.OnDecisionResolved("d3_administrative_crisis");
                }
            },
        };
    }

    // ══════════════════════════════════════════════════════════════════════════
    // DRAW HELPERS
    // ══════════════════════════════════════════════════════════════════════════

    private float Dial(string label, float value, float w, ref float y)
    {
        GUI.Label(new Rect(0f, y, LabelW, Row), label, _lbl);
        float v = GUI.HorizontalSlider(new Rect(LabelW + 4f, y + 6f, SliderW, 14f), value, 0f, 1f);
        GUI.Label(new Rect(LabelW + SliderW + 8f, y, 44f, Row), $"{v:P0}", _dim);
        y += Row;
        return v;
    }

    /// A genuine N-way tradeoff slider set (a labor/sector split) instead of independent 0-1 dials —
    /// dragging one slider proportionally rescales the OTHERS in `values` so the group sums to ~1,
    /// instead of letting every slider sit at 100% simultaneously with nothing enforcing any
    /// relationship between them. This was a real bug, not a display issue: SectorProduction/
    /// SectorMilitary/SectorCulture (and the Collective caste dials) were plain independent Dial()
    /// calls, consumed directly at their raw magnitude in Era3Manager — CivilizationEconomy's
    /// Allocation dials at least normalized internally (only the readout was missing there); these
    /// had no normalization anywhere, so cranking every slider to 100% genuinely triple-counted.
    ///
    /// EU4-style per-slider lock (civ.AllocationLocks, keyed "groupKey:index"): a locked slider is
    /// still directly draggable, but is protected from being rebalanced when a SIBLING slider moves
    /// — that sibling's change is absorbed only by the OTHER unlocked sliders instead. If every
    /// sibling is locked, the slider being dragged is capped at whatever share the locked ones leave
    /// available, rather than silently breaking the group's sum-to-1 invariant.
    private void AllocationDial(CivilizationState civ, string groupKey, string label, float w, ref float y, float[] values, int index)
    {
        string lockKey = $"{groupKey}:{index}";
        civ.AllocationLocks.TryGetValue(lockKey, out bool locked);

        const float LockBtnW = 56f;
        float rowY = y;
        float old = values[index];
        float updated = Dial(label, old, w - LockBtnW - 4f, ref y);

        if (GUI.Button(new Rect(w - LockBtnW, rowY, LockBtnW, Row - 2f), locked ? "Locked" : "Lock", locked ? _tabOn : _dimBtn))
            civ.AllocationLocks[lockKey] = !locked;

        if (Mathf.Approximately(updated, old)) return;
        values[index] = updated;

        bool IsLocked(int i) => civ.AllocationLocks.TryGetValue($"{groupKey}:{i}", out bool li) && li;

        float lockedOthersSum = 0f;
        for (int i = 0; i < values.Length; i++) if (i != index && IsLocked(i)) lockedOthersSum += values[i];

        // Can't grow past whatever share the locked siblings have reserved for themselves.
        float maxAllowed = Mathf.Max(0f, 1f - lockedOthersSum);
        if (values[index] > maxAllowed) values[index] = maxAllowed;

        float remaining = Mathf.Max(0f, 1f - values[index] - lockedOthersSum);
        float unlockedOthersSum = 0f; int unlockedCount = 0;
        for (int i = 0; i < values.Length; i++)
            if (i != index && !IsLocked(i)) { unlockedOthersSum += values[i]; unlockedCount++; }

        if (unlockedOthersSum > 0.0001f)
        {
            float scale = remaining / unlockedOthersSum;
            for (int i = 0; i < values.Length; i++) if (i != index && !IsLocked(i)) values[i] *= scale;
        }
        else if (unlockedCount > 0)
        {
            float even = remaining / unlockedCount;
            for (int i = 0; i < values.Length; i++) if (i != index && !IsLocked(i)) values[i] = even;
        }
    }

    private void Readout(string label, string value, float w, ref float y)
    {
        GUI.Label(new Rect(0f,        y, LabelW,      Row), label, _lbl);
        GUI.Label(new Rect(LabelW+4f, y, w-LabelW-4f, Row), value, _dim);
        y += Row;
    }

    private void ReadoutColored(string label, string value, Color color, float w, ref float y)
    {
        GUI.Label(new Rect(0f, y, LabelW, Row), label, _lbl);
        var prev = GUI.color;
        GUI.color = color;
        GUI.Label(new Rect(LabelW+4f, y, w-LabelW-4f, Row), value, _dim);
        GUI.color = prev;
        y += Row;
    }

    private void ColorBar(string label, float value, Color color, float w, ref float y)
    {
        GUI.Label(new Rect(0f, y, LabelW, Row), label, _lbl);
        float by = y + (Row - BarH) * 0.5f;
        GUI.DrawTexture(new Rect(LabelW+4f, by, SliderW, BarH), Texture2D.grayTexture);
        var prev = GUI.color;
        GUI.color = color;
        float fill = Mathf.Clamp01(value) * SliderW;
        if (fill > 0f) GUI.DrawTexture(new Rect(LabelW+4f, by, fill, BarH), Texture2D.whiteTexture);
        GUI.color = prev;
        GUI.Label(new Rect(LabelW + SliderW + 8f, y, 44f, Row), $"{value:P0}", _dim);
        y += Row;
    }

    private void Header(string text, float w, ref float y)
    {
        y += 4f;
        GUI.Label(new Rect(0f, y, w, Row - 2f), text, _hdr);
        y += Row - 2f;
    }

    private void Sub(string text, float w, ref float y)
    {
        GUI.Label(new Rect(0f, y, w, Row - 3f), text, _sub);
        y += Row - 3f;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // STYLE CONSTRUCTION
    // ══════════════════════════════════════════════════════════════════════════

    private void BuildStyles()
    {
        _panel = Box(new Color(0.07f, 0.07f, 0.09f, 0.94f));

        // 9 tabs now fit in the panel's width at fontSize 11 (previously the panel only ever had to
        // fit 5-6) — text overflowed and got clipped on both sides, reading as garbled fragments
        // ("dstentl" etc.). Shrunk font + shortened labels (TabNames) together instead of just
        // widening the panel arbitrarily, since a much wider panel would start covering other HUD
        // elements at common window sizes.
        _tabOn = new GUIStyle(GUI.skin.button)
        {
            fontStyle = FontStyle.Bold, fontSize = 9, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow,
            normal = { background = Tex(new Color(0.18f, 0.38f, 0.70f, 0.95f)), textColor = Color.white },
            hover  = { background = Tex(new Color(0.22f, 0.44f, 0.78f, 1f)),    textColor = Color.white },
        };
        _tabOff = new GUIStyle(GUI.skin.button)
        {
            fontSize = 9, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow,
            normal = { background = Tex(new Color(0.11f, 0.11f, 0.14f, 0.94f)), textColor = new Color(0.68f, 0.68f, 0.68f) },
            hover  = { background = Tex(new Color(0.16f, 0.16f, 0.20f, 0.94f)), textColor = Color.white },
        };

        _hdr = Label(new Color(0.55f, 0.82f, 1f), 11, FontStyle.Bold);
        _sub = Label(new Color(0.55f, 0.82f, 1f), 10, FontStyle.Bold);
        _lbl = Label(new Color(0.82f, 0.82f, 0.82f), 11, FontStyle.Normal);
        _dim = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true, normal = { textColor = new Color(0.60f, 0.60f, 0.60f) } };
        _nodeLabel = new GUIStyle(_dim) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 9 };

        _card     = Box(new Color(0.12f, 0.15f, 0.22f, 0.96f));
        _crisisCard = Box(new Color(0.22f, 0.10f, 0.10f, 0.96f));

        _choiceBtn = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11, richText = true, alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(8, 8, 2, 2),
            normal = { background = Tex(new Color(0.18f, 0.27f, 0.44f, 0.92f)), textColor = Color.white },
            hover  = { background = Tex(new Color(0.25f, 0.36f, 0.58f, 1f)),    textColor = Color.white },
        };

        _dimBtn = new GUIStyle(GUI.skin.button)
        {
            fontSize = 10,
            normal = { background = Tex(new Color(0.25f, 0.10f, 0.10f, 0.8f)), textColor = new Color(0.9f, 0.5f, 0.5f) },
            hover  = { background = Tex(new Color(0.50f, 0.15f, 0.15f, 1f)),   textColor = Color.white },
        };

        _repStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11, fontStyle = FontStyle.Italic,
            normal = { textColor = new Color(0.92f, 0.76f, 0.42f) },
        };

        _stylesReady = true;
    }

    private static GUIStyle Box(Color c)
    {
        var s = new GUIStyle(GUI.skin.box);
        s.normal.background = Tex(c);
        return s;
    }

    private static GUIStyle Label(Color c, int size, FontStyle style)
    {
        var s = new GUIStyle(GUI.skin.label);
        s.fontSize  = size;
        s.fontStyle = style;
        s.richText  = true;
        s.normal.textColor = c;
        return s;
    }

    private static Texture2D Tex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}
