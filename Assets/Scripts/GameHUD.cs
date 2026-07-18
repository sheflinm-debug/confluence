using System.Collections.Generic;
using UnityEngine;

/// Collapsible right-side HUD panel with four scrollable tabs:
///   Global  — population, species, biome, speciation, weather, climate, atmosphere
///   Mine    — player species identity, genes, trait bars; click to focus camera
///   Ranks   — global community leaderboard; click row to focus camera on that community
///   Settings — atmosphere visual toggle, planet-lock, star/orbit info
///
/// Mouse wheel scrolls content when the cursor is over the panel.
/// Community click in Mine/Ranks calls FocusOnCommunity(), which swings the orbit
/// camera toward the centroid of that community's agents.
public class GameHUD : MonoBehaviour
{
    public AgentSpawner agentSpawner;
    public int playerCommunityId = 0;

    /// When true all raw-component OnGUI overlays are suppressed.
    /// Toggle in the Settings tab at runtime for debugging.
    public static bool SuppressRawOverlays = true;

    private enum Page { Global, Mine, Ranks, Settings }
    private Page _page = Page.Global;
    private bool _open = true;

    private enum RankTab { Population, Strength, Intelligence }
    private RankTab _rankTab = RankTab.Population;

    // Inline community-rename state (Ranks tab) — player can click their own community's name to
    // rename it. Single shared buffer since only the player's own community is ever renameable.
    private bool   _renamingCommunity;
    private string _renameBuffer = "";
    private const string RenameFieldControlName = "CommunityRenameField";

    private const float PanelW   = 270f;
    private const float TabH     = 26f;
    private const float ToggleH  = 20f;
    private const float Pad      = 6f;
    private const float ScrollW  = 14f; // approximate Unity IMGUI scrollbar width

    // Per-tab scroll positions and last measured content height.
    private readonly Vector2[] _scroll   = new Vector2[4];
    private readonly float[]   _contentH = { 800f, 600f, 500f, 400f };

    // Community highlight / marker state.
    private int  _highlightCommunityId = 0; // community whose agents get screen markers
    private bool _showMarkers          = true;

    // Cached marker positions computed in Update() (not OnGUI) to avoid per-frame
    // flickering caused by the raycast running on every Layout+Repaint event.
    private readonly List<Vector2> _markerPositions = new List<Vector2>(64);
    private Color _markerColor = Color.white;

    // Cached references.
    private AtmosphereVisual _atmosVisual;
    private OrbitCamera      _orbitCam;

    // ── Static panel rect so InspectPopup can suppress clicks inside HUD ─────

    private static Rect  _lastPanelRect;
    private static bool  _lastOpen;

    public static bool IsOpenAndContains(Vector2 guiPoint) =>
        _lastOpen && _lastPanelRect.Contains(guiPoint);

    /// Returns true when the HUD panel is under the given screen-space position
    /// (Y=0 at bottom, as Unity screen coords use). Called by OrbitCamera to suppress
    /// scroll-wheel zoom while the cursor is over the HUD.
    public static bool IsScrollBlockedAtScreenPos(Vector2 screenPos)
    {
        // IMGUI uses Y=0 at top; screen coords use Y=0 at bottom.
        Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
        return IsOpenAndContains(guiPos);
    }

    // ── Unity ─────────────────────────────────────────────────────────────────

    void LateUpdate()
    {
        if (!_showMarkers || agentSpawner == null) { _markerPositions.Clear(); return; }
        Camera cam = Camera.main;
        if (cam == null) { _markerPositions.Clear(); return; }

        _markerPositions.Clear();
        bool colorKnown = false;

        foreach (var a in agentSpawner.ActiveAgents)
        {
            if (a == null || a.communityId != _highlightCommunityId) continue;
            Vector3 sp = cam.WorldToScreenPoint(a.transform.position);
            if (sp.z <= 0f) continue;

            // Occlusion: skip agents on the far side of the planet. Uses a stable geometric horizon
            // test (is the agent's outward surface normal facing the camera?) rather than a physics
            // raycast against the low-poly, per-frame-rotating planet collider — the raycast
            // intermittently self-hit the faceted surface at the agent's feet, making markers flicker.
            Vector3 agentPos = a.transform.position;
            Vector3 camPos   = cam.transform.position;
            Vector3 surfaceNormal = (agentPos - agentSpawner.planetCenter).normalized;
            if (Vector3.Dot(surfaceNormal, (camPos - agentPos).normalized) < 0f) continue; // back hemisphere

            _markerPositions.Add(new Vector2(sp.x, Screen.height - sp.y));
            if (!colorKnown) { _markerColor = a.lineageColor; colorKnown = true; }
        }
    }

    void Awake()
    {
        _atmosVisual          = FindAnyObjectByType<AtmosphereVisual>();
        _orbitCam             = FindAnyObjectByType<OrbitCamera>();
        _highlightCommunityId = playerCommunityId; // highlight player community by default
    }

