using System.Collections.Generic;
using UnityEngine;

/// era3-diplomacy-ai-spec: the accept_probability logistic function AI civs use to respond to
/// diplomatic actions, built on top of the two-tier PolityRelation/SpeciesDisposition model
/// (storage + first-contact seeding + EMA update live on Era3Manager, since they need manager-wide
/// state; this file holds the pure-function scoring machinery and the era3-primitives-spec §1-§2
/// trait derivations that feed TraitFactor).
///
/// Two real gaps in what era3-primitives-spec §1.3/§2 assumes already exists, resolved with the
/// closest real substitute rather than invented from scratch: `kin_recognition_strength` and a
/// tracked `dispersal_rate` stat are NOT present anywhere in this codebase (confirmed by search) —
/// KinBias below uses the existing player-facing Kinship policy field instead, which is thematically
/// the most direct real analog. `predation_reliance` (a diet-strategy stat) also isn't tracked;
/// DomainKinetic (native kinetic war-domain coverage) is used as its closest real substitute.
public static class Era3Diplomacy
{
    public enum ActionType
    {
        CollectiveSecurityAlliance, RequestWarAssistance, JointResearch, JointOperation,
        StealTech, AcceptPeace, AcceptVassalage, DemandVassalage, DeclareWar, LandConcessionAccept,
    }

    private struct Weights { public float wPr, wSd, wPow, wEnemy, wCost, wGain, threshold; }

    // §3.1 per-action weight table, copied verbatim (all explicitly TUNABLE / first-pass per spec).
    private static readonly Dictionary<ActionType, Weights> W = new()
    {
        [ActionType.CollectiveSecurityAlliance] = new Weights{ wPr=1.2f,  wSd=0.35f, wPow=0.2f, wEnemy=1.0f, wCost=0.3f, wGain=0.3f, threshold=0.6f },
        [ActionType.RequestWarAssistance]       = new Weights{ wPr=1.0f,  wSd=0.3f,  wPow=0.3f, wEnemy=0.6f, wCost=1.2f, wGain=0.2f, threshold=0.8f },
        [ActionType.JointResearch]              = new Weights{ wPr=0.8f,  wSd=0.25f, wPow=0.1f, wEnemy=0.2f, wCost=0.4f, wGain=1.0f, threshold=0.5f },
        [ActionType.JointOperation]             = new Weights{ wPr=1.0f,  wSd=0.3f,  wPow=0.4f, wEnemy=0.9f, wCost=0.8f, wGain=0.6f, threshold=0.7f },
        [ActionType.StealTech]                  = new Weights{ wPr=-0.6f, wSd=-0.2f, wPow=1.2f, wEnemy=0.1f, wCost=0.5f, wGain=1.0f, threshold=0.9f },
        [ActionType.AcceptPeace]                = new Weights{ wPr=0.7f,  wSd=0.2f,  wPow=1.0f, wEnemy=0.1f, wCost=1.0f, wGain=0.5f, threshold=0.4f },
        [ActionType.AcceptVassalage]             = new Weights{ wPr=0.5f,  wSd=0.2f,  wPow=1.5f, wEnemy=0.4f, wCost=0.6f, wGain=0.3f, threshold=0.9f },
        [ActionType.DemandVassalage]             = new Weights{ wPr=-0.3f, wSd=-0.15f,wPow=1.4f, wEnemy=0.2f, wCost=0.7f, wGain=1.0f, threshold=0.85f },
        [ActionType.DeclareWar]                  = new Weights{ wPr=-1.2f, wSd=-0.4f, wPow=1.0f, wEnemy=0.3f, wCost=1.0f, wGain=0.8f, threshold=0.9f },
        [ActionType.LandConcessionAccept]        = new Weights{ wPr=0.6f,  wSd=0.2f,  wPow=1.0f, wEnemy=0.1f, wCost=0.8f, wGain=0.4f, threshold=0.5f },
    };

    // event_valence(A,B,t) per interaction (§1.2).
    public const float ValenceWarDeclaration      = -0.8f;
    public const float ValenceTreatyBetrayal      = -1.0f;
    public const float ValenceHostileOccupation   = -0.5f; // per tick, while sustained
    public const float ValenceFavorableTrade       = 0.1f;
    public const float ValenceJointOperation       = 0.4f;
    public const float ValenceHonoredAllianceCall  = 0.6f;
    public const float ValenceAcceptedPeace        = 0.2f;
    public const float ValenceRefusedWarAssistance = -0.3f;

