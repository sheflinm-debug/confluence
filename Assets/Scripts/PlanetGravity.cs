using UnityEngine;

/// Singleton MonoBehaviour that rolls and stores physically grounded planetary
/// gravity parameters. Call Init(archetype) once during world-gen before reading
/// any properties.
public class PlanetGravity : MonoBehaviour
{
    // ── Physical constants ────────────────────────────────────────────────────
    private const float G         = 6.674e-11f;   // m³ kg⁻¹ s⁻²
    private const float EarthRadM = 6.371e6f;      // m
    // kB = 1.38e-23f J/K  (reserved for Jeans escape calculations)
    private const float kB        = 1.38e-23f;

    // ── Singleton ─────────────────────────────────────────────────────────────
    public static PlanetGravity Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // ── Backing fields ────────────────────────────────────────────────────────
    private float _physicalRadiusM;
    private float _massKg;
    private float _surfaceGravityMs2;
    private float _escapeVelocityMs;
    private bool  _hasMagnetosphere;

    // ── Public properties ─────────────────────────────────────────────────────
    public float PhysicalRadiusM    => _physicalRadiusM;
    public float MassKg             => _massKg;
    public float SurfaceGravityMs2  => _surfaceGravityMs2;
    public float EscapeVelocityMs   => _escapeVelocityMs;
    public bool  HasMagnetosphere   => _hasMagnetosphere;

    // ── Initialisation ────────────────────────────────────────────────────────
    /// Roll all gravity parameters from first principles given a rock archetype.
    /// Must be called once before any property is read.
    public void Init(RockArchetypeDef archetype)
    {
        // 1. Physical radius: 0.6–1.8× Earth radii
        _physicalRadiusM = Random.Range(0.6f, 1.8f) * EarthRadM;

        // 2. Bulk density by rock archetype (kg/m³)
        float density = RollDensity(archetype.Id);

        // 3. Mass from sphere volume
        float r3 = _physicalRadiusM * _physicalRadiusM * _physicalRadiusM;
        _massKg = density * (4f / 3f) * Mathf.PI * r3;

        // 4. Surface gravity: g = GM / r²
        float r2 = _physicalRadiusM * _physicalRadiusM;
        _surfaceGravityMs2 = G * _massKg / r2;

        // 5. Escape velocity: v_esc = sqrt(2GM / r)
        _escapeVelocityMs = Mathf.Sqrt(2f * G * _massKg / _physicalRadiusM);

        // 6. Magnetosphere presence
        float magProb = archetype.Id == RockArchetypeId.OxidizedIronRich ? 0.80f : 0.45f;
        _hasMagnetosphere = Random.value < magProb;

        Debug.Log($"[Gravity] g={_surfaceGravityMs2:F2} m/s² | " +
                  $"v_esc={_escapeVelocityMs / 1000f:F1} km/s | " +
                  $"mag={_hasMagnetosphere}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static float RollDensity(RockArchetypeId id)
    {
        switch (id)
        {
            case RockArchetypeId.OxidizedIronRich:
            case RockArchetypeId.MetalRichStripped:
                return Random.Range(5200f, 6000f);

            case RockArchetypeId.MaficBasaltic:
            case RockArchetypeId.UltramaficReducing:
                return Random.Range(3800f, 4800f);

            case RockArchetypeId.FelsicGranitic:
            case RockArchetypeId.GlassyVitrified:
                return Random.Range(2700f, 3500f);

            case RockArchetypeId.CarbonRichReduced:
            case RockArchetypeId.CarbonateEvaporite:
                return Random.Range(2400f, 3200f);

            default:
                // UltramaficReducing already handled above; covers IcySilicateMix,
                // SulfideSulfurRich, and any future additions.
                return Random.Range(3000f, 5000f);
        }
    }
}
