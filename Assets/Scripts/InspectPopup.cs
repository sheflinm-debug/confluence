using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// Click-to-inspect overlay: left-click anywhere on the planet in the Game view to see
/// a small popup describing what's at that point (agent, liquid shell, atmosphere shell,
/// or terrain surface). Only one popup is shown at a time; clicking elsewhere replaces it.
/// Added by SimulationBootstrap in its onComplete callback.
public class InspectPopup : MonoBehaviour
{
    // Wired by Init().
    private AgentSpawner _agentSpawner;
    private Vector3 _planetCenter;
    private float _planetRadius;
    private float _worldDisplayRadius; // world-space radius (planet is displayed smaller than logical radius)

    // Popup state.
    private bool _visible;
    private Vector2 _screenPos;   // screen-space click position
    private string _title;
    private string _body;

    private const float PopupWidth  = 240f;
    private const float PopupHeight = 200f;
    private const float PopupOffset = 16f;  // gap between click point and popup edge
    private const float AgentOverlapRadius = 2f;

    public void Init(AgentSpawner agentSpawner, Vector3 center, float planetRadius)
    {
        _agentSpawner  = agentSpawner;
        _planetCenter  = center;
        _planetRadius  = planetRadius;
    }

    void Start()
    {
        // Derive world-space radius from the planet's SphereCollider (radius * lossyScale).
        var sc = FindAnyObjectByType<SphereCollider>();
        _worldDisplayRadius = sc != null
            ? sc.radius * sc.transform.lossyScale.x
            : _planetRadius; // fallback to logical if not found
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector2 mousePos = mouse.position.ReadValue();
        Vector2 guiPos = new Vector2(mousePos.x, Screen.height - mousePos.y);

        // Block clicks that land inside the HUD panel so tabs don't open a popup.
        if (GameHUD.IsOpenAndContains(guiPos)) return;

        // Block clicks while a gene event or atmosphere-crisis popup is on screen.
        if (GeneEvolutionManager.IsShowingPopup) return;

        // If a popup is already visible and the click lands inside it, let OnGUI
        // handle it (X button). Without this guard, Update() rebuilds the popup at
        // the new click position, moving the X button away before OnGUI can fire it.
        if (_visible && GetPopupRect().Contains(guiPos))
            return;

        Ray ray = cam.ScreenPointToRay(mousePos);

        _screenPos = mousePos;
        _screenPos.y = Screen.height - _screenPos.y; // flip to GUI coords

        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
        {
            // Ray missed all colliders — check if it passes through the atmosphere shell
            // (atmosphere mesh has no MeshCollider; detect geometrically).
            float atmRadius = _worldDisplayRadius * 1.15f;
            if (RayIntersectsSphere(ray, _planetCenter, atmRadius))
            {
                BuildAtmospherePopup();
                _visible = true;
            }
            return;
        }

        string hitName = hit.collider != null ? hit.collider.gameObject.name : string.Empty;
        string hitTag  = hit.collider != null ? hit.collider.gameObject.tag  : string.Empty;

        // Ignore hits that aren't on the planet (e.g. stray colliders from cinematic objects).
        bool hitIsOnPlanet = hitName == "Planet" || hitTag == "Planet" ||
                             Vector3.Distance(hit.point, _planetCenter) <= _worldDisplayRadius * 1.5f;
        if (!hitIsOnPlanet) { _visible = false; return; }

        // --- Atmosphere shell ---
        if (hitName == "Atmosphere" || hitTag == "Atmosphere")
        {
            BuildAtmospherePopup();
            _visible = true;
            return;
        }

        // --- Liquid shell ---
        if (hitName == "LiquidShell" || hitTag == "LiquidShell" || hitTag == "Liquid")
        {
            BuildLiquidPopup();
            _visible = true;
            return;
        }

        // --- Settlement proximity check (Era 3) — checked before agents so clicking a settlement
        // marker with organisms milling around its base still opens the settlement, not a random
        // nearby agent; a settlement claim covers a much larger, deliberate radius than an organism. ---
        if (Era3VisualManager.Instance != null)
        {
            var settlement = Era3VisualManager.Instance.FindNearestSettlementAt(hit.point);
            if (settlement != null)
            {
                BuildSettlementPopup(settlement);
                _visible = true;
                return;
            }
        }

        // --- Agent sphere-overlap check at hit point ---
        AgentController nearest = FindNearestAgentAt(hit.point);
        if (nearest != null)
        {
            BuildAgentPopup(nearest);
            _visible = true;
            return;
        }

        // --- Default: terrain or liquid surface ---
        // Liquid shell has no separate collider; check FluidDynamicsManager to distinguish.
        float liquidDepth = FluidDynamicsManager.Instance != null
            ? FluidDynamicsManager.Instance.GetLiquidDepthNearPosition(hit.point)
            : 0f;
        if (liquidDepth > 0f)
            BuildLiquidPopup();
        else
            BuildTerrainPopup(hit.point);
        _visible = true;
    }