    public const float LambdaPr       = 0.10f;
    public const float PullToSpecies  = 0.01f;
    public const float LambdaSd       = 0.02f;
    private const float SigmaAi        = 0.15f;

    // ── §4 per-AI variance — seeded per (civ, action), fixed for that civ's lifetime ────────────
    private static readonly Dictionary<int, float> _aiOffsetCache = new Dictionary<int, float>();

    private static float AiOffset(CivilizationState ai, ActionType action)
    {
        int key = ai.CommunityId * 97 + (int)action;
        if (_aiOffsetCache.TryGetValue(key, out float cached)) return cached;

        var prevState = Random.state;
        Random.InitState(key * 7919 + 13);
        float sum = 0f; for (int i = 0; i < 6; i++) sum += Random.value; // cheap ~N(0,1) via sum of uniforms
        Random.state = prevState;

        float u = (sum - 3f) * SigmaAi;
        _aiOffsetCache[key] = u;
        return u;
    }

    // ── era3-primitives-spec §1: traits that derive cleanly from existing Era 1/2 state ─────────
    public static float Sociality(CivilizationState civ)
    {
        // sociality_stage folded into the structure-weight axis: by Era 3 every civ's SocialStructure
        // has resolved past Unset (seeded in BuildCivFromCommunity), making the stage component the
        // spec calls for largely redundant with structure at this point in the pipeline.
        return civ.SocialStructure switch
        {
            SocialStructureType.SolitaryTerritorial => 0.0f,
            SocialStructureType.PairBonded           => 0.5f,
            SocialStructureType.MultiMemberTroop      => 0.65f,
            SocialStructureType.FissionFusion          => 0.75f,
            SocialStructureType.EusocialColonial        => 1.0f,
            _ => 0.4f,
        };
    }

    public static float Territoriality(CivilizationState civ)
    {
        float intensity = 0f;
        var rec = TerritorialityManager.Instance?.GetRecord(civ.CommunityId);
        if (rec != null)
            intensity = rec.Strictness switch
            {
                TerritorialityStrictness.Nomadic    => 0.0f,
                TerritorialityStrictness.LooseRange => 0.5f,
                TerritorialityStrictness.StrictSite  => 1.0f,
                _ => 0f,
            };
        float solitaryBonus = civ.SocialStructure == SocialStructureType.SolitaryTerritorial ? 1f : 0.3f;
        return Mathf.Clamp01(0.6f * intensity + 0.4f * solitaryBonus);
    }

    public static float KinBias(CivilizationState civ) => civ.Kinship switch
    {
        KinshipPolicy.Nuclear      => 0.9f,
        KinshipPolicy.Extended     => 0.6f,
        KinshipPolicy.Clan         => 0.7f,
        KinshipPolicy.CrossLineage => 0.2f,
        _ => 0.5f,
    };

    // ── era3-primitives-spec §2: traits built on Phase 1's new contestPropensity/boldness ────────
    public static float Aggression(Era3Manager mgr, CivilizationState civ)
    {
        float contest = mgr.AverageContestPropensity(civ.CommunityId) / 100f;
        float predationReliance = civ.DomainKinetic; // closest real substitute — see file header
        return Mathf.Clamp01(0.5f * contest + 0.3f * predationReliance + 0.2f * Territoriality(civ));
    }

    public static float RiskTolerance(Era3Manager mgr, CivilizationState civ)
    {
        float boldness01 = mgr.AverageBoldness(civ.CommunityId) / 100f;
        // No tracked per-civ dispersal-rate stat (see file header) — boldness alone, scaled to
        // occupy the same [0,1] band the 0.7/0.3 blend would have produced.
        return Mathf.Clamp01(boldness01);
    }

