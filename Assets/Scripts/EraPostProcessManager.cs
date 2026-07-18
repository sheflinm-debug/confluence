using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Drives per-era post-processing mood arc via URP Volume cross-fades.
///
/// Four fixed VolumeProfiles are built at Start() (Intro, Era1, Era2, Era3).
/// Two Volume components sit on this GameObject at priority 10/11 — the base
/// profile and a blend target. CrossfadeTo() alpha-sweeps the blend target's
/// weight from 0→1 then swaps it into the base slot, keeping only one running
/// coroutine at a time.
///
/// Era-shift hooks (OnIntroBegin, OnEra1Begin, OnEra2Begin, OnEra3Begin) are
/// called by EraManager / Era2Manager / Era3Manager at the relevant transition.
/// SimulationBootstrap adds this component before live gameplay begins.
public class EraPostProcessManager : MonoBehaviour
{
    public static EraPostProcessManager Instance { get; private set; }

    // Cross-fade duration in seconds for era shifts (shorter for intro).
    private const float EraFadeDuration   = 3.0f;
    private const float IntroFadeDuration = 1.5f;

    private Volume _base;
    private Volume _blend;

    private VolumeProfile _introProfile;
    private VolumeProfile _era1Profile;
    private VolumeProfile _era2Profile;
    private VolumeProfile _era3Profile;

    private Coroutine _activeFade;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Initialize immediately in Awake so public hooks are safe to call
        // in the same frame that AddComponent runs (SimulationBootstrap pattern).
        BuildProfiles();
        SetupVolumes();
        _base.profile = _introProfile;
        _base.weight  = 1f;
        _blend.weight = 0f;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public era hooks ────────────────────────────────────────────────────────

    public void OnIntroBegin()  => CrossfadeTo(_introProfile, IntroFadeDuration);
    public void OnEra1Begin()   => CrossfadeTo(_era1Profile,  EraFadeDuration);
    public void OnEra2Begin()   => CrossfadeTo(_era2Profile,  EraFadeDuration);
    public void OnEra3Begin()   => CrossfadeTo(_era3Profile,  EraFadeDuration);

    // ── Volume setup ────────────────────────────────────────────────────────────

    private void SetupVolumes()
    {
        _base = gameObject.AddComponent<Volume>();
        _base.isGlobal = true;
        _base.priority = 10f;
        _base.profile  = ScriptableObject.CreateInstance<VolumeProfile>();

        _blend = gameObject.AddComponent<Volume>();
        _blend.isGlobal = true;
        _blend.priority = 11f;
        _blend.weight   = 0f;
        _blend.profile  = ScriptableObject.CreateInstance<VolumeProfile>();
    }

    // ── Profile construction ────────────────────────────────────────────────────

    private void BuildProfiles()
    {
        _introProfile = BuildIntroProfile();
        _era1Profile  = BuildEra1Profile();
        _era2Profile  = BuildEra2Profile();
        _era3Profile  = BuildEra3Profile();
    }

    // Intro: high-contrast, mostly desaturated, selective bright highlights.
    private static VolumeProfile BuildIntroProfile()
    {
        var p = ScriptableObject.CreateInstance<VolumeProfile>();

        var ca = p.Add<ColorAdjustments>(true);
        ca.postExposure.Override(0.2f);
        ca.contrast.Override(30f);
        ca.saturation.Override(-60f);

        var vig = p.Add<Vignette>(true);
        vig.intensity.Override(0.50f);
        vig.smoothness.Override(0.6f);

        var chrom = p.Add<ChromaticAberration>(true);
        chrom.intensity.Override(0.15f);

        return p;
    }

    // Era 1: primordial murk — desaturated, heavy vignette, cool tint.
    private static VolumeProfile BuildEra1Profile()
    {
        var p = ScriptableObject.CreateInstance<VolumeProfile>();

        var ca = p.Add<ColorAdjustments>(true);
        ca.postExposure.Override(-0.2f);
        ca.saturation.Override(-30f);
        ca.colorFilter.Override(new Color(0.75f, 0.88f, 1.00f)); // cool blue tint

        var vig = p.Add<Vignette>(true);
        vig.intensity.Override(0.40f);
        vig.smoothness.Override(0.5f);

        var chrom = p.Add<ChromaticAberration>(true);
        chrom.intensity.Override(0.10f);

        // No bloom — primordial world has no bioluminescent or canopy highlights.

        return p;
    }

    // Era 2: ecological richness — saturation returning, bloom introduced.
    private static VolumeProfile BuildEra2Profile()
    {
        var p = ScriptableObject.CreateInstance<VolumeProfile>();

        var ca = p.Add<ColorAdjustments>(true);
        ca.postExposure.Override(0.0f);
        ca.saturation.Override(0f);   // neutral — diversity of pigments reads naturally

        var vig = p.Add<Vignette>(true);
        vig.intensity.Override(0.20f);
        vig.smoothness.Override(0.4f);

        var bloom = p.Add<Bloom>(true);
        bloom.threshold.Override(0.9f);
        bloom.intensity.Override(0.5f);
        bloom.scatter.Override(0.7f);

        return p;
    }

    // Era 3: civilizational clarity — high exposure/saturation, minimal vignette, bloom on structures.
    private static VolumeProfile BuildEra3Profile()
    {
        var p = ScriptableObject.CreateInstance<VolumeProfile>();

        var ca = p.Add<ColorAdjustments>(true);
        ca.postExposure.Override(0.3f);
        ca.contrast.Override(10f);
        ca.saturation.Override(20f);

        var vig = p.Add<Vignette>(true);
        vig.intensity.Override(0.10f);
        vig.smoothness.Override(0.3f);

        var bloom = p.Add<Bloom>(true);
        bloom.threshold.Override(0.7f);
        bloom.intensity.Override(1.0f);
        bloom.scatter.Override(0.75f);

        return p;
    }

    // ── Cross-fade ──────────────────────────────────────────────────────────────

    private void CrossfadeTo(VolumeProfile target, float duration)
    {
        if (_activeFade != null) StopCoroutine(_activeFade);
        _blend.profile = target;
        _activeFade = StartCoroutine(FadeCoroutine(duration));
    }

    private IEnumerator FadeCoroutine(float duration)
    {
        float t = 0f;
        _blend.weight = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            _blend.weight = Mathf.Clamp01(t / duration);
            yield return null;
        }
        _blend.weight  = 0f;
        _base.profile  = _blend.profile;
        _activeFade    = null;
    }
}
