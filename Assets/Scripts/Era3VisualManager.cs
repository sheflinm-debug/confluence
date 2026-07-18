using System.Collections.Generic;
using UnityEngine;

/// Gives Era 3 (The Commerce Engine) a WORLD representation. Until now Era 3 was a headless data
/// simulation — civilizations, settlements and polities existed only as records and log lines, with
/// nothing drawn on the planet. This manager renders that data:
///
///   • Settlement markers — a building-like marker per Era3Manager.Settlement, placed at its world
///     position, scaled by tier (Village → Town → City), tinted by its owning civilization, and
///     labelled with name + population. Parented to the planet so they co-rotate with the terrain.
///
///   • Civilization territory + borders — a transparent vertex-coloured overlay mesh (same pattern as
///     MineralOverlayManager) that fills each civ's claimed ground in the civ's colour and draws a
///     dark seam wherever two civs (or a civ and unclaimed land) meet, so polities read as distinct
///     regions with CLEAR borders.
///
/// Wire up via SimulationBootstrap after tectonics are generated and Era3Manager is initialised.
public class Era3VisualManager : MonoBehaviour
{
    public static Era3VisualManager Instance { get; private set; }

    private TectonicResult _tectonics;
    private float _planetRadius;
    private float _elevationWorldScale;
    private Transform _planet;           // planet root; children co-rotate with the terrain spin

    // Agents themselves sit at bare planetRadius (SphereSurface.ProjectToSurface uses no offset), so
    // settlement bases must match that exactly — not the terrain-hugging overlays' small offset (those
    // are flat decals that need a hair of clearance to avoid z-fighting; a 3D marker with agents
    // standing at its base doesn't, and any offset here shows as visibly floating above the ground/feet).
    private const float ShellOffset = 0f;
    private const float RefreshInterval = 1.0f;
    private float _refreshTimer;
    private int _lastSignature = -1;

    private readonly Dictionary<int, GameObject> _markers = new Dictionary<int, GameObject>();
    // appearance-generation-spec §4.7 — real per-structure markers, keyed "{civId}:{structureName}".
    // First-pass simplification: all of a civ's BuiltStructures cluster around its largest/capital
    // settlement rather than the spec's full per-settlement slot-capacity distribution model.
    private readonly Dictionary<string, GameObject> _structureMarkers = new Dictionary<string, GameObject>();
    // Planet-LOCAL unit direction per settlement, cached the first time we see it. Must be computed
    // once (near founding) and reused: the settlement's stored Position is a founding-time WORLD point,
    // so converting it through the planet's CURRENT rotation on every rebuild would make settlements
    // drift as the terrain spins. Caching the local direction pins them to the terrain.
    private readonly Dictionary<int, Vector3> _localDir = new Dictionary<int, Vector3>();
    private GameObject _territoryGo;
    private Material _markerMat, _territoryMat;

    /// The settlement's CURRENT world position, accounting for the planet's rotation since founding —
    /// Settlement.Position itself is a one-time founding-time snapshot that goes stale as the planet
    /// spins (this is what the marker's own rendering already correctly compensates for via the
    /// cached local direction below). Any external system that needs to aim at or measure distance to
    /// a settlement "where it actually is right now" — camera focus, absorption radius, war strike
    /// range — must use this, not s.Position directly, or it silently drifts wrong over time. Falls
    /// back to the stale s.Position only if the marker hasn't been created yet (e.g. same-frame as
    /// founding, before the next SyncMarkers pass).
    public Vector3 GetCurrentWorldPosition(Era3Manager.Settlement s) =>
        _markers.TryGetValue(s.Id, out var go) && go != null ? go.transform.position : s.Position;

    private Vector3 LocalDir(Era3Manager.Settlement s)
    {
        if (!_localDir.TryGetValue(s.Id, out var d))
        {
            d = _planet.InverseTransformPoint(s.Position).normalized;
            _localDir[s.Id] = d;
        }
        return d;
    }

    // Extra radial clearance for settlements founded on a submerged point, cached once per settlement
    // (like _localDir) from the liquid depth AT FOUNDING TIME. Without this a sea-based civ's
    // settlement sits at bare ground level — same radius agents stand at — which is BELOW the liquid
    // shell's surface, so the marker renders hidden under/inside the translucent liquid mesh and
    // effectively never shows up. A sea settlement is real (stilts, a floating platform, a reef
    // structure breaching the surface) so it should visibly clear the water, not vanish beneath it.
    private readonly Dictionary<int, float> _waterClearance = new Dictionary<int, float>();

    private float WaterClearance(Era3Manager.Settlement s)
    {
        if (!_waterClearance.TryGetValue(s.Id, out var c))
        {
            float depth = FluidDynamicsManager.Instance != null
                ? FluidDynamicsManager.Instance.GetLiquidDepthNearPosition(s.Position)
                : 0f;
            // Clear the surface with a bit to spare (0.15) so it doesn't sit exactly at the waterline.
            c = depth > 0f ? depth + 0.15f : 0f;
            _waterClearance[s.Id] = c;
        }
        return c;
    }

