using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// Generic, data-driven gene-event engine (Section 6b). Replaces the old single
/// hardcoded vision/speed event - genes are registered as data (GeneCatalog), and this
/// class just evaluates eligibility (prerequisites + trigger condition) against whatever
/// genes are registered, queues a choice popup if the gene has one, or applies it
/// automatically if not. Adding a new gene never requires touching this file.
public class GeneEvolutionManager : MonoBehaviour
{
    private static readonly List<GeneDefinition> _catalog = new List<GeneDefinition>();
    private static readonly Queue<PendingChoice> _pending = new Queue<PendingChoice>();
    // Atmosphere events (Great Gas Event etc.) queue as narrative-only popups with no agent target.
    private static readonly Queue<string> _atmosphereEvents = new Queue<string>();
    // Genes that have already been presented to the player; subsequent eligible agents
    // get the gene auto-applied using the gene's DefaultAutoApply (or choice[last] if unset).
    private static readonly HashSet<string> _presentedGlobally = new HashSet<string>();

    // Community 0 is the "player's community" - only its agents get the choice popup.
    public static int PlayerCommunityId = 0;

    private struct PendingChoice
    {
        public AgentController Agent;
        public GeneDefinition Gene;
    }

    public static void ResetCatalog()
    {
        _catalog.Clear();
        _pending.Clear();
        _presentedGlobally.Clear();
        _atmosphereEvents.Clear();
    }

    public static void Register(GeneDefinition gene) => _catalog.Add(gene);

    /// Called by AtmosphereManager when a global threshold is crossed.
    public static void QueueAtmosphereEvent(string eventName) => _atmosphereEvents.Enqueue(eventName);

    /// Call once per agent per tick (cheap - catalog is small). Checks every registered
    /// gene this agent hasn't already acquired; fires the first newly-eligible one whose
    /// prerequisites are met. Marks the gene acquired immediately (even if a choice is
    /// pending) so it can't be queued twice.
    public static void CheckEligibleGenes(AgentController agent)
    {
        foreach (var gene in _catalog)
        {
            if (agent.AcquiredGenes.Contains(gene.Id)) continue;
            if (!PrerequisitesMet(agent, gene)) continue;
            if (gene.IsEligible != null && !gene.IsEligible(agent)) continue;

            agent.AcquiredGenes.Add(gene.Id);

            if (gene.Choices == null || gene.Choices.Length == 0)
            {
                gene.AutoApply?.Invoke(agent);
            }
            else if (agent.communityId == PlayerCommunityId && !_presentedGlobally.Contains(gene.Id))
            {
                // First player-community agent to trigger this gene gets the popup.
                _presentedGlobally.Add(gene.Id);
                _pending.Enqueue(new PendingChoice { Agent = agent, Gene = gene });
            }
            else
            {
                // Background agents (or non-player communities) silently get the default.
                if (gene.DefaultAutoApply != null)
                    gene.DefaultAutoApply(agent);
                else if (gene.Choices != null && gene.Choices.Length > 0)
                    gene.Choices[gene.Choices.Length - 1].Apply(agent);
            }
        }
    }

    private static bool PrerequisitesMet(AgentController agent, GeneDefinition gene)
    {
        if (gene.Prerequisites == null) return true;
        foreach (var prereq in gene.Prerequisites)
        {
            if (!agent.AcquiredGenes.Contains(prereq)) return false;
        }
        return true;
    }

    void OnGUI()
    {
        // Atmosphere events show as a timed notification (auto-dismiss after a keypress).
        if (_atmosphereEvents.Count > 0 && _pending.Count == 0)
        {
            string evtName = _atmosphereEvents.Peek();
            string msg = evtName switch
            {
                "GreatGasEvent"     => "GREAT GAS EVENT\nThe breathed gas has collapsed!\nConsumers are dying en masse. Survivors may develop Efficient Respiration.",
                "ExpelledGlutEvent" => "RESPIRATORY WASTE GLUT\nExhaled gas has saturated the atmosphere!\nThe breathed gas is being crowded out.",
                "ToxicityEvent"     => "TOXICITY CRISIS\nA poisonous gas has reached lethal concentration!\nAll organisms are under threat.",
                _                   => evtName
            };

            float w = 420f; float h = 100f;
            Rect r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
            GUI.Box(r, "Atmosphere Crisis");
            GUI.Label(new Rect(r.x + 10, r.y + 25, w - 20, 60), msg);
            if (GUI.Button(new Rect(r.x + w / 2f - 50f, r.y + h - 30f, 100f, 24f), "OK"))
                _atmosphereEvents.Dequeue();
            return;
        }

        if (_pending.Count == 0) return;

        PendingChoice pending = _pending.Peek();
        if (pending.Agent == null)
        {
            _pending.Dequeue();
            ResumeIfEmpty();
            return;
        }

        Time.timeScale = 0f; // pause the sim while the player decides

        float boxWidth = 380f;
        float boxHeight = 70f + pending.Gene.Choices.Length * 35f;
        Rect boxRect = new Rect((Screen.width - boxWidth) / 2f, (Screen.height - boxHeight) / 2f, boxWidth, boxHeight);

        GUI.Box(boxRect, $"Gene Event: {pending.Gene.Id}");
        GUI.Label(new Rect(boxRect.x + 10, boxRect.y + 28, boxWidth - 20, 24), "Choose a direction:");

        var keyboard = Keyboard.current;

        for (int i = 0; i < pending.Gene.Choices.Length; i++)
        {
            GeneChoice choice = pending.Gene.Choices[i];
            bool clicked = GUI.Button(new Rect(boxRect.x + 20, boxRect.y + 55 + i * 35, boxWidth - 40, 30), $"{i + 1}: {choice.Label}");

            bool keyPressed = false;
            if (keyboard != null && i < 9)
            {
                var key = keyboard[(Key)((int)Key.Digit1 + i)];
                if (key != null && key.wasPressedThisFrame) keyPressed = true;
            }

            if (clicked || keyPressed)
            {
                choice.Apply(pending.Agent);
                _pending.Dequeue();
                ResumeIfEmpty();
                break;
            }
        }
    }

    private void ResumeIfEmpty()
    {
        if (_pending.Count == 0) Time.timeScale = 1f;
    }
}
