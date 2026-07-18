using UnityEngine;

/// TEMPORARY debug HUD: two buttons that fast-forward to Era 2 or Era 3 so those eras can be tested
/// without playing through all of Era 1 in real time. "Skip to Era 2" advances the biological
/// sub-phase to mature, force-applies every eligible Era 1 gene to all agents with RANDOM choices
/// (no popups), then begins Era 2. "Skip to Era 3" does that and then begins Era 3. Remove before
/// shipping — this bypasses the normal era gates on purpose.
public class DebugEraSkipHUD : MonoBehaviour
{
    private AgentSpawner _spawner;
    private GUIStyle _btn;

    public void Init(AgentSpawner spawner) => _spawner = spawner;

    // Approximate population a species would actually reach after a full, successful Era 1/2 —
    // the skip otherwise leaves whatever small count happened to be alive the moment it was pressed.
    private const int TargetPopulationOnSkip = 300;

    private void SkipToEra2()
    {
        if (Era2Manager.Instance == null || Era2Manager.Instance.IsActive) return;

        // Mature the biological clock (sub-phase + deep-time index) so every era-gated Era 1 gene is
        // eligible, then apply the full Era 1 gene chain (random paths) to EVERY agent, then begin Era 2.
        EraManager.Instance?.DebugForceFinalSubPhase();
        if (DeepTimeClock.Instance != null) DeepTimeClock.Instance.StartFrom(EraTimeline.Phases.Length - 1);
        if (_spawner != null)
        {
            foreach (var a in _spawner.ActiveAgents)
                if (a != null) GeneEvolutionManager.DebugForceEra1GenesRandom(a);

            // The gene sweep above can flip an agent's habitat (LandColonization/ReturnToSea), but that
            // flag alone doesn't move anyone — a "land" species would otherwise stay sitting wherever it
            // was, still visually in the sea. Relocate everyone to a point matching their NEW medium.
            foreach (var a in _spawner.ActiveAgents)
                if (a != null) a.DebugRelocateToMatchingMedium();

            // Extrapolate population growth: a real Era 1/2 run reaches the hundreds, not the 30-70
            // agents typically alive at the moment this button is pressed. Clone up to a realistic total.
            _spawner.DebugBulkUpPopulation(TargetPopulationOnSkip);
        }
        Era2Manager.Instance.BeginEra2();
        Debug.Log("[DebugSkip] Skipped to Era 2 (full Era 1 gene paths, habitat relocation, population growth applied).");
    }

    private void SkipToEra3()
    {
        if (Era3Manager.Instance == null || Era3Manager.Instance.IsActive) return;
        // Fully evolve Era 1, then run all of Era 2's intelligence/civilization development, then Era 3 —
        // so species arrive in Era 3 as developed civilizations, not blank slates.
        if (Era2Manager.Instance != null && !Era2Manager.Instance.IsActive) SkipToEra2();
        Era2Manager.Instance?.DebugForceComplete();
        Era3Manager.Instance.BeginEra3();
        // The first settlement doesn't spawn until 25s of real Era 3 elapsed time have passed
        // (e3_agriculture at 15s -> e3_permanent_settlement at 25s) — fast-forward past that so
        // testing doesn't require sitting and waiting. Era3Manager.Update() fires the whole eligible
        // auto-event chain (agriculture, settlement, trade, stratification, etc.) on the very next
        // frame once _elapsed is past their MinTimes, since each event's prereqs are satisfied within
        // the same pass once its prerequisite has already fired.
        Era3Manager.Instance.DebugFastForwardElapsed(100f);
        Debug.Log("[DebugSkip] Skipped to Era 3 (Era 1 + Era 2 fully developed, Era 3 clock fast-forwarded).");
    }

    void OnGUI()
    {
        if (_btn == null)
            _btn = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold };

        bool inEra2 = Era2Manager.Instance != null && Era2Manager.Instance.IsActive;
        bool inEra3 = Era3Manager.Instance != null && Era3Manager.Instance.IsActive;

        float x = 12f, y = Screen.height - 76f, w = 150f, h = 28f;

        GUI.enabled = !inEra2 && !inEra3;
        if (GUI.Button(new Rect(x, y, w, h), ">> Skip to Era 2", _btn)) SkipToEra2();

        GUI.enabled = !inEra3;
        if (GUI.Button(new Rect(x, y + h + 6f, w, h), ">> Skip to Era 3", _btn)) SkipToEra3();

        GUI.enabled = true;
    }
}
