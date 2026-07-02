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
