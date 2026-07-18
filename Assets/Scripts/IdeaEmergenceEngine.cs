using System;
using System.Collections.Generic;
using UnityEngine;

/// Probabilistic Idea emergence engine (idea-emergence-spec §3).
///
/// Each registered IdeaDef accumulates exposure_time for each civilization
/// whose pressure exceeds the idea's threshold.  A Weibull-hazard roll fires
/// once per SampleInterval; on success the Idea becomes available and an
/// OnIdeaEmerged event fires so Era3Manager can notify the player.
///
/// Design principle: the player cannot trigger emergence directly — only
/// indirectly through conditions that drive the underlying pressure variables.
public class IdeaEmergenceEngine
{
    // ── Idea definition (spec §3.1) ──────────────────────────────────────────
    public class IdeaDef
    {
        public string Id;

        /// All must return true for this civ before exposure begins (hard gate, §3.2).
        public Func<CivilizationState, bool>[] Preconditions;

        /// Continuous pressure variable in [0, 1] that drives exposure (§3.1 condition_set).
        public Func<CivilizationState, float> PressureSource;

        /// Minimum pressure for exposure to accumulate (§3.1 threshold).
        public float Threshold = 0.3f;

        /// Minimum P(emerge) per sample tick once exposure begins (§3.1 base_rate).
        public float BaseRate = 0.03f;

        /// Weibull shape exponent k > 1 — slight upward hazard over time (§3.3).
        public float K = 1.15f;

        /// Per-second decay rate of exposure_time when pressure drops below threshold (§3.3).
        public float ExposureDecayRate = 0.015f;

        /// Cultural-diffusion rate: extra exposure_time accrued per second per unit of adoption
        /// prevalence in OTHER civilizations. An idea already present elsewhere exerts transmission
        /// pressure (trade/contact/informational channel), so it spreads faster than it originates —
        /// a civ can pick it up from neighbours without independently reaching the pressure threshold.
        public float DiffusionRate = 2.0f;

        /// Human-readable name shown in the HUD idea card.
        public string DisplayName;
        public string Description;
    }

    // ── Emergence notification ────────────────────────────────────────────────
    /// Fires when an Idea successfully emerges for a civilization.
    public event Action<CivilizationState, IdeaDef> OnIdeaEmerged;

    // ── Internal state ─────────────────────────────────────────────────────────
    private readonly List<IdeaDef> _ideas = new();

    // exposure_time per (civId, ideaId) in seconds.
    private readonly Dictionary<(int, string), float> _exposure = new();

    // Ideas that have already emerged (available to player, no re-roll needed).
    private readonly HashSet<(int, string)> _emerged = new();

    // Ideas the player has chosen to invest in / adopt.
    private readonly HashSet<(int, string)> _adopted = new();

    // Sample interval (§3.4): coarser than per-frame to reduce overhead.
    private const float SampleInterval = 12f;
    private float _sampleTimer;

    // ── Registration ──────────────────────────────────────────────────────────
    public void Register(IdeaDef idea) => _ideas.Add(idea);

    // ── Queries ───────────────────────────────────────────────────────────────
    public bool HasEmerged(int civId, string ideaId) => _emerged.Contains((civId, ideaId));
    public bool HasAdopted(int civId, string ideaId) => _adopted.Contains((civId, ideaId));

    /// Number of civilizations in which the given idea has already emerged — the diffusion source.
    private int CountEmerged(string ideaId)
    {
        int n = 0;
        foreach (var (_, id) in _emerged) if (id == ideaId) n++;
        return n;
    }

    public void Adopt(int civId, string ideaId)
    {
        _adopted.Add((civId, ideaId));
        Debug.Log($"[IdeaEmergence] Civ {civId} adopted Idea '{ideaId}'.");
    }

