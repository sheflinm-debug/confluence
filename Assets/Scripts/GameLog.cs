using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// Per-community + global session log, file-backed to Application.persistentDataPath.
/// Init() opens the file; WriteSummary() flushes and closes it.
/// All methods are null-safe — calling before Init() or after WriteSummary() is a no-op.
public static class GameLog
{
    private static StreamWriter _writer;

    /// The real current top-level stage: "Era3" / "Era2" once those (later, separate) systems
    /// activate, otherwise "Era1:{sub-phase name}" for one of the six sub-phases EraManager cycles
    /// through (Abiogenesis, Minimal-Replicator Seas, Great Gas Event, Compartmentalized Cells,
    /// Multicellularity, Morphological Complexity Threshold). Previously this log printed
    /// "Era0".."Era5" directly from EraManager.CurrentEra's sub-phase index, which reads as six
    /// top-level eras and collides with the actual Era 2/Era 3 identities — and since CurrentEra
    /// freezes at its max index once Era 1 ends, it would have kept printing "Era5" forever even
    /// after the simulation genuinely reached Era 2 or Era 3.
    private static string GetStageLabel()
    {
        if (Era3Manager.Instance != null && Era3Manager.Instance.IsActive) return "Era3";
        if (Era2Manager.Instance != null && Era2Manager.Instance.IsActive) return "Era2";
        return EraManager.Instance != null ? $"Era1:{EraManager.Instance.CurrentEraName}" : "?";
    }

    // Lifetime death totals (for final summary)
    private static readonly Dictionary<DeathCause, int> _deathCounts = new Dictionary<DeathCause, int>();

    // Per-community interval counters — reset each periodic snapshot
    private static readonly Dictionary<int, int> _intervalBirths    = new Dictionary<int, int>();
    private static readonly Dictionary<int, Dictionary<DeathCause, int>> _intervalDeaths
        = new Dictionary<int, Dictionary<DeathCause, int>>();
    private static readonly Dictionary<int, Dictionary<string, int>> _intervalReproFails
        = new Dictionary<int, Dictionary<string, int>>();

    // Periodic snapshot cadence
    private const float SnapshotIntervalSec = 30f;
    private static float _nextSnapshotTime  = SnapshotIntervalSec;

    // ── Priority 6: lifespan-jitter running summary ─────────────────────────────
    // Lowered from 500: a struggling/short-lived world (the exact case this diagnostic exists to
    // catch) may never spawn 500 total agents, so the periodic summary would silently never fire —
    // defeating the point on precisely the runs where it matters most.
    private const int JitterSummaryEvery = 50; // spawns
    private static int   _jSampled;
    private static float _jSum, _jSumSq, _jMin = float.MaxValue, _jMax = float.MinValue;
    private static int   _jPos, _jNeg;

    // ── Priority 4: dispersal counts this interval, per community ────────────────
    private static readonly Dictionary<int, int> _intervalDispersals = new Dictionary<int, int>();

    /// Priority 6 — record each agent's rolled lifespan jitter; emits a distribution summary every
    /// JitterSummaryEvery spawns so the symmetry (or a regression to the one-sided bug) is visible
    /// at a glance without manually aggregating raw per-agent lines.
    public static void RecordLifespanJitter(float jitter)
    {
        _jSampled++;
        _jSum += jitter; _jSumSq += jitter * jitter;
        if (jitter > 0f) _jPos++; else if (jitter < 0f) _jNeg++;
        if (jitter < _jMin) _jMin = jitter;
        if (jitter > _jMax) _jMax = jitter;

        if (_jSampled % JitterSummaryEvery == 0)
            WriteLifespanStats();
    }

    private static int _jLastWrittenAt; // _jSampled value at the last summary write, so session-end doesn't double-write

    private static void WriteLifespanStats()
    {
        if (_writer == null || _jSampled == 0 || _jSampled == _jLastWrittenAt) return;
        _jLastWrittenAt = _jSampled;
        float mean = _jSum / _jSampled;
        float var  = Mathf.Max(0f, _jSumSq / _jSampled - mean * mean);
        _writer.WriteLine($"[LifespanStats] sampled={_jSampled} mean={mean:+0.000;-0.000} stdDev={Mathf.Sqrt(var):0.000} " +
                          $"posCount={_jPos} negCount={_jNeg} min={_jMin:+0.000;-0.000} max={_jMax:+0.000;-0.000}");
    }

