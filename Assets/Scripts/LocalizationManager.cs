using System.Collections.Generic;
using UnityEngine;

/// Two-language localization (English / Simplified Chinese Mandarin).
/// Usage: LocalizationManager.L("key") → localized string in current language.
public static class LocalizationManager
{
    public enum Language { English, Chinese }

    public static Language CurrentLanguage { get; set; } = Language.English;

    // Dictionary<key, [English, Chinese]>
    private static readonly Dictionary<string, string[]> _table = new Dictionary<string, string[]>
    {
        // ── Menu ────────────────────────────────────────────────────────────────
        ["game_title"]          = new[] { "EVOSIM",              "演化模拟器"        },
        ["single_player"]       = new[] { "Single Player",       "单人游戏"          },
        ["multiplayer"]         = new[] { "Multiplayer",         "多人游戏"          },
        ["quit"]                = new[] { "Quit",                "退出"              },
        ["back"]                = new[] { "Back",                "返回"              },
        ["create_game"]         = new[] { "Create Game",         "创建游戏"          },
        ["join_game"]           = new[] { "Join Game",           "加入游戏"          },
        ["new_world"]           = new[] { "NEW WORLD",           "新世界"            },
        ["start_simulation"]    = new[] { "Start Simulation",    "开始模拟"          },
        ["start_as_host"]       = new[] { "Create & Host",       "创建并托管"        },
        ["geology_preset"]      = new[] { "Geology Preset",      "地质预设"          },
        ["sea_level"]           = new[] { "Sea Level",           "海平面"            },
        ["tectonic_activity"]   = new[] { "Tectonic Activity",   "构造活动"          },
        ["volcanism"]           = new[] { "Volcanism",           "火山活动"          },
        ["language"]            = new[] { "Language",            "语言"              },
        ["scanning"]            = new[] { "Scanning for games…", "正在搜索游戏…"      },
        ["refresh"]             = new[] { "Refresh",             "刷新"              },
        ["no_games"]            = new[] { "No games found on LAN","局域网上没有找到游戏"},
        ["join"]                = new[] { "Join",                "加入"              },
        ["host_label"]          = new[] { "Host:",               "主机:"             },
        ["players_label"]       = new[] { "Players:",            "玩家:"             },
        ["connecting"]          = new[] { "Connecting…",         "正在连接…"          },

        // ── Geology preset names ─────────────────────────────────────────────
        ["geo_continents"]      = new[] { "Continents",          "大陆"              },
        ["geo_pangea"]          = new[] { "Pangea",              "泛大陆"            },
        ["geo_islands"]         = new[] { "Islands",             "群岛"              },
        ["geo_ocean"]           = new[] { "Ocean World",         "海洋世界"          },
        ["geo_highlands"]       = new[] { "Highlands",           "高原世界"          },
        ["geo_random"]          = new[] { "Random",              "随机"              },

        // ── HUD tabs ─────────────────────────────────────────────────────────
        ["tab_global"]          = new[] { "Global",              "全球"              },
        ["tab_mine"]            = new[] { "Mine",                "我的"              },
        ["tab_ranks"]           = new[] { "Ranks",               "排行"              },
        ["tab_settings"]        = new[] { "⚙",                   "⚙"                 },

        // ── HUD sections ─────────────────────────────────────────────────────
        ["sec_population"]      = new[] { "POPULATION",          "种群"              },
        ["sec_surface"]         = new[] { "SURFACE",             "表面"              },
        ["sec_climate"]         = new[] { "CLIMATE",             "气候"              },
        ["sec_atmosphere"]      = new[] { "ATMOSPHERE",          "大气层"            },
        ["sec_my_community"]    = new[] { "MY COMMUNITY",        "我的群落"          },
        ["sec_biology"]         = new[] { "BIOLOGY",             "生物学"            },
        ["sec_era2_intel"]      = new[] { "ERA 2 — INTELLIGENCE","纪元2 — 智能"      },
        ["sec_genes"]           = new[] { "ACQUIRED GENES",      "已获基因"          },
        ["sec_traits"]          = new[] { "TRAITS",              "特征"              },
        ["sec_rankings_pop"]    = new[] { "RANKINGS — POPULATION","排行 — 种群"      },
        ["sec_rankings_str"]    = new[] { "RANKINGS — STRENGTH", "排行 — 力量"       },
        ["sec_rankings_int"]    = new[] { "RANKINGS — INTELLIGENCE","排行 — 智能"    },
        ["sec_visuals"]         = new[] { "VISUALS",             "视觉"              },
        ["sec_star"]            = new[] { "STAR SYSTEM",         "恒星系统"          },
        ["sec_debug"]           = new[] { "DEBUG",               "调试"              },
        ["sec_language"]        = new[] { "LANGUAGE",            "语言"              },

        // ── HUD labels ───────────────────────────────────────────────────────
        ["lbl_organisms"]       = new[] { "Organisms:",          "生物数:"           },
        ["lbl_species"]         = new[] { "Species:",            "物种:"             },
        ["lbl_era"]             = new[] { "Era:",                "纪元:"             },
        ["lbl_chemo"]           = new[] { "Chemo",               "化合"              },
        ["lbl_photo"]           = new[] { "Photo",               "光合"              },
        ["lbl_hetero"]          = new[] { "Hetero",              "异养"              },
        ["lbl_ocean"]           = new[] { "Ocean",               "海洋"              },
        ["lbl_rocky"]           = new[] { "Rocky",               "陆地"              },
        ["lbl_temperature"]     = new[] { "Temperature:",        "温度:"             },
        ["lbl_pressure"]        = new[] { "Pressure:",           "气压:"             },
        ["lbl_season"]          = new[] { "Season:",             "季节:"             },
        ["lbl_storms"]          = new[] { "Active storms:",      "活跃风暴:"          },
        ["lbl_in_hz"]           = new[] { "In habitable zone",   "宜居带内"          },
        ["lbl_out_hz"]          = new[] { "Outside habitable zone","宜居带外"         },
        ["lbl_star"]            = new[] { "Star:",               "恒星:"             },
        ["lbl_hz"]              = new[] { "HZ:",                 "宜居带:"           },
        ["lbl_orbit"]           = new[] { "Planet orbit:",       "行星轨道:"          },
        ["lbl_ecc_tilt"]        = new[] { "Eccentricity:",       "偏心率:"           },
        ["lbl_orbital_phase"]   = new[] { "Orbital phase:",      "轨道相位:"          },
        ["lbl_no_solar"]        = new[] { "(no solar system data)","(无恒星系统数据)" },

        // ── HUD buttons ──────────────────────────────────────────────────────
        ["btn_atmo_on"]         = new[] { "Atmosphere: ON",      "大气层: 开"         },
        ["btn_atmo_off"]        = new[] { "Atmosphere: OFF",     "大气层: 关"         },
        ["btn_markers_on"]      = new[] { "Agent markers: ON",   "标记: 开"           },
        ["btn_markers_off"]     = new[] { "Agent markers: OFF",  "标记: 关"           },
        ["btn_lock_on"]         = new[] { "[L] Planet-lock: ON", "[L] 星球锁定: 开"   },
        ["btn_lock_off"]        = new[] { "[L] Planet-lock: OFF","[L] 星球锁定: 关"   },
        ["btn_overlays_hidden"] = new[] { "Raw overlays: hidden","原始覆盖层: 隐藏"   },
        ["btn_overlays_vis"]    = new[] { "Raw overlays: visible","原始覆盖层: 显示"  },

        // ── Mine page ────────────────────────────────────────────────────────
        ["lbl_community"]       = new[] { "Community",           "群落"              },
        ["lbl_lineage"]         = new[] { "Lineage",             "谱系"              },
        ["lbl_metabolism"]      = new[] { "Metabolism",          "代谢方式"          },
        ["lbl_locomotion"]      = new[] { "Locomotion",          "运动方式"          },
        ["lbl_backbone"]        = new[] { "Backbone",            "骨架元素"          },
        ["lbl_mass"]            = new[] { "Mass",                "质量"              },
        ["lbl_energy"]          = new[] { "Energy",              "能量"              },
        ["lbl_vision"]          = new[] { "Vision",              "视觉"              },
        ["lbl_speed"]           = new[] { "Speed",               "速度"              },
        ["lbl_strength"]        = new[] { "Strength",            "力量"              },
        ["lbl_hardiness"]       = new[] { "Hardiness",           "耐受性"            },
        ["lbl_neural"]          = new[] { "Neural Complexity",   "神经复杂度"         },
        ["lbl_sociality"]       = new[] { "Sociality",           "社会性"            },
        ["lbl_comm_medium"]     = new[] { "Comm. Medium",        "通信方式"          },
        ["lbl_manipulation"]    = new[] { "Manipulation",        "操作能力"          },

        // ── Pause menu ───────────────────────────────────────────────────────
        ["pause_resume"]        = new[] { "Resume",              "继续游戏"          },
        ["pause_settings"]      = new[] { "Settings",            "设置"              },
        ["pause_save"]          = new[] { "Save",                "保存"              },
        ["pause_quit"]          = new[] { "Quit to Menu",        "退出到主菜单"       },

        // ── Main menu Settings panel ─────────────────────────────────────────
        ["settings"]            = new[] { "Settings",            "设置"              },
        ["volume_master"]       = new[] { "Master Volume",       "主音量"            },

        // ── Gene event outcome previews (px_) ─────────────────────────────────
        // Era 1: 3-tier per option (0=Favorable, 1=Marginal, 2=Unfavorable)
        // Era 2: 5-tier per option (0=far more … 4=far less)

        // PhotosynthesisEmergence — photo option
        ["px_photo_photo_0"] = new[] { "The starlight feels very warm.",     "这里的星光暖意很浓。"           },
        ["px_photo_photo_1"] = new[] { "The starlight is mild.",             "这里的星光还算温和。"           },
        ["px_photo_photo_2"] = new[] { "The starlight feels thin.",          "这里的星光感觉很弱。"           },
        // PhotosynthesisEmergence — chemo option
        ["px_photo_chemo_0"] = new[] { "The vent feels hot.",                "热泉的热量非常充沛。"           },
        ["px_photo_chemo_1"] = new[] { "The vent feels lukewarm.",           "热泉温度还算可以。"             },
        ["px_photo_chemo_2"] = new[] { "The vent's warmth is faint.",        "热泉的温度几乎感觉不到。"       },

        // EfficientRespiration — tolerate option
        ["px_resp_adapt_0"]  = new[] { "The new air feels invigorating.",    "新的气体让我们充满活力。"       },
        ["px_resp_adapt_1"]  = new[] { "The new air feels tolerable.",       "新的气体还算可以忍受。"         },
        ["px_resp_adapt_2"]  = new[] { "The new air feels thin and harsh.",  "新的气体稀薄而刺激。"           },
        // EfficientRespiration — refuge option
        ["px_resp_hide_0"]   = new[] { "The deep pockets feel safe.",        "深处的避所安全而宁静。"         },
        ["px_resp_hide_1"]   = new[] { "The deep pockets feel cramped but livable.", "深处的避所狭窄但还能生存。" },
        ["px_resp_hide_2"]   = new[] { "The deep pockets feel like a dead end.", "深处的避所感觉是死胡同。"   },

        // MotilityEmergence — motile option
        ["px_moti_move_0"]    = new[] { "There's more out there than right here.",   "远处比近处的资源丰富得多。" },
        ["px_moti_move_1"]    = new[] { "Nearby is about as good as what's far.",    "近处与远处的资源差不多。"  },
        ["px_moti_move_2"]    = new[] { "Everything worth having is within reach.",  "所需的一切近在咫尺。"      },
        // MotilityEmergence — sessile option
        ["px_moti_sessile_0"] = new[] { "This spot alone provides plenty.",          "这个地方本身就够用了。"    },
        ["px_moti_sessile_1"] = new[] { "This spot gets by.",                        "这里勉强够用。"            },
        ["px_moti_sessile_2"] = new[] { "This spot is starting to feel thin.",       "这个地方资源开始变稀少。"  },

        // GermLayerComplexity — three layers
        ["px_germ_three_0"]  = new[] { "There's a lot worth building toward.",       "值得为未来多做投入。"       },
        ["px_germ_three_1"]  = new[] { "It could go either way.",                    "两种选择都差不多。"         },
        ["px_germ_three_2"]  = new[] { "Simplicity still serves us fine.",           "现在简单点就够了。"         },
        // GermLayerComplexity — two layers
        ["px_germ_two_0"]    = new[] { "We're stretched thin as it is.",             "我们现在已经很紧张了。"     },
        ["px_germ_two_1"]    = new[] { "We're managing.",                            "勉强维持着。"               },
        ["px_germ_two_2"]    = new[] { "We have room to spare.",                     "还有余力可以投入。"         },

        // ProtectiveStructureEmergence — exoskeleton
        ["px_prot_exo_0"]    = new[] { "Something out there wants to bite.",         "外面有东西想来咬我们。"     },
        ["px_prot_exo_1"]    = new[] { "It's calm for now.",                         "目前还算平静。"             },
        ["px_prot_exo_2"]    = new[] { "Nothing's hunting here.",                    "这里没有什么在猎食我们。"   },
        // ProtectiveStructureEmergence — shell
        ["px_prot_shell_0"]  = new[] { "Slow and steady would serve us here.",       "稳扎稳打更适合这里。"       },
        ["px_prot_shell_1"]  = new[] { "Either pace would do.",                      "哪种节奏都可以。"           },
        ["px_prot_shell_2"]  = new[] { "We'd rather be quick.",                      "这里需要快一点。"           },
        // ProtectiveStructureEmergence — endoskeleton
        ["px_prot_endo_0"]   = new[] { "There's room here to grow big and strong.",  "这里有空间让我们更大更强。" },
        ["px_prot_endo_1"]   = new[] { "We could go either way.",                    "两种方式都可以。"           },
        ["px_prot_endo_2"]   = new[] { "Space is tight; bulk won't help.",           "空间有限，体型帮不了什么。" },
        // ProtectiveStructureEmergence — soft-body
        ["px_prot_soft_0"]   = new[] { "Speed would save us here.",                  "速度是这里生存的关键。"     },
        ["px_prot_soft_1"]   = new[] { "Armor or speed, either helps.",              "装甲或速度都有用。"         },
        ["px_prot_soft_2"]   = new[] { "We'd rather be tough than fast.",            "这里还是结实点好。"         },

        // ReproductiveStrategyShift — asexual
        ["px_repro_asex_0"]  = new[] { "What works, works — no need to gamble.",     "现有方式运作良好，无需冒险。" },
        ["px_repro_asex_1"]  = new[] { "It's an even trade either way.",             "差不多是种权衡。"           },
        ["px_repro_asex_2"]  = new[] { "We're falling behind as things stand.",      "我们正在慢慢落后。"         },
        // ReproductiveStrategyShift — sexual
        ["px_repro_sexual_0"]= new[] { "The world keeps changing on us.",            "这个世界一直在变化。"       },
        ["px_repro_sexual_1"]= new[] { "Things shift, but slowly.",                  "变化很慢，但确实在变。"     },
        ["px_repro_sexual_2"]= new[] { "What works today will work tomorrow.",       "今天的方式明天依然有效。"   },

        // SequentialHermaphroditism — hermaphroditic
        ["px_herm_hermap_0"] = new[] { "Partners are hard to come by out here.",     "这里很难找到伴侣。"         },
        ["px_herm_hermap_1"] = new[] { "Partners turn up now and then.",             "偶尔能找到伴侣。"           },
        ["px_herm_hermap_2"] = new[] { "Partners are everywhere we look.",           "伴侣到处都是。"             },
        // SequentialHermaphroditism — fixed sex
        ["px_herm_fixed_0"]  = new[] { "There's no shortage of company.",            "伴侣从不匮乏。"             },
        ["px_herm_fixed_1"]  = new[] { "It's manageable.",                           "还算能应付。"               },
        ["px_herm_fixed_2"]  = new[] { "We're struggling to find each other.",       "找伴侣变得越来越难。"       },

        // SensoryOrganDevelopment — acute
        ["px_sens_acute_0"]  = new[] { "Danger tends to strike from one direction.", "危险往往来自同一方向。"     },
        ["px_sens_acute_1"]  = new[] { "Threats come from all directions evenly.",   "威胁从各方向均匀涌来。"     },
        ["px_sens_acute_2"]  = new[] { "We're never sure where to look next.",       "我们无法预判威胁方向。"     },
        // SensoryOrganDevelopment — wide-field
        ["px_sens_wide_0"]   = new[] { "We're never sure where to look next.",       "我们无法预判威胁方向。"     },
        ["px_sens_wide_1"]   = new[] { "Threats come from all directions evenly.",   "威胁从各方向均匀涌来。"     },
        ["px_sens_wide_2"]   = new[] { "Danger tends to strike from one direction.", "危险往往来自同一方向。"     },

        // LocomotorAppendageDevelopment — fast
        ["px_loco_fast_0"]   = new[] { "Outrunning trouble beats fighting it.",      "跑赢麻烦比打赢更容易。"     },
        ["px_loco_fast_1"]   = new[] { "Either approach holds up.",                  "两种方式都可以。"           },
        ["px_loco_fast_2"]   = new[] { "Nothing here we can't outmuscle.",           "这里的对手我们能硬打过去。" },
        // LocomotorAppendageDevelopment — powerful
        ["px_loco_power_0"]  = new[] { "Nothing here we can't outmuscle.",           "这里的对手我们能硬打过去。" },
        ["px_loco_power_1"]  = new[] { "Either approach holds up.",                  "两种方式都可以。"           },
        ["px_loco_power_2"]  = new[] { "Outrunning trouble beats fighting it.",      "跑赢麻烦比打赢更容易。"     },

        // KingdomFork — producer
        ["px_king_prod_0"]   = new[] { "We make more than enough on our own.",       "我们自己产出的已绰绰有余。" },
        ["px_king_prod_1"]   = new[] { "We just about cover ourselves.",             "我们的产出刚好够用。"       },
        ["px_king_prod_2"]   = new[] { "Making our own isn't cutting it.",           "靠自己产出已经不够了。"     },
        // KingdomFork — consumer
        ["px_king_cons_0"]   = new[] { "There's plenty moving around to catch.",     "周围有很多可以捕食的东西。" },
        ["px_king_cons_1"]   = new[] { "Prey is around, if you work for it.",        "猎物有，但需要努力追。"     },
        ["px_king_cons_2"]   = new[] { "There's barely anything worth chasing.",     "几乎没有什么值得追的猎物。" },

        // ManipulationAppendageDevelopment — simple appendages
        ["px_mani_simple_0"] = new[] { "There's a lot here worth taking hold of.",   "这里有很多值得抓握的东西。" },
        ["px_mani_simple_1"] = new[] { "A few things here might be worth grasping.", "这里有些东西值得尝试抓握。" },
        ["px_mani_simple_2"] = new[] { "There's little here worth reaching for.",    "这里没什么值得伸手去拿的。" },
        // ManipulationAppendageDevelopment — remain without
        ["px_mani_none_0"]   = new[] { "Every bit of spare energy counts right now.", "现在每一点多余能量都很重要。" },
        ["px_mani_none_1"]   = new[] { "We can spare a little for this, if needed.", "如果需要的话还能腾出一点。" },
        ["px_mani_none_2"]   = new[] { "We have energy to spare either way.",        "我们的能量绰绰有余。"       },

        // ArticulatedAppendages — articulated
        ["px_arti_artic_0"]  = new[] { "Simple pushing and pulling isn't enough.",   "简单的推拉已经不够用了。"   },
        ["px_arti_artic_1"]  = new[] { "A basic grip mostly still gets by.",         "基础抓握大致还够。"         },
        ["px_arti_artic_2"]  = new[] { "A basic grip handles everything we need.",   "基础抓握完全能应对需求。"   },
        // ArticulatedAppendages — retain simple
        ["px_arti_simple_0"] = new[] { "What we have already does the job.",         "现有的已经够用。"           },
        ["px_arti_simple_1"] = new[] { "It could go either way.",                    "两种都差不多。"             },
        ["px_arti_simple_2"] = new[] { "What we have is falling short.",             "现有的开始跟不上了。"       },

        // DexterousAppendages — dexterous digits
        ["px_dext_dext_0"]   = new[] { "This calls for a finer touch than we have.", "这里需要比我们更精细的操作。" },
        ["px_dext_dext_1"]   = new[] { "A rough grip mostly gets the job done.",     "粗糙的抓握大致够用。"       },
        ["px_dext_dext_2"]   = new[] { "Nothing here needs more than a rough grip.", "这里用不上精细操作。"       },
        // DexterousAppendages — retain articulated
        ["px_dext_artic_0"]  = new[] { "What we have already handles this fine.",    "现有的抓握完全够用。"       },
        ["px_dext_artic_1"]  = new[] { "It could go either way.",                    "两种都差不多。"             },
        ["px_dext_artic_2"]  = new[] { "What we have is starting to hold us back.",  "现有的开始拖后腿了。"       },

        // SocialityEmergence — aggregate
        ["px_soci_agg_0"]    = new[] { "There's safety in numbers out here.",        "这里聚在一起更安全。"       },
        ["px_soci_agg_1"]    = new[] { "Threats come and go.",                       "威胁时有时无。"             },
        ["px_soci_agg_2"]    = new[] { "It's quiet and safe on our own.",            "单独行动也很安全。"         },
        // SocialityEmergence — solitary
        ["px_soci_solo_0"]   = new[] { "Sharing this space would only get in the way.", "有同伴反而会妨碍我们。" },
        ["px_soci_solo_1"]   = new[] { "Company wouldn't hurt, but we manage.",      "有没有同伴影响不大。"       },
        ["px_soci_solo_2"]   = new[] { "Being alone is starting to cost us.",        "独自行动开始让我们付出代价。" },

        // GroupFormation — group formation
        ["px_grpf_group_0"]  = new[] { "Working together could do more than alone.", "协作能做到单打独斗做不到的事。" },
        ["px_grpf_group_1"]  = new[] { "Loose clustering mostly still works.",       "松散的聚集大致还行。"       },
        ["px_grpf_group_2"]  = new[] { "Loose clustering handles everything fine.",  "松散的聚集完全够用。"       },
        // GroupFormation — retain aggregation
        ["px_grpf_agg_0"]    = new[] { "What we're doing already works well enough.", "现在的方式已经行之有效。"  },
        ["px_grpf_agg_1"]    = new[] { "It could go either way.",                    "两种都差不多。"             },
        ["px_grpf_agg_2"]    = new[] { "Sticking together loosely isn't cutting it.", "单纯聚集已经不够用了。"    },

        // Mixotrophy — mixotrophic
        ["px_mixo_mixo_0"]   = new[] { "The light here isn't something to count on.", "这里的光照不稳定，不可依赖。" },
        ["px_mixo_mixo_1"]   = new[] { "The light holds up most of the time.",        "光照大多数时候还算稳定。"  },
        ["px_mixo_mixo_2"]   = new[] { "The light here never fails us.",              "这里的光照从未让我们失望。" },
        // Mixotrophy — phototrophic
        ["px_mixo_photo_0"]  = new[] { "The light here never fails us.",              "这里的光照从未让我们失望。" },
        ["px_mixo_photo_1"]  = new[] { "The light holds up most of the time.",        "光照大多数时候还算稳定。"  },
        ["px_mixo_photo_2"]  = new[] { "The light here isn't something to count on.", "这里的光照不稳定，不可依赖。" },

        // ── Era 2 previews (5-tier) ───────────────────────────────────────────

        // CognitiveInvestmentStrategy
        ["px_e2cog_soc_0"]   = new[] { "Group coordination will pay off far more than it costs us here.",   "群体协调在这里远比代价值得。"  },
        ["px_e2cog_soc_1"]   = new[] { "Group coordination will pay off more than it costs us here.",       "群体协调在这里比代价更有意义。"},
        ["px_e2cog_soc_2"]   = new[] { "Group coordination will pay off about as much as it costs us.",     "群体协调与其代价大致相当。"    },
        ["px_e2cog_soc_3"]   = new[] { "Group coordination will pay off less than it costs us here.",       "群体协调在这里回报有限。"      },
        ["px_e2cog_soc_4"]   = new[] { "Group coordination will pay off far less than it costs us here.",   "群体协调在这里远不值其代价。"  },

        ["px_e2cog_mani_0"]  = new[] { "Precision handling will pay off far more than it costs us here.",   "精密操作在这里远比代价值得。"  },
        ["px_e2cog_mani_1"]  = new[] { "Precision handling will pay off more than it costs us here.",       "精密操作在这里比代价更有意义。"},
        ["px_e2cog_mani_2"]  = new[] { "Precision handling will pay off about as much as it costs us.",     "精密操作与其代价大致相当。"    },
        ["px_e2cog_mani_3"]  = new[] { "Precision handling will pay off less than it costs us here.",       "精密操作在这里回报有限。"      },
        ["px_e2cog_mani_4"]  = new[] { "Precision handling will pay off far less than it costs us here.",   "精密操作在这里远不值其代价。"  },

        ["px_e2cog_scal_0"]  = new[] { "Raw size will pay off far more than it costs us here.",             "单纯扩大体量在这里远比代价值得。"},
        ["px_e2cog_scal_1"]  = new[] { "Raw size will pay off more than it costs us here.",                 "单纯扩大体量在这里比代价值得。"},
        ["px_e2cog_scal_2"]  = new[] { "Raw size will pay off about as much as it costs us.",               "单纯扩大体量与其代价大致相当。"},
        ["px_e2cog_scal_3"]  = new[] { "Raw size will pay off less than it costs us here.",                 "单纯扩大体量在这里回报有限。"  },
        ["px_e2cog_scal_4"]  = new[] { "Raw size will pay off far less than it costs us here.",             "单纯扩大体量在这里远不值其代价。"},

        // CommunicationMedium
        ["px_e2comm_voc_0"]  = new[] { "Sound will carry far more effectively here than sight.",   "声音在这里传播效果远超视觉。"  },
        ["px_e2comm_voc_1"]  = new[] { "Sound will carry more effectively here than sight.",       "声音在这里比视觉传播更好。"    },
        ["px_e2comm_voc_2"]  = new[] { "Sound will carry about as effectively here as sight.",     "声音与视觉传播效果大致相当。"  },
        ["px_e2comm_voc_3"]  = new[] { "Sound will carry less effectively here than sight.",       "声音传播不如视觉有效。"        },
        ["px_e2comm_voc_4"]  = new[] { "Sound will carry far less effectively here than sight.",   "声音传播远不如视觉有效。"      },

        ["px_e2comm_vis_0"]  = new[] { "Sight will carry far more clearly here than sound.",       "视觉在这里传播效果远超声音。"  },
        ["px_e2comm_vis_1"]  = new[] { "Sight will carry more clearly here than sound.",           "视觉在这里比声音传播更清晰。"  },
        ["px_e2comm_vis_2"]  = new[] { "Sight will carry about as clearly here as sound.",         "视觉与声音传播清晰度相当。"    },
        ["px_e2comm_vis_3"]  = new[] { "Sight will carry less clearly here than sound.",           "视觉传播不如声音清晰。"        },
        ["px_e2comm_vis_4"]  = new[] { "Sight will carry far less clearly here than sound.",       "视觉传播远不如声音清晰。"      },

        ["px_e2comm_chem_0"] = new[] { "Scent will linger far more usefully here than a call.",    "气味在这里远比声音或动作更持久。"},
        ["px_e2comm_chem_1"] = new[] { "Scent will linger more usefully here than a call.",        "气味在这里比声音或动作更有用。"},
        ["px_e2comm_chem_2"] = new[] { "Scent will linger about as usefully here as a call.",      "气味与声音的作用大致相当。"    },
        ["px_e2comm_chem_3"] = new[] { "Scent will linger less usefully here than a call.",        "气味在这里不如声音或动作有用。"},
        ["px_e2comm_chem_4"] = new[] { "Scent will linger far less usefully here than a call.",    "气味在这里远不如声音或动作有用。"},

        ["px_e2comm_bio_0"]  = new[] { "A signal will show up far more clearly here than sound.",  "生物电信号在这里远比声音更清晰。"},
        ["px_e2comm_bio_1"]  = new[] { "A signal will show up more clearly here than sound.",      "生物电信号在这里比声音更清晰。"},
        ["px_e2comm_bio_2"]  = new[] { "A signal will show up about as clearly here as sound.",    "生物电信号与声音清晰度相当。"  },
        ["px_e2comm_bio_3"]  = new[] { "A signal will show up less clearly here than sound.",      "生物电信号在这里不如声音清晰。"},
        ["px_e2comm_bio_4"]  = new[] { "A signal will show up far less clearly here than sound.",  "生物电信号在这里远不如声音清晰。"},

        // NicheConstructionOrientation
        ["px_e2nic_tool_0"]  = new[] { "Tool investment will pay back far more than it costs.",    "工具投入在这里远比代价值得。"  },
        ["px_e2nic_tool_1"]  = new[] { "Tool investment will pay back more than it costs.",        "工具投入比代价更有价值。"      },
        ["px_e2nic_tool_2"]  = new[] { "Tool investment will pay back about as much as it costs.", "工具投入与代价大致相当。"      },
        ["px_e2nic_tool_3"]  = new[] { "Tool investment will pay back less than it costs.",        "工具投入回报不及代价。"        },
        ["px_e2nic_tool_4"]  = new[] { "Tool investment will pay back far less than it costs.",    "工具投入远不值其代价。"        },

        ["px_e2nic_env_0"]   = new[] { "Reshaping this place will pay back far more than it costs.", "改造环境在这里远比代价值得。"},
        ["px_e2nic_env_1"]   = new[] { "Reshaping this place will pay back more than it costs.",   "改造环境比代价更有价值。"      },
        ["px_e2nic_env_2"]   = new[] { "Reshaping this place will pay back about as much as costs.","改造环境与代价大致相当。"    },
        ["px_e2nic_env_3"]   = new[] { "Reshaping this place will pay back less than it costs.",   "改造环境回报不及代价。"        },
        ["px_e2nic_env_4"]   = new[] { "Reshaping this place will pay back far less than it costs.","改造环境远不值其代价。"       },

        ["px_e2nic_soc_0"]   = new[] { "Passing knowledge down will cost far less than building.", "传授知识远比建造便宜。"        },
        ["px_e2nic_soc_1"]   = new[] { "Passing knowledge down will cost less than building.",     "传授知识比建造便宜。"          },
        ["px_e2nic_soc_2"]   = new[] { "Passing knowledge down will cost about as much as building.","传授知识与建造成本大致相当。"},
        ["px_e2nic_soc_3"]   = new[] { "Passing knowledge down will cost more than building.",     "传授知识比建造更贵。"          },
        ["px_e2nic_soc_4"]   = new[] { "Passing knowledge down will cost far more than building.", "传授知识远比建造昂贵。"        },

        // MetabolicAllocation
        ["px_e2met_brain_0"] = new[] { "Extra thinking will earn back far more than it burns.",    "额外思维能力在这里远比消耗值得。"},
        ["px_e2met_brain_1"] = new[] { "Extra thinking will earn back more than it burns.",        "额外思维能力比消耗更有价值。"  },
        ["px_e2met_brain_2"] = new[] { "Extra thinking will earn back about as much as it burns.", "额外思维与其消耗大致相当。"    },
        ["px_e2met_brain_3"] = new[] { "Extra thinking will earn back less than it burns.",        "额外思维回报不及消耗。"        },
        ["px_e2met_brain_4"] = new[] { "Extra thinking will earn back far less than it burns.",    "额外思维远不值其消耗。"        },

        ["px_e2met_bal_0"]   = new[] { "Staying balanced will cost far less than specializing.",   "保持平衡远比专精代价低。"      },
        ["px_e2met_bal_1"]   = new[] { "Staying balanced will cost less than specializing.",       "保持平衡比专精代价低。"        },
        ["px_e2met_bal_2"]   = new[] { "Staying balanced will cost about as much as specializing.","保持平衡与专精代价大致相当。"  },
        ["px_e2met_bal_3"]   = new[] { "Staying balanced will cost more than specializing.",       "保持平衡比专精代价高。"        },
        ["px_e2met_bal_4"]   = new[] { "Staying balanced will cost far more than specializing.",   "保持平衡远比专精代价高。"      },

        ["px_e2met_body_0"]  = new[] { "Extra physical capacity will earn back far more than it burns.", "额外体能在这里远比消耗值得。"},
        ["px_e2met_body_1"]  = new[] { "Extra physical capacity will earn back more than it burns.", "额外体能比消耗更有价值。"    },
        ["px_e2met_body_2"]  = new[] { "Extra physical capacity will earn back about as much as it burns.","额外体能与其消耗大致相当。"},
        ["px_e2met_body_3"]  = new[] { "Extra physical capacity will earn back less than it burns.", "额外体能回报不及消耗。"      },
        ["px_e2met_body_4"]  = new[] { "Extra physical capacity will earn back far less than it burns.","额外体能远不值其消耗。"   },

        // SocialStructure
        ["px_e2sst_pair_0"]  = new[] { "Two of us will manage far better here than a whole troop.",  "两人在此远比整个群体效率更高。"},
        ["px_e2sst_pair_1"]  = new[] { "Two of us will manage better here than a whole troop.",      "两人在此比整个群体更好管理。" },
        ["px_e2sst_pair_2"]  = new[] { "Two of us will manage about as well here as a whole troop.", "两人与整个群体效率大致相当。" },
        ["px_e2sst_pair_3"]  = new[] { "Two of us will manage less well here than a whole troop.",   "两人在此效率不如整个群体。"   },
        ["px_e2sst_pair_4"]  = new[] { "Two of us will manage far worse here than a whole troop.",   "两人在此效率远不如整个群体。" },

        ["px_e2sst_trp_0"]   = new[] { "A troop will manage far better here than a lone pair.",      "群体在此远比单对效率更高。"   },
        ["px_e2sst_trp_1"]   = new[] { "A troop will manage better here than a lone pair.",          "群体在此比单对更好管理。"     },
        ["px_e2sst_trp_2"]   = new[] { "A troop will manage about as well here as a lone pair.",     "群体与单对效率大致相当。"     },
        ["px_e2sst_trp_3"]   = new[] { "A troop will manage less well here than a lone pair.",       "群体在此效率不如单对。"       },
        ["px_e2sst_trp_4"]   = new[] { "A troop will manage far worse here than a lone pair.",       "群体在此效率远不如单对。"     },

        ["px_e2sst_fiss_0"]  = new[] { "Staying flexible will pay off far more than committing.",    "保持灵活在此远比固定群体更有价值。"},
        ["px_e2sst_fiss_1"]  = new[] { "Staying flexible will pay off more than committing.",        "保持灵活比固定群体更有价值。" },
        ["px_e2sst_fiss_2"]  = new[] { "Staying flexible will pay off about as much as committing.", "灵活与固定群体的价值大致相当。"},
        ["px_e2sst_fiss_3"]  = new[] { "Staying flexible will pay off less than committing.",        "保持灵活不如固定群体有价值。" },
        ["px_e2sst_fiss_4"]  = new[] { "Staying flexible will pay off far less than committing.",    "保持灵活远不如固定群体有价值。"},

        ["px_e2sst_solo_0"]  = new[] { "Holding ground alone will pay off far more than grouping.",  "独自守地在此远比聚群更有价值。"},
        ["px_e2sst_solo_1"]  = new[] { "Holding ground alone will pay off more than grouping.",      "独自守地比聚群更有价值。"     },
        ["px_e2sst_solo_2"]  = new[] { "Holding ground alone will pay off about as much as grouping.","独自守地与聚群价值大致相当。"},
        ["px_e2sst_solo_3"]  = new[] { "Holding ground alone will pay off less than grouping.",      "独自守地不如聚群有价值。"     },
        ["px_e2sst_solo_4"]  = new[] { "Holding ground alone will pay off far less than grouping.",  "独自守地远不如聚群有价值。"   },

        // CommunicationCodeification
        ["px_e2cod_oral_0"]  = new[] { "Recording will cost far less than it's worth to us now.",   "记录远比其代价更值得。"        },
        ["px_e2cod_oral_1"]  = new[] { "Recording will cost less than it's worth to us now.",       "记录比其代价更值得。"          },
        ["px_e2cod_oral_2"]  = new[] { "Recording will cost about as much as it's worth.",          "记录与其代价大致相当。"        },
        ["px_e2cod_oral_3"]  = new[] { "Recording will cost more than it's worth to us now.",       "记录代价超过其价值。"          },
        ["px_e2cod_oral_4"]  = new[] { "Recording will cost far more than it's worth to us now.",   "记录代价远超其价值。"          },

        ["px_e2cod_rec_0"]   = new[] { "Keeping a record will preserve far more than word of mouth.", "留下记录远比口耳相传保留得更多。"},
        ["px_e2cod_rec_1"]   = new[] { "Keeping a record will preserve more than word of mouth.",   "留下记录比口耳相传保留得更多。"},
        ["px_e2cod_rec_2"]   = new[] { "Keeping a record will preserve about as much as word of mouth.","留下记录与口耳相传保留量相当。"},
        ["px_e2cod_rec_3"]   = new[] { "Keeping a record will preserve less than word of mouth.",   "留下记录比口耳相传保留得更少。"},
        ["px_e2cod_rec_4"]   = new[] { "Keeping a record will preserve far less than word of mouth.", "留下记录远比口耳相传保留得少。"},

        // GlidingAdaptation
        ["px_e2gli_glide_0"] = new[] { "Catching the air will save far more energy than walking.",  "借助气流远比行走节省更多能量。"},
        ["px_e2gli_glide_1"] = new[] { "Catching the air will save more energy than walking.",      "借助气流比行走节省更多能量。" },
        ["px_e2gli_glide_2"] = new[] { "Catching the air will save about as much energy as walking.","借助气流与行走消耗大致相当。" },
        ["px_e2gli_glide_3"] = new[] { "Catching the air will save less energy than walking.",      "借助气流比行走节省能量更少。" },
        ["px_e2gli_glide_4"] = new[] { "Catching the air will save far less energy than walking.",  "借助气流远比行走节省能量更少。"},

        ["px_e2gli_ground_0"]= new[] { "Staying grounded will cost far less than any aerial path.", "留在地面远比任何空中路径代价低。"},
        ["px_e2gli_ground_1"]= new[] { "Staying grounded will cost less than any aerial path.",     "留在地面比空中路径代价低。"   },
        ["px_e2gli_ground_2"]= new[] { "Staying grounded will cost about as much as any aerial path.","留在地面与空中路径代价相当。"},
        ["px_e2gli_ground_3"]= new[] { "Staying grounded will cost more than any aerial path.",     "留在地面比空中路径代价高。"   },
        ["px_e2gli_ground_4"]= new[] { "Staying grounded will cost far more than any aerial path.", "留在地面远比空中路径代价高。" },

        // AerialLocomotion
        ["px_e2aer_fly_0"]   = new[] { "Taking to the air will open up far more than the muscle will cost.", "翱翔天空的收益远超肌肉消耗。"},
        ["px_e2aer_fly_1"]   = new[] { "Taking to the air will open up more than the muscle will cost.",     "翱翔天空的收益超过肌肉消耗。"},
        ["px_e2aer_fly_2"]   = new[] { "Taking to the air will open about as much as the muscle will cost.", "翱翔天空收益与肌肉消耗大致相当。"},
        ["px_e2aer_fly_3"]   = new[] { "Taking to the air will open up less than the muscle will cost.",     "翱翔天空收益不及肌肉消耗。" },
        ["px_e2aer_fly_4"]   = new[] { "Flight would burn far more than it would ever earn back here.",      "飞行消耗远超在这里能获得的收益。"},

        ["px_e2aer_ground_0"]= new[] { "Staying grounded will cost far less than any aerial path.", "留在地面远比任何空中路径代价低。"},
        ["px_e2aer_ground_1"]= new[] { "Staying grounded will cost less than any aerial path.",     "留在地面比空中路径代价低。"   },
        ["px_e2aer_ground_2"]= new[] { "Staying grounded will cost about as much as any aerial path.","留在地面与空中路径代价相当。"},
        ["px_e2aer_ground_3"]= new[] { "Staying grounded will cost more than any aerial path.",     "留在地面比空中路径代价高。"   },
        ["px_e2aer_ground_4"]= new[] { "Staying grounded will cost far more than any aerial path.", "留在地面远比空中路径代价高。" },
    };

    /// Returns the localized string for the given key. Falls back to the key itself if missing.
    public static string L(string key)
    {
        if (_table.TryGetValue(key, out var pair))
            return pair[(int)CurrentLanguage];
        Debug.LogWarning($"[Loc] Missing key: {key}");
        return key;
    }

    /// Returns the localized name of the given geology preset.
    public static string GeologyName(GeologyPreset preset) => preset switch
    {
        GeologyPreset.Continents => L("geo_continents"),
        GeologyPreset.Pangea     => L("geo_pangea"),
        GeologyPreset.Islands    => L("geo_islands"),
        GeologyPreset.OceanWorld => L("geo_ocean"),
        GeologyPreset.Highlands  => L("geo_highlands"),
        GeologyPreset.Random     => L("geo_random"),
        _                        => preset.ToString(),
    };

    public static string[] TabLabels() => new[]
    {
        L("tab_global"), L("tab_mine"), L("tab_ranks"), L("tab_settings"),
    };
}
