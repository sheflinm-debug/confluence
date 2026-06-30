using System.Text;
using UnityEngine;

/// Generates taxonomic-sounding but entirely made-up kingdom names (e.g. "Phleces",
/// "Hazzelard") for emergent kingdom-equivalents - per Section 1's "logic is real,
/// specifics are procedural" pillar, kingdoms aren't picked from a fixed Earth-taxonomy
/// menu (Animalia/Plantae/Fungi), they're labeled after the fact based on which major
/// gene forks a lineage took.
public static class KingdomNameGenerator
{
    private static readonly string[] Syllables =
    {
        "Phle", "Haz", "Vor", "Quin", "Sel", "Thal", "Mor", "Cra", "Lun",
        "Es", "Zar", "Nim", "Tor", "Vel", "Ash", "Bryn", "Och", "Sil", "Drax", "Kel"
    };

    private static readonly string[] Suffixes = { "ard", "ces", "ida", "ae", "on", "is", "eth", "or" };

    public static string Generate()
    {
        int syllableCount = Random.Range(2, 4);
        var sb = new StringBuilder();
        for (int i = 0; i < syllableCount; i++)
        {
            sb.Append(Syllables[Random.Range(0, Syllables.Length)].ToLowerInvariant());
        }
        sb.Append(Suffixes[Random.Range(0, Suffixes.Length)]);

        string name = sb.ToString();
        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }
}
