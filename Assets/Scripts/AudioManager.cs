using System.Collections;
using UnityEngine;

/// Adaptive layered music + SFX manager for the three-era fork system.
///
/// Music architecture (Unity AudioMixer-based, no FMOD dependency):
///   Era  = which theme family plays  (era-shift trigger)
///   Fork = instrumental variant within that era (resolved once at era entry)
///   Two AudioSources crossfade music; one AudioSource plays SFX via PlayOneShot.
///
/// Era 1 fork — planetary geochemical archetype (3 buckets + exotic modifier):
///   Hydrothermal | Photic | Cryogenic  +  exotic timbral overlay flag
/// Era 2 fork — primary energy strategy (Autotroph | Heterotroph | Mixotroph)
/// Era 3 fork — cognitive architecture  (Individuated | Distributed | Collective)
///
/// AudioClips are assigned via Inspector (public fields). All SFX clips are
/// optional — missing clips are silently skipped. Music clips may be null during
/// development; the crossfade still runs so the wiring is in place.
///
/// SimulationBootstrap adds this component before live gameplay begins.
/// AgentSpawner is passed via Init() (no static Instance on AgentSpawner).
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    // ── Music crossfade ─────────────────────────────────────────────────────────
    private const float MusicFadeDuration       = 3.0f;
    private const float ForkFocusFadeDuration   = 2.0f;

    private AudioSource _musicA;
    private AudioSource _musicB;
    private AudioSource _sfxSource;
    private bool _usingA = true;
    private Coroutine _musicFade;

    // ── Era 1 music clips (assign in Inspector) ─────────────────────────────────
    [Header("Era 1 Music — Geochemical Fork")]
    public AudioClip era1Hydrothermal;
    public AudioClip era1Photic;
    public AudioClip era1Cryogenic;
    // Exotic timbral overlay (played simultaneously with the bucket clip at reduced volume).
    public AudioClip era1ExoticOverlay;
    private AudioSource _era1OverlaySource;

    // ── Era 2 music clips ────────────────────────────────────────────────────────
    [Header("Era 2 Music — Energy Strategy Fork")]
    public AudioClip era2Autotroph;
    public AudioClip era2Heterotroph;
    public AudioClip era2Mixotroph;

    // ── Era 3 music clips ────────────────────────────────────────────────────────
    [Header("Era 3 Music — Cognitive Architecture Fork")]
    public AudioClip era3Individuated;
    public AudioClip era3Distributed;
    public AudioClip era3Collective;

    // ── Intro / cinematic ────────────────────────────────────────────────────────
    [Header("Intro")]
    public AudioClip introCinematic;

    // ── SFX clips (§2.7) ────────────────────────────────────────────────────────
    [Header("SFX — Era Transitions")]
    public AudioClip sfxEraShiftEra1;
    public AudioClip sfxEraShiftEra2;
    public AudioClip sfxEraShiftEra3;

    [Header("SFX — Biology")]
    public AudioClip sfxSpeciation;
    public AudioClip sfxMassExtinction;
    public AudioClip sfxEndOfEra2Threshold;   // biggest narrative beat

    [Header("SFX — Civilization")]
    public AudioClip sfxCivFounded;
    public AudioClip sfxWarDeclared;
    public AudioClip sfxTreatyFormed;
    public AudioClip sfxTradeAgreement;
    public AudioClip sfxReligionFounded;
    public AudioClip sfxSchism;
    public AudioClip sfxExchangeContact;
    public AudioClip sfxCrisisWarning;         // escalating tension pulse
    public AudioClip sfxCivCollapse;           // player/named civ collapse

    // ── Internal state ────────────────────────────────────────────────────────────
    private AgentSpawner _spawner;
    private bool _exoticActive;

    // ── Lifecycle ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        _musicA = gameObject.AddComponent<AudioSource>();
        _musicA.loop = true;
        _musicA.volume = 0f;
        _musicA.playOnAwake = false;

        _musicB = gameObject.AddComponent<AudioSource>();
        _musicB.loop = true;
        _musicB.volume = 0f;
        _musicB.playOnAwake = false;

        _sfxSource = gameObject.AddComponent<AudioSource>();
        _sfxSource.playOnAwake = false;

        _era1OverlaySource = gameObject.AddComponent<AudioSource>();
        _era1OverlaySource.loop = true;
        _era1OverlaySource.volume = 0f;
        _era1OverlaySource.playOnAwake = false;
    }

    public void Init(AgentSpawner spawner)
    {
        _spawner = spawner;
    }

    // ── Era hooks ─────────────────────────────────────────────────────────────────

    public void OnIntroBegin()
    {
        CrossfadeMusic(introCinematic, MusicFadeDuration);
    }

    public void OnEraShiftToEra1()
    {
        PlaySfx(sfxEraShiftEra1);
        AudioClip clip = ResolveEra1Fork(out bool exotic);
        CrossfadeMusic(clip, MusicFadeDuration);
        SetExoticOverlay(exotic);
    }

    public void OnEraShiftToEra2()
    {
        PlaySfx(sfxEraShiftEra2);
        SetExoticOverlay(false);
        AudioClip clip = ResolveEra2Fork();
        CrossfadeMusic(clip, MusicFadeDuration);
    }

    public void OnEraShiftToEra3()
    {
        PlaySfx(sfxEraShiftEra3);
        AudioClip clip = ResolveEra3Fork();
        CrossfadeMusic(clip, MusicFadeDuration);
    }

    // Called when the player switches focus to a different lineage/civilization.
    // Resolves the fork for that entity and cross-fades to it within the current era's family.
    public void OnFocusChange(int communityId)
    {
        // Era 3: look up the civ architecture and pick its fork variant.
        if (Era3Manager.Instance != null && Era3Manager.Instance.IsActive)
        {
            CivilizationState civ = FindCiv(communityId);
            if (civ == null) return;
            AudioClip clip = Era3ForkClip(civ.Architecture);
            CrossfadeMusic(clip, ForkFocusFadeDuration);
        }
        // Era 1/2 focus change: no mid-era fork swap — spec §2.6.
    }

    // ── SFX public triggers ───────────────────────────────────────────────────────

    public void OnSpeciation()            => PlaySfx(sfxSpeciation);
    public void OnMassExtinction()        => PlaySfx(sfxMassExtinction);
    public void OnEndOfEra2Threshold()    => PlaySfx(sfxEndOfEra2Threshold);
    public void OnCivFounded()            => PlaySfx(sfxCivFounded);
    public void OnWarDeclared()           => PlaySfx(sfxWarDeclared);
    public void OnTreatyFormed()          => PlaySfx(sfxTreatyFormed);
    public void OnTradeAgreement()        => PlaySfx(sfxTradeAgreement);
    public void OnReligionFounded()       => PlaySfx(sfxReligionFounded);
    public void OnSchism()                => PlaySfx(sfxSchism);
    public void OnExchangeContact()       => PlaySfx(sfxExchangeContact);
    public void OnCrisisWarning()         => PlaySfx(sfxCrisisWarning);
    public void OnCivCollapse(bool named) { if (named) PlaySfx(sfxCivCollapse); }

    // ── Fork resolution ───────────────────────────────────────────────────────────

    // Era 1: geochemical archetype from PlanetTemperature + fluid/vent state.
    // 3 buckets: Hydrothermal (vent-dominant), Photic (high irradiance), Cryogenic (ice-shell).
    // Exotic backbone = timbral overlay on top of whichever bucket matches.
    private AudioClip ResolveEra1Fork(out bool exotic)
    {
        exotic = false;
        float tempK = PlanetTemperature.Instance != null ? PlanetTemperature.Instance.CurrentK : 288f;

        // Exotic backbone: detected from AgentController's static xenobiology flag.
        var backbone = AtmosphereManager.Instance?.RolledBiochemistry?.Backbone ?? BackboneElement.Carbon;
        exotic = backbone != BackboneElement.Carbon;

        // Cryogenic: subsurface tidal-heated ocean; surface temp below water freeze.
        if (tempK < 250f)
            return era1Cryogenic;

        // Hydrothermal: vent presence signals vent-dominant chemosynthetic energy regime.
        if (HydrothermalVentManager.Instance != null && HydrothermalVentManager.Instance.VentCount > 0)
            return era1Hydrothermal;

        // Photic: default for warm, open-ocean, irradiance-available worlds.
        return era1Photic;
    }

    // Era 2: primary energy strategy from player lineage EnergyStrategy snapshot.
    private AudioClip ResolveEra2Fork()
    {
        if (Era2Manager.Instance == null) return era2Autotroph;
        var rec = Era2Manager.Instance.GetRecord(0);
        if (rec == null) return era2Autotroph;

        return rec.EnergyStrategy switch
        {
            MetabolismType.Phototrophic  => era2Autotroph,
            MetabolismType.Heterotrophic => era2Heterotroph,
            MetabolismType.Mixotrophic   => era2Mixotroph,
            _                            => era2Autotroph,  // Chemosynthetic → autotroph bucket
        };
    }

    // Era 3: cognitive architecture from player civ.
    private AudioClip ResolveEra3Fork()
    {
        if (Era3Manager.Instance == null) return era3Individuated;
        return Era3ForkClip(Era3Manager.Instance.PlayerCiv.Architecture);
    }

    private AudioClip Era3ForkClip(CognitiveArchitecture arch) => arch switch
    {
        CognitiveArchitecture.Distributed => era3Distributed,
        CognitiveArchitecture.Collective  => era3Collective,
        _                                  => era3Individuated,
    };

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private void PlaySfx(AudioClip clip)
    {
        if (clip != null && _sfxSource != null)
            _sfxSource.PlayOneShot(clip);
    }

    private void SetExoticOverlay(bool active)
    {
        if (_era1OverlaySource == null) return;
        if (active && !_exoticActive)
        {
            if (era1ExoticOverlay != null)
            {
                _era1OverlaySource.clip = era1ExoticOverlay;
                _era1OverlaySource.volume = 0.25f;
                _era1OverlaySource.Play();
            }
            _exoticActive = true;
        }
        else if (!active && _exoticActive)
        {
            _era1OverlaySource.Stop();
            _exoticActive = false;
        }
    }

    private void CrossfadeMusic(AudioClip clip, float duration)
    {
        if (_musicFade != null) StopCoroutine(_musicFade);
        _musicFade = StartCoroutine(MusicCrossfade(clip, duration));
    }

    private IEnumerator MusicCrossfade(AudioClip clip, float duration)
    {
        AudioSource incoming = _usingA ? _musicB : _musicA;
        AudioSource outgoing = _usingA ? _musicA : _musicB;

        if (clip != null)
        {
            incoming.clip   = clip;
            incoming.volume = 0f;
            incoming.Play();
        }

        float t = 0f;
        float startVol = outgoing.volume;
        while (t < duration)
        {
            t += Time.deltaTime;
            float frac = Mathf.Clamp01(t / duration);
            outgoing.volume = Mathf.Lerp(startVol, 0f, frac);
            if (clip != null) incoming.volume = frac;
            yield return null;
        }

        outgoing.Stop();
        outgoing.volume = 0f;
        if (clip != null) incoming.volume = 1f;

        _usingA    = !_usingA;
        _musicFade = null;
    }

    private CivilizationState FindCiv(int communityId)
    {
        if (Era3Manager.Instance == null) return null;
        if (Era3Manager.Instance.PlayerCiv?.CommunityId == communityId)
            return Era3Manager.Instance.PlayerCiv;
        foreach (var npc in Era3Manager.Instance.NpcCivs)
            if (npc.CommunityId == communityId) return npc;
        return null;
    }
}
