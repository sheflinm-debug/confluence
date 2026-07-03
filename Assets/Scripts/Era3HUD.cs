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
    private const float PanelW  = 440f;
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
    private readonly HashSet<string> _dismissed = new HashSet<string>();

    // ── Styles ─────────────────────────────────────────────────────────────────
    private GUIStyle _panel, _tabOn, _tabOff, _hdr, _sub, _lbl, _dim,
                     _card, _choiceBtn, _repStyle, _crisisCard, _dimBtn;
    private bool _stylesReady;

    private static readonly string[] TabNames =
        { "Economic", "Genetic/Bio", "Informational", "Existential", "Coercive" };

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
    }

    private List<Card> _cards;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    void Start() => BuildCards();

    void OnGUI()
    {
        if (!_stylesReady) BuildStyles();
        if (Era3Manager.Instance == null || !Era3Manager.Instance.IsActive) return;

        float btnX = Screen.width - 140f;
        if (GUI.Button(new Rect(btnX, 8f, 132f, 24f),
            _open ? "▼ Civilization" : "▲ Civilization"))
            _open = !_open;

        if (!_open) return;

        var mgr = Era3Manager.Instance;
        var civ = mgr.PlayerCiv;

        float px = Screen.width - PanelW - 8f;
        float py = 38f;
        GUI.Box(new Rect(px, py, PanelW, PanelH), GUIContent.none, _panel);

        // Tab bar.
        float tw = PanelW / TabNames.Length;
        for (int i = 0; i < TabNames.Length; i++)
        {
            if (GUI.Button(new Rect(px + i * tw, py, tw, TabH),
                    TabNames[i], i == _activeTab ? _tabOn : _tabOff))
                _activeTab = i;
        }

        // Content clip group.
        float cx = px + PadX;
        float cy = py + TabH + PadY;
        float cw = PanelW - PadX * 2f;
        float ch = PanelH - TabH - PadY * 2f;
        GUI.BeginGroup(new Rect(cx, cy, cw, ch));
        float y = 0f;
        DrawTab(_activeTab, civ, mgr, cw, ref y);
        DrawEventLog(mgr, cw, ref y);
        GUI.EndGroup();
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
        }
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
                // A2 (solitary/manipulative) caps mass-labour dials.
                bool massLabourCapped = civ.Subtrack == IndividuatedSubTrack.A2_SolitaryManipulative;
                if (massLabourCapped)
                    Readout("Agriculture", "capped (solitary lineage)", w, ref y);
                else
                    civ.SectorProduction = Dial("Agriculture / industry", civ.SectorProduction, w, ref y);
                civ.SectorMilitary = Dial("Craft / specialisation", civ.SectorMilitary, w, ref y);
                civ.SectorCulture  = Dial("Trade / services",       civ.SectorCulture,  w, ref y);

                Header("Trade Posture", w, ref y);
                civ.TariffRate      = Dial("Tariff rate",       civ.TariffRate,      w, ref y);
                civ.ForeignOpenness = Dial("Foreign openness",  civ.ForeignOpenness, w, ref y);
                break;

            // ── Distributed ──────────────────────────────────────────────────
            case CognitiveArchitecture.Distributed:
                Header("Routing & Exchange", w, ref y);
                // NetworkConnectivityTier caps partner-choice slot rows.
                int slots = civ.NetworkConnectivityTier + 1;
                Readout("Active trade links", $"{slots} (connectivity tier {civ.NetworkConnectivityTier})", w, ref y);
                civ.ExchangePosture = Dial("Exchange posture  (sanction ↔ reward)", civ.ExchangePosture, w, ref y);
                civ.Stockpile       = Mathf.Max(0f, Dial("Boom/crash target stockpile", Mathf.Clamp01(civ.Stockpile / 5f), w, ref y) * 5f);
                break;

            // ── Collective ───────────────────────────────────────────────────
            case CognitiveArchitecture.Collective:
                Header("Caste Allocation", w, ref y);
                civ.CasteForager = Dial("Forager caste", civ.CasteForager, w, ref y);
                civ.CasteBuilder = Dial("Builder caste", civ.CasteBuilder, w, ref y);
                civ.CasteSoldier = Dial("Soldier caste", civ.CasteSoldier, w, ref y);
                // Trader caste only at Polymorphic differentiation.
                if (civ.CasteDiff == CasteDifferentiation.Polymorphic)
                    civ.CasteTrader = Dial("Trader caste", civ.CasteTrader, w, ref y);
                else
                    Readout("Trader caste", "locked — needs polymorphic castes", w, ref y);

                Header("Biomass Target", w, ref y);
                civ.StockpileTarget = Dial("Stockpile target", civ.StockpileTarget, w, ref y);
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

        DrawCards(0, civ, mgr, w, ref y);
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
                civ.PublicHealthInvest = Dial("Public health", civ.PublicHealthInvest, w, ref y);

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
                civ.ImmuneCasteInvest = Dial("Immune caste investment",
                    civ.ImmuneCasteInvest, w, ref y);

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
                civ.CensorshipLevel = Dial("Censorship / state control",   civ.CensorshipLevel,  w, ref y);

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
                // 0=encrypted/closed ↔ 1=open/legible to neighbours.
                civ.SignalLegibility   = Dial("Signal legibility  (encrypted ↔ open)",
                    civ.SignalLegibility,   w, ref y);
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
                civ.PheroMemoryInvest   = Dial("Ritual / pheromone memory",  civ.PheroMemoryInvest,   w, ref y);

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
                civ.SectorMilitary     = Dial("Military budget",       civ.SectorMilitary,     w, ref y);
                civ.DomesticSecurityLevel = Dial("Domestic security",  civ.DomesticSecurityLevel, w, ref y);
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

        DrawCards(4, civ, mgr, w, ref y);
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
            string hint  = i < card.ChoiceHints.Length
                ? $"  <color=#5a6a7a>({card.ChoiceHints[i]})</color>" : "";
            string label = card.ChoiceLabels[i] + hint;

            if (GUI.Button(new Rect(0f, y, w, Row), label, _choiceBtn))
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

            new Card
            {
                Id = "d3_trade_policy", Tab = 0,
                Title   = "Trade Policy",
                Dilemma = "Open borders or protect your economy?",
                ChoiceLabels = new[] { "Open Routes", "Balanced Tariffs", "Embargo" },
                ChoiceHints  = new[] { "max exchange, arbitrage risk", "moderate protection", "isolationist, resilience cost" },
                IsEligible = civ => civ.Has("e3_exchange_contact"),
                Apply = (mgr, i) => {
                    float[] tariff  = { 0.05f, 0.35f, 0.95f };
                    float[] openness = { 0.90f, 0.60f, 0.15f };
                    mgr.SetTradePolicy(0, tariff[i], openness[i]);
                    mgr.OnDecisionResolved("d3_trade_policy");
                }
            },

            new Card  // Individuated — formal currency / market card.
            {
                Id = "d3_formal_currency", Tab = 0,
                Title   = "Formal Currency",
                Dilemma = "Adopt standardised market exchange?",
                ChoiceLabels = new[] { "Adopt coinage / tokens", "Keep barter" },
                ChoiceHints  = new[] { "+economic domain, unlock market economy", "simpler, less domain growth" },
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Individuated
                    && civ.Has("e3_surplus_economy") && civ.Has("e3_trade_network"),
                Apply = (mgr, i) => {
                    if (i == 0) { mgr.PlayerCiv.DomainEconomic = Mathf.Min(mgr.PlayerCiv.DomainEconomic + 0.15f, 1f); }
                    mgr.OnDecisionResolved("d3_formal_currency");
                }
            },

            new Card  // Distributed — formalise graft-link.
            {
                Id = "d3_graft_link_treaty", Tab = 0,
                Title   = "Formalise Graft-Link",
                Dilemma = "Make the trade connection permanent?",
                ChoiceLabels = new[] { "Permanent graft-link treaty", "Keep informal contact" },
                ChoiceHints  = new[] { "exchange rate locked, resilience bond", "flexibility, easier to sever" },
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Distributed
                    && civ.Has("e3_trade_network"),
                Apply = (mgr, i) => {
                    if (i == 0) { mgr.PlayerCiv.FormalTradeActive = true; mgr.PlayerCiv.RecoverResilience(0.05f); }
                    mgr.OnDecisionResolved("d3_graft_link_treaty");
                }
            },

            new Card
            {
                Id = "d3_large_initiative_1", Tab = 0,
                Title   = "Large Initiative",
                Dilemma = "Spend the surplus on what?",
                ChoiceLabels = new[] { "Vaccination Drive", "Trade Expansion", "Monument" },
                ChoiceHints  = new[] { "+10% resilience", "open routes, higher openness", "+religion channel" },
                IsEligible = civ => civ.Has("e3_surplus_economy"),
                Apply = (mgr, i) => {
                    var c = mgr.PlayerCiv;
                    switch (i) {
                        case 0: c.RecoverResilience(0.10f); break;
                        case 1: mgr.SetTradePolicy(0, 0.10f, 0.80f); break;
                        case 2: c.InvestReligion = Mathf.Min(c.InvestReligion + 0.15f, 1f); break;
                    }
                    mgr.OnDecisionResolved("d3_large_initiative_1");
                }
            },

            // ── GENETIC/BIOLOGICAL (1) ────────────────────────────────────────

            new Card
            {
                Id = "d3_caste_labor", Tab = 1,
                Title   = "Labor Allocation",
                Dilemma = "Where does the population's effort go?",
                ChoiceLabels = new[] { "Production Focus", "Military Focus", "Culture Focus" },
                ChoiceHints  = new[] { "max output, stockpile", "coercive expansion", "legitimacy, ideas" },
                IsEligible = civ => civ.Has("e3_social_stratification"),
                Apply = (mgr, i) => {
                    var c = mgr.PlayerCiv;
                    bool coll = c.Architecture == CognitiveArchitecture.Collective;
                    switch (i) {
                        case 0: if (coll) mgr.SetCasteAllocation(0,.7f,.2f,.1f); else mgr.SetSectorAllocation(0,.65f,.2f,.15f); break;
                        case 1: if (coll) mgr.SetCasteAllocation(0,.3f,.2f,.5f); else mgr.SetSectorAllocation(0,.30f,.55f,.15f); break;
                        case 2: if (coll) mgr.SetCasteAllocation(0,.4f,.4f,.2f); else mgr.SetSectorAllocation(0,.30f,.20f,.50f); break;
                    }
                    mgr.OnDecisionResolved("d3_caste_labor");
                }
            },

            new Card  // Individuated — domesticate a species.
            {
                Id = "d3_domesticate_species", Tab = 1,
                Title   = "Domesticate a Species",
                Dilemma = "Bring a wild species into managed production?",
                ChoiceLabels = new[] { "Domesticate (herd/crop)", "Leave wild" },
                ChoiceHints  = new[] { "+stockpile growth, +biological investment", "no change" },
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Individuated
                    && civ.Has("e3_agriculture")
                    && civ.Subtrack != IndividuatedSubTrack.A3_BulkBrain,  // aquatic tool ceiling blocks this
                Apply = (mgr, i) => {
                    if (i == 0) {
                        var c = mgr.PlayerCiv;
                        c.Stockpile = Mathf.Min(c.Stockpile + 0.3f, 5f);
                        c.InvestBiological = Mathf.Min(c.InvestBiological + 0.08f, 1f);
                    }
                    mgr.OnDecisionResolved("d3_domesticate_species");
                }
            },

            new Card  // Distributed — recruit symbiotic defender.
            {
                Id = "d3_symbiotic_defender", Tab = 1,
                Title   = "Symbiotic Defender",
                Dilemma = "Recruit a mutualist defender species?",
                ChoiceLabels = new[] { "Establish symbiotic defense", "Decline" },
                ChoiceHints  = new[] { "+biochemical domain, ant-acacia model", "no change" },
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Distributed
                    && civ.Has("e3_trade_network"),
                Apply = (mgr, i) => {
                    if (i == 0) mgr.ApplyDomainInvestment(0, 0f, 0.15f, 0f, 0f);
                    mgr.OnDecisionResolved("d3_symbiotic_defender");
                }
            },

            new Card  // Plague crisis — all archs, triggered by Era3Manager crisis system.
            {
                Id = "d3_plague_response", Tab = 1, IsCrisis = true,
                Title   = "⚠ Plague / Pandemic",
                Dilemma = "How does the civilisation respond?",
                ChoiceLabels = new[] { "Quarantine — restrict movement", "Treat — invest in public health", "Ignore — accept losses" },
                ChoiceHints  = new[] { "isolates spread, trade penalty", "+health invest, slower spread", "resilience drain continues" },
                IsEligible = civ => civ.Has("e3_plague_active"),
                Apply = (mgr, i) => {
                    var c = mgr.PlayerCiv;
                    switch (i) {
                        case 0: c.ForeignOpenness = Mathf.Max(c.ForeignOpenness - 0.20f, 0f); c.RecoverResilience(0.08f); break;
                        case 1: c.PublicHealthInvest = Mathf.Min(c.PublicHealthInvest + 0.15f, 1f); c.RecoverResilience(0.04f); break;
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
                IsEligible = civ => civ.Has("e3_chiefdom"),
                Apply = (mgr, i) => {
                    var t = i switch { 0 => IdeaPatronageType.Culture, 1 => IdeaPatronageType.Religion,
                                       2 => IdeaPatronageType.Science, _ => IdeaPatronageType.Military };
                    mgr.SetIdeaPatronage(0, t);
                    mgr.OnDecisionResolved("d3_idea_patronage");
                }
            },

            new Card  // Individuated: writing/codification adoption (gated by CommMedium).
            {
                Id = "d3_writing_adoption", Tab = 2,
                Title   = "Writing / Codification",
                Dilemma = "Record your language and knowledge?",
                ChoiceLabels = new[] { "Adopt written record", "Remain oral" },
                ChoiceHints  = new[] { "+info channel, cross-gen knowledge", "flexible, no permanence" },
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Individuated
                    && civ.Has("e3_writing")
                    && (civ.CommMedium == CommunicationMedium.VocalAuditory
                     || civ.CommMedium == CommunicationMedium.VisualGestural),
                Apply = (mgr, i) => {
                    if (i == 0) {
                        var c = mgr.PlayerCiv;
                        c.InvestInformation = Mathf.Min(c.InvestInformation + 0.12f, 1f);
                        c.DomainInformational = Mathf.Min(c.DomainInformational + 0.10f, 1f);
                    }
                    mgr.OnDecisionResolved("d3_writing_adoption");
                }
            },

            new Card  // Distributed: kin-recognition-breaking (SIGINT equivalent §6.3).
            {
                Id = "d3_kin_recognition_break", Tab = 2,
                Title   = "Kin-Recognition Disruption",
                Dilemma = "Develop chemical SIGINT against neighbours?",
                ChoiceLabels = new[] { "Develop KRB tech", "Abstain" },
                ChoiceHints  = new[] { "+detection cap, depletes neighbour trust", "no change" },
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Distributed
                    && civ.SignalBandwidthTier >= 1
                    && civ.Has("e3_trade_network"),
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
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Collective
                    && civ.DecVelocity == DecisionVelocity.Slow,
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
                IsEligible = civ => civ.Has("e3_family_norms_emerge"),
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
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Individuated
                    && civ.BeliefTier >= 2 && !civ.HasOrganizedReligion
                    && civ.Has("e3_religion_organized"),
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
                IsEligible = civ => civ.Has("e3_social_stratification"),
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

            new Card
            {
                Id = "d3_bioweapon_option", Tab = 4,
                Title   = "Biochemical Weapons",
                Dilemma = "Develop offensive capacity or restrict?",
                ChoiceLabels = new[] { "Develop Weapons", "Defense Only" },
                ChoiceHints  = new[] { "+30% biochem domain, escalation risk", "no escalation, lower domain" },
                IsEligible = civ => civ.Has("d3_domain_investment") && civ.Has("e3_warfare_organized"),
                Apply = (mgr, i) => {
                    if (i == 0) mgr.ApplyDomainInvestment(0, 0f, 0.30f, 0f, 0f);
                    mgr.OnDecisionResolved("d3_bioweapon_option");
                }
            },

            new Card  // Individuated: negotiate treaty (post-war or independent).
            {
                Id = "d3_negotiate_treaty", Tab = 4,
                Title   = "Negotiate Treaty",
                Dilemma = "Formalise a peace or alliance?",
                ChoiceLabels = new[] { "Peace treaty — end hostilities", "Formal alliance", "Decline" },
                ChoiceHints  = new[] { "+trade health, -coercive drain", "permanent alliance active", "no change" },
                IsEligible = civ => civ.Has("e3_diplomacy") && !civ.Has("d3_negotiate_treaty"),
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
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Distributed
                    && civ.Has("e3_trade_network"),
                Apply = (mgr, i) => {
                    if (i == 0) {
                        var c = mgr.PlayerCiv;
                        c.RecoverResilience(0.08f);
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
                IsEligible = civ => civ.Architecture == CognitiveArchitecture.Collective
                    && civ.Has("e3_warfare_organized"),
                Apply = (mgr, i) => {
                    if (i == 0) {
                        var c = mgr.PlayerCiv;
                        c.Stockpile = Mathf.Min(c.Stockpile + 0.4f, 5f);
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
                        case 1: c.DomainEconomic = Mathf.Min(c.DomainEconomic + 0.12f, 1f); c.Stockpile = Mathf.Min(c.Stockpile + 0.5f, 5f); break;
                        case 2: c.DomainKinetic = Mathf.Min(c.DomainKinetic + 0.12f, 1f); c.InvestCoercive = Mathf.Min(c.InvestCoercive + 0.08f, 1f); break;
                    }
                    c.AcquiredEvents.Remove("e3_golden_age_active");
                    mgr.OnDecisionResolved("d3_golden_age_response");
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

        _tabOn = new GUIStyle(GUI.skin.button)
        {
            fontStyle = FontStyle.Bold, fontSize = 11,
            normal = { background = Tex(new Color(0.18f, 0.38f, 0.70f, 0.95f)), textColor = Color.white },
            hover  = { background = Tex(new Color(0.22f, 0.44f, 0.78f, 1f)),    textColor = Color.white },
        };
        _tabOff = new GUIStyle(GUI.skin.button)
        {
            fontSize = 11,
            normal = { background = Tex(new Color(0.11f, 0.11f, 0.14f, 0.94f)), textColor = new Color(0.68f, 0.68f, 0.68f) },
            hover  = { background = Tex(new Color(0.16f, 0.16f, 0.20f, 0.94f)), textColor = Color.white },
        };

        _hdr = Label(new Color(0.55f, 0.82f, 1f), 11, FontStyle.Bold);
        _sub = Label(new Color(0.55f, 0.82f, 1f), 10, FontStyle.Bold);
        _lbl = Label(new Color(0.82f, 0.82f, 0.82f), 11, FontStyle.Normal);
        _dim = new GUIStyle(GUI.skin.label) { fontSize = 11, richText = true, normal = { textColor = new Color(0.60f, 0.60f, 0.60f) } };

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