    void Awake() { if (Instance == null) Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    public void Init(TectonicResult tectonics, float planetRadius, float elevationWorldScale, Transform planet)
    {
        Instance = this;
        _tectonics = tectonics;
        _planetRadius = planetRadius;
        _elevationWorldScale = elevationWorldScale;
        _planet = planet;

        _markerMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        _territoryMat = new Material(Shader.Find("Custom/VertexColorTransparentURP"));

        _territoryGo = new GameObject("Era3Territory");
        _territoryGo.transform.SetParent(_planet, worldPositionStays: true);
        var mf = _territoryGo.AddComponent<MeshFilter>();
        var mr = _territoryGo.AddComponent<MeshRenderer>();
        mr.sharedMaterial = _territoryMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mf.mesh = new Mesh();
    }

    // ── Civilization colours ────────────────────────────────────────────────────────────────
    /// Stable per-civ colour. Player civ (id 0) is a distinct gold; others get golden-ratio-spaced hues.
    public static Color CivColor(int civId)
    {
        if (civId < 0) return new Color(0.5f, 0.5f, 0.5f);
        if (civId == 0)
        {
            // Stays gold on ordinary worlds — ContrastHueNear only nudges away from the branded
            // hue when it actually clashes with THIS world's ground/ocean (e.g. a sandy/gold-toned
            // planet), rather than jumping to an unrelated scheme every time.
            Color.RGBToHSV(new Color(1f, 0.82f, 0.15f), out float goldHue, out _, out _);
            return Color.HSVToRGB(PlanetPalette.ContrastHueNear(goldHue), 0.82f, 1f);
        }
        // Contrast-aware hue (PlanetPalette) — dodges the ground/ocean bands so settlement markers
        // and territory overlays don't visually melt into the terrain they sit on.
        float hue = PlanetPalette.ContrastHueForId(civId);
        return Color.HSVToRGB(hue, 0.72f, 0.95f);
    }

    void Update()
    {
        if (_tectonics == null) return;
        var mgr = Era3Manager.Instance;
        bool active = mgr != null && mgr.IsActive;

        // Show/hide the whole layer with Era 3.
        if (_territoryGo != null && _territoryGo.activeSelf != active) _territoryGo.SetActive(active);
        if (!active) { if (_markers.Count > 0) ClearMarkers(); return; }

        _refreshTimer -= Time.deltaTime;
        if (_refreshTimer <= 0f)
        {
            _refreshTimer = RefreshInterval;
            int sig = SettlementSignature(mgr);
            if (sig != _lastSignature) // something changed — full rebuild (also applies base colors)
            {
                _lastSignature = sig;
                SyncMarkers(mgr);
                SyncStructures(mgr);
                SyncGrowthNetwork(mgr);
                SyncGuestNesting(mgr);
                RebuildTerritory(mgr);
            }
        }

        // Attack-flash pulse runs EVERY frame, independent of the signature throttle above — a
        // settlement under attack needs a smooth animated pulse even on frames where nothing else
        // about the settlement data changed, and needs to cleanly revert to its normal (possibly
        // multispecies-tinted) color the moment the flash expires.
        ApplyAttackFlashes(mgr);
    }

    /// Pulses a settlement's marker toward red while it has a live entry in Era3Manager's
    /// RecentAttackFlash (a war strike just landed on it), then reverts cleanly to its normal —
    /// possibly multispecies-tinted — color once the flash expires. See TickConflict in Era3Manager
    /// for what actually triggers a flash (a real conquest or biochemical strike, not a stat nudge).
    private readonly List<int> _expiredFlashes = new List<int>();
    private void ApplyAttackFlashes(Era3Manager mgr)
    {
        if (mgr == null || mgr.RecentAttackFlash.Count == 0) return;
        _expiredFlashes.Clear();
        foreach (var kv in mgr.RecentAttackFlash)
        {
            int settlementId = kv.Key; float expiry = kv.Value;
            if (!_markers.TryGetValue(settlementId, out var go) || go == null) continue;
            Era3Manager.Settlement s = null;
            foreach (var candidate in mgr.Settlements) if (candidate.Id == settlementId) { s = candidate; break; }
            if (s == null) continue;

            Color baseColor = MultispeciesTint(s, CivColor(s.OwnerCivId >= 0 ? s.OwnerCivId : s.FounderCivId));
            Color finalColor;
            if (Time.time < expiry)
            {
                float pulse = (Mathf.Sin(Time.time * 8f) + 1f) * 0.5f; // fast 0..1 pulse
                finalColor = Color.Lerp(baseColor, Color.red, 0.4f + pulse * 0.5f);
            }
            else
            {
                finalColor = baseColor;
                _expiredFlashes.Add(settlementId);
            }

            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", finalColor);
            go.GetComponent<Renderer>().SetPropertyBlock(mpb);
        }
        foreach (var id in _expiredFlashes) mgr.RecentAttackFlash.Remove(id);
    }

    /// Blends a settlement's base civ color toward white in proportion to how many distinct
    /// communities have been absorbed into it — a quick, readable "this settlement is diverse" tell
    /// without needing separate geometry. A single-species settlement is untouched.
    private static Color MultispeciesTint(Era3Manager.Settlement s, Color baseColor)
    {
        int extra = Mathf.Max(0, s.ContributingCommunities.Count - 1);
        if (extra <= 0) return baseColor;
        float blend = Mathf.Clamp01(0.18f + extra * 0.12f);
        return Color.Lerp(baseColor, Color.white, blend);
    }

    /// Cheap hash of the settlement set so we only rebuild when something actually changes
    /// (a settlement founded, a tier upgrade, or an ownership flip).
    private static int SettlementSignature(Era3Manager mgr)
    {
        int h = 17;
        foreach (var s in mgr.Settlements)
            // ContributingCommunities.Count catches a newly-multispecies settlement (absorbed via
            // TickSettlementAbsorption, no tier/owner change). RecognizedOwnerCivId catches a peace
            // treaty formalizing occupied territory (OwnerCivId itself doesn't change there — only
            // RecognizedOwnerCivId does, converting striped occupation to a solid claim).
            h = h * 31 + s.Id * 131 + (int)s.Tier * 7 + (s.OwnerCivId + 2) * 3
                + s.ContributingCommunities.Count * 13 + (s.RecognizedOwnerCivId + 2) * 7;
        // BuiltStructures count per civ — so a newly-earned structure (TickStructures) triggers a
        // rebuild even though nothing about the settlement records themselves changed. Living Reef's
        // growth-network node count (SyncGrowthNetwork) derives from InvestEconomic/Stockpile rather
        // than BuiltStructures (it has none), so those are folded in too, bucketed the same way
        // SyncGrowthNetwork itself buckets them — otherwise a Living Reef civ's growing mat would
        // never actually trigger a rebuild.
        foreach (var civ in mgr.AllCivsView)
        {
            h = h * 31 + civ.CommunityId * 17 + civ.BuiltStructures.Count * 53;
            if (civ.Path == Era3Path.LivingReef)
                // era3-systems-implementation-spec §6: Stockpile retired — Economy.Stock[Industry] is
                // the replacement "accumulated capacity" signal.
                h = h * 31 + Mathf.FloorToInt(civ.InvestEconomic * 10f + (civ.Economy?.Stock[CivilizationEconomy.Industry] ?? 0f) * 2f) * 41;
        }
        // host-guest-relation-spec: a footprint/state change (SyncGuestNesting) doesn't touch
        // BuiltStructures or any settlement record, so it needs its own signature contribution.
        foreach (var rel in mgr.HostGuestRelations)
            h = h * 31 + rel.HostCivId * 19 + rel.GuestCivId * 23 + rel.SubstrateFootprint * 59 + (int)rel.State * 5;
        return h;
    }

    // ── Settlement markers ──────────────────────────────────────────────────────────────────
    private void SyncMarkers(Era3Manager mgr)
    {
        var live = new HashSet<int>();
        foreach (var s in mgr.Settlements)
        {
            live.Add(s.Id);
            if (!_markers.TryGetValue(s.Id, out var go) || go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Settlement_{s.Id}";
                Destroy(go.GetComponent<Collider>());
                go.GetComponent<Renderer>().sharedMaterial = _markerMat; // shared base; per-instance colour via MPB
                go.transform.SetParent(_planet, worldPositionStays: true);
                _markers[s.Id] = go;
            }

            // Cached planet-local direction so the marker rides the terrain spin as a child of the
            // planet (like the mineral/ice overlays) without drifting.
            Vector3 dir = LocalDir(s);
            float baseR = _planetRadius + ShellOffset + WaterClearance(s);
            go.transform.localPosition = dir * (baseR + TierHeight(s.Tier) * 0.5f);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);

            float w = TierFootprint(s.Tier);
            go.transform.localScale = new Vector3(w, TierHeight(s.Tier), w);

            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", MultispeciesTint(s, CivColor(s.OwnerCivId >= 0 ? s.OwnerCivId : s.FounderCivId)));
            go.GetComponent<Renderer>().SetPropertyBlock(mpb);
        }

        // Remove markers for settlements that no longer exist.
        _cleanup.Clear();
        foreach (var kv in _markers) if (!live.Contains(kv.Key)) _cleanup.Add(kv.Key);
        foreach (var id in _cleanup) { if (_markers[id] != null) Destroy(_markers[id]); _markers.Remove(id); _localDir.Remove(id); }
    }