    void OnGUI()
    {
        float panelX = Screen.width - PanelW - 2f;
        float panelY = 2f;
        float panelH = Screen.height - 4f;

        // ── Collapse / expand toggle arrow ────────────────────────────────────
        float toggleX = panelX + PanelW / 2f - 11f;
        if (GUI.Button(new Rect(toggleX, panelY, 22f, ToggleH),
                _open ? "▲" : "▼", BtnStyle(10)))
            _open = !_open;

        _lastOpen = _open;

        if (!_open)
        {
            _lastPanelRect = new Rect(panelX, panelY, PanelW, ToggleH);
            return;
        }

        _lastPanelRect = new Rect(panelX, panelY, PanelW, panelH);

        // Background behind panel content.
        DrawRect(panelX, panelY + ToggleH, PanelW, panelH - ToggleH, new Color(0f, 0f, 0f, 0.78f));
        DrawRect(panelX, panelY + ToggleH, PanelW, 1f, new Color(0.5f, 0.5f, 0.5f, 0.5f));

        float innerW   = PanelW - Pad * 2f;
        float contentX = panelX + Pad;
        float y        = panelY + ToggleH + Pad;

        // ── Persistent stats strip (always visible above tabs) ────────────────
        {
            // ActiveAgents.Count alone undercounts once Era 3 settlement absorption has begun — every
            // organism folded into a settlement leaves ActiveAgents permanently, so its population
            // must be added back in from the settlements themselves to get a true total.
            int totalPop = agentSpawner != null ? agentSpawner.ActiveAgents.Count : 0;
            if (Era3Manager.Instance != null && Era3Manager.Instance.IsActive)
                totalPop += Mathf.RoundToInt(Era3Manager.Instance.TotalSettlementPopulation());
            int living   = SpeciationManager.Instance != null ? SpeciationManager.Instance.SpeciesCount : 1;
            int extinct  = SpeciationManager.Instance != null ? SpeciationManager.Instance.ExtinctSpeciesCount : 0;
            // No hard population cap anymore — carrying capacity is purely emergent from energy math.
            string statsText = $"Species: {living} living / {extinct} extinct   Pop: {totalPop}";
            var stripStyle = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            stripStyle.normal.textColor = new Color(0.9f, 0.85f, 0.6f); // warm gold — distinct from tab labels
            GUI.Label(new Rect(contentX, y, innerW, 16f), statsText, stripStyle);
            y += 16f;
            DrawRect(contentX, y, innerW, 1f, new Color(0.4f, 0.4f, 0.4f, 0.4f));
            y += 2f;
        }

        // ── Tab row (always visible, outside scroll) ──────────────────────────
        string[] tabs = LocalizationManager.TabLabels();
        float tabW = innerW / tabs.Length;
        for (int i = 0; i < tabs.Length; i++)
        {
            bool active = (int)_page == i;
            if (GUI.Button(new Rect(contentX + i * tabW, y, tabW - 2f, TabH), tabs[i],
                    active ? ActiveTabStyle() : InactiveTabStyle()))
            {
                _page = (Page)i;
                _scroll[(int)_page] = Vector2.zero; // reset scroll on tab switch
            }
        }
        y += TabH + 2f;
        DrawRect(contentX, y, innerW, 1f, new Color(0.4f, 0.4f, 0.4f, 0.6f));
        y += 2f;

        // ── Scrollable content area ───────────────────────────────────────────
        int pi = (int)_page;
        float viewH = panelH - (y - panelY) - Pad;
        Rect viewRect    = new Rect(panelX, y, PanelW, viewH);
        float scrollInnerW = PanelW - ScrollW - Pad * 2f;
        Rect contentRect = new Rect(0, 0, scrollInnerW, _contentH[pi]);

        _scroll[pi] = GUI.BeginScrollView(viewRect, _scroll[pi], contentRect,
            false, true); // never show horizontal bar, always show vertical

        float cy = Pad;
        switch (_page)
        {
            case Page.Global:   DrawGlobalPage  (Pad, ref cy, scrollInnerW); break;
            case Page.Mine:     DrawMinePage    (Pad, ref cy, scrollInnerW); break;
            case Page.Ranks:    DrawRanksPage   (Pad, ref cy, scrollInnerW); break;
            case Page.Settings: DrawSettingsPage(Pad, ref cy, scrollInnerW); break;
        }
        _contentH[pi] = cy + 8f; // measure content height for next frame

        GUI.EndScrollView();

        // ── Agent markers — drawn after EndScrollView so they're never clipped ─
        if (_showMarkers) DrawCommunityMarkers();

        // ── Mouse-wheel scroll when cursor is over the panel ──────────────────
        if (Event.current.type == EventType.ScrollWheel &&
            _lastPanelRect.Contains(Event.current.mousePosition))
        {
            _scroll[pi].y = Mathf.Clamp(
                _scroll[pi].y + Event.current.delta.y * 18f,
                0f,
                Mathf.Max(0f, _contentH[pi] - viewH));
            Event.current.Use();
        }
    }

    // ── Pages ─────────────────────────────────────────────────────────────────

    private void DrawGlobalPage(float x, ref float y, float w)
    {
        Section(x, ref y, w, LocalizationManager.L("sec_population"));
        int total   = agentSpawner != null ? agentSpawner.ActiveAgents.Count : 0;
        if (Era3Manager.Instance != null && Era3Manager.Instance.IsActive)
            total += Mathf.RoundToInt(Era3Manager.Instance.TotalSettlementPopulation());
        int living  = SpeciationManager.Instance != null ? SpeciationManager.Instance.SpeciesCount : 1;
        int extinct = SpeciationManager.Instance != null ? SpeciationManager.Instance.ExtinctSpeciesCount : 0;
        int hist    = SpeciationManager.Instance != null ? SpeciationManager.Instance.HistoricalSpeciesCount : 1;
        SmallLabel(x, ref y, w, $"Organisms: {total}   Species: {living}");
        SmallLabel(x, ref y, w, $"Historical: {hist} total  |  Extinct: {extinct}");

        if (Era3Manager.Instance != null && Era3Manager.Instance.IsActive)
        {
            int polities = (Era3Manager.Instance.PlayerCiv != null ? 1 : 0) + Era3Manager.Instance.NpcCivs.Count;
            SmallLabel(x, ref y, w, $"Polities: {polities}   Settlements: {Era3Manager.Instance.Settlements.Count}");
        }

        if (SpeciationManager.Instance != null)
        {
            float si  = SpeciationManager.Instance.MaxSI;
            string era = SpeciationManager.Instance.EraLabel;
            SmallLabel(x, ref y, w, $"Era: {era}   SI: {si:G3}");
        }

        if (agentSpawner != null)
        {
            int chemo = 0, photo = 0, hetero = 0;
            foreach (var a in agentSpawner.ActiveAgents)
            {
                if (a == null) continue;
                switch (a.Metabolism)
                {
                    case MetabolismType.Chemosynthetic: chemo++;  break;
                    case MetabolismType.Phototrophic:   photo++;  break;
                    case MetabolismType.Heterotrophic:  hetero++; break;
                }
            }
            SmallLabel(x, ref y, w, $"Chemo {chemo}  Photo {photo}  Hetero {hetero}");
        }

        y += 3f;
        Section(x, ref y, w, LocalizationManager.L("sec_surface"));
        float liq = FluidDynamicsManager.Instance != null
            ? FluidDynamicsManager.Instance.GetLiquidCoverageFraction() : 0f;
        GasBar(x, ref y, w, "Ocean", liq,       new Color(0.25f, 0.55f, 0.9f));
        GasBar(x, ref y, w, "Rocky", 1f - liq,  new Color(0.55f, 0.45f, 0.35f));

        y += 3f;
        Section(x, ref y, w, LocalizationManager.L("sec_climate"));
        if (PlanetTemperature.Instance != null)
            SmallLabel(x, ref y, w, $"Temperature: {PlanetTemperature.Instance.CurrentK:F0} K");
        if (AtmosphereManager.Instance != null)
        {
            float p = AtmosphereManager.Instance.PressureBar;
            string pStr = p < 0.001f ? $"{p * 1e6f:F1} µbar"
                        : p < 1f    ? $"{p * 1000f:F1} mbar"
                        :              $"{p:F2} bar";
            SmallLabel(x, ref y, w, $"Pressure: {pStr}");
        }
        if (OrbitalSeasons.Instance != null)
        {
            var os = OrbitalSeasons.Instance;
            SmallLabel(x, ref y, w, $"Season: {os.SeasonLabel()}  {os.CurrentDistanceAU:F3} AU");
        }
        if (WeatherManager.Instance != null && WeatherManager.Instance.ActiveStorms.Count > 0)
            SmallLabel(x, ref y, w, $"Active storms: {WeatherManager.Instance.ActiveStorms.Count}");

        y += 3f;
        Section(x, ref y, w, LocalizationManager.L("sec_atmosphere"));
        if (AtmosphereManager.Instance != null)
        {
            float stress = AtmosphereManager.Instance.AtmosphericStress;
            Color sc = Color.Lerp(new Color(0.3f, 0.9f, 0.3f), new Color(0.9f, 0.2f, 0.3f), stress);
            Color prev = GUI.color; GUI.color = sc;
            SmallLabel(x, ref y, w, $"Pop atmos stress: {stress * 100f:F0}%");
            GUI.color = prev;

            foreach (var g in AtmosphereManager.Instance.Gases)
            {
                Color bc = g.Role switch
                {
                    GasRole.Breathed => new Color(0.3f, 0.8f, 0.4f),
                    GasRole.Expelled => new Color(0.9f, 0.5f, 0.2f),
                    _                => new Color(0.6f, 0.6f, 0.6f),
                };
                GasBar(x, ref y, w, g.Name, g.Fraction, bc);
            }
        }
        else SmallLabel(x, ref y, w, "(no atmosphere)");

        SmallLabel(x, ref y, w, $"UV: {UVManager.BaseUVIntensity:F2}  Ozone: {UVManager.OzoneAttenuation * 100f:F0}%");
        SmallLabel(x, ref y, w, ChemicalNutrientPool.Initialized ? "Nutrient pool: active" : "Nutrient pool: —");
    }