    // ── §2 TraitFactor(AI, action) — per-action coefficients on the 5 traits, mapped to [-1,1] ──
    // Not enumerated anywhere in the spec (explicitly flagged as an open item, §5.2) — filled in
    // here from each action's "Dominant trait" annotation in §3.1's table, which IS spec text.
    private struct TraitCoeffs { public float Aggr, Soc, Terr, Risk, Kin; }
    private static readonly Dictionary<ActionType, TraitCoeffs> TraitW = new()
    {
        [ActionType.CollectiveSecurityAlliance] = new TraitCoeffs{ Soc=1.0f, Kin=0.3f },
        [ActionType.RequestWarAssistance]       = new TraitCoeffs{ Aggr=0.6f, Risk=0.6f },
        [ActionType.JointResearch]              = new TraitCoeffs{ Soc=1.0f },
        [ActionType.JointOperation]             = new TraitCoeffs{ Aggr=1.0f, Soc=0.3f, Risk=0.3f },
        [ActionType.StealTech]                  = new TraitCoeffs{ Soc=-0.6f, Risk=1.0f },
        [ActionType.AcceptPeace]                = new TraitCoeffs{ Aggr=-0.8f, Terr=0.5f },
        [ActionType.AcceptVassalage]             = new TraitCoeffs{ Risk=-1.0f },
        [ActionType.DemandVassalage]             = new TraitCoeffs{ Aggr=1.0f },
        [ActionType.DeclareWar]                  = new TraitCoeffs{ Aggr=1.0f, Terr=0.6f },
        [ActionType.LandConcessionAccept]        = new TraitCoeffs{ Terr=-1.2f },
    };
    private const float TraitFactorScale = 0.5f; // TUNABLE overall scale so traits nudge, not dominate

    public static float TraitFactor(Era3Manager mgr, CivilizationState civ, ActionType action)
    {
        var c = TraitW[action];
        float Map(float t01) => (Mathf.Clamp01(t01) - 0.5f) * 2f; // [0,1] → [-1,1]
        float sum = c.Aggr * Map(Aggression(mgr, civ))
                  + c.Soc  * Map(Sociality(civ))
                  + c.Terr * Map(Territoriality(civ))
                  + c.Risk * Map(RiskTolerance(mgr, civ))
                  + c.Kin  * Map(KinBias(civ));
        return sum * TraitFactorScale;
    }

    // ── §3 acceptance function ────────────────────────────────────────────────────────────────
    public static float AcceptProbability(Era3Manager mgr, CivilizationState ai, CivilizationState proposer,
        ActionType action, float relativePowerDelta, float sharedEnemy, float projectedCost, float projectedGain)
    {
        var w = W[action];
        float pr = mgr.GetPolityRelation(ai.CommunityId, proposer.CommunityId);
        float sd = mgr.GetSpeciesDisposition(ai.CommunityId, proposer.CommunityId);
        float trait = TraitFactor(mgr, ai, action);
        float offset = AiOffset(ai, action);

        // era3-policy-catalog-spec: Isolationism/Hegemonic Expansion/Balance of Power/Collective
        // Security etc. all read as a direct scale on how hard THIS specific action is to clear —
        // read from the AI's own active policies, since it's the AI's threshold to accept/refuse.
        string thresholdVar = action switch
        {
            ActionType.CollectiveSecurityAlliance => Era3PolicyCatalog.Var.AllianceThreshold,
            ActionType.DeclareWar or ActionType.AcceptPeace or ActionType.AcceptVassalage or ActionType.DemandVassalage
                => Era3PolicyCatalog.Var.WarThreshold,
            _ => null,
        };
        float threshold = w.threshold * (thresholdVar != null ? Era3PolicyCatalog.GetVar(ai, thresholdVar) : 1f);

        float z = w.wPr * pr + w.wSd * sd + w.wPow * relativePowerDelta + w.wEnemy * sharedEnemy
                + w.wCost * (-projectedCost) + w.wGain * projectedGain + trait + offset - threshold;
        return 1f / (1f + Mathf.Exp(-z));
    }

    /// Convenience wrapper for the common case (no shared-enemy/cost/gain modeling available at the
    /// call site — those terms default to neutral rather than being fabricated).
    public static float AcceptProbability(Era3Manager mgr, CivilizationState ai, CivilizationState proposer, ActionType action)
    {
        float powerDelta = Mathf.Clamp((ai.Resilience + ai.DomainKinetic * 0.5f) - (proposer.Resilience + proposer.DomainKinetic * 0.5f), -1f, 1f);
        return AcceptProbability(mgr, ai, proposer, action, powerDelta, 0f, 0.3f, 0.3f);
    }
}