    // ── Structure markers (appearance-generation-spec §4.2/§4.3/§4.7/§4.8) ────────────────────
    // Real geometry per StructureInstance in a civ's BuiltStructures (Era3Manager.TickStructures) —
    // a discrete-placement generator for the two tracks that have one (Commerce Engine Individuated,
    // Apex Predator; §4.2/§4.3). Distributed/Collective Commerce Engine civs use the growth-network
    // generator instead (SyncGrowthNetwork, §4.3); Living Reef/Terraformer/Bloom Front have no
    // discrete-building concept at all (§4.4/§4.5).
    private const int MaxVisibleStructures = 18; // matches the largest §4.7.1 slot_capacity tier
    private void SyncStructures(Era3Manager mgr)
    {
        var live = new HashSet<string>();
        foreach (var civ in mgr.AllCivsView)
        {
            if (civ.HasCollapsed || civ.BuiltStructures.Count == 0) continue;
            // §4.3: only Individuated uses discrete site-placement within Commerce Engine — Apex
            // Predator uses the same family (§4.6) with its own (already track-restricted) name
            // vocabulary. Distributed/Collective render via SyncGrowthNetwork instead.
            bool discretePlacement = civ.Path == Era3Path.ApexPredator
                || (civ.Path == Era3Path.CommerceEngine && civ.Architecture == CognitiveArchitecture.Individuated);
            if (!discretePlacement) continue;

            Era3Manager.Settlement capital = null;
            foreach (var s in mgr.Settlements)
                if (s.OwnerCivId == civ.CommunityId && (capital == null || s.Population > capital.Population))
                    capital = s;
            if (capital == null) continue;

            Vector3 dir = LocalDir(capital);
            float baseR = _planetRadius + ShellOffset + WaterClearance(capital);
            Vector3 centerLocal = dir * (baseR + TierHeight(capital.Tier));
            Vector3 tangent = Vector3.Cross(dir, Vector3.up);
            if (tangent.sqrMagnitude < 0.01f) tangent = Vector3.Cross(dir, Vector3.right);
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(dir, tangent);

            int techTier = Era3TechTree.GetTechTier(civ);
            int count = Mathf.Min(civ.BuiltStructures.Count, MaxVisibleStructures);
            int i = 0;
            foreach (var inst in civ.BuiltStructures)
            {
                if (i >= MaxVisibleStructures) break;
                string key = $"{civ.CommunityId}:{i}";
                live.Add(key);
                if (!_structureMarkers.TryGetValue(key, out var go) || go == null)
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = $"Structure_{key}";
                    Destroy(go.GetComponent<Collider>());
                    go.GetComponent<Renderer>().sharedMaterial = _markerMat;
                    go.transform.SetParent(_planet, worldPositionStays: true);
                    _structureMarkers[key] = go;
                }

                float angle = (i / (float)count) * Mathf.PI * 2f;
                Vector3 ringOffset = (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * (TierFootprint(capital.Tier) * 0.9f);
                var (w, h, tint) = StructureVisual(inst.Name);
                // §4.7.2 height tier: each step roughly doubles apparent height, capped by the tech
                // tier's own max (Era3Manager.MaxHeightTierByTechTier) — "high population -> dense
                // high-rise residences" made visible.
                float heightMult = 1f + inst.HeightTier * 0.6f;
                h *= heightMult;
                go.transform.localPosition = centerLocal + ringOffset + dir * (h * 0.5f);
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
                go.transform.localScale = new Vector3(w, h, w);

                // §4.8: tech-tier material blended with the structure's own category tint, then
                // toward the civ's own color — three-way blend so tier, category, and ownership all
                // stay legible on the same marker.
                Color materialTint = Color.Lerp(tint, TechTierTint(techTier), 0.5f);
                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", Color.Lerp(CivColor(civ.CommunityId), materialTint, 0.6f));
                go.GetComponent<Renderer>().SetPropertyBlock(mpb);
                i++;
            }
        }

        _structureCleanup.Clear();
        foreach (var kv in _structureMarkers) if (!live.Contains(kv.Key)) _structureCleanup.Add(kv.Key);
        foreach (var key in _structureCleanup) { if (_structureMarkers[key] != null) Destroy(_structureMarkers[key]); _structureMarkers.Remove(key); }
    }
    private readonly List<string> _structureCleanup = new List<string>();