    /// Returns all ideas currently available (emerged but not yet adopted) for this civ.
    public List<IdeaDef> GetAvailable(int civId)
    {
        var result = new List<IdeaDef>();
        foreach (var idea in _ideas)
            if (_emerged.Contains((civId, idea.Id)) && !_adopted.Contains((civId, idea.Id)))
                result.Add(idea);
        return result;
    }

    /// Returns the exposure fraction [0,1] for display (how 'close' an Idea is to emerging).
    /// Value is a rough gauge — not a guaranteed countdown.
    public float GetExposureFraction(int civId, string ideaId)
    {
        if (!_exposure.TryGetValue((civId, ideaId), out float e)) return 0f;
        // Exposure of ~300s typically gives P(emerge/tick) ≈ several times base_rate.
        // Use 300s as the display ceiling so the bar isn't permanently empty.
        return Mathf.Clamp01(e / 300f);
    }

    // ── Tick ──────────────────────────────────────────────────────────────────
    /// Call from Era3Manager.Update with the full civilization list and elapsed dt.
    public void Tick(IReadOnlyList<CivilizationState> civs, float dt)
    {
        // Continuous exposure accumulation / decay regardless of sample cadence.
        foreach (var civ in civs)
        {
            foreach (var idea in _ideas)
            {
                var key = (civ.CommunityId, idea.Id);
                if (_emerged.Contains(key)) continue;
                if (!AllPreconditionsMet(civ, idea)) { Decay(key, idea.ExposureDecayRate, dt); continue; }

                float pressure = idea.PressureSource?.Invoke(civ) ?? 0f;
                if (pressure >= idea.Threshold)
                    _exposure[key] = _exposure.GetValueOrDefault(key) + dt;
                else
                    Decay(key, idea.ExposureDecayRate, dt);

                // Cultural diffusion: an idea already present in other civilizations adds exposure
                // here proportional to how widespread it is, so it spreads civ-to-civ rather than
                // every civ having to independently originate it. Preconditions still gate it (a
                // Collective without caste-tech still can't receive Planned Colonization).
                int adopters = CountEmerged(idea.Id);
                if (adopters > 0)
                {
                    float prevalence = adopters / (float)Mathf.Max(1, civs.Count);
                    _exposure[key] = _exposure.GetValueOrDefault(key) + idea.DiffusionRate * prevalence * dt;
                }
            }
        }

        // Emergence roll at coarse sample interval (§3.4).
        _sampleTimer += dt;
        if (_sampleTimer < SampleInterval) return;
        _sampleTimer = 0f;

        foreach (var civ in civs)
        {
            foreach (var idea in _ideas)
            {
                var key = (civ.CommunityId, idea.Id);
                if (_emerged.Contains(key)) continue;
                if (!AllPreconditionsMet(civ, idea)) continue;

                float exposure = _exposure.GetValueOrDefault(key);
                if (exposure <= 0f) continue;

                // P(emerge this tick) = base_rate × exposure_time^k  (§3.3 Weibull hazard)
                float p = idea.BaseRate * Mathf.Pow(exposure, idea.K);
                if (UnityEngine.Random.value < p)
                {
                    _emerged.Add(key);
                    _exposure.Remove(key);
                    Debug.Log($"[IdeaEmergence] Idea '{idea.Id}' emerged for civ {civ.CommunityId} " +
                              $"(exposure was {exposure:F0}s, p={p:F3}).");
                    OnIdeaEmerged?.Invoke(civ, idea);
                }
            }
        }
    }

    private void Decay((int, string) key, float rate, float dt)
    {
        if (!_exposure.TryGetValue(key, out float e)) return;
        e = Mathf.Max(0f, e - rate * dt);
        if (e <= 0f) _exposure.Remove(key);
        else         _exposure[key] = e;
    }

    private static bool AllPreconditionsMet(CivilizationState civ, IdeaDef idea)
    {
        if (idea.Preconditions == null) return true;
        foreach (var p in idea.Preconditions)
            if (!p(civ)) return false;
        return true;
    }
}
