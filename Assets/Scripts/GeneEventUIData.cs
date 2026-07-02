using System.Collections.Generic;

/// Static lookup table: gene ID → (topic icon, short title, dilemma line, learn-more text)
/// and per-choice (icon name, short label). Matches event-prompt-icon-redesign-spec.md.
public static class GeneEventUIData
{
    public struct ChoiceUI
    {
        public string Icon;  // filename in Resources/Icons without extension
        public string Label; // 1-2 words shown on button
    }

    public struct EventUI
    {
        public string TopicIcon;   // icon name for the event header
        public string Title;       // ≤4 words
        public string Dilemma;     // ≤8 words
        public string LearnMore;   // 1-2 sentences for inline expand
        public ChoiceUI[] Choices; // parallel to GeneDefinition.Choices (available choices)
    }

    private static readonly Dictionary<string, EventUI> _map = new Dictionary<string, EventUI>
    {
        // ── Era 1 ────────────────────────────────────────────────────────────────

        ["SensoryOrganDevelopment"] = new EventUI
        {
            TopicIcon = "eye",
            Title     = "Sensory Evolution",
            Dilemma   = "Precision or breadth of vision?",
            LearnMore = "Acute vision narrows the field but spots prey far away. Wide-field gives panoramic awareness at lower resolution.",
            Choices   = new[] {
                new ChoiceUI { Icon = "focus-2",     Label = "Acute"      },
                new ChoiceUI { Icon = "eye-search",  Label = "Wide-field" },
            }
        },

        ["LocomotorAppendageDevelopment"] = new EventUI
        {
            TopicIcon = "walk",
            Title     = "Locomotor Specialization",
            Dilemma   = "Speed or raw muscular force?",
            LearnMore = "Fast limbs improve pursuit and escape. Powerful limbs confer combat and structural strength bonuses.",
            Choices   = new[] {
                new ChoiceUI { Icon = "bolt",   Label = "Fast"     },
                new ChoiceUI { Icon = "weight", Label = "Powerful" },
            }
        },

        ["ReproductiveStrategyShift"] = new EventUI
        {
            TopicIcon = "dna-2",
            Title     = "Reproductive Strategy",
            Dilemma   = "Clone alone or share genes?",
            LearnMore = "Asexual reproduction is fast but accumulates mutations. Sexual reproduction blends traits across mates, creating more variety.",
            Choices   = new[] {
                new ChoiceUI { Icon = "circle-dot",      Label = "Asexual" },
                new ChoiceUI { Icon = "heart-handshake", Label = "Sexual"  },
            }
        },

        ["SequentialHermaphroditism"] = new EventUI
        {
            TopicIcon = "refresh",
            Title     = "Sex Flexibility",
            Dilemma   = "Switch sex to match population?",
            LearnMore = "Sequential hermaphroditism lets organisms change sex when the local ratio skews heavily, preventing mating bottlenecks.",
            Choices   = new[] {
                new ChoiceUI { Icon = "arrows-exchange", Label = "Hermaphroditic" },
                new ChoiceUI { Icon = "lock",            Label = "Fixed sex"      },
            }
        },

        ["EfficientRespiration"] = new EventUI
        {
            TopicIcon = "wind",
            Title     = "Oxidation Crisis",
            Dilemma   = "Adapt or hide from the gas?",
            LearnMore = "Adapting yields long-term resilience and wider habitat. Retreating to anoxic refuges is safer now but permanently restricts range and speed.",
            Choices   = new[] {
                new ChoiceUI { Icon = "shield-check", Label = "Tolerate"  },
                new ChoiceUI { Icon = "home-shield",  Label = "Retreat"   },
            }
        },

        ["PhotosynthesisEmergence"] = new EventUI
        {
            TopicIcon = "sun",
            Title     = "Photosynthesis",
            Dilemma   = "Harvest sunlight or stay chemical?",
            LearnMore = "Photosynthesis unlocks unlimited surface energy but releases gas that will reshape the atmosphere. Chemosynthesis is stable but spatially constrained.",
            Choices   = new[] {
                new ChoiceUI { Icon = "leaf",       Label = "Photosynthesize" },
                new ChoiceUI { Icon = "flask",      Label = "Stay chemical"   },
            }
        },

        ["MotilityEmergence"] = new EventUI
        {
            TopicIcon = "route",
            Title     = "Motility",
            Dilemma   = "Self-direct or drift with currents?",
            LearnMore = "Motility enables active foraging and predation. Sessile organisms spend less energy on movement and can dominate stable nutrient patches.",
            Choices   = new[] {
                new ChoiceUI { Icon = "fish",   Label = "Motile"  },
                new ChoiceUI { Icon = "anchor", Label = "Sessile" },
            }
        },

        ["ManipulationAppendageDevelopment"] = new EventUI
        {
            TopicIcon = "walk",
            Title     = "Appendage Development",
            Dilemma   = "Grow simple manipulators?",
            LearnMore = "Simple appendages (cilia, pseudopods) add a modest manipulation tier and advance neural complexity toward nerve-net organization.",
            Choices   = new[] {
                new ChoiceUI { Icon = "hand-grab", Label = "Simple"     },
                new ChoiceUI { Icon = "x",         Label = "None"       },
            }
        },

        ["ArticulatedAppendages"] = new EventUI
        {
            TopicIcon = "walk",
            Title     = "Articulated Limbs",
            Dilemma   = "Upgrade to grasping appendages?",
            LearnMore = "Articulated limbs allow crude tool use and open the path toward dexterous manipulation in later eras.",
            Choices   = new[] {
                new ChoiceUI { Icon = "hand-grab", Label = "Articulated" },
                new ChoiceUI { Icon = "anchor",    Label = "Retain"      },
            }
        },

        ["DexterousAppendages"] = new EventUI
        {
            TopicIcon = "walk",
            Title     = "Dexterous Digits",
            Dilemma   = "Evolve precision grasping?",
            LearnMore = "Dexterous digits enable precise manipulation and are the prerequisite for Era 2 tool use.",
            Choices   = new[] {
                new ChoiceUI { Icon = "hand-grab", Label = "Dexterous" },
                new ChoiceUI { Icon = "anchor",    Label = "Retain"    },
            }
        },

        ["SocialityEmergence"] = new EventUI
        {
            TopicIcon = "users-plus",
            Title     = "Social Behavior",
            Dilemma   = "Cluster or stay solitary?",
            LearnMore = "Aggregating behavior reduces individual predation risk via density-dependent defense. Solitary organisms specialize more efficiently in sparse niches.",
            Choices   = new[] {
                new ChoiceUI { Icon = "users", Label = "Aggregate" },
                new ChoiceUI { Icon = "user",  Label = "Solitary"  },
            }
        },

        ["GroupFormation"] = new EventUI
        {
            TopicIcon = "users-group",
            Title     = "Group Formation",
            Dilemma   = "Coordinate or stay loosely clustered?",
            LearnMore = "Active group formation enables role division and coordinated foraging, key inputs for the Era 2 social architecture fork.",
            Choices   = new[] {
                new ChoiceUI { Icon = "users-group", Label = "Group"    },
                new ChoiceUI { Icon = "users",       Label = "Aggregate"},
            }
        },

        ["Mixotrophy"] = new EventUI
        {
            TopicIcon = "scale",
            Title     = "Dual Metabolism",
            Dilemma   = "Combine photosynthesis and feeding?",
            LearnMore = "Mixotrophy provides a metabolic fallback — either energy source sustains the organism when the other is scarce, at 70% efficiency each.",
            Choices   = new[] {
                new ChoiceUI { Icon = "scale", Label = "Mixotrophic"  },
                new ChoiceUI { Icon = "leaf",  Label = "Phototrophic" },
            }
        },

        ["GermLayerComplexity"] = new EventUI
        {
            TopicIcon = "layers-intersect",
            Title     = "Tissue Complexity",
            Dilemma   = "Develop three tissue layers?",
            LearnMore = "Triploblastic organization (three germ layers) is the prerequisite for evolving an endoskeleton — the vertebrate ancestor path.",
            Choices   = new[] {
                new ChoiceUI { Icon = "layers-linked", Label = "Three layers" },
                new ChoiceUI { Icon = "square",        Label = "Two layers"   },
            }
        },

        ["ProtectiveStructureEmergence"] = new EventUI
        {
            TopicIcon = "shield",
            Title     = "Body Plan",
            Dilemma   = "How to answer the predation threat?",
            LearnMore = "Exoskeleton and shell provide defense at a speed cost. Endoskeleton (requires three tissue layers) unlocks the vertebrate body plan. Soft-body invests in pure evasion.",
            Choices   = new[] {
                new ChoiceUI { Icon = "shield-checkered", Label = "Exoskeleton" },
                new ChoiceUI { Icon = "egg",              Label = "Shell"       },
                new ChoiceUI { Icon = "bone",             Label = "Endoskeleton"},
                new ChoiceUI { Icon = "run",              Label = "Soft, fast"  },
            }
        },

        ["KingdomFork"] = new EventUI
        {
            TopicIcon = "git-fork",
            Title     = "Kingdom Fork",
            Dilemma   = "Produce energy or consume others?",
            LearnMore = "Becoming a Producer locks in photosynthetic autotrophy. Becoming a Consumer opens predation and scavenging — a higher-risk, higher-reward energy strategy.",
            Choices   = new[] {
                new ChoiceUI { Icon = "leaf", Label = "Producer" },
                new ChoiceUI { Icon = "paw",  Label = "Consumer" },
            }
        },

        // ── Era 2 Player Decision Layer ───────────────────────────────────────────

        ["CognitiveInvestmentStrategy"] = new EventUI
        {
            TopicIcon = "brain",
            Title     = "Cognitive Investment",
            Dilemma   = "Social, manipulative, or scale?",
            LearnMore = "A1 Social breadth favors group coordination. A2 Manipulative specialization favors tool mastery. A3 Scale favors raw neuron count and cultural ceiling.",
            Choices   = new[] {
                new ChoiceUI { Icon = "users",           Label = "Social"      },
                new ChoiceUI { Icon = "hand-grab",       Label = "Manipulative"},
                new ChoiceUI { Icon = "arrows-maximize", Label = "Scale"       },
            }
        },

        ["CommunicationMedium"] = new EventUI
        {
            TopicIcon = "message-circle",
            Title     = "Communication Medium",
            Dilemma   = "How does this lineage signal?",
            LearnMore = "Medium choice gates later codification options. Vocal/Auditory is universal; Visual/Gestural requires vision; Chemical and Bioelectric are specialist paths.",
            Choices   = new[] {
                new ChoiceUI { Icon = "microphone",  Label = "Vocal"     },
                new ChoiceUI { Icon = "eye",         Label = "Visual"    },
                new ChoiceUI { Icon = "droplet",     Label = "Chemical"  },
                new ChoiceUI { Icon = "bolt",        Label = "Bioelectric"},
            }
        },

        ["NicheConstructionOrientation"] = new EventUI
        {
            TopicIcon = "tools",
            Title     = "Niche Construction",
            Dilemma   = "Tools, environment, or teaching?",
            LearnMore = "Tool-Based unlocks cordage and weapon technologies. Environment-Modification favors large-scale habitat reshaping. Social Transmission builds pure cultural networks.",
            Choices   = new[] {
                new ChoiceUI { Icon = "hammer", Label = "Tool-based"  },
                new ChoiceUI { Icon = "home-2", Label = "Environment" },
                new ChoiceUI { Icon = "school", Label = "Social-only" },
            }
        },

        ["MetabolicAllocation"] = new EventUI
        {
            TopicIcon = "scale",
            Title     = "Metabolic Allocation",
            Dilemma   = "Brain or body investment?",
            LearnMore = "Brain-heavy allocation accelerates Intelligence Index growth. Somatic investment preserves physical robustness. Balanced is the neutral path.",
            Choices   = new[] {
                new ChoiceUI { Icon = "brain", Label = "Brain"    },
                new ChoiceUI { Icon = "scale", Label = "Balanced" },
                new ChoiceUI { Icon = "run",   Label = "Body"     },
            }
        },

        ["SocialStructure"] = new EventUI
        {
            TopicIcon = "users-group",
            Title     = "Social Structure",
            Dilemma   = "How does this lineage bond?",
            LearnMore = "Multi-member troops maximize group-brain Sociality bonuses. Fission-fusion creates variance. Pair-bonded leads to structured Era 3 governance. Solitary-territorial minimizes social overhead.",
            Choices   = new[] {
                new ChoiceUI { Icon = "heart-handshake", Label = "Pair-bonded"   },
                new ChoiceUI { Icon = "users",           Label = "Troop"         },
                new ChoiceUI { Icon = "arrows-split",    Label = "Fission-fusion"},
                new ChoiceUI { Icon = "map-pin",         Label = "Territorial"   },
            }
        },

        // ── Era 2 §8 Threshold events ──────────────────────────────────────────

        ["FireHeatMastery"] = new EventUI
        {
            TopicIcon = "flame",
            Title     = "Fire Mastery",
            Dilemma   = "Harness controlled combustion?",
            LearnMore = "Fire enables cooking (energy budget boost), warmth in cold biomes, and tool refinement. Anoxic-refuge lineages cannot access this path.",
            Choices   = new[] {
                new ChoiceUI { Icon = "flame", Label = "Harness fire" },
                new ChoiceUI { Icon = "ban",   Label = "Avoid fire"   },
            }
        },

        ["CommunicationCodeification"] = new EventUI
        {
            TopicIcon = "book",
            Title     = "Codify Language",
            Dilemma   = "Oral tradition or recorded symbols?",
            LearnMore = "Crystallizing language into a recorded form compounds knowledge across generations and boosts the Intelligence Index multiplier heading into Era 3.",
            Choices   = new[] {
                new ChoiceUI { Icon = "message-circle", Label = "Oral"     },
                new ChoiceUI { Icon = "book-2",         Label = "Recorded" },
            }
        },
    };

    public static bool TryGet(string geneId, out EventUI ui) => _map.TryGetValue(geneId, out ui);
}