    private void DrawMinePage(float x, ref float y, float w)
    {
        AgentController player = FindPlayerAgent();

        Section(x, ref y, w, "MY COMMUNITY");

        if (player == null)
        {
            // Once a community is fully civilized, EVERY member gets absorbed into settlements
            // (Phase 1's Era 3 lag fix) — FindPlayerAgent() then permanently returns null, and this
            // pane used to just say "waiting for first organism" forever even with a thriving,
            // populous civilization. Falls back to civ/settlement + the last-known Era 2 record
            // instead of the live organism fields (Backbone/Sociality/etc.) that no longer exist.
            if (Era3Manager.Instance != null && Era3Manager.Instance.IsActive && Era3Manager.Instance.PlayerCiv != null)
            {
                DrawMinePageCivilized(x, ref y, w);
                return;
            }
            SmallLabel(x, ref y, w, "(waiting for first organism…)");
            return;
        }

        // Color swatch + focus button on the same row.
        Color prev = GUI.color;
        GUI.color = player.lineageColor;
        GUI.DrawTexture(new Rect(x, y + 1f, 14f, 14f), Texture2D.whiteTexture);
        GUI.color = prev;
        SmallLabelAt(x + 18f, y, w - 70f, $"{CommunityNameRegistry.GetName(player.communityId)} — {player.AtmoLineage}");
        if (GUI.Button(new Rect(x + w - 58f, y - 1f, 58f, 16f), "→ Focus", BtnStyle(10)))
        {
            FocusOnCommunity(playerCommunityId);
            _highlightCommunityId = playerCommunityId;
        }
        y += 18f;

        SmallLabel(x, ref y, w, $"Metabolism : {CommunityMetabolismLabel(playerCommunityId)}");
        string kingdom = string.IsNullOrEmpty(player.Kingdom) ? "—" : player.Kingdom;
        SmallLabel(x, ref y, w, $"Kingdom    : {kingdom}");

        // Show motility for the LINEAGE (any member motile = lineage has motility).
        bool lineageMotile = CommunityHasMotility(playerCommunityId);
        SmallLabel(x, ref y, w, $"Motion     : {(lineageMotile ? "Self-directed" : "Passive drift")}");
        SmallLabel(x, ref y, w, $"Backbone   : {player.Backbone}");
        SmallLabel(x, ref y, w, $"Appendages : {player.Manipulation}");
        SmallLabel(x, ref y, w, $"Sociality  : {player.Sociality}");
        SmallLabel(x, ref y, w, $"Neural     : {player.NeuralComplexity}");
        // CurrentMedium is the INSTANTANEOUS position (is this individual physically in liquid right
        // now); IsAquatic is the LOCKED habitat this lineage colonized (see AgentController.ColonizeLand
        // / ReturnToSea). These can legitimately differ — a land-colonized organism standing in a puddle
        // still reads "Sea" for CurrentMedium — so show both, or the pairing reads as a contradiction
        // (e.g. "Medium: Sea" next to a Fire Mastery threshold, which is gated on habitat, not position).
        string habitatLabel = player.IsAquatic ? "aquatic species" : "land species";
        SmallLabel(x, ref y, w, $"Medium     : {player.CurrentMedium} (currently)  [{habitatLabel}]");
        string sexLabel = player.IsSexual
            ? $"{player.Sex}{(player.CanChangeSex ? " (hermaphrodite)" : "")}"
            : "Asexual";
        SmallLabel(x, ref y, w, $"Sex        : {sexLabel}");
        SmallLabel(x, ref y, w, $"Str {player.strengthTrait:F0}  Spd {player.speedTrait:F0}  Vis {player.visionTrait:F0}  Hrd {player.hardinessTrait:F0}");
        SmallLabel(x, ref y, w, $"Age {player.AgeSeconds:F0} s   Stress {player.StressLevel:F1}");

        // ── Biology section: gas/liquid chemistry ──────────────────────────────
        y += 4f;
        Section(x, ref y, w, "BIOLOGY");
        // Both read the SAME underlying respiration-gas fields (BreathedGasName/ExpelledGasName) —
        // previously "Expels" ignored ExpelledGasName and instead showed generic flavor-text keyed
        // only on Metabolism type (a completely separate axis), so an organism whose real rolled
        // biochemistry breathed CO2 and expelled O2 could still display "Expels: CO2" if its
        // Metabolism happened to be Heterotrophic — a display bug, not an actual same-gas metabolism.
        string breathes = string.IsNullOrEmpty(player.BreathedGasName) ? "—" : player.BreathedGasName;
        SmallLabel(x, ref y, w, $"Breathes   : {breathes}");
        string exhales = string.IsNullOrEmpty(player.ExpelledGasName) ? "—" : player.ExpelledGasName;
        SmallLabel(x, ref y, w, $"Expels     : {exhales}");
        string liquidAffinity = string.IsNullOrEmpty(player.RequiredLiquidKind)
            ? "—" : player.RequiredLiquidKind;
        SmallLabel(x, ref y, w, $"Liquid     : {liquidAffinity}");
        SmallLabel(x, ref y, w, $"Atmo-lineage: {player.AtmoLineage}");

        // This is under "MY COMMUNITY" — it must be scoped to the player's OWN community, not every
        // living organism in the world (the previous ActiveAgents.Count was unfiltered by community,
        // a separate bug from the settlement-absorption undercount below). Add the player's own
        // settlements' population back in for the same reason as the other two readouts above.
        int total = 0;
        if (agentSpawner != null)
            foreach (var a in agentSpawner.ActiveAgents)
                if (a != null && a.communityId == player.communityId) total++;
        if (Era3Manager.Instance != null && Era3Manager.Instance.IsActive)
            total += Mathf.RoundToInt(Era3Manager.Instance.SettlementPopulationForCiv(player.communityId));
        SmallLabel(x, ref y, w, $"Population: {total}");

        // ── Era 2 Intelligence (shown once Era 2 activates) ─────────────
        if (Era2Manager.Instance != null && Era2Manager.Instance.IsActive)
        {
            y += 4f;
            Section(x, ref y, w, "ERA 2 — INTELLIGENCE");
            var rec = Era2Manager.Instance.GetRecord(playerCommunityId);
            if (rec != null)
            {
                SmallLabel(x, ref y, w, $"Architecture  : {rec.Architecture}");
                if (rec.Architecture == CognitiveArchitecture.Individuated)
                    SmallLabel(x, ref y, w, $"Sub-track     : {rec.SubTrack}");
                SmallLabel(x, ref y, w, $"Intel. Index  : {rec.II:F2}");

                // §6 Player Decision Layer — show once chosen.
                if (rec.CognitiveInvestmentMult != 1.0f || rec.Architecture == CognitiveArchitecture.Individuated)
                    SmallLabel(x, ref y, w, $"Cog.Invest.   : ×{rec.CognitiveInvestmentMult:F2}");
                if (rec.CommMedium != CommunicationMedium.Unset)
                    SmallLabel(x, ref y, w, $"Comm.Medium   : {rec.CommMedium}");
                if (rec.NicheOrientation != NicheConstructionOrientation.Unset)
                    SmallLabel(x, ref y, w, $"Niche         : {rec.NicheOrientation}");
                if (rec.MetabolicBrainWeight != 1.0f)
                    SmallLabel(x, ref y, w, $"Brain Alloc.  : ×{rec.MetabolicBrainWeight:F2}");
                if (rec.SocialStructure != SocialStructureType.Unset)
                    SmallLabel(x, ref y, w, $"Social Struct.: {rec.SocialStructure}");

                // End-of-Era-2 thresholds. Full names so the "FireMastery" THRESHOLD is not confused
                // with the "Harness Fire" GENE (which unlocks Pyrotechnology and is listed under genes).
                string thresholds = "";
                if (rec.ThresholdLLFP)                     thresholds += "LLFP ";
                if (rec.ThresholdFireMastery)             thresholds += "FireMastery ";
                if (rec.ThresholdCumulativeCulture)       thresholds += "CumulativeCulture ";
                if (rec.ThresholdCommunicationCodeified)  thresholds += "CommCodified ";
                if (rec.ThresholdLaborFormalized)         thresholds += "LaborFormalized ";
                if (rec.ThresholdColonialEngineering)     thresholds += "ColonialEng ";
                if (rec.ThresholdBiosphereTerraforming)   thresholds += "Terraforming ";
                if (rec.ThresholdBloomDominance)          thresholds += "BloomDominance ";
                if (rec.ThresholdTrophicApex)             thresholds += "TrophicApex ";
                SmallLabel(x, ref y, w, $"Thresholds    : {(string.IsNullOrEmpty(thresholds) ? "none yet" : thresholds.Trim())} ({rec.ThresholdCount}/9)");
                if (Era3Manager.Instance != null && Era3Manager.Instance.IsActive && Era3Manager.Instance.PlayerCiv != null)
                {
                    SmallLabel(x, ref y, w, $"Era 3 path    : {Era3Manager.Instance.PlayerCiv.Path}");
                    int mySettlements = 0;
                    foreach (var s in Era3Manager.Instance.Settlements)
                        if (s.OwnerCivId == playerCommunityId) mySettlements++;
                    SmallLabel(x, ref y, w, $"Settlements   : {mySettlements}  (see Civilization > Settlements)");
                }

                // ── Era 3 gate readout: exactly what still gates the jump to Era 3 ──
                if (Era2Manager.Instance.TryGetPlayerEra3Gate(out bool hasFork, out bool hasComm, out bool hasThresh, out _))
                {
                    string Chk(bool b) => b ? "[x]" : "[ ]";
                    SmallLabel(x, ref y, w, $"Era3 gate     : Arch {Chk(hasFork)}  Comm {Chk(hasComm)}  Threshold {Chk(hasThresh)}");
                    float remain = Mathf.Max(0f, Era2Manager.Era3Ceiling - Era2Manager.Instance.Era2Elapsed);
                    string ceilingLine = Era2Manager.Instance.Era3GateFired
                        ? "Era3 unlocked!"
                        : (hasFork && hasComm && hasThresh)
                            ? "achievement met → advancing"
                            : $"or survive ceiling: {remain:F0}s left";
                    SmallLabel(x, ref y, w, $"              {ceilingLine}");
                }
            }
        }

        y += 4f;
        Section(x, ref y, w, "ACQUIRED GENES");
        if (player.AcquiredGenes.Count == 0)
            SmallLabel(x, ref y, w, "  (none yet)");
        else
            foreach (var g in player.AcquiredGenes)
                SmallLabel(x, ref y, w, $"  ✓ {g}");

        y += 4f;
        Section(x, ref y, w, "TRAITS");
        TraitBar(x, ref y, w, "Vision",    player.visionTrait);
        TraitBar(x, ref y, w, "Speed",     player.speedTrait);
        TraitBar(x, ref y, w, "Strength",  player.strengthTrait);
        TraitBar(x, ref y, w, "Hardiness", player.hardinessTrait);
        TraitBar(x, ref y, w, "Temp Pref", player.temperaturePreference);
        TraitBar(x, ref y, w, "Moisture",  player.moisturePreference);
        TraitBar(x, ref y, w, "UV Tol",    player.uvTolerance);
        TraitBar(x, ref y, w, "Pres Tol",  player.pressureTolerance);
        TraitBar(x, ref y, w, "Therm Tol", player.thermalCycleTolerance);
    }

