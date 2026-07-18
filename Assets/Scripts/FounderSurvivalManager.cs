using System.Collections.Generic;
using UnityEngine;

/// Protects the 8 ORIGINAL founder lineages (communityId 0-7 specifically — NOT their speciated
/// descendants, which remain fully at risk as normal) from being wiped out by a single acute
/// environmental event (a fast climate swing, a gas crisis) before Era 3. This is deliberately NOT
/// blanket immunity or a population floor that silently prevents death — it's a reactive, one-shot
/// emergency event: if a founder is caught at critically low population AND under genuine acute
/// environmental duress (high StressLevel, which already blends climate/atmosphere/UV/pressure/
/// thermal-cycle/starvation discomfort — reused rather than inventing a second signal), a survival
/// choice fires. Every option costs something real (population trimmed further, or growth stalled) —
/// per design direction, survival is "not necessarily at the same strength," just survival. Ordinary
/// attrition (OldAge, slow ecological decline, losing to competition) is NOT protected — this only
/// catches the specific "abrupt swing wipes out a founder" failure mode. A lineage can still
/// genuinely die if it's unlucky again after using this once (cooldown-gated, not permanent immunity).
public class FounderSurvivalManager : MonoBehaviour
{
    public static FounderSurvivalManager Instance { get; private set; }

    private AgentSpawner _spawner;

    // ── Tunables (first pass — calibrate from playtest) ──────────────────────────────────
    private const int FounderCount = 8; // communityId 0-7
    private const int DangerPopulation = 5;      // at or below this, a founder is "in danger"
    private const float CrisisStressThreshold = 55f; // StressLevel (0-100) that counts as acute duress
    private const float CrisisCooldownSeconds = 90f; // per-founder cooldown after a crisis resolves
    private const float PopulationTrimFraction = 0.25f; // Migration/Adaptation cost, TUNABLE
    private const int SurvivorFloor = 2; // guaranteed minimum after ANY option resolves
    private const float DormancyDuration = 45f;
    private const float AutoSelectSeconds = 20f; // matches GeneEvolutionManager's popup pacing philosophy (shorter here — a founder crisis is urgent)

    private readonly float[] _cooldownRemaining = new float[FounderCount];
    private bool[] _crisisActive = new bool[FounderCount];

    // ── Player popup state ────────────────────────────────────────────────────────────────
    private int _pendingCommunityId = -1;
    private float _pendingShowTime = -1f;
    private List<AgentController> _pendingSurvivors;

