using System.Collections.Generic;
using UnityEngine;

/// Renders the planet's atmosphere as a translucent shell slightly larger than the
/// terrain sphere, colored/opacity per atmosphere_generator_spec.docx Section 7
/// (alpha-composited gas/haze layers, render verdict scaled by rolled pressure).
/// "Translucent unless specified otherwise" - types without an explicit Opaque/
/// Transparent verdict fall back to Translucent in MapVerdictToAlpha. A toggle
/// button in the top-right corner of the screen shows/hides the shell.
public class AtmosphereVisual : MonoBehaviour
{
    public bool Visible { get; private set; } = true;

    private MeshRenderer _renderer;
    private static readonly int ColorId = Shader.PropertyToID("_BaseColor");

    public void Build(float planetRadius, Vector3 planetCenter, AtmosphereTypeDef type, float pressureBar, Transform parent)
    {
        gameObject.name = "Atmosphere";
        transform.SetParent(parent);
        transform.position = planetCenter;

        IcosphereGenerator.Build(2, out List<Vector3> unitVerts, out List<int> triangles);

        (Color baseColor, float alpha) = ComputeCompositeColor(type, pressureBar);

        var verts = new Vector3[unitVerts.Count];
        var colors = new Color[unitVerts.Count];
        // Real atmospheres are proportionally razor-thin (~1.016x Earth's radius), but
        // at that ratio the shell sits BELOW single-celled-organism height once agents
        // are scaled down to a believable size - 1.15x gives clear visible altitude
        // instead of creatures poking out the top of the sky.
        float shellRadius = planetRadius * 1.15f;
        for (int i = 0; i < unitVerts.Count; i++)
        {
            verts[i] = unitVerts[i] * shellRadius;
            colors[i] = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }

        var mesh = new Mesh();
        mesh.indexFormat = verts.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(verts);
        mesh.SetTriangles(triangles, 0);
        mesh.SetColors(colors);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        MeshFilter filter = gameObject.AddComponent<MeshFilter>();
        filter.mesh = mesh;

        _renderer = gameObject.AddComponent<MeshRenderer>();
        Shader shader = Shader.Find("Custom/VertexColorTransparentURP");
        _renderer.material = new Material(shader);

        Debug.Log($"[Atmosphere] Verdict render: color={baseColor} alpha={alpha:F2} pressure={pressureBar:F4} bar");
    }

    /// Section 7 compositing: alpha-over each gas's (color, fraction-scaled alpha),
    /// then any synthetic haze layer for the type, then clamp to the type's render
    /// verdict band (scaled by how the rolled pressure compares to the type's max).
    private (Color color, float alpha) ComputeCompositeColor(AtmosphereTypeDef type, float pressureBar)
    {
        Color accum = Color.black;
        float accumAlpha = 0f;

        if (AtmosphereManager.Instance != null)
        {
            foreach (var gas in AtmosphereManager.Instance.Gases)
            {
                (Color color, float baseAlpha) = AtmosphereColorTable.Get(gas.Name);
                float layerAlpha = Mathf.Clamp01(baseAlpha * Mathf.Clamp01(gas.Fraction * 4f));
                accum = color * layerAlpha + accum * (1f - layerAlpha);
                accumAlpha = layerAlpha + accumAlpha * (1f - layerAlpha);
            }
        }

        var haze = AtmosphereColorTable.SyntheticHaze(type, pressureBar);
        if (haze.HasValue)
        {
            (Color color, float layerAlpha) = haze.Value;
            accum = color * layerAlpha + accum * (1f - layerAlpha);
            accumAlpha = layerAlpha + accumAlpha * (1f - layerAlpha);
        }

        if (accumAlpha < 0.01f)
            accum = new Color(0.65f, 0.78f, 0.95f); // Rayleigh-scatter default for an otherwise colorless sky

        AtmosphereRenderVerdict verdict = type.RenderVerdict;
        if (verdict == AtmosphereRenderVerdict.BranchOnPressure)
            verdict = pressureBar > type.OpaquePressureThreshold ? AtmosphereRenderVerdict.Opaque : AtmosphereRenderVerdict.Translucent;

        float verdictCap = verdict switch
        {
            AtmosphereRenderVerdict.Transparent => 0.10f,
            AtmosphereRenderVerdict.Opaque      => 0.50f, // was 0.85f — terrain must stay visible
            _ => 0.32f, // Translucent - also the default per "translucent unless specified otherwise"
        };

        float pressureScale = Mathf.Clamp(pressureBar / Mathf.Max(0.01f, type.PressureMaxBar * 0.3f), 0.4f, 1.6f);
        float finalAlpha = Mathf.Clamp01(Mathf.Max(accumAlpha, verdictCap * 0.5f) * pressureScale);
        finalAlpha = Mathf.Min(finalAlpha, verdictCap * 1.2f);

        return (accum, finalAlpha);
    }

    public void SetVisible(bool visible)
    {
        Visible = visible;
        if (_renderer != null) _renderer.enabled = visible;
    }

    void OnGUI()
    {
        if (GameHUD.SuppressRawOverlays) return;
        float w = 130f, h = 26f;
        Rect r = new Rect(Screen.width - w - 10f, 10f, w, h);
        string label = Visible ? "Atmosphere: ON" : "Atmosphere: OFF";
        if (GUI.Button(r, label))
            SetVisible(!Visible);
    }
}
