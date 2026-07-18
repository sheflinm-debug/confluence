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

        // Initialize AudioSources immediately in Awake so public hooks are safe
        // to call in the same frame that AddComponent runs.
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

    void OnDestroy() { if (Instance == this) Instance = null; }

    public void Init(AgentSpawner spawner)
    {
        _spawner = spawner;
        // Fill any unassigned Inspector clips with synthesized placeholders so the
        // audio system is functional even without authored audio files.
        GenerateProceduralClips();
        // Start playing intro music immediately — era hooks fire later.
        CrossfadeMusic(introCinematic, MusicFadeDuration);
    }

    // ── Procedural placeholder audio ──────────────────────────────────────────

    private void GenerateProceduralClips()
    {
        const int R = 44100;

        // Era 1 — geochemical archetype; public-domain melodies over drone pads
        if (era1Hydrothermal  == null) era1Hydrothermal  = MakeMelodyClip("E1_Hydro",  s_OdeToJoy,      new[]{ 55f, 110f, 165f }, R);
        if (era1Photic        == null) era1Photic        = MakeMelodyClip("E1_Photic", s_Hallelujah,    new[]{ 82f, 164f, 246f }, R);
        if (era1Cryogenic     == null) era1Cryogenic     = MakeMelodyClip("E1_Cryo",   s_Cryogenic,     new[]{ 41f,  82f, 123f }, R, melodyVol: 0.38f, droneVol: 0.20f);
        if (era1ExoticOverlay == null) era1ExoticOverlay = MakeDroneClip ("E1_Exotic", new[]{ 369f, 493f }, 12f, R, 0.30f);

        // Era 2 — primary energy strategy
        if (era2Autotroph   == null) era2Autotroph   = MakeMelodyClip("E2_Auto",   s_Greensleeves,  new[]{ 110f, 220f, 330f }, R);
        if (era2Heterotroph == null) era2Heterotroph = MakeMelodyClip("E2_Hetero", s_PachelbelCanon,new[]{ 92f,  185f, 277f }, R);
        if (era2Mixotroph   == null) era2Mixotroph   = MakeMelodyClip("E2_Mixo",   s_MinuetG,       new[]{ 98f,  196f, 294f }, R, melodyVol: 0.42f);

        // Era 3 — cognitive architecture
        if (era3Individuated == null) era3Individuated = MakeMelodyClip("E3_Ind",  s_FurElise,    new[]{ 130f, 261f, 392f }, R);
        if (era3Distributed  == null) era3Distributed  = MakeMelodyClip("E3_Dist", s_BachInv1,    new[]{ 116f, 233f, 349f }, R, melodyVol: 0.38f);
        if (era3Collective   == null) era3Collective   = MakeMelodyClip("E3_Coll", s_MountainKing,new[]{ 87f,  174f, 261f }, R);

        // Intro
        if (introCinematic == null) introCinematic = MakeMelodyClip("Intro", s_MinuetG, new[]{ 65f, 130f, 196f }, R, melodyVol: 0.48f);

        // SFX — chord stabs, amplitude raised for audibility
        if (sfxEraShiftEra1       == null) sfxEraShiftEra1       = MakeSfxClip("SFX_E1",       new[]{ 261f, 329f, 392f }, 1.8f, R);
        if (sfxEraShiftEra2       == null) sfxEraShiftEra2       = MakeSfxClip("SFX_E2",       new[]{ 329f, 415f, 523f }, 1.8f, R);
        if (sfxEraShiftEra3       == null) sfxEraShiftEra3       = MakeSfxClip("SFX_E3",       new[]{ 392f, 523f, 659f }, 2.2f, R);
        if (sfxSpeciation         == null) sfxSpeciation         = MakeSfxClip("SFX_Spec",     new[]{ 440f, 523f },       1.2f, R);
        if (sfxMassExtinction     == null) sfxMassExtinction     = MakeSfxClip("SFX_Extinct",  new[]{ 110f, 138f },       2.0f, R);
        if (sfxEndOfEra2Threshold == null) sfxEndOfEra2Threshold = MakeSfxClip("SFX_E2End",    new[]{ 523f, 659f, 784f }, 2.5f, R);
        if (sfxCivFounded         == null) sfxCivFounded         = MakeSfxClip("SFX_CivFnd",   new[]{ 392f, 494f },       1.0f, R);
        if (sfxWarDeclared        == null) sfxWarDeclared        = MakeSfxClip("SFX_War",       new[]{ 220f, 277f },       1.4f, R);
        if (sfxTreatyFormed       == null) sfxTreatyFormed       = MakeSfxClip("SFX_Treaty",    new[]{ 494f, 587f },       1.0f, R);
        if (sfxTradeAgreement     == null) sfxTradeAgreement     = MakeSfxClip("SFX_Trade",     new[]{ 440f, 554f },       1.0f, R);
        if (sfxReligionFounded    == null) sfxReligionFounded    = MakeSfxClip("SFX_Relig",     new[]{ 523f, 659f },       1.2f, R);
        if (sfxSchism             == null) sfxSchism             = MakeSfxClip("SFX_Schism",    new[]{ 277f, 311f },       1.2f, R);
        if (sfxExchangeContact    == null) sfxExchangeContact    = MakeSfxClip("SFX_Contact",   new[]{ 440f, 494f },       0.8f, R);
        if (sfxCrisisWarning      == null) sfxCrisisWarning      = MakeSfxClip("SFX_Crisis",    new[]{ 349f, 392f },       1.5f, R);
        if (sfxCivCollapse        == null) sfxCivCollapse        = MakeSfxClip("SFX_Collapse",  new[]{ 110f, 123f },       2.5f, R);
    }

    // ── Public-domain melody tables ────────────────────────────────────────────
    // (frequency_hz, duration_sec); 0 Hz = rest.

    // Beethoven, "Ode to Joy" (Sym. 9, 4th mvt.) — C-major, BPM 72
    private static readonly (float hz, float dur)[] s_OdeToJoy =
    {
        (329.63f,0.833f),(329.63f,0.833f),(349.23f,0.833f),(392.00f,0.833f),
        (392.00f,0.833f),(349.23f,0.833f),(329.63f,0.833f),(293.66f,0.833f),
        (261.63f,0.833f),(261.63f,0.833f),(293.66f,0.833f),(329.63f,0.833f),
        (329.63f,1.250f),(293.66f,0.417f),(293.66f,1.667f),
        (329.63f,0.833f),(329.63f,0.833f),(349.23f,0.833f),(392.00f,0.833f),
        (392.00f,0.833f),(349.23f,0.833f),(329.63f,0.833f),(293.66f,0.833f),
        (261.63f,0.833f),(261.63f,0.833f),(293.66f,0.833f),(329.63f,0.833f),
        (293.66f,1.250f),(261.63f,0.417f),(261.63f,1.667f),
    };

    // Petzold/Bach, "Minuet in G" (BWV Anh. 114) — BPM 96, 3/4
    private static readonly (float hz, float dur)[] s_MinuetG =
    {
        (392.00f,0.625f),(440.00f,0.625f),(493.88f,0.625f),
        (392.00f,1.875f),
        (523.25f,0.625f),(493.88f,0.625f),(440.00f,0.625f),
        (392.00f,1.875f),
        (587.33f,0.625f),(523.25f,0.625f),(493.88f,0.625f),
        (440.00f,1.875f),
        (493.88f,0.625f),(523.25f,0.625f),(493.88f,0.625f),
        (440.00f,1.875f),
        (493.88f,0.625f),(440.00f,0.625f),(392.00f,0.625f),
        (329.63f,0.625f),(349.23f,0.625f),(392.00f,0.625f),
        (329.63f,1.875f),
    };

    // Beethoven, "Für Elise" (WoO 59) — opening, A minor, BPM 130
    private static readonly (float hz, float dur)[] s_FurElise =
    {
        (659.25f,0.231f),(622.25f,0.231f),(659.25f,0.231f),(622.25f,0.231f),(659.25f,0.231f),
        (493.88f,0.231f),(587.33f,0.231f),(523.25f,0.231f),
        (440.00f,0.692f),(0f,0.231f),
        (261.63f,0.231f),(329.63f,0.231f),(440.00f,0.231f),
        (493.88f,0.692f),(0f,0.231f),
        (261.63f,0.231f),(392.00f,0.231f),(493.88f,0.231f),
        (659.25f,0.231f),(622.25f,0.231f),(659.25f,0.231f),(622.25f,0.231f),(659.25f,0.231f),
        (493.88f,0.231f),(587.33f,0.231f),(523.25f,0.231f),
        (440.00f,0.692f),(0f,0.231f),
        (261.63f,0.231f),(329.63f,0.231f),(440.00f,0.231f),
        (493.88f,0.231f),(392.00f,0.231f),(440.00f,0.231f),
        (329.63f,1.385f),
    };

    // Grieg, "In the Hall of the Mountain King" (Peer Gynt Op. 23) — BPM 120
    private static readonly (float hz, float dur)[] s_MountainKing =
    {
        (246.94f,0.5f),(261.63f,0.5f),(293.66f,0.5f),(329.63f,0.5f),
        (349.23f,0.5f),(329.63f,0.5f),(293.66f,0.5f),(261.63f,0.5f),
        (246.94f,1.0f),(196.00f,0.5f),(246.94f,0.5f),
        (0f,1.0f),
        (246.94f,0.5f),(261.63f,0.5f),(293.66f,0.5f),(329.63f,0.5f),
        (349.23f,0.5f),(329.63f,0.5f),(293.66f,0.5f),(261.63f,0.5f),
        (246.94f,0.5f),(196.00f,0.5f),(246.94f,0.5f),(196.00f,0.5f),
        (174.61f,2.0f),
    };

    // Pachelbel, Canon in D — melody, BPM 60
    private static readonly (float hz, float dur)[] s_PachelbelCanon =
    {
        (369.99f,1.0f),(329.63f,1.0f),(293.66f,1.0f),(277.18f,1.0f),
        (246.94f,1.0f),(220.00f,1.0f),(246.94f,1.0f),(277.18f,1.0f),
        (293.66f,1.0f),(277.18f,1.0f),(246.94f,1.0f),(220.00f,1.0f),
        (246.94f,1.0f),(220.00f,1.0f),(196.00f,1.0f),(185.00f,1.0f),
    };

    // Traditional, "Greensleeves" — A natural minor, BPM 60, 3/4
    private static readonly (float hz, float dur)[] s_Greensleeves =
    {
        (440.00f,1.0f),
        (523.25f,1.5f),(587.33f,0.5f),(659.25f,1.0f),
        (587.33f,1.5f),(523.25f,0.5f),
        (440.00f,2.0f),(415.30f,1.0f),
        (392.00f,1.5f),(440.00f,0.5f),(493.88f,1.0f),
        (415.30f,3.0f),
        (392.00f,1.0f),
        (349.23f,1.5f),(392.00f,0.5f),(415.30f,1.0f),
        (392.00f,1.5f),(349.23f,0.5f),
        (293.66f,2.0f),(293.66f,1.0f),
        (329.63f,1.5f),(293.66f,0.5f),(261.63f,1.0f),
        (293.66f,3.0f),
    };

    // Handel, "Hallelujah" (Messiah HWV 56) — D major, BPM 100
    private static readonly (float hz, float dur)[] s_Hallelujah =
    {
        (587.33f,0.6f),(587.33f,0.3f),(587.33f,0.3f),
        (587.33f,0.6f),(587.33f,0.3f),(587.33f,0.3f),
        (587.33f,0.3f),(659.25f,0.3f),(523.25f,0.3f),(587.33f,0.6f),
        (493.88f,1.8f),(0f,0.6f),
        (659.25f,0.6f),(659.25f,0.3f),(659.25f,0.3f),
        (659.25f,0.6f),(659.25f,0.3f),(659.25f,0.3f),
        (659.25f,0.3f),(783.99f,0.6f),(698.46f,0.3f),(659.25f,0.3f),
        (587.33f,1.8f),(0f,0.6f),
    };

    // Slow descending cryogenic theme (Moonlight Sonata-inspired, Beethoven Op. 27 No. 2)
    private static readonly (float hz, float dur)[] s_Cryogenic =
    {
        (415.30f,1.5f),(369.99f,1.5f),(329.63f,3.0f),
        (369.99f,1.5f),(329.63f,1.5f),(277.18f,3.0f),
        (329.63f,1.5f),(293.66f,1.5f),(246.94f,3.0f),
        (277.18f,1.5f),(246.94f,1.5f),(207.65f,3.0f),
        (220.00f,6.0f),
    };

    // Bach, Invention No. 1 in C (BWV 772) — BPM 100, 16th-note runs
    private static readonly (float hz, float dur)[] s_BachInv1 =
    {
        (261.63f,0.15f),(293.66f,0.15f),(329.63f,0.15f),(261.63f,0.15f),
        (329.63f,0.15f),(261.63f,0.15f),(392.00f,0.15f),(329.63f,0.15f),
        (293.66f,0.15f),(329.63f,0.15f),(349.23f,0.15f),(261.63f,0.15f),
        (349.23f,0.15f),(261.63f,0.15f),(440.00f,0.15f),(349.23f,0.15f),
        (329.63f,0.15f),(349.23f,0.15f),(329.63f,0.15f),(261.63f,0.15f),
        (329.63f,0.15f),(261.63f,0.15f),(493.88f,0.15f),(329.63f,0.15f),
        (293.66f,0.15f),(329.63f,0.15f),(349.23f,0.15f),(261.63f,0.15f),
        (349.23f,0.15f),(293.66f,0.15f),(261.63f,0.15f),(246.94f,0.15f),
        (261.63f,0.60f),
    };

    /// Melody-based music clip: note sequence over a sustained drone pad.
    private static AudioClip MakeMelodyClip(string name, (float hz, float dur)[] melody,
        float[] droneFreqs, int sampleRate, float melodyVol = 0.45f, float droneVol = 0.22f)
    {
        float totalDur = 0f;
        foreach (var n in melody) totalDur += n.dur;
        totalDur = Mathf.Max(totalDur + 0.5f, 8f);
        int N = Mathf.RoundToInt(totalDur * sampleRate);
        float[] data = new float[N];

        // Melody voice: additive sine with ADSR envelope
        int cursor = 0;
        foreach (var (hz, dur) in melody)
        {
            int ns = Mathf.RoundToInt(dur * sampleRate);
            float atk = Mathf.Min(0.06f, dur * 0.10f);
            float rel = Mathf.Min(0.20f, dur * 0.25f);
            const float sus = 0.72f;
            for (int i = 0; i < ns && cursor + i < N; i++)
            {
                float t    = (float)i / sampleRate;
                float tRem = dur - t;
                float env  = t < atk    ? t / atk
                           : tRem < rel ? sus * tRem / rel
                           : sus;
                float s = 0f;
                if (hz > 0f)
                {
                    s  = Mathf.Sin(2f * Mathf.PI * hz * t);
                    s += 0.32f * Mathf.Sin(2f * Mathf.PI * hz * 2f * t);
                    s += 0.10f * Mathf.Sin(2f * Mathf.PI * hz * 3f * t);
                }
                data[cursor + i] += s * melodyVol * env;
            }
            cursor += ns;
        }

        // Drone pad
        if (droneFreqs != null && droneFreqs.Length > 0)
        {
            float da = droneVol / droneFreqs.Length;
            for (int i = 0; i < N; i++)
            {
                float t    = (float)i / sampleRate;
                float edge = Mathf.Min(1f, t / 1.5f, (totalDur - t) / 1.5f);
                float am   = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * 0.12f * t);
                float s    = 0f;
                foreach (float f in droneFreqs)
                    s += Mathf.Sin(2f * Mathf.PI * f * t);
                data[i] += s * da * am * edge;
            }
        }

        var clip = AudioClip.Create(name, N, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// Pure drone clip — used for exotic overlay and similar sustained-tone slots.
    private static AudioClip MakeDroneClip(string name, float[] freqs, float duration,
                                            int sampleRate, float volume = 0.55f)
    {
        int n = Mathf.RoundToInt(duration * sampleRate);
        float[] data = new float[n];
        float ampPerFreq = volume / freqs.Length;
        for (int i = 0; i < n; i++)
        {
            float t    = (float)i / sampleRate;
            float am   = 0.82f + 0.18f * Mathf.Sin(2f * Mathf.PI * 0.18f * t);
            float edge = Mathf.Min(1f, t / 1.5f, (duration - t) / 1.5f);
            float s    = 0f;
            foreach (float f in freqs)
            {
                s += Mathf.Sin(2f * Mathf.PI * f * t);
                s += 0.28f * Mathf.Sin(2f * Mathf.PI * f * 2f * t);
                s += 0.12f * Mathf.Sin(2f * Mathf.PI * f * 3f * t);
            }
            data[i] = s * ampPerFreq * am * edge;
        }
        var clip = AudioClip.Create(name, n, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// Short chord stab with attack + exponential decay, for SFX slots.
    private static AudioClip MakeSfxClip(string name, float[] freqs, float duration, int sampleRate)
    {
        int n = Mathf.RoundToInt(duration * sampleRate);
        float[] data = new float[n];
        float attackTime = Mathf.Min(0.04f, duration * 0.08f);
        float ampPerFreq = 0.70f / freqs.Length;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / sampleRate;
            // Attack-decay envelope
            float env = t < attackTime
                ? t / attackTime
                : Mathf.Exp(-2.8f * (t - attackTime) / duration);
            float s = 0f;
            foreach (float f in freqs)
            {
                // Slight upward chirp adds transient brightness
                float fi = f * (1f + 0.06f * Mathf.Max(0f, 1f - t / 0.12f));
                s += Mathf.Sin(2f * Mathf.PI * fi * t);
                s += 0.22f * Mathf.Sin(2f * Mathf.PI * fi * 2f * t);
            }
            data[i] = s * ampPerFreq * env;
        }

        var clip = AudioClip.Create(name, n, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
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