    /// Priority 4 — count a dispersal event for its community (rolled up per interval).
    public static void LogDispersal(int communityId)
    {
        _intervalDispersals.TryGetValue(communityId, out int d);
        _intervalDispersals[communityId] = d + 1;
    }

    public static void Init()
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path  = Path.Combine(Application.persistentDataPath, $"gamelog_{stamp}.txt");
        try
        {
            _writer = new StreamWriter(path, append: false) { AutoFlush = true };
            _deathCounts.Clear();
            _intervalBirths.Clear();
            _intervalDeaths.Clear();
            _intervalReproFails.Clear();
            _nextSnapshotTime = SnapshotIntervalSec;
            Application.quitting += WriteSummary;
            // Mirror every Debug.Log/LogWarning/LogError into this same file. Previously the rich
            // per-agent diagnostics (ChemoMeta, PhotoMeta, FIRST_UPDATE, DISPERSAL, MUTATION_ORIGIN,
            // JITTER_SELFTEST, etc.) only went to Unity's console/player log, a SEPARATE file from
            // this one — so diagnosing any specific event required both files, and only the Unity
            // console log was commonly exported. Now a single gamelog_*.txt has everything.
            Application.logMessageReceived += MirrorUnityLog;
            LogGlobal("=== EvoSim Session Begin ===");
            Debug.Log($"[GameLog] Writing to {path}");
            AgentController.SelfTestLifespanJitter(); // now guaranteed to land in this file — see method doc
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GameLog] Could not open log file: {e.Message}");
        }
    }

    private static void MirrorUnityLog(string condition, string stackTrace, LogType type)
    {
        if (_writer == null) return;
        // Skip our own [GameLog]/structured lines re-entering via LogGlobal/LogCommunity — those
        // already write directly to _writer, so mirroring them too would duplicate every line.
        if (condition.StartsWith("[GameLog]") || condition.StartsWith("[t=")) return;
        try { _writer.WriteLine($"[t={Time.time,8:F1}] {(type != LogType.Log ? type + " " : "")}{condition}"); }
        catch { /* writer may be mid-close during shutdown; drop silently */ }
    }

    /// Call once per frame from AgentSpawner.Update. Fires a rich community snapshot every
    /// SnapshotIntervalSec game-seconds and is otherwise a cheap time check.
    public static void MaybeSnapshot(AgentSpawner spawner)
    {
        if (_writer == null || spawner == null) return;
        if (Time.time < _nextSnapshotTime) return;
        _nextSnapshotTime = Time.time + SnapshotIntervalSec;
        WritePeriodicSnapshot(spawner);
    }

    /// Called from AgentController when an organism successfully reproduces.
    public static void LogBirth(int communityId)
    {
        _intervalBirths.TryGetValue(communityId, out int b);
        _intervalBirths[communityId] = b + 1;
    }

    /// Called from AgentController when TryReproduce fails, with a short reason token
    /// (e.g. "PopCap", "NoMate", "LowEnergy").
    public static void LogReproFail(int communityId, string reason)
    {
        if (!_intervalReproFails.TryGetValue(communityId, out var d))
        {
            d = new Dictionary<string, int>();
            _intervalReproFails[communityId] = d;
        }
        d.TryGetValue(reason, out int n);
        d[reason] = n + 1;
    }

    public static void LogGlobal(string msg)
    {
        _writer?.WriteLine($"[t={Time.time,8:F1}] GLOBAL   {msg}");
    }

    /// Exposes the underlying writer so specialised subsystems (e.g. GeneEvolutionManager)
    /// can write multi-line diagnostic blocks directly without routing every line through LogGlobal.
    public static System.IO.StreamWriter Writer => _writer;

    public static void LogCommunity(int id, string msg)
    {
        _writer?.WriteLine($"[t={Time.time,8:F1}] COM#{id,-3}  {msg}");
    }

    public static void LogDeath(int communityId, DeathCause cause, int popAfter)
    {
        LogCommunity(communityId, $"Death:{cause,-20} pop→{popAfter}");
        _deathCounts.TryGetValue(cause, out int n);
        _deathCounts[cause] = n + 1;

        if (!_intervalDeaths.TryGetValue(communityId, out var d))
        {
            d = new Dictionary<DeathCause, int>();
            _intervalDeaths[communityId] = d;
        }
        d.TryGetValue(cause, out int dn);
        d[cause] = dn + 1;
    }

    private static void WritePeriodicSnapshot(AgentSpawner spawner)
    {
        // Gather per-community agent stats
        var pop        = new Dictionary<int, int>();
        var chemo      = new Dictionary<int, int>();
        var photo      = new Dictionary<int, int>();
        var hetero     = new Dictionary<int, int>();
        var energySum  = new Dictionary<int, float>();
        var netEnerSum = new Dictionary<int, float>();
        var geneCount  = new Dictionary<int, int>();

        foreach (var a in spawner.ActiveAgents)
        {
            if (a == null) continue;
            int cid = a.communityId;
            pop.TryGetValue(cid, out int p);         pop[cid] = p + 1;
            energySum.TryGetValue(cid, out float e); energySum[cid] = e + a.EnergyFraction;
            netEnerSum.TryGetValue(cid, out float ne); netEnerSum[cid] = ne + a.NetEnergy;
            geneCount.TryGetValue(cid, out int g);   geneCount[cid] = g + a.AcquiredGenes.Count;

            switch (a.Metabolism)
            {
                case MetabolismType.Chemosynthetic:
                    chemo.TryGetValue(cid, out int ch); chemo[cid] = ch + 1; break;
                case MetabolismType.Phototrophic:
                    photo.TryGetValue(cid, out int ph); photo[cid] = ph + 1; break;
                case MetabolismType.Heterotrophic:
                    hetero.TryGetValue(cid, out int he); hetero[cid] = he + 1; break;
            }
        }

        // "refPop" is the era's balance-tuning reference figure (used for resource scaling, no
        // longer a hard cap). Actual population is unbounded and tracked separately below.
        int refPop = EraManager.Instance != null ? EraManager.Instance.MaxPopulation : -1;
        string era  = GetStageLabel();
        string tempStr = PlanetTemperature.Instance != null ? $"{PlanetTemperature.Instance.CurrentK:F0}K" : "?K";

        // Atmosphere line
        var sb = new StringBuilder();
        if (AtmosphereManager.Instance != null)
        {
            foreach (var g in AtmosphereManager.Instance.Gases)
                if (g.Fraction > 0.001f) sb.Append($" {g.Name}={g.Fraction * 100f:F1}%");
        }

        _writer.WriteLine($"\n[t={Time.time,8:F1}] ── SNAPSHOT  era={era}  temp={tempStr}  " +
                          $"pop={spawner.ActiveAgents.Count} refPop={refPop}  atmo:{sb}");

        // ── Priority 1: single-line PopulationSummary rollup (grep-friendly) ─────────
        int totBirths = 0; foreach (var kv in _intervalBirths) totBirths += kv.Value;
        var causeAgg = new Dictionary<DeathCause, int>();
        int totDeaths = 0;
        foreach (var comm in _intervalDeaths)
            foreach (var kv in comm.Value) { causeAgg.TryGetValue(kv.Key, out int cc); causeAgg[kv.Key] = cc + kv.Value; totDeaths += kv.Value; }
        var causeSb = new StringBuilder();
        foreach (var kv in causeAgg) causeSb.Append($"{kv.Key}:{kv.Value} ");
        float globalNet = 0f; int globalNetN = 0;
        foreach (var kv in netEnerSum) globalNet += kv.Value;
        foreach (var kv in pop) globalNetN += kv.Value;
        int totDisp = 0; foreach (var kv in _intervalDispersals) totDisp += kv.Value;
        string eraName = GetStageLabel();
        _writer.WriteLine($"[PopulationSummary] t={Time.time:F0} era={eraName} pop_global={spawner.ActiveAgents.Count} " +
                          $"births_interval={totBirths} deaths_interval={totDeaths} dispersals_interval={totDisp} " +
                          $"avg_net_energy={(globalNetN > 0 ? globalNet / globalNetN : 0f):+0.0000;-0.0000} " +
                          $"death_causes={{{causeSb.ToString().TrimEnd()}}}");

        // Sort communities by id
        var allCids = new System.Collections.Generic.SortedSet<int>(pop.Keys);
        foreach (var cid in allCids)
        {
            int n = pop.TryGetValue(cid, out int pv) ? pv : 0;
            if (n == 0) continue;

            float avgEnergy    = energySum.TryGetValue(cid, out float ev)  ? ev  / n : 0f;
            float avgNetEnergy = netEnerSum.TryGetValue(cid, out float nv) ? nv  / n : 0f;
            int avgGenes       = geneCount.TryGetValue(cid, out int gv)    ? gv  / n : 0;

            int births = _intervalBirths.TryGetValue(cid, out int bv) ? bv : 0;

            // Deaths in interval
            var dsb = new StringBuilder();
            if (_intervalDeaths.TryGetValue(cid, out var dd) && dd.Count > 0)
                foreach (var kv in dd) dsb.Append($"{kv.Key}:{kv.Value} ");
            string deathStr = dsb.Length > 0 ? dsb.ToString().TrimEnd() : "none";

            // Repro fails in interval
            var rsb = new StringBuilder();
            if (_intervalReproFails.TryGetValue(cid, out var rf) && rf.Count > 0)
                foreach (var kv in rf) rsb.Append($"{kv.Key}:{kv.Value} ");
            string reproStr = rsb.Length > 0 ? rsb.ToString().TrimEnd() : "—";

            int chemoN  = chemo.TryGetValue(cid, out int cv) ? cv : 0;
            int photoN  = photo.TryGetValue(cid, out int phn) ? phn : 0;
            int heteroN = hetero.TryGetValue(cid, out int hv) ? hv : 0;

            _writer.WriteLine(
                $"[t={Time.time,8:F1}] COM#{cid,-3}  " +
                $"pop={n,-4} births={births,-3} deaths=[{deathStr}]  " +
                $"chemo={chemoN} photo={photoN} het={heteroN}  " +
                $"avgEnergy={avgEnergy:F2}  avgNet={avgNetEnergy:+0.0000;-0.0000}  avgGenes={avgGenes}  " +
                $"reproFails=[{reproStr}]");
        }

        // Full per-gene eligibility diagnosis for one player agent
        AgentController playerAgent = null;
        foreach (var a in spawner.ActiveAgents)
        {
            if (a != null && a.communityId == GeneEvolutionManager.PlayerCommunityId)
            { playerAgent = a; break; }
        }
        GeneEvolutionManager.DiagnoseGenes(playerAgent, _writer);

        // Reset interval counters
        _intervalBirths.Clear();
        _intervalDeaths.Clear();
        _intervalReproFails.Clear();
        _intervalDispersals.Clear();
    }

    /// Log a one-line population snapshot across all communities.
    public static void Snapshot(AgentSpawner spawner)
    {
        if (spawner == null || _writer == null) return;
        var counts = new Dictionary<int, int>();
        foreach (var a in spawner.ActiveAgents)
        {
            if (a == null) continue;
            counts.TryGetValue(a.communityId, out int c);
            counts[a.communityId] = c + 1;
        }
        var sb = new StringBuilder("PopSnapshot:");
        foreach (var kv in counts)
            sb.Append($" COM#{kv.Key}={kv.Value}");
        LogGlobal(sb.ToString());
    }

    // Set by AgentSpawner so the session-end line can report the true final population/era even
    // though Application.quitting passes no arguments.
    private static AgentSpawner _spawnerRef;
    public static void RegisterSpawner(AgentSpawner s) => _spawnerRef = s;

    public static void WriteSummary()
    {
        if (_writer == null) return;
        Application.quitting -= WriteSummary;
        Application.logMessageReceived -= MirrorUnityLog;
        WriteLifespanStats(); // final read even if the run never crossed the periodic threshold
        _writer.WriteLine();

        // Priority 8: explicit completeness marker. If this line is ABSENT from an exported log,
        // the export was truncated (session didn't stop cleanly) and shouldn't be trusted to
        // contain the actual end-of-run event.
        int finalPop = _spawnerRef != null ? _spawnerRef.ActiveAgents.Count : -1;
        string finalEra = GetStageLabel();
        _writer.WriteLine($"[GameLog] SESSION_END final_pop={finalPop} final_era={finalEra} reason=UserStopped t={Time.time:F0}");

        _writer.WriteLine("=== Session Summary ===");
        if (_deathCounts.Count == 0)
        {
            _writer.WriteLine("  No deaths recorded.");
        }
        else
        {
            foreach (var kv in _deathCounts)
                _writer.WriteLine($"  Deaths by {kv.Key}: {kv.Value}");
        }
        _writer.WriteLine("=== End ===");
        _writer.Flush();
        _writer.Close();
        _writer = null;
    }
}
