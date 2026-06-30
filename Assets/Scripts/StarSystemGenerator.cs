using System.Collections.Generic;
using UnityEngine;

public class StarClassDef
{
    public string Name;     // spectral class letter
    public int Weight;
    public float MinSolarLuminosity, MaxSolarLuminosity;
    public float MinSurfaceK, MaxSurfaceK;
    public Color Color;
}

public class StarDef
{
    public string SpectralClass;
    public float LuminositySolar;
    public float SurfaceK;
    public Color Color;
}

public enum DecorativePlanetKind { Rocky, GasGiant, IceGiant }

/// A purely-decorative moon orbiting a planet (life-planet or one of the OtherPlanets).
/// Fields are named to match the generalization path TidalForceManager's header comment
/// already anticipated (RelativeMass/OrbitDistance/OrbitalPeriod) so a future tidal-forces
/// pass can consume this directly without renaming anything. Not simulated physically -
/// orbit is a circle (or, for the life-planet's moons, optionally an ellipse) walked at a
/// fixed angular rate by SolarSystemRuntime once gameplay begins.
public class MoonDef
{
    public float RelativeSize;       // radius relative to the parent planet's RENDERED radius (not true-to-scale)
    public float RelativeMass;       // arbitrary 0-1ish units, for a future tidal-forces pass (mass ~ size^3 * density roll)
    public float OrbitDistance;      // world-units from parent CENTER at gameplay scale (see SolarSystemRuntime.MoonOrbitWorldUnits)
    public float OrbitalPeriodSeconds; // real-time seconds for one full revolution once gameplay begins (visual pacing, not physical)
    public float StartAngleRad;      // randomized initial phase so multiple moons don't line up
    public Color Color;
}

public class DecorativePlanetDef
{
    public DecorativePlanetKind Kind;
    public float OrbitAU;
    public float RelativeSize;
    public Color Color;
    public List<MoonDef> Moons = new List<MoonDef>();
}

public class SolarSystemDef
{
    public StarDef Star;
    public float LifePlanetOrbitAU; // semi-major axis (a) of the life planet's orbit
    public float LifePlanetEccentricity; // 0 = circular, up to ~0.35 for habitable-zone plausibility
    public float LifePlanetAxialTiltDeg; // 0-35 degrees, drives seasonal per-latitude exposure
    public List<DecorativePlanetDef> OtherPlanets;
    public List<MoonDef> LifePlanetMoons = new List<MoonDef>();

    /// Standard sqrt(L)-scaling habitable-zone approximation (conservative-ish bounds,
    /// per Kasting et al.-style estimates commonly quoted as HZ_inner ~ sqrt(L/1.1),
    /// HZ_outer ~ sqrt(L/0.53) in solar units): inner edge runaway-greenhouse-ish, outer
    /// edge maximum-greenhouse-ish. These constants are an approximation, not a precise
    /// climate model - good enough for a diagnostic readout of "is the rolled orbit
    /// plausibly in the zone," which is all this is used for.
    public float HabitableZoneInnerAU => Mathf.Sqrt(Star.LuminositySolar / 1.1f);
    public float HabitableZoneOuterAU => Mathf.Sqrt(Star.LuminositySolar / 0.53f);
    public bool LifePlanetInHabitableZone => LifePlanetOrbitAU >= HabitableZoneInnerAU && LifePlanetOrbitAU <= HabitableZoneOuterAU;
}

/// Rolls a host star and a rough solar system layout. The life-bearing planet's orbital
/// distance is back-solved from the ALREADY-rolled PlanetTemperature band (so the star
/// and orbit are consistent with the atmosphere/temperature generation that already
/// happened, rather than fighting it) via the standard equilibrium-temperature
/// approximation: Teq (K) = 278 * L^0.25 / sqrt(d_AU), assuming ~Earth-like albedo.
/// Other planets are purely decorative background dressing for the genesis cinematic -
/// positioned at roughly realistic (compressed-for-visibility) spacing, never simulated.
public static class StarSystemGenerator
{
    private static readonly StarClassDef[] _classes =
    {
        new StarClassDef { Name = "M", Weight = 40, MinSolarLuminosity = 0.0001f, MaxSolarLuminosity = 0.08f, MinSurfaceK = 2400, MaxSurfaceK = 3700, Color = new Color(1f, 0.55f, 0.35f) },
        new StarClassDef { Name = "K", Weight = 22, MinSolarLuminosity = 0.08f, MaxSolarLuminosity = 0.6f, MinSurfaceK = 3700, MaxSurfaceK = 5200, Color = new Color(1f, 0.75f, 0.5f) },
        new StarClassDef { Name = "G", Weight = 15, MinSolarLuminosity = 0.6f, MaxSolarLuminosity = 1.5f, MinSurfaceK = 5200, MaxSurfaceK = 6000, Color = new Color(1f, 0.95f, 0.8f) },
        new StarClassDef { Name = "F", Weight = 12, MinSolarLuminosity = 1.5f, MaxSolarLuminosity = 5f, MinSurfaceK = 6000, MaxSurfaceK = 7500, Color = new Color(1f, 1f, 0.95f) },
        new StarClassDef { Name = "A", Weight = 7, MinSolarLuminosity = 5f, MaxSolarLuminosity = 25f, MinSurfaceK = 7500, MaxSurfaceK = 10000, Color = new Color(0.85f, 0.9f, 1f) },
        new StarClassDef { Name = "B", Weight = 3, MinSolarLuminosity = 25f, MaxSolarLuminosity = 500f, MinSurfaceK = 10000, MaxSurfaceK = 30000, Color = new Color(0.7f, 0.8f, 1f) },
        new StarClassDef { Name = "O", Weight = 1, MinSolarLuminosity = 500f, MaxSolarLuminosity = 5000f, MinSurfaceK = 30000, MaxSurfaceK = 50000, Color = new Color(0.6f, 0.7f, 1f) },
    };

