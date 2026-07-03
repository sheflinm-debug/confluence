using System.Collections.Generic;

/// Static lookup table: gene ID → (topic icon, short title, dilemma line, learn-more text)
/// and per-choice (icon name, short label). Matches event-prompt-icon-redesign-spec.md.
public static class GeneEventUIData
{
    public struct ChoiceUI
    {
        public string Icon;  // filename in Resources/Icons without extension
        public string Label; // 1-2 words shown on button
        public string Hint;  // terse parenthetical shown after label
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
                new ChoiceUI { Icon = "focus-2",     Label = "Acute",       Hint = "far range, narrow field" },
                new ChoiceUI { Icon = "eye-search",  Label = "Wide-field",  Hint = "panoramic, lower res"    },
            }
        },

        ["LocomotorAppendageDevelopment"] = new EventUI
        {
            TopicIcon = "walk",
            Title     = "Locomotor Specialization",
            Dilemma   = "Speed or raw muscular force?",
            LearnMore = "Fast limbs improve pursuit and escape. Powerful limbs confer combat and structural strength bonuses.",
            Choices   = new[] {
                new ChoiceUI { Icon = "bolt",   Label = "Fast",      Hint = "pursuit & escape bonus"   },
                new ChoiceUI { Icon = "weight", Label = "Powerful",  Hint = "combat & strength bonus"  },
            }
        },

        ["ReproductiveStrategyShift"] = new EventUI
        {
            TopicIcon = "dna-2",
            Title     = "Reproductive Strategy",
            Dilemma   = "Clone alone or share genes?",
            LearnMore = "Asexual reproduction is fast but accumulates mutations. Sexual reproduction blends traits across mates, creating more variety.",
            Choices   = new[] {
                new ChoiceUI { Icon = "circle-dot",      Label = "Asexual", Hint = "fast, mutation risk"    },
                new ChoiceUI { Icon = "heart-handshake", Label = "Sexual",  Hint = "trait mixing, variety"  },
            }
        },

        ["SequentialHermaphroditism"] = new EventUI
        {
            TopicIcon = "refresh",
            Title     = "Sex Flexibility",
            Dilemma   = "Switch sex to match population?",
            LearnMore = "Sequential hermaphroditism lets organisms change sex when the local ratio skews heavily, preventing mating bottlenecks.",
            Choices   = new[] {
                new ChoiceUI { Icon = "arrows-exchange", Label = "Hermaphroditic", Hint = "shifts sex with local ratio" },
                new ChoiceUI { Icon = "lock",            Label = "Fixed sex",      Hint = "stable, no switching"       },
            }
        },

        ["EfficientRespiration"] = new EventUI
        {
            TopicIcon = "wind",
            Title     = "Oxidation Crisis",
            Dilemma   = "Adapt or hide from the gas?",
            LearnMore = "Adapting yields long-term resilience and wider habitat. Retreating to anoxic refuges is safer now but permanently restricts range and speed.",
            Choices   = new[] {
                new ChoiceUI { Icon = "shield-check", Label = "Tolerate", Hint = "resilient, full habitat range" },
                new ChoiceUI { Icon = "home-shield",  Label = "Retreat",  Hint = "safe now, restricted range"   },
            }
        },

        ["PhotosynthesisEmergence"] = new EventUI
        {
            TopicIcon = "sun",
            Title     = "Photosynthesis",
            Dilemma   = "Harvest sunlight or stay chemical?",
            LearnMore = "Photosynthesis unlocks unlimited surface energy but releases gas that will reshape the atmosphere. Chemosynthesis is stable but spatially constrained.",
            Choices   = new[] {
                new ChoiceUI { Icon = "leaf",  Label = "Photosynthesize", Hint = "surface energy, reshapes atmo" },
                new ChoiceUI { Icon = "flask", Label = "Stay chemical",   Hint = "stable, spatially constrained" },
            }
        },

        ["MotilityEmergence"] = new EventUI
        {
            TopicIcon = "route",
            Title     = "Motility",
            Dilemma   = "Self-direct or drift with currents?",
            LearnMore = "Motility enables active foraging and predation. Sessile organisms spend less energy on movement and can dominate stable nutrient patches.",
            Choices   = new[] {
                new ChoiceUI { Icon = "fish",   Label = "Motile",  Hint = "active foraging, predation"    },
                new ChoiceUI { Icon = "anchor", Label = "Sessile", Hint = "low energy, stable patches"    },
            }
        },

        ["ManipulationAppendageDevelopment"] = new EventUI
        {
            TopicIcon = "walk",
            Title     = "Appendage Development",
            Dilemma   = "Grow simple manipulators?",
            LearnMore = "Simple appendages (cilia, pseudopods) add a modest manipulation tier and advance neural complexity toward nerve-net organization.",
            Choices   = new[] {
                new ChoiceUI { Icon = "hand-grab", Label = "Simple", Hint = "manipulation tier, nerve-net path" },
                new ChoiceUI { Icon = "x",         Label = "None",   Hint = "no change"                        },
            }
        },

        ["ArticulatedAppendages"] = new EventUI
        {
            TopicIcon = "walk",
            Title     = "Articulated Limbs",
            Dilemma   = "Upgrade to grasping appendages?",
            LearnMore = "Articulated limbs allow crude tool use and open the path toward dexterous manipulation in later eras.",
            Choices   = new[] {
                new ChoiceUI { Icon = "hand-grab", Label = "Articulated", Hint = "crude tool use, dex path" },
                new ChoiceUI { Icon = "anchor",    Label = "Retain",      Hint = "no change"                },
            }
        },

        ["DexterousAppendages"] = new EventUI
        {
            TopicIcon = "walk",
            Title     = "Dexterous Digits",
            Dilemma   = "Evolve precision grasping?",
            LearnMore = "Dexterous digits enable precise manipulation and are the prerequisite for Era 2 tool use.",
            Choices   = new[] {
                new ChoiceUI { Icon = "hand-grab", Label = "Dexterous", Hint = "precision grip, unlocks tools" },
                new ChoiceUI { Icon = "anchor",    Label = "Retain",    Hint = "no change"                    },
            }
        },

        ["SocialityEmergence"] = new EventUI
        {
            TopicIcon = "users-plus",
            Title     = "Social Behavior",
            Dilemma   = "Cluster or stay solitary?",
            LearnMore = "Aggregating behavior reduces individual predation risk via density-dependent defense. Solitary organisms specialize more efficiently in sparse niches.",
            Choices   = new[] {
                new ChoiceUI { Icon = "users", Label = "Aggregate", Hint = "lower predation risk"   },
                new ChoiceUI { Icon = "user",  Label = "Solitary",  Hint = "niche specialization"   },
            }
        },

        ["GroupFormation"] = new EventUI
        {
            TopicIcon = "users-group",
            Title     = "Group Formation",
            Dilemma   = "Coordinate or stay loosely clustered?",
            LearnMore = "Active group formation enables role division and coordinated foraging, key inputs for the Era 2 social architecture fork.",
            Choices   = new[] {
                new ChoiceUI { Icon = "users-group", Label = "Group",     Hint = "role division, coordinated foraging" },
                new ChoiceUI { Icon = "users",       Label = "Aggregate", Hint = "loose clustering, no coordination"   },
            }
        },

        ["Mixotrophy"] = new EventUI
        {
            TopicIcon = "scale",
            Title     = "Dual Metabolism",
            Dilemma   = "Combine photosynthesis and feeding?",
            LearnMore = "Mixotrophy provides a metabolic fallback — either energy source sustains the organism when the other is scarce, at 70% efficiency each.",
            Choices   = new[] {
                new ChoiceUI { Icon = "scale", Label = "Mixotrophic",  Hint = "dual fallback, 70% each"     },
                new ChoiceUI { Icon = "leaf",  Label = "Phototrophic", Hint = "sun only, full efficiency"   },
            }
        },

        ["GermLayerComplexity"] = new EventUI
        {
            TopicIcon = "layers-intersect",
            Title     = "Tissue Complexity",
            Dilemma   = "Develop three tissue layers?",
            LearnMore = "Triploblastic organization (three germ layers) is the prerequisite for evolving an endoskeleton — the vertebrate ancestor path.",
            Choices   = new[] {
                new ChoiceUI { Icon = "layers-linked", Label = "Three layers", Hint = "endoskeleton prereq"  },
                new ChoiceUI { Icon = "square",        Label = "Two layers",   Hint = "no change"            },
            }
        },

        ["ProtectiveStructureEmergence"] = new EventUI
        {
            TopicIcon = "shield",
            Title     = "Body Plan",
            Dilemma   = "How to answer the predation threat?",
            LearnMore = "Exoskeleton and shell provide defense at a speed cost. Endoskeleton (requires three tissue layers) unlocks the vertebrate body plan. Soft-body invests in pure evasion.",
            Choices   = new[] {
                new ChoiceUI { Icon = "shield-checkered", Label = "Exoskeleton",  Hint = "armored, speed cost"          },
                new ChoiceUI { Icon = "egg",              Label = "Shell",         Hint = "heavy defense, slow"          },
                new ChoiceUI { Icon = "bone",             Label = "Endoskeleton",  Hint = "vertebrate path (needs 3 layers)"},
                new ChoiceUI { Icon = "run",              Label = "Soft, fast",    Hint = "pure evasion, no armor"       },
            }
        },

        ["KingdomFork"] = new EventUI
        {
            TopicIcon = "git-fork",
            Title     = "Kingdom Fork",
            Dilemma   = "Produce energy or consume others?",
            LearnMore = "Becoming a Producer locks in photosynthetic autotrophy. Becoming a Consumer opens predation and scavenging — a higher-risk, higher-reward energy strategy.",
            Choices   = new[] {
                new ChoiceUI { Icon = "leaf", Label = "Producer", Hint = "photosynthetic autotroph"          },
                new ChoiceUI { Icon = "paw",  Label = "Consumer", Hint = "predation, higher risk/reward"     },
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
                new ChoiceUI { Icon = "users",           Label = "Social",       Hint = "group coordination"     },
                new ChoiceUI { Icon = "hand-grab",       Label = "Manipulative", Hint = "tool mastery"           },
                new ChoiceUI { Icon = "arrows-maximize", Label = "Scale",        Hint = "higher cultural ceiling" },
            }
        },

        ["CommunicationMedium"] = new EventUI
        {
            TopicIcon = "message-circle",
            Title     = "Communication Medium",
            Dilemma   = "How does this lineage signal?",
            LearnMore = "Medium choice gates later codification options. Vocal/Auditory is universal; Visual/Gestural requires vision; Chemical and Bioelectric are specialist paths.",
            Choices   = new[] {
                new ChoiceUI { Icon = "microphone", Label = "Vocal",       Hint = "universal, distance"         },
                new ChoiceUI { Icon = "eye",        Label = "Visual",      Hint = "requires vision"             },
                new ChoiceUI { Icon = "droplet",    Label = "Chemical",    Hint = "range-limited, specialist"   },
                new ChoiceUI { Icon = "bolt",       Label = "Bioelectric", Hint = "underwater, specialist"      },
            }
        },

        ["NicheConstructionOrientation"] = new EventUI
        {
            TopicIcon = "tools",
            Title     = "Niche Construction",
            Dilemma   = "Tools, environment, or teaching?",
            LearnMore = "Tool-Based unlocks cordage and weapon technologies. Environment-Modification favors large-scale habitat reshaping. Social Transmission builds pure cultural networks.",
            Choices   = new[] {
                new ChoiceUI { Icon = "hammer", Label = "Tool-based",   Hint = "cordage & weapons"          },
                new ChoiceUI { Icon = "home-2", Label = "Environment",  Hint = "habitat reshaping"          },
                new ChoiceUI { Icon = "school", Label = "Social-only",  Hint = "pure cultural networks"     },
            }
        },

        ["MetabolicAllocation"] = new EventUI
        {
            TopicIcon = "scale",
            Title     = "Metabolic Allocation",
            Dilemma   = "Brain or body investment?",
            LearnMore = "Brain-heavy allocation accelerates Intelligence Index growth. Somatic investment preserves physical robustness. Balanced is the neutral path.",
            Choices   = new[] {
                new ChoiceUI { Icon = "brain", Label = "Brain",    Hint = "faster intelligence growth" },
                new ChoiceUI { Icon = "scale", Label = "Balanced", Hint = "neutral path"               },
                new ChoiceUI { Icon = "run",   Label = "Body",     Hint = "physical robustness"        },
            }
        },

        ["SocialStructure"] = new EventUI
        {
            TopicIcon = "users-group",
            Title     = "Social Structure",
            Dilemma   = "How does this lineage bond?",
            LearnMore = "Multi-member troops maximize group-brain Sociality bonuses. Fission-fusion creates variance. Pair-bonded leads to structured Era 3 governance. Solitary-territorial minimizes social overhead.",
            Choices   = new[] {
                new ChoiceUI { Icon = "heart-handshake", Label = "Pair-bonded",    Hint = "structured Era 3 governance" },
                new ChoiceUI { Icon = "users",           Label = "Troop",          Hint = "max group-brain bonuses"     },
                new ChoiceUI { Icon = "arrows-split",    Label = "Fission-fusion", Hint = "high variance"               },
                new ChoiceUI { Icon = "map-pin",         Label = "Territorial",    Hint = "minimal social overhead"     },
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
                new ChoiceUI { Icon = "flame", Label = "Harness fire", Hint = "cooking, cold biomes, tools" },
                new ChoiceUI { Icon = "ban",   Label = "Avoid fire",   Hint = "no change"                   },
            }
        },

        ["CommunicationCodeification"] = new EventUI
        {
            TopicIcon = "book",
            Title     = "Codify Language",
            Dilemma   = "Oral tradition or recorded symbols?",
            LearnMore = "Crystallizing language into a recorded form compounds knowledge across generations and boosts the Intelligence Index multiplier heading into Era 3.",
            Choices   = new[] {
                new ChoiceUI { Icon = "message-circle", Label = "Oral",     Hint = "flexible, no permanence"     },
                new ChoiceUI { Icon = "book-2",         Label = "Recorded", Hint = "cross-gen knowledge, II boost"},
            }
        },

        // ── Era 3 Decision Layer (d3_*) ───────────────────────────────────────────

        ["d3_trade_policy"] = new EventUI
        {
            TopicIcon = "arrows-exchange",
            Title     = "Trade Policy",
            Dilemma   = "Open borders or protect your economy?",
            LearnMore = "Open Routes maximizes exchange_rate and trade health but exposes you to arbitrage. Embargo severs trade bonds — resilience suffers until alternative channels form.",
            Choices   = new[] {
                new ChoiceUI { Icon = "route",      Label = "Open Routes",       Hint = "max exchange, arbitrage risk"     },
                new ChoiceUI { Icon = "scale",      Label = "Balanced Tariffs",  Hint = "moderate protection"              },
                new ChoiceUI { Icon = "ban",        Label = "Embargo",           Hint = "isolationist, resilience cost"    },
            }
        },

        ["d3_kinship_policy"] = new EventUI
        {
            TopicIcon = "heart-handshake",
            Title     = "Kinship Norms",
            Dilemma   = "Tight households or broad kin coalitions?",
            LearnMore = "Nuclear units maximize internal investment. CrossLineage kinship drives inter-civ exchange rates upward and opens foreign openness.",
            Choices   = new[] {
                new ChoiceUI { Icon = "home-2",          Label = "Nuclear",       Hint = "tight unit, internal cohesion"    },
                new ChoiceUI { Icon = "users",           Label = "Extended",      Hint = "broader kin, moderate openness"   },
                new ChoiceUI { Icon = "users-group",     Label = "Clan",          Hint = "coalitions, factionalism risk"    },
                new ChoiceUI { Icon = "heart-handshake", Label = "CrossLineage",  Hint = "intermarriage, trade openness"    },
            }
        },

        ["d3_government_transition"] = new EventUI
        {
            TopicIcon = "building-bank",
            Title     = "Government Form",
            Dilemma   = "How does power concentrate or distribute?",
            LearnMore = "Monarchy/Hub/Queen: concentrated authority, fast decisions. Oligarchy/Mesh/Cluster: distributed power. Democracy: broad participation, slower. Theocracy: religious legitimacy.",
            Choices   = new[] {
                new ChoiceUI { Icon = "crown",       Label = "Monarchy",    Hint = "concentrated, fast decisions"         },
                new ChoiceUI { Icon = "network",     Label = "Oligarchy",   Hint = "power-sharing, moderate speed"        },
                new ChoiceUI { Icon = "users-group", Label = "Democracy",   Hint = "broad vote, stability bonus"          },
                new ChoiceUI { Icon = "flame",       Label = "Theocracy",   Hint = "sacred authority, religion path"      },
            }
        },

        ["d3_idea_patronage"] = new EventUI
        {
            TopicIcon = "bulb",
            Title     = "Idea Patronage",
            Dilemma   = "What does the ruling class invest in?",
            LearnMore = "Culture builds legitimacy and oral tradition. Religion upgrades belief tier. Science boosts information investment. Military raises kinetic domain.",
            Choices   = new[] {
                new ChoiceUI { Icon = "masks-theater", Label = "Culture",    Hint = "oral tradition, legitimacy"    },
                new ChoiceUI { Icon = "flame",         Label = "Religion",   Hint = "tier-3 belief unlock"          },
                new ChoiceUI { Icon = "flask",         Label = "Science",    Hint = "+info channel investment"      },
                new ChoiceUI { Icon = "sword",         Label = "Military",   Hint = "+kinetic domain"               },
            }
        },

        ["d3_war_or_diplomacy"] = new EventUI
        {
            TopicIcon = "scale",
            Title     = "War or Peace?",
            Dilemma   = "Expand through force or alliance?",
            LearnMore = "War opens the domain investment branch and boosts coercive channel. Diplomacy activates formal alliances, raises foreign openness, and can trigger the Empire event.",
            Choices   = new[] {
                new ChoiceUI { Icon = "sword",           Label = "War",       Hint = "opens domain investment, +coercive"  },
                new ChoiceUI { Icon = "heart-handshake", Label = "Diplomacy", Hint = "alliances, +openness, empire path"   },
            }
        },

        ["d3_domain_investment"] = new EventUI
        {
            TopicIcon = "shield",
            Title     = "War Domain",
            Dilemma   = "Where does doctrine focus?",
            LearnMore = "Kinetic: conventional force. Biochemical: disease and toxin tools. Informational: espionage and disinformation. Economic: sanctions and leverage. Each raises that domain's coverage score.",
            Choices   = new[] {
                new ChoiceUI { Icon = "sword",      Label = "Kinetic",       Hint = "+conventional force"         },
                new ChoiceUI { Icon = "droplet",    Label = "Biochemical",   Hint = "+plague/toxin doctrine"      },
                new ChoiceUI { Icon = "wifi",       Label = "Informational", Hint = "+espionage/disinfo"          },
                new ChoiceUI { Icon = "currency",   Label = "Economic",      Hint = "+sanctions/leverage"         },
            }
        },

        ["d3_bioweapon_option"] = new EventUI
        {
            TopicIcon = "biohazard",
            Title     = "Biochemical Weapons",
            Dilemma   = "Develop offensive capacity or restrict?",
            LearnMore = "Developing biochemical weapons gives a major domain boost but risks counter-deployment and resilience feedback. Restricting to defense avoids escalation penalties.",
            Choices   = new[] {
                new ChoiceUI { Icon = "droplet",    Label = "Develop",        Hint = "+30% biochem domain, escalation risk"  },
                new ChoiceUI { Icon = "shield-check", Label = "Defense only", Hint = "no escalation, lower domain"           },
            }
        },

        ["d3_caste_labor"] = new EventUI
        {
            TopicIcon = "users-group",
            Title     = "Labor Allocation",
            Dilemma   = "Where does the population's effort go?",
            LearnMore = "Production focus raises stockpile and economic channel. Military shifts caste/sector to soldiers. Culture focus builds legitimacy and idea patronage.",
            Choices   = new[] {
                new ChoiceUI { Icon = "hammer",      Label = "Production",  Hint = "max output, stockpile growth"       },
                new ChoiceUI { Icon = "sword",       Label = "Military",    Hint = "caste/sector to soldiers"           },
                new ChoiceUI { Icon = "masks-theater", Label = "Culture",   Hint = "legitimacy, idea investment"        },
            }
        },

        ["d3_large_initiative_1"] = new EventUI
        {
            TopicIcon = "trophy",
            Title     = "Large Initiative",
            Dilemma   = "Spend the surplus on what?",
            LearnMore = "Vaccination Drive shores up disease resilience. Trade Expansion opens new exchange routes. Monument investment raises religious legitimacy and belief tier.",
            Choices   = new[] {
                new ChoiceUI { Icon = "heart-plus",  Label = "Vaccination",  Hint = "+10% resilience"                  },
                new ChoiceUI { Icon = "route",       Label = "Trade Expand", Hint = "open routes, higher openness"     },
                new ChoiceUI { Icon = "building",    Label = "Monument",     Hint = "+religion channel investment"     },
            }
        },
    };

    public static bool TryGet(string geneId, out EventUI ui) => _map.TryGetValue(geneId, out ui);
}