    // -----------------------------------------------------------------------
    // Popup builders
    // -----------------------------------------------------------------------

    private void BuildAgentPopup(AgentController agent)
    {
        _title = agent.communityId == 0 ? "★ Your Organism" : "Agent";
        string kingdom = string.IsNullOrEmpty(agent.Kingdom) ? "—" : agent.Kingdom;
        string motility = agent.HasMotility ? "Self-directed" : "Passive drift";
        string genes = agent.AcquiredGenes.Count > 0
            ? string.Join(", ", agent.AcquiredGenes)
            : "none";
        _body = $"Community: {agent.communityId}  Lineage: {agent.AtmoLineage}\n" +
                $"Metabolism: {agent.Metabolism}\n" +
                $"Kingdom: {kingdom}  Motion: {motility}\n" +
                $"Age: {agent.AgeSeconds:F0}s  Stress: {agent.StressLevel:F1}\n" +
                $"Vision:{agent.visionTrait:F0} Speed:{agent.speedTrait:F0} Str:{agent.strengthTrait:F0}\n" +
                $"Genes: {genes}";
    }

    private void BuildSettlementPopup(Era3Manager.Settlement s)
    {
        var mgr = Era3Manager.Instance;
        bool mine = mgr != null && mgr.PlayerCiv != null && s.OwnerCivId == mgr.PlayerCiv.CommunityId;
        _title = mine ? "★ Your Settlement" : "Settlement";
        string pathLabel = mgr != null ? mgr.GetCivPath(s.OwnerCivId >= 0 ? s.OwnerCivId : s.FounderCivId).ToString() : "Unknown";
        string owner = s.OwnerCivId >= 0 ? $"civ {s.OwnerCivId}{(mine ? " (you)" : "")}" : "unaffiliated";
        string species = s.ContributingCommunities.Count > 1
            ? $"Multispecies ({s.ContributingCommunities.Count}: {string.Join(", ", s.ContributingCommunities)})"
            : "Single-species";
        bool underAttack = mgr != null && mgr.RecentAttackFlash.TryGetValue(s.Id, out float expiry) && Time.time < expiry;
        string status = s.IsOccupied
            ? $"OCCUPIED — conquered from civ {s.RecognizedOwnerCivId}, not yet formally recognized (shown hatched on the map)"
            : "Recognized";
        _body = $"Name: {s.Name}\n" +
                $"Type: {s.Tier}  ({pathLabel} path)\n" +
                $"Population: {s.Population:F0}\n" +
                $"Owner: {owner}\n" +
                $"Founded by: civ {s.FounderCivId}\n" +
                $"Composition: {species}\n" +
                $"Status: {status}" +
                (underAttack ? "\n⚠ UNDER ATTACK" : "");
    }

    private void BuildLiquidPopup()
    {
        float tempK = PlanetTemperature.Instance != null ? PlanetTemperature.Instance.CurrentK : 0f;
        string liquidName = FluidDynamicsManager.Instance?.CurrentLiquid?.Name ?? "Unknown liquid";
        _title = "Liquid Shell";
        _body  = $"Type: {liquidName}\n" +
                 $"Surface temp: {tempK:F0} K";
    }

    private void BuildAtmospherePopup()
    {
        _title = "Atmosphere";
        if (AtmosphereManager.Instance == null)
        {
            _body = "(no atmosphere data)";
            return;
        }
        IReadOnlyList<GasDefinition> gases = AtmosphereManager.Instance.Gases;
        var sb = new System.Text.StringBuilder();
        foreach (var g in gases)
            sb.AppendLine($"{g.Name}: {g.Fraction * 100f:F1}% ({g.Role})");
        _body = sb.Length > 0 ? sb.ToString().TrimEnd() : "(empty)";
    }

