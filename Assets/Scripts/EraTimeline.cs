/// Single source of truth for game-eras-spec.md's phase durations and the in-fiction
/// years-ago span each phase covers. GenesisCinematic uses the durations to time its
/// coroutine; DeepTimeClock uses the same table (auto-advancing independently, so the
/// two never drift) to drive the persistent "years ago" caption through the
/// cinematic AND on into Era 1's live-simulation stasis stretches.
public struct EraPhase
{
    public string EraLabel;
    public string PhaseLabel;
    public float DurationSeconds;
    public float YearsAgoStart;
    public float YearsAgoEnd;

    public EraPhase(string era, string phase, float duration, float yearsAgoStart, float yearsAgoEnd)
    {
        EraLabel = era; PhaseLabel = phase; DurationSeconds = duration;
        YearsAgoStart = yearsAgoStart; YearsAgoEnd = yearsAgoEnd;
    }
}

public static class EraTimeline
{
    private const string Intro   = "Intro";
    private const string Abiotic = "Abiotic/Tectonic";
    private const string Era1    = "Era 1: Origin & Early Life";
    private const string Era2    = "Era 2: Complex Life & Cognition";

    public static readonly EraPhase[] Phases =
    {
        // Intro Animation - 18s total (game-eras-spec.md lines 21-31)
        new EraPhase(Intro, "Big Bang & early universe", 5f, 13_800_000_000f, 13_790_000_000f),
        new EraPhase(Intro, "Star formation", 5f, 13_790_000_000f, 4_600_000_000f),
        new EraPhase(Intro, "Protoplanetary disk -> planet accretion", 5f, 4_600_000_000f, 4_560_000_000f),
        new EraPhase(Intro, "System settles", 3f, 4_560_000_000f, 4_540_000_000f),

        // Abiotic/Tectonic Era - 20s total (lines 34-44)
        new EraPhase(Abiotic, "Molten surface & bombardment", 6f, 4_540_000_000f, 4_400_000_000f),
        new EraPhase(Abiotic, "Crust formation & cooling", 5f, 4_400_000_000f, 4_200_000_000f),
        new EraPhase(Abiotic, "Condensation & solvent arrival", 5f, 4_200_000_000f, 4_000_000_000f),
        new EraPhase(Abiotic, "Topography reveal", 4f, 4_000_000_000f, 4_000_000_000f),

        // Era 1: Origin & Early Life - 240s (4 min) baseline (lines 47-60).
        // Sub-phases run as a caption/clock overlay on top of the live colony simulation.
        new EraPhase(Era1, "Abiogenesis", 45f, 4_000_000_000f, 3_900_000_000f),
        new EraPhase(Era1, "Minimal-Replicator Seas / long stasis", 90f, 3_900_000_000f, 2_400_000_000f),
        new EraPhase(Era1, "Great Gas Event", 30f, 2_400_000_000f, 2_000_000_000f),
        new EraPhase(Era1, "Compartmentalized Cells", 30f, 2_000_000_000f, 1_200_000_000f),
        new EraPhase(Era1, "Multicellularity", 30f, 1_200_000_000f, 600_000_000f),
        new EraPhase(Era1, "Morphological Complexity Threshold", 45f, 600_000_000f, 540_000_000f),

        // Era 2: Complex Life & Cognition — caption-only sub-phase labels.
        // Durations are pacing targets from game-eras-spec.md; triggering is handled
        // by Era2Manager's biology gate, not by the clock reaching this index.
        new EraPhase(Era2, "Substrate Transition", 120f, 540_000_000f, 360_000_000f),
        new EraPhase(Era2, "Apex Radiation Interval", 180f, 360_000_000f, 66_000_000f),
        new EraPhase(Era2, "Post-Extinction Adaptive Radiation", 150f, 66_000_000f, 7_000_000f),
        new EraPhase(Era2, "Encephalization Ramp", 180f, 7_000_000f, 50_000f),
        new EraPhase(Era2, "Symbolic Cognition Threshold", 105f, 50_000f, 12_000f),
    };

    public const int IntroStartIndex  = 0;
    public const int AbioticStartIndex = 4;
    public const int Era1StartIndex    = 8;
    public const int Era2StartIndex    = 14;
}