    /// "MY COMMUNITY" once the player's community has no live organisms left to sample (everyone's
    /// absorbed into settlements) — identifies the civ from CivilizationState/Era2Record instead,
    /// since that's the only place this data still lives at that point.
    private void DrawMinePageCivilized(float x, ref float y, float w)
    {
        var mgr = Era3Manager.Instance;
        var civ = mgr.PlayerCiv;

        SmallLabelAt(x, y, w - 70f, $"{civ.Name}  —  {civ.Path}  ({civ.Architecture})");
        if (GUI.Button(new Rect(x + w - 58f, y - 1f, 58f, 16f), "→ Focus", BtnStyle(10)))
        {
            FocusOnCommunity(playerCommunityId);
            _highlightCommunityId = playerCommunityId;
        }
        y += 18f;

        // Founder biology snapshot — the pane's old identity readout (Kingdom/Backbone/Metabolism/
        // gas exchange) with no live organism to read it FROM anymore; see CivilizationState's
        // Founder* fields, snapshotted once at civilization time before absorption.
        SmallLabel(x, ref y, w, $"Kingdom      : {civ.FounderKingdom}");
        SmallLabel(x, ref y, w, $"Backbone     : {civ.FounderBackbone}");
        SmallLabel(x, ref y, w, $"Metabolism   : {civ.FounderMetabolism}");
        SmallLabel(x, ref y, w, $"Breathes     : {civ.FounderBreathedGas}   Expels: {civ.FounderExpelledGas}");
        SmallLabel(x, ref y, w, $"Liquid       : {civ.FounderLiquidKind}");

        var rec = Era2Manager.Instance != null ? Era2Manager.Instance.GetRecord(playerCommunityId) : null;
        if (rec != null)
        {
            SmallLabel(x, ref y, w, $"Intel. Index : {rec.II:F2}");
            if (rec.SocialStructure != SocialStructureType.Unset)
                SmallLabel(x, ref y, w, $"Social Struct: {rec.SocialStructure}");
            if (rec.CommMedium != CommunicationMedium.Unset)
                SmallLabel(x, ref y, w, $"Comm. Medium : {rec.CommMedium}");
        }

        int settlementCount = 0; float pop = 0f;
        foreach (var s in mgr.Settlements)
            if (s.OwnerCivId == playerCommunityId) { settlementCount++; pop += s.Population; }
        SmallLabel(x, ref y, w, $"Settlements  : {settlementCount}  (see Civilization > Settlements)");
        SmallLabel(x, ref y, w, $"Population   : {pop:F0}");
        SmallLabel(x, ref y, w, $"Resilience   : {civ.Resilience:P0}{(civ.HasCollapsed ? "  — COLLAPSED" : "")}");
        if (civ.Roster.Count > 1)
            SmallLabel(x, ref y, w, $"Roster       : {civ.Roster.Count} communities  (Diversity {Era3Polity.RosterShannonDiversity(civ.Roster):F2})");

        // population-energy-aggregation-spec.md's Cohort model tracks real, continuously-updated
        // trait data for the civ's own population (nudged by every absorption/birth event) — this is
        // MORE than the frozen Founder* snapshot above ever gave, so show it distinctly.
        var coreCohort = mgr.FindCivCoreCohort(playerCommunityId);
        if (coreCohort != null)
        {
            y += 4f;
            Section(x, ref y, w, "CURRENT POPULATION (live, evolving)");
            SmallLabel(x, ref y, w, $"Metabolism   : {coreCohort.Traits.Metabolism}   Backbone: {coreCohort.Traits.Backbone}");
            SmallLabel(x, ref y, w, $"Mean size    : {coreCohort.Traits.MeanSizeScale:F3}  (σ² {coreCohort.Traits.VarianceSizeScale:F4})");
            SmallLabel(x, ref y, w, $"Photo eff.   : {coreCohort.Traits.MeanPhotoEfficiency:F2}   Chemo eff.: {coreCohort.Traits.MeanChemoEfficiency:F2}");
            SmallLabel(x, ref y, w, $"Core biomass : {coreCohort.Biomass:F1}  (this settlement's population cohort)");
        }

        y += 4f;
        Section(x, ref y, w, "ERA 3");
        SmallLabel(x, ref y, w, "Full policy, settlement, tech/idea, polity, and warfare controls are");
        SmallLabel(x, ref y, w, "in the Civilization panel (top-left) — this page is identification-only.");
    }