    /// appearance-generation-spec §4.8 tech-tier material palette — a color-progression stand-in for
    /// the spec's literal material table (wood/hide -> fired brick -> cut stone -> steel/glass ->
    /// composites): warm/earthy/matte at low tech, cool/refined/bright at high tech, so same-tier
    /// structures visibly share a material language regardless of category.
    private static Color TechTierTint(int techTier) => techTier switch
    {
        0 => new Color(0.55f, 0.45f, 0.30f),
        1 => new Color(0.70f, 0.45f, 0.30f),
        2 => new Color(0.65f, 0.62f, 0.58f),
        3 => new Color(0.55f, 0.60f, 0.65f),
        _ => new Color(0.75f, 0.80f, 0.90f),
    };

    /// Crude but real shape/size/color-tint variety per structure category — a stand-in for the
    /// proper per-track generator families (site-placement/growth-network/zone-coverage,
    /// appearance-generation-spec §4.3-4.5) this spec's Open Items flags as the largest net-new
    /// engineering item, not yet attempted here.
    private static (float w, float h, Color tint) StructureVisual(string name) => name switch
    {
        "Workshop" or "Cache"              => (0.35f, 0.30f, new Color(0.90f, 0.70f, 0.30f)),
        "Market"                           => (0.40f, 0.25f, new Color(0.90f, 0.80f, 0.40f)),
        "Granary"                          => (0.30f, 0.45f, new Color(0.75f, 0.65f, 0.35f)),
        "Archive"                          => (0.30f, 0.35f, new Color(0.40f, 0.60f, 0.90f)),
        "Forum"                            => (0.45f, 0.20f, new Color(0.50f, 0.70f, 0.90f)),
        "Shrine"                           => (0.25f, 0.50f, new Color(0.85f, 0.85f, 0.95f)),
        "State Temple"                     => (0.40f, 0.60f, new Color(0.90f, 0.90f, 1.00f)),
        "Garrison" or "Territorial Marker" => (0.30f, 0.40f, new Color(0.80f, 0.30f, 0.25f)),
        "Fortification"                    => (0.45f, 0.35f, new Color(0.60f, 0.25f, 0.20f)),
        "Government Hall"                  => (0.50f, 0.55f, new Color(0.70f, 0.35f, 0.30f)),
        "Den"                              => (0.25f, 0.20f, new Color(0.50f, 0.40f, 0.30f)),
        _                                  => (0.30f, 0.30f, Color.gray),
    };
    private readonly List<int> _cleanup = new List<int>();

    // ── Growth-network markers (appearance-generation-spec §4.3/§4.4) ─────────────────────────
    // Distributed/Collective Commerce Engine civs (§4.3: "growth-algorithm network... tissue-
    // differentiation driven" / "growth-algorithm with caste-specialization") and Living Reef
    // (§4.4: pure colonial growth, no building layer at all) share this branching-module family
    // instead of discrete cube buildings — the same "linked modules read as a graph, not a loose
    // cluster" concept MorphologyGenerator.BuildColonial already established at organism scale,
    // reused here at civilization scale (spheres + connective cylinder stalks).
    private readonly Dictionary<string, GameObject> _growthNodes = new Dictionary<string, GameObject>();
    private readonly List<string> _growthCleanup = new List<string>();