    private void BuildTerrainPopup(Vector3 worldPos)
    {
        float localTemp = ClimateManager.GetTemperature(worldPos);
        float moisture  = ClimateManager.GetMoisture(worldPos);
        // Mineral deposit + rock substrate (use plain-English names, no geology jargon).
        string composition = "Bare rock";
        if (MineralOverlayManager.Instance != null)
        {
            var (mineral, richness, rockType) = MineralOverlayManager.Instance.GetSurfaceAt(worldPos);
            string substrate = rockType switch
            {
                RockType.IgneousMafic  => "Basalt",
                RockType.IgneousFelsic => "Granite",
                RockType.Metamorphic   => "Metamorphic rock",
                RockType.Sedimentary   => "Sediment",
                _ => "Rock"
            };
            composition = mineral != null && richness > 0.05f
                ? $"{mineral.Name} deposit ({richness * 100f:F0}%) on {substrate}"
                : substrate;
        }

        // Climate zone — label without life-ecosystem names since this is abiotic terrain.
        float climateTemp = ClimateManager.GetTemperature(worldPos);
        string climateZone = climateTemp switch
        {
            float t when t < 20f  => "Polar",
            float t when t < 40f  => "Cold",
            float t when t < 60f  => "Temperate",
            float t when t < 75f  => "Warm",
            _                     => "Scorching"
        };

        // Storm overlay.
        string stormLine = string.Empty;
        if (WeatherManager.Instance != null)
        {
            float stormIntensity = WeatherManager.Instance.GetStormIntensityAt(worldPos);
            if (stormIntensity > 0.05f)
                stormLine = $"\nStorm: {stormIntensity * 100f:F0}% intensity";
        }

        _title = "Surface";
        _body  = $"Local temp: {localTemp:F0}  Moisture: {moisture:F0}\n" +
                 $"Zone: {climateZone}\n" +
                 $"Geology: {composition}" +
                 stormLine;
    }

    // -----------------------------------------------------------------------
    // GUI rendering
    // -----------------------------------------------------------------------

    void OnGUI()
    {
        if (!_visible) return;

        Rect popupRect = GetPopupRect();
        float px = popupRect.x;
        float py = popupRect.y;

        // Dark semi-transparent background.
        Color prevColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(popupRect, Texture2D.whiteTexture);
        GUI.color = prevColor;

        // Thin border.
        GUI.color = new Color(0.6f, 0.6f, 0.6f, 0.8f);
        GUI.DrawTexture(new Rect(popupRect.x,                         popupRect.y,                          popupRect.width, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(popupRect.x,                         popupRect.y + popupRect.height - 1f,  popupRect.width, 1f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(popupRect.x,                         popupRect.y,                          1f, popupRect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(popupRect.x + popupRect.width - 1f,  popupRect.y,                          1f, popupRect.height), Texture2D.whiteTexture);
        GUI.color = prevColor;

        float innerX = px + 6f;
        float innerY = py + 6f;
        float innerW = PopupWidth - 12f;

        // Title row (bold via larger font size, white).
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            fontSize  = 12,
            normal    = { textColor = new Color(1f, 0.85f, 0.4f) }
        };
        GUI.Label(new Rect(innerX, innerY, innerW, 18f), _title, titleStyle);

        // Close button.
        GUIStyle closeStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal   = { textColor = new Color(0.8f, 0.4f, 0.4f) },
            alignment = TextAnchor.UpperRight
        };
        if (GUI.Button(new Rect(px + PopupWidth - 22f, py + 4f, 18f, 18f),
            "×", new GUIStyle(GUI.skin.label)
            {
                fontSize  = 14,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(0.8f, 0.4f, 0.4f) },
                alignment = TextAnchor.MiddleCenter
            }))
        {
            _visible = false;
            return;
        }

        // Body text.
        GUIStyle bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 11,
            wordWrap  = true,
            normal    = { textColor = Color.white }
        };
        GUI.Label(new Rect(innerX, innerY + 20f, innerW, PopupHeight - 30f), _body, bodyStyle);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Rect GetPopupRect()
    {
        float px = _screenPos.x + PopupOffset;
        float py = _screenPos.y + PopupOffset;
        if (px + PopupWidth  > Screen.width)  px = _screenPos.x - PopupWidth  - PopupOffset;
        if (py + PopupHeight > Screen.height) py = _screenPos.y - PopupHeight - PopupOffset;
        return new Rect(px, py, PopupWidth, PopupHeight);
    }

    private static bool RayIntersectsSphere(Ray ray, Vector3 center, float radius)
    {
        Vector3 oc = ray.origin - center;
        float b = Vector3.Dot(oc, ray.direction);
        float c = Vector3.Dot(oc, oc) - radius * radius;
        return b * b - c >= 0f;
    }

    /// Finds the nearest AgentController whose world position is within AgentOverlapRadius
    /// of the ray's surface intersection point.
    private AgentController FindNearestAgentAt(Vector3 hitPoint)
    {
        if (_agentSpawner == null) return null;

        AgentController nearest = null;
        float nearestDist = AgentOverlapRadius;

        foreach (var agent in _agentSpawner.ActiveAgents)
        {
            if (agent == null) continue;
            float d = Vector3.Distance(hitPoint, agent.transform.position);
            if (d < nearestDist)
            {
                nearestDist = d;
                nearest = agent;
            }
        }
        return nearest;
    }
}