    private void DrawRanksPage(float x, ref float y, float w)
    {
        string[] subTabs = { "Pop", "Str", "Intel" };
        float stW = w / subTabs.Length;
        for (int i = 0; i < subTabs.Length; i++)
        {
            bool active = (int)_rankTab == i;
            if (GUI.Button(new Rect(x + i * stW, y, stW - 2f, 22f), subTabs[i],
                    active ? ActiveTabStyle() : InactiveTabStyle()))
                _rankTab = (RankTab)i;
        }
        y += 26f;
        DrawRect(x, y, w, 1f, new Color(0.4f, 0.4f, 0.4f, 0.6f));
        y += 4f;

        if (agentSpawner == null) { SmallLabel(x, ref y, w, "(no data)"); return; }

        var pop   = new Dictionary<int, int>();
        var str   = new Dictionary<int, float>();
        var color = new Dictionary<int, Color>();

        foreach (var a in agentSpawner.ActiveAgents)
        {
            if (a == null) continue;
            int c = a.communityId;
            pop.TryGetValue(c, out int pc); pop[c] = pc + 1;
            str.TryGetValue(c, out float s); str[c] = s + a.strengthTrait;
            if (!color.ContainsKey(c)) color[c] = a.lineageColor;
        }

        // Once a community is fully civilized, its population lives in Era 3 settlements, not
        // ActiveAgents — the loop above then contributes NOTHING for it, and since `ids` (below) is
        // built purely from `pop`'s keys, a civ with real, growing population could vanish from the
        // rankings entirely even though the top-strip "Pop" total already accounts for settlements
        // (see Era3Manager.TotalSettlementPopulation). Add it back in here, additively, same pattern.
        if (Era3Manager.Instance != null && Era3Manager.Instance.IsActive)
        {
            foreach (var s in Era3Manager.Instance.Settlements)
            {
                if (s.OwnerCivId < 0) continue;
                pop.TryGetValue(s.OwnerCivId, out int pc);
                pop[s.OwnerCivId] = pc + Mathf.RoundToInt(s.Population);
                if (!color.ContainsKey(s.OwnerCivId)) color[s.OwnerCivId] = Era3VisualManager.CivColor(s.OwnerCivId);
            }
        }

        // Build intelligence totals from Era2Manager if active.
        var intel = new Dictionary<int, float>(); // total II × population
        bool era2Active = Era2Manager.Instance != null && Era2Manager.Instance.IsActive;
        if (era2Active)
        {
            foreach (var rec in Era2Manager.Instance.AllRecords)
            {
                int n = pop.GetValueOrDefault(rec.communityId, 1);
                intel[rec.communityId] = rec.II * n;
            }
        }

        var ids = new List<int>(pop.Keys);
        ids.Sort((a, b) => RankMetric(b, pop, str, intel).CompareTo(RankMetric(a, pop, str, intel)));

        string metricLabel = _rankTab switch
        {
            RankTab.Population    => "POP",
            RankTab.Strength      => "STR (total)",
            _                     => "INTEL (total)",
        };
        Section(x, ref y, w, $"RANKINGS — {metricLabel}");

        int rank = 1;
        foreach (int cid in ids)
        {
            if (rank > 15) break;
            bool isPlayer = cid == playerCommunityId;

            Rect rowRect = new Rect(x - 2f, y - 1f, w + 4f, 16f);
            if (isPlayer)
                DrawRect(rowRect.x, rowRect.y, rowRect.width, rowRect.height,
                    new Color(1f, 0.85f, 0.4f, 0.10f));

            bool renamingThisRow = isPlayer && _renamingCommunity;

            if (renamingThisRow)
            {
                // Inline rename: text field + OK/Cancel replace the row for the duration of editing.
                // Deliberately skip the row-focus button this frame so clicks/typing inside the field
                // don't also swing the camera.
                GUI.SetNextControlName(RenameFieldControlName);
                _renameBuffer = GUI.TextField(new Rect(x + 14f, y, w - 90f, 16f), _renameBuffer, 24);
                bool confirmedByEnter = Event.current.type == EventType.KeyDown
                    && Event.current.keyCode == KeyCode.Return
                    && GUI.GetNameOfFocusedControl() == RenameFieldControlName;
                if (confirmedByEnter) Event.current.Use();
                if (confirmedByEnter || GUI.Button(new Rect(x + w - 74f, y, 34f, 16f), "OK", BtnStyle(9)))
                {
                    CommunityNameRegistry.SetName(cid, _renameBuffer);
                    _renamingCommunity = false;
                }
                if (GUI.Button(new Rect(x + w - 38f, y, 34f, 16f), "X", BtnStyle(9)))
                    _renamingCommunity = false;
                y += 16f;
                rank++;
                continue;
            }

            // For the player's row, a name-sized button is checked BEFORE the full-row focus button
            // so clicking the name opens rename mode instead of swinging the camera (IMGUI dispatches
            // overlapping Button hit-tests in call order, so whichever runs first wins the click).
            if (isPlayer)
            {
                Rect nameRect = new Rect(x + 14f, y, w - 62f, 16f);
                if (GUI.Button(nameRect, "", GUIStyle.none))
                {
                    _renamingCommunity = true;
                    _renameBuffer = CommunityNameRegistry.GetName(cid);
                    GUI.FocusControl(RenameFieldControlName);
                }
            }

            if (GUI.Button(rowRect, "", GUIStyle.none))
                FocusOnCommunity(cid);

            if (color.TryGetValue(cid, out Color cc))
            {
                Color prev = GUI.color; GUI.color = cc;
                GUI.DrawTexture(new Rect(x, y + 2f, 10f, 10f), Texture2D.whiteTexture);
                GUI.color = prev;
            }

            float metric = RankMetric(cid, pop, str, intel);
            string tag = isPlayer ? "★" : $"{rank}";
            string valStr;
            if (_rankTab == RankTab.Population)
                valStr = $"{(int)metric}";
            else if (_rankTab == RankTab.Intelligence)
            {
                int n = Mathf.Max(1, pop.GetValueOrDefault(cid));
                float perCapita = intel.GetValueOrDefault(cid) / n;
                valStr = $"{metric:F0} ({perCapita:F1}/c)";
            }
            else
                valStr = $"{metric:F0}";

            SmallLabelAt(x + 14f, y, w - 48f, $"{tag} {CommunityNameRegistry.GetName(cid)}");
            SmallLabelAt(x + w - 42f, y, 42f, valStr);
            y += 16f;

            // Architecture tag shown on Intel tab or always when Era 2 active.
            if (era2Active)
            {
                var era2rec = Era2Manager.Instance.GetRecord(cid);
                if (era2rec != null && era2rec.Architecture != CognitiveArchitecture.Unresolved)
                {
                    string archTag = era2rec.Architecture switch
                    {
                        CognitiveArchitecture.Individuated => "Ind",
                        CognitiveArchitecture.Distributed  => "Dis",
                        CognitiveArchitecture.Collective   => "Col",
                        _                                  => "---"
                    };
                    if (_rankTab != RankTab.Intelligence)
                        SmallLabelAt(x + 14f, y, w, $"  [{archTag}] II:{era2rec.II:F1}");
                    else
                        SmallLabelAt(x + 14f, y, w, $"  [{archTag}]");
                    y += 13f;
                }
            }
            rank++;
        }
    }

