using System.Collections.Generic;
using UnityEngine;

/// How tightly a community's home range is anchored to one place. Orthogonal to social structure —
/// any grouping type (troop, pair, fission-fusion, colony) can independently be nomadic or settled.
public enum TerritorialityStrictness
{
    Nomadic,    // no home site; roams freely (unchanged original behavior)
    LooseRange, // a soft "ecosystem range" — wanders within a broad favorable region, not pinned to a point
    StrictSite, // anchored to one fixed point (a nest/hive/anthill) — real colonies work this way
}

/// One community's current territoriality state. Re-evaluated periodically, not fixed at creation —
/// a lineage can settle or abandon a site as conditions or social structure change.
public class TerritorialityRecord
{
    public int CommunityId;
    public TerritorialityStrictness Strictness = TerritorialityStrictness.Nomadic;
    public Vector3 HomeSite;     // meaningful for LooseRange (range center) and StrictSite (fixed point)
    public float HomeRadius;     // effective range: small for StrictSite, larger for LooseRange
}

/// Periodically evaluates each community's territoriality: how favorable its current location is
/// against its own tolerance profile (reusing the existing StressLevel adversity signal — no new
/// environmental scoring invented), and whether its social structure forces a particular outcome
/// (Eusocial/Colonial lineages are always StrictSite — a real ant/bee colony IS a fixed site, not a
/// preference). High favorability trends a community toward settling; low favorability keeps it
/// nomadic. This does not change individual simulation — it only sets a soft "home" bias that
/// AgentController's movement code can optionally pull toward (see AgentController.ApplyTerritorialBias).
public class TerritorialityManager : MonoBehaviour
{
    public static TerritorialityManager Instance { get; private set; }

    private AgentSpawner _spawner;
    private readonly Dictionary<int, TerritorialityRecord> _records = new Dictionary<int, TerritorialityRecord>();
    // A visible marker (nest/hive/anthill) exists ONLY for StrictSite communities — this is the
    // player-visible cue that a lineage has become a fixed colony, distinct from roaming individuals.
    // Destroyed if the community ever reverts to LooseRange/Nomadic (e.g. conditions worsen).
    private readonly Dictionary<int, GameObject> _colonyMarkers = new Dictionary<int, GameObject>();
    // Living-individual count for each active colony marker, drawn as a number on the sphere (OnGUI).
    // Kept in lockstep with _colonyMarkers: an entry exists iff that community currently has a marker.
    private readonly Dictionary<int, int> _colonyCounts = new Dictionary<int, int>();
    private GUIStyle _countStyle;

    private const float TickInterval = 8f;
    private float _tickTimer;