    public void Init(AgentSpawner spawner) => _spawner = spawner;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(this); return; }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    // ── Unconditional founder survival floor (guarantee to Era 3) ────────────────────────────
    // The stress-triggered crisis popup below only catches ACUTE environmental swings. It does NOT
    // catch a founder dying out to ordinary attrition — a synchronized OldAge die-off, a generational
    // trough, slow starvation — which is how founders were actually going extinct. This hard floor
    // closes that gap: called from AgentController.Die, it converts a would-be fatal death into a
    // "cling on" rejuvenation whenever it would push one of the 8 founding lineages below SurvivorFloor
    // before Era 3. Result: the 8 founders CANNOT go extinct before Era 3, regardless of death cause.
    private readonly float[] _lastRescueLog = new float[FounderCount];
    // Founders keep their survival floor for a grace window AFTER Era 3 begins, not cut off at the
    // exact instant. Otherwise a lineage pinned at the 2-member floor through Era 2 dies the moment
    // Era 3 starts (protection lifts while it's at its weakest) — "survived to Era 3" in name only.
    // The grace lets the new era's dynamics (and the tolerance band) give it a real chance to recover.
    private const float Era3GraceSeconds = 45f;
    private float _era3FirstSeen = -1f;

    public static bool TryRescueFounderFromDeath(AgentController a)
        => Instance != null && a != null && Instance.TryRescueFounderImpl(a);

    private bool TryRescueFounderImpl(AgentController a)
    {
        int cid = a.communityId;
        if (cid < 0 || cid >= FounderCount) return false;                 // only the 8 founders
        if (_spawner == null) return false;

        // Guarantee holds through Era 2 and a grace window into Era 3.
        bool era3 = Era3Manager.Instance != null && Era3Manager.Instance.IsActive;
        if (era3)
        {
            if (_era3FirstSeen < 0f) _era3FirstSeen = Time.time;
            if (Time.time - _era3FirstSeen > Era3GraceSeconds) return false;
        }

        int living = 0;
        foreach (var x in _spawner.ActiveAgents)
            if (x != null && x.communityId == cid) living++;
        if (living > SurvivorFloor) return false; // lineage has a buffer — let this death proceed normally

        // This death would drop the lineage to/below its floor before Era 3 — cling on instead.
        a.RejuvenateForFounderRescue();
        if (Time.time - _lastRescueLog[cid] > 15f)
        {
            _lastRescueLog[cid] = Time.time;
            Debug.Log($"[FounderSurvival] community={cid} at survival floor ({living}) pre-Era 3 — last member(s) " +
                      $"clinging on (rejuvenated, not at full strength) rather than going extinct.");
        }
        return true;
    }

    void Update()
    {
        if (_spawner == null) return;

        for (int i = 0; i < FounderCount; i++)
            if (_cooldownRemaining[i] > 0f) _cooldownRemaining[i] -= Time.deltaTime;

        // Once Era 3 begins, founders have "made it" — the mechanic this spec asks for is explicitly
        // about surviving TO Era 3, not permanent protection beyond it.
        if (Era3Manager.Instance != null && Era3Manager.Instance.IsActive) return;

        // Throttled: a full ActiveAgents scan every frame forever isn't needed for something that
        // only needs a couple seconds of detection latency — same O(n)-every-frame cost this session
        // already fixed elsewhere (AgentSpawner's spatial grid) shouldn't be reintroduced here.
        _checkTimer += Time.deltaTime;
        if (_checkTimer < CheckInterval) return;
        _checkTimer = 0f;

        if (_pendingCommunityId < 0)
            CheckForNewCrisis();
    }

    private float _checkTimer;
    private const float CheckInterval = 2f;

    private void CheckForNewCrisis()
    {
        var byCommunity = new Dictionary<int, List<AgentController>>();
        foreach (var a in _spawner.ActiveAgents)
        {
            if (a == null || a.communityId < 0 || a.communityId >= FounderCount) continue;
            if (!byCommunity.TryGetValue(a.communityId, out var list)) { list = new List<AgentController>(); byCommunity[a.communityId] = list; }
            list.Add(a);
        }

        for (int cid = 0; cid < FounderCount; cid++)
        {
            if (_cooldownRemaining[cid] > 0f) continue;
            if (!byCommunity.TryGetValue(cid, out var members) || members.Count == 0) continue;
            if (members.Count > DangerPopulation) continue;

            float avgStress = 0f;
            foreach (var m in members) avgStress += m.StressLevel;
            avgStress /= members.Count;
            if (avgStress < CrisisStressThreshold) continue; // small but not actually in acute danger — leave it be

            TriggerCrisis(cid, members);
            return; // one crisis resolution at a time keeps this simple and avoids overlapping popups
        }
    }

    private void TriggerCrisis(int communityId, List<AgentController> survivors)
    {
        Debug.Log($"[FounderSurvival] community={communityId} pop={survivors.Count} avgStress dangerously high — " +
                  $"founder crisis triggered (acute environmental event, not ordinary attrition).");

        if (communityId == GeneEvolutionManager.PlayerCommunityId)
        {
            _pendingCommunityId = communityId;
            _pendingSurvivors = survivors;
            _pendingShowTime = Time.unscaledTime;
        }
        else
        {
            // NPC founders auto-resolve to Dormancy — the safest default (no population cost),
            // consistent with how NPC gene choices elsewhere default to the conservative option.
            ApplyDormancy(communityId, survivors);
            _cooldownRemaining[communityId] = CrisisCooldownSeconds;
        }
    }

    // ── Choice effects (each costs something real — see class header) ────────────────────

    private void ApplyDormancy(int communityId, List<AgentController> survivors)
    {
        foreach (var a in survivors)
        {
            if (a == null) continue;
            a.EnterDormancy(DormancyDuration);
        }
        Debug.Log($"[FounderSurvival] community={communityId} chose Dormancy — {survivors.Count} survive, metabolism suppressed, no growth for {DormancyDuration:F0}s.");
    }

    private void ApplyRefugeMigration(int communityId, List<AgentController> survivors)
    {
        int survivorCount = ApplyPopulationCost(survivors);
        foreach (var a in survivors)
        {
            if (a == null) continue;
            // First-pass heuristic: jump a modest distance away from the crisis zone in a random
            // tangent direction, rather than a full favorable-terrain search — TUNABLE/improvable.
            Vector3 normal = (a.transform.position - _spawner.planetCenter).normalized;
            Vector3 randDir = (SphereSurface.RandomPointOnSphere(_spawner.planetCenter, _spawner.planetRadius) - _spawner.planetCenter).normalized;
            Vector3 pos = _spawner.planetCenter + Vector3.Slerp(normal, randDir, 0.15f) * _spawner.planetRadius;
            a.transform.position = SphereSurface.ProjectToSurface(pos, _spawner.planetCenter, _spawner.planetRadius);
        }
        Debug.Log($"[FounderSurvival] community={communityId} chose Refuge Migration — {survivorCount}/{survivors.Count} survive the journey, relocated.");
    }

    private void ApplyEmergencyAdaptation(int communityId, List<AgentController> survivors)
    {
        int survivorCount = ApplyPopulationCost(survivors);
        foreach (var a in survivors)
        {
            if (a == null) continue;
            a.ExpandThermalTolerance();
        }
        Debug.Log($"[FounderSurvival] community={communityId} chose Emergency Adaptation — {survivorCount}/{survivors.Count} survive the selective pressure, tolerance widened.");
    }

    /// Trims the population by PopulationTrimFraction, clamped so at least SurvivorFloor remain —
    /// the mechanic guarantees the founder survives THIS crisis, never that it survives at full cost.
    private int ApplyPopulationCost(List<AgentController> survivors)
    {
        int startCount = survivors.Count;
        int targetCount = Mathf.Max(SurvivorFloor, Mathf.RoundToInt(startCount * (1f - PopulationTrimFraction)));
        int toRemove = startCount - targetCount;
        for (int i = 0; i < toRemove && survivors.Count > SurvivorFloor; i++)
        {
            int idx = Random.Range(0, survivors.Count);
            AgentController a = survivors[idx];
            survivors.RemoveAt(idx);
            if (a != null) a.Die(DeathCause.EnergyDepletion); // the crisis itself claims them, honestly logged
        }
        return survivors.Count;
    }

    private void ResolvePendingCrisis(int choice)
    {
        if (_pendingCommunityId < 0 || _pendingSurvivors == null) return;
        // Re-filter — some may have died in the popup's decision window.
        var alive = new List<AgentController>();
        foreach (var a in _pendingSurvivors) if (a != null) alive.Add(a);
        if (alive.Count == 0) { _pendingCommunityId = -1; _pendingSurvivors = null; return; }

        switch (choice)
        {
            case 0: ApplyDormancy(_pendingCommunityId, alive); break;
            case 1: ApplyRefugeMigration(_pendingCommunityId, alive); break;
            default: ApplyEmergencyAdaptation(_pendingCommunityId, alive); break;
        }

        _cooldownRemaining[_pendingCommunityId] = CrisisCooldownSeconds;
        _pendingCommunityId = -1;
        _pendingSurvivors = null;
    }

    // ── UI ─────────────────────────────────────────────────────────────────────────────────
    private GUIStyle _cardStyle, _titleStyle, _bodyStyle, _btnStyle;

    private void EnsureStyles()
    {
        if (_cardStyle != null) return;
        _cardStyle = new GUIStyle(GUI.skin.box);
        _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _titleStyle.normal.textColor = new Color(1f, 0.35f, 0.3f);
        _bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
        _bodyStyle.normal.textColor = Color.white;
        _btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 12, alignment = TextAnchor.MiddleLeft, wordWrap = true };
    }

    void OnGUI()
    {
        if (_pendingCommunityId < 0) return;
        EnsureStyles();

        float elapsed = Time.unscaledTime - _pendingShowTime;
        if (elapsed >= AutoSelectSeconds) { ResolvePendingCrisis(0); return; } // Dormancy is the safe default

        const float w = 440f, h = 260f;
        Rect card = new Rect((Screen.width - w) / 2f, Screen.height * 0.2f, w, h);
        GUI.Box(card, "", _cardStyle);

        float x = card.x + 14f, y = card.y + 12f;
        GUI.Label(new Rect(x, y, w - 28f, 24f), "FOUNDER LINEAGE IN CRISIS", _titleStyle);
        y += 30f;
        int secsLeft = Mathf.CeilToInt(AutoSelectSeconds - elapsed);
        GUI.Label(new Rect(x, y, w - 28f, 18f), $"Your species is critically low and under acute environmental stress. " +
                  $"Choose how it responds — auto-resolving to Dormancy in {secsLeft}s.", _bodyStyle);
        y += 54f;

        if (GUI.Button(new Rect(x, y, w - 28f, 48f),
            "Emergency Dormancy\nMetabolism shuts down — no deaths from this crisis, but no growth for 45s. Safest option.", _btnStyle))
            ResolvePendingCrisis(0);
        y += 56f;

        if (GUI.Button(new Rect(x, y, w - 28f, 48f),
            "Refuge Migration\nFlee to a new location. Some won't survive the journey, but survivors start fresh elsewhere.", _btnStyle))
            ResolvePendingCrisis(1);
        y += 56f;

        if (GUI.Button(new Rect(x, y, w - 28f, 48f),
            "Emergency Adaptation\nA harsh selective push widens tolerance. Costly now, more resilient after.", _btnStyle))
            ResolvePendingCrisis(2);
    }
}