    private void DrawSettingsPage(float x, ref float y, float w)
    {
        Section(x, ref y, w, LocalizationManager.L("sec_visuals"));

        bool atmosOn = _atmosVisual == null || _atmosVisual.Visible;
        if (GUI.Button(new Rect(x, y, w, 22f),
                atmosOn ? LocalizationManager.L("btn_atmo_on") : LocalizationManager.L("btn_atmo_off"),
                BtnStyle(11)))
            if (_atmosVisual != null) _atmosVisual.SetVisible(!_atmosVisual.Visible);
        y += 26f;

        if (GUI.Button(new Rect(x, y, w, 22f),
                _showMarkers ? LocalizationManager.L("btn_markers_on") : LocalizationManager.L("btn_markers_off"),
                BtnStyle(11)))
            _showMarkers = !_showMarkers;
        y += 26f;

        bool locked = _orbitCam != null && _orbitCam.PlanetLockEnabled;
        if (GUI.Button(new Rect(x, y, w, 22f),
                locked ? LocalizationManager.L("btn_lock_on") : LocalizationManager.L("btn_lock_off"),
                BtnStyle(11)))
        {
            if (locked) _orbitCam?.DisablePlanetLock();
            else        _orbitCam?.EnablePlanetLock();
        }
        y += 26f;

        // Era 3 game speed (time-scale-spec §5.1) — cycles through the five presets on click (a
        // dropdown would need new IMGUI plumbing; a cycling button matches every other Settings
        // toggle here). Only real-time pacing changes — years-per-tick stays fixed (spec §2).
        if (GUI.Button(new Rect(x, y, w, 22f),
                $"Era 3 speed: {Era3Calendar.SpeedLabel(Era3Calendar.Speed)}", BtnStyle(11)))
        {
            Era3Calendar.Speed = (Era3Calendar.GameSpeed)(((int)Era3Calendar.Speed + 1) % 5);
        }
        y += 26f;

        y += 4f;
        Section(x, ref y, w, LocalizationManager.L("sec_star"));
        var sr = SolarSystemRuntime.Instance;
        if (sr?.SolarSystem != null)
        {
            var sys = sr.SolarSystem;
            SmallLabel(x, ref y, w, $"Star: {sys.Star.SpectralClass}-class  L={sys.Star.LuminositySolar:F2} sol");
            SmallLabel(x, ref y, w, $"HZ: {sys.HabitableZoneInnerAU:F2}–{sys.HabitableZoneOuterAU:F2} AU");
            SmallLabel(x, ref y, w, $"Planet orbit: {sys.LifePlanetOrbitAU:F2} AU");
            bool inHz = sys.LifePlanetInHabitableZone;
            Color prev = GUI.color;
            GUI.color = inHz ? new Color(0.3f, 0.9f, 0.4f) : new Color(0.9f, 0.5f, 0.3f);
            SmallLabel(x, ref y, w, inHz ? LocalizationManager.L("lbl_in_hz") : LocalizationManager.L("lbl_out_hz"));
            GUI.color = prev;
        }
        else SmallLabel(x, ref y, w, "(no solar system data)");

        if (OrbitalSeasons.Instance != null)
        {
            var os = OrbitalSeasons.Instance;
            SmallLabel(x, ref y, w, $"Eccentricity: {os.Eccentricity:F2}  Tilt: {os.AxialTiltDeg:F0}°");
            SmallLabel(x, ref y, w, $"Orbital phase: {os.OrbitalPhase01 * 100f:F0}%");
        }

        y += 4f;
        Section(x, ref y, w, LocalizationManager.L("sec_debug"));
        if (GUI.Button(new Rect(x, y, w, 22f),
                SuppressRawOverlays
                    ? LocalizationManager.L("btn_overlays_hidden")
                    : LocalizationManager.L("btn_overlays_vis"),
                BtnStyle(11)))
            SuppressRawOverlays = !SuppressRawOverlays;
        y += 26f;
    }