    private void SyncGrowthNetwork(Era3Manager mgr)
    {
        var live = new HashSet<string>();
        foreach (var civ in mgr.AllCivsView)
        {
            if (civ.HasCollapsed) continue;
            bool isNetworkTrack = civ.Path == Era3Path.LivingReef
                || (civ.Path == Era3Path.CommerceEngine
                    && (civ.Architecture == CognitiveArchitecture.Distributed || civ.Architecture == CognitiveArchitecture.Collective));
            if (!isNetworkTrack) continue;

            Era3Manager.Settlement capital = null;
            foreach (var s in mgr.Settlements)
                if (s.OwnerCivId == civ.CommunityId && (capital == null || s.Population > capital.Population))
                    capital = s;
            if (capital == null) continue;

            // Node count: Commerce Engine Distributed/Collective scales with BuiltStructures (real
            // tissue/caste differentiation, §4.3). Living Reef has no BuiltStructures at all (§4.4),
            // so it scales off accumulated Economic investment + Economy.Stock[Industry] instead
            // (era3-systems-implementation-spec §6: Stockpile retired) — the closest already-tracked
            // "how much has this reef grown" signal available to it.
            int nodeCount = civ.Path == Era3Path.LivingReef
                ? Mathf.Clamp(2 + Mathf.FloorToInt(civ.InvestEconomic * 10f + (civ.Economy?.Stock[CivilizationEconomy.Industry] ?? 0f) * 2f), 2, 16)
                : Mathf.Clamp(2 + civ.BuiltStructures.Count, 2, 18);

            // Deterministic per-(civ,nodeCount) seed — the branching pattern stays stable frame to
            // frame and grows incrementally as nodeCount rises, rather than reshuffling wholesale.
            var rng = new System.Random(civ.CommunityId * 7919 + nodeCount * 13);
            Vector3 dir = LocalDir(capital);
            float baseR = _planetRadius + ShellOffset + WaterClearance(capital);
            Vector3 centerLocal = dir * (baseR + TierHeight(capital.Tier) * 0.5f);
            Vector3 tangent = Vector3.Cross(dir, Vector3.up);
            if (tangent.sqrMagnitude < 0.01f) tangent = Vector3.Cross(dir, Vector3.right);
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(dir, tangent);

            float nodeRadius = TierFootprint(capital.Tier) * 0.28f;
            var positions = new List<Vector3> { Vector3.zero };
            var parents = new List<int> { -1 };
            for (int n = 1; n < nodeCount; n++)
            {
                int parent = rng.Next(0, positions.Count);
                float ang = (float)(rng.NextDouble() * Mathf.PI * 2f);
                float dist = nodeRadius * (2.2f + (float)rng.NextDouble() * 1.2f);
                Vector3 offset = (tangent * Mathf.Cos(ang) + bitangent * Mathf.Sin(ang)) * dist;
                positions.Add(positions[parent] + offset);
                parents.Add(parent);
            }

            int techTier = civ.Path == Era3Path.CommerceEngine ? Era3TechTree.GetTechTier(civ) : 0;
            Color baseTint = civ.Path == Era3Path.LivingReef
                ? new Color(0.35f, 0.55f, 0.40f) // reef/mat green — distinct from any built-structure palette
                : TechTierTint(techTier);

            for (int n = 0; n < positions.Count; n++)
            {
                string key = $"{civ.CommunityId}:node:{n}";
                live.Add(key);
                if (!_growthNodes.TryGetValue(key, out var go) || go == null)
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    go.name = $"GrowthNode_{key}";
                    Destroy(go.GetComponent<Collider>());
                    go.GetComponent<Renderer>().sharedMaterial = _markerMat;
                    go.transform.SetParent(_planet, worldPositionStays: true);
                    _growthNodes[key] = go;
                }
                // Collective's caste specialization reads as size variance between nodes (a queen/
                // core chamber larger than worker pods); Distributed/Living-Reef nodes stay closer
                // to uniform (tissue differentiation, not discrete caste roles).
                float sizeVar = civ.Path == Era3Path.CommerceEngine && civ.Architecture == CognitiveArchitecture.Collective
                    ? (n == 0 ? 1.6f : 0.8f + (float)rng.NextDouble() * 0.5f)
                    : 1f;
                go.transform.localPosition = centerLocal + positions[n] + dir * (nodeRadius * sizeVar * 0.5f);
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
                go.transform.localScale = Vector3.one * (nodeRadius * 2f * sizeVar);

                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", Color.Lerp(CivColor(civ.CommunityId), baseTint, 0.55f));
                go.GetComponent<Renderer>().SetPropertyBlock(mpb);

                if (parents[n] >= 0)
                {
                    string stalkKey = $"{civ.CommunityId}:stalk:{n}";
                    live.Add(stalkKey);
                    if (!_growthNodes.TryGetValue(stalkKey, out var stalk) || stalk == null)
                    {
                        stalk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        stalk.name = $"GrowthStalk_{stalkKey}";
                        Destroy(stalk.GetComponent<Collider>());
                        stalk.GetComponent<Renderer>().sharedMaterial = _markerMat;
                        stalk.transform.SetParent(_planet, worldPositionStays: true);
                        _growthNodes[stalkKey] = stalk;
                    }
                    Vector3 a = positions[parents[n]], b = positions[n];
                    Vector3 mid = (a + b) * 0.5f;
                    float len = Mathf.Max(Vector3.Distance(a, b), 0.001f);
                    Vector3 along = (b - a).sqrMagnitude > 0.0001f ? (b - a).normalized : tangent;
                    stalk.transform.localPosition = centerLocal + mid + dir * (nodeRadius * 0.3f);
                    stalk.transform.localRotation = Quaternion.FromToRotation(Vector3.up, along);
                    stalk.transform.localScale = new Vector3(nodeRadius * 0.35f, len * 0.5f, nodeRadius * 0.35f);

                    var smpb = new MaterialPropertyBlock();
                    smpb.SetColor("_BaseColor", Color.Lerp(CivColor(civ.CommunityId), baseTint, 0.75f));
                    stalk.GetComponent<Renderer>().SetPropertyBlock(smpb);
                }
            }
        }