    // Favorability thresholds — TUNABLE. Below LooseRangeThreshold stays Nomadic; between the two
    // thresholds settles into a LooseRange; above StrictSiteThreshold can tighten to StrictSite.
    private const float LooseRangeThreshold = 0.45f;
    private const float StrictSiteThreshold = 0.75f;
    private const float LooseRangeRadius = 15f;
    private const float StrictSiteRadius = 3f;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(this); return; }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    public void Init(AgentSpawner spawner) => _spawner = spawner;

    public TerritorialityRecord GetRecord(int communityId)
    {
        _records.TryGetValue(communityId, out var rec);
        return rec;
    }

    void Update()
    {
        if (_spawner == null) return;

        CoRotateMarkers();

        _tickTimer += Time.deltaTime;
        if (_tickTimer < TickInterval) return;
        _tickTimer = 0f;
        Evaluate();
    }

    /// Colony markers are plain GameObjects, not AgentControllers, so they were never included in
    /// AgentSpawner's per-frame co-rotation of ActiveAgents with the planet's visual spin. Without
    /// this they stay fixed in world space while the terrain rotates under them — visibly drifting
    /// in a circle around the planet instead of staying anchored to their nest site. Same math as
    /// AgentSpawner.Update's co-rotation, kept in sync with it deliberately (not shared code) since
    /// each just needs the current frame's incremental rotation, not a persistent transform link.
    private void CoRotateMarkers()
    {
        SolarSystemRuntime sr = SolarSystemRuntime.Instance;
        if (sr == null || sr.planetRotationPeriodSeconds <= 0f) return;

        float degPerSec = 360f / sr.planetRotationPeriodSeconds;
        float deltaAngle = degPerSec * Time.deltaTime;
        if (Mathf.Approximately(deltaAngle, 0f)) return;

        Vector3 axis = WindManager.RotationAxis;
        Quaternion rot = Quaternion.AngleAxis(deltaAngle, axis);
        Vector3 planetCenter = _spawner.planetCenter;

        foreach (var kv in _colonyMarkers)
        {
            GameObject marker = kv.Value;
            if (marker == null) continue;
            marker.transform.position = planetCenter + rot * (marker.transform.position - planetCenter);
        }

        // Keep every ANCHORED record's HomeSite in lockstep with the rotating terrain — this applies
        // to LooseRange too (no visible marker, but ApplyTerritorialBias still pulls agents toward
        // it), not just StrictSite. Nomadic records don't need this: their HomeSite is recomputed
        // from the live population centroid every Evaluate() anyway, never held fixed between ticks.
        foreach (var rec in _records.Values)
            if (rec.Strictness != TerritorialityStrictness.Nomadic)
                rec.HomeSite = planetCenter + rot * (rec.HomeSite - planetCenter);
    }

    private void Evaluate()
    {
        var byCommunity = new Dictionary<int, List<AgentController>>();
        foreach (var a in _spawner.ActiveAgents)
        {
            if (a == null) continue;
            if (!byCommunity.TryGetValue(a.communityId, out var list)) { list = new List<AgentController>(); byCommunity[a.communityId] = list; }
            list.Add(a);
        }

        foreach (var kv in byCommunity)
        {
            int cid = kv.Key;
            List<AgentController> members = kv.Value;
            if (members.Count == 0) continue;

            float favSum = 0f;
            Vector3 posSum = Vector3.zero;
            foreach (var m in members) { favSum += m.LocalFavorability; posSum += m.transform.position; }
            float avgFavorability = favSum / members.Count;
            Vector3 centroid = posSum / members.Count;

            if (!_records.TryGetValue(cid, out var rec)) { rec = new TerritorialityRecord { CommunityId = cid }; _records[cid] = rec; }

            // Eusocial/Colonial social structure forces StrictSite regardless of favorability — a
            // colony is structurally a fixed site, not an environmental preference. Everything else
            // derives purely from favorability.
            bool forcedColonial = Era2Manager.Instance != null
                && Era2Manager.Instance.GetRecord(cid)?.SocialStructure == SocialStructureType.EusocialColonial;

            TerritorialityStrictness target;
            if (forcedColonial) target = TerritorialityStrictness.StrictSite;
            else if (avgFavorability >= StrictSiteThreshold) target = TerritorialityStrictness.StrictSite;
            else if (avgFavorability >= LooseRangeThreshold) target = TerritorialityStrictness.LooseRange;
            else target = TerritorialityStrictness.Nomadic;

            // Only move the home site when first settling or when still nomadic (tracking the group);
            // once anchored (LooseRange/StrictSite), the site stays put rather than drifting with the
            // population's current centroid every tick — that's what "anchored" means.
            if (rec.Strictness == TerritorialityStrictness.Nomadic || target == TerritorialityStrictness.Nomadic)
                rec.HomeSite = centroid;

            rec.Strictness = target;
            rec.HomeRadius = target == TerritorialityStrictness.StrictSite ? StrictSiteRadius : LooseRangeRadius;

            UpdateColonyMarker(cid, rec, members[0].lineageColor, members.Count, forcedColonial);
        }

        // Extinction cleanup: any community that still owns a marker but no longer appears in
        // byCommunity has died out (0 living members) — Evaluate's main loop can't reach it because
        // it only iterates communities that HAVE living agents, so its nest marker would otherwise
        // orphan on the map forever. Tear those down here so a colony vanishes when its last member
        // dies. Snapshot the keys first since we mutate the dictionary inside the loop.
        _cleanupScratch.Clear();
        foreach (int cid in _colonyMarkers.Keys)
            if (!byCommunity.TryGetValue(cid, out var living) || living.Count == 0)
                _cleanupScratch.Add(cid);
        foreach (int cid in _cleanupScratch)
            DestroyColonyMarker(cid);
    }

    private readonly List<int> _cleanupScratch = new List<int>();

    private void DestroyColonyMarker(int communityId)
    {
        if (_colonyMarkers.TryGetValue(communityId, out var marker))
        {
            if (marker != null) Destroy(marker);
            _colonyMarkers.Remove(communityId);
        }
        _colonyCounts.Remove(communityId);
    }

    /// Keeps a small visible marker (the nest/hive/anthill itself) in sync with StrictSite state —
    /// created when a community first settles, repositioned if the home site is still tracking
    /// (shouldn't happen once truly anchored, but stays correct if it does), destroyed if the
    /// community ever reverts to LooseRange/Nomadic.
    // A visible nest/hive sphere means an actual COLONY — a real, plural, anchored settlement — not
    // merely a lineage sitting in a favorable spot. In early Era 1 everything is single-celled, so
    // favorability-driven StrictSite would otherwise plant a "nest" on every solitary community from
    // t=0 (the bug that put a numbered sphere on every individual). A genuine colony requires: a real
    // group (≥ MinColonySize), and either a eusocial/colonial social structure OR an Era 2 anchored
    // proto-settlement. Loose territorial ranges and lone anchored individuals get no marker.
    private const int MinColonySize = 3;

    private void UpdateColonyMarker(int communityId, TerritorialityRecord rec, Color lineageColor,
        int memberCount, bool forcedColonial)
    {
        bool era2 = Era2Manager.Instance != null && Era2Manager.Instance.IsActive;
        bool isRealColony = rec.Strictness == TerritorialityStrictness.StrictSite
            && memberCount >= MinColonySize
            && (forcedColonial || era2);
        // Once this community has an actual Era 3 settlement, that marker/label takes over as the
        // "civilization here" visual — the older colony sphere + bare population number would
        // otherwise draw its own overlapping text label at nearly the same spot (the garbled
        // double-label bug), and is redundant once a proper settlement exists anyway.
        bool supersededBySettlement = Era3Manager.Instance != null && Era3Manager.Instance.IsActive
            && Era3Manager.Instance.CivHasSettlement(communityId);
        bool shouldExist = isRealColony && !supersededBySettlement;

        if (!shouldExist) { DestroyColonyMarker(communityId); return; }

        _colonyMarkers.TryGetValue(communityId, out GameObject marker);
        _colonyCounts[communityId] = memberCount;

        if (marker == null)
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = $"ColonySite_C{communityId}";
            Destroy(marker.GetComponent<Collider>()); // purely visual, no physics/blocking needed
            Renderer r = marker.GetComponent<Renderer>();
            if (r != null)
            {
                Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = Color.Lerp(lineageColor, new Color(0.35f, 0.25f, 0.15f), 0.5f); // earthy tint, reads as a built structure not an organism
                r.material = mat;
            }
            _colonyMarkers[communityId] = marker;
        }

        float eraScale = EraManager.Instance != null ? EraManager.Instance.AgentTargetScale : 0.1f;
        marker.transform.localScale = Vector3.one * eraScale * 2.5f; // visibly larger than an individual organism, reads as a structure
        marker.transform.position = rec.HomeSite;
    }

    // ── Colony population labels ─────────────────────────────────────────────────────────────
    // Draws the living-individual count as a number floating on each colony sphere, so a marker
    // reads as "a colony of N", not an anonymous blob. Screen-space text via OnGUI (no per-marker
    // TextMesh/font asset to manage, and it always renders on top of the planet). A marker only
    // exists while its community is alive (see Evaluate's extinction cleanup), so any label drawn
    // here is by construction a count >= 1 — there are no "0" labels to suppress.
    void OnGUI()
    {
        if (_colonyMarkers.Count == 0) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        if (_countStyle == null)
            _countStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };

        foreach (var kv in _colonyMarkers)
        {
            GameObject marker = kv.Value;
            if (marker == null) continue;
            if (!_colonyCounts.TryGetValue(kv.Key, out int count)) continue;

            Vector3 sp = cam.WorldToScreenPoint(marker.transform.position);
            if (sp.z <= 0f) continue; // behind the camera
            // Screen-space GUI.Label ignores 3D depth/occlusion — without this check, a colony on the
            // far side of the planet still projects to a valid screen point and its label appears to
            // render "through" the opaque terrain. Hide labels for markers on the planet's far side.
            if (!SphereSurface.IsFacingCamera(marker.transform.position, _spawner.planetCenter, cam.transform.position)) continue;

            float y = Screen.height - sp.y; // GUI y is top-down; screen point is bottom-up
            var rect = new Rect(sp.x - 20f, y - 8f, 40f, 16f);
            // Cheap 1px drop shadow for legibility over both the bright water and dark space.
            var prev = _countStyle.normal.textColor;
            _countStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), count.ToString(), _countStyle);
            _countStyle.normal.textColor = prev;
            GUI.Label(rect, count.ToString(), _countStyle);
        }
    }
}