    // ── Camera navigation & community markers ────────────────────────────────

    /// Swings the orbit camera toward the centroid of all agents in the given community
    /// and marks them with screen-space dots until another community is selected.
    private void FocusOnCommunity(int communityId)
    {
        _highlightCommunityId = communityId;
        _showMarkers          = true;

        if (agentSpawner == null || _orbitCam == null || _orbitCam.target == null) return;

        Vector3 center   = _orbitCam.target.position;
        Vector3 centroid = Vector3.zero;
        int     count    = 0;

        foreach (var a in agentSpawner.ActiveAgents)
        {
            if (a == null || a.communityId != communityId) continue;
            centroid += a.transform.position;
            count++;
        }

        if (count == 0) return;

        centroid /= count;
        Vector3 dir = (centroid - center).normalized;
        if (dir == Vector3.zero) dir = Vector3.up;

        // Preserve the player's current zoom rather than snapping to a fixed close-in distance —
        // same fix as Era3HUD's settlement-focus click, for the same reason (jarring, too close).
        _orbitCam.FocusOnDirection(dir, _orbitCam.distance);
        _orbitCam.EnablePlanetLock();
    }

    /// Draws a small screen-space dot over every agent belonging to the highlighted community.
    /// A dark border ring makes the dot readable against both bright and dark backgrounds.
    private void DrawCommunityMarkers()
    {
        if (_markerPositions.Count == 0) return;

        const float DotR  = 5f;
        const float RingR = 7f;

        Color prev = GUI.color;

        // Dark outline rings.
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        foreach (var p in _markerPositions)
            GUI.DrawTexture(new Rect(p.x - RingR, p.y - RingR, RingR * 2f, RingR * 2f),
                Texture2D.whiteTexture);

        // Colored fill dots.
        GUI.color = _markerColor;
        foreach (var p in _markerPositions)
            GUI.DrawTexture(new Rect(p.x - DotR, p.y - DotR, DotR * 2f, DotR * 2f),
                Texture2D.whiteTexture);

        GUI.color = prev;
    }