        _growthCleanup.Clear();
        foreach (var kv in _growthNodes) if (!live.Contains(kv.Key)) _growthCleanup.Add(kv.Key);
        foreach (var key in _growthCleanup) { if (_growthNodes[key] != null) Destroy(_growthNodes[key]); _growthNodes.Remove(key); }
    }

    // ── Guest civilization nesting (appearance-generation-spec §4.10 / host-guest-relation-spec) ──
    // A guest's presence within a host's territory renders AT the host's capital, sized by
    // SubstrateFootprint (the guest's slot_capacity for that territory) rather than at the guest's
    // own — possibly distant — settlements. Uses the guest's OWN Track/Architecture to pick discrete-
    // cube vs. growth-network-sphere styling (the same two families SyncStructures/SyncGrowthNetwork
    // already build), just nested in a tighter inner ring and visually marked as guest-owned (a thin
    // outline tint) so it reads as distinct from the host's own structures.
    private readonly Dictionary<string, GameObject> _guestMarkers = new Dictionary<string, GameObject>();
    private readonly List<string> _guestCleanup = new List<string>();
    private const int MaxVisibleGuestSlots = 10;

    private void SyncGuestNesting(Era3Manager mgr)
    {
        var live = new HashSet<string>();
        foreach (var rel in mgr.HostGuestRelations)
        {
            if (rel.State == Era3Manager.HostGuestState.Terminated || rel.SubstrateFootprint <= 0) continue;
            var host = mgr.GetCiv(rel.HostCivId);
            var guest = mgr.GetCiv(rel.GuestCivId);
            if (host == null || guest == null || host.HasCollapsed || guest.HasCollapsed) continue;

            Era3Manager.Settlement capital = null;
            foreach (var s in mgr.Settlements)
                if (s.OwnerCivId == host.CommunityId && (capital == null || s.Population > capital.Population))
                    capital = s;
            if (capital == null) continue;

            Vector3 dir = LocalDir(capital);
            float baseR = _planetRadius + ShellOffset + WaterClearance(capital);
            // Inner ring, closer to the capital than the host's own structure ring — reads as
            // "nested within," not competing for the same visual footprint.
            Vector3 centerLocal = dir * (baseR + TierHeight(capital.Tier) * 0.7f);
            Vector3 tangent = Vector3.Cross(dir, Vector3.up);
            if (tangent.sqrMagnitude < 0.01f) tangent = Vector3.Cross(dir, Vector3.right);
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(dir, tangent);

            bool guestIsNetworkTrack = guest.Path == Era3Path.LivingReef
                || (guest.Path == Era3Path.CommerceEngine && (guest.Architecture == CognitiveArchitecture.Distributed || guest.Architecture == CognitiveArchitecture.Collective));

            int count = Mathf.Min(rel.SubstrateFootprint, MaxVisibleGuestSlots);
            Color guestTint = Color.Lerp(CivColor(guest.CommunityId), Color.white, 0.15f); // faint "guest" cue
            for (int i = 0; i < count; i++)
            {
                string key = $"{rel.HostCivId}:{rel.GuestCivId}:{i}";
                live.Add(key);
                if (!_guestMarkers.TryGetValue(key, out var go) || go == null)
                {
                    go = GameObject.CreatePrimitive(guestIsNetworkTrack ? PrimitiveType.Sphere : PrimitiveType.Cube);
                    go.name = $"Guest_{key}";
                    Destroy(go.GetComponent<Collider>());
                    go.GetComponent<Renderer>().sharedMaterial = _markerMat;
                    go.transform.SetParent(_planet, worldPositionStays: true);
                    _guestMarkers[key] = go;
                }

                float ringR = TierFootprint(capital.Tier) * 0.45f;
                float angle = (i / (float)count) * Mathf.PI * 2f;
                Vector3 offset = (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle)) * ringR;
                float size = TierFootprint(capital.Tier) * 0.22f;
                go.transform.localPosition = centerLocal + offset + dir * (size * 0.5f);
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
                go.transform.localScale = Vector3.one * size;

                var mpb = new MaterialPropertyBlock();
                mpb.SetColor("_BaseColor", guestTint);
                go.GetComponent<Renderer>().SetPropertyBlock(mpb);
            }
        }

        _guestCleanup.Clear();
        foreach (var kv in _guestMarkers) if (!live.Contains(kv.Key)) _guestCleanup.Add(kv.Key);
        foreach (var key in _guestCleanup) { if (_guestMarkers[key] != null) Destroy(_guestMarkers[key]); _guestMarkers.Remove(key); }
    }

    private static float TierHeight(Era3Manager.SettlementTier t) =>
        t == Era3Manager.SettlementTier.City ? 1.4f : t == Era3Manager.SettlementTier.Town ? 0.9f : 0.5f;
    private static float TierFootprint(Era3Manager.SettlementTier t) =>
        t == Era3Manager.SettlementTier.City ? 0.9f : t == Era3Manager.SettlementTier.Town ? 0.6f : 0.4f;
    // Angular claim radius (radians) — a City projects influence far wider than a Village.
    private static float TierClaimRadius(Era3Manager.SettlementTier t) =>
        t == Era3Manager.SettlementTier.City ? 0.28f : t == Era3Manager.SettlementTier.Town ? 0.18f : 0.11f;

    // ── Zone-coverage generator (appearance-generation-spec §4.5/§4.9) ────────────────────────
    private readonly Dictionary<int, float> _coverageIntensity = new Dictionary<int, float>();
    private const float CoverageLagRate = 0.15f; // §4.9 smoothing — fraction of the gap closed per RebuildTerritory call (~1s cadence)

    /// Reads the civ's REAL Order Doctrine (Policy Catalog, CoerciveDomestic slot) and tech tier to
    /// produce a target coverage intensity + falloff sharpness. Wide Scatter/Local Optimization ->
    /// low intensity, broad (low sharpness); Concentrated Fronts/Planetary Engineering -> high
    /// intensity, tight (high sharpness) — "sharper zone boundaries," per §4.5's own wording.
    private static float ZoneCoverageTarget(CivilizationState civ, out float sharpness)
    {
        string activeId = civ.PolicySlots.TryGetValue(Era3PolicyCatalog.PolicySlot.CoerciveDomestic, out var state) ? state.ActiveId : null;
        bool concentrated = activeId == "ter_order_planetary" || activeId == "bf_order_concentrated";
        sharpness = concentrated ? 2.2f : 0.6f;
        // §4.8's zone-coverage-tracks column: reach grows with tech tier ("Minimal coverage reach"
        // at Pre-agrarian up to "Maximal reach" at Post-industrial) even though material palette
        // itself doesn't apply the same way to these tracks.
        float techReach = 0.05f * Era3TechTree.GetTechTier(civ);
        float baseIntensity = concentrated ? 0.7f : 0.35f;
        return Mathf.Clamp01(baseIntensity + techReach);
    }

    // ── Territory + borders overlay ─────────────────────────────────────────────────────────
    private void RebuildTerritory(Era3Manager mgr)
    {
        var unitVerts = _tectonics.UnitVerts;
        var triangles = _tectonics.Triangles;
        int vCount = unitVerts.Count;

        // Per-vertex owning civ (-1 = unclaimed) AND owning settlement index, from the nearest
        // settlement whose angular claim covers it. The settlement index (not just the civ id) is
        // needed so occupied-but-unrecognized territory (a conquest not yet ratified by treaty) can
        // be told apart from an ordinary, settled claim by the same civ.
        var owner = new int[vCount];
        var ownerSettlement = new int[vCount];
        // appearance-generation-spec §4.5: how deep inside its claim each vertex sits (0 at the
        // claim edge, 1 dead-center) — the raw input the zone-coverage intensity field below shapes
        // into a broad-vs-sharp falloff per Terraformer/Bloom Front's Order Doctrine.
        var claimDepth = new float[vCount];
        int m = mgr.Settlements.Count;
        var sDir = new Vector3[m];
        var sCos = new float[m];
        var sCiv = new int[m];
        for (int j = 0; j < m; j++)
        {
            var s = mgr.Settlements[j];
            sDir[j] = LocalDir(s);
            sCos[j] = Mathf.Cos(TierClaimRadius(s.Tier));
            sCiv[j] = s.OwnerCivId >= 0 ? s.OwnerCivId : s.FounderCivId;
        }
        // era3-sovereignty-interaction-gaps-spec.md §3: claim_strength (the tiebreak the spec asks
        // for) and contest detection ride this exact same loop — bestDot IS the natural numeric
        // strength already driving ownership, and counting how many DIFFERENT civs' radii also reach
        // a vertex (not just the winner) is one extra cheap comparison, no second pass needed.
        var claimStrength = new float[vCount];
        var contested = new bool[vCount];
        for (int i = 0; i < vCount; i++)
        {
            Vector3 v = unitVerts[i];
            int best = -1; int bestJ = -1; float bestDot = -2f;
            for (int j = 0; j < m; j++)
            {
                float d = Vector3.Dot(v, sDir[j]);
                if (d >= sCos[j] && d > bestDot) { bestDot = d; best = sCiv[j]; bestJ = j; }
            }
            owner[i] = best;
            ownerSettlement[i] = bestJ;
            claimDepth[i] = bestJ >= 0 ? Mathf.InverseLerp(sCos[bestJ], 1f, bestDot) : 0f;
            claimStrength[i] = bestDot;

            // Contested: at least one OTHER civ's radius also reaches this vertex, not just the
            // winner — a cheap second O(m) pass (same complexity class as the loop above, not a new
            // order of growth) rather than a per-vertex allocation to track a full distinct-civ set.
            if (best >= 0)
                for (int j = 0; j < m; j++)
                {
                    if (sCiv[j] == best) continue;
                    if (Vector3.Dot(v, sDir[j]) >= sCos[j]) { contested[i] = true; break; }
                }
        }

        // population-energy-aggregation-spec.md §3.1: additive TerritoryCell lifecycle for the
        // zone-based tracks, riding this same per-vertex ownership recompute (same ~1s cadence, no
        // extra O(vCount) pass). The world-position lookup lets Era3Manager's cohort tick resolve a
        // cell id back to a real position for its climate/solar/nutrient formulas without needing its
        // own TectonicResult reference.
        mgr.SetCellWorldPositionLookup(cellId => _planet.TransformPoint(unitVerts[cellId] * ShellR(cellId)));
        mgr.SyncTerritoryCells(owner, claimStrength, contested);

        // §4.9: smoothed per-civ intensity — lags toward its Order-Doctrine target instead of
        // jumping, same "gradual, not a snap" principle as the discrete-structure hazard-rebuild.
        foreach (var civ in mgr.AllCivsView)
        {
            if (civ.Path != Era3Path.Terraformer && civ.Path != Era3Path.BloomFront) continue;
            float target = ZoneCoverageTarget(civ, out _);
            float current = _coverageIntensity.TryGetValue(civ.CommunityId, out var c) ? c : target;
            _coverageIntensity[civ.CommunityId] = Mathf.Lerp(current, target, CoverageLagRate);
        }

        var verts = new List<Vector3>();
        var colors = new List<Color>();
        var tris = new List<int>();

        Color borderColor = new Color(0.05f, 0.03f, 0.0f, 0.85f); // dark seam between polities / at edges
        // Occupied (militarily conquered, not yet diplomatically recognized) territory is rendered as
        // a hatched blend between the conqueror's color and the last recognized owner's, evaluated
        // PER VERTEX (not per face, unlike ordinary solid claims) so the GPU interpolates a banded
        // look across each triangle — this is the "diagonal stripes" representation. A fixed world
        // axis (not derived per-triangle) keeps the bands reading as consistently diagonal wherever
        // they appear on the sphere. TUNABLE: StripeAxis / StripeFrequency for band size/orientation.
        Vector3 stripeAxis = new Vector3(0.6f, 0.3f, 0.75f).normalized;
        const float StripeFrequency = 40f;

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];
            int oa = owner[ia], ob = owner[ib], oc = owner[ic];
            if (oa < 0 && ob < 0 && oc < 0) continue; // fully unclaimed — draw nothing

            int b = verts.Count;
            verts.Add(unitVerts[ia] * ShellR(ia));
            verts.Add(unitVerts[ib] * ShellR(ib));
            verts.Add(unitVerts[ic] * ShellR(ic));
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);

            if (oa == ob && ob == oc)
            {
                // Interior of one polity. Check whether the owning settlement is currently occupied —
                // if so, hatch it per-vertex instead of a flat fill.
                var s = ownerSettlement[ia] >= 0 ? mgr.Settlements[ownerSettlement[ia]] : null;
                if (s != null && s.IsOccupied)
                {
                    Color conquerorColor = CivColor(oa); conquerorColor.a = 0.36f;
                    Color priorColor = CivColor(s.RecognizedOwnerCivId >= 0 ? s.RecognizedOwnerCivId : oa); priorColor.a = 0.36f;
                    int[] idx = { ia, ib, ic };
                    foreach (int vi in idx)
                    {
                        float stripe = Mathf.Repeat(Vector3.Dot(unitVerts[vi], stripeAxis) * StripeFrequency, 1f);
                        colors.Add(stripe < 0.5f ? conquerorColor : priorColor);
                    }
                }
                else
                {
                    var occupyingCiv = mgr.GetCiv(oa);
                    // appearance-generation-spec §4.5: Terraformer/Bloom Front render as a coverage-
                    // intensity FIELD (density/reach gradient) instead of a flat civ-color fill — the
                    // spec's own explicit "should NOT use the site-suitability/settlement-placement
                    // engine" distinction from every other track.
                    if (occupyingCiv != null && (occupyingCiv.Path == Era3Path.Terraformer || occupyingCiv.Path == Era3Path.BloomFront))
                    {
                        float smoothed = _coverageIntensity.TryGetValue(oa, out var v) ? v : 0.3f;
                        ZoneCoverageTarget(occupyingCiv, out float sharpness);
                        int[] idxs = { ia, ib, ic };
                        foreach (int vi in idxs)
                        {
                            // Sharpness shapes the falloff from claim edge to center: Concentrated
                            // Fronts/Planetary Engineering (high sharpness) hold near-target intensity
                            // only close to the core and drop fast toward the edge ("sharper zone
                            // boundaries"); Wide Scatter/Local Optimization (low sharpness) stays
                            // closer to uniform across the whole claim ("broad coverage").
                            float depthShaped = Mathf.Pow(claimDepth[vi], sharpness);
                            float vertexIntensity = Mathf.Lerp(smoothed * 0.25f, smoothed, depthShaped);
                            // appearance-generation-spec.md §4.5: cell_intensity = f(doctrine_weight,
                            // tech_tier_reach, local_cohort_health) — doctrine_weight/tech_tier_reach
                            // are ZoneCoverageTarget's existing inputs (feeding `smoothed` above);
                            // local_cohort_health is the missing third term, now supplied by the real
                            // TerritoryCell cohort state a struggling/thriving cell actually has.
                            vertexIntensity *= mgr.GetTerritoryCellHealth(vi);
                            Color c = CivColor(oa);
                            c.a = Mathf.Clamp(vertexIntensity, 0.08f, 0.85f);
                            colors.Add(c);
                        }
                    }
                    else
                    {
                        Color faceColor = CivColor(oa); faceColor.a = 0.30f;
                        colors.Add(faceColor); colors.Add(faceColor); colors.Add(faceColor);
                    }
                }
            }
            else
            {
                // Vertices disagree (two civs, or a civ vs unclaimed) — this face straddles a border.
                colors.Add(borderColor); colors.Add(borderColor); colors.Add(borderColor);
            }
        }

        var mesh = _territoryGo.GetComponent<MeshFilter>().mesh;
        mesh.Clear();
        if (verts.Count > 0)
        {
            mesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }

    private float ShellR(int vertexIndex) =>
        _planetRadius + _tectonics.Elevation[vertexIndex] * _elevationWorldScale + ShellOffset;

    private void ClearMarkers()
    {
        foreach (var kv in _markers) if (kv.Value != null) Destroy(kv.Value);
        _markers.Clear();
        _localDir.Clear();
        _lastSignature = -1;

        foreach (var kv in _structureMarkers) if (kv.Value != null) Destroy(kv.Value);
        _structureMarkers.Clear();

        foreach (var kv in _growthNodes) if (kv.Value != null) Destroy(kv.Value);
        _growthNodes.Clear();

        foreach (var kv in _guestMarkers) if (kv.Value != null) Destroy(kv.Value);
        _guestMarkers.Clear();
    }

    /// Finds the settlement whose MARKER is within maxDist of a world point (e.g. an InspectPopup
    /// raycast hit) — mirrors InspectPopup's own FindNearestAgentAt proximity pattern, so settlement
    /// markers don't need a Collider (avoiding the same physics-raycast interference agents were
    /// stripped of colliders to prevent). Returns null if nothing is close enough.
    public Era3Manager.Settlement FindNearestSettlementAt(Vector3 worldPoint, float maxDist = 2.5f)
    {
        var mgr = Era3Manager.Instance;
        if (mgr == null) return null;
        Era3Manager.Settlement nearest = null;
        float nearestDist = maxDist;
        foreach (var s in mgr.Settlements)
        {
            if (!_markers.TryGetValue(s.Id, out var go) || go == null) continue;
            float d = Vector3.Distance(worldPoint, go.transform.position);
            if (d < nearestDist) { nearestDist = d; nearest = s; }
        }
        return nearest;
    }

    // ── Settlement labels ───────────────────────────────────────────────────────────────────
    private GUIStyle _labelStyle;
    void OnGUI()
    {
        var mgr = Era3Manager.Instance;
        if (mgr == null || !mgr.IsActive || _markers.Count == 0) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        if (_labelStyle == null)
            _labelStyle = new GUIStyle(GUI.skin.label)
            { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
              wordWrap = true, // settlement names ("Venek Collective Village") can be wider than a
              // one-line box at this font size — without wrapping the text simply overflows; WITH
              // wrapping but no matching box-height increase, it wraps to 2 lines inside a box only
              // tall enough for 1, clipping the top/bottom of both — see the taller rect below, which
              // is the actual fix (this just makes text overflow readable instead of overrun).
              normal = { textColor = Color.white } };

        foreach (var s in mgr.Settlements)
        {
            if (!_markers.TryGetValue(s.Id, out var go) || go == null) continue;
            Vector3 labelPos = go.transform.position + go.transform.up * TierHeight(s.Tier) * 0.6f;
            Vector3 sp = cam.WorldToScreenPoint(labelPos);
            if (sp.z <= 0f) continue;
            // Screen-space GUI.Label ignores 3D depth/occlusion — without this check, a settlement on
            // the far side of the planet still projects to a valid screen point and its label appears
            // to render "through" the opaque terrain. _planet.position is the sphere's world center
            // (markers are children placed at local direction * radius around that same pivot).
            if (!SphereSurface.IsFacingCamera(labelPos, _planet.position, cam.transform.position)) continue;
            float y = Screen.height - sp.y;
            // s.Name is the settlement's own unique name (already includes its path-appropriate tier
            // word, e.g. "Wyrond Network Node" — see SpawnSettlement) — show that, not just the tier.
            string label = $"{s.Name}  ({s.Population:F0})";
            // Wide + tall enough for a two-line wrap of a long settlement name at this font size —
            // the box was previously 140x16, which fit neither one long line nor two wrapped ones.
            var rect = new Rect(sp.x - 100f, y - 16f, 200f, 32f);
            var prev = _labelStyle.normal.textColor;
            _labelStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f);
            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), label, _labelStyle);
            _labelStyle.normal.textColor = prev;
            GUI.Label(rect, label, _labelStyle);
        }
    }
}