    public static SolarSystemDef Generate(float targetEquilibriumK)
    {
        StarClassDef cls = RollClass();
        var star = new StarDef
        {
            SpectralClass = cls.Name,
            LuminositySolar = Random.Range(cls.MinSolarLuminosity, cls.MaxSolarLuminosity),
            SurfaceK = Random.Range(cls.MinSurfaceK, cls.MaxSurfaceK),
            Color = cls.Color,
        };

        // Teq = 278 * L^0.25 / sqrt(d)  =>  d = (278 * L^0.25 / Teq)^2
        float lQuarter = Mathf.Pow(star.LuminositySolar, 0.25f);
        float orbitAU = Mathf.Pow(278f * lQuarter / Mathf.Max(targetEquilibriumK, 1f), 2f);
        orbitAU = Mathf.Clamp(orbitAU, 0.03f, 60f);

        var others = new List<DecorativePlanetDef>();
        int otherCount = Random.Range(3, 8);
        float spacing = orbitAU; // roughly geometric spacing around the life planet's orbit
        for (int i = 0; i < otherCount; i++)
        {
            int slot = i - otherCount / 2;
            if (slot == 0) slot = otherCount; // never collide with the life planet's own slot
            float d = orbitAU * Mathf.Pow(1.5f, slot);
            d = Mathf.Max(0.02f, d);

            bool isGiant = d > orbitAU * 2.5f && Random.value < 0.6f;
            var planet = new DecorativePlanetDef
            {
                Kind = !isGiant ? DecorativePlanetKind.Rocky : (Random.value < 0.5f ? DecorativePlanetKind.GasGiant : DecorativePlanetKind.IceGiant),
                OrbitAU = d,
                RelativeSize = isGiant ? Random.Range(2.5f, 5f) : Random.Range(0.4f, 1.2f),
                Color = !isGiant
                    ? new Color(Random.Range(0.4f, 0.8f), Random.Range(0.35f, 0.7f), Random.Range(0.3f, 0.6f))
                    : Color.Lerp(new Color(0.7f, 0.55f, 0.35f), new Color(0.5f, 0.7f, 0.85f), Random.value),
            };
            // Gas/ice giants roll more moons on average (real solar system precedent -
            // Jupiter/Saturn vs. Mercury/Venus), and bigger moons in absolute terms.
            int moonCount = isGiant ? Random.Range(0, 4) : (Random.value < 0.5f ? Random.Range(0, 2) : 0);
            planet.Moons = RollMoons(moonCount, planet.RelativeSize);
            others.Add(planet);
        }

        float eccentricity = Random.Range(0f, 0.35f);
        float axialTiltDeg = Random.Range(0f, 35f);

        // The life-planet gets 0-3 moons too (e.g. an Earth-Moon-style single satellite is
        // the most common real-world outcome for a rocky habitable-zone world, so 0-1 is
        // weighted heavier than 2-3 via two independent rolls rather than a flat range).
        int lifeMoonCount = Random.value < 0.55f ? (Random.value < 0.7f ? 1 : 0) : Random.Range(2, 4);
        List<MoonDef> lifeMoons = RollMoons(lifeMoonCount, 1f);

        return new SolarSystemDef
        {
            Star = star,
            LifePlanetOrbitAU = orbitAU,
            LifePlanetEccentricity = eccentricity,
            LifePlanetAxialTiltDeg = axialTiltDeg,
            OtherPlanets = others,
            LifePlanetMoons = lifeMoons,
        };
    }

    /// Rolls `count` moons for a planet of the given RelativeSize (planet's own
    /// RelativeSize unit - 1f for the life-planet since its "relative size" is always its
    /// own full radius). Moon sizes/distances are scaled off the parent so a giant's moons
    /// read as proportionally bigger/farther than a small rocky world's, without needing
    /// true astronomical mass ratios (which would be visually illegible at any single
    /// compression scale - see SolarSystemRuntime's header comment for the full scale doc).
    private static List<MoonDef> RollMoons(int count, float parentRelativeSize)
    {
        var moons = new List<MoonDef>();
        for (int i = 0; i < count; i++)
        {
            float relSize = Random.Range(0.06f, 0.28f) * Mathf.Sqrt(parentRelativeSize);
            moons.Add(new MoonDef
            {
                RelativeSize = relSize,
                RelativeMass = Mathf.Pow(relSize, 3f) * Random.Range(0.6f, 1.4f), // mass ~ size^3 * density roll
                // Spread successive moons out so they don't overlap/collide visually -
                // each one orbits further out than the last, in parent-radius multiples.
                OrbitDistance = 2.2f + i * 1.4f + Random.Range(0f, 0.6f),
                OrbitalPeriodSeconds = Random.Range(20f, 70f) + i * 15f, // outer moons orbit slower, same as Kepler's 3rd law in spirit
                StartAngleRad = Random.Range(0f, Mathf.PI * 2f),
                Color = Color.Lerp(new Color(0.55f, 0.55f, 0.58f), new Color(0.85f, 0.82f, 0.78f), Random.value),
            });
        }
        return moons;
    }

    private static StarClassDef RollClass()
    {
        int total = 0;
        foreach (var c in _classes) total += c.Weight;
        int pick = Random.Range(0, total);
        foreach (var c in _classes)
        {
            if (pick < c.Weight) return c;
            pick -= c.Weight;
        }
        return _classes[0];
    }
}