    // ── Drawing helpers ───────────────────────────────────────────────────────

    private float RankMetric(int cid,
        Dictionary<int, int>   pop,
        Dictionary<int, float> str,
        Dictionary<int, float> intel)
    {
        return _rankTab switch
        {
            RankTab.Population    => pop.GetValueOrDefault(cid),
            RankTab.Strength      => str.GetValueOrDefault(cid),   // total strength × population
            _                     => intel.GetValueOrDefault(cid),  // total II × population
        };
    }

    private void Section(float x, ref float y, float w, string title)
    {
        DrawRect(x - 2f, y, w + 4f, 17f, new Color(1f, 0.85f, 0.4f, 0.12f));
        var s = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold };
        s.normal.textColor = new Color(1f, 0.85f, 0.4f);
        GUI.Label(new Rect(x, y + 1f, w, 15f), title, s);
        y += 19f;
    }

    private void SmallLabel(float x, ref float y, float w, string text)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = 11 };
        s.normal.textColor = Color.white;
        GUI.Label(new Rect(x, y, w, 16f), text, s);
        y += 16f;
    }

    private void SmallLabelAt(float x, float y, float w, string text)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = 11 };
        s.normal.textColor = Color.white;
        GUI.Label(new Rect(x, y, w, 16f), text, s);
    }

    private void TraitBar(float x, ref float y, float w, string label, float value)
    {
        var ls = new GUIStyle(GUI.skin.label) { fontSize = 10 };
        ls.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
        GUI.Label(new Rect(x, y, 74f, 14f), label, ls);
        float barX = x + 78f;
        float barW = w - 78f - 28f;
        DrawRect(barX, y + 2f, barW, 10f, new Color(0.18f, 0.18f, 0.18f, 0.9f));
        float fill = Mathf.Clamp01(value / 100f);
        DrawRect(barX, y + 2f, barW * fill, 10f,
            Color.Lerp(new Color(0.25f, 0.65f, 0.35f), new Color(0.9f, 0.65f, 0.15f), fill));
        var vs = new GUIStyle(GUI.skin.label) { fontSize = 10 };
        vs.normal.textColor = Color.white;
        GUI.Label(new Rect(barX + barW + 2f, y, 26f, 14f), $"{value:F0}", vs);
        y += 15f;
    }

    private void GasBar(float x, ref float y, float w, string label, float fraction, Color barColor)
    {
        var ls = new GUIStyle(GUI.skin.label) { fontSize = 10 };
        ls.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
        GUI.Label(new Rect(x, y, 86f, 14f), label, ls);
        float barX = x + 90f;
        float barW = w - 90f - 36f;
        DrawRect(barX, y + 2f, barW, 10f, new Color(0.18f, 0.18f, 0.18f, 0.9f));
        DrawRect(barX, y + 2f, barW * Mathf.Clamp01(fraction), 10f, barColor);
        var vs = new GUIStyle(GUI.skin.label) { fontSize = 10 };
        vs.normal.textColor = Color.white;
        GUI.Label(new Rect(barX + barW + 2f, y, 36f, 14f), $"{fraction * 100f:F1}%", vs);
        y += 15f;
    }

    private static void DrawRect(float x, float y, float w, float h, Color color)
    {
        Color prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = prev;
    }

    private static GUIStyle BtnStyle(int size)
    {
        var s = new GUIStyle(GUI.skin.button) { fontSize = size };
        s.normal.textColor = Color.white;
        return s;
    }

    private static GUIStyle ActiveTabStyle()
    {
        var s = new GUIStyle(GUI.skin.button) { fontSize = 11, fontStyle = FontStyle.Bold };
        s.normal.textColor = new Color(1f, 0.85f, 0.4f);
        return s;
    }

    private static GUIStyle InactiveTabStyle()
    {
        var s = new GUIStyle(GUI.skin.button) { fontSize = 11 };
        s.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
        return s;
    }

    private AgentController FindPlayerAgent()
    {
        if (agentSpawner == null) return null;
        AgentController best = null;
        float bestAge = -1f;
        foreach (var a in agentSpawner.ActiveAgents)
        {
            if (a == null || a.communityId != playerCommunityId) continue;
            if (a.AgeSeconds > bestAge) { bestAge = a.AgeSeconds; best = a; }
        }
        return best;
    }

    /// Returns a compact metabolism label for the community. If all members share the same
    /// metabolism it shows just the name; if there's a mix it shows each type with its count
    /// so players can see a transition in progress (e.g. "Hetero ×5  Chemo ×2").
    private string CommunityMetabolismLabel(int communityId)
    {
        if (agentSpawner == null) return "—";
        int chemo = 0, photo = 0, hetero = 0;
        foreach (var a in agentSpawner.ActiveAgents)
        {
            if (a == null || a.communityId != communityId) continue;
            switch (a.Metabolism)
            {
                case MetabolismType.Chemosynthetic: chemo++;  break;
                case MetabolismType.Phototrophic:   photo++;  break;
                case MetabolismType.Heterotrophic:  hetero++; break;
            }
        }
        int kinds = (chemo > 0 ? 1 : 0) + (photo > 0 ? 1 : 0) + (hetero > 0 ? 1 : 0);
        if (kinds == 0) return "—";
        if (kinds == 1)
        {
            if (chemo  > 0) return "Chemosynthetic";
            if (photo  > 0) return "Phototrophic";
            return "Heterotrophic";
        }
        // Mixed community — show counts so the player can see the transition in progress.
        var parts = new System.Collections.Generic.List<string>(3);
        if (hetero > 0) parts.Add($"Hetero ×{hetero}");
        if (photo  > 0) parts.Add($"Photo ×{photo}");
        if (chemo  > 0) parts.Add($"Chemo ×{chemo}");
        return string.Join("  ", parts);
    }

    /// Returns true if ANY agent in the community has motility — used so a gene choice
    /// that was applied to one agent (and then broadcast) shows immediately in the HUD
    /// even if the displayed "oldest" agent happened to already have a different state.
    private bool CommunityHasMotility(int communityId)
    {
        if (agentSpawner == null) return false;
        foreach (var a in agentSpawner.ActiveAgents)
            if (a != null && a.communityId == communityId && a.HasMotility) return true;
        return false;
    }
}
